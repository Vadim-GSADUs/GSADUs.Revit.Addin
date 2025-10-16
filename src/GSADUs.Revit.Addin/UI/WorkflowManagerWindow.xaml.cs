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
            _settings = _catalog.Settings;
            _doc = doc;

            _presenter.OnWindowConstructed(this);

            try { (FindName("PdfTabRoot") as FrameworkElement)!.DataContext = _presenter.PdfWorkflow; } catch { }
            try { (FindName("ImageTabRoot") as FrameworkElement)!.DataContext = _presenter.ImageWorkflow; } catch { }

            RefreshMainList();
            RefreshSavedCombos();

            this.Loaded += WorkflowManagerWindow_Loaded;
        }

        private void WorkflowManagerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var uiDoc = RevitUiContext.Current?.ActiveUIDocument;
                var doc = _doc ?? uiDoc?.Document;

                _presenter.OnLoaded(uiDoc, this);

                if (doc == null || doc.IsFamilyDocument)
                {
                    TryDisablePdfControls();
                    return;
                }

                try { _presenter.PopulateImageSources(doc); } catch { }
                try { _presenter.PopulatePdfSources(doc); } catch { }
            }
            catch { }
        }

        private void TryDisablePdfControls()
        {
            try
            {
                foreach (var n in new[] { "ViewSetCombo", "ExportSetupCombo", "FileNamePatternBox", "PdfSaveBtn" })
                {
                    if (FindName(n) is System.Windows.Controls.Control c) { c.IsEnabled = false; }
                }
                var outLbl = FindName("PdfOutFolderLabel") as Label; if (outLbl != null) outLbl.Content = "(no project open)";
                var overLbl = FindName("PdfOverwriteLabel") as Label; if (overLbl != null) overLbl.Content = string.Empty;
            }
            catch { }
        }

        private void RefreshMainList()
        {
            try
            {
                var rows = (_catalog.Workflows ?? new System.Collections.ObjectModel.ObservableCollection<WorkflowDefinition>())
                    .Select(w => new { w.Id, w.Name, w.Output, w.Scope, w.Description })
                    .ToList();
                var cvs = FindResource("WorkflowsView") as CollectionViewSource;
                if (cvs != null) cvs.Source = rows;
            }
            catch { }
        }

        private void RefreshSavedCombos()
        {
            try
            {
                var all = _catalog.Workflows?.ToList() ?? new List<WorkflowDefinition>();
                var mk = static (WorkflowDefinition w) => new { Id = w.Id, Display = $"{w.Name} - {w.Scope} - {w.Description}" };
                (FindName("PdfSavedCombo") as ComboBox)!.ItemsSource = all.Where(w => w.Output == OutputType.Pdf).Select(mk).ToList();
                (FindName("ImageSavedCombo") as ComboBox)!.ItemsSource = all.Where(w => w.Output == OutputType.Image).Select(mk).ToList();
            }
            catch { }
        }

        private void WorkflowsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                var lv = sender as ListView; if (lv?.SelectedItem == null) return;
                var id = lv.SelectedItem.GetType().GetProperty("Id")?.GetValue(lv.SelectedItem) as string;
                var wf = (_catalog.Settings.Workflows ?? new List<WorkflowDefinition>()).FirstOrDefault(w => w.Id == id);
                if (wf == null) return;
                var tabIndex = wf.Output switch { OutputType.Pdf => 1, OutputType.Image => 2, _ => 0 };
                (Tabs as TabControl)!.SelectedIndex = tabIndex;
                string comboName = wf.Output switch { OutputType.Pdf => "PdfSavedCombo", OutputType.Image => "ImageSavedCombo", _ => string.Empty };
                var items = (FindName(comboName) as ComboBox)?.ItemsSource as System.Collections.IEnumerable;
                if (items != null)
                {
                    foreach (var it in items)
                    {
                        var itId = it.GetType().GetProperty("Id")?.GetValue(it) as string;
                        if (string.Equals(itId, wf.Id, StringComparison.OrdinalIgnoreCase)) { (FindName(comboName) as ComboBox)!.SelectedItem = it; break; }
                    }
                }
            }
            catch { }
        }

        private void PdfManageSetupBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var uiapp = RevitUiContext.Current; if (uiapp == null) { _dialogs.Info("PDF", "Revit UI not available."); return; }
                var cmd = Autodesk.Revit.UI.RevitCommandId.LookupPostableCommandId(Autodesk.Revit.UI.PostableCommand.ExportPDF);
                if (cmd != null && uiapp.CanPostCommand(cmd)) uiapp.PostCommand(cmd);
            }
            catch { }
        }

        private void SaveWorkflow_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as FrameworkElement; if (btn == null) return;
            var tag = (btn.Tag as string) ?? string.Empty;
            if (tag == "Png") tag = "Image";

            WorkflowTabBaseViewModel? baseVm = tag == "Pdf" ? _presenter.PdfWorkflow : _presenter.ImageWorkflow;

            var nameVal = baseVm?.Name?.Trim() ?? string.Empty;
            var scopeVal = baseVm?.WorkflowScope ?? string.Empty;
            var descVal = baseVm?.Description ?? string.Empty;

            if (string.IsNullOrWhiteSpace(nameVal) || string.IsNullOrWhiteSpace(scopeVal)) { _dialogs.Info("Save", "Name and Scope required."); return; }

            var output = tag switch { "Pdf" => OutputType.Pdf, _ => OutputType.Image };

            // locate existing via VM SelectedWorkflowId
            var selectedId = tag == "Pdf" ? _presenter.PdfWorkflow.SelectedWorkflowId : _presenter.ImageWorkflow.SelectedWorkflowId;
            var existing = (_catalog.Settings.Workflows ?? new List<WorkflowDefinition>()).FirstOrDefault(w => string.Equals(w.Id, selectedId, StringComparison.OrdinalIgnoreCase) && w.Output == output);
            if (existing == null)
            {
                existing = new WorkflowDefinition { Id = Guid.NewGuid().ToString("N"), Kind = WorkflowKind.Internal, Output = output, ActionIds = new List<string>(), Parameters = new Dictionary<string, JsonElement>() };
                _catalog.Settings.Workflows ??= new List<WorkflowDefinition>();
                _catalog.Settings.Workflows.Add(existing);
                if (tag == "Pdf") _presenter.PdfWorkflow.SelectedWorkflowId = existing.Id; else _presenter.ImageWorkflow.SelectedWorkflowId = existing.Id;
            }

            existing.Name = nameVal; existing.Scope = scopeVal; existing.Description = descVal;

            switch (output)
            {
                case OutputType.Pdf:
                    if (!_presenter.SavePdfWorkflow(existing)) return; _presenter.PdfWorkflow.SetDirty(false); break;
                case OutputType.Image:
                    _presenter.SaveImageWorkflow(existing); _presenter.ImageWorkflow.SetDirty(false); break;
            }

            _catalog.SaveAndRefresh();
            RefreshMainList();
            RefreshSavedCombos();
        }
    }
}
