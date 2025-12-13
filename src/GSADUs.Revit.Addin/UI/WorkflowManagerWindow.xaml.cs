using Autodesk.Revit.DB;
using GSADUs.Revit.Addin.Abstractions;
using GSADUs.Revit.Addin.Infrastructure;
using GSADUs.Revit.Addin.Workflows.Image;
using GSADUs.Revit.Addin.Workflows.Pdf;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Diagnostics;
using System.Windows.Threading;

namespace GSADUs.Revit.Addin.UI
{
    public partial class WorkflowManagerWindow : Window
    {
        private static WorkflowManagerWindow? _activeInstance;
        private GridViewColumnHeader? _workflowsLastHeader;
        private ListSortDirection _workflowsLastDirection = ListSortDirection.Ascending;
        public static bool TryActivateExisting()
        {
            if (_activeInstance == null) return false;
            if (!_activeInstance.IsLoaded || !_activeInstance.IsVisible) return false;
            _activeInstance.Topmost = true; _activeInstance.Activate(); _activeInstance.Topmost = false;
            return true;
        }
        private void RegisterInstance()
        {
            _activeInstance = this;
            this.Closed += (_, __) => { if (ReferenceEquals(_activeInstance, this)) _activeInstance = null; };
        }

        private readonly WorkflowCatalogService _catalog;
        private readonly WorkflowManagerPresenter _presenter;
        private readonly WorkflowManagerViewModel _vm;
        private AppSettings _settings;
        private readonly IDialogService _dialogs;
        private readonly Document? _doc;

        public WorkflowManagerWindow(AppSettings? settings = null) : this(null, settings) { }
        public WorkflowManagerWindow(Document? doc, AppSettings? settings = null)
        {
            System.Windows.Application.LoadComponent(this, new Uri("/GSADUs.Revit.Addin;component/UI/WorkflowManagerWindow.xaml", UriKind.Relative));

            RegisterInstance();

            _dialogs = ServiceBootstrap.Provider.GetService(typeof(IDialogService)) as IDialogService ?? new DialogService();
            _catalog = CreateCatalog(doc);
            var notifier = ServiceBootstrap.Provider.GetService(typeof(WorkflowCatalogChangeNotifier)) as WorkflowCatalogChangeNotifier
                           ?? new WorkflowCatalogChangeNotifier();
            _presenter = new WorkflowManagerPresenter(_catalog, _dialogs, notifier);
            _vm = new WorkflowManagerViewModel(_catalog, _presenter);
            _settings = _catalog.Settings;
            _doc = doc;

            DataContext = _vm;
            InitializeWorkflowsListSorting();

            if (DataContext is WorkflowManagerViewModel vm)
                System.Diagnostics.Trace.WriteLine($"[Window] DataContext.Image VM instance: {vm.Image.GetHashCode()}");

            // Log validation errors globally for faster diagnostics
            this.AddHandler(Validation.ErrorEvent, new EventHandler<ValidationErrorEventArgs>((s, e) =>
            {
                try
                {
                    var be = e.Error?.BindingInError as BindingExpression;
                    var path = be?.ParentBinding?.Path?.Path ?? string.Empty;
                    Debug.WriteLine($"Validation error on '{path}': {e.Error?.ErrorContent}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }));

            _presenter.OnWindowConstructed(this);

            this.Loaded += WorkflowManagerWindow_Loaded;

            // CSV wiring now handled inside presenter.WireCsv(), invoked by presenter ctor
        }

        private WorkflowCatalogService CreateCatalog(Document? doc)
        {
            if (doc != null)
            {
                return new WorkflowCatalogService(new EsProjectSettingsProvider(() => doc));
            }

            var resolved = ServiceBootstrap.Provider.GetService(typeof(WorkflowCatalogService)) as WorkflowCatalogService;
            if (resolved != null)
            {
                return resolved;
            }

            return new WorkflowCatalogService(new EsProjectSettingsProvider(() => RevitUiContext.Current?.ActiveUIDocument?.Document));
        }

        // Helper to require named element lookups
        private T GetRequired<T>(string name) where T : FrameworkElement
        {
            var el = this.FindName(name) as T;
            if (el == null) throw new InvalidOperationException($"Missing required control: {name}");
            return el;
        }

        private void WorkflowManagerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var uidoc = RevitUiContext.Current?.ActiveUIDocument;
                // Single auto-refresh on open for all tabs (PDF/Image/CSV)
                _presenter.OnLoaded(uidoc, this);
            }
            catch { }
        }

        private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void WorkflowsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void SaveCloseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.IsEnabled = false;
            }

            var dispatcher = Dispatcher;
            var fallbackTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };

            void CompleteClose(bool success)
            {
                fallbackTimer.Stop();
                if (sender is Button sourceBtn)
                {
                    sourceBtn.IsEnabled = true;
                }

                if (!success)
                {
                    _dialogs.Info("Save Workflows", "Unable to persist workflow changes. See log for details.");
                }

                Close();
            }

            fallbackTimer.Tick += (_, _) =>
            {
                dispatcher?.BeginInvoke(new Action(() => CompleteClose(success: true)));
            };
            fallbackTimer.Start();

            _presenter.SaveSettings(success =>
            {
                dispatcher?.BeginInvoke(new Action(() => CompleteClose(success)));
            });
        }

        private void InitializeWorkflowsListSorting()
        {
            try
            {
                var list = GetRequired<ListView>("WorkflowsList");
                list.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(WorkflowsListHeader_Click));
            }
            catch { }
        }

        private void WorkflowsListHeader_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not GridViewColumnHeader header || header.Tag == null)
                return;

            var sortBy = header.Tag.ToString();
            if (string.IsNullOrWhiteSpace(sortBy)) return;

            var direction = (_workflowsLastHeader == header && _workflowsLastDirection == ListSortDirection.Ascending)
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            ApplyWorkflowSort(sortBy, direction);
            _workflowsLastHeader = header;
            _workflowsLastDirection = direction;
        }

        private void ApplyWorkflowSort(string sortBy, ListSortDirection direction)
        {
            try
            {
                var list = GetRequired<ListView>("WorkflowsList");
                var view = CollectionViewSource.GetDefaultView(list.ItemsSource);
                if (view == null) return;

                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription(sortBy, direction));
                view.Refresh();
            }
            catch { }
        }
    }
}
