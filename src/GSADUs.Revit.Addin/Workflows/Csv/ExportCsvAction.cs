using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GSADUs.Revit.Addin.Workflows.Csv;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace GSADUs.Revit.Addin.Workflows.Csv
{
    internal sealed class ExportCsvAction : IExportAction
    {
        public string Id => "export-csv";
        public int Order => 500;
        public bool RequiresExternalClone => false;

        public bool IsEnabled(AppSettings app, BatchExportSettings request)
        {
            try { return request.ActionIds?.Any(a => string.Equals(a, Id, StringComparison.OrdinalIgnoreCase)) == true; } catch { return false; }
        }

        public void Execute(UIApplication uiapp, Document sourceDoc, Document? outDoc, string setName, IList<string> preserveUids, bool isDryRun)
        {
            if (uiapp == null || sourceDoc == null) return;
            var settings = AppSettingsStore.Load();
            var selectedIds = new HashSet<string>(settings.SelectedWorkflowIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var workflows = (settings.Workflows ?? new List<WorkflowDefinition>())
                .Where(w => w.Output == OutputType.Csv && selectedIds.Contains(w.Id) && (w.ActionIds?.Any(a => string.Equals(a, Id, StringComparison.OrdinalIgnoreCase)) ?? false))
                .ToList();
            if (workflows.Count == 0) return;

            var outputDir = AppSettingsStore.GetEffectiveOutputDir(settings);
            try { Directory.CreateDirectory(outputDir); } catch { return; }

            var modelName = Path.GetFileNameWithoutExtension(sourceDoc.PathName) ?? "Model";

            foreach (var wf in workflows)
            {
                var p = wf.Parameters ?? new Dictionary<string, JsonElement>();
                string Gs(string k)
                {
                    try
                    {
                        if (p.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString() ?? string.Empty;
                    }
                    catch { }
                    return string.Empty;
                }
                List<string> Gsa(string k)
                {
                    try
                    {
                        if (p.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.Array)
                        {
                            var list = new List<string>();
                            foreach (var je in v.EnumerateArray())
                            {
                                if (je.ValueKind == JsonValueKind.String)
                                    list.Add(je.GetString() ?? string.Empty);
                                else if (je.ValueKind == JsonValueKind.Number)
                                    list.Add(je.ToString());
                            }
                            return list;
                        }
                    }
                    catch { }
                    return new List<string>();
                }

                var scheduleIds = Gsa(CsvWorkflowKeys.scheduleIds);
                var pattern = Gs(CsvWorkflowKeys.fileNamePattern);
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    pattern = string.Equals(wf.Scope, "EntireProject", StringComparison.OrdinalIgnoreCase)
                        ? "{FileName} {ViewName}"
                        : "{SetName} {ViewName}";
                }

                if (scheduleIds.Count == 0) continue; // nothing to export

                foreach (var idStr in scheduleIds)
                {
                    if (!int.TryParse(idStr, out var idInt)) continue;
                    ViewSchedule? vs = null;
                    try { vs = sourceDoc.GetElement(new ElementId(idInt)) as ViewSchedule; } catch { vs = null; }
                    if (vs == null) continue; // ignore silently

                    var viewName = vs.Name ?? string.Empty;
                    var baseName = ApplyTokens(pattern, setName, modelName, viewName);
                    baseName = Sanitize(baseName);
                    if (!baseName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) baseName += ".csv";

                    var fullPath = Path.Combine(outputDir, baseName);
                    if (!settings.DefaultOverwrite && File.Exists(fullPath))
                    {
                        fullPath = EnsureUnique(outputDir, baseName);
                    }

                    if (isDryRun) { Trace.WriteLine($"[CSV] DRYRUN -> {fullPath}"); continue; }

                    try
                    {
                        // Late-bind options type and call Export to avoid compile-time dependency on specific API version
                        object? opts = CreateScheduleExportOptions(uiapp);
                        if (opts != null)
                        {
                            TrySetProperty(opts, "FieldDelimiter", ",");
                            TrySetProperty(opts, "TextQualifier", '"');
                            TrySetProperty(opts, "HeadersFootersBlanks", true);
                        }

                        // Prefer overload with options if present
                        bool exported = TryExportWithOptions(vs, outputDir, Path.GetFileName(fullPath), opts)
                                        || TryExportBasic(vs, outputDir, Path.GetFileName(fullPath));
                        if (!exported)
                        {
                            try { uiapp.Application?.WriteJournalComment($"ExportCsvAction: no suitable Export overload found.", false); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        try { uiapp.Application?.WriteJournalComment($"ExportCsvAction failed for '{viewName}': {ex.Message}", false); } catch { }
                    }
                }
            }
        }

        private static object? CreateScheduleExportOptions(UIApplication uiapp)
        {
            try
            {
                var asm = typeof(ViewSchedule).Assembly;
                // Revit typically exposes Autodesk.Revit.DB.ScheduleExportOptions
                var t = asm.GetType("Autodesk.Revit.DB.ScheduleExportOptions", throwOnError: false, ignoreCase: false);
                if (t == null) return null;
                return Activator.CreateInstance(t);
            }
            catch { return null; }
        }

        private static void TrySetProperty(object target, string name, object value)
        {
            if (target == null) return;
            try
            {
                var p = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (p == null || !p.CanWrite) return;
                var vt = p.PropertyType;
                object? coerced = value;
                try
                {
                    if (value != null && !vt.IsAssignableFrom(value.GetType()))
                    {
                        if (vt == typeof(char) && value is string s && s.Length > 0) coerced = s[0];
                        else coerced = Convert.ChangeType(value, vt);
                    }
                }
                catch { coerced = value; }
                p.SetValue(target, coerced);
            }
            catch { }
        }

        private static bool TryExportWithOptions(ViewSchedule vs, string folder, string fileName, object? options)
        {
            try
            {
                var mi = typeof(ViewSchedule).GetMethod("Export", new[] { typeof(string), typeof(string), options?.GetType() ?? typeof(object) });
                if (mi == null && options != null)
                {
                    // Try to resolve overload dynamically when reflection with exact signature fails
                    var methods = typeof(ViewSchedule).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                        .Where(m => m.Name == "Export" && m.GetParameters().Length == 3)
                        .ToList();
                    foreach (var m in methods)
                    {
                        var ps = m.GetParameters();
                        if (ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string) && ps[2].ParameterType.Name.Contains("ScheduleExportOptions"))
                        {
                            mi = m; break;
                        }
                    }
                }
                if (mi == null) return false;
                mi.Invoke(vs, new object?[] { folder, fileName, options });
                return true;
            }
            catch { return false; }
        }

        private static bool TryExportBasic(ViewSchedule vs, string folder, string fileName)
        {
            try
            {
                var mi = typeof(ViewSchedule).GetMethod("Export", new[] { typeof(string), typeof(string) });
                if (mi == null) return false;
                mi.Invoke(vs, new object?[] { folder, fileName });
                return true;
            }
            catch { return false; }
        }

        private static string ApplyTokens(string pattern, string setName, string fileName, string viewName)
        {
            return (pattern ?? string.Empty)
                .Replace("{SetName}", Sanitize(setName))
                .Replace("{FileName}", Sanitize(fileName))
                .Replace("{ViewName}", Sanitize(viewName));
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace('/', '_').Replace('\\', '_').Trim();
        }

        private static string EnsureUnique(string dir, string fileName)
        {
            try
            {
                var baseName = Path.GetFileNameWithoutExtension(fileName);
                var ext = Path.GetExtension(fileName);
                var candidate = fileName; int i = 2;
                while (File.Exists(Path.Combine(dir, candidate)))
                {
                    candidate = $"{baseName} ({i}){ext}"; i++; if (i > 10000) break;
                }
                return candidate;
            }
            catch { return fileName; }
        }
    }
}
