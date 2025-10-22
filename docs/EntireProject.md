# Plan: Enable `EntireProject` Workflows in Batch Export

## 1. Broaden Workflow Selection
- Update `BatchExportWindow.LoadWorkflowsIntoList` to include `EntireProject` scopes alongside set-scoped entries, and surface the scope in the UI so mixed selections are obvious. 【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L449-L487】
- Persist the workflow scope choice when saving selections, ensuring `AppSettings.SelectedWorkflowIds` can represent pure project-scoped runs without companion sets. 【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L604-L745】【F:src/GSADUs.Revit.Addin/AppSettings.cs†L143-L193】

## 2. Relax Set Validation Rules
- Allow the Batch Export window to proceed when zero sets are selected if every chosen workflow is project-scoped; display a warning only when set-scoped workflows are present without any sets. 【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L604-L745】
- Adjust `BatchRunCoordinator.RunCore`/`RunOnce` to skip the "No Selection Filters found" guard and empty-set cancellation whenever the batch contains only `EntireProject` workflows. 【F:src/GSADUs.Revit.Addin/Orchestration/BatchRunCoordinator.cs†L27-L216】

## 3. Scope-Aware Execution Path
- Extend `BatchExportSettings` (and callers) with metadata describing the scope of each workflow so the coordinator can separate project-scoped actions from set-scoped ones. 【F:src/GSADUs.Revit.Addin/BatchExportSettings.cs†L5-L15】【F:src/GSADUs.Revit.Addin/Orchestration/BatchRunCoordinator.cs†L127-L216】
- Refactor the coordinator to execute project-scoped workflows exactly once per run, outside the per-set loop, while preserving the existing iteration for set-scoped workflows. Ensure staging toggles and set-specific context are skipped for project-scoped runs. 【F:src/GSADUs.Revit.Addin/Orchestration/BatchRunCoordinator.cs†L270-L396】
- Update individual `IExportAction` implementations, where necessary, to tolerate an empty set context (e.g., null `setName`, empty UID lists) when triggered for `EntireProject`. 【F:src/GSADUs.Revit.Addin/Workflows/Image/ExportImageAction.cs†L14-L421】【F:src/GSADUs.Revit.Addin/Workflows/Csv/ExportCsvAction.cs†L20-L148】

## 4. Defaults, Telemetry, and UX
- Review `AppSettingsStore.EnsureWorkflowDefaults` and related migrations so seeded workflows can opt into project scope without being downgraded, and add new defaults/documentation as needed. 【F:src/GSADUs.Revit.Addin/AppSettings.cs†L143-L193】
- Expand logging/telemetry (e.g., `PerfLogger`, batch CSV log) to record whether a run was project-wide or per-set, aiding diagnostics after deployment. 【F:src/GSADUs.Revit.Addin/Orchestration/BatchRunCoordinator.cs†L138-L385】
