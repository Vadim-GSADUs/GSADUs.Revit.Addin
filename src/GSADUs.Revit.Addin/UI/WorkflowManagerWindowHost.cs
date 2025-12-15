using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Windows;

namespace GSADUs.Revit.Addin.UI
{
    internal static class WorkflowManagerWindowHost
    {
        private static readonly Dictionary<string, WorkflowManagerWindow> _windows = new();

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

            var win = new WorkflowManagerWindow(doc, null);
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
                        return $"WM::{path}";
                    }

                    var title = doc.Title;
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        return $"WM::{title}";
                    }

                    return $"WM::<unknown>::{doc.GetHashCode()}";
                }
            }
            catch { }

            return "WM::<no-doc>";
        }
    }
}
