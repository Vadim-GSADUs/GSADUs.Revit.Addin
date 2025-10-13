using Autodesk.Revit.UI;

namespace GSADUs.Revit.Addin
{
    // Simple holder for the current UIApplication so modal windows can post Revit commands
    internal static class RevitUiContext
    {
        private static UIApplication? _current;
        public static UIApplication? Current
        {
            get => _current;
            set => _current = value;
        }
    }
}
