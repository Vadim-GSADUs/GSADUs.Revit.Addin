# Workflow Manager Phase 2 Reanalysis (Updated)

This document tracks progress and the remaining steps to complete the MVVM simplification for `WorkflowManagerWindow` with minimal legacy dependencies.

## A. Changes completed in this phase
- Replaced header-click sorting with a `CollectionViewSource` (`WorkflowsView`) sorted by `Name` ascending; removed header-click sorting logic.
- Main list now binds to the `CollectionViewSource` (no ad-hoc sorting in code-behind).
- Removed the "Audit" column and related button from the Main tab.
- Duplicated PDF/Image XAML controls and old handlers were cleaned where relevant.
- Wired PDF/Image "New" buttons to `NewCommand` on their tab view models (`PdfWorkflowTabViewModel`, `ImageWorkflowTabViewModel`), which clear state and mark dirty.
- Kept presenter-owned persistence: window delegates to presenter (`SavePdfWorkflow`, `SaveImageWorkflow`).
- PDF labels (`OutputFolder`, `OverwritePolicyText`) and Image preview are VM-bound (no manual label setting).

## B. Hotspots still blocking full MVVM
- Root DataContext for main list is still not a dedicated root VM; code-behind still provisions data for the `CollectionViewSource` via `RefreshMainList`.
- Saved-workflow combos (`PdfSavedCombo`, `ImageSavedCombo`) still rely on code-behind `RefreshSavedCombos` to set `ItemsSource`.
- Some code-behind remains around double-click navigation and reflection-based selection of saved workflow items.
- Presenter is still created within the window; DI/binding from a composition root would improve testability.
- Window still handles some Revit UI checks (e.g., PDF enablement for family docs) directly instead of via VM state.

## C. Deviations narrowed (improved vs. original V2 plan)
- Sorting is now binding-driven (ok).
- VM `NewCommand` introduced and bound (ok).
- Save-state visuals are binding-driven (`IsSaveEnabled`, `HasUnsavedChanges`) for both PDF and Image (ok).
- Persistence is presenter-owned (ok). JSON/parameters remain out of the window (ok).
- Remaining: root Workflows collection binding, saved-combo `ItemsSource` binding, code-behind reflection and navigation.

## D. Refactoring targets aligned with "stupid simple"
- Introduce a root `WorkflowManagerViewModel` exposing:
  - `ObservableCollection<WorkflowListItem> Workflows` (Main list rows)
  - `WorkflowListItem? SelectedWorkflow`
  - `PdfWorkflowTabViewModel Pdf`, `ImageWorkflowTabViewModel Image`
  - Aggregated `HasUnsavedChanges`, `IsSaveEnabled` (derived or forwarded)
  - Commands: `Duplicate`, `Rename`, `Delete`, `OpenWorkflow`, `SaveClose`, `Cancel`
- Bind `WorkflowsView.Source` directly to `WorkflowManagerViewModel.Workflows`; remove `RefreshMainList`.
- Bind saved combos `ItemsSource` to `Pdf.SavedWorkflows` and `Image.SavedWorkflows`; keep `SelectedValue` bound to `SelectedWorkflowId` (already bound). Remove `RefreshSavedCombos` and reflection-based selection.
- Keep presenter as the boundary to Revit API and persistence, injected into the VM. Remove presenter/window coupling.

## E. RVT/CSV tabs
- Current scope is PDF/Image only. RVT/CSV editors remain out-of-scope to keep surface area low.
- Reintroduce RVT/CSV later by cloning PDF/Image tab structure with small VMs.

## F. Incremental MVVM migration roadmap (status)
- Stage 1: Presenter hydrates PDF/Image sources; window `Loaded` calls presenter methods only. (In progress, window still does some UI enablement.)
- Stage 2: Remove window-side enablement/dirty code; rely on `IsSaveEnabled`/`HasUnsavedChanges`. (Done for active buttons.)
- Stage 3: Saved-combo selection is VM-only via `SelectedWorkflowId`. (Partially done: `SelectedValue` is bound, but `ItemsSource` still set in code-behind.)
- Stage 4: Image whitelist is VM-owned (`WhitelistSummary`, `PickWhitelistCommand`). (Done.)
- Stage 5: Root `WorkflowManagerViewModel` provides main list rows/sort/selection; remove code-behind provisioning. (Pending.)

## G. Next edit checkpoints (actionable)
1) Root VM and bindings
- Create `WorkflowManagerViewModel` with `Workflows`, `SelectedWorkflow`, and children: `Pdf`, `Image`.
- In XAML: set `WorkflowsView.Source="{Binding Workflows}"` and 
  `WorkflowsList.SelectedItem="{Binding SelectedWorkflow, Mode=TwoWay}"`.
- In window construction: set `DataContext` to the root VM (prefer DI), remove `RefreshMainList`.

2) Saved-workflow combos to VM-only
- Add `ObservableCollection<SavedWorkflowListItem> SavedWorkflows` to each tab VM.
- Bind `PdfSavedCombo.ItemsSource` and `ImageSavedCombo.ItemsSource` to those collections.
- Remove `RefreshSavedCombos` and reflection selection logic; presenter updates VM lists and honors `SelectedWorkflowId`.

3) Remove navigation/reflection from window
- Replace `WorkflowsList_MouseDoubleClick` with a command (e.g., `OpenWorkflowCommand`) on the root VM.
- Remove header-click remnants, selection reflection, and any remaining `FindName`-based plumbing.

4) Presenter to service boundary
- Construct presenter (or services) in the composition root/DI; inject into root VM and tab VMs.
- Window becomes a dumb view with bindings only.

5) PDF enablement from VM
- Provide `IsPdfEnabled` on VM based on active document context; bind controls’ `IsEnabled` instead of toggling in window.
- Remove `TryDisablePdfControls`.

## H. Risks / notes
- Keep presenter/service boundary for Revit API and persistence; do not push Revit calls into VMs directly.
- Avoid reintroducing control name lookups; prefer bindings/commands everywhere.

## I. Quick checklist
- [x] CollectionViewSource sorting for main list
- [x] Remove Audit column/button
- [x] Bind PDF/Image New buttons to VM `NewCommand`
- [x] Presenter owns persistence (save)
- [x] Image whitelist in VM
- [ ] Root `WorkflowManagerViewModel` provides Workflows and selection
- [ ] Saved-workflow combos: ItemsSource bound to VM (remove RefreshSavedCombos)
- [ ] Replace double-click handler with bound command
- [ ] Presenter/Services via DI, no window-owned creation
- [ ] PDF enablement via VM property (remove TryDisablePdfControls)

## J. Status notes
- Main-screen sorting is now stable and binding-driven.
- New-workflow creation is consistent and marks dirty via VM.
- Remaining window logic focuses on provisioning lists and navigation, which are the next targets for removal.