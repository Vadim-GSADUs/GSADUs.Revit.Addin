# GSADUs Revit Add-in Cleanup & Refactor Plan

## Assumptions and constraints (agreed)
- Keep `OutputType.Rvt` in enums and settings for now (back-compat, no settings migration code).
- Do NOT change `IExportAction` signature in Phase 1. Only introduce temporary stubs if compilation requires it.
- Settings migration will be handled manually (e.g., delete or prune `Settings.json`). No extra code to manage old IDs.
- Keep the RVT tab in the UI/XAML as a generic placeholder; remove/guard RVT-specific code-behind and constants.
- Prefer deletion of RVT code and references; if removal causes unavoidable compile errors, introduce minimal no-op stubs to unblock the build, then remove them later.
- SDK-style project on .NET 8: deleting files should be sufficient; still run builds to catch stale references.
- Use a feature branch; commit in small, ordered steps and build after each step.

---

## Phase 1 – Purge RVT Workflow Code and References

### 1. Deletion Checklist (Workflows/Rvt)
| Target | Path | Notes |
| --- | --- | --- |
| `CleanupExportsAction` & helpers | `src/GSADUs.Revit.Addin/Workflows/Rvt/CleanupExportsAction.cs` **and** `src/GSADUs.Revit.Addin/Workflows/Rvt/ExportCleanup.cs` | Remove the action plus the multi-class helper module (`CleanupOptions`, `CleanupReport`, `CleanupDiagnostics`, `DeletePlan`, `ExportCleanup`, `CleanupFailures`). Track downstream types before deletion. |
| `BackupCleanupAction` | `src/GSADUs.Revit.Addin/Workflows/Rvt/BackupCleanupAction.cs` | Legacy file-system backup/cleanup runner; delete entirely. |
| `OpenForDryRunAction` | `src/GSADUs.Revit.Addin/Workflows/Rvt/OpenForDryRunAction.cs` | Dry-run document loader; delete entirely. |
| `ResaveRvtAction` | `src/GSADUs.Revit.Addin/Workflows/Rvt/ResaveRvtAction.cs` | Save-as orchestration of cloned doc; delete entirely. |
| `SaveAsRvtAction` | `src/GSADUs.Revit.Addin/Workflows/Rvt/SaveAsRvtAction.cs` | Primary export action; delete entirely. |
| `RvtWorkflowRunner` | `src/GSADUs.Revit.Addin/Workflows/Rvt/RvtWorkflowRunner.cs` | Composite runner that sequences the action set; delete entirely. |
| `RvtWorkflowKeys` | `src/GSADUs.Revit.Addin/Workflows/Rvt/RvtWorkflowKeys.cs` | Static key bag used by the RVT UI; delete. If the UI still references constants, add a temporary minimal stub (see Stubbing fallback). |

> Callout: Removing the files above eliminates RVT-specific types. No new redesign stubs should be kept beyond temporary compile-time shims noted below.

### 2. Reference Wipe Blueprint
Perform search-and-delete in the order shown to avoid compiler breakage.

#### `src/GSADUs.Revit.Addin/Infrastructure/DI/Startup.cs`
- Remove `using GSADUs.Revit.Addin.Workflows.Rvt;` (top of file).
- Delete the RVT action registrations inside `ConfigureServices()`:
  - `services.AddSingleton<IExportAction, CleanupExportsAction>();`
  - `services.AddSingleton<IExportAction, BackupCleanupAction>();`
- Collapse extra blank lines left behind.

#### `src/GSADUs.Revit.Addin/Infrastructure/ActionRegistry.cs`
- In the constructor list, remove the `ActionDescriptor` entries whose `Id` matches any of:
  - `"export-rvt"`
  - `"open-dryrun"`
  - `"cleanup"`
  - `"backup-cleanup"`
  - `"resave-rvt"`
- After deleting, re-run ordering to ensure remaining IDs use contiguous sort orders (update `Order` integers if gaps cause UI sorting issues).

#### `src/GSADUs.Revit.Addin/Infrastructure/WorkflowRegistry.cs` and `Infrastructure/WorkflowPlanRegistry.cs`
- Verify they do not resolve or require RVT action IDs or `OutputType.Rvt` specifics. Keep `OutputType.Rvt` enum value, but treat RVT workflows as inert/no-op until redesigned.

#### `src/GSADUs.Revit.Addin/Orchestration/BatchRunCoordinator.cs`
- Remove `using GSADUs.Revit.Addin.Workflows.Rvt;` at the top.
- Delete all variables and guard logic tied to RVT workflows:
  - The field `DeletePlan? deletePlan = null;` near the top of `RunOnce`.
  - The entire `if (workflows.Any(w => w.Output == OutputType.Rvt)) { ... }` block that injects RVT action IDs.
  - Any `DeletePlan` or `CleanupDiagnostics` locals (e.g., `planForThisRun`, `cleanupDiag`) and their initialization branches.
  - Remove calls that instantiate or reference RVT actions (`new CleanupDiagnostics()`, `new DeletePlan()` or `ExportCleanup` calls).
  - Strip branches that check `RequiresExternalClone` solely to satisfy RVT export.
- Update the execution pipeline so RVT-only IDs are no longer considered when building `chosenActionIds` or evaluating `resolved` actions.
- Keep `OutputType.Rvt` value but ignore RVT workflows in planning/execution.

#### `src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml`
- Keep the RVT tab (generic placeholder) in the XAML for now.
- Remove or comment out RVT-only controls that rely on deleted constants/handlers if they cause compile/XAML load errors.
- Reindex tab orders only if you remove controls; otherwise leave as-is.

#### `src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs`
- Keep the window but prune RVT-specific code-behind:
  - Drop fields/flags tied to RVT state (e.g., `_isDirtyRvt`).
  - Delete handlers wired to RVT controls (e.g., `RvtOption_Checked`, `RvtThumbnailCombo_SelectionChanged`, `RvtNewBtn_Click`).
  - Remove helper methods managing RVT thumbnails, scope combos, or SaveAs patterns.
  - Remove switch cases and mappings for `OutputType.Rvt` that inject RVT action IDs (e.g., `EnsureActionId(existing, "export-rvt")`).
- If code-behind still requires `RvtWorkflowKeys` constants, use a temporary stub (see Stubbing fallback) and mark with TODO to remove later.

#### `src/GSADUs.Revit.Addin/AppSettings.cs`
- In `EnsureWorkflowDefaults`, delete the seeded workflow whose `Output` is `OutputType.Rvt` and remove it from any default-selected lists.
- No automated settings migration code: you will manually prune stale RVT IDs from `Settings.json` (or delete the file to start fresh).
- Keep `OutputType.Rvt` in enums; treat RVT workflows as inert for now.

#### `src/GSADUs.Revit.Addin/Abstractions/IExportAction.cs`
- Do NOT change the interface signature in this phase.
- Ensure remaining action implementations (PDF/Image) no longer pass or depend on `CleanupDiagnostics` or `DeletePlan` values.
- If compilation fails due to missing types in method signatures, prefer adding temporary minimal stubs for those types instead of changing the interface.

#### `src/GSADUs.Revit.Addin/AuditAndCurate.cs`
- Delete `using GSADUs.Revit.Addin.Workflows.Rvt;`.
- Remove or comment out `BuildAndCacheDeletePlan` and other references to `ExportCleanup`/`DeletePlan`.
- Replace with a `// TODO` stub noting RVT purge removed this functionality pending redesign, unless a temporary stub type is introduced.

#### `src/GSADUs.Revit.Addin/Domain/Cleanup/DeletePlanCache.cs`
- Remove the `DeletePlan`-specific cache if no longer referenced.
- If referenced indirectly and you prefer zero behavior, reduce it to a no-op placeholder and add a `// TODO` explaining future home of staging plans.

#### Solution & Project Files
- Remove `Workflows/Rvt/*.cs` files. With SDK-style projects on .NET 8, explicit `.csproj` edits are usually unnecessary, but still validate after deletion.

### Stubbing fallback (only if needed to compile)
- Introduce a temporary `src/GSADUs.Revit.Addin/Legacy/Stubs.cs` with minimal no-op types referenced by the remaining code:
  - `namespace GSADUs.Revit.Addin.Workflows.Rvt { public static class RvtWorkflowKeys { /* include only constants still referenced by UI */ } }`
  - `namespace GSADUs.Revit.Addin.Domain.Cleanup { public sealed class DeletePlan { } public sealed class CleanupDiagnostics { } }`
- Keep these stubs as small as possible, add `// TODO` to remove them after Phase 3.

### 3. Sanity Pass
- Run `dotnet build` after each subsection (DI/registries, coordinator, UI, deletions) to surface any surviving references to deleted types.
- Add `#TODO` markers wherever RVT functionality leaves gaps (dialogs, batch sequencing) so future phases can trace work.

---

## Phase 2 – WorkflowManagerWindow Refactor (LOC Reduction)

### Responsibility Decomposition
1. Workflow Catalog & Persistence Controller
   - Proposed class: `WorkflowCatalogController`
   - Location: `src/GSADUs.Revit.Addin/UI/ViewModels/WorkflowManager/WorkflowCatalogController.cs`
   - Responsibilities: Manage in-memory `AppSettings.Workflows`, hydrate the saved combos, handle CRUD operations (`New`, `Clone`, `Delete`) and serialization back to `AppSettingsStore`.

2. PDF Configuration Presenter
   - Proposed class: `PdfWorkflowTabViewModel`
   - Location: `src/GSADUs.Revit.Addin/UI/ViewModels/WorkflowManager/Tabs/PdfWorkflowTabViewModel.cs`
   - Responsibilities: Encapsulate PDF tab data binding, selection change handlers, validation, and save state tracking (`_isDirtyPdf`, enabling Save button, file naming helpers).

3. Image Configuration Presenter
   - Proposed class: `ImageWorkflowTabViewModel`
   - Location: `src/GSADUs.Revit.Addin/UI/ViewModels/WorkflowManager/Tabs/ImageWorkflowTabViewModel.cs`
   - Responsibilities: Wrap image scope radio toggles, combo hydration, blacklist summary updates, and formatting helpers (e.g., `MapFormatToExt`). Also manage `_hydratingImage` guard logic.

4. Shared UI Orchestrator (Shell)
   - Proposed class: `WorkflowManagerShell`
   - Location: `src/GSADUs.Revit.Addin/UI/ViewModels/WorkflowManager/WorkflowManagerShell.cs`
   - Responsibilities: Maintain window-level concerns—singleton activation, dialog service coordination, top-level dirty-state routing, and delegating tab operations to the specialized view models above.

### Extraction Steps
- Create a `UI/ViewModels/WorkflowManager` folder to house new classes; ensure namespace is `GSADUs.Revit.Addin.UI.WorkflowManager` or similar.
- Convert existing event handlers into delegate calls on the new view models (e.g., `PdfSelectionChanged` becomes `PdfWorkflowTabViewModel.OnSelectionChanged`).
- Replace direct `FindName` lookups with strongly-typed bindings wherever feasible; if staying in code-behind, have the view models expose commands and observable properties bound via XAML.
- After extraction, shrink `WorkflowManagerWindow.xaml.cs` to orchestrate dependency creation and subscribe to view model events; aim to reduce LOC by at least 50%.
- Document follow-up TODOs for any functionality deferred to the future RVT plan.

---

## Phase 3 – BatchRunCoordinator Refactor (Service Abstraction)

### Interface Blueprint
1. `ISelectionSetResolutionService`
   - Purpose: Encapsulate the logic that gathers `SelectionFilterElement` objects, resolves set IDs/names, and performs validation (currently ~200 LOC in `RunOnce`).
   - Key Methods:
     - `SelectionResolutionResult Resolve(UIDocument uidoc, BatchExportSettings request);`
     - `void PersistSelections(AppSettings settings, SelectionResolutionResult result);`
   - Implementation Location: `src/GSADUs.Revit.Addin/Orchestration/Services/SelectionSetResolutionService.cs`.

2. `IWorkflowActionPlanner`
   - Purpose: Build the ordered list of `IExportAction` instances, enforce default action injection, and handle dry-run toggles (currently the `chosenActionIds` section).
   - Key Methods:
     - `WorkflowPlan BuildPlan(AppSettings settings, BatchExportSettings request, bool isDryRun);`
     - `IReadOnlyList<IExportAction> ResolveExecutables(WorkflowPlan plan, IEnumerable<IExportAction> registeredActions);`
   - Implementation Location: `src/GSADUs.Revit.Addin/Orchestration/Services/WorkflowActionPlanner.cs`.

3. `IExportExecutionService`
   - Purpose: Manage the execution loop that iterates actions, handles transactions, logging, cancelation tokens, and dialog messaging (the lower half of `RunOnce`).
   - Key Methods:
     - `RunOutcome Execute(UIApplication uiapp, UIDocument uidoc, WorkflowPlan plan, SelectionResolutionResult selectionState);`
   - Implementation Location: `src/GSADUs.Revit.Addin/Orchestration/Services/ExportExecutionService.cs`.

### Refactor Steps
- Carve `RunOnce` into three distinct stages: selection resolution, action planning, and execution. Each stage delegates to the new services.
- Move helper structs (`RunOutcome`, `BatchExportSettings` interactions) into a shared namespace if needed (`Orchestration.Models`).
- Adjust `BatchRunCoordinator` to act as a façade: request services via DI, call them in sequence, and orchestrate loop/retry logic only.
- Update `Infrastructure/DI/Startup.cs`:
  - Register the new services with `services.AddSingleton<ISelectionSetResolutionService, SelectionSetResolutionService>();` etc.
  - Remove direct `GetServices<IExportAction>()` from the coordinator once dependencies are injected via constructor or factory.
- Ensure `BatchExportWindow` passes along necessary parameters (e.g., dry-run flag) in a DTO consumed by the planner.
- After refactor, run `dotnet build` and create targeted unit tests for each service where feasible.

---

## Phase 4 – New RVT Workflow Plan (Future Specification)

### Document Blueprint (`docs/RVT_WORKFLOW_PLAN.md`)
Suggested outline for the forthcoming specification document:
1. Purpose & Scope – Define why the new workflow exists and the architectural goals (deterministic cloning, minimal Revit API risk, telemetry hooks).
2. Action Sequence Overview – Describe the canonical pipeline:
   - `CloneSourceDocument` ? `PrepareCloneForProcessing` ? `ExecuteProductionExports` ? `FinalizeDeliverable` ? `DisposeClone`.
3. Detailed Step Cards – For each stage include:
   - Preconditions
   - Primary class entry point
   - External dependencies (files, settings, APIs)
   - Error handling strategy and rollback notes.
4. Class & Interface Inventory – Proposed new types:
   - `RvtDocumentCloner`
   - `RvtClonePreparationService`
   - `RvtExportActionOrchestrator`
   - `RvtDeliverableSaver`
   - `RvtCloneDisposer`
   - Shared contracts (`IRvtWorkflowLogger`, `IRvtCleanupPolicyProvider`).
5. Configuration & Settings – Outline JSON/settings keys required for the workflow (e.g., output directory tokens, cleanup policy definitions).
6. Testing & Validation Plan – Manual regression steps, integration test hooks, telemetry validation.
7. Open Questions / TODOs – Capture unresolved design decisions discovered during Phases 1–3.

### Immediate Next Steps After Phase 3
- Draft `docs/RVT_WORKFLOW_PLAN.md` using the outline above before implementing any new code.
- Schedule design reviews with stakeholders to validate the new action sequence and dependency contracts.

---

Execution Notes
- Perform each phase sequentially; do not mix deletions with refactors to simplify testing and code review.
- Keep commits small and logically grouped (e.g., one commit per checklist section).
- Leverage Copilot/Visual Studio refactoring tools for safe extractions, but manually verify generated code.
