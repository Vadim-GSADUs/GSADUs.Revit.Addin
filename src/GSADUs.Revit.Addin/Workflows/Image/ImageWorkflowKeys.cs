namespace GSADUs.Revit.Addin.Workflows.Image
{
    /// <summary>
    /// Image Export workflow parameter keys (simplified – view selection removed; print set now required for scoping).
    /// </summary>
    internal static class ImageWorkflowKeys
    {
        // Core (print set only – specific view selection removed)
        public const string imagePrintSetName = "imagePrintSetName"; // View/Sheet set name
        public const string appendViewName = "appendViewName";       // bool; default true (legacy – still honored if present)

        // New: Export Range scope
        public const string exportScope = "exportScope";             // "PrintSet" | "SingleView"
        public const string singleViewId = "singleViewId";           // string ElementId

        // Format + resolution keys
        public const string imageFormat = "imageFormat";             // PNG | BMP | TIFF
        public const string resolutionPreset = "resolutionPreset";   // DPI_72 | DPI_150 | DPI_300 | DPI_600 (legacy UI may still emit Low/Medium/High/Ultra)

        // File naming
        public const string fileNamePattern = "fileNamePattern";     // must contain {SetName}

        // Crop
        public const string cropOffset = "cropOffset";               // double feet, store only if > 0
        public const string cropMode = "cropMode";                   // Auto | Static (omit if Static)

        // Heuristic camera controls (3D Auto crop)
        public const string heuristicFovDeg = "heuristicFovDeg";     // string double degrees, e.g. "50"
        public const string heuristicFovBufferPct = "heuristicFovBufferPct"; // string double percent, e.g. "5"

        // Visual overrides (reserved – still unused here)
        public const string background = "background";               // FromView | White | Black | Transparent (omit if FromView)
        public const string visualStyle = "visualStyle";             // FromView | HiddenLine | Shaded | ConsistentColors | Realistic | Wireframe (omit if FromView)

        // PascalCase aliases (mirroring surviving keys)
        public const string ImagePrintSetName = imagePrintSetName;
        public const string AppendViewName = appendViewName;
        public const string ExportScope = exportScope;
        public const string SingleViewId = singleViewId;
        public const string ImageFormat = imageFormat;
        public const string ResolutionPreset = resolutionPreset;
        public const string FileNamePattern = fileNamePattern;
        public const string CropOffset = cropOffset;
        public const string CropMode = cropMode;
        public const string Background = background;
        public const string VisualStyle = visualStyle;
        // PascalCase aliases for new keys
        public const string HeuristicFovDeg = heuristicFovDeg;
        public const string HeuristicFovBufferPct = heuristicFovBufferPct;
    }
}
