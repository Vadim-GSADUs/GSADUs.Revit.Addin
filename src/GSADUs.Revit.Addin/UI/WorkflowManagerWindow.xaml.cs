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

namespace GSADUs.Revit.Addin.UI
{
    public partial class WorkflowManagerWindow : Window
    {
        private static WorkflowManagerWindow? _activeInstance;
        public static bool TryActivateExisting()
        {
            if (_activeInstance == null) return false;
            if (!_activeInstance.IsLoaded || !_activeInstance.IsVisible) return false;
            try { _activeInstance.Topmost = true; _activeInstance.Activate(); _activeInstance.Topmost = false; } catch { }
            return true;
        }
        private void RegisterInstance()
        {
            _activeInstance = this;
            try { this.Closed += (_, __) => { if (ReferenceEquals(_activeInstance, this)) _activeInstance = null; }; } catch { }
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

            _presenter.OnWindowConstructed(this);

            this.Loaded += WorkflowManagerWindow_Loaded;
        }

        private void WorkflowManagerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var uiDoc = RevitUiContext.Current?.ActiveUIDocument;
                var doc = _doc ?? uiDoc?.Document;

                _presenter.OnLoaded(uiDoc, this);

                if (doc != null)
                {
                    try { _presenter.PopulateImageSources(doc); } catch { }
                    try { _presenter.PopulatePdfSources(doc); } catch { }
                }
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
            try { _presenter.SaveSettings(); } catch { }
            try { this.Close(); } catch { }
        }
    }
}
