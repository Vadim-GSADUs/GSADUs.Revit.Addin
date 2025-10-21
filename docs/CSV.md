# CSV Workflow Tab – MVVM Outline (Planning)

Goal
- Enable saving and running workflows that export selected Revit schedules/quantities to CSV files.
- Reuse a common base across all workflow tabs with identical Name and Scope inputs.
- Define CSV-specific UI/logic for schedule selection and file name patterning.

Scope of this plan
- Planning only; no code yet.
- Align with existing `WorkflowManagerWindow.xaml` and `WorkflowTabBaseViewModel`.

Decisions confirmed by user
- Selection: Multiple selection is supported and should result in multiple CSV files (one per selected schedule).
- Inclusion: Include all schedule types, including Key Schedules and Material Takeoffs.
- Host scope: Schedules come from the host document only (no linked documents).
- No data changes: Export schedule data exactly as shown in Revit; no filtering/transformation beyond what the schedule already defines.
- IDs and display
  - Persist schedule IDs as Revit `ElementId` integers serialized as strings (e.g., "12345").
  - UI displays schedule titles/names only (no IDs in dropdowns).
  - If a saved schedule ID is not found in the current document, ignore it silently.
- Tokens and sources
  - `{SetName}` comes from the current saved selection set that is batch processed (same as RVT/PDF/Image workflows). A schedule may be authored with a `CurrentSet == true` filter so the same schedule definition exports different rows per set.
  - `{FileName}` is the active (parent) document’s file name without extension.
  - `{ViewName}` is the schedule title (`ViewSchedule.Name`).
  - No additional tokens for now.
- CSV defaults: comma delimiter, UTF-8 encoding, include headers.
- Simplicity: Keep the filename pattern logic simple.
- Persistence: CSV workflows are stored in `settings.json` alongside other workflows with the same shape.
- Name uniqueness: Workflow names must be unique; saving with an existing Name updates (upserts) that workflow.
- Filename collision/overwrite behavior (collisions only; schedule data never modified)
  - If multiple exports in a run resolve to the same filename, treat as a filename collision.
  - In collision cases, overwrite regardless; for all other cases, follow `DefaultOverwrite` from user preferences.
- Execution linkage: Output folder and overwrite rules follow user prefs in `settings.json`.

1) Shared workflow tab baseline (applies to all tabs)
- Base ViewModel: `WorkflowTabBaseViewModel`
  - Properties
    - `Name` (string): required; unique within all workflows; upsert by name
    - `Scope` (enum): `CurrentSet`, `EntireProject`
    - `IsDirty`/`HasChanges` (bool)
  - Commands
    - `SaveWorkflowCommand`
  - Validation
    - Name: not empty, unique (case-insensitive)
    - Scope: required
- Base View: reusable section for Name and Scope
  - A shared user control or XAML region to embed in all tabs for consistency.

2) CSV tab specifics
- ViewModel: `CsvWorkflowTabViewModel` : `WorkflowTabBaseViewModel`
  - CSV Export Range section
    - `AvailableSchedules` (collection): all `ViewSchedule`, including key schedules and material takeoffs, from the active document (host only).
    - `SelectedSchedules` (collection): multi-select; one CSV per selected schedule per run (per set when applicable).
    - `RefreshSchedulesCommand` to re-enumerate.
  - File Name section
    - `FileNamePattern` (string) with supported tokens: `{SetName}`, `{FileName}`, `{ViewName}` only.
    - `FileNamePreview` (collection of strings): live preview of resolved filenames for all `SelectedSchedules` (like the PDF workflow).
    - Defaulting behavior
      - If `Scope == CurrentSet` and pattern is empty, default to `{SetName} {ViewName}`.
      - If `Scope == EntireProject` and pattern is empty, default to `{FileName} {ViewName}`.
      - On scope change: if pattern equals the previous default or is empty, switch to the new default; otherwise, preserve user-entered pattern.
    - Validation: pattern must only contain allowed tokens; no unknown tokens.

3) Services and data access
- `IScheduleDiscoveryService`
  - Enumerate all `ViewSchedule` in the active (host) document (include key schedules and material takeoffs).
  - Return descriptors for UI and persistence:
    - `Id` (string): the Revit `ElementId` integer serialized to string.
    - `Name` (string): the schedule title/name for display.
- `ITokenResolver`
  - Resolve `{SetName}`, `{FileName}`, `{ViewName}` given: scope, active document, current set context, and schedule(s).
  - Provide errors for unknown tokens.
- `IWorkflowPersistenceService`
  - Save/load workflow definitions from `settings.json`.
  - Upsert by `Name`; preserve existing `Id` if name matches; otherwise generate a new `Id`.

4) Models
- `CsvWorkflowDefinition`
  - `Id`, `Name`, `Kind`, `Output`, `Scope`, `Description`, `ActionIds`, `Parameters`, `Enabled`, `Order` (same shape as other workflows)
  - `Parameters` for CSV
    - `scheduleIds`: array of selected schedule ids as strings (e.g., ["12345", "67890"]).
    - `fileNamePattern`: string (e.g., `{SetName} {ViewName}` or `{FileName} {ViewName}`).
    - CSV output defaults (implicitly applied unless extended later): comma delimiter, UTF-8 encoding, include headers.

5) UI composition and integration
- `WorkflowManagerWindow.xaml`
  - Include the shared Name/Scope region (from the base) for all tabs.
  - Register a DataTemplate for `CsvWorkflowTabViewModel`.
- CSV tab layout
  - Section: “CSV Export Range”
    - Multi-select ListBox with checkboxes bound to `AvailableSchedules` / `SelectedSchedules`.
    - Refresh button to repopulate schedules from the active document.
  - Section: “File Name”
    - TextBox for `FileNamePattern` input.
    - Read-only list preview bound to `FileNamePreview`.
    - Helper text listing supported tokens.

6) Validation rules
- Name: required and unique across all workflows; saving updates existing by name (upsert).
- Scope: required.
- Schedule selection: must have at least one selected schedule.
- File name pattern: required; only allowed tokens; must resolve to non-empty strings.
- Missing schedules: if a persisted `scheduleId` is not found, ignore silently.
- File system: resolved names must not contain invalid characters; detect duplicate output filenames and apply collision rules.

7) Behavior and state
- On document or scope change: refresh `AvailableSchedules`.
- On selection or pattern change: recalc `FileNamePreview`.
- On save: write to `settings.json` Workflows; CSV workflow should use `ActionIds: ["export-csv"]` and `Output: 3` to match existing convention.

8) Filename collision and overwrite handling
- Detect duplicate output filenames during a run using a map keyed by resolved filename (without extension). This does not modify schedule contents; each export writes the full schedule data.
- In filename-collision cases: overwrite regardless. For non-collision cases, follow `DefaultOverwrite`.

9) Extensibility (out of scope for now)
- Tokens and CSV options can be extended in the future; today only the three tokens are valid and CSV defaults are fixed.

10) Clarifications resolved
- Schedule ID serialization: Use Revit `ElementId` integers serialized as strings in `Parameters.scheduleIds`. If not found, ignore silently.
- Pattern requirements: `{ViewName}` equals schedule name. Recommended via defaults; not required.
- Document scope: Host document only.
- Preview: Provide live filename preview only; no data preview.
- Overwrite interplay: Filename-collision cases overwrite as specified; otherwise follow `DefaultOverwrite`.
- CSV defaults: comma delimiter, UTF-8 encoding, include headers (may become configurable later).

11) Next steps
- Implement services (`IScheduleDiscoveryService`, `ITokenResolver`, `IWorkflowPersistenceService`).
- Add `CsvWorkflowDefinition` and `CsvWorkflowTabViewModel`.
- Update `WorkflowManagerWindow.xaml` with the CSV tab and shared base.
- Implement export using Revit `ViewSchedule.Export(folder, fileName, ScheduleExportOptions)` with the defaults above.
- Add tests for token resolution, validation, name upsert logic, missing schedule ID handling, filename preview generation, and filename-collision behavior.