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
- Tokens and sources
  - `{SetName}` comes from the current saved selection set that is batch processed (same as RVT/PDF/Image workflows).
  - `{FileName}` is the active (parent) document’s file name without extension.
  - `{ViewName}` is the schedule title (`ViewSchedule.Name`).
  - No additional tokens for now.
- Simplicity: Keep the filename pattern logic simple.
- Persistence: CSV workflows are stored in `settings.json` alongside other workflows with the same shape.
- Name uniqueness: Workflow names must be unique; saving with an existing Name updates (upserts) that workflow.
- Collision/overwrite behavior
  - `{SetName}` pattern: process once per set; overwrite existing CSV.
  - `{FileName}` pattern: process once per export run if selected; overwrite existing CSV. This likely requires coordination in `BatchRunCoordinator` after all CurrentSet batch exports complete.
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
    - `AvailableSchedules` (collection): all `ViewSchedule`, including key schedules and material takeoffs, from the active document.
    - `SelectedSchedules` (collection): multi-select.
    - `RefreshSchedulesCommand` to re-enumerate.
  - File Name section
    - `FileNamePattern` (string) with supported tokens: `{SetName}`, `{FileName}`, `{ViewName}` only.
    - Defaulting behavior
      - If `Scope == CurrentSet` and pattern is empty, default to `{SetName} {ViewName}`.
      - If `Scope == EntireProject` and pattern is empty, default to `{FileName} {ViewName}`.
      - On scope change: if pattern equals the previous default or is empty, switch to the new default; otherwise, preserve user-entered pattern.
    - `FileNamePreview` (collection of strings): live preview of resolved filenames for all `SelectedSchedules`.
    - Validation: pattern must only contain allowed tokens; no unknown tokens.

3) Services and data access
- `IScheduleDiscoveryService`
  - Enumerate all `ViewSchedule` in the active document (include keys and material takeoffs).
  - Return descriptors: `Id` (int), `Name` (string), optional `Category`/`Type` for display.
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
    - `scheduleIds`: array of selected schedule ids (e.g., `["12345", "67890"]`)
    - `fileNamePattern`: string (e.g., `{SetName} {ViewName}` or `{FileName} {ViewName}`)

5) UI composition and integration
- `WorkflowManagerWindow.xaml`
  - Include the shared Name/Scope region (from the base) for all tabs.
  - Register a DataTemplate for `CsvWorkflowTabViewModel`.
- CSV tab layout
  - Section: “CSV Export Range”
    - Multi-select ListBox with checkboxes (simple and clear) bound to `AvailableSchedules` / `SelectedSchedules`.
    - Refresh button to repopulate schedules from the active document.
  - Section: “File Name”
    - TextBox for `FileNamePattern` input.
    - Read-only list preview bound to `FileNamePreview` (show top N with “+ more” if large).
    - Helper text listing supported tokens.

6) Validation rules
- Name: required and unique across all workflows; saving updates existing by name (upsert).
- Scope: required.
- Schedule selection: must have at least one selected schedule.
- File name pattern: required; only allowed tokens; must resolve to non-empty strings.
- File system: resolved names must not contain invalid characters; detect duplicates and apply collision rules.

7) Behavior and state
- On document or scope change: refresh `AvailableSchedules` and recalc previews.
- On selection or pattern change: recalc `FileNamePreview`.
- On save: write to `settings.json` Workflows; CSV workflow should use `ActionIds: ["export-csv"]` and `Output: 3` to match existing convention.

8) Collision and overwrite handling
- Dedupe during a run using a map keyed by resolved filename (without extension); when a duplicate is detected:
  - If pattern involves `{SetName}`: only export once per set; overwrite existing output.
  - If pattern involves `{FileName}`: only export once per export batch; overwrite existing output, likely after all set-based exports complete (coordinate via `BatchRunCoordinator`).
- For unique names, follow the global overwrite preference in `settings.json`.

9) Extensibility
- Tokens can be extended in the future; today only the three are valid.
- Additional CSV options (delimiter, encoding) can be introduced later via `Parameters` without breaking shape.

10) Open items remaining (to confirm)
- Schedule IDs persistence
  - Confirm using Revit integer `ElementId` values serialized as strings in `Parameters.scheduleIds`, same as `singleViewId` elsewhere.
  - Behavior when an id is not found in the current document (ignore vs warn?).
- Required tokens
  - Should `{ViewName}` be required in the pattern (since output is per schedule), or merely recommended by default?
- Schedule list scope
  - Host document only (assumed), or include linked documents’ schedules too?
- Preview size
  - Limit `FileNamePreview` list length (e.g., show first 50) to keep UI performant?
- Default overwrite interplay
  - In deduped cases we will overwrite; for all other cases follow `DefaultOverwrite`. Is that acceptable even if `DefaultOverwrite` is false?

11) Next steps
- Confirm the open items above.
- Implement services (`IScheduleDiscoveryService`, `ITokenResolver`, `IWorkflowPersistenceService`).
- Add `CsvWorkflowDefinition` and `CsvWorkflowTabViewModel`.
- Update `WorkflowManagerWindow.xaml` with the CSV tab and shared base.
- Wire up validation, commands, live preview, and dedupe behavior at run time (in `BatchRunCoordinator`).
- Add tests for token resolution, validation, and name upsert logic.


Still ambiguous (please confirm):
1.	Schedule ID serialization
•	Is it acceptable to store Revit ElementId integers as strings in Parameters.scheduleIds (consistent with singleViewId)?
•	If a saved id isn’t found in the document, ignore silently or surface a warning in the tab?
2.	Pattern requirements
•	Should {ViewName} be required in the pattern (since output is per schedule), or just recommended via defaults?
3.	Document scope
•	List schedules only from the host document (assumed) or include linked documents’ schedules?
4.	Preview list sizing
•	OK to cap preview to a max (e.g., first 50) and show “+N more” for performance?
5.	Overwrite interplay
•	In deduped cases we overwrite regardless; for all other cases, follow DefaultOverwrite. Is that acceptable even when DefaultOverwrite is false?