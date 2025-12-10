# Settings deep dive

## 1) AppSettings member classification
- **LogDir (string?)** — user/machine-scoped; local log folder should stay personal.
- **DefaultOutputDir (string?)** — project-scoped; drives deliverable location shared by batch exports.
- **DefaultRunAuditBeforeExport (bool)** — project-scoped default for batch operations.
- **DefaultSaveBefore (bool)** — project-scoped; relates to project file safety.
- **DefaultRecenterXY (bool)** — project-scoped geometry behavior for exports.
- **DefaultOverwrite (bool)** — project-scoped default overwrite policy for shared deliverables.
- **DefaultCleanup (bool)** — project-scoped cleanup toggle tied to model content.
- **OpenOutputFolder (bool)** — user-scoped convenience UX.
- **ValidateStagingArea (bool)** — project-scoped staging validation behavior.
- **SelectionSeedCategories (List<int>?)** — project-scoped selection strategy.
- **SelectionProxyCategories (List<int>?)** — project-scoped selection strategy.
- **SelectionProxyDistance (double)** — project-scoped selection behavior.
- **CleanupBlacklistCategories (List<int>?)** — project-scoped content rules.
- **ImageWhitelistCategoryIds (List<int>?)** — project-scoped visualization/export behavior.
- **Version (int)** — project-scoped settings version marker.
- **PreferredBatchLogColumns (List<string>?)** — user-scoped UI preference.
- **PreferredActions (List<string>?)** — ambiguous; could be project defaults or personal shortcuts — need product decision.
- **DeepAnnoStatus (bool)** — project-scoped model annotation behavior.
- **DryrunDiagnostics (bool)** — user-scoped troubleshooting toggle.
- **PurgeCompact (bool)** — project-scoped model maintenance behavior.
- **ThumbnailViewName (string?)** — project-scoped document behavior.
- **PerfDiagnostics (bool)** — user-scoped diagnostics toggle.
- **DrawAmbiguousRectangles (bool)** — user-scoped visualization preference.
- **Workflows (List<WorkflowDefinition>?)** — project-scoped workflow catalog.
- **SelectedWorkflowIds (List<string>?)** — project-scoped selection set for batch runs.
- **CurrentSetParameterName (string?)** — project-scoped shared parameter choice.
- **CurrentSetParameterGuid (string?)** — project-scoped shared parameter identifier.
- **SharedParametersFilePath (string?)** — ambiguous; could be project-shared but may vary per user path. Need guidance on shared parameter hosting.
- **StagingWidth/Height/Buffer (double)** — project-scoped staging geometry.
- **StageMoveMode (string)** — project-scoped staging behavior.
- **StagingAuthorizedUids (List<string>?)** — project-scoped whitelist.
- **StagingAuthorizedCategoryNames (List<string>?)** — project-scoped whitelist.

## 2) Storage logic in `AppSettings.cs`
- **Path construction**: `BaseDir` = `%LOCALAPPDATA%/GSADUs/Revit/Addin`; `FilePath` = `BaseDir/settings.json`.【F:src/GSADUs.Revit.Addin/AppSettings.cs†L82-L88】
- **Fallbacks**: hard-coded shared drive defaults: `FallbackLogDir` and `FallbackOutputDir` point to `G:\Shared drives\GSADUs Projects\Our Models\0 - CATALOG\Output`.【F:src/GSADUs.Revit.Addin/AppSettings.cs†L89-L91】
- **Load**: reads `settings.json` if it exists, deserializes with case-insensitive JSON, ensures workflow defaults, migrates RVT workflows, and enforces Sketch cleanup; otherwise returns a fresh default instance with the same defaults applied. Exceptions are swallowed.【F:src/GSADUs.Revit.Addin/AppSettings.cs†L93-L114】
- **Save**: creates the base directory, serializes with indentation and null-ignoring, writes to a temp file then replaces/moves to `settings.json`; exceptions swallowed.【F:src/GSADUs.Revit.Addin/AppSettings.cs†L116-L129】
- **Effective directories**: `GetEffectiveLogDir` and `GetEffectiveOutputDir` substitute the shared-drive fallbacks when `LogDir`/`DefaultOutputDir` are blank.【F:src/GSADUs.Revit.Addin/AppSettings.cs†L131-L141】
- **Workflow seeding/migration**: seeds default workflows when none exist and migrates RVT scopes/action IDs; writes back if changed. Also forces Sketch category into cleanup blacklist.【F:src/GSADUs.Revit.Addin/AppSettings.cs†L143-L200】【F:src/GSADUs.Revit.Addin/AppSettings.cs†L195-L200】【F:src/GSADUs.Revit.Addin/AppSettings.cs†L201-L214】

## 3) Concrete consumers of `AppSettings`
- **Infrastructure/SettingsPersistence**: wraps `AppSettingsStore.Load/Save` for DI. (Load/Save).【F:src/GSADUs.Revit.Addin/Infrastructure/SettingsPersistence.cs†L6-L9】 Project-scoped data handled as user-local today.
- **Services/WorkflowCatalogService**: loads settings in constructor, manipulates `Workflows` and `SelectedWorkflowIds`, saves via persistence. (Load/Save/workflow definitions). Project-scoped data stored locally.【F:src/GSADUs.Revit.Addin/Services/WorkflowCatalogService.cs†L15-L111】
- **Orchestration/BatchRunCoordinator**: loads settings before and after dialog; uses defaults (`DefaultRecenterXY`, `DefaultOverwrite`) and workflow selection (`SelectedWorkflowIds`, `Workflows`) to build action list and output directories via `AppSettingsStore.GetEffectiveOutputDir`. Project-scoped values. (Load/use).【F:src/GSADUs.Revit.Addin/Orchestration/BatchRunCoordinator.cs†L50-L88】【F:src/GSADUs.Revit.Addin/Orchestration/BatchRunCoordinator.cs†L103-L165】
- **Workflows/Csv/ExportCsvAction**: loads settings, filters selected CSV workflows, derives output directory via `GetEffectiveOutputDir`, respects `DefaultOverwrite`. Project-scoped catalog/output; overwrite policy project-scoped.【F:src/GSADUs.Revit.Addin/Workflows/Csv/ExportCsvAction.cs†L20-L88】【F:src/GSADUs.Revit.Addin/Workflows/Csv/ExportCsvAction.cs†L101-L143】
- **Workflows/Pdf/ExportPdfAction & PdfWorkflowRunner**: load settings to find selected workflows and output directory fallback; `DefaultOverwrite` honored. Project-scoped workflows/output.【F:src/GSADUs.Revit.Addin/Workflows/Pdf/ExportPdfAction.cs†L14-L29】【F:src/GSADUs.Revit.Addin/Workflows/Pdf/PdfWorkflowRunner.cs†L54-L77】
- **Workflows/Image/ExportImageAction**: multiple `AppSettingsStore.Load` calls to fetch image whitelist, default overwrite, and output directory. Output/whitelist project-scoped; perf/dryrun user-scoped.【F:src/GSADUs.Revit.Addin/Workflows/Image/ExportImageAction.cs†L115-L171】【F:src/GSADUs.Revit.Addin/Workflows/Image/ExportImageAction.cs†L390-L417】
- **Workflows/Rvt/ExportRvtAction**: loads settings, filters selected RVT workflows, and computes output directory via `GetEffectiveOutputDir`; uses project-scoped settings.【F:src/GSADUs.Revit.Addin/Workflows/Rvt/ExportRvtAction.cs†L17-L61】【F:src/GSADUs.Revit.Addin/Workflows/Rvt/ExportRvtAction.cs†L323-L333】
- **UI/SettingsWindow.xaml.cs**: initializes UI from `AppSettings`, shows hard-coded `%LOCALAPPDATA%` path, and saves all settings back to store. Mixes project and user-scoped concerns.【F:src/GSADUs.Revit.Addin/UI/SettingsWindow.xaml.cs†L45-L106】【F:src/GSADUs.Revit.Addin/UI/SettingsWindow.xaml.cs†L230-L269】
- **UI/WorkflowManagerWindow.xaml.cs**: stores an `AppSettings` reference for workflow management (project-scoped) and uses it to edit catalog selections; constructed with `AppSettingsStore.Load()`.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L35-L47】
- **UI/ViewModels/WorkflowManagerViewModel & Presenters**: consume `WorkflowCatalogService.Settings` (project-scoped workflows/selection).【F:src/GSADUs.Revit.Addin/Services/WorkflowCatalogService.cs†L15-L111】
- **UI/ViewModels/CsvWorkflowTabViewModel**: applies settings for CSV tab (project-scoped).【F:src/GSADUs.Revit.Addin/UI/ViewModels/PdfWorkflowTabViewModel.cs†L180-L183】
- **UI/BatchExportWindow.xaml.cs**: loads settings multiple times for defaults (`DefaultOverwrite`, `DefaultSaveBefore`, `SelectedWorkflowIds`, `Workflows`), computes log/output directories via `GetEffective...`, and persists selected workflow IDs. Mostly project-scoped, with minor user flags (DryrunDiagnostics).【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L41-L180】【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L730-L779】

## 4) Phase 1 concrete edit plan
- **Interfaces**
  - `public interface IProjectSettingsProvider { AppSettings Load(); void Save(AppSettings settings); string GetEffectiveOutputDir(AppSettings settings); }`
  - `public interface IUserSettingsProvider { AppSettings Load(); void Save(AppSettings settings); string GetEffectiveLogDir(AppSettings settings); bool DryrunDiagnostics { get; } }` (property proxies to current settings).
- **Adapter implementations (new files)**
  - `LegacyProjectSettingsProvider` (temporary): wraps `AppSettingsStore.Load/Save` and delegates `GetEffectiveOutputDir` to `AppSettingsStore.GetEffectiveOutputDir` to keep current behavior.
  - `LegacyUserSettingsProvider`: wraps the same store but exposes `GetEffectiveLogDir` and `DryrunDiagnostics` for user-only flags.
- **DI registration (`Infrastructure/DI/Startup.cs`)**
  - Add `services.AddSingleton<IProjectSettingsProvider, LegacyProjectSettingsProvider>();`
  - Add `services.AddSingleton<IUserSettingsProvider, LegacyUserSettingsProvider>();`
  - Keep existing `ISettingsPersistence` registration for backward compatibility.
- **Constructor/usage rewiring examples**
  - `BatchRunCoordinator`: inject `IProjectSettingsProvider projectSettings`; replace `AppSettingsStore.Load()` calls with `projectSettings.Load()` and `GetEffectiveOutputDir`. Preserve behavior by using same data object.
  - `WorkflowCatalogService`: inject `IProjectSettingsProvider` instead of `ISettingsPersistence`; use `provider.Load()`/`Save()` to manage `Workflows` and `SelectedWorkflowIds`.
  - (Optional third) `ExportCsvAction`: accept `IProjectSettingsProvider` via constructor, use it to load settings and resolve output directory.

## 5) Quick wins vs. risky areas
- **Quick wins**
  - `WorkflowCatalogService` and presenters: centralized workflow CRUD already goes through one service; swapping in `IProjectSettingsProvider` is low risk.
  - `ExportCsvAction`/`ExportPdfAction`/`ExportRvtAction`: each loads settings locally; constructor injection to use `IProjectSettingsProvider` is straightforward.
- **Risky/entangled areas**
  - `BatchRunCoordinator` and `BatchExportWindow`: multiple scattered loads, UI-derived defaults, and post-save updates to `SelectedWorkflowIds` mix project and user flags. Minimum viable change: inject providers but leave the same `AppSettings` shape; centralize a single load per run and pass the instance through dialog/view-models.
  - `SettingsWindow`: mixes all scopes and exposes the local path; needs UI split or filtering to avoid saving user-only toggles into project scope. Short term: limit `OK_Click` to update two separate DTOs (project vs user) before calling providers.
  - Image workflow whitelist and diagnostics toggles share file with project data; ensure new providers clarify scope to avoid regressions.
