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
- `Powershell/Install-GSADUsAddin.ps1`: Deploys the latest add-in build to the configured Revit add-in folder, including backup rotation, DLL/json copying, and addin manifest generation.
- `powershell/Export-LastCopilotChat.ps1`: Locates the most recent GitHub Copilot chat snapshot and exports a readable log plus a raw copy with optional heuristics and telemetry.
- `src/GSADUs.Revit.Addin/AppSettings.cs`: Defines persisted application settings, default values, and helpers for loading/saving configuration to disk, including workflow seed data and effective path resolution.
- `src/GSADUs.Revit.Addin/AuditAndCurate.cs`: Audits selection sets against configured categories, computes bounding boxes, detects ambiguities, and prepares curation plans while caching delete plans.
  - #TODO: Split this 600+ LOC module into focused helpers/services to improve readability and make future edits safer.
- `src/GSADUs.Revit.Addin/BatchExportCommand.cs`: Implements the external Revit command that launches batch export, wires up logging, and hands control to the service coordinator.
- `src/GSADUs.Revit.Addin/BatchExportSettings.cs`: Record describing batch export requests, including selected set IDs, output preferences, and derived action identifiers.
- `src/GSADUs.Revit.Addin/CuratePlan.cs`: Data transfer objects for curation results, capturing per-set membership deltas and ambiguity metadata.
- `src/GSADUs.Revit.Addin/CuratePlanCache.cs`: ConditionalWeakTable-backed cache for storing and retrieving the most recent `CuratePlan` per document.
- `src/GSADUs.Revit.Addin/PurgeUtil.cs`: Utility for purging unused element types with recursive fallback deletion and optional warning suppression.
- `src/GSADUs.Revit.Addin/SelectionSetCategoryCache.cs`: Tracks built-in categories represented in selection sets for a document, using hashes and refresh helpers.
- `src/GSADUs.Revit.Addin/Services/WorkflowCatalogService.cs`: Manages CRUD operations over stored workflows, keeps observable collections in sync for the UI, and persists changes through the settings abstraction.
- `src/GSADUs.Revit.Addin/Startup.cs`: Revit `IExternalApplication` entry point that initializes DI and registers the Batch Export ribbon button with icons.

## Abstractions
- `src/GSADUs.Revit.Addin/Abstractions/IActionRegistry.cs`: Interfaces describing export action metadata and registry lookup capabilities.
- `src/GSADUs.Revit.Addin/Abstractions/IBatchLog.cs`: Interfaces for manipulating batch logs, including factories, header management, and persistence contracts.
- `src/GSADUs.Revit.Addin/Abstractions/IBatchRunCoordinator.cs`: Defines a coordinator capable of executing batch runs within Revit.
- `src/GSADUs.Revit.Addin/Abstractions/IDialogService.cs`: UI abstraction for information dialogs, confirmations, and staging prompts.
- `src/GSADUs.Revit.Addin/Abstractions/IExportAction.cs`: Contract for export actions including enablement checks and execution semantics against Revit documents.
- `src/GSADUs.Revit.Addin/Abstractions/ILogSyncService.cs`: Synchronizes batch logs with live selection sets in a document.
- `src/GSADUs.Revit.Addin/Abstractions/IOperationTimer.cs`: Disposable timer abstraction backed by performance logging.
- `src/GSADUs.Revit.Addin/Abstractions/ISettingsPersistence.cs`: Abstraction around loading and saving `AppSettings` to enable debounced persistence and testing seams.
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

## Infrastructure
- `src/GSADUs.Revit.Addin/Infrastructure/ActionRegistry.cs`: Concrete action registry seeded with known export actions and ordering metadata.
- `src/GSADUs.Revit.Addin/Infrastructure/BatchExportPrefs.cs`: Loads and saves window layout preferences for the batch export UI, including column layout and filters.
- `src/GSADUs.Revit.Addin/Infrastructure/DI/Startup.cs`: Configures dependency injection for commands, services, registries, and export actions.
- `src/GSADUs.Revit.Addin/Infrastructure/DialogService.cs`: TaskDialog-backed implementation of the dialog service abstraction.
- `src/GSADUs.Revit.Addin/Infrastructure/HashUtil.cs`: Hashing utilities providing FNV-1a helpers and legacy hashing fallbacks.
- `src/GSADUs.Revit.Addin/Infrastructure/PerfTimer.cs`: Wraps `PerfLogger` scopes to implement `IOperationTimer`.
- `src/GSADUs.Revit.Addin/Infrastructure/RevitAdapters/SelectionSets.cs`: Helpers for reading selection set elements, computing stable IDs, and hashing membership.
- `src/GSADUs.Revit.Addin/Infrastructure/RevitUiContext.cs`: Static holder for the active `UIApplication` so modal UI can post commands.
- `src/GSADUs.Revit.Addin/Infrastructure/SettingsPersistence.cs`: Concrete `ISettingsPersistence` that defers to `AppSettingsStore` for loading and saving configuration snapshots.
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

### Commands
- `src/GSADUs.Revit.Addin/UI/Commands/DelegateCommand.cs`: Lightweight `ICommand` wrapper that delegates execution and can-execute evaluation with optional requery notification.

### Converters
- `src/GSADUs.Revit.Addin/UI/Converters/EqualityToBooleanConverter.cs`: Converts equality checks between bound values and parameters to booleans with case-insensitive string handling and reverse binding support.
- `src/GSADUs.Revit.Addin/UI/Converters/PatternValidationRule.cs`: Validation rule ensuring naming patterns include required tokens, with a binding proxy helper for dynamic token lists.

### Presenters
- `src/GSADUs.Revit.Addin/UI/Presenters/WorkflowManagerPresenter.cs`: Coordinates workflow catalog CRUD, wiring commands, and Revit interactions for the workflow manager tabs, including PDF setup routing and whitelist management.

### ViewModels
- `src/GSADUs.Revit.Addin/UI/ViewModels/BatchExportWindowViewModel.cs`: View-model scaffold for batch export bindings including selected actions and set names.
  - #TODO: Wire this scaffold into the XAML to replace code-behind state management.
- `src/GSADUs.Revit.Addin/UI/ViewModels/CsvWorkflowTabViewModel.cs`: Backing model for CSV workflow tab with validation, pattern enforcement, and command surface for saving selections.
- `src/GSADUs.Revit.Addin/UI/ViewModels/ImageWorkflowTabViewModel.cs`: Image workflow tab view-model managing scope, format, whitelist, and validation state with save orchestration hooks.
- `src/GSADUs.Revit.Addin/UI/ViewModels/PdfWorkflowTabViewModel.cs`: PDF workflow tab view-model tracking selections, pattern validation, preview data, and save command routing.
- `src/GSADUs.Revit.Addin/UI/ViewModels/SavedWorkflowListItem.cs`: Simple DTO representing saved workflow list entries used across workflow tabs.
- `src/GSADUs.Revit.Addin/UI/ViewModels/SelectionSetManagerViewModel.cs`: View-model scaffold representing selection set rows and summary text for the manager window.
  - #TODO: Integrate this view-model with the selection set manager UI to decouple UI logic from code-behind.
- `src/GSADUs.Revit.Addin/UI/ViewModels/WorkflowManagerViewModel.cs`: Coordinates workflow collections, selection state, and tab switching while delegating CRUD actions to the presenter.
- `src/GSADUs.Revit.Addin/UI/ViewModels/WorkflowTabBaseViewModel.cs`: Shared base class providing common fields and save-enable logic for workflow tab view-models.

## Workflows
### Image
- `src/GSADUs.Revit.Addin/Workflows/Image/ExportImageAction.cs`: Implements image export action with filename token expansion, scope handling, auto-crop diagnostics, and file normalization.
- `src/GSADUs.Revit.Addin/Workflows/Image/ImageWorkflowKeys.cs`: Constants describing workflow parameter keys for image exports, including scope, naming, and crop settings.

### Pdf
- `src/GSADUs.Revit.Addin/Workflows/Pdf/ExportPdfAction.cs`: Bridges selected PDF workflows to the PDF runner, invoking exports for each configured workflow.
- `src/GSADUs.Revit.Addin/Workflows/Pdf/PdfWorkflowKeys.cs`: Parameter key definitions for PDF workflows (print set, export setup, naming pattern).
- `src/GSADUs.Revit.Addin/Workflows/Pdf/PdfWorkflowRunner.cs`: Resolves view sets and export setups, sanitizes filenames, executes Revit PDF export, and reports generated artifacts.
