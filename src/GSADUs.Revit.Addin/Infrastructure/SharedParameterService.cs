using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GSADUs.Revit.Addin
{
    internal static class SharedParameterService
    {
        public static string EnsureYesNoInstanceParameter(Document doc, AppSettings settings, string name, ref string? storedGuid, IEnumerable<Category>? categories = null)
        {
            if (doc == null) return "No active document.";
            if (string.IsNullOrWhiteSpace(name)) name = "CurrentSet";

            Definition? defFound = null;
            if (!string.IsNullOrWhiteSpace(storedGuid) && Guid.TryParse(storedGuid, out var g))
            {
                var spe = SharedParameterElement.Lookup(doc, g);
                if (spe != null) defFound = spe.GetDefinition();
            }
            if (defFound == null)
            {
                var map = doc.ParameterBindings;
                var it = map.ForwardIterator();
                while (it.MoveNext())
                {
                    var def = it.Key as Definition;
                    if (def != null && string.Equals(def.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        defFound = def; break;
                    }
                }
            }

            // If not found, use the team/shared SP file when available and writable, else fall back to local temp
            string statusPrefix;
            if (defFound == null)
            {
                var app = doc.Application;
                var originalPath = SafeGetSharedParamsPath(app);
                var effectivePath = ResolveEffectiveSharedParamsPath(app, settings, originalPath);

                bool created = false;
                try
                {
                    // Temporarily point Revit to the effective path
                    if (!string.Equals(app.SharedParametersFilename, effectivePath, StringComparison.OrdinalIgnoreCase))
                        app.SharedParametersFilename = effectivePath;

                    EnsureFileExists(effectivePath);

                    var spFile = app.OpenSharedParameterFile();
                    if (spFile == null)
                        return $"Shared parameter file not available: {effectivePath}";

                    var group = spFile.Groups.get_Item("GSADUs") ?? spFile.Groups.Create("GSADUs");
                    var opts = new ExternalDefinitionCreationOptions(name, SpecTypeId.Boolean.YesNo) { Visible = true };
                    var defNew = group.Definitions.get_Item(name) ?? group.Definitions.Create(opts);
                    defFound = defNew as Definition;
                    if (defNew is ExternalDefinition ex)
                    {
                        storedGuid = ex.GUID.ToString("D");
                    }
                    created = true;
                }
                catch
                {
                    // Fall back to local file (read-only or inaccessible shared file)
                    var localPath = GetLocalFallbackSharedParametersPath(settings);
                    try
                    {
                        if (!string.Equals(app.SharedParametersFilename, localPath, StringComparison.OrdinalIgnoreCase))
                            app.SharedParametersFilename = localPath;
                        EnsureFileExists(localPath);
                        var spFile = app.OpenSharedParameterFile();
                        var group = spFile.Groups.get_Item("GSADUs") ?? spFile.Groups.Create("GSADUs");
                        var opts = new ExternalDefinitionCreationOptions(name, SpecTypeId.Boolean.YesNo) { Visible = true };
                        var defNew = group.Definitions.get_Item(name) ?? group.Definitions.Create(opts);
                        defFound = defNew as Definition;
                        if (defNew is ExternalDefinition ex)
                        {
                            storedGuid = ex.GUID.ToString("D");
                        }
                        effectivePath = localPath;
                        created = true;
                    }
                    catch { return $"Failed to create/find shared parameter '{name}'."; }
                    finally
                    {
                        // Restore original path if there was one
                        if (!string.IsNullOrEmpty(originalPath)) app.SharedParametersFilename = originalPath;
                    }

                    statusPrefix = $"Used fallback SP file: {effectivePath}. ";
                }
                finally
                {
                    // Restore original path if we changed it (success path)
                    if (!string.IsNullOrEmpty(originalPath)) app.SharedParametersFilename = originalPath;
                }

                statusPrefix = created ? $"SP file: {effectivePath}. " : string.Empty;
            }
            else
            {
                statusPrefix = "SP def exists. ";
            }

            if (defFound == null)
                return statusPrefix + $"Failed to create/find shared parameter '{name}'.";

            var catSet = BuildCategorySet(doc, categories);
            using (var t = new Transaction(doc, $"Bind '{name}'"))
            {
                t.Start();
                var binding = doc.Application.Create.NewInstanceBinding(catSet);
                var map = (BindingMap)doc.ParameterBindings;
                // Use new API GroupTypeId for parameter group in Revit 2026+
                if (!map.Insert(defFound, binding, GroupTypeId.IdentityData))
                {
                    map.ReInsert(defFound, binding, GroupTypeId.IdentityData);
                }
                t.Commit();
            }

            return statusPrefix + $"'{name}' is present and bound to {catSet.Size} categories.";
        }

        private static string ResolveEffectiveSharedParamsPath(Autodesk.Revit.ApplicationServices.Application app, AppSettings settings, string originalPath)
        {
            // Prefer settings path if valid; else prefer the app's current path; else local fallback
            var fromSettings = settings?.SharedParametersFilePath;
            if (!string.IsNullOrWhiteSpace(fromSettings) && File.Exists(fromSettings)) return fromSettings!;
            if (!string.IsNullOrWhiteSpace(originalPath) && File.Exists(originalPath)) return originalPath;
            return GetLocalFallbackSharedParametersPath(settings);
        }

        private static string SafeGetSharedParamsPath(Autodesk.Revit.ApplicationServices.Application app)
        {
            try { return app.SharedParametersFilename ?? string.Empty; } catch { return string.Empty; }
        }

        private static void EnsureFileExists(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (!File.Exists(path)) File.WriteAllText(path, "# GSADUs Shared Params\n");
            }
            catch { }
        }

        private static string GetLocalFallbackSharedParametersPath(AppSettings settings)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GSADUs", "Revit", "Addin");
            Directory.CreateDirectory(dir);
            var path = settings?.SharedParametersFilePath;
            if (string.IsNullOrWhiteSpace(path)) path = Path.Combine(dir, "GSADUs.SharedParameters.txt");
            return path!;
        }

        private static CategorySet BuildCategorySet(Document doc, IEnumerable<Category>? categories)
        {
            var catSet = doc.Application.Create.NewCategorySet();
            IEnumerable<Category> cats = categories ?? doc.Settings.Categories.Cast<Category>()
                .Where(c => c != null && c.AllowsBoundParameters);
            foreach (var c in cats)
                try { catSet.Insert(c); } catch { }
            return catSet;
        }
    }
}
