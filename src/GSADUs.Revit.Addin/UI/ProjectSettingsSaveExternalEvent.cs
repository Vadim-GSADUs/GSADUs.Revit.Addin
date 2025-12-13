using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        public ProjectSettingsSaveExternalEvent(WorkflowCatalogService catalog, Dispatcher dispatcher)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _externalEvent = ExternalEvent.Create(this);
        }

        public void RequestSave(Action<bool>? onCompleted = null)
        {
            lock (_sync)
            {
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
            try
            {
                _catalog.Save(force: true);
                success = true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
            }

            Trace.WriteLine(success
                ? "[SettingsSave] Extensible Storage save completed."
                : "[SettingsSave] Extensible Storage save failed.");

            Action<bool>[] callbacks;
            lock (_sync)
            {
                callbacks = _callbacks.ToArray();
                _callbacks.Clear();
            }

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
    }
}
