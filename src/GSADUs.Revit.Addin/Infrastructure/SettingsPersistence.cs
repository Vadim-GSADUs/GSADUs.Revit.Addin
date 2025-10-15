using System;

namespace GSADUs.Revit.Addin
{
    internal sealed class SettingsPersistence : ISettingsPersistence
    {
        public AppSettings Load() => AppSettingsStore.Load();
        public void Save(AppSettings settings) => AppSettingsStore.Save(settings);
    }
}
