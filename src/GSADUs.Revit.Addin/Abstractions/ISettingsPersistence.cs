using System;

namespace GSADUs.Revit.Addin
{
    /// <summary>
    /// Abstraction over settings persistence to enable debouncing and testability.
    /// Phase 2: simple sync wrapper over AppSettingsStore; can evolve later.
    /// </summary>
    public interface ISettingsPersistence
    {
        AppSettings Load();
        void Save(AppSettings settings);
    }
}
