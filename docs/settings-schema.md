# Project settings Extensible Storage schema

This document defines the Phase 2 Extensible Storage schema used by `EsProjectSettingsProvider`.
It is intentionally simple: we store a serialized `AppSettings` payload (minus runtime-only fields) in ES.
Phase 3 will introduce a more normalized, project-focused DTO.

## Schema identity

- **Schema name**: `GSADUs_ProjectSettings`
- **Schema GUID**: `385875C5-79D1-44E2-A31C-7C961AE4D5B0`  <!-- generate once and keep stable -->
- **Scope**: one entity per RVT document

## Fields

### 1. SettingsVersion

- **Field name**: `SettingsVersion`
- **Type**: `int`
- **Description**: Mirrors `AppSettings.Version`. Used for future migrations of the serialized payload shape.

### 2. SettingsJson

- **Field name**: `SettingsJson`
- **Type**: `string`
- **Description**: JSON-serialized `AppSettings` clone containing only project-scoped members.
- **Contained properties** (examples based on current settings.json):

  - `DefaultOutputDir` (string)
  - `DefaultRunAuditBeforeExport` (bool)
  - `DefaultSaveBefore` (bool)
  - `DefaultRecenterXY` (bool)
  - `DefaultOverwrite` (bool)
  - `DefaultCleanup` (bool)
  - `ValidateStagingArea` (bool)

  - `SelectionSeedCategories` (List<int>)
  - `SelectionProxyCategories` (List<int>)
  - `SelectionProxyDistance` (double)
  - `CleanupBlacklistCategories` (List<int>)

  - `Version` (int)
  - `DeepAnnoStatus` (bool)
  - `PurgeCompact` (bool)

  - `Workflows` (List<WorkflowDefinition>)
  - `SelectedWorkflowIds` (List<string>)

  - `CurrentSetParameterName` (string)
  - `StagingWidth` (double)
  - `StagingHeight` (double)
  - `StagingBuffer` (double)
  - `StageMoveMode` (string)
  - `StagingAuthorizedUids` (List<string>)
  - `StagingAuthorizedCategoryNames` (List<string>)

- **Excluded properties** (runtime-only; never persisted in ES):

  - `LogDir`
  - `OpenOutputFolder`
  - `DryrunDiagnostics`
  - `PerfDiagnostics`
  - `DrawAmbiguousRectangles`
  - Any other fields classified as runtime-only in `docs/settings-deep-dive.md`.

## Provider behavior

### Load()

1. Attempt to read the `ProjectSettings` entity for the current Document.
2. If `SettingsJson` exists:
   - Deserialize into an `AppSettings` instance.
   - Apply any in-code migrations needed for older versions (based on `SettingsVersion` and/or `AppSettings.Version`).
3. If no entity exists yet:
   - Call `LegacyProjectSettingsProvider.Load()` once to obtain the current file-based `AppSettings`.
   - Remove or ignore any runtime-only members.
   - Serialize the filtered object into `SettingsJson`, set `SettingsVersion`, and write the entity.
   - Return the initialized `AppSettings` instance.

### Save(AppSettings settings)

1. Remove or ignore runtime-only members.
2. Serialize the remaining project-scoped members into `SettingsJson`.
3. Update `SettingsVersion` if needed.
4. Write/overwrite the ES entity in a Revit `Transaction`.
5. (Optional during migration) Also call `LegacyProjectSettingsProvider.Save()` to keep `%LOCALAPPDATA%\...settings.json` updated for rollback.

### GetEffectiveOutputDir / GetEffectiveLogDir

- These remain methods on `IProjectSettingsProvider`.
- They use the values from the loaded `AppSettings` plus existing fallback rules (e.g. G-drive defaults) and do **not** persist machine-specific paths in ES.
