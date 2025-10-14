# README Maintenance Instruction

This README file is auto-summarized from the actual codebase. If any files are added, removed, renamed, or changed, the next agent should:

1. Enumerate all files in the project directory to ensure the README structure matches the current codebase.
2. For each file:
   - If the file exists, update or add its summary based on its latest content.
   - If the file has been removed or renamed, delete or update its entry in the README.
   - If new files are found, add them to the README with a brief summary.
3. Ensure the README file structure and summaries remain consistent with the actual files present in the codebase.

This process keeps the README up-to-date and accurate for all contributors and agents.

#TODO: Automate this process with a script or CI pipeline for even more reliable maintenance in the future.

# File Structure

The following is the comprehensive file structure of the `src/GSADUs.Revit.Addin` project. Each file is listed with its filepath from the root and a brief summary of its content or purpose.

## Root Directory
- `src/GSADUs.Revit.Addin/AppSettings.cs`: Defines persisted application settings, default values, and helpers for loading/saving configuration to disk, including workflow seed data and effective path resolution. 
- `src/GSADUs.Revit.Addin/AuditAndCurate.cs`: Audits selection sets against configured categories, computes bounding boxes, detects ambiguities, and prepares curation plans while caching delete plans. 
  - #TODO: Split this 600+ LOC module into focused helpers/services to improve readability and make future edits safer. 
- `src/GSADUs.Revit.Addin/BatchExportCommand.cs`: Implements the external Revit command that launches batch export, wires up logging, and hands control to the service coordinator. 
- `src/GSADUs.Revit.Addin/BatchExportSettings.cs`: Record describing batch export requests, including selected set IDs, output preferences, and derived action identifiers. 
- `src/GSADUs.Revit.Addin/CuratePlan.cs`: Data transfer objects for curation results, capturing per-set membership deltas and ambiguity metadata. 
- `src/GSADUs.Revit.Addin/CuratePlanCache.cs`: ConditionalWeakTable-backed cache for storing and retrieving the most recent `CuratePlan` per document. 
- `src/GSADUs.Revit.Addin/PurgeUtil.cs`: Utility for purging unused element types with recursive fallback deletion and optional warning suppression. 
- `src/GSADUs.Revit.Addin/SelectionSetCategoryCache.cs`: Tracks built-in categories represented in selection sets for a document, using hashes and refresh helpers. 
- `src/GSADUs.Revit.Addin/Startup.cs`: Revit `IExternalApplication` entry point that initializes DI and registers the Batch Export ribbon button with icons. 

## Abstractions
- `src/GSADUs.Revit.Addin/Abstractions/IActionRegistry.cs`: Interfaces describing export action metadata and registry lookup capabilities. 
- `src/GSADUs.Revit.Addin/Abstractions/IBatchLog.cs`: Interfaces for manipulating batch logs, including factories, header management, and persistence contracts. 
- `src/GSADUs.Revit.Addin/Abstractions/IBatchRunCoordinator.cs`: Defines a coordinator capable of executing batch runs within Revit. 
- `src/GSADUs.Revit.Addin/Abstractions/IDialogService.cs`: UI abstraction for information dialogs, confirmations, and staging prompts. 
- `src/GSADUs.Revit.Addin/Abstractions/IExportAction.cs`: Contract for export actions including enablement checks and execution semantics against Revit documents. 
- `src/GSADUs.Revit.Addin/Abstractions/ILogSyncService.cs`: Synchronizes batch logs with live selection sets in a document. 
- `src/GSADUs.Revit.Addin/Abstractions/IOperationTimer.cs`: Disposable timer abstraction backed by performance logging. 
- `src/GSADUs.Revit.Addin/Abstractions/IWorkflow.cs`: Interfaces describing workflows and workflow registries exposed to the UI and coordinator. 
- `src/GSADUs.Revit.Addin/Abstractions/IWorkflowPlans.cs`: Workflow definition model and registry abstraction with selected-workflow filtering helpers. 

## Commands
- `src/GSADUs.Revit.Addin/Commands/ClearAmbiguityRectangles.cs`: Deletes model-line rectangles tagged for ambiguity visualization from the active document. 
- `src/GSADUs.Revit.Addin/Commands/ToggleAmbiguousRectangles.cs`: Toggles the persisted setting that controls drawing ambiguity rectangles and informs the user. 

## Curate
- `src/GSADUs.Revit.Addin/Curate/AmbiguityVisualizer.cs`: Builds plan-view rectangles around intersecting inflated bounding boxes to visualize ambiguous selection sets. 

## Domain
### Audit
- `src/GSADUs.Revit.Addin/Domain/Audit/AuditComputeCache.cs`: Per-document cache of category IDs and non-template views supporting audit computations. 

### Cleanup
- `src/GSADUs.Revit.Addin/Domain/Cleanup/DeletePlanCache.cs`: Stores and retrieves delete plans per document with helpers for clearing specific or all cached entries. 

## Infrastructure
- `src/GSADUs.Revit.Addin/Infrastructure/ActionRegistry.cs`: Concrete action registry seeded with known export actions and ordering metadata. 
- `src/GSADUs.Revit.Addin/Infrastructure/BatchExportPrefs.cs`: Loads and saves window layout preferences for the batch export UI, including column layout and filters. 
- `src/GSADUs.Revit.Addin/Infrastructure/DI/Startup.cs`: Configures dependency injection for commands, services, registries, and export actions. 
- `src/GSADUs.Revit.Addin/Infrastructure/DialogService.cs`: TaskDialog-backed implementation of the dialog service abstraction. 
- `src/GSADUs.Revit.Addin/Infrastructure/HashUtil.cs`: Hashing utilities providing FNV-1a helpers and legacy hashing fallbacks. 
- `src/GSADUs.Revit.Addin/Infrastructure/PerfTimer.cs`: Wraps `PerfLogger` scopes to implement `IOperationTimer`. 
- `src/GSADUs.Revit.Addin/Infrastructure/RevitAdapters/SelectionSets.cs`: Helpers for reading selection set elements, computing stable IDs, and hashing membership. 
- `src/GSADUs.Revit.Addin/Infrastructure/RevitUiContext.cs`: Static holder for the active `UIApplication` so modal UI can post commands. 
- `src/GSADUs.Revit.Addin/Infrastructure/SharedParameterService.cs`: Creates or binds shared parameters across categories with fallback file handling and binding logic. 
- `src/GSADUs.Revit.Addin/Infrastructure/WorkflowPlanRegistry.cs`: Registry exposing workflow definitions from settings with ordering and selection filters. 
- `src/GSADUs.Revit.Addin/Infrastructure/WorkflowRegistry.cs`: Registry of runtime workflows, including the batch export workflow wrapper. 

## Logging
- `src/GSADUs.Revit.Addin/Logging/BatchLog.cs`: In-memory batch log implementation supporting upserts, header management, validation, and persistence. 
- `src/GSADUs.Revit.Addin/Logging/CsvBatchLogger.cs`: Factory for creating/loading CSV-backed batch logs with guarded proxies. 
- `src/GSADUs.Revit.Addin/Logging/GuardedBatchLog.cs`: Decorator that sanitizes headers and strips legacy columns before delegating to the core batch log. 
- `src/GSADUs.Revit.Addin/Logging/LegacyGuards.cs`: Helper methods preserving compatibility with legacy workflows and data expectations. 
- `src/GSADUs.Revit.Addin/Logging/LogDateUtil.cs`: Date/time formatting helpers used across logging components. 
- `src/GSADUs.Revit.Addin/Logging/LogStatus.cs`: Constants describing log statuses for batch operations. 
- `src/GSADUs.Revit.Addin/Logging/LogSyncService.cs`: Ensures CSV logs stay in sync with live selection filters, marking missing sets and updating metadata. 
- `src/GSADUs.Revit.Addin/Logging/PerfLogger.cs`: Conditional performance logger writing CSV diagnostics when enabled. 
- `src/GSADUs.Revit.Addin/Logging/RunLog.cs`: Structured logging helper for batch runs with correlation IDs, sections, and failure tracking. 

## Orchestration
- `src/GSADUs.Revit.Addin/Orchestration/BatchRunCoordinator.cs`: Core coordinator driving batch export workflow selection, staging, action execution, and UI orchestration. 
  - #TODO: Refactor this 900+ LOC coordinator into smaller units and add targeted tests to reduce risk when evolving the pipeline. 
- `src/GSADUs.Revit.Addin/Orchestration/BatchRunOptions.cs`: Simple options object exposing selection sets and run preferences for orchestration helpers. 

## UI
- `src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs`: Batch export dialog code-behind that loads logs, manages selection set lists, workflow selection, and persists user preferences. 
  - #TODO: Break this 759-line code-behind into composable components or view-model logic to keep changes manageable. 
- `src/GSADUs.Revit.Addin/UI/CategoriesPickerWindow.xaml.cs`: Modal picker for category selection with singleton enforcement, filtering, and persistence. 
  - #TODO: Review this 300+ LOC window for opportunities to share logic with other pickers and reduce duplication. 
- `src/GSADUs.Revit.Addin/UI/ElementsPickerWindow.xaml.cs`: Modal picker for element selection with search, staging scope filters, and capped result sets. 
- `src/GSADUs.Revit.Addin/UI/ProgressWindow.xaml.cs`: Displays progress, elapsed time, and cancellation controls during long-running operations. 
- `src/GSADUs.Revit.Addin/UI/SelectionSetManagerWindow.xaml.cs`: Manages selection sets with auditing summaries, inline rename, staging actions, and log sync utilities. 
  - #TODO: Consider extracting reusable services from this 500+ LOC window to simplify future edits. 
- `src/GSADUs.Revit.Addin/UI/SettingsWindow.xaml.cs`: Settings dialog allowing configuration of directories, audit options, staging parameters, and workflow toggles. 
- `src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs`: Comprehensive workflow editor for creating, duplicating, and configuring export workflows across PDF, image, and RVT outputs. 
  - #TODO: Split this 1,200+ LOC file into smaller components (e.g., per-tab controllers) to improve maintainability. 

### ViewModels
- `src/GSADUs.Revit.Addin/UI/ViewModels/BatchExportWindowViewModel.cs`: View-model scaffold for batch export bindings including selected actions and set names. 
  - #TODO: Wire this scaffold into the XAML to replace code-behind state management. 
- `src/GSADUs.Revit.Addin/UI/ViewModels/SelectionSetManagerViewModel.cs`: View-model scaffold representing selection set rows and summary text for the manager window. 
  - #TODO: Integrate this view-model with the selection set manager UI to decouple UI logic from code-behind. 

## Workflows
### Image
- `src/GSADUs.Revit.Addin/Workflows/Image/ExportImageAction.cs`: Implements image export action with filename token expansion, scope handling, auto-crop diagnostics, and file normalization. 
- `src/GSADUs.Revit.Addin/Workflows/Image/ImageWorkflowKeys.cs`: Constants describing workflow parameter keys for image exports, including scope, naming, and crop settings. 

### Pdf
- `src/GSADUs.Revit.Addin/Workflows/Pdf/ExportPdfAction.cs`: Bridges selected PDF workflows to the PDF runner, invoking exports for each configured workflow. 
- `src/GSADUs.Revit.Addin/Workflows/Pdf/PdfWorkflowKeys.cs`: Parameter key definitions for PDF workflows (print set, export setup, naming pattern). 
- `src/GSADUs.Revit.Addin/Workflows/Pdf/PdfWorkflowRunner.cs`: Resolves view sets and export setups, sanitizes filenames, executes Revit PDF export, and reports generated artifacts. 

### Rvt
- `src/GSADUs.Revit.Addin/Workflows/Rvt/BackupCleanupAction.cs`: Removes Revit backup files created during export from the target directory. 
- `src/GSADUs.Revit.Addin/Workflows/Rvt/CleanupExportsAction.cs`: Executes element cleanup on exported documents using `ExportCleanup` when cleanup is enabled. 
- `src/GSADUs.Revit.Addin/Workflows/Rvt/ExportCleanup.cs`: Builds delete plans, filters preserved elements, and deletes model/annotation content with diagnostic reporting. 
  - #TODO: Expand this 380+ LOC utility with structured logging, persistence, and configurable include/exclude lists as noted in code comments. 
- `src/GSADUs.Revit.Addin/Workflows/Rvt/OpenForDryRunAction.cs`: Placeholder action for opening cloned documents in dry-run scenarios. 
  - #TODO: Implement actual document activation/opening once runner supplies cloned file context. 
- `src/GSADUs.Revit.Addin/Workflows/Rvt/ResaveRvtAction.cs`: Saves or compacts the exported document based on settings, ensuring final deliverables are persisted. 
- `src/GSADUs.Revit.Addin/Workflows/Rvt/RvtWorkflowKeys.cs`: Constant keys specific to RVT workflow options such as cleanup and purge toggles. 
- `src/GSADUs.Revit.Addin/Workflows/Rvt/RvtWorkflowRunner.cs`: Stub runner orchestrating RVT-specific steps with structured logging hooks. 
  - #TODO: Replace stubbed sections with real SaveAs, dry-run opening, cleanup, and backup removal implementations. 
- `src/GSADUs.Revit.Addin/Workflows/Rvt/SaveAsRvtAction.cs`: Saves the source document to the configured output directory with sanitized filenames and logging. 

## obj/x64/Debug
- `.NETCoreApp,Version=v8.0.AssemblyAttributes.cs`
- `GSADUs.Revit.Addin.AssemblyInfo.cs`
- `UI/BatchExportWindow.g.i.cs`
- `UI/CategoriesPickerWindow.g.i.cs`
- `UI/ElementsPickerWindow.g.i.cs`
- `UI/ProgressWindow.g.i.cs`
- `UI/SelectionSetManagerWindow.g.i.cs`
- `UI/SettingsWindow.g.i.cs`
- `UI/WorkflowManagerWindow.g.i.cs`
