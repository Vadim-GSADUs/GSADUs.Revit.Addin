# Workflow Manager Phase 2 Reanalysis

## 1. Current hotspots blocking LOC reduction
- **WorkflowManagerWindow.xaml.cs remains large** and still hosts workflow hydration, validation, and persistence logic, preventing the 60-70% shrinkage target from the Phase 2 guide.
- The `Loaded` handler rehydrates PDF and Image tabs by hand (sheet set collectors, label updates) instead of delegating to presenter/VM layers, keeping tight coupling with Revit APIs and WPF controls.
- PDF enablement, dirty tracking, and save-state rendering are still partially computed via `FindName` lookups (`UpdateCanSaveFor`, `SetSaveVisual`), duplicating logic that already exists in tab view models.
- Image tab persistence/validation and preview remain partially duplicated between VM and window, risking drift.
- `SaveWorkflow_Click` still performs action ID manipulation and JSON plumbing; persistence should be presenter/service-owned.

## 2. Deviations from the Phase 2 workflow plan
- The plan called for the presenter to set tab DataContexts and own hydration; the window still performs much of the wiring.
- Base fields (name/scope/description) are read directly from controls on save instead of trusting VMs.
- Code-behind event handlers remain for grid sorting, name changes, and image scope toggles; bindings/commands should replace these.
- Image preview and enablement are calculated in multiple places (VM + window).

## 3. Refactoring opportunities aligned with "stupid simple"
- Promote view models to single source of truth for all tab state; delete window-side enablement/preview calculations once bindings drive the buttons directly.
- Move Revit API hydration into the presenter (populate PDF view/sheet sets and export setups; populate Image print sets and single views).
- Extract persistence: move JSON parameter assembly to presenter/service methods (e.g., `SavePdfWorkflow`, `SaveImageWorkflow`), with the window delegating.
- Centralize dirty state inside view models (`HasUnsavedChanges`/`IsSaveEnabled`) and XAML triggers; remove `SetSaveVisual` and ad-hoc enablement.
- Replace `FindName` sweeps with bindings or strongly typed fields.

## 4. Recommended approach: purge RVT/CSV tabs and reboot later
RVT and CSV tabs currently have no real workflow logic and add code-behind surface area. To accelerate VM-only convergence and reduce LOC:

- Remove RVT and CSV `TabItem`s from `WorkflowManagerWindow.xaml` for now.
- Delete RVT/CSV-specific code-behind: handlers (`RvtNewBtn_Click`, `CsvNewBtn_Click`, RVT/CSV branches in `SavedCombo_SelectionChanged`, `UpdateCanSaveFor`, `MarkDirty`, and RVT/CSV cases in `SaveWorkflow_Click`).
- Trim presenter of `RvtBase`/`CsvBase` VMs and any RVT/CSV wiring; keep only `PdfWorkflow` and `ImageWorkflow`.
- Main grid options:
  - Option A: show all workflows, but edit UI exists only for PDF/Image.
  - Option B (simpler now): filter to PDF/Image to avoid selection paths for missing tabs.
- When needed later, recreate RVT/CSV tabs by copying PDF/Image tab structure and backing them with small VMs (name/scope/description + future options), following the same VM-only + presenter-save pattern.

Pros
- Immediate LOC reduction and lower coupling.
- Faster move to VM-only source of truth for active tabs.

Cons
- Temporarily no editor UI for RVT/CSV.

## 5. VM-only migration roadmap (incremental)
- Stage 1: Presenter fully hydrates PDF and Image sources; window `Loaded` calls presenter methods only. PDF/Image save remains presenter-owned. Trust VM for name/scope/description on save.
- Stage 2: Remove window-side enablement/dirty code; XAML binds buttons to `IsSaveEnabled` and visuals to `HasUnsavedChanges` for both PDF and Image.
- Stage 3: Replace reflection-based saved-combo lookups with bound `SelectedWorkflowId` per tab VM; presenter updates VM and model on selection.
- Stage 4: Move Image whitelist (category IDs + summary text + pick command) into Image VM; window binds to VM command/property.
- Stage 5: Introduce a root `WorkflowManagerViewModel` for main list (rows, sort, selected id) and static settings (output folder/overwrite labels) and bind labels instead of setting via code-behind.

## 6. Next edit checkpoints (concrete steps)
1) Purge RVT/CSV tabs (UI only)
- Remove RVT and CSV `TabItem`s from `WorkflowManagerWindow.xaml`.
- Update XAML bindings and references to avoid dangling element names.

2) Remove RVT/CSV code paths (code-behind + presenter)
- In `WorkflowManagerWindow.xaml.cs`:
  - Delete RVT/CSV branches in `SavedCombo_SelectionChanged`, `UpdateCanSaveFor`, `MarkDirty`, `SaveWorkflow_Click`, `WorkflowsList_MouseDoubleClick`, `RefreshSavedCombos`.
  - Delete RVT/CSV button handlers (`RvtNewBtn_Click`, `CsvNewBtn_Click`, `RvtOption_Checked`).
  - Simplify `GetSelectedFromTab` to PDF/Image only.
- In `WorkflowManagerPresenter.cs`:
  - Remove `RvtBase`/`CsvBase` fields and any RVT/CSV wiring.

3) Presenter hydration is source of truth
- Ensure `PopulatePdfSources` and `PopulateImageSources` are the only hydration points; window `Loaded` calls them and assigns VMs to DataContexts.
- Remove any collector logic from window.

4) VM-driven save state (PDF/Image)
- Bind button `IsEnabled` and visuals to `IsSaveEnabled`/`HasUnsavedChanges` on both tabs.
- Remove `SetSaveVisual`/`MarkDirty`/`UpdateCanSaveFor` branches for PDF/Image.

5) Move PDF labels to bindings
- Expose `PdfOutputFolder` and `PdfOverwritePolicyText` on a suitable VM (temporary: presenter exposes read-only properties on `PdfWorkflowTabViewModel`).
- Bind `PdfOutFolderLabel`/`PdfOverwriteLabel` to those properties; remove code-behind assignment.

6) Image whitelist to VM
- Add `WhitelistSummary` and `PickWhitelistCommand` to `ImageWorkflowTabViewModel`.
- Bind UI to those; delete `ImageBlacklistPickBtn_Click` and summary updater from window.

7) Saved-combo selection to VM-only
- Add `SelectedWorkflowId` per tab VM; bind saved Combos to it.
- Presenter observes changes, locates the model (id), updates VM fields, and clears dirty.
- Remove reflection-based selection code in window.

## 7. Nice-to-haves (later)
- Introduce a root `WorkflowManagerViewModel` for main list (rows, sort, selection, commands).
- Replace `GridViewColumnHeader_Click` sorting with `CollectionViewSource` sorting through bindings/commands.
- Centralize persistence into `WorkflowCatalogService` helpers (`UpdatePdfWorkflow`, `UpdateImageWorkflow`).

## 8. Status notes
- {SetName} is now auto-inserted during save for PDF and Image; token validation prompts are no longer needed.
- Active goal: VM-only source of truth for PDF/Image tabs before reintroducing RVT/CSV.