using System;

namespace GSADUs.Revit.Addin
{
    // Marked as a hard error in DEBUG builds to prevent new references.
#if DEBUG
    [Obsolete("Legacy API. Do not reference. Use v2 audit/export fields.", true)]
#endif
    internal static class LogStatus
    {
        /// <summary>
        /// LEGACY: Status derivation based on export vs audit timestamps.
        /// v2 replaces this with UI-derived freshness using MembersHash + WorkflowSig.
        /// </summary>
        [Obsolete("Use v2 UI-derived freshness (MembersHash + WorkflowSig). This legacy method will be removed after migration.")]
        public static string Derive(string exportStamp, string auditDate)
        {
            var dtExport = LogDateUtil.Parse(exportStamp);
            var dtAudit = LogDateUtil.Parse(auditDate);
            if (dtExport == null || string.IsNullOrWhiteSpace(exportStamp)) return "New";
            if (dtAudit == null) return "Up to Date";
            return dtExport >= dtAudit ? "Up to Date" : "Update Needed";
        }
    }
}
