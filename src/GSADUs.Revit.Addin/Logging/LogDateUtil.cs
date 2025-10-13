using System;
using System.Globalization;

namespace GSADUs.Revit.Addin
{
    // Marked as a hard error in DEBUG builds to prevent accidental usage.
#if DEBUG
    [Obsolete("Legacy API. Do not reference. Use BatchLog.NowIso() + UI formatter.", true)]
#endif
    internal static class LogDateUtil
    {
        private static readonly string[] Formats = new[]
        {
            // Legacy accepted formats (audit/export pre-v2)
            "MM/dd/yy HH:mm", "M/d/yy HH:mm", "MM/dd/yyyy HH:mm", "M/d/yyyy HH:mm"
        };

        /// <summary>
        /// LEGACY: Parse legacy timestamp strings used in pre-v2 logs.
        /// Retained only for migration when loading old CSV files.
        /// </summary>
        public static DateTime? Parse(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (DateTime.TryParseExact(s.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return dt;
            return null;
        }

        /// <summary>
        /// LEGACY: Writer methods were removed. Use BatchLog.NowIso() for new audit / export timestamps.
        /// </summary>
        [Obsolete("Use BatchLog.NowIso() for all new timestamps.")]
        public static string LegacyNow() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
    }
}
