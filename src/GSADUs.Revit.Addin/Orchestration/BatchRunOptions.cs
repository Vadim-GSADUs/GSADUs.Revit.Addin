using System;
using System.Collections.Generic;

namespace GSADUs.Revit.Addin
{
    public sealed class BatchRunOptions
    {
        // Optional preferred identifiers (SelectionFilterElement.UniqueId)
        public IReadOnlyList<string>? SetIds { get; init; }
        // Required for legacy execution (name based)
        public IReadOnlyList<string> SetNames { get; init; } = Array.Empty<string>();
        public string OutputDir { get; init; } = string.Empty;
        public bool Overwrite { get; init; }
        public bool SaveBefore { get; init; }
    }
}
