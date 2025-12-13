using System;

namespace GSADUs.Revit.Addin
{
    internal sealed class WorkflowCatalogChangeNotifier
    {
        private readonly object _gate = new();
        private event EventHandler? CatalogChanged;

        public IDisposable Subscribe(EventHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (_gate)
            {
                CatalogChanged += handler;
            }
            return new Subscription(this, handler);
        }

        public void NotifyChanged()
        {
            EventHandler? snapshot;
            lock (_gate)
            {
                snapshot = CatalogChanged;
            }
            snapshot?.Invoke(this, EventArgs.Empty);
        }

        private void Unsubscribe(EventHandler handler)
        {
            lock (_gate)
            {
                CatalogChanged -= handler;
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly WorkflowCatalogChangeNotifier _owner;
            private readonly EventHandler _handler;
            private bool _disposed;

            public Subscription(WorkflowCatalogChangeNotifier owner, EventHandler handler)
            {
                _owner = owner;
                _handler = handler;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _owner.Unsubscribe(_handler);
            }
        }
    }
}
