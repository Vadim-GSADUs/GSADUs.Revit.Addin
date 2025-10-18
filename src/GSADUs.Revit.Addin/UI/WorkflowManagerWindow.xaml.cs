using Autodesk.Revit.DB;
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

namespace GSADUs.Revit.Addin.UI
{
    public partial class WorkflowManagerWindow : Window
    {
        private static WorkflowManagerWindow? _activeInstance;
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
            _catalog = ServiceBootstrap.Provider.GetService(typeof(WorkflowCatalogService)) as WorkflowCatalogService
                       ?? new WorkflowCatalogService(new SettingsPersistence());
            _presenter = ServiceBootstrap.Provider.GetService(typeof(WorkflowManagerPresenter)) as WorkflowManagerPresenter
                         ?? new WorkflowManagerPresenter(_catalog, _dialogs);
            _vm = new WorkflowManagerViewModel(_catalog, _presenter);
            _settings = _catalog.Settings;
            _doc = doc;

            DataContext = _vm;

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
            var uiDoc = RevitUiContext.Current?.ActiveUIDocument;

            _presenter.OnLoaded(uiDoc, this);
        }

        private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void WorkflowsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void SaveCloseBtn_Click(object sender, RoutedEventArgs e)
        {
            _presenter.SaveSettings();
            this.Close();
        }
    }
}
