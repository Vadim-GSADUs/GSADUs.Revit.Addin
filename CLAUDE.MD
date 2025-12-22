# CLAUDE.MD - AI Assistant Guide

## Project Overview

**GSADUs Revit Batch Export Add-in** is a Revit 2026 add-in that provides batch export capabilities for selection sets with advanced curation, staging, and multi-format export workflows (PDF, images, RVT, CSV).

## Technology Stack

- **Platform**: .NET 8.0 (Windows)
- **UI Framework**: WPF
- **Target Application**: Autodesk Revit 2026
- **Architecture**: Dependency Injection (Microsoft.Extensions.DependencyInjection)
- **Language**: C# (latest version, nullable reference types enabled)

## Project Structure

```
GSADUs.Revit.BatchExport.sln
└── src/GSADUs.Revit.Addin/
    ├── Abstractions/       # Interface definitions for core services
    ├── Commands/           # Revit external commands
    ├── Curate/            # Ambiguity visualization logic
    ├── Domain/            # Domain models and audit caching
    ├── Infrastructure/     # Concrete implementations, adapters, DI setup
    ├── Logging/           # CSV batch logging, performance logging
    ├── Orchestration/     # Batch run coordination
    ├── UI/                # WPF windows, view models, converters
    ├── Workflows/         # Export action implementations (PDF, Image, CSV)
    └── Powershell/        # Deployment and diagnostic scripts
```

## Key Concepts

### Selection Sets
The core unit of work. Selection sets are named groups of Revit elements (by category) that can be:
- Audited for ambiguities (overlapping bounding boxes)
- Staged with proxy elements
- Exported in batches
- Tracked in CSV logs

### Workflows
User-defined export configurations that specify:
- Output format (PDF, image, RVT, CSV)
- Naming patterns with tokens (e.g., `{SetName}_{Date}`)
- Scope (seed elements vs. proxy elements)
- Format-specific settings (print setup, image format, crop settings)

### Batch Export Coordinator
The `BatchRunCoordinator` (src/GSADUs.Revit.Addin/Orchestration/BatchRunCoordinator.cs:84) orchestrates the entire batch export pipeline:
1. Workflow selection
2. Staging area validation
3. Audit execution
4. Action execution per selection set
5. Cleanup and purge operations
6. Log synchronization

### Staging Area
A controlled workspace where:
- Selection set seed elements are isolated
- Proxy elements are added based on proximity rules
- Ambiguities are detected and visualized
- Cleanup removes blacklisted categories

## Critical Files to Understand

### Entry Point
- `src/GSADUs.Revit.Addin/Startup.cs:33` - Revit IExternalApplication that initializes DI and ribbon UI

### Core Settings
- `src/GSADUs.Revit.Addin/AppSettings.cs:5` - Persisted configuration including directories, toggles, and workflow definitions
- Settings are saved to JSON via `SettingsPersistence`

### Main UI
- `src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs:89` - Primary batch export dialog (759 LOC, marked for refactoring)
- `src/GSADUs.Revit.Addin/UI/SelectionSetManagerWindow.xaml.cs:95` - Selection set management with audit summaries
- `src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs:98` - Workflow editor (1,200+ LOC, marked for refactoring)

### Export Actions
- `src/GSADUs.Revit.Addin/Workflows/Pdf/ExportPdfAction.cs:129` - PDF export implementation
- `src/GSADUs.Revit.Addin/Workflows/Image/ExportImageAction.cs:125` - Image export with auto-crop
- All implement `IExportAction` interface

### Logging
- `src/GSADUs.Revit.Addin/Logging/CsvBatchLogger.cs:74` - CSV batch log factory
- `src/GSADUs.Revit.Addin/Logging/LogSyncService.cs:79` - Synchronizes logs with live selection sets

## Known TODOs and Technical Debt

The codebase includes inline TODOs marking areas needing refactoring:

1. **AuditAndCurate.cs:24** - Split 600+ LOC module into focused helpers
2. **BatchRunCoordinator.cs:85** - Refactor 900+ LOC coordinator, add targeted tests
3. **BatchExportWindow.xaml.cs:90** - Break 759-line code-behind into components
4. **WorkflowManagerWindow.xaml.cs:99** - Split 1,200+ LOC into per-tab controllers
5. **CategoriesPickerWindow.xaml.cs:92** - Review for logic sharing opportunities
6. **SelectionSetManagerWindow.xaml.cs:95** - Extract reusable services

## Common Patterns

### Dependency Injection
All services are registered in `Infrastructure/DI/Startup.cs:61` and resolved via constructor injection.

### Settings Persistence
- Settings are loaded/saved via `ISettingsPersistence`
- Debounced save prevents excessive I/O
- Window preferences use separate `BatchExportPrefs`

### Revit API Access
- `RevitUiContext` holds active UIApplication for modal UI
- External commands receive UIApplication in Execute method
- Transactions wrap all model modifications

### Hashing and Caching
- `HashUtil` provides FNV-1a hashing for stable IDs
- `ConditionalWeakTable` caches (CuratePlanCache, SelectionSetCategoryCache) tied to document lifetime
- Deep annotation status toggle enables/disables expensive hashing

### View Models and Commands
- Base class: `WorkflowTabBaseViewModel:121` provides common save logic
- Commands use `DelegateCommand:102` wrapper
- Validation rules for pattern tokens: `PatternValidationRule:106`

## Working with This Codebase

### Building
- Requires Revit 2026 installed with `REVIT_2026_PATH` environment variable set
- Build outputs to `bin/x64/Debug` or `bin/x64/Release`
- Automatic deployment to `C:\GSADUs\RevitAddin\` on successful build

### Debugging
- Attach to Revit.exe after launching
- Logs written to configured `LogDir` (from AppSettings)
- Performance logging available via `PerfLogger:80` when enabled

### Adding New Export Actions
1. Implement `IExportAction:40`
2. Register in `ActionRegistry:59`
3. Add workflow definition UI in WorkflowManagerWindow
4. Add workflow keys constant class (e.g., PdfWorkflowKeys)

### Modifying Settings
- Add property to `AppSettings:5`
- Update UI in `SettingsWindow.xaml.cs:97`
- Increment `Version` field if breaking changes
- Settings auto-save with debouncing

### UI Development
- XAML + code-behind pattern (migration to full MVVM in progress)
- Converters in `UI/Converters/` for binding transformations
- Presenters coordinate between UI and domain logic
- Singleton pattern for pickers (enforced in code-behind)

## Architecture Decisions

### Why Separate Seed and Proxy Elements?
Selection sets define "seed" categories. Proxy categories are added dynamically based on proximity rules to capture related elements (like tags near equipment).

### Why CSV Logging?
- Human-readable
- Excel-compatible
- Version control friendly
- Legacy compatibility with existing workflows

### Why ConditionalWeakTable Caching?
Automatically clears when document is closed, preventing memory leaks without manual cleanup.

### Why Guard Pattern for Logs?
`GuardedBatchLog:75` strips legacy columns and sanitizes headers, enabling safe evolution of the log format while maintaining backward compatibility.

## Integration Points

### Revit API
- Element filtering by category
- Selection set manipulation
- View creation and management
- Export operations (PDF, image)
- Bounding box calculations

### File System
- Settings JSON in user profile
- CSV logs in configured directory
- Export outputs to user-specified folders
- Shared parameter definition files

### User Interaction
- TaskDialog for confirmations (via DialogService)
- Modal pickers for categories and elements
- Progress window with cancellation
- Ribbon button in Revit UI

## Testing Strategy

Currently minimal automated tests. Key areas that need coverage (per inline TODOs):
- Batch run coordinator pipeline stages
- Audit ambiguity detection logic
- Workflow parameter validation
- CSV log sync operations

## Deployment

Use PowerShell script:
```powershell
.\Powershell\Install-GSADUsAddin.ps1
```

This handles:
- Backup rotation
- DLL and JSON copying to Revit addins folder
- Manifest (.addin file) generation

## Performance Considerations

- Performance logging available via `PerfLogger` (opt-in)
- Deep annotation hashing is expensive - toggled via settings
- Audit cache reduces redundant element queries
- Dry-run diagnostics mode for testing without actual exports

## Common Issues and Solutions

### Settings Not Persisting
Check that `AppSettings.json` path is writable and not locked by another process.

### Export Failures
- Check RunLog output for correlation ID and error details
- Verify output directory is writable
- Confirm Revit document is saved if required by workflow

### Ambiguity Rectangles Not Showing
- Ensure toggle is enabled in settings
- Check that active view is a plan view
- Clear existing rectangles with `ClearAmbiguityRectangles` command

### Workflow Not Appearing
- Verify workflow is enabled in WorkflowManagerWindow
- Check that required parameters (print setup, naming pattern) are configured
- Ensure workflow definition was saved to settings

## Future Enhancements (Potential)

Based on code structure and TODOs:
- Full MVVM refactoring for all windows
- Automated testing suite
- Plugin architecture for custom export actions
- Cloud storage integration
- Batch processing multiple documents
- Undo/redo for selection set operations

## Questions to Ask When Modifying Code

1. **Does this change require a settings migration?** (Check `AppSettings.Version`)
2. **Will this affect existing logs?** (Consider GuardedBatchLog compatibility)
3. **Is this Revit API call transaction-safe?** (Modifications need transactions)
4. **Should this be cached?** (Expensive queries benefit from caching)
5. **Does the UI need to update?** (Settings changes may need UI sync)
6. **Will this work in all Revit versions?** (Currently targets 2026 only)

## Contact and Support

For issues, feature requests, or questions about this codebase, refer to the repository owner or project documentation.

---

**Last Updated**: 2025-12-22
**Claude Code Version**: This file is designed to help AI assistants (like Claude) understand and work effectively with this codebase.
