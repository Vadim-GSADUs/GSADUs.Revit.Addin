using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Threading;
using Autodesk.Revit.UI;

namespace GSADUs.Revit.Addin.UI
{
    internal sealed class ProjectSettingsSaveExternalEvent : IExternalEventHandler
    {
        private readonly WorkflowCatalogService _catalog;
        private readonly Dispatcher _dispatcher;
        private readonly ExternalEvent _externalEvent;
        private readonly object _sync = new();
        private readonly Queue<Action<bool>> _callbacks = new();
        private AppSettings? _pendingSnapshot;

        public ProjectSettingsSaveExternalEvent(WorkflowCatalogService catalog, Dispatcher dispatcher)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _externalEvent = ExternalEvent.Create(this);
        }

        public void RequestSave(AppSettings snapshot, Action<bool>? onCompleted = null)
        {
            if (snapshot == null)
            {
                onCompleted?.Invoke(false);
                return;
            }

            lock (_sync)
            {
                // Latest snapshot wins; callers expect the most recent state to be persisted.
                _pendingSnapshot = DeepClone(snapshot);

                if (onCompleted != null)
                {
                    _callbacks.Enqueue(onCompleted);
                }
            }

            _externalEvent.Raise();
        }

        public void Execute(UIApplication app)
        {
            bool success = false;
            AppSettings? snapshot;
            Action<bool>[] callbacks;

            // Capture and clear the pending snapshot + callbacks under a single lock to avoid
            // races where callbacks are enqueued during Execute.
            lock (_sync)
            {
                snapshot = _pendingSnapshot;
                _pendingSnapshot = null;
                callbacks = _callbacks.ToArray();
                _callbacks.Clear();
            }

            try
            {
                if (snapshot != null)
                {
                    // Apply the snapshot onto the catalog's settings inside API
                    // context so the persisted state matches exactly what the
                    // caller requested. Deep-clone again to ensure isolation
                    // between the catalog and any remaining UI references.
                    var applied = DeepClone(snapshot);
                    _catalog.ApplySettings(applied);
                }

                _catalog.Save(force: true);
                success = true;
            }
            catch (Exception ex)
            {
                // TEMP: surface underlying save exception for diagnostics.
                var details = ex.ToString();
                Trace.WriteLine(ex);
                try
                {
                    // Do not block the Revit API thread with UI; dispatch to the WPF dispatcher.
                    _dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            System.Windows.MessageBox.Show(
                                details,
                                "Project Settings Save Failed",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Error);
                        }
                        catch
                        {
                            // Swallow any UI exceptions; failure is still reported via success flag and trace.
                        }
                    }));
                }
                catch
                {
                    // Swallow any dispatcher exceptions; failure is still reported via success flag and trace.
                }
            }

            Trace.WriteLine(success
                ? "[SettingsSave] Extensible Storage save completed."
                : "[SettingsSave] Extensible Storage save failed.");

            if (callbacks.Length == 0)
            {
                return;
            }

            foreach (var cb in callbacks)
            {
                if (cb == null) continue;
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        cb(success);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine(ex);
                    }
                }));
            }
        }

        public string GetName() => "Project Settings Save";

        // Defensive deep clone of AppSettings so later UI mutations cannot affect the
        // snapshot that will be persisted by the ExternalEvent.
        private static AppSettings DeepClone(AppSettings source)
        {
            try
            {
                var json = JsonSerializer.Serialize(source);
                var clone = JsonSerializer.Deserialize<AppSettings>(json);
                return clone ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }
    }
}
