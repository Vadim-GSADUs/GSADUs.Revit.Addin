using Autodesk.Revit.UI;
using GSADUs.Revit.Addin.Abstractions;
using GSADUs.Revit.Addin.Infrastructure;
using GSADUs.Revit.Addin.Orchestration;
using GSADUs.Revit.Addin.Workflows.Image;
using GSADUs.Revit.Addin.Workflows.Pdf; // Updated namespace for ExportPdfAction
using Microsoft.Extensions.DependencyInjection;
using System;
using GSADUs.Revit.Addin.Workflows.Rvt;
using GSADUs.Revit.Addin.Workflows.Csv;

namespace GSADUs.Revit.Addin
{
    internal static class ServiceBootstrap
    {
        private static ServiceProvider? _provider;
		private static readonly object _sync = new();

		public static ServiceProvider Provider
		{
			get
			{
				if (_provider == null)
				{
					throw new InvalidOperationException("ServiceBootstrap not initialized.");
				}
				return _provider;
			}
		}

		public static void InitializeOrThrow(UIApplication uiapp)
		{
			if (_provider != null) return;
			if (uiapp == null) throw new ArgumentNullException(nameof(uiapp));
			lock (_sync)
			{
				if (_provider != null) return;
				_provider = ConfigureServices();
				System.Diagnostics.Trace.WriteLine("ServiceBootstrap initialized");
			}
		}

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

            services.AddSingleton<WorkflowCatalogChangeNotifier>();
            // Actions registry (UI picks from here)
            services.AddSingleton<IActionRegistry, ActionRegistry>();

            // Built-in action implementations (execution)
            services.AddSingleton<IExportAction, ExportPdfAction>();
            services.AddSingleton<IExportAction, ExportImageAction>();
            services.AddSingleton<IExportAction, ExportRvtAction>();
            services.AddSingleton<IExportAction, ExportCsvAction>();

            // Phase 2: settings persistence abstraction and catalog/presenter scaffolding
            services.AddSingleton<ISettingsPersistence, SettingsPersistence>();
            services.AddSingleton<IProjectSettingsProvider, EsProjectSettingsProvider>();

            // Project settings save external event: create once in valid Revit API context
            services.AddSingleton<GSADUs.Revit.Addin.UI.ProjectSettingsSaveExternalEvent>(sp =>
            {
                var catalog = sp.GetRequiredService<WorkflowCatalogService>();
                var dispatcher = System.Windows.Application.Current?.Dispatcher
                                 ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
                // ExternalEvent.Create will run here during startup, under a valid UIApplication context
                return new GSADUs.Revit.Addin.UI.ProjectSettingsSaveExternalEvent(catalog, dispatcher);
            });

            services.AddSingleton<IProjectSettingsSaveService, ProjectSettingsSaveService>();
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
			ServiceBootstrap.InitializeOrThrow(uiapp);
            RevitUiContext.Current = uiapp;
            return BatchRunCoordinator.RunCore(uiapp, uidoc);
        }
    }
}
