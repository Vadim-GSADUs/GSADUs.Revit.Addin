using System.Collections.Generic;

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
}
