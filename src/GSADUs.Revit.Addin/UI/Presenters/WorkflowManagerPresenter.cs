using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using GSADUs.Revit.Addin.Workflows.Pdf;
using GSADUs.Revit.Addin.Workflows.Image;

namespace GSADUs.Revit.Addin.UI
{
    /// <summary>
    /// Phase 2 presenter. Mediates window lifecycle and selection routing.
    /// Currently thin to keep behavior unchanged; expanded in later PRs.
    /// </summary>
    internal sealed class WorkflowManagerPresenter
    {
        private readonly WorkflowCatalogService _catalog;
        private readonly IDialogService _dialogs;

        public PdfWorkflowTabViewModel PdfWorkflow { get; } = new PdfWorkflowTabViewModel();
        public ImageWorkflowTabViewModel ImageWorkflow { get; } = new ImageWorkflowTabViewModel();

        public WorkflowManagerPresenter(WorkflowCatalogService catalog, IDialogService dialogs)
        {
            _catalog = catalog;
            _dialogs = dialogs;
        }

        public AppSettings Settings => _catalog.Settings;

        public void OnWindowConstructed(WorkflowManagerWindow win)
        {
            // Placeholder: could set DataContexts in future.
        }

        public void OnLoaded(UIDocument? uidoc, WorkflowManagerWindow win)
        {
            // Placeholder: hook for future hydration orchestration.
        }

        public void OnSavedComboChanged(string tag, WorkflowDefinition? wf, WorkflowManagerWindow win)
        {
            // Placeholder: will coordinate tab routing later.
        }

        public void OnMarkDirty(string tag)
        {
            // Placeholder: could surface dirty-state telemetry.
            try { PerfLogger.Write("WorkflowManager.MarkDirty", tag, TimeSpan.Zero); } catch { }
        }

        public void SaveSettings() => _catalog.Save();

        // ---- Phase 1: move collectors from window into presenter ----
        public void PopulatePdfSources(Document doc)
        {
            if (doc == null) return;
            try
            {
                var setNames = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheetSet))
                    .Cast<ViewSheetSet>()
                    .Select(s => s.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList();
                PdfWorkflow.AvailableViewSets.Clear();
                foreach (var n in setNames) PdfWorkflow.AvailableViewSets.Add(n);

                var setupNames = new FilteredElementCollector(doc)
                    .OfClass(typeof(ExportPDFSettings))
                    .Cast<ExportPDFSettings>()
                    .Select(s => s.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList();
                PdfWorkflow.AvailableExportSetups.Clear();
                foreach (var n in setupNames) PdfWorkflow.AvailableExportSetups.Add(n);
            }
            catch { }
        }

        public void PopulateImageSources(Document doc)
        {
            if (doc == null) return;
            try
            {
                var setNamesForImage = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheetSet))
                    .Cast<ViewSheetSet>()
                    .Select(s => s.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList();

                ImageWorkflow.AvailablePrintSets.Clear();
                foreach (var n in setNamesForImage)
                    ImageWorkflow.AvailablePrintSets.Add(n);

                var views = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate
                                && v.ViewType != ViewType.ThreeD
                                && v.ViewType != ViewType.DrawingSheet
                                && (v is ViewPlan || v is ViewDrafting || v is ViewSection || v.ViewType == ViewType.AreaPlan))
                    .OrderBy(v => v.ViewType.ToString())
                    .ThenBy(v => v.Name)
                    .Select(v => new SingleViewOption { Id = v.Id.ToString(), Label = $"{v.ViewType} - {v.Name}" })
                    .ToList();
                ImageWorkflow.AvailableSingleViews.Clear();
                foreach (var o in views)
                    ImageWorkflow.AvailableSingleViews.Add(o);
            }
            catch { }
        }

        // ---- Phase 1: move JSON persistence into presenter ----
        private static void EnsureActionId(WorkflowDefinition wf, string id)
        {
            wf.ActionIds ??= new List<string>();
            if (!wf.ActionIds.Any(a => string.Equals(a, id, StringComparison.OrdinalIgnoreCase))) wf.ActionIds.Add(id);
        }

        private static JsonElement J(string value)
        {
            using var d = JsonDocument.Parse("\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"");
            return d.RootElement.Clone();
        }

        public bool SavePdfWorkflow(WorkflowDefinition existing)
        {
            var vm = PdfWorkflow;
            var pdfSetName = vm?.SelectedSetName;
            var pdfSetupName = vm?.SelectedPrintSet;
            var pdfPattern = vm?.Pattern;

            // Require set and setup; pattern will be normalized automatically
            if (string.IsNullOrWhiteSpace(pdfSetName) || string.IsNullOrWhiteSpace(pdfSetupName))
            {
                _dialogs.Info("Save", "Select a view/sheet set and an export setup.");
                return false;
            }

            // Normalize pattern: ensure {SetName} present and .pdf extension
            if (string.IsNullOrWhiteSpace(pdfPattern)) pdfPattern = "{SetName}.pdf";
            if (!pdfPattern.Contains("{SetName}")) pdfPattern = "{SetName}" + pdfPattern;
            if (!pdfPattern.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) pdfPattern += ".pdf";

            // basic sanitize (mirror window helper without dependency)
            try
            {
                var invalid = System.IO.Path.GetInvalidFileNameChars();
                var s = pdfPattern.Trim();
                foreach (var c in invalid) s = s.Replace(c, '_');
                s = s.Replace('/', '_').Replace('\\', '_');
                pdfPattern = s;
            }
            catch { }

            var p = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [PdfWorkflowKeys.PrintSetName] = J(pdfSetName!),
                [PdfWorkflowKeys.ExportSetupName] = J(pdfSetupName!),
                [PdfWorkflowKeys.FileNamePattern] = J(pdfPattern!)
            };
            existing.Parameters = p;
            EnsureActionId(existing, "export-pdf");
            return true;
        }

        public void SaveImageWorkflow(WorkflowDefinition existing)
        {
            var vm = ImageWorkflow;
            var imageParams = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            void SP(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) imageParams[k] = J(v); }

            // Normalize pattern to always include {SetName}
            var pat = vm?.Pattern;
            if (string.IsNullOrWhiteSpace(pat)) pat = "{SetName}";
            if (!pat.Contains("{SetName}")) pat = "{SetName}" + pat;

            SP(ImageWorkflowKeys.imagePrintSetName, vm?.SelectedPrintSet);
            SP(ImageWorkflowKeys.fileNamePattern, pat);
            SP(ImageWorkflowKeys.prefix, vm?.Prefix);
            SP(ImageWorkflowKeys.suffix, vm?.Suffix);

            var cropMode = vm?.CropMode;
            if (!string.IsNullOrWhiteSpace(cropMode) && !string.Equals(cropMode, "Static", StringComparison.OrdinalIgnoreCase))
                SP(ImageWorkflowKeys.cropMode, cropMode);

            var cropOffset = vm?.CropOffset;
            if (!string.IsNullOrWhiteSpace(cropOffset)) SP(ImageWorkflowKeys.cropOffset, cropOffset);

            SP(ImageWorkflowKeys.resolutionPreset, vm?.Resolution);

            var fmt = vm?.Format;
            if (!string.IsNullOrWhiteSpace(fmt))
            {
                var up = fmt.Trim().ToUpperInvariant();
                if (up == "PNG" || up == "BMP" || up == "TIFF") SP(ImageWorkflowKeys.imageFormat, up);
            }

            try
            {
                var scope = vm?.ExportScope ?? "PrintSet";
                imageParams[ImageWorkflowKeys.exportScope] = J(scope);
                if (string.Equals(scope, "SingleView", StringComparison.OrdinalIgnoreCase))
                {
                    var svId = vm?.SelectedSingleViewId;
                    if (!string.IsNullOrWhiteSpace(svId)) imageParams[ImageWorkflowKeys.singleViewId] = J(svId);
                }
            }
            catch { }

            existing.Parameters = imageParams;
            EnsureActionId(existing, "export-image");
        }
    }
}
