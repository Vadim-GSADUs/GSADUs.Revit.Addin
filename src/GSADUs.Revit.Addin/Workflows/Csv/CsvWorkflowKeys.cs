namespace GSADUs.Revit.Addin.Workflows.Csv
{
    internal static class CsvWorkflowKeys
    {
        public const string scheduleIds = "scheduleIds";            // string[] of ElementId integers serialized as strings
        public const string fileNamePattern = "fileNamePattern";     // supports {SetName}, {FileName}, {ViewName}
        public const string headersFootersBlanks = "headersFootersBlanks"; // bool: include headers/footers/blank lines
        public const string title = "title";                          // bool: include schedule title

        // Legacy compatibility (read-only)
        public const string suppressBlankRowsLegacy = "suppressBlankRows"; // legacy flag (inverse semantics of headersFootersBlanks)

        // PascalCase aliases (for symmetry)
        public const string ScheduleIds = scheduleIds;
        public const string FileNamePattern = fileNamePattern;
        public const string HeadersFootersBlanks = headersFootersBlanks;
        public const string Title = title;
    }
}
