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

        public ProjectSettingsSaveService(WorkflowCatalogService catalog)
        {
            _catalog = catalog ?? throw new System.ArgumentNullException(nameof(catalog));
            var dispatcher = System.Windows.Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
            _saver = new UI.ProjectSettingsSaveExternalEvent(_catalog, dispatcher);
        }

        public void RequestSave(AppSettings newSettings, System.Action<bool>? onCompleted = null)
        {
            if (newSettings == null)
            {
                onCompleted?.Invoke(false);
                return;
            }

            // Apply the incoming settings snapshot to the catalog before persisting.
            try
            {
                var current = _catalog.Settings;
                if (current == null)
                {
                    onCompleted?.Invoke(false);
                    return;
                }

                // Shallow copy of top-level settings so catalog remains authoritative owner.
                current.Version = newSettings.Version;
                current.LogDir = newSettings.LogDir;
                current.DefaultOutputDir = newSettings.DefaultOutputDir;
                current.DefaultRunAuditBeforeExport = newSettings.DefaultRunAuditBeforeExport;
                current.DefaultSaveBefore = newSettings.DefaultSaveBefore;
                current.DefaultOverwrite = newSettings.DefaultOverwrite;
                current.DeepAnnoStatus = newSettings.DeepAnnoStatus;
                current.DryrunDiagnostics = newSettings.DryrunDiagnostics;
                current.PerfDiagnostics = newSettings.PerfDiagnostics;
                current.OpenOutputFolder = newSettings.OpenOutputFolder;
                current.ValidateStagingArea = newSettings.ValidateStagingArea;
                current.DrawAmbiguousRectangles = newSettings.DrawAmbiguousRectangles;
                current.SelectionSeedCategories = newSettings.SelectionSeedCategories != null ? new System.Collections.Generic.List<int>(newSettings.SelectionSeedCategories) : null;
                current.SelectionProxyCategories = newSettings.SelectionProxyCategories != null ? new System.Collections.Generic.List<int>(newSettings.SelectionProxyCategories) : null;
                current.CleanupBlacklistCategories = newSettings.CleanupBlacklistCategories != null ? new System.Collections.Generic.List<int>(newSettings.CleanupBlacklistCategories) : null;
                current.SelectionProxyDistance = newSettings.SelectionProxyDistance;
                current.CurrentSetParameterName = newSettings.CurrentSetParameterName;
                current.StagingWidth = newSettings.StagingWidth;
                current.StagingHeight = newSettings.StagingHeight;
                current.StagingBuffer = newSettings.StagingBuffer;
                current.StageMoveMode = newSettings.StageMoveMode;
                current.StagingAuthorizedCategoryNames = newSettings.StagingAuthorizedCategoryNames != null ? new System.Collections.Generic.List<string>(newSettings.StagingAuthorizedCategoryNames) : null;
                current.StagingAuthorizedUids = newSettings.StagingAuthorizedUids != null ? new System.Collections.Generic.List<string>(newSettings.StagingAuthorizedUids) : null;
                current.Workflows = newSettings.Workflows != null ? new System.Collections.Generic.List<WorkflowDefinition>(newSettings.Workflows) : null;
                current.SelectedWorkflowIds = newSettings.SelectedWorkflowIds != null ? new System.Collections.Generic.List<string>(newSettings.SelectedWorkflowIds) : null;
            }
            catch
            {
                onCompleted?.Invoke(false);
                return;
            }

            try
            {
                _saver.RequestSave(onCompleted);
            }
            catch
            {
                onCompleted?.Invoke(false);
            }
        }
    }
}
