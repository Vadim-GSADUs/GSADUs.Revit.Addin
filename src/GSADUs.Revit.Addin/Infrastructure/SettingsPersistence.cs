using System;
using GSADUs.Revit.Addin.Abstractions;
using GSADUs.Revit.Addin.UI;

namespace GSADUs.Revit.Addin
{
    internal sealed class SettingsPersistence : ISettingsPersistence
    {
        private readonly IProjectSettingsProvider _projectSettingsProvider;

        public SettingsPersistence(IProjectSettingsProvider projectSettingsProvider)
        {
            _projectSettingsProvider = projectSettingsProvider;
        }

        public AppSettings Load() => _projectSettingsProvider.Load();

        public void Save(AppSettings settings)
        {
            _projectSettingsProvider.Save(settings);
        }
    }

    /// <summary>
    /// Default implementation that applies settings to the catalog and persists via ExternalEvent.
    /// </summary>
    internal sealed class ProjectSettingsSaveService : IProjectSettingsSaveService
    {
        private readonly WorkflowCatalogService _catalog;
        private readonly UI.ProjectSettingsSaveExternalEvent _saver;

        public ProjectSettingsSaveService(WorkflowCatalogService catalog, UI.ProjectSettingsSaveExternalEvent saver)
        {
            _catalog = catalog ?? throw new System.ArgumentNullException(nameof(catalog));
            _saver = saver ?? throw new System.ArgumentNullException(nameof(saver));
        }

        public void RequestSave(AppSettings newSettings, System.Action<bool>? onCompleted = null)
        {
            if (newSettings == null)
            {
                onCompleted?.Invoke(false);
                return;
            }

            try
            {
                _saver.RequestSave(newSettings, onCompleted);
            }
            catch
            {
                onCompleted?.Invoke(false);
            }
        }
    }
}
