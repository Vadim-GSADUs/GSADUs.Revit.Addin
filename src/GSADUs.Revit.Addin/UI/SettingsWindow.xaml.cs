using Autodesk.Revit.DB;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GSADUs.Revit.Addin.UI
{
    public partial class SettingsWindow : Window
    {
        // --- Singleton management ---
        private static SettingsWindow? _activeInstance;
        public static bool TryActivateExisting()
        {
            if (_activeInstance == null) return false;
            if (!_activeInstance.IsLoaded || !_activeInstance.IsVisible) return false;
            try
            {
                _activeInstance.Topmost = true;
                _activeInstance.Activate();
                _activeInstance.Topmost = false;
            }
            catch { }
            return true;
        }
        private void RegisterInstance()
        {
            _activeInstance = this;
            try { this.Closed += (_, __) => { if (ReferenceEquals(_activeInstance, this)) _activeInstance = null; }; } catch { }
        }
        // --- end singleton management ---

        private AppSettings _settings;
        private readonly Document? _doc; // optional for CategoryType resolution

        private List<int> _seedIds = new();
        private List<int> _proxyIds = new();
        private List<int> _cleanupBlacklistIds = new();

        // Staging whitelists
        private List<int> _stageWhitelistCatIds = new();
        private List<string> _stageWhitelistUids = new();

        public SettingsWindow() : this(new AppSettings(), null) { }
        public SettingsWindow(AppSettings settings) : this(settings, null) { }
        public SettingsWindow(AppSettings settings, Document? doc)
        {
            InitializeComponent();

            RegisterInstance();

            _settings = settings;
            _doc = doc;

            LogDirBox.Text = string.IsNullOrWhiteSpace(settings.LogDir) ? AppSettingsStore.FallbackLogDir : settings.LogDir;
            OutDirBox.Text = string.IsNullOrWhiteSpace(settings.DefaultOutputDir) ? AppSettingsStore.FallbackOutputDir : settings.DefaultOutputDir;
            RunAuditBox.IsChecked = settings.DefaultRunAuditBeforeExport;
            SaveBeforeBox.IsChecked = settings.DefaultSaveBefore;
            OverwriteBox.IsChecked = settings.DefaultOverwrite;
            DeepAnnoBox.IsChecked = settings.DeepAnnoStatus;
            DryrunDiagBox.IsChecked = settings.DryrunDiagnostics;
            PerfDiagBox.IsChecked = settings.PerfDiagnostics;
            OpenOutputFolderBox.IsChecked = settings.OpenOutputFolder; // unchanged
            ValidateStagingBox.IsChecked = settings.ValidateStagingArea; // now on Staging tab
            ChkDrawAmbiguousRectangles.IsChecked = settings.DrawAmbiguousRectangles; // new checkbox

            // Display-only paths
            try
            {
                var baseDir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "GSADUs", "Revit", "Addin");
                var defaultSettingsPath = System.IO.Path.Combine(baseDir, "settings.json");
                SettingsPathBox.Text = defaultSettingsPath;
            }
            catch { }
            try
            {
                string shared = string.Empty;
                try { shared = _doc?.Application?.SharedParametersFilename ?? string.Empty; } catch { shared = string.Empty; }
                if (string.IsNullOrWhiteSpace(shared)) shared = _settings.SharedParametersFilePath ?? string.Empty;
                SharedParamsPathBox.Text = shared;
            }
            catch { }

            // Selection tab init
            _seedIds = new List<int>(_settings.SelectionSeedCategories ?? new List<int> { (int)BuiltInCategory.OST_Walls, (int)BuiltInCategory.OST_Floors, (int)BuiltInCategory.OST_Roofs });
            _proxyIds = new List<int>(_settings.SelectionProxyCategories ?? new List<int>());
            _cleanupBlacklistIds = new List<int>(_settings.CleanupBlacklistCategories ?? new List<int>());
            ProxyDistanceBox.Text = (_settings.SelectionProxyDistance == 0 ? 1.0 : _settings.SelectionProxyDistance).ToString(System.Globalization.CultureInfo.InvariantCulture);
            UpdateSeedSummary();
            UpdateProxySummary();
            UpdateCleanupBlacklistSummary();

            // Staging tab init
            StageParamNameBox.Text = _settings.CurrentSetParameterName ?? "CurrentSet";
            StageWidthBox.Text = _settings.StagingWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
            StageHeightBox.Text = _settings.StagingHeight.ToString(System.Globalization.CultureInfo.InvariantCulture);
            StageBufferBox.Text = _settings.StagingBuffer.ToString(System.Globalization.CultureInfo.InvariantCulture);
            StageMoveModeCombo.ItemsSource = new[] { "CentroidToOrigin", "MinToOrigin" };
            StageMoveModeCombo.SelectedItem = string.IsNullOrWhiteSpace(_settings.StageMoveMode) ? "CentroidToOrigin" : _settings.StageMoveMode;

            _stageWhitelistCatIds = new List<int>((_settings.StagingAuthorizedCategoryNames ?? new List<string>()).Select(n => TryResolveBuiltInCategoryId(n, _doc)).Where(id => id != 0));
            _stageWhitelistUids = new List<string>(_settings.StagingAuthorizedUids ?? new List<string>());
            UpdateStageWhitelistCatSummary();
            UpdateStageWhitelistElemSummary();
        }

        private static int TryResolveBuiltInCategoryId(string? name, Document? doc)
        {
            if (string.IsNullOrWhiteSpace(name)) return 0;
            // Try enum name
            try { var bic = (BuiltInCategory)System.Enum.Parse(typeof(BuiltInCategory), name, true); return (int)bic; } catch { }
            // Try by display name using document
            try
            {
                if (doc != null)
                {
                    foreach (var bic in System.Enum.GetValues(typeof(BuiltInCategory)).Cast<BuiltInCategory>())
                    {
                        try { var cat = Category.GetCategory(doc, bic); if (cat != null && string.Equals(cat.Name, name, System.StringComparison.OrdinalIgnoreCase)) return (int)bic; } catch { }
                    }
                }
            }
            catch { }
            return 0;
        }

        private void UpdateSeedSummary()
        {
            SeedSummary.Text = _seedIds.Count == 0 ? "(none)" : string.Join(", ", _seedIds.Select(id => CategoryNameOrEnum((BuiltInCategory)id)));
        }
        private void UpdateProxySummary()
        {
            ProxySummary.Text = _proxyIds.Count == 0 ? "(none)" : string.Join(", ", _proxyIds.Select(id => CategoryNameOrEnum((BuiltInCategory)id)));
        }
        private void UpdateCleanupBlacklistSummary()
        {
            CleanupBlacklistSummary.Text = _cleanupBlacklistIds.Count == 0 ? "(none)" : string.Join(", ", _cleanupBlacklistIds.Select(id => CategoryNameOrEnum((BuiltInCategory)id)));
        }
        private void UpdateStageWhitelistCatSummary()
        {
            StageWhitelistCatSummary.Text = _stageWhitelistCatIds.Count == 0 ? "(none)" : string.Join(", ", _stageWhitelistCatIds.Select(id => CategoryNameOrEnum((BuiltInCategory)id)));
        }
        private void UpdateStageWhitelistElemSummary()
        {
            StageWhitelistElemSummary.Text = _stageWhitelistUids.Count == 0 ? "(none)" : string.Join(", ", _stageWhitelistUids.Take(3)) + (_stageWhitelistUids.Count > 3 ? $" (+{_stageWhitelistUids.Count - 3} more)" : string.Empty);
        }

        private string CategoryNameOrEnum(BuiltInCategory bic)
        {
            try { if (_doc != null) { var c = Category.GetCategory(_doc, bic); if (c != null && !string.IsNullOrWhiteSpace(c.Name)) return c.Name; } } catch { }
            var c2 = _doc != null ? Category.GetCategory(_doc, bic) : null;
            if (c2 == null) return bic.ToString().StartsWith("OST_") ? bic.ToString().Substring(4) : bic.ToString();
            return c2.Name;
        }

        private void PickSeed_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CategoriesPickerWindow(_seedIds, _doc, initialScope: 2) { Owner = this }; // Selection Set scope by default
            if (dlg.ShowDialog() == true)
            {
                _seedIds = dlg.ResultIds.ToList();
                UpdateSeedSummary();
            }
        }
        private void PickProxy_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CategoriesPickerWindow(_proxyIds, _doc, initialScope: 2) { Owner = this }; // Selection Set scope by default
            if (dlg.ShowDialog() == true)
            {
                _proxyIds = dlg.ResultIds.ToList();
                UpdateProxySummary();
            }
        }
        private void PickCleanupBlacklist_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CategoriesPickerWindow(_cleanupBlacklistIds, _doc, initialScope: 1) { Owner = this }; // Current File scope by default
            if (dlg.ShowDialog() == true)
            {
                _cleanupBlacklistIds = dlg.ResultIds.ToList();
                UpdateCleanupBlacklistSummary();
            }
        }

        private void PickStageWhitelistCategories_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CategoriesPickerWindow(_stageWhitelistCatIds, _doc, initialScope: 3) { Owner = this }; // Staging Area scope
            if (dlg.ShowDialog() == true)
            {
                _stageWhitelistCatIds = dlg.ResultIds.ToList();
                UpdateStageWhitelistCatSummary();
            }
        }

        private void PickStageWhitelistElements_Click(object sender, RoutedEventArgs e)
        {
            var win = new ElementsPickerWindow(_doc, _settings, _stageWhitelistUids) { Owner = this };
            if (win.ShowDialog() == true)
            {
                _stageWhitelistUids = win.ResultUids.ToList();
                UpdateStageWhitelistElemSummary();
            }
        }

        private void BrowseLog_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { CheckFileExists = false, FileName = "Select Folder" };
            if (dlg.ShowDialog() == true)
            {
                var dir = System.IO.Path.GetDirectoryName(dlg.FileName);
                if (!string.IsNullOrEmpty(dir)) LogDirBox.Text = dir;
            }
        }
        private void BrowseOut_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { CheckFileExists = false, FileName = "Select Folder" };
            if (dlg.ShowDialog() == true)
            {
                var dir = System.IO.Path.GetDirectoryName(dlg.FileName);
                if (!string.IsNullOrEmpty(dir)) OutDirBox.Text = dir;
            }
        }

        private void ManageWorkflowsBtn_Click(object sender, RoutedEventArgs e)
        {
            var win = new WorkflowManagerWindow(_doc, AppSettingsStore.Load()) { Owner = this };
            try { win.ShowDialog(); } catch { }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            _settings.SelectionSeedCategories = _seedIds.Distinct().ToList();
            _settings.SelectionProxyCategories = _proxyIds.Distinct().ToList();
            // Always include <Sketch> (-2000045) in CleanupBlacklistCategories
            const int SketchCategoryId = -2000045;
            var blacklist = _cleanupBlacklistIds.Distinct().ToList();
            if (!blacklist.Contains(SketchCategoryId))
                blacklist.Add(SketchCategoryId);
            _settings.CleanupBlacklistCategories = blacklist;
            if (double.TryParse(ProxyDistanceBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
                _settings.SelectionProxyDistance = d;

            _settings.LogDir = string.IsNullOrWhiteSpace(LogDirBox.Text) ? null : LogDirBox.Text;
            _settings.DefaultOutputDir = string.IsNullOrWhiteSpace(OutDirBox.Text) ? null : OutDirBox.Text;
            _settings.DefaultRunAuditBeforeExport = RunAuditBox.IsChecked == true;
            _settings.DefaultSaveBefore = SaveBeforeBox.IsChecked == true;
            _settings.DefaultOverwrite = OverwriteBox.IsChecked == true;
            _settings.DeepAnnoStatus = DeepAnnoBox.IsChecked == true;
            _settings.DryrunDiagnostics = DryrunDiagBox.IsChecked == true;
            _settings.PerfDiagnostics = PerfDiagBox.IsChecked == true;
            _settings.OpenOutputFolder = OpenOutputFolderBox.IsChecked == true; // new
            _settings.ValidateStagingArea = ValidateStagingBox.IsChecked == true; // new
            _settings.DrawAmbiguousRectangles = ChkDrawAmbiguousRectangles.IsChecked == true; // persist new flag

            // Staging
            _settings.CurrentSetParameterName = string.IsNullOrWhiteSpace(StageParamNameBox.Text) ? "CurrentSet" : StageParamNameBox.Text.Trim();
            if (double.TryParse(StageWidthBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w)) _settings.StagingWidth = w;
            if (double.TryParse(StageHeightBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h)) _settings.StagingHeight = h;
            if (double.TryParse(StageBufferBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b)) _settings.StagingBuffer = b;
            _settings.StageMoveMode = StageMoveModeCombo.SelectedItem as string ?? "CentroidToOrigin";

            _settings.StagingAuthorizedCategoryNames = _stageWhitelistCatIds
                .Select(id => CategoryNameOrEnum((BuiltInCategory)id))
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            _settings.StagingAuthorizedUids = _stageWhitelistUids.Distinct(System.StringComparer.OrdinalIgnoreCase).ToList();

            AppSettingsStore.Save(_settings);
            DialogResult = true;
        }
    }
}
