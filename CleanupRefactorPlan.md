diff --git a/docs/CleanupRefactorPlan.md b/docs/CleanupRefactorPlan.md
new file mode 100644
index 0000000000000000000000000000000000000000..845bdc9d4b2ad7105d22df856573d18caf22241d
--- /dev/null
+++ b/docs/CleanupRefactorPlan.md
@@ -0,0 +1,181 @@
+# GSADUs Revit Add-in Cleanup & Refactor Plan
+
+## Phase 1 – Purge RVT Workflow Code and References
+
+### 1. Deletion Checklist (Workflows/Rvt)
+| Target | Path | Notes |
+| --- | --- | --- |
+| `CleanupExportsAction` & helpers | `src/GSADUs.Revit.Addin/Workflows/Rvt/CleanupExportsAction.cs` **and** `src/GSADUs.Revit.Addin/Workflows/Rvt/ExportCleanup.cs` | Remove the action plus the multi-class helper module (`CleanupOptions`, `CleanupReport`, `CleanupDiagnostics`, `DeletePlan`, `ExportCleanup`, `CleanupFailures`). Track downstream types before deletion. |
+| `BackupCleanupAction` | `src/GSADUs.Revit.Addin/Workflows/Rvt/BackupCleanupAction.cs` | Legacy file-system backup/cleanup runner; delete entirely. |
+| `OpenForDryRunAction` | `src/GSADUs.Revit.Addin/Workflows/Rvt/OpenForDryRunAction.cs` | Dry-run document loader; delete entirely. |
+| `ResaveRvtAction` | `src/GSADUs.Revit.Addin/Workflows/Rvt/ResaveRvtAction.cs` | Save-as orchestration of cloned doc; delete entirely. |
+| `SaveAsRvtAction` | `src/GSADUs.Revit.Addin/Workflows/Rvt/SaveAsRvtAction.cs` | Primary export action; delete entirely. |
+| `RvtWorkflowRunner` | `src/GSADUs.Revit.Addin/Workflows/Rvt/RvtWorkflowRunner.cs` | Composite runner that sequences the action set; delete entirely. |
+| `RvtWorkflowKeys` | `src/GSADUs.Revit.Addin/Workflows/Rvt/RvtWorkflowKeys.cs` | Static key bag used by the RVT UI; delete entirely. |
+
+> **Callout:** Removing the files above eliminates every RVT-specific type; no additional stubs should be kept because the workflow will be redesigned later.
+
+### 2. Reference Wipe Blueprint
+The following checklist is structured like an annotated README. Each subsection lists the exact places that must be edited once the RVT files are removed. Search-and-delete in the order shown to avoid compiler breakage.
+
+#### `src/GSADUs.Revit.Addin/Infrastructure/DI/Startup.cs`
+- Remove `using GSADUs.Revit.Addin.Workflows.Rvt;` (top of file).
+- Delete the two singleton registrations inside `ConfigureServices()`:
+  - `services.AddSingleton<IExportAction, CleanupExportsAction>();`
+  - `services.AddSingleton<IExportAction, BackupCleanupAction>();`
+- If no other action registrations remain in that region, collapse double blank lines left behind.
+
+#### `src/GSADUs.Revit.Addin/Infrastructure/ActionRegistry.cs`
+- In the constructor list, remove the `ActionDescriptor` entries whose `Id` matches any of:
+  - `"export-rvt"`
+  - `"open-dryrun"`
+  - `"cleanup"`
+  - `"backup-cleanup"`
+  - `"resave-rvt"`
+- After deleting, re-run ordering to ensure remaining IDs use contiguous sort orders (update the `Order` integers if gaps cause UI sorting issues).
+
+#### `src/GSADUs.Revit.Addin/Orchestration/BatchRunCoordinator.cs`
+- Remove `using GSADUs.Revit.Addin.Workflows.Rvt;` at the top.
+- Delete all variables and guard logic tied to RVT workflows:
+  - The field `DeletePlan? deletePlan = null;` near the top of `RunOnce`.
+  - The entire `if (workflows.Any(w => w.Output == OutputType.Rvt)) { ... }` block that injects RVT action IDs.
+  - Any `DeletePlan` or `CleanupDiagnostics` local variables (e.g., `planForThisRun`, `cleanupDiag`) and their initialization branches inside the run loop.
+  - Remove calls that instantiate RVT actions (`new CleanupDiagnostics()`, `new DeletePlan()` or `ExportCleanup` calls).
+  - Strip branches that check `RequiresExternalClone` solely to satisfy RVT export (revisit clone requirements once new workflow spec is drafted in Phase 4).
+- Update the execution pipeline so that RVT-only IDs are no longer referenced when building `chosenActionIds` or evaluating `resolved` actions.
+- Ensure any references to `DeletePlanCache` or `AuditAndCurate` remain only if they serve non-RVT exports; otherwise flag them for removal.
+
+#### `src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml`
+- Remove the entire `<TabItem>` for RVT (controls named `RvtSavedCombo`, `RvtNewBtn`, `RvtScopeCombo`, `RvtCleanupBox`, etc.).
+- Delete RVT-only buttons, combo boxes, and labels to avoid dangling event handlers.
+- After removal, reindex tab orders so that PDF becomes the first export tab (adjust any `TabIndex` or `SelectedIndex` assumptions in the code-behind).
+
+#### `src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs`
+- Drop any fields or flags tied to RVT state (`_isDirtyRvt`, `_isDirtyCsv` remains, `_imageBlacklistIds` is shared so keep, but remove RVT-specific booleans, event handlers, and helper methods such as `PopulateRvtThumbnailList()`).
+- Delete event handlers wired from XAML (e.g., `RvtOption_Checked`, `RvtThumbnailCombo_SelectionChanged`, `RvtNewBtn_Click`, `SaveWorkflow_Click` with `Tag="Rvt"`).
+- Remove helper methods managing RVT thumbnails, scope combos, or SaveAs patterns.
+- Update logic that maps `OutputType.Rvt` (switch expressions, dictionary lookups, `EnsureActionId(existing, "export-rvt")`, etc.) so it no longer expects RVT entries.
+- Rebaseline `_isDirty*` tracking arrays so indices align with the reduced tab set.
+
+#### `src/GSADUs.Revit.Addin/AppSettings.cs`
+- In `EnsureWorkflowDefaults`, delete the seeded workflow whose `Output` is `OutputType.Rvt` and remove it from any default-selected lists.
+- Inspect `SelectedWorkflowIds` defaults to make sure no RVT IDs remain; trim stale IDs from persisted settings migration code if necessary.
+
+#### `src/GSADUs.Revit.Addin/Abstractions/IExportAction.cs`
+- Remove the `using GSADUs.Revit.Addin.Workflows.Rvt;` directive.
+- Update the `Execute` signature to drop the `CleanupDiagnostics? cleanupDiag` and `DeletePlan? planForThisRun` parameters. Document the change so remaining actions adjust their implementations accordingly.
+- Re-run through every implementation (`Workflows/Pdf/ExportPdfAction.cs`, `Workflows/Image/ExportImageAction.cs`, etc.) and strip unused parameters.
+
+#### `src/GSADUs.Revit.Addin/AuditAndCurate.cs`
+- Delete `using GSADUs.Revit.Addin.Workflows.Rvt;`.
+- Comment out or remove the `BuildAndCacheDeletePlan` helper and any references to `ExportCleanup`/`DeletePlan`. Replace with a `// TODO` stub noting that RVT purge removed this functionality pending redesign.
+
+#### `src/GSADUs.Revit.Addin/Domain/Cleanup/DeletePlanCache.cs`
+- Remove the `DeletePlan`-specific cache or relocate it under a new namespace only when the new workflow is defined. For now, delete the file or reduce it to an empty placeholder with a `// TODO` comment explaining the future home of staging plans.
+
+#### `src/GSADUs.Revit.Addin/Infrastructure/DI/Startup.cs` (follow-up)
+- After interface signature changes (above), update any registrations or factories that referenced `DeletePlan` or `CleanupDiagnostics` to avoid build errors.
+
+#### Solution & Project Files
+- Remove `Workflows/Rvt/*.cs` includes from the `.csproj` (if explicit) once deletions occur.
+
+### 3. Sanity Pass
+- Run `dotnet build` to surface any surviving references to deleted types.
+- Add `#TODO` markers wherever RVT functionality leaves gaps (dialogs, batch sequencing) so future phases can trace work.
+
+## Phase 2 – WorkflowManagerWindow Refactor (LOC Reduction)
+
+### Responsibility Decomposition
+1. **Workflow Catalog & Persistence Controller**
+   - **Proposed class:** `WorkflowCatalogController`
+   - **Location:** `src/GSADUs.Revit.Addin/UI/ViewModels/WorkflowManager/WorkflowCatalogController.cs`
+   - **Responsibilities:** Manage in-memory `AppSettings.Workflows`, hydrate the saved combos, handle CRUD operations (`New`, `Clone`, `Delete`) and serialization back to `AppSettingsStore`.
+
+2. **PDF Configuration Presenter**
+   - **Proposed class:** `PdfWorkflowTabViewModel`
+   - **Location:** `src/GSADUs.Revit.Addin/UI/ViewModels/WorkflowManager/Tabs/PdfWorkflowTabViewModel.cs`
+   - **Responsibilities:** Encapsulate PDF tab data binding, selection change handlers, validation, and save state tracking (`_isDirtyPdf`, enabling Save button, file naming helpers).
+
+3. **Image Configuration Presenter**
+   - **Proposed class:** `ImageWorkflowTabViewModel`
+   - **Location:** `src/GSADUs.Revit.Addin/UI/ViewModels/WorkflowManager/Tabs/ImageWorkflowTabViewModel.cs`
+   - **Responsibilities:** Wrap image scope radio toggles, combo hydration, blacklist summary updates, and formatting helpers (e.g., `MapFormatToExt`). Also manage `_hydratingImage` guard logic.
+
+4. **Shared UI Orchestrator (Shell)**
+   - **Proposed class:** `WorkflowManagerShell`
+   - **Location:** `src/GSADUs.Revit.Addin/UI/ViewModels/WorkflowManager/WorkflowManagerShell.cs`
+   - **Responsibilities:** Maintain window-level concerns—singleton activation, dialog service coordination, top-level dirty-state routing, and delegating tab operations to the specialized view models above.
+
+### Extraction Steps
+- Create a `UI/ViewModels/WorkflowManager` folder to house new classes; ensure namespace is `GSADUs.Revit.Addin.UI.WorkflowManager` or similar.
+- Convert existing event handlers into delegate calls on the new view models (e.g., `PdfSelectionChanged` becomes `PdfWorkflowTabViewModel.OnSelectionChanged`).
+- Replace direct `FindName` lookups with strongly-typed bindings wherever feasible; if staying in code-behind, have the view models expose commands and observable properties bound via XAML.
+- After extraction, shrink `WorkflowManagerWindow.xaml.cs` to orchestrate dependency creation and subscribe to view model events; aim to reduce LOC by at least 50%.
+- Document follow-up TODOs for any functionality deferred to the future RVT plan.
+
+## Phase 3 – BatchRunCoordinator Refactor (Service Abstraction)
+
+### Interface Blueprint
+1. **`ISelectionSetResolutionService`**
+   - **Purpose:** Encapsulate the logic that gathers `SelectionFilterElement` objects, resolves set IDs/names, and performs validation (currently ~200 LOC in `RunOnce`).
+   - **Key Methods:**
+     - `SelectionResolutionResult Resolve(UIDocument uidoc, BatchExportSettings request);`
+     - `void PersistSelections(AppSettings settings, SelectionResolutionResult result);`
+   - **Implementation Location:** `src/GSADUs.Revit.Addin/Orchestration/Services/SelectionSetResolutionService.cs`.
+
+2. **`IWorkflowActionPlanner`**
+   - **Purpose:** Build the ordered list of `IExportAction` instances, enforce default action injection, and handle dry-run toggles (currently the `chosenActionIds` section).
+   - **Key Methods:**
+     - `WorkflowPlan BuildPlan(AppSettings settings, BatchExportSettings request, bool isDryRun);`
+     - `IReadOnlyList<IExportAction> ResolveExecutables(WorkflowPlan plan, IEnumerable<IExportAction> registeredActions);`
+   - **Implementation Location:** `src/GSADUs.Revit.Addin/Orchestration/Services/WorkflowActionPlanner.cs`.
+
+3. **`IExportExecutionService`**
+   - **Purpose:** Manage the execution loop that iterates actions, handles transactions, logging, cancelation tokens, and dialog messaging (the lower half of `RunOnce`).
+   - **Key Methods:**
+     - `RunOutcome Execute(UIApplication uiapp, UIDocument uidoc, WorkflowPlan plan, SelectionResolutionResult selectionState);`
+   - **Implementation Location:** `src/GSADUs.Revit.Addin/Orchestration/Services/ExportExecutionService.cs`.
+
+### Refactor Steps
+- Carve `RunOnce` into three distinct stages: selection resolution, action planning, and execution. Each stage delegates to the new services.
+- Move helper structs (`RunOutcome`, `BatchExportSettings` interactions) into a shared namespace if needed (`Orchestration.Models`).
+- Adjust `BatchRunCoordinator` to act as a façade: request services via DI, call them in sequence, and orchestrate loop/retry logic only.
+- Update `Infrastructure/DI/Startup.cs`:
+  - Register the new services with `services.AddSingleton<ISelectionSetResolutionService, SelectionSetResolutionService>();` etc.
+  - Remove direct `GetServices<IExportAction>()` from the coordinator once dependencies are injected via constructor or factory.
+- Ensure `BatchExportWindow` passes along necessary parameters (e.g., dry-run flag) in a DTO consumed by the planner.
+- After refactor, run `dotnet build` and create targeted unit tests for each service where feasible.
+
+## Phase 4 – New RVT Workflow Plan (Future Specification)
+
+### Document Blueprint (`docs/RVT_WORKFLOW_PLAN.md`)
+Suggested outline for the forthcoming specification document:
+1. **Purpose & Scope** – Define why the new workflow exists and the architectural goals (deterministic cloning, minimal Revit API risk, telemetry hooks).
+2. **Action Sequence Overview** – Describe the canonical pipeline:
+   - `CloneSourceDocument` → `PrepareCloneForProcessing` → `ExecuteProductionExports` → `FinalizeDeliverable` → `DisposeClone`.
+3. **Detailed Step Cards** – For each stage include:
+   - Preconditions
+   - Primary class entry point
+   - External dependencies (files, settings, APIs)
+   - Error handling strategy and rollback notes.
+4. **Class & Interface Inventory** – Proposed new types:
+   - `RvtDocumentCloner`
+   - `RvtClonePreparationService`
+   - `RvtExportActionOrchestrator`
+   - `RvtDeliverableSaver`
+   - `RvtCloneDisposer`
+   - Shared contracts (`IRvtWorkflowLogger`, `IRvtCleanupPolicyProvider`).
+5. **Configuration & Settings** – Outline JSON/settings keys required for the workflow (e.g., output directory tokens, cleanup policy definitions).
+6. **Testing & Validation Plan** – Manual regression steps, integration test hooks, telemetry validation.
+7. **Open Questions / TODOs** – Capture unresolved design decisions discovered during Phases 1–3.
+
+### Immediate Next Steps After Phase 3
+- Draft `docs/RVT_WORKFLOW_PLAN.md` using the outline above before implementing any new code.
+- Schedule design reviews with stakeholders to validate the new action sequence and dependency contracts.
+
+---
+
+**Execution Notes**
+- Perform each phase sequentially; do not mix deletions with refactors to simplify testing and code review.
+- Keep commits small and logically grouped (e.g., one commit per checklist section).
+- Leverage Copilot/Visual Studio refactoring tools for safe extractions, but manually verify generated code.