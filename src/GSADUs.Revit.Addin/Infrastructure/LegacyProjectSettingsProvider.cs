using GSADUs.Revit.Addin.Abstractions;

namespace GSADUs.Revit.Addin.Infrastructure
{
    public class LegacyProjectSettingsProvider : IProjectSettingsProvider
    {
        public AppSettings Load()
        {
            return AppSettingsStore.Load();
        }

        public void Save(AppSettings settings)
        {
            AppSettingsStore.Save(settings);
        }

        public string GetEffectiveOutputDir(AppSettings settings)
        {
            return AppSettingsStore.GetEffectiveOutputDir(settings);
        }

        public string GetEffectiveLogDir(AppSettings settings)
        {
            return AppSettingsStore.GetEffectiveLogDir(settings);
        }
    }
}
