using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace GSADUs.Revit.Addin.UI
{
    public partial class CategoriesPickerWindow : Window
    {
        // --- Singleton management ---
        private static CategoriesPickerWindow? _activeInstance;
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
        // --- end singleton management ---

        public IReadOnlyList<int> ResultIds { get; private set; } = new List<int>();

        private readonly List<Item> _allItems = new(); // full pool based on chosen scope
        private readonly List<Item> _items = new();     // active view (after discipline filter)
        private ListCollectionView? _view;
        private readonly Document? _doc;

        public CategoriesPickerWindow(IEnumerable<int>? preselected = null, Document? doc = null, int? initialScope = null)
        {
            InitializeComponent();
            RegisterInstance();
            _doc = doc;
            try
            {
                // Defaults
                if (initialScope.HasValue)
                    ScopeCombo.SelectedIndex = initialScope.Value;
                else
                    ScopeCombo.SelectedIndex = 1; // Current File Categories Only
                DisciplineCombo.SelectedIndex = 0; // Show All

                RebuildScope(preselected ?? System.Linq.Enumerable.Empty<int>());
                ApplyDisciplineFilter();
                Bind();
            }
            catch (System.Exception ex)
            {
                // Fail safe: degrade to enum list so the UI still opens
                try { MessageBox.Show(this, "Category picker failed to load document categories. Falling back to enum list.\n\n" + ex.Message, "Categories", MessageBoxButton.OK, MessageBoxImage.Warning); } catch { }
                try
                {
                    _allItems.Clear();
                    foreach (var bic in System.Enum.GetValues(typeof(BuiltInCategory)).Cast<BuiltInCategory>())
                    {
                        var name = bic.ToString();
                        _allItems.Add(new Item { EnumName = name, DisplayName = name, Value = (int)bic, Group = "Model" });
                    }
                    ApplyDisciplineFilter();
                    Bind();
                }
                catch { }
            }
        }

        // Build the base list according to scope
        private void RebuildScope(IEnumerable<int> preselected)
        {
            try
            {
                var sel = new HashSet<int>(preselected ?? Enumerable.Empty<int>());
                _allItems.Clear();

                int scope = 1;
                try { scope = ScopeCombo?.SelectedIndex ?? 1; } catch { scope = 1; }

                // Always compute selection set categories once to enforce subset relation (Selection Set ? Project ? Revit)
                IEnumerable<BuiltInCategory> setCats = Enumerable.Empty<BuiltInCategory>();
                if (_doc != null)
                {
                    try { setCats = SelectionSetCategoryCache.GetOrBuild(_doc).Select(i => (BuiltInCategory)i); } catch { setCats = Enumerable.Empty<BuiltInCategory>(); }
                }

                IEnumerable<BuiltInCategory> source;
                if (scope == 0)
                {
                    // Revit (global) - all categories in this Revit API version
                    source = System.Enum.GetValues(typeof(BuiltInCategory)).Cast<BuiltInCategory>();
                }
                else if (scope == 2 && _doc != null)
                {
                    // Selection Set - union with preselected so saved/default picks stay visible regardless of scope
                    source = setCats;
                }
                else if (scope == 3 && _doc != null)
                {
                    // Staging area categories only: based on elements inside the defined staging outline
                    var s = AppSettingsStore.Load();
                    double w = System.Math.Max(1.0, s.StagingWidth);
                    double h = System.Math.Max(1.0, s.StagingHeight);
                    double buffer = System.Math.Max(0.0, s.StagingBuffer);
                    double halfW = w * 0.5 + buffer;
                    double halfH = h * 0.5 + buffer;

                    var min = new XYZ(-halfW, -halfH, double.NegativeInfinity);
                    var max = new XYZ(halfW, halfH, double.PositiveInfinity);
                    var outline = new Outline(min, max);
                    var bbFilter = new BoundingBoxIntersectsFilter(outline);

                    var cats = new HashSet<BuiltInCategory>();
                    try
                    {
                        foreach (var e in new FilteredElementCollector(_doc)
                            .WhereElementIsNotElementType()
                            .WherePasses(bbFilter)
                            .ToElements())
                        {
                            try
                            {
                                var cat = e.Category;
                                if (cat != null && cat.BuiltInCategory != BuiltInCategory.INVALID) cats.Add(cat.BuiltInCategory);
                            }
                            catch { }
                        }
                    }
                    catch { }
                    source = cats;
                }
                else
                {
                    // Project - categories in use
                    var used = new HashSet<BuiltInCategory>();
                    if (_doc != null)
                    {
                        foreach (var bic in System.Enum.GetValues(typeof(BuiltInCategory)).Cast<BuiltInCategory>())
                        {
                            Category? cat = null;
                            try { cat = Category.GetCategory(_doc, bic); } catch { cat = null; }
                            if (cat == null) continue;
                            try
                            {
                                bool any = new FilteredElementCollector(_doc)
                                    .OfCategoryId(cat.Id)
                                    .WhereElementIsNotElementType()
                                    .Take(1)
                                    .Any();
                                if (any) used.Add(bic);
                            }
                            catch { /* ignore */ }
                        }
                    }
                    // Ensure Selection Set categories are always available within Project scope
                    source = used.Concat(setCats).Distinct();
                }

                // Ensure preselected categories are always present in the list, regardless of scope choice
                source = source.Concat(sel.Select(i => (BuiltInCategory)i)).Distinct();

                foreach (var bic in source)
                {
                    Category? cat = null;
                    try { if (_doc != null) cat = Category.GetCategory(_doc, bic); } catch { cat = null; }

                    var enumName = bic.ToString();
                    var displayName = cat != null && !string.IsNullOrWhiteSpace(cat.Name) ? cat.Name : enumName;
                    var ctype = cat?.CategoryType ?? CategoryType.Model;
                    string group = ctype == CategoryType.Annotation ? "Annotation" : "Model";
                    if (IsImported(enumName, cat)) group = "Imported";

                    int key = (int)bic;
                    _allItems.Add(new Item
                    {
                        EnumName = enumName,
                        DisplayName = displayName,
                        Value = key,
                        Group = group,
                        IsSelected = sel.Contains(key)
                    });
                }
            }
            catch { /* swallow to avoid Revit fatal when API throws */ }
        }

        private static bool IsImported(string enumName, Category? cat)
        {
            if (enumName.Contains("Import", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (enumName.Contains("Link", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (enumName.Contains("Raster", System.StringComparison.OrdinalIgnoreCase)) return true;
            try { if (cat?.Parent != null && cat.Parent.Name.IndexOf("Import", System.StringComparison.OrdinalIgnoreCase) >= 0) return true; } catch { }
            return false;
        }

        private void Bind()
        {
            _view = new ListCollectionView(_items);
            _view.Filter = Filter;
            ItemsList.ItemsSource = _view;
        }

        // Discipline coarse map by name heuristics
        private static readonly (string key, string[] tokens)[] DisciplineTokens = new[]
        {
            ("Architectural", new[]{ "Wall", "Door", "Window", "Ceiling", "Floor", "Roof", "Room", "Stair", "Railing", "Curtain", "Furniture", "Casework", "Site", "Topography" }),
            ("Mechanical",     new[]{ "Duct", "Air", "Mechanical", "HVAC" }),
            ("Electrical",     new[]{ "Electrical", "Lighting", "Light", "Conduit", "Panel", "Cable", "Device" }),
            ("Piping",         new[]{ "Pipe", "Plumb", "Sprinkler", "Sanitary" }),
            ("Infrastructure", new[]{ "Civil", "Road", "Bridge", "Structural", "Rebar", "Foundation", "Steel" })
        };

        private void ApplyDisciplineFilter()
        {
            _items.Clear();
            int discIndex = 0; try { discIndex = DisciplineCombo?.SelectedIndex ?? 0; } catch { discIndex = 0; }
            if (discIndex <= 0)
            {
                _items.AddRange(_allItems);
            }
            else
            {
                var target = DisciplineTokens[discIndex - 1];
                foreach (var it in _allItems)
                {
                    var name = it.DisplayName ?? it.EnumName;
                    if (target.tokens.Any(t => name.IndexOf(t, System.StringComparison.OrdinalIgnoreCase) >= 0))
                        _items.Add(it);
                }
            }
            _items.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.OrdinalIgnoreCase));
            _view?.Refresh();
        }

        private bool Filter(object o)
        {
            var it = (Item)o;
            var t = (FilterBox.Text ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(t))
            {
                if (it.DisplayName.IndexOf(t, System.StringComparison.OrdinalIgnoreCase) < 0 && it.EnumName.IndexOf(t, System.StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }
            bool showModel = ShowModelBox.IsChecked == true;
            bool showAnno = ShowAnnoBox.IsChecked == true;
            bool showImported = ShowImportedBox.IsChecked == true;
            return (it.Group == "Model" && showModel) || (it.Group == "Annotation" && showAnno) || (it.Group == "Imported" && showImported);
        }

        private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => _view?.Refresh();
        private void GroupFilterChanged(object sender, RoutedEventArgs e) => _view?.Refresh();
        private void ScopeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var currentSelected = _allItems.Where(i => i.IsSelected).Select(i => i.Value).ToList();
            RebuildScope(currentSelected);
            ApplyDisciplineFilter();
            _view?.Refresh();
        }
        private void DisciplineCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            ApplyDisciplineFilter();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_view == null) return;
            foreach (var obj in _view)
            {
                var it = (Item)obj;
                it.IsSelected = true;
            }
        }
        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            if (_view == null) return;
            foreach (var obj in _view)
            {
                var it = (Item)obj;
                it.IsSelected = false;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            ResultIds = _allItems.Where(i => i.IsSelected).Select(i => i.Value).ToList();
            DialogResult = true;
        }

        private sealed class Item : INotifyPropertyChanged
        {
            public string EnumName { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public int Value { get; set; }
            public string Group { get; set; } = "Model";
            private bool _isSelected;
            public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } } }
            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
