using System;
using System.Linq;

namespace GSADUs.Revit.Addin.Logging
{
    internal static class LegacyGuards
    {
        internal static readonly string[] BannedHeaders = new[]
        {
            "Key","Status","Date","Export Date","Members","CurrentHash","AnnoHash",
            "MemberIds","Before","PlusAdded","MinusRemoved"
        };

        internal static bool IsBanned(string k) =>
            !string.IsNullOrWhiteSpace(k) &&
            BannedHeaders.Any(h => string.Equals(h, k, StringComparison.OrdinalIgnoreCase));
    }
}
