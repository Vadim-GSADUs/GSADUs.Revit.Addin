using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Windows;

namespace GSADUs.Revit.Addin.UI
{
    internal static class WorkflowManagerWindowHost
    {
        private static readonly Dictionary<string, WorkflowManagerWindow> _windows = new();

        public static WorkflowManagerWindow ShowOrActivate(Document? doc, Window? owner = null)
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

            WorkflowManagerWindow win;
            try
            {
                win = new WorkflowManagerWindow(doc, null);
            }
            catch (System.Exception ex)
            {
                try
                {
                    System.Diagnostics.Trace.WriteLine(ex.ToString());
                    MessageBox.Show(ex.ToString(), "WorkflowManagerWindowHost failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
