using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Windows;

namespace GSADUs.Revit.Addin.UI
{
    internal static class SettingsWindowHost
    {
        private static readonly Dictionary<string, SettingsWindow> _windows = new();

        public static void ShowOrActivate(Document? doc, Window? owner = null)
        {
            var key = GetKey(doc);
            if (_windows.TryGetValue(key, out var existing))
            {
                if (existing != null && existing.IsLoaded)
                {
                    try
                    {
                        if (!existing.IsVisible) existing.Show();
                        existing.Topmost = true;
                        existing.Activate();
                        existing.Topmost = false;
                        return;
                    }
                    catch { }
                }
            }

            var win = new SettingsWindow(null, doc);
            if (owner != null)
            {
                try { win.Owner = owner; } catch { }
            }

            _windows[key] = win;
            win.Closed += (_, _) =>
            {
                try
                {
                    _windows.Remove(key);
                }
                catch { }
            };

            win.Show();
        }

        private static string GetKey(Document? doc)
        {
            try
            {
                if (doc != null)
                {
                    var path = doc.PathName;
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        return $"SW::{path}";
                    }

                    var title = doc.Title;
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        return $"SW::{title}";
                    }

                    return $"SW::<unknown>::{doc.GetHashCode()}";
                }
            }
            catch { }

            return "SW::<no-doc>";
        }
    }
}
