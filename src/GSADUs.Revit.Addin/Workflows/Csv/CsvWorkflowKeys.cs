namespace GSADUs.Revit.Addin.Workflows.Csv
{
    internal static class CsvWorkflowKeys
    {
        public const string scheduleIds = "scheduleIds";        // string[] of ElementId integers serialized as strings
        public const string fileNamePattern = "fileNamePattern"; // supports {SetName}, {FileName}, {ViewName}

        // PascalCase aliases (for symmetry)
        public const string ScheduleIds = scheduleIds;
        public const string FileNamePattern = fileNamePattern;
    }
}
