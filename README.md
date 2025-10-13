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

The following is the comprehensive file structure of the `src\GSADUs.Revit.Addin` project. Each file is listed with itsfilepath from the root and a brief summary of its content or purpose.

## Root Directory
- `src/GSADUs.Revit.Addin/BatchExportCommand.cs`: Implements the `BatchExportCommand` class, which is an external Revit command. It coordinates batch export operations by interacting with the `BatchExportWindow` and `IBatchRunCoordinator` services. Updated to log document paths and ensure activation of existing windows.
- `src/GSADUs.Revit.Addin/BatchExportSettings.cs`: Defines the `BatchExportSettings` record, which encapsulates configuration options for batch export operations, such as set names, output directory, and various flags for processing behavior.
- `src/GSADUs.Revit.Addin/PurgeUtil.cs`: Provides utility methods for purging unused element types in a Revit document. It includes a best-effort approach to delete unused types over multiple passes, handling cascading references and suppressing warnings if needed.
- `src/GSADUs.Revit.Addin/Startup.cs`: Implements the `Startup` class, which initializes the add-in by setting up the Revit ribbon panel and adding a "Batch Export" button. It also initializes the dependency injection container early in the application lifecycle.
- `src/GSADUs.Revit.Addin/AppSettings.cs`: Defines the `AppSettings` class, which manages configuration settings for the add-in, including logging, output directories, workflow definitions, and various feature toggles. It also includes methods for loading and saving settings to a JSON file.

## Abstractions
- `src/GSADUs.Revit.Addin/Abstractions/IActionRegistry.cs`: Defines the `IActionRegistry` interface for managing and discovering actions, along with the `IActionDescriptor` interface for describing individual actions.
- `src/GSADUs.Revit.Addin/Abstractions/IBatchLog.cs`: Provides an interface for managing batch logs, including adding, updating, and removing rows, as well as persisting logs to files. Includes a factory interface for creating and loading logs.
- `src/GSADUs.Revit.Addin/Abstractions/IBatchRunCoordinator.cs`: Coordinates the execution of batch runs, managing the workflow and logging within the Revit environment.
- `src/GSADUs.Revit.Addin/Abstractions/IDialogService.cs`: Defines an interface for displaying dialogs to the user, including informational messages, confirmation prompts, and staging decisions.
- `src/GSADUs.Revit.Addin/Abstractions/IExportAction.cs`: Represents an export action, defining its execution logic, requirements, and conditions for enabling it within the application.
- `src/GSADUs.Revit.Addin/Abstractions/ILogSyncService.cs`: Synchronizes the batch log with the current state of the Revit document, ensuring consistency between selection sets and log entries.
- `src/GSADUs.Revit.Addin/Abstractions/IOperationTimer.cs`: Provides a disposable interface for timing operations, useful for performance tracking.
- `src/GSADUs.Revit.Addin/Abstractions/IWorkflow.cs`: Defines the `IWorkflow` interface for executing workflows and the `IWorkflowRegistry` interface for managing and discovering workflows.
- `src/GSADUs.Revit.Addin/Abstractions/IWorkflowPlans.cs`: Defines the `IWorkflowPlanRegistry` interface for managing workflow plans and the `WorkflowDefinition` class for describing individual workflows, including their actions, parameters, and metadata.

## Commands
- `src/GSADUs.Revit.Addin/Commands/ClearAmbiguityRectangles.cs`: Implements a command to clear ambiguity rectangles in the Revit model. It identifies and deletes rectangles tagged with "Ambiguity IBB".
- `src/GSADUs.Revit.Addin/Commands/ToggleAmbiguousRectangles.cs`: Implements a command to toggle the visibility of ambiguous rectangles in the Revit model. It updates the application settings to enable or disable the drawing of ambiguous rectangles.

## Curate
- `src/GSADUs.Revit.Addin/Curate/AmbiguityVisualizer.cs`: Provides functionality to visualize ambiguities in the Revit model by drawing rectangles around intersecting bounding boxes. It identifies ambiguous sets and creates model-line rectangles in the active or fallback plan view.

## Domain
### Audit
- `src/GSADUs.Revit.Addin/Domain/Audit/AuditComputeCache.cs`: Implements a per-document cache for category IDs and non-template view IDs used during audit computations. Dynamically builds and updates category sets and view lists based on application settings.

### Cleanup
- `src/GSADUs.Revit.Addin/Domain/Cleanup/DeletePlanCache.cs`: Implements a per-document cache for `DeletePlan` objects, providing methods to store, retrieve, and clear cached plans for Revit documents.
- `src/GSADUs.Revit.Addin/Domain/Cleanup/ExportCleanup.cs`: Implements cleanup logic for exported Revit files, including deletion plans, diagnostics, and reporting. Supports safe deletion ordering, preserve logic, and batch processing.

## Infrastructure
- `src/GSADUs.Revit.Addin/Infrastructure/ActionRegistry.cs`: Implements the `ActionRegistry` class, which manages a collection of actions available in the application. It provides methods to retrieve all actions or find specific actions by ID.
- `src/GSADUs.Revit.Addin/Infrastructure/BatchExportPrefs.cs`: Manages user preferences for the batch export window, including window dimensions, splitter ratios, and column settings. Preferences are persisted to a JSON file.
- `src/GSADUs.Revit.Addin/Infrastructure/DialogService.cs`: Implements the `IDialogService` interface, providing methods to display informational dialogs, confirmation prompts, and staging decisions to the user.
- `src/GSADUs.Revit.Addin/Infrastructure/HashUtil.cs`: Provides utility methods for generating hashes, including FNV-1a 64-bit hashes for strings and collections. Includes legacy methods for compatibility.
- `src/GSADUs.Revit.Addin/Infrastructure/PerfTimer.cs`: Implements the `PerfTimer` class, which measures the performance of operations using the `PerfLogger`.
- `src/GSADUs.Revit.Addin/Infrastructure/RevitAdapters/SelectionSets.cs`: Provides utilities for working with Revit selection sets, including methods to retrieve, compute, and hash selection set members.
- `src/GSADUs.Revit.Addin/Infrastructure/RevitUiContext.cs`: Holds the current `UIApplication` instance, allowing modal windows to post Revit commands.
- `src/GSADUs.Revit.Addin/Infrastructure/SharedParameterService.cs`: Manages shared parameters in Revit, including their creation, binding, and storage. Supports fallback mechanisms for shared parameter files.
- `src/GSADUs.Revit.Addin/RevitUiContext.cs`: Provides the context for Revit's UI interactions, including access to application and document.
- `src/GSADUs.Revit.Addin/WorkflowPlanRegistry.cs`: Registers and manages workflow plans, facilitating their discovery and execution.
- `src/GSADUs.Revit.Addin/WorkflowRegistry.cs`: Registers and manages workflows, enabling their execution and monitoring.
- `src/GSADUs.Revit.Addin/DI/Startup.cs`: Configures dependency injection for the application, registering services and interfaces.

## Logging
- `src/GSADUs.Revit.Addin/Logging/BatchLog.cs`: Implements the `BatchLog` class, which manages batch logs, including upserting rows, ensuring columns, and saving logs to CSV files. It supports legacy migration and validation of unique IDs.
- `src/GSADUs.Revit.Addin/Logging/CsvBatchLogger.cs`: Implements the `CsvBatchLogger` class, which acts as a factory for creating and managing batch logs. It provides a proxy for interacting with the `BatchLog` class.
- `src/GSADUs.Revit.Addin/Logging/GuardedBatchLog.cs`: Implements the `GuardedBatchLog` class, which decorates a batch log to strip legacy columns and filter out banned headers.
- `src/GSADUs.Revit.Addin/Logging/RunLog.cs`: Implements the `RunLog` class, which manages logging for batch export operations. It resolves the log directory based on user settings, environment variables, and fallbacks, and writes detailed logs for each operation.
- `src/GSADUs.Revit.Addin/LegacyGuards.cs`: Contains legacy guard functions for backwards compatibility, ensuring older workflows and data formats remain supported in the add-in.
- `src/GSADUs.Revit.Addin/LogDateUtil.cs`: Provides utility methods for handling and formatting log dates, supporting consistent date management across logging features.
- `src/GSADUs.Revit.Addin/LogStatus.cs`: Defines log statuses and related constants used within the application to track and manage the state of batch logs and export operations.
- `src/GSADUs.Revit.Addin/LogSyncService.cs`: Synchronizes logs between different components or services, ensuring consistency and up-to-date information in batch export workflows.
- `src/GSADUs.Revit.Addin/PerfLogger.cs`: Logs performance data, aiding in performance tuning and monitoring of batch export and workflow operations.

## Orchestration
- `src/GSADUs.Revit.Addin/BatchRunCoordinator.cs`: Orchestrates the execution of batch runs, coordinating between different services and components to manage workflow execution and logging. Includes new guards for missing workflows and enhanced staging area validation.
- `src/GSADUs.Revit.Addin/BatchRunOptions.cs`: Defines options for batch runs, including selection set and view templates, allowing customization of batch export behavior.

## UI
- `src/GSADUs.Revit.Addin/BatchExportWindow.xaml.cs`: Code-behind for the batch export window, initializes the window and its components, and binds UI events to logic for batch export operations.
- `src/GSADUs.Revit.Addin/CategoriesPickerWindow.xaml.cs`: Implements a modal window for picking Revit categories, supporting scope and discipline filtering, search, and selection management. Handles singleton instance logic and provides a filtered, grouped list of categories based on the current document and user choices.
- `src/GSADUs.Revit.Addin/ElementsPickerWindow.xaml.cs`: Implements a modal window for picking Revit elements, supporting scope selection (staging area or current set), search, and selection management. Displays element details and allows bulk selection/deselection, with a cap on the number of displayed items.
- `src/GSADUs.Revit.Addin/ProgressWindow.xaml.cs`: Displays progress information during batch operations, including status, elapsed time, and cancellation support. Handles UI updates and user-initiated cancellation requests.
- `src/GSADUs.Revit.Addin/SelectionSetManagerWindow.xaml.cs`: Manages saved selection sets, allowing users to audit, rename, delete, and stage selection sets. Displays set details, supports inline renaming, and synchronizes with batch logs and audit results.
- `src/GSADUs.Revit.Addin/SettingsWindow.xaml.cs`: Manages user preferences and settings for the add-in, including output directories, audit options, selection categories, staging parameters, and feature toggles. Provides UI for picking categories and elements, and persists settings.
- `src/GSADUs.Revit.Addin/WorkflowManagerWindow.xaml.cs`: Manages workflows and their configuration, allowing users to create, edit, duplicate, and delete workflows for PDF, RVT, Image, and CSV exports. Handles workflow parameters, UI state, and workflow persistence.

### ViewModels
- `src/GSADUs.Revit.Addin/BatchExportWindowViewModel.cs`: ViewModel for the batch export window, providing properties and collections for output directory, dry run mode, available set names, selected sets, and export actions. Implements property change notification for UI binding.
- `src/GSADUs.Revit.Addin/SelectionSetManagerViewModel.cs`: ViewModel for the selection set manager window, providing a collection of selection set rows and a summary property. Each row tracks set name, edit state, member counts, ambiguity, and details, with property change notification for UI binding.

## Workflows
### Image
- `src/GSADUs.Revit.Addin/ExportImageAction.cs`: Implements the image export action for workflows, supporting print set and single view export, file naming, cropping, and format options. Handles auto-crop logic, file normalization, and export settings.
- `src/GSADUs.Revit.Addin/ImageWorkflowKeys.cs`: Defines constant keys for image export workflow parameters, including print set, view selection, format, resolution, file naming, crop, and visual overrides.
### Pdf
- `src/GSADUs.Revit.Addin/ExportPdfAction.cs`: Implements the PDF export action for workflows, delegating execution to the PDF workflow runner and supporting batch export of selected workflows.
- `src/GSADUs.Revit.Addin/PdfWorkflowKeys.cs`: Defines constant keys for PDF export workflow parameters, including print set name, export setup name, and file name pattern.
- `src/GSADUs.Revit.Addin/PdfWorkflowRunner.cs`: Runs PDF export workflows, resolving print set and export setup, building output filenames, and executing the export with overwrite and file name sanitization logic. Updated to include enhanced logging and artifact tracking.

### Rvt
- `src/GSADUs.Revit.Addin/Workflows/Rvt/BackupCleanupAction.cs`: Cleans up Revit backup files in the export directory, removing known backup file variants after export operations.
- `src/GSADUs.Revit.Addin/Workflows/Rvt/CleanupExportsAction.cs`: Performs cleanup of exported Revit files, invoking element and data cleanup logic on external clones after export.
- `src/GSADUs.Revit.Addin/Workflows/Rvt/OpenForDryRunAction.cs`: Opens a Revit document for dry run mode, ensuring the cloned document is activated for testing without making changes.
- `src/GSADUs.Revit.Addin/Workflows/Rvt/RvtWorkflowKeys.cs`: Defines constant keys for RVT workflow parameters, such as cleanup and purge actions.
- `src/GSADUs.Revit.Addin/Workflows/Rvt/RvtWorkflowRunner.cs`: Runs RVT export workflows, orchestrating cloning, cleanup, saving, and backup removal for batch export operations.
- `src/GSADUs.Revit.Addin/Workflows/Rvt/SaveAsRvtAction.cs`: Saves the Revit document as a new RVT file, using compact and overwrite options, and naming the file after the selection set.

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