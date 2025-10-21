using Autodesk.Revit.DB;
using GSADUs.Revit.Addin.Workflows.Csv;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace GSADUs.Revit.Addin.UI
{
    internal sealed partial class WorkflowManagerPresenter
    {
        public CsvWorkflowTabViewModel CsvWorkflow { get; } = new CsvWorkflowTabViewModel();

        private void WireCsv()
        {
            // Populate scope options
            var scopes = new[] { "CurrentSet", "EntireProject" };
            CsvWorkflow.Scopes.Clear(); foreach (var s in scopes) CsvWorkflow.Scopes.Add(s);

            // Commands
            CsvWorkflow.SaveCommand = new DelegateCommand(_ => SaveCurrentCsv(), _ => true);

            // Observe selection changes similar to other tabs
            CsvWorkflow.PropertyChanged += VmOnPropertyChanged; // reuse existing handler to load when selection changes
        }

        private void SaveCurrentCsv()
        {
            var vm = CsvWorkflow;
            var nameVal = vm?.Name?.Trim() ?? string.Empty;
            var scopeVal = vm?.WorkflowScope ?? string.Empty;
            var descVal = vm?.Description ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nameVal) || string.IsNullOrWhiteSpace(scopeVal)) { _dialogs.Info("Save", "Name and Scope required."); return; }

            var existing = (_catalog.Settings.Workflows ?? new List<WorkflowDefinition>())
                .FirstOrDefault(w => string.Equals(w.Id, vm.SelectedWorkflowId, StringComparison.OrdinalIgnoreCase) && w.Output == OutputType.Csv);
            if (existing == null)
            {
                existing = new WorkflowDefinition { Id = Guid.NewGuid().ToString("N"), Kind = WorkflowKind.Internal, Output = OutputType.Csv, ActionIds = new List<string>(), Parameters = new Dictionary<string, JsonElement>() };
                _catalog.Settings.Workflows ??= new List<WorkflowDefinition>();
                _catalog.Settings.Workflows.Add(existing);
                vm.SelectedWorkflowId = existing.Id;
            }

            existing.Name = nameVal; existing.Scope = scopeVal; existing.Description = descVal;
            SaveCsvWorkflow(existing);
            vm.SetDirty(false);
            RefreshListsAfterSave();
        }

        private void SaveCsvWorkflow(WorkflowDefinition existing)
        {
            var vm = CsvWorkflow;
            var p = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

            // Persist selected schedule ids as string array
            var ids = vm.SelectedScheduleIds ?? Array.Empty<string>();
            using (var d = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(ids)))
            {
                p[CsvWorkflowKeys.scheduleIds] = d.RootElement.Clone();
            }
            // Persist file name pattern
            var pat = vm.CsvPattern ?? string.Empty;
            using (var d2 = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(pat)))
            {
                p[CsvWorkflowKeys.fileNamePattern] = d2.RootElement.Clone();
            }

            existing.Parameters = p;
            EnsureActionId(existing, "export-csv");
        }

        private void PopulateCsvSources(Document doc)
        {
            if (doc == null) return;
            try
            {
                var prev = CsvWorkflow.AvailableSchedules.Where(o => o.IsSelected).Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                CsvWorkflow.AvailableSchedules.Clear();
                foreach (var (Id, Name) in ScheduleDiscovery.GetAll(doc))
                {
                    CsvWorkflow.AvailableSchedules.Add(new ScheduleOption { Id = Id, Name = Name, IsSelected = prev.Contains(Id) });
                }
                // model file name base
                CsvWorkflow.ModelFileName = System.IO.Path.GetFileNameWithoutExtension(doc.PathName) ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
                throw;
            }
        }

        private void LoadWorkflowIntoCsvVm(string? id)
        {
            var wf = (_catalog.Settings.Workflows ?? new List<WorkflowDefinition>())
                .FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));
            ApplyBaseFields(wf, CsvWorkflow);
            ApplySavedCsvParameters(wf);
            CsvWorkflow.SetDirty(false);
        }

        private void ApplySavedCsvParameters(WorkflowDefinition? wf)
        {
            try
            {
                var p = wf?.Parameters;
                // Pattern
                string pat = string.Empty;
                try
                {
                    if (p != null && p.TryGetValue(CsvWorkflowKeys.fileNamePattern, out var je) && je.ValueKind == JsonValueKind.String)
                        pat = je.GetString() ?? string.Empty;
                }
                catch { }
                if (string.IsNullOrWhiteSpace(pat)) pat = "{SetName} {ViewName}";
                CsvWorkflow.CsvPattern = pat;

                // Selected schedule ids
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (p != null && p.TryGetValue(CsvWorkflowKeys.scheduleIds, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var je in arr.EnumerateArray())
                        {
                            if (je.ValueKind == JsonValueKind.String) ids.Add(je.GetString() ?? string.Empty);
                            else if (je.ValueKind == JsonValueKind.Number) ids.Add(je.ToString());
                        }
                    }
                }
                catch { }

                // Apply selection state to available list (preserve if not loaded yet)
                if (CsvWorkflow.AvailableSchedules.Count > 0)
                {
                    foreach (var o in CsvWorkflow.AvailableSchedules)
                    {
                        o.IsSelected = ids.Contains(o.Id);
                    }
                }
            }
            catch { }
        }
    }
}
