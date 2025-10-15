using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace GSADUs.Revit.Addin
{
    /// <summary>
    /// Encapsulates CRUD and persistence for AppSettings.Workflows.
    /// Exposes observable collections for binding in UI.
    /// </summary>
    internal sealed class WorkflowCatalogService
    {
        private readonly ISettingsPersistence _persistence;
        private AppSettings _settings;

        public ObservableCollection<WorkflowDefinition> Workflows { get; } = new();
        public ObservableCollection<string> SavedWorkflowNames { get; } = new();

        public WorkflowCatalogService(ISettingsPersistence persistence)
        {
            _persistence = persistence;
            _settings = _persistence.Load();
            RefreshCaches();
        }

        public AppSettings Settings => _settings;

        public void RefreshCaches()
        {
            Workflows.Clear();
            var list = _settings.Workflows ?? new List<WorkflowDefinition>();
            foreach (var wf in list.OrderBy(w => w.Order))
                Workflows.Add(wf);

            SavedWorkflowNames.Clear();
            foreach (var name in Workflows.Select(w => w.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
                SavedWorkflowNames.Add(name!);
        }

        public WorkflowDefinition Create(string name, WorkflowKind kind, OutputType output)
        {
            var wf = new WorkflowDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Kind = kind,
                Output = output,
                Order = (_settings.Workflows?.Count ?? 0)
            };
            _settings.Workflows ??= new List<WorkflowDefinition>();
            _settings.Workflows.Add(wf);
            Save();
            RefreshCaches();
            return wf;
        }

        public void Rename(string id, string newName)
        {
            var wf = Find(id);
            if (wf == null) return;
            wf.Name = newName;
            SaveAndRefresh();
        }

        public void Delete(string id)
        {
            if (_settings.Workflows == null) return;
            _settings.Workflows.RemoveAll(w => w.Id == id);
            _settings.SelectedWorkflowIds?.RemoveAll(x => _settings.Workflows.All(w => w.Id != x));
            SaveAndRefresh();
        }

        public WorkflowDefinition? Duplicate(string id, string? newName = null)
        {
            var wf = Find(id);
            if (wf == null) return null;
            var clone = new WorkflowDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = newName ?? (string.IsNullOrWhiteSpace(wf.Name) ? "Workflow Copy" : wf.Name + " Copy"),
                Kind = wf.Kind,
                Output = wf.Output,
                Scope = wf.Scope,
                Description = wf.Description,
                ActionIds = new List<string>(wf.ActionIds ?? new List<string>()),
                Parameters = new Dictionary<string, System.Text.Json.JsonElement>(wf.Parameters ?? new Dictionary<string, System.Text.Json.JsonElement>()),
                Enabled = wf.Enabled,
                Order = (_settings.Workflows?.Max(w => (int?)w.Order) ?? -1) + 1
            };
            _settings.Workflows ??= new List<WorkflowDefinition>();
            _settings.Workflows.Add(clone);
            SaveAndRefresh();
            return clone;
        }

        public WorkflowDefinition? Find(string id) => _settings.Workflows?.FirstOrDefault(w => w.Id == id);

        public void Save()
        {
            _persistence.Save(_settings);
        }

        public void SaveAndRefresh()
        {
            Save();
            RefreshCaches();
        }
    }
}
