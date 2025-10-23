# Findings: Batch Export Support for `EntireProject` Workflows (Revised)

## Summary
The Batch Export feature assumes most workflows run per selection set. Project-scoped (EntireProject) runs are only needed for CSV exports. The picker currently shows all workflows, but the window and coordinator require at least one set, preventing true project-wide runs. To enable `EntireProject` for CSV only, we need scope-aware validation in the window and coordinator plus a single-pass execution path for project runs.

## UI Workflow Picker
* The workflow list already includes all saved workflows and shows `Scope`; it does not filter out `EntireProject`. 【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs】
* The Workflow Manager exposes `Scope` choices for all tabs. We will constrain `Scope` to `SelectionSet` for RVT/PDF/Image, and allow `EntireProject` only for CSV. 【F:src/GSADUs.Revit.Addin/UI/Presenters/WorkflowManagerPresenter.cs】
* The Run command requires at least one selection set row. When none are checked the window performs curation and aborts with "No sets selected". This blocks project-scoped CSV runs that should not require sets. 【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L604-L745】

## Coordinator Preflight
* `BatchRunCoordinator.RunCore` and `RunOnce` cancel when the active model has zero selection filters. This must be relaxed when the selection contains only `EntireProject` CSV workflows. 【F:src/GSADUs.Revit.Addin/Orchestration/BatchRunCoordinator.cs†L27-L105】
* After the dialog closes, `RunOnce` resolves chosen sets and cancels if empty. We need an alternate path that proceeds with zero sets when all selected workflows are CSV with `Scope == "EntireProject"`. 【F:src/GSADUs.Revit.Addin/Orchestration/BatchRunCoordinator.cs†L158-L216】

## Execution Loop
* The coordinator precomputes per-set membership and iterates `foreach` over selected `SelectionFilterElement` instances. Every enabled action executes once per set name. We need a single-pass branch for project-scoped CSV runs that executes actions once without staging/toggling or set context. 【F:src/GSADUs.Revit.Addin/Orchestration/BatchRunCoordinator.cs†L270-L396】
* Workflow actions receive the set name and member UID list; staging toggles the `CurrentSet` parameter each loop. Project-scoped CSV runs should pass an empty/null set context and bypass staging entirely. 【F:src/GSADUs.Revit.Addin/Orchestration/BatchRunCoordinator.cs†L315-L385】

## Settings Defaults
* `AppSettingsStore.EnsureWorkflowDefaults` seeds sample workflows with `Scope = "SelectionSet"` for RVT/PDF/Image/CSV. We will retain `SelectionSet` as the default for all types, while continuing to allow users to set CSV workflows to `EntireProject`. 【F:src/GSADUs.Revit.Addin/AppSettings.cs†L143-L193】

## Risks & Considerations
* `BatchExportSettings` and callers must tolerate empty `SetNames`/`SetIds` for project-scoped CSV-only runs. 【F:src/GSADUs.Revit.Addin/BatchExportSettings.cs†L5-L15】
* Action implementations:
  - CSV already handles scope-aware patterns and can operate with an empty set context. 【F:src/GSADUs.Revit.Addin/Workflows/Csv/ExportCsvAction.cs】
  - PDF/Image/RVT rely on per-set context and should remain `SelectionSet`-only; selecting them without sets should be blocked by validation.
* Mixed selections: If any set-scoped workflow is selected and no sets are chosen, the run should be blocked with a clear message; optionally, we may disallow mixing `EntireProject` CSV with set-scoped workflows in a single batch for simplicity.
