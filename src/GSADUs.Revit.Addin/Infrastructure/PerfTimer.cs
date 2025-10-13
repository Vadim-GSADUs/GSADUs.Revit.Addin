namespace GSADUs.Revit.Addin
{
    internal sealed class PerfTimer : IOperationTimer
    {
        private readonly PerfLogger.Scope _scope;
        public PerfTimer(string phase, string context)
        {
            _scope = PerfLogger.Measure(phase, context);
        }
        public void Dispose() => _scope?.Dispose();
    }
}
