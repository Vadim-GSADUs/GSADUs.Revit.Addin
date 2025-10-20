namespace GSADUs.Revit.Addin.Workflows.Rvt
{
    internal static class RvtWorkflowKeys
    {
        public const string templatePath = "templatePath";           // string: full path to .rte
        public const string outputDirOverride = "outputDirOverride"; // string: optional path overriding DefaultOutputDir
        public const string backupCleanup = "backupCleanup";         // bool: if true, delete *.000*.rvt after save
    }
}
