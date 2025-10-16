diff --git a/docs/WorkflowManagerPhase2Reanalysis.md b/docs/WorkflowManagerPhase2Reanalysis.md
new file mode 100644
index 0000000000000000000000000000000000000000..ede8ca28e052815c6057f6293d34a65459c3c196
--- /dev/null
+++ b/docs/WorkflowManagerPhase2Reanalysis.md
@@ -0,0 +1,45 @@
+# Workflow Manager Phase 2 Reanalysis
+
+## Overview
+This report reassesses the current Workflow Manager implementation against the Phase 2 strategy, with emphasis on reducing LOC, flattening complexity, and adhering to the "stupid simple" (KISS) directive. Observations focus on the main window code-behind, associated view models, and supporting services.
+
+## Areas for LOC Reduction / Simplification
+
+1. **Residual control lookups in code-behind**  
+   * The window constructor still uses `FindName` to set tab data contexts instead of simple field references or XAML wiring, keeping boilerplate and fragile null-guard patterns in place.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L206-L214】  
+   * `UpdatePdfEnableState`, `UpdateCanSaveFor`, and similar helpers repeatedly query controls with `FindName`, duplicating logic that already exists in the PDF/Image view models. Removing these would collapse dozens of LOC and let bindings drive button enablement as planned.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L370-L455】
+
+2. **Duplicated state computation between VMs and code-behind**  
+   * `PdfWorkflowTabViewModel` exposes `IsSaveEnabled`, but the code-behind still recomputes the same guards manually, undermining the VM abstraction and keeping redundant LOC.【F:src/GSADUs.Revit.Addin/UI/ViewModels/PdfWorkflowTabViewModel.cs†L7-L23】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L370-L400】  
+   * Likewise `ImageWorkflowTabViewModel` calculates validation and preview text, yet the window retains `ComputeImageSaveEligibility`, `UpdateImagePreview`, and a large block of event handlers that manipulate controls directly.【F:src/GSADUs.Revit.Addin/UI/ViewModels/ImageWorkflowTabViewModel.cs†L9-L99】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L1045-L1154】
+
+3. **Hydration/persistence still control-centric**  
+   * `HydrateImageWorkflow` and `PersistImageParameters` use `FindName` on every field and mirror logic that already exists (or should exist) on the Image VM. Moving hydration to the presenter/VM would eliminate most of these methods and align with the target architecture.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L872-L1043】
+
+4. **Main-tab list/command plumbing repeats reflection lookups**  
+   * The main tab still creates anonymous objects for the list view and uses reflection when handling selection/double-click events. Introducing a lightweight DTO or using the observable collection directly would remove repeated reflection and string concatenation code.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L487-L707】
+
+## Deviations from WorkflowManagerPhase2Analysis.md
+
+* **Bindings vs. direct control manipulation** – Phase 2 step C2.2 calls for the PDF tab to bind enablement/selection to the VM. Persisting `UpdatePdfEnableState` and wiring handlers that reach into controls means that the migration is only half complete and diverges from the plan to eliminate `FindName` dependencies.【F:docs/WorkflowManagerPhase2Analysis.md†L108-L140】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L370-L400】
+* **Presenter owning tab state** – The presenter is supposed to mediate hydration, yet `WorkflowManagerWindow_Loaded` still performs document queries and populates collections directly. These responsibilities should move into the presenter/service layer per C1/C2, but currently the presenter remains a stub.【F:docs/WorkflowManagerPhase2Analysis.md†L92-L132】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L259-L360】【F:src/GSADUs.Revit.Addin/UI/Presenters/WorkflowManagerPresenter.cs†L8-L41】
+* **KISS / LOC reduction target** – The code-behind remains ~1.2k LOC, far from the 60–70% reduction goal in section C3. Continued reliance on ad-hoc helpers, defensive `try/catch` blocks, and manual syncing counteracts the KISS principle.【F:docs/WorkflowManagerPhase2Analysis.md†L134-L140】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L200-L1269】
+
+## Refactoring Suggestions (Stupid Simple Focus)
+
+1. **Bind tab roots directly in XAML** – Declare `DataContext` bindings for each tab root using `WorkflowManagerPresenter` properties (or expose via the window `DataContext`). This removes the `FindName` lookup loop and associated null checks.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L206-L214】
+2. **Let VMs drive save enablement** – Delete `UpdatePdfEnableState`, `ComputeImageSaveEligibility`, and related event handlers after ensuring XAML bindings reference VM properties. Simplify code-behind to a single `MarkDirty` call that toggles `_isDirty*` flags.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L370-L455】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L1045-L1154】
+3. **Move hydration/persistence into presenter** – Introduce presenter methods (`LoadPdfTab`, `LoadImageTab`, `PersistImage`) that take a `WorkflowDefinition` and update the VMs. The window would simply call presenter helpers and bind to VM state, dropping >200 LOC of manual UI manipulation.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L259-L360】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L872-L1043】
+4. **Replace anonymous list projections** – Expose an observable list of simple DTOs from `WorkflowCatalogService` or presenter so the main tab can bind without reflection or manual copy logic.【F:src/GSADUs.Revit.Addin/Services/WorkflowCatalogService.cs†L23-L65】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L487-L545】
+5. **Remove redundant try/catch shells** – Many UI helpers wrap every line in `try { } catch { }`, hiding real issues and adding LOC. Replace with targeted guards (null checks, optional chaining) so failures surface during development.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L223-L360】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L487-L707】
+
+## Recommended Next 3–5 Copilot-Friendly Tasks
+
+1. **“Bind PdfSaveBtn to PdfWorkflowTabViewModel”** – Remove `UpdatePdfEnableState` and update the XAML so the save button’s `IsEnabled`/`Opacity` rely solely on VM properties, deleting the redundant handler wiring.【F:src/GSADUs.Revit.Addin/UI/ViewModels/PdfWorkflowTabViewModel.cs†L7-L23】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L370-L455】
+2. **“Port Image tab enablement to ImageWorkflowTabViewModel”** – Eliminate `ComputeImageSaveEligibility`, `UpdateImagePreview`, and associated event handlers by binding XAML elements to VM properties (`Preview`, `IsSaveEnabled`, `ExportScope`).【F:src/GSADUs.Revit.Addin/UI/ViewModels/ImageWorkflowTabViewModel.cs†L9-L99】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L1045-L1154】
+3. **“Move workflow hydration into presenter”** – Add presenter methods to populate PDF/Image VMs and call them from `SavedCombo_SelectionChanged`, allowing removal of `HydrateImageWorkflow` and most of the `WorkflowManagerWindow_Loaded` logic.【F:src/GSADUs.Revit.Addin/UI/Presenters/WorkflowManagerPresenter.cs†L8-L41】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L259-L360】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L872-L1043】
+4. **“Expose workflow list items from WorkflowCatalogService”** – Return an observable projection (Id, Name, Output, Scope, Description) to bind directly, enabling deletion of the reflection-based list refresh code.【F:src/GSADUs.Revit.Addin/Services/WorkflowCatalogService.cs†L17-L65】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L487-L545】
+5. **“Simplify SaveWorkflow_Click to delegate to presenter”** – Encapsulate save logic in presenter/service so the window only routes user actions, trimming repeated parameter serialization and `FindName` usage.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L1156-L1250】
+
+## Conclusion
+The core window still shoulders legacy responsibilities that Phase 2 intended to decentralize. Prioritizing binding-driven enablement, presenter-mediated hydration, and VM-owned validation will deliver the largest LOC reductions while honoring the KISS mandate.