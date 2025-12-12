using System;

namespace GSADUs.Revit.Addin
{
    /// <summary>
    /// Abstraction over settings persistence to enable debouncing and testability.
    /// Phase 4: thin wrapper over the ES-backed IProjectSettingsProvider.
    /// </summary>
    public interface ISettingsPersistence
    {
        AppSettings Load();
        void Save(AppSettings settings);
    }
}
