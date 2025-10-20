using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;

namespace GSADUs.Revit.Addin
{
    public sealed class AppSettings
    {
        public string? LogDir { get; set; }
        public string? DefaultOutputDir { get; set; }
        // All bool toggles default to false
        public bool DefaultRunAuditBeforeExport { get; set; } = false;
        public bool DefaultSaveBefore { get; set; } = false;
        public bool DefaultRecenterXY { get; set; } = false;
        public bool DefaultOverwrite { get; set; } = false;
        public bool DefaultCleanup { get; set; } = false;
        // New: open output folder when batch export completes
        public bool OpenOutputFolder { get; set; } = false;
        // New: toggle staging area validation prompt/logic
        public bool ValidateStagingArea { get; set; } = false;

        // Selection Sets settings
        public List<int>? SelectionSeedCategories { get; set; }
        public List<int>? SelectionProxyCategories { get; set; }
        public double SelectionProxyDistance { get; set; } = 1.0;

        // Cleanup settings
        public List<int>? CleanupBlacklistCategories { get; set; }

        // Image Whielist (mirrors proxy categories pattern)
        public List<int>? ImageWhitelistCategoryIds { get; set; }

        public int Version { get; set; } = 1;

        // Persist preferred columns for BatchExportWindow
        public List<string>? PreferredBatchLogColumns { get; set; }

        // New: persist preferred action IDs for BatchExportWindow
        public List<string>? PreferredActions { get; set; }

        // Deep annotation hash toggle
        public bool DeepAnnoStatus { get; set; } = false;

        // New: toggle single-button dry-run diagnostics mode
        public bool DryrunDiagnostics { get; set; } = false;

        // New: Purge & Compact (run near the end after cleanup)
        public bool PurgeCompact { get; set; } = false;

        // New: Preferred floor plan view to use for thumbnail generation on save
        public string? ThumbnailViewName { get; set; }

        // New: enable performance diagnostics logging
        public bool PerfDiagnostics { get; set; } = false;

        // Visualization: toggle drawing ambiguous selection set rectangles
        public bool DrawAmbiguousRectangles { get; set; } = false;

        // ---- Workflows (Phase 2 additions) ----
        public List<WorkflowDefinition>? Workflows { get; set; }
        public List<string>? SelectedWorkflowIds { get; set; }

        // ---- Internal staging & shared parameters ----
        public string? CurrentSetParameterName { get; set; } = "CurrentSet";
        public string? CurrentSetParameterGuid { get; set; }
        public string? SharedParametersFilePath { get; set; }

        // Staging configuration (Phase 2)
        public double StagingWidth { get; set; } = 200.0;   // model units
        public double StagingHeight { get; set; } = 200.0;  // model units
        public double StagingBuffer { get; set; } = 10.0;   // model units
        public string StageMoveMode { get; set; } = "CentroidToOrigin"; // or "MinToOrigin"

        // Staging authorization
        public List<string>? StagingAuthorizedUids { get; set; }
        public List<string>? StagingAuthorizedCategoryNames { get; set; }
    }

    internal static class AppSettingsStore
    {
        private static readonly string BaseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GSADUs", "Revit", "Addin");
        private static readonly string FilePath = Path.Combine(BaseDir, "settings.json");

        // Fallbacks used when no user override
        internal static readonly string FallbackLogDir = @"G:\\Shared drives\\GSADUs Projects\\Our Models\\0 - CATALOG\\Output";
        internal static readonly string FallbackOutputDir = @"G:\\Shared drives\\GSADUs Projects\\Our Models\\0 - CATALOG\\Output";

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var s = JsonSerializer.Deserialize<AppSettings>(json, opts) ?? new AppSettings();
                    EnsureWorkflowDefaults(s);
                    MigrateRvtWorkflowsIfNeeded(s);
                    EnsureSketchInCleanupBlacklist(s);
                    return s;
                }
            }
            catch { }
            var fresh = new AppSettings();
            EnsureWorkflowDefaults(fresh);
            MigrateRvtWorkflowsIfNeeded(fresh);
            EnsureSketchInCleanupBlacklist(fresh);
            return fresh;
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(BaseDir);
                var opts = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
                var json = JsonSerializer.Serialize(settings, opts);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch { }
        }

        public static string GetEffectiveLogDir(AppSettings s)
        {
            var dir = string.IsNullOrWhiteSpace(s.LogDir) ? FallbackLogDir : s.LogDir!;
            return dir;
        }

        public static string GetEffectiveOutputDir(AppSettings s)
        {
            var dir = string.IsNullOrWhiteSpace(s.DefaultOutputDir) ? FallbackOutputDir : s.DefaultOutputDir!;
            return dir;
        }

        private static void EnsureWorkflowDefaults(AppSettings s)
        {
            s.Workflows ??= new List<WorkflowDefinition>();
            s.SelectedWorkflowIds ??= new List<string>();
            s.StagingAuthorizedUids ??= new List<string>();
            s.StagingAuthorizedCategoryNames ??= new List<string>();
            if (s.Workflows.Count == 0)
            {
                // Seed defaults
                s.Workflows.Add(new WorkflowDefinition
                {
                    Name = "RVT ? Model",
                    Kind = WorkflowKind.External,
                    Output = OutputType.Rvt,
                    Scope = "CurrentSet", // changed from Model so it's visible in BatchExportWindow
                    Description = "Deliverable RVT clone",
                    Order = 0,
                    ActionIds = new List<string> { "export-rvt" }
                });
                s.Workflows.Add(new WorkflowDefinition
                {
                    Name = "PDF ? Floorplan",
                    Kind = WorkflowKind.Internal,
                    Output = OutputType.Pdf,
                    Scope = "CurrentSet",
                    Description = "Export floor plan PDFs (stub)",
                    Order = 1,
                    ActionIds = new List<string> { "export-pdf" }
                });
                s.Workflows.Add(new WorkflowDefinition
                {
                    Name = "Image ? Floorplan",
                    Kind = WorkflowKind.Internal,
                    Output = OutputType.Image,
                    Scope = "CurrentSet",
                    Description = "Export floor plan images (stub)",
                    Order = 2,
                    ActionIds = new List<string> { "export-image" }
                });
                s.Workflows.Add(new WorkflowDefinition
                {
                    Name = "CSV ? Schedule ? Room",
                    Kind = WorkflowKind.Internal,
                    Output = OutputType.Csv,
                    Scope = "CurrentSet",
                    Description = "Export Room schedule CSV (stub)",
                    Order = 3,
                    ActionIds = new List<string> { "export-csv" }
                });
            }
        }

        // One-time migration: make existing RVT workflows visible in Batch Export and ensure action id
        private static void MigrateRvtWorkflowsIfNeeded(AppSettings s)
        {
            if (s.Workflows == null || s.Workflows.Count == 0) return;
            bool changed = false;
            foreach (var wf in s.Workflows)
            {
                try
                {
                    if (wf.Output == OutputType.Rvt)
                    {
                        if (string.IsNullOrWhiteSpace(wf.Scope) || wf.Scope.Equals("Model", StringComparison.OrdinalIgnoreCase))
                        {
                            wf.Scope = "CurrentSet";
                            changed = true;
                        }
                        if (wf.ActionIds == null || !wf.ActionIds.Any(a => string.Equals(a, "export-rvt", StringComparison.OrdinalIgnoreCase)))
                        {
                            wf.ActionIds ??= new List<string>();
                            wf.ActionIds.Add("export-rvt");
                            changed = true;
                        }
                    }
                }
                catch { }
            }
            if (changed)
            {
                try { Save(s); } catch { }
            }
        }

        // Ensure <Sketch> category (-2000045) is always present in CleanupBlacklistCategories
        private static void EnsureSketchInCleanupBlacklist(AppSettings s)
        {
            const int SketchCategoryId = -2000045;
            if (s.CleanupBlacklistCategories == null)
                s.CleanupBlacklistCategories = new List<int> { SketchCategoryId };
            else if (!s.CleanupBlacklistCategories.Contains(SketchCategoryId))
                s.CleanupBlacklistCategories.Add(SketchCategoryId);
        }
    }
}
