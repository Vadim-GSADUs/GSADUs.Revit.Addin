using Autodesk.Revit.UI;
using System;

namespace GSADUs.Revit.Addin.Orchestration
{
    internal sealed class BatchRunExternalEvent : IExternalEventHandler
    {
        private readonly object _gate = new();
        private ExternalEvent? _evt;
        private Action<UIApplication>? _pending;

        public BatchRunExternalEvent()
        {
        }

        public void Initialize(UIApplication uiapp)
        {
            if (_evt != null) return;
            // ExternalEvent.Create must be called from a valid Revit API context.
            // We opportunistically create it during IExternalCommand execution (BatchExportCommand)
            // and avoid creating it from modeless WPF callbacks.
            _evt = ExternalEvent.Create(this);
        }

        public void Raise(Action<UIApplication> work)
        {
            if (work == null) return;
            lock (_gate)
            {
                _pending = work;
            }
            _evt?.Raise();
        }

        public void Execute(UIApplication app)
        {
            Action<UIApplication>? work;
            lock (_gate)
            {
                work = _pending;
                _pending = null;
            }

            try { work?.Invoke(app); } catch { }
        }

        public string GetName() => "Batch Run ExternalEvent";
    }
}
