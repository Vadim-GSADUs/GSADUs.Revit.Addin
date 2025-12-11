using GSADUs.Revit.Addin.Abstractions;

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
}
