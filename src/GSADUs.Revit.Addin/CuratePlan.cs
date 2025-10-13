using System;
using System.Collections.Generic;
using System.Linq;

namespace GSADUs.Revit.Addin
{
    // DTOs used by preview-first curation flow
    public sealed class SetDelta
    {
        public string SetName { get; set; } = string.Empty;
        public HashSet<int> BeforeIds { get; set; } = new HashSet<int>();
        public HashSet<int> ToAdd { get; set; } = new HashSet<int>();
        public HashSet<int> ToRemove { get; set; } = new HashSet<int>();
        public HashSet<int> AfterIds { get; set; } = new HashSet<int>();
        public bool WasAmbiguous { get; set; }
        public string Details { get; set; } = string.Empty; // human-readable grouped summary
        // New: stable identity of the SelectionFilterElement (preferred over name for Apply)
        public string? FilterUniqueId { get; set; }

        public int BeforeCount => BeforeIds.Count;
        public int AddedCount => ToAdd.Count;
        public int RemovedCount => ToRemove.Count;
        public int AfterCount => AfterIds.Count;
        public bool HasChanges => AddedCount > 0 || RemovedCount > 0;
    }

    public sealed class CuratePlan
    {
        public IReadOnlyList<string> ValidSets { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> IgnoredSets { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> AmbiguousSets { get; set; } = Array.Empty<string>();
        public IReadOnlyList<SetDelta> Deltas { get; set; } = Array.Empty<SetDelta>();

        public bool AnyChanges => Deltas.Any(d => d.HasChanges);
    }
}
