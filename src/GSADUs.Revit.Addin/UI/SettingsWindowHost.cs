using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Windows;

namespace GSADUs.Revit.Addin.UI
{
    internal static class SettingsWindowHost
    {
        private static readonly Dictionary<string, SettingsWindow> _windows = new();

        public static SettingsWindow ShowOrActivate(Document? doc, Window? owner = null)
        {
            var key = GetKey(doc);
            if (_windows.TryGetValue(key, out var existing))
            {
                if (existing != null && existing.IsLoaded)
                {
                    try
                    {
                        if (!existing.IsVisible) existing.Show();
                        existing.Activate();
                        existing.Topmost = true;
                        existing.Topmost = false;
                        return existing;
                    }
                    catch { }
                }
            }

            SettingsWindow win;
            try
            {
                win = new SettingsWindow(null, doc);
            }
            catch (System.Exception ex)
            {
                try
                {
                    System.Diagnostics.Trace.WriteLine(ex.ToString());
                    MessageBox.Show(ex.ToString(), "SettingsWindowHost failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
                throw;
            }
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
            try
            {
                win.Activate();
                win.Topmost = true;
                win.Topmost = false;
            }
            catch { }

            return win;
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
