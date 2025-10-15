# WorkflowManagerWindow Phase 2 Analysis & Refactor Plan

## A. System map

### Module overview
| Area | Responsibilities | Inputs | Outputs | Key dependencies |
| --- | --- | --- | --- | --- |
| Startup & DI | Bootstraps the service provider and Revit ribbon button, registering coordinators, dialog/logging services, workflow registries, and export actions at add-in load. | Revit `UIControlledApplication`, Microsoft.Extensions DI | Ribbon button, singleton service provider | `ServiceCollection`, `BatchRunCoordinatorAdapter`, action implementations |
| Batch export command & coordinator | Entry point command that logs runs and delegates to the coordinator, which validates documents, opens selection UI, resolves workflows, and executes actions across selection sets. | `UIApplication`, `UIDocument`, selection filters, app settings | Batch export dialog results, action execution requests, file artifacts | `AppSettingsStore`, `SelectionSets`, `ActionRegistry`, `IExportAction` implementations |
| BatchExportWindow | Collects batch run preferences, maintains selection state, and persists window/grid layout along with selected workflows. | `UIDocument`, `AppSettings`, batch preferences | `BatchRunOptions` result, updated prefs/settings | `AppSettingsStore`, `BatchExportPrefs`, WPF grid/list controls |
| WorkflowManagerWindow | Singleton window responsible for catalog list management, tab hydration, validation, settings persistence, and dialog interactions across Main, RVT, PDF, Image, and CSV tabs. | `AppSettings`, optional active `Document`, dialog service | Updated `AppSettings.Workflows`, global flags, tab state | `AppSettingsStore`, `CategoriesPickerWindow`, Revit document APIs, direct WPF element lookups |
| Settings & workflow registries | `AppSettings` holds global flags, workflow definitions, and selected workflow IDs; registries surface plans and descriptors used in UI and orchestration. | JSON settings file, DI container | Default workflow seeds, ordered descriptors, selected workflows | `AppSettingsStore`, `WorkflowDefinition`, DI singleton lifetimes |
| Export actions & runners | PDF/image actions filter selected workflows and delegate to runners that execute Revit exports with filename sanitation, overwrite behavior, and validation. | `WorkflowDefinition.Parameters`, Revit documents, selected set names | Exported PDFs/images, diagnostics, sanitized filenames | `AppSettingsStore`, Revit export APIs, token expansion helpers |
| Utilities & context | Helpers expose selection set resolution, Revit UI context hand-off, and hashing utilities consumed by orchestration and UI. | Active `Document`, `UIApplication` | Selection metadata, UI context for posting commands | Revit API collectors, static context storage |

### Data flow and coupling highlights
- `WorkflowManagerWindow_Loaded` simultaneously populates PDF and Image controls from the active document, wiring events and applying saved parameters, which binds the PDF tab's lifecycle to Image tab hydration.
- Saving workflows pushes updates back into the global `AppSettings` list and writes to disk before refreshing UI sources, so catalog state, tab validation, and persistence are tightly coupled to the window code-behind.
- `BatchRunCoordinator` re-loads `AppSettings` after the selection dialog to merge chosen workflow IDs, then expands them into ordered action IDs while enforcing implicit guards (PDF/Image/RVT actions), intertwining settings persistence with execution logic.
- Export actions repeat settings loads inside `Execute`, creating redundant disk I/O and assuming global state is consistent with the workflow manager's recent writes.
- Action descriptors and default workflows still enumerate RVT actions (`export-rvt`, `resave-rvt`, etc.), so the orchestration layer continues to plan for external clones despite the RVT flow being retired.

### Implicit assumptions and fragile points
- Image tab logic assumes access to the active document for populating view lists, and falls back to settings when `_doc` is null; loss of the document context disables validation and scope toggling.
- PDF save enablement requires `{SetName}` in the pattern and both combos populated, so any divergence in bindings or parameter defaults can silently disable the save button.
- `BatchRunCoordinator` copies the source RVT file before running external actions and depends on `DeletePlan` objects seeded elsewhere, so staging/cleanup cross-cut the action pipeline without explicit interfaces.
- Image parameter persistence sanitizes prefixes/suffixes and strips extensions, while PDF persistence forces `.pdf`; inconsistent serialization increases the chance of mismatched expectations between UI and runners.
- `ImageWorkflowKeys` comments reference DPI constants (`DPI_72`, etc.) but the UI still emits `Low/Medium/High/Ultra`, implying downstream conversions must tolerate both forms.

## A1. Cross-tab base template normalization (all tabs)

Goal: make the shared scaffold identical across RVT, PDF, Image, CSV before migrating tab-specific logic.

Base template (top)
- Saved Workflow dropdown
- New Workflow button
- Name textbox
- Scope dropdown

Base template (bottom)
- Workflow Description textbox
- “Save Workflow” button

Non-behavioral normalizations
- Add root names for each tab container: `RvtTabRoot`, `PdfTabRoot`, `ImageTabRoot`, `CsvTabRoot`.
- Style: Save button uses an opacity trigger when `IsEnabled == false` (visual consistency).
- Keep all existing handlers and serialization unchanged during this pass.

ViewModel base
- Introduce `WorkflowTabBaseViewModel` (Name, Scope, Description, IsBaseSaveEnabled).
- PDF/Image tab VMs inherit and extend with tab-specific state and `IsSaveEnabled = IsBaseSaveEnabled && TabSpecificIsValid`.

Bindings (shared)
- `NameBox.Text` -> `Name`
- `ScopeCombo.SelectedItem` -> `Scope`
- `DescBox.Text` -> `Description`
- Save button `IsEnabled` -> tab VM `IsSaveEnabled` (initially can bind to base and later refine).
- Keep click handlers; no deletions yet.

Presenter wiring
- Presenter owns one VM per tab and sets each tab root `DataContext` in window construction/Loaded.

Persistence bridge
- In `SaveWorkflow_Click`, read from the tab VM first, then fall back to the control values (temporary). Preserve legacy code paths until bindings are fully verified.

## B. Dead/stale code inventory
- RVT tab persists RVT workflows even though RVT flow is retired; keep for now to normalize UI scaffold, but do not add new behavior.
- `RvtCleanupCheck_Changed` and `RvtCompactCheck_Changed` handlers are no longer referenced; candidates for deletion after binding migration.
- `ImageResolutionCombo_SelectionChanged` remains as an empty placeholder; delete once bindings replace it.
- Action registry defaults and DI wiring previously registered RVT actions; RVT registrations should remain quarantined until RVT redesign.
- `AppSettingsStore.EnsureWorkflowDefaults` seeds RVT defaults; to be updated in a later phase when the new workflow is designed.

## C. Refined Phase 2 implementation plan

### C1. Updated module responsibilities
- WorkflowManagerPresenter: mediates window lifecycle and selection routing; surfaces tab VMs and coordinates save/dirty signals.
- WorkflowCatalogService: encapsulates CRUD/persistence for `AppSettings.Workflows`; exposes observable collections for UI.
- PdfWorkflowTabViewModel: PDF tab state + validation; mirrors current enablement rules and parameter persistence.
- ImageWorkflowTabViewModel: Image tab state + validation; handles whitelist, scope toggles, parameter serialization.
- (Optional) WorkflowListViewModel: Main tab list sorting/filtering/navigation.

### C2. Extraction steps (minimal blast radius)
1) Cross-tab base normalization (this PR4 slice)
- Add tab root names; add disabled-opacity Save style in all tabs.
- Introduce `WorkflowTabBaseViewModel`; bind Name/Scope/Description and Save button to base properties across all tabs.
- Presenter sets `DataContext` per tab root. Keep all existing event handlers and persistence logic.

2) PDF tab migration
- Move PDF enablement logic into `PdfWorkflowTabViewModel` and bind:
  - `FileNamePatternBox.Text` -> `Pattern`
  - `ViewSetCombo.SelectedItem` -> `SelectedSetName`
  - `ExportSetupCombo.SelectedItem` -> `SelectedPrintSet`
- Wire `PdfSaveBtn.IsEnabled` to `PdfWorkflowTabViewModel.IsSaveEnabled`. Keep click handler.

3) Image tab migration
- Move scope toggle, preview, and validation to `ImageWorkflowTabViewModel`.
- Bind Single View/Print Set choice, pattern/prefix/suffix/format/resolution, and enablement state.

4) Replace `FindName` lookups
- Gradually replace direct lookups with bindings and commands. Start with read-only labels and enablement, then text/combos.

5) RVT tab decisions (defer removal)
- Keep normalized scaffold for visual consistency; do not extend logic.
- Remove RVT tab and related handlers only when the RVT redesign lands.

6) Orchestration alignment (later)
- Adjust `BatchExportWindow`/`BatchRunCoordinator` to consume `WorkflowCatalogService` instead of reloading `AppSettings` directly (no behavior change).

### C3. LOC reduction target
- Expect ~60–70% reduction in `WorkflowManagerWindow.xaml.cs` after moving PDF/Image logic and catalog CRUD into dedicated classes.

### C4. Scope, risks, testing, rollback
- Scope: workflow manager UI/persistence only; no runner behavior changes.
- Risks: binding regressions, serialization mismatches; mitigated by small slices and VM-first/fallback-to-control reads.
- Testing: smoke via window interactions and dry-run batch execution for PDF/Image workflows.
- Rollback: leave legacy handlers intact until bindings are verified; single-commit revert restores prior behavior.

## D. Shell/controller architecture options
- Option 1: MVVM commands/data binding across the board (max decoupling; larger change).
- Option 2: Presenter mediates events; gradual migration (chosen).
- Option 3: Mediator/event aggregator (overkill for current scope).

## E. Safe deletions & high-leverage cleanups
- Defer RVT tab removal; keep normalized scaffold. Remove RVT only with its redesign.
- Delete unused handlers (`RvtCleanupCheck_Changed`, `RvtCompactCheck_Changed`, `ImageResolutionCombo_SelectionChanged`) after bindings replace them.
- Quarantine RVT action registrations and default seeds; focus Phase 2 on PDF/Image.
- Normalize serialization helpers in `WorkflowCatalogService` for reuse across PDF/Image.
- Use `ISettingsPersistence` to debounce disk writes triggered from multiple UI paths.

## F. Consistency checklist for future contributions
- Naming & casing: align parameter keys and UI labels; remove legacy references (e.g., `Png`) when Image workflow is authoritative.
- Async/UI boundaries: ensure Revit API calls stay on UI thread; wrap long tasks with progress.
- Serialization formats: PDF patterns include `.pdf`; Image patterns are extensionless. Document and enforce.
- Validation rules: mirror UI validation in VMs and runners to tolerate malformed settings on load.
- Logging & diagnostics: retain `Trace`/`PerfLogger` entries; presenter/service expose hooks for telemetry.