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

    /// <summary>
    /// ExternalEvent-backed save service for project settings.
    /// Ensures settings are applied to the authoritative catalog before persisting.
    /// </summary>
    public interface IProjectSettingsSaveService
    {
        void RequestSave(AppSettings newSettings, Action<bool>? onCompleted = null);
    }
}
