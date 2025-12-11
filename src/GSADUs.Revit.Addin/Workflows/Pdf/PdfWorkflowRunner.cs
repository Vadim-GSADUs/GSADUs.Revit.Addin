using Autodesk.Revit.DB;
using GSADUs.Revit.Addin.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GSADUs.Revit.Addin.Workflows.Pdf
{
    internal sealed class PdfWorkflowRunner
    {
        private readonly IProjectSettingsProvider _projectSettingsProvider;

        public PdfWorkflowRunner(IProjectSettingsProvider projectSettingsProvider)
        {
            _projectSettingsProvider = projectSettingsProvider;
        }

        public IReadOnlyList<string> Run(Document doc, WorkflowDefinition wf, string displaySetName, AppSettings settings)
        {
            // Basic guards
            if (doc == null || doc.IsFamilyDocument) return Array.Empty<string>();
            if (wf == null || wf.Output != OutputType.Pdf) return Array.Empty<string>();
            displaySetName ??= string.Empty;

            var p = wf.Parameters ?? new Dictionary<string, JsonElement>();
            string Gs(string k) => p.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? string.Empty) : string.Empty;

            // printSetName still used to resolve Revit View/Sheet Set
            var printSetName = Gs(PdfWorkflowKeys.PrintSetName);
            var setupName = Gs(PdfWorkflowKeys.ExportSetupName);
            var pattern = Gs(PdfWorkflowKeys.FileNamePattern);

            if (string.IsNullOrWhiteSpace(pattern)) pattern = "{SetName}.pdf"; // fallback pattern

            // Validation (fail fast)
            if (string.IsNullOrWhiteSpace(printSetName)) return Array.Empty<string>();
            if (string.IsNullOrWhiteSpace(setupName)) return Array.Empty<string>();
            if (!pattern.Contains("{SetName}")) return Array.Empty<string>();

            // Resolve sheet set (by printSetName)
            var setElem = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheetSet))
                .Cast<ViewSheetSet>()
                .FirstOrDefault(s => string.Equals(s.Name, printSetName, StringComparison.OrdinalIgnoreCase));
            if (setElem == null) return Array.Empty<string>();
            var viewIds = setElem.Views?.Cast<View>()?.Select(v => v.Id).ToList() ?? new List<ElementId>();
            if (viewIds.Count == 0) return Array.Empty<string>();

            // Resolve export setup
            var pdfSetup = new FilteredElementCollector(doc)
                .OfClass(typeof(ExportPDFSettings))
                .Cast<ExportPDFSettings>()
                .FirstOrDefault(s => string.Equals(s.Name, setupName, StringComparison.OrdinalIgnoreCase));
            if (pdfSetup == null) return Array.Empty<string>();

            var options = pdfSetup.GetOptions();
            options.Combine = true; // always combine into single PDF

            // Output directory + overwrite policy
            var outputDir = _projectSettingsProvider.GetEffectiveOutputDir(settings);
            try { Directory.CreateDirectory(outputDir); } catch { }
            if (!Directory.Exists(outputDir)) return Array.Empty<string>();

            // Build filename using displaySetName (not printSetName)
            var rawName = pattern.Replace("{SetName}", displaySetName);
            rawName = Sanitize(rawName);
            // Strip any number of trailing .pdf (case-insensitive), then add exactly one
            while (rawName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                rawName = rawName.Substring(0, rawName.Length - 4);
            var fileName = rawName + ".pdf";

            if (!settings.DefaultOverwrite)
            {
                fileName = EnsureUnique(outputDir, fileName);
            }

            var fullPath = Path.Combine(outputDir, fileName);
            // Revit sometimes appends the extension internally; provide name WITHOUT extension to avoid double .pdf
            var baseNoExt = System.IO.Path.GetFileNameWithoutExtension(fileName);
            options.FileName = baseNoExt;

            Trace.WriteLine($"PDF_CONFIG views={(viewIds?.Count ?? 0)} combine={options?.Combine ?? false} dir=\"{outputDir}\" corr={Logging.RunLog.CorrId}");
            var before = Directory.Exists(outputDir) ? new HashSet<string>(Directory.GetFiles(outputDir, "*.pdf")) : new HashSet<string>();
            Trace.WriteLine($"ARTIFACT_BASELINE count={before.Count} corr={Logging.RunLog.CorrId}");
            Trace.WriteLine("PDF_EXPORT begin corr=" + Logging.RunLog.CorrId);
            bool exportOk = false;
            try
            {
                exportOk = doc.Export(outputDir, viewIds, options);
                Trace.WriteLine($"PDF_EXPORT end ok={exportOk} corr={Logging.RunLog.CorrId}");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"FAIL PDF_EXPORT ex={ex.GetType().Name} msg={ex.Message} corr={Logging.RunLog.CorrId}");
                throw;
            }
            var after = Directory.Exists(outputDir) ? Directory.GetFiles(outputDir, "*.pdf") : Array.Empty<string>();
            var newFiles = after.Where(p => !before.Contains(p)).ToList();
            Trace.WriteLine($"ARTIFACT_DIFF new={newFiles.Count} corr={Logging.RunLog.CorrId}");
            foreach (var nf in newFiles.Take(10)) Trace.WriteLine($"ARTIFACT_NEW \"{nf}\" corr={Logging.RunLog.CorrId}");
            bool produced = (options?.Combine ?? false) ? newFiles.Count == 1 : newFiles.Count >= (viewIds?.Count ?? 0);
            Trace.WriteLine($"ARTIFACT_CHECK produced={produced} combine={(options?.Combine ?? false)} expected={(viewIds?.Count ?? 0)} corr={Logging.RunLog.CorrId}");

            try
            {
                if (!File.Exists(fullPath))
                {
                    var alt = Path.Combine(outputDir, baseNoExt + ".pdf");
                    if (File.Exists(alt)) fullPath = alt; // adjust if API wrote only once
                }
                return File.Exists(fullPath) ? new[] { fullPath } : Array.Empty<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string EnsureUnique(string dir, string fileName)
        {
            try
            {
                var baseName = Path.GetFileNameWithoutExtension(fileName);
                var ext = Path.GetExtension(fileName); // expect .pdf
                var candidate = fileName;
                int i = 2;
                while (File.Exists(Path.Combine(dir, candidate)))
                {
                    candidate = $"{baseName} ({i}){ext}";
                    i++;
                    if (i > 10000) break; // safety
                }
                return candidate;
            }
            catch { return fileName; }
        }

        private static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}
