using Autodesk.Revit.UI;
using GSADUs.Revit.Addin.Orchestration;
using GSADUs.Revit.Addin.Workflows.Image;
using GSADUs.Revit.Addin.Workflows.Pdf; // Updated namespace for ExportPdfAction
using Microsoft.Extensions.DependencyInjection;
using System;
using GSADUs.Revit.Addin.Workflows.Rvt;

namespace GSADUs.Revit.Addin
{
    internal static class ServiceBootstrap
    {
        private static ServiceProvider? _provider;
        public static ServiceProvider Provider => _provider ??= ConfigureServices();

        private static ServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Coordinators (adapter over current static helper)
            services.AddSingleton<IBatchRunCoordinator, BatchRunCoordinatorAdapter>();

            // UI services
            services.AddSingleton<IDialogService, DialogService>();

            // Timers: expose as factory
            services.AddSingleton<Func<string, string, IOperationTimer>>(_ => (phase, ctx) => new PerfTimer(phase, ctx));

            // CSV batch log factory
            services.AddSingleton<IBatchLogFactory, CsvBatchLogger>();

            // Log sync
            services.AddSingleton<ILogSyncService, LogSyncService>();

            // Workflows (legacy command registry)
            services.AddSingleton<IWorkflow, BatchExportWorkflow>();
            services.AddSingleton<IWorkflowRegistry, WorkflowRegistry>();

            // Settings-driven workflow plan registry
            services.AddSingleton<IWorkflowPlanRegistry, WorkflowPlanRegistry>();

            // Actions registry (UI picks from here)
            services.AddSingleton<IActionRegistry, ActionRegistry>();

            // Built-in action implementations (execution)

            // In-place action stubs
            services.AddSingleton<IExportAction, ExportPdfAction>();
            services.AddSingleton<IExportAction, ExportImageAction>();
            services.AddSingleton<IExportAction, ExportRvtAction>();

            // Phase 2: settings persistence abstraction and catalog/presenter scaffolding
            services.AddSingleton<ISettingsPersistence, SettingsPersistence>();
            services.AddSingleton<WorkflowCatalogService>();
            services.AddSingleton<GSADUs.Revit.Addin.UI.WorkflowManagerPresenter>();

            return services.BuildServiceProvider();
        }
    }

    internal sealed class BatchRunCoordinatorAdapter : IBatchRunCoordinator
    {
        public Result Run(UIApplication uiapp, UIDocument uidoc)
        {
            // Persist current UIApplication for UI usage (PostCommand etc.)
            RevitUiContext.Current = uiapp;
            return BatchRunCoordinator.RunCore(uiapp, uidoc);
        }
    }
}
