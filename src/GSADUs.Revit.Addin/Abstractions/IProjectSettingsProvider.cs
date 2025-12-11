using GSADUs.Revit.Addin;

namespace GSADUs.Revit.Addin.Abstractions
{
    public interface IProjectSettingsProvider
    {
        AppSettings Load();
        void Save(AppSettings settings);
        string GetEffectiveOutputDir(AppSettings settings);
        string GetEffectiveLogDir(AppSettings settings);
    }
}
