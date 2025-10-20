# Prototype workflow: export-rvt — Design and Validation Plan

1) What already exists and is reusable
- Core contracts and orchestration
  - `IExportAction` with `Id`, `Order`, `RequiresExternalClone`, `IsEnabled(...)`, `Execute(...)` — ready to host RVT steps.
  - `BatchRunCoordinator` composes actions by `ActionRegistry` and selected workflows; resolves selection sets; loops actions and calls `Execute(uiapp, sourceDoc, outDoc:null, setName, preserveUids, isDryRun)`.
  - `ActionRegistry` provides UI-visible action descriptors (id/name/order/RequiresExternalClone). Currently only `export-pdf`, `export-image` are registered; RVT-related ids were removed (comment notes prior removal).
- Existing actions (patterns to follow)
  - `Workflows/Pdf/ExportPdfAction` and `Workflows/Image/ExportImageAction` implement `IExportAction` and are registered in DI.
- Settings & workflow model
  - `AppSettingsStore.Load/Save` and `WorkflowDefinition` (`ActionIds`, `Parameters`) allow per-workflow configuration via `Parameters` without schema changes.
  - Defaults seed an RVT workflow entry: `Name: "RVT ? Model"`, `Kind: External`, `Output: Rvt`, `Scope: "Model"`, `ActionIds: ["export-rvt"]`.
- UI selection
  - `BatchExportWindow` lets users pick selection sets and workflows; persists `SelectedWorkflowIds`. It filters workflows by scope: only ones with `Scope` equal to `CurrentSet`/`SelectionSet` are shown.

2) Gaps vs. RVTworkflow.md requirements
- Missing action(s): no implementation registered for `export-rvt`.
- `ActionRegistry` has no descriptor for `export-rvt` (so it can’t be chosen/executed).
- Coordinator `outDoc` flow: external actions are detected but never executed; `outDoc` is always null and `externalActions` list is ignored.
- Workflow visibility: the default RVT workflow has `Scope: "Model"` and is therefore hidden in `BatchExportWindow` (which only shows CurrentSet/SelectionSet). To be selectable, the scope must be adjusted (or the window relaxed).
- Parameters: no defined keys yet for template path; `.rvt` output path relies on global `DefaultOutputDir`.
- Required behaviors not implemented: create-from-template, copy set elements, save-as `{SetName}.rvt` into output dir, close doc, delete `.000#.rvt` backups.

3) New classes/files to add (clean integration)
- `src/GSADUs.Revit.Addin/Workflows/Rvt/ExportRvtAction.cs`
  - Prototype, monolithic action with `Id = "export-rvt"` (Recommended for first cut). Steps inside `Execute(...)`:
    - Read template file path from the selected workflow’s `Parameters["templatePath"]` (fallback: error dialog if missing).
    - Create new document: `uiapp.Application.NewProjectDocument(templatePath)`.
    - Resolve elements for the current set using `preserveUids` or by set name; copy to new doc via `ElementTransformUtils.CopyElements(sourceDoc, ids, newDoc, Transform.Identity, new CopyPasteOptions())` inside a destination `Transaction`.
    - Build `{SetName}.rvt` path in effective output dir; `SaveAs` with overwrite policy from settings; close the new doc.
    - Delete `*.000*.rvt` backups next to the saved file.
- (Optional split for future, once coordinator supports `outDoc`):
  - `Actions/Rvt/CreateFromTemplateAction.cs` (Id: `rvt.create-from-template`, `RequiresExternalClone = true`)
  - `Actions/Rvt/CopySetToNewDocAction.cs` (Id: `rvt.copy-set-to-new-doc`, `RequiresExternalClone = true`)
  - `Actions/Rvt/SaveAndCloseNewDocAction.cs` (Id: `rvt.save-and-close`, `RequiresExternalClone = true`)
  - `Actions/Common/DeleteRevitBackupsAction.cs` (Id: `common.delete-rvt-backups`, `RequiresExternalClone = false`)
- Utilities
  - `Infrastructure/SelectionSetResolver.cs` to centralize: resolve by SetId/SetName, map `preserveUids` → `ElementId` list, and sanitize/sort for copying.
- Keys
  - `Workflows/Rvt/RvtWorkflowKeys.cs` with string constants, e.g. `templatePath`, and optional `outputDirOverride`.
- Wiring
  - Add `export-rvt` descriptor in `ActionRegistry` and register the action in `Infrastructure/DI/Startup.cs`.

4) Architectural/dependency conflicts to watch
- Coordinator lifecycle
  - `BatchRunCoordinator` ignores `externalActions` and never supplies a non-null `outDoc`. Multi-action RVT implementations that depend on `RequiresExternalClone` must extend the coordinator to: create the doc, pass it to subsequent actions, and ensure close on success/failure/cancel.
- Workflow visibility
  - `BatchExportWindow.LoadWorkflowsIntoList` only shows workflows with scope `CurrentSet`/`SelectionSet`; default seeded RVT workflow scope is `Model` → it won’t appear. Either change the default scope for RVT to `CurrentSet` (recommended) or relax the filter.
- DI and registry alignment
  - Selected workflow → `ActionIds` are gathered; if `ActionRegistry` lacks matching descriptors/implementations, the run aborts with "No enabled actions found".
- Revit API nuances
  - `Application.NewProjectDocument` requires a valid `.rte` template path; surface a clear error if missing.
  - Cross-document copy must run in a transaction on the destination document.
  - Saving a brand-new doc requires `SaveAs` with proper `SaveAsOptions` (and optional overwrite) before `Document.Close(false)`.
  - Backup cleanup: constrain deletion to `{SetName}.000*.rvt` peers in the same directory; avoid touching the primary file.

5) Suggested implementation order (min-risk to working prototype)
- Step 0: Make the RVT workflow selectable
  - Update the seeded RVT workflow scope to `CurrentSet` so it shows in `BatchExportWindow`. Optionally add a one-time migration that flips existing `Scope: "Model"` to `"CurrentSet"` for users.
- Step 1: Implement a single `ExportRvtAction` (prototype)
  - Register in DI and add an `ActionRegistry` descriptor `Id = "export-rvt"`, `DisplayName = "Export RVT (from template)", Order ~ 300, RequiresExternalClone = false`.
  - Read `templatePath` from workflow `Parameters`, fallback to user prompt or abort with dialog.
  - Perform: new-doc → copy elements → save-as `{SetName}.rvt` in output directory → close → delete backups. Honor `DefaultOverwrite`.
- Step 2: Validate end-to-end
  - Select one set in `BatchExportWindow` and the RVT workflow; confirm output `{SetName}.rvt` created and openable; verify `.000*.rvt` backups removed.
- Step 3: Harden parameters and error paths
  - Support optional per-workflow `outputDirOverride`; sanitize filename; robust exception messages via `IDialogService`.
- Step 4 (optional refactor): Split into sub-actions and extend coordinator
  - Add `CreateFromTemplateAction`, `CopySetToNewDocAction`, `SaveAndCloseNewDocAction`, `DeleteRevitBackupsAction` with `RequiresExternalClone` on the first two.
  - Extend `BatchRunCoordinator` to create/own `outDoc` when any selected descriptor has `RequiresExternalClone == true`, pass it to `Execute`, and ensure dispose/close on exit paths.
  - Update default RVT workflow `ActionIds` to the split ids to match the new pipeline.

6) Quick validation checklist
- Workflow appears in `BatchExportWindow` and can be selected.
- Run creates `{OutputDir}/{SetName}.rvt` from the provided template and closes it.
- Elements from the selected set exist in the new file (spot-check by opening the file).
- No unhandled exceptions on missing template path; user sees a clear message.
- Any `*.000*.rvt` in the output folder for that set are removed after save.

Addendum: Review of docs/RVTworkflow.md (alignment and corrections)

- Strong alignment
  - Core steps match requirements: create from template, copy selected set, save/close, delete .000#.rvt.
  - Suggestion to introduce `SelectionSetResolver` is sound; centralizes logic scattered in `BatchRunCoordinator`.
  - Need for coordinator to manage a secondary document (`outDoc`) when actions require it is correct.

- Corrections to match current codebase
  - WorkflowDefinition shape: use `Output`, `Scope`, `ActionIds`, `Parameters` (Dictionary<string, JsonElement>); not `Type`, `Actions`, `Params`.
  - Visibility in Batch Export Window: only workflows with `Scope` equal to `CurrentSet`/`SelectionSet` are listed. The example uses `Model`; change to `CurrentSet` so users can select it.
  - Action registration: implementations are registered via DI in `Infrastructure/DI/Startup.cs` (`services.AddSingleton<IExportAction, ...>()`), while UI metadata lives in `Infrastructure/ActionRegistry.cs` (descriptor list). Do not return actions from `ActionRegistry` as in the doc sample.
  - Coordinator behavior: `BatchRunCoordinator` currently ignores `RequiresExternalClone` and never supplies non-null `outDoc`. A split multi-action RVT pipeline requires extending it; until then, prefer a single `export-rvt` action that performs the full sequence internally.

- Parameterization and keys
  - Define `Workflows/Rvt/RvtWorkflowKeys.cs` with constants like `templatePath`, `outputDirOverride`, `backupCleanup`. Store values in the selected workflow’s `Parameters` bag; do not extend `AppSettings` schema unless needed globally.
  - Use `AppSettingsStore.GetEffectiveOutputDir(...)` unless a per-workflow override is provided.

- Implementation nuances (Revit API)
  - Create: `uiapp.Application.NewProjectDocument(templatePath)` and validate path; show `IDialogService.Info` on missing/invalid template.
  - Copy: resolve `ICollection<ElementId>` from selected set; run `ElementTransformUtils.CopyElements(sourceDoc, ids, newDoc, Transform.Identity, new CopyPasteOptions())` inside a `Transaction` on the destination doc.
  - Save/Close: compute `{OutputDir}/{SetName}.rvt`; apply overwrite based on `DefaultOverwrite`; `newDoc.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = overwrite })`; then `newDoc.Close(false)`.
  - Cleanup: delete only sibling files matching `{SetName}.000*.rvt`.

- Plan delta vs. the doc
  - Phase 1 (prototype): implement single `ExportRvtAction` (`Id = "export-rvt"`, `Order ~ 300`, `RequiresExternalClone = false`) and add descriptor + DI registration. Adjust default RVT workflow `Scope` to `CurrentSet` so it appears in the window.
  - Phase 2 (refactor): split into `rvt.create-from-template` (+`RequiresExternalClone = true`), `rvt.copy-set-to-new-doc`, `rvt.save-and-close`, and `common.delete-rvt-backups`; extend `BatchRunCoordinator` to own the `outDoc` lifecycle.

- Quick naming map (doc → codebase)
  - Doc `Type` → code `Output` (`OutputType.Rvt`)
  - Doc `Actions` → code `ActionIds`
  - Doc `Params` → code `Parameters`
  - Doc scope `Model` → use `CurrentSet` to be visible in Batch Export UI
