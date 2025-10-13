using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace GSADUs.Revit.Addin.UI
{
    public partial class ElementsPickerWindow : Window
    {
        // --- Singleton management (per type) ---
        private static ElementsPickerWindow? _activeInstance;
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

        public IReadOnlyList<string> ResultUids { get; private set; } = new List<string>();

        private readonly List<Item> _allItems = new();
        private readonly List<Item> _items = new();
        private ListCollectionView? _view;
        private readonly Document? _doc;
        private readonly AppSettings _settings;
        private int _cap = 200;

        public ElementsPickerWindow(Document? doc, AppSettings settings, IEnumerable<string>? preselected = null)
        {
            InitializeComponent();
            RegisterInstance();
            _doc = doc;
            _settings = settings;

            try
            {
                ScopeCombo.SelectedIndex = 0; // Staging Area default
                CapBox.Text = "200";
                Rebuild(preselected ?? Enumerable.Empty<string>());
                Bind();
            }
            catch { }
        }

        private void Bind()
        {
            _view = new ListCollectionView(_items);
            _view.Filter = Filter;
            ItemsList.ItemsSource = _view;
        }

        private bool Filter(object? o)
        {
            if (o is not Item it) return false;
            var t = (FilterBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return true;
            return (it.IdText?.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                || (it.TypeName?.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                || (it.FamilyName?.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                || (it.Category?.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                || (it.CategoryType?.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                || (it.Discipline?.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void Rebuild(IEnumerable<string> preselected)
        {
            _allItems.Clear();
            _items.Clear();
            var pre = new HashSet<string>(preselected ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            IEnumerable<Element> source = Enumerable.Empty<Element>();
            if (_doc != null)
            {
                try
                {
                    if (ScopeCombo.SelectedIndex == 0)
                    {
                        // Staging area: elements inside BB
                        double w = Math.Max(1.0, _settings.StagingWidth);
                        double h = Math.Max(1.0, _settings.StagingHeight);
                        double b = Math.Max(0.0, _settings.StagingBuffer);
                        double halfW = w * 0.5 + b;
                        double halfH = h * 0.5 + b;
                        var min = new XYZ(-halfW, -halfH, double.NegativeInfinity);
                        var max = new XYZ(halfW, halfH, double.PositiveInfinity);
                        var outline = new Outline(min, max);
                        var bbFilter = new BoundingBoxIntersectsFilter(outline);
                        source = new FilteredElementCollector(_doc)
                            .WhereElementIsNotElementType()
                            .WherePasses(bbFilter)
                            .ToElements();
                    }
                    else
                    {
                        // Current Set: parameter value == true
                        string pName = _settings.CurrentSetParameterName ?? "CurrentSet";
                        var elems = new FilteredElementCollector(_doc)
                            .WhereElementIsNotElementType()
                            .ToElements();
                        source = elems.Where(e => HasBoolParamTrue(e, pName));
                    }
                }
                catch { source = Enumerable.Empty<Element>(); }
            }

            var list = new List<Item>();
            foreach (var e in source)
            {
                try
                {
                    var uid = e.UniqueId ?? string.Empty;
                    if (string.IsNullOrEmpty(uid)) continue;

                    var cat = e.Category;
                    string catName = string.Empty; try { catName = cat?.Name ?? string.Empty; } catch { }
                    string catType = string.Empty; try { catType = cat?.CategoryType == CategoryType.Annotation ? "Annotation" : "Model"; } catch { }

                    string typeName = string.Empty;
                    string familyName = string.Empty;
                    try
                    {
                        var et = _doc?.GetElement(e.GetTypeId()) as ElementType;
                        if (et != null)
                        {
                            typeName = et.Name ?? string.Empty;
                            try { familyName = (et as FamilySymbol)?.Family?.Name ?? string.Empty; } catch { }
                        }
                    }
                    catch { }

                    string discipline = GuessDiscipline(catName, typeName, familyName);

                    list.Add(new Item
                    {
                        UniqueId = uid,
                        IdText = e.Id?.ToString() ?? string.Empty,
                        TypeName = typeName,
                        FamilyName = familyName,
                        Category = catName,
                        CategoryType = catType,
                        Discipline = discipline,
                        IsSelected = pre.Contains(uid)
                    });
                }
                catch { }
            }

            // cap list length
            int cap = _cap;
            if (!int.TryParse(CapBox.Text, out cap)) cap = _cap;
            _allItems.AddRange(list.OrderBy(i => i.Category).ThenBy(i => i.TypeName).ThenBy(i => i.IdText).Take(cap));
            _items.AddRange(_allItems);
            _view?.Refresh();
        }

        private static string GuessDiscipline(string category, string type, string family)
        {
            string s = string.Join(" ", new[] { category, type, family });
            if (ContainsAny(s, "Duct", "Mechanical", "HVAC")) return "Mechanical";
            if (ContainsAny(s, "Electrical", "Lighting", "Light", "Conduit", "Panel", "Device")) return "Electrical";
            if (ContainsAny(s, "Pipe", "Plumb", "Sprinkler", "Sanitary")) return "Piping";
            if (ContainsAny(s, "Wall", "Door", "Window", "Ceiling", "Floor", "Roof", "Room", "Stair", "Railing", "Curtain", "Furniture", "Casework", "Site", "Topography")) return "Architectural";
            if (ContainsAny(s, "Civil", "Road", "Bridge", "Structural", "Rebar", "Foundation", "Steel")) return "Infrastructure";
            return string.Empty;
        }

        private static bool ContainsAny(string s, params string[] tokens)
        {
            foreach (var t in tokens)
            {
                if (s.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static bool HasBoolParamTrue(Element e, string name)
        {
            if (e == null || string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                var p = e.LookupParameter(name);
                if (p == null) return false;
                if (p.StorageType == StorageType.Integer) return p.AsInteger() == 1;
                if (p.StorageType == StorageType.String) return string.Equals(p.AsString(), "1", StringComparison.OrdinalIgnoreCase) || string.Equals(p.AsString(), "true", StringComparison.OrdinalIgnoreCase);
                return false;
            }
            catch { return false; }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var it in _items) it.IsSelected = true;
            _view?.Refresh();
        }
        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var it in _items) it.IsSelected = false;
            _view?.Refresh();
        }

        private void ScopeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var pre = _items.Where(i => i.IsSelected).Select(i => i.UniqueId).ToList();
            Rebuild(pre);
            _view?.Refresh();
        }

        private void CapBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!int.TryParse(CapBox.Text, out _cap) || _cap <= 0) _cap = 200;
            var pre = _items.Where(i => i.IsSelected).Select(i => i.UniqueId).ToList();
            Rebuild(pre);
            _view?.Refresh();
        }

        private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _view?.Refresh();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            ResultUids = _allItems.Where(i => i.IsSelected).Select(i => i.UniqueId).ToList();
            DialogResult = true;
        }

        private sealed class Item : INotifyPropertyChanged
        {
            public string UniqueId { get; set; } = string.Empty;
            public string IdText { get; set; } = string.Empty;
            public string TypeName { get; set; } = string.Empty;
            public string FamilyName { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public string CategoryType { get; set; } = string.Empty;
            public string Discipline { get; set; } = string.Empty;
            private bool _isSelected;
            public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } } }
            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
