using System.Collections.Generic;

namespace GSADUs.Revit.Addin
{
    public record BatchExportSettings(
      IReadOnlyList<string> SetNames,
      string OutputDir,
      bool SaveBefore,
      bool Detach,
      bool RecenterXY,
      bool Overwrite,
      IReadOnlyList<string>? SetIds = null)
    {
        public IReadOnlyList<string>? ActionIds { get; init; }
    }
}

