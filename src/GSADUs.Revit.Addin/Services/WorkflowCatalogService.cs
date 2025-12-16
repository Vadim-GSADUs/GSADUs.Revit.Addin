using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using GSADUs.Revit.Addin.Abstractions;

namespace GSADUs.Revit.Addin
{
    /// <summary>
    /// Encapsulates CRUD and persistence for AppSettings.Workflows.
    /// Exposes observable collections for binding in UI.
    /// </summary>
    internal sealed class WorkflowCatalogService
    {
        private readonly IProjectSettingsProvider _projectSettingsProvider;
        private AppSettings _settings;
        private bool _hasPendingChanges;

        public ObservableCollection<WorkflowDefinition> Workflows { get; } = new();
        public ObservableCollection<string> SavedWorkflowNames { get; } = new();

        public WorkflowCatalogService(IProjectSettingsProvider projectSettingsProvider)
        {
            _projectSettingsProvider = projectSettingsProvider;
            _settings = _projectSettingsProvider.Load();
            RefreshCaches();
        }

        public AppSettings Settings => _settings;
        public bool HasPendingChanges => _hasPendingChanges;

        /// <summary>
        /// Replace the current settings instance with the provided snapshot
        /// and refresh in-memory caches. Used by the ExternalEvent-backed
        /// save pipeline so that the catalog reflects the latest UI-edited
        /// state 1:1 before persisting to storage.
        /// </summary>
        /// <param name="snapshot">New settings snapshot to apply.</param>
        public void ApplySettings(AppSettings snapshot)
        {
            _settings = snapshot ?? new AppSettings();
            _hasPendingChanges = true;
            RefreshCaches();
        }

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
            MarkDirtyAndRefresh();
            return wf;
        }

        public void Rename(string id, string newName)
        {
            var wf = Find(id);
            if (wf == null) return;
            wf.Name = newName;
            MarkDirtyAndRefresh();
        }

        public bool Delete(string id)
        {
            if (_settings.Workflows == null) return false;
            int removed = _settings.Workflows.RemoveAll(w => w.Id == id);
            if (removed == 0) return false;
            _settings.SelectedWorkflowIds?.RemoveAll(x => _settings.Workflows.All(w => w.Id != x));
            MarkDirtyAndRefresh();
            return true;
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
            MarkDirtyAndRefresh();
            return clone;
        }

        public WorkflowDefinition? Find(string id) => _settings.Workflows?.FirstOrDefault(w => w.Id == id);

        public bool Save() => Save(force: false);

        public bool Save(bool force)
        {
            if (!force && !_hasPendingChanges) return false;
            _projectSettingsProvider.Save(_settings);
            _hasPendingChanges = false;
            return true;
        }

        public bool SaveAndRefresh()
        {
            var hadChanges = _hasPendingChanges;
            var persisted = Save();
            if (hadChanges)
                RefreshCaches();
            return persisted;
        }

        private void MarkDirtyAndRefresh()
        {
            _hasPendingChanges = true;
            RefreshCaches();
        }
    }
}
