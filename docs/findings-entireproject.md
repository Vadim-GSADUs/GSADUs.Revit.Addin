# Findings: Batch Export Support for `EntireProject` Workflows

## Summary
The Batch Export feature currently assumes every workflow is scoped to a selection set. As a result, project-scoped workflows are filtered out of the picker and cannot run, and the execution loop always iterates per selection set. To integrate `EntireProject` workflows we need coordinated updates to the picker, validation logic, and `BatchRunCoordinator`.

## UI Workflow Picker
* `BatchExportWindow.LoadWorkflowsIntoList` explicitly excludes any workflow whose scope string is not interpreted as a selection-set scope. This drops `EntireProject` entries from the grid, preventing users from selecting them. 【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L449-L461】
* The Run command requires at least one selection set row to be selected. When none are checked the window performs curation and aborts with "No sets selected", so project-scoped workflows can never execute without a set. 【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L604-L745】

## Coordinator Preflight
* `BatchRunCoordinator.RunCore` and `RunOnce` refuse to continue if the active model has zero selection filters, making it impossible to trigger a project-wide workflow when no sets exist. 【F:src/GSADUs.Revit.Addin/Orchestration/BatchRunCoordinator.cs†L27-L105】
* After the dialog closes, `RunOnce` resolves the chosen sets and cancels if the resulting list is empty, reinforcing the requirement that at least one set accompany every workflow. 【F:src/GSADUs.Revit.Addin/Orchestration/BatchRunCoordinator.cs†L158-L216】

## Execution Loop
* The coordinator precomputes per-set membership data and then iterates `foreach` over the selected `SelectionFilterElement` instances. Every enabled action executes once per set name, so an `EntireProject` workflow would repeat for each set instead of running once. 【F:src/GSADUs.Revit.Addin/Orchestration/BatchRunCoordinator.cs†L270-L396】
* Workflow actions receive the set name and member UID list, and staging helpers toggle the `CurrentSet` parameter around each loop. Project-scoped workflows will need alternate inputs (probably `null`/empty set context) and should bypass staging. 【F:src/GSADUs.Revit.Addin/Orchestration/BatchRunCoordinator.cs†L315-L385】

## Settings Defaults
* `AppSettingsStore.EnsureWorkflowDefaults` seeds all sample workflows with `Scope = "CurrentSet"` so they remain visible in the Batch Export window. When support for `EntireProject` is added, the defaults and migrations should be reviewed to avoid re-migrating project-wide scopes back to `CurrentSet`. 【F:src/GSADUs.Revit.Addin/AppSettings.cs†L143-L193】

## Risks & Considerations
* `BatchExportSettings` and downstream actions assume non-empty `SetNames`/`SetIds`. Callers and implementations must tolerate empty collections when only project-scoped workflows are selected. 【F:src/GSADUs.Revit.Addin/BatchExportSettings.cs†L5-L15】
* Actions that currently depend on set membership (e.g., image staging, CSV pattern tokens) may need scope-aware logic to avoid null references or incorrect naming conventions when no set is provided.
