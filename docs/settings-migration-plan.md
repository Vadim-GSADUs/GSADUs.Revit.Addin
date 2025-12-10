# Settings migration plan

## Goal

Move all configuration from the local JSON file at:

- `%LOCALAPPDATA%\GSADUs\Revit\Addin\settings.json`

to **project-scoped settings** associated with each RVT file (via Revit Extensible Storage), while:

- Keeping behavior stable during Phase 1.
- Avoiding per-user configuration (all team members share consistent settings).
- Gradually eliminating `AppSettingsStore` and direct `settings.json` usage.

This plan assumes `docs/settings-deep-dive.md` as the reference for the current state and classification of `AppSettings` members.

---

## Scopes and principles

We use only two conceptual scopes:

1. **Project settings**  
   - One logical settings object per RVT.
   - Persisted with the RVT (Extensible Storage).
   - All previously “project-scoped” members in `AppSettings` (see `settings-deep-dive.md`) fall here.
   - Optional: `PerfDiagnostics`/`DryrunDiagnostics` may be treated as project-wide toggles or runtime-only flags.

2. **Defaults**  
   - Hard-coded or template-based values used when a project setting has never been initialized.
   - Not per-user; just simple fallback behavior.

We do **not** support per-user settings. Any behavior that used to be “per-user” is either:

- Removed or treated as project-wide, or  
- Derived at runtime (e.g. a temp/log directory on the current machine) without being persisted.

---

## Phase 1 – Introduce project settings provider (no behavior change)

Objective: introduce a single abstraction that all code uses to access settings, while still backing it with `settings.json` and `AppSettingsStore`.

### 1. Add `IProjectSettingsProvider` interface

File: `src/GSADUs.Revit.Addin/Abstractions/IProjectSettingsProvider.cs`

Define an interface that wraps the existing behavior:

```csharp
namespace GSADUs.Revit.Addin.Abstractions
{
    public interface IProjectSettingsProvider
    {
        AppSettings Load();
        void Save(AppSettings settings);

        // Helpers so callers stop touching AppSettingsStore directly
        string GetEffectiveOutputDir(AppSettings settings);
        string GetEffectiveLogDir(AppSettings settings);
    }
}
Notes:

AppSettings remains the DTO for now.

No Document dependency yet; that comes later with Extensible Storage.

2. Add LegacyProjectSettingsProvider implementation
File: src/GSADUs.Revit.Addin/Infrastructure/LegacyProjectSettingsProvider.cs

Implementation should delegate to the existing static store:

csharp
Copy code
using GSADUs.Revit.Addin.Abstractions;

namespace GSADUs.Revit.Addin.Infrastructure
{
    public class LegacyProjectSettingsProvider : IProjectSettingsProvider
    {
        public AppSettings Load()
        {
            return AppSettingsStore.Load();
        }

        public void Save(AppSettings settings)
        {
            AppSettingsStore.Save(settings);
        }

        public string GetEffectiveOutputDir(AppSettings settings)
        {
            return AppSettingsStore.GetEffectiveOutputDir(settings);
        }

        public string GetEffectiveLogDir(AppSettings settings)
        {
            return AppSettingsStore.GetEffectiveLogDir(settings);
        }
    }
}
3. Register provider in DI
File: src/GSADUs.Revit.Addin/Infrastructure/DI/Startup.cs

Add registration:

csharp
Copy code
using GSADUs.Revit.Addin.Abstractions;
using GSADUs.Revit.Addin.Infrastructure;

// inside ConfigureServices or equivalent:
services.AddSingleton<IProjectSettingsProvider, LegacyProjectSettingsProvider>();
Keep existing ISettingsPersistence registration for now if it is still used. It may be retired later once all consumers use IProjectSettingsProvider.

4. Rewire a first set of consumers
The goal is to stop calling AppSettingsStore directly from “big” components and instead consume IProjectSettingsProvider. Do this in small steps:

4.1 WorkflowCatalogService
File: src/GSADUs.Revit.Addin/Services/WorkflowCatalogService.cs

Add a constructor dependency on IProjectSettingsProvider.

Replace direct calls to AppSettingsStore.Load/Save (or indirect via SettingsPersistence) with _projectSettingsProvider.Load() and _projectSettingsProvider.Save(settings).

Continue to use AppSettings internally for Workflows and SelectedWorkflowIds.

4.2 BatchRunCoordinator
File: src/GSADUs.Revit.Addin/Orchestration/BatchRunCoordinator.cs

Add IProjectSettingsProvider to the constructor and store it in a field.

Replace each AppSettingsStore.Load() with _projectSettingsProvider.Load().

Replace AppSettingsStore.GetEffectiveOutputDir(settings) with _projectSettingsProvider.GetEffectiveOutputDir(settings).

Aim to load settings once per batch run and pass the same AppSettings instance through the run logic instead of repeatedly reloading.

4.3 ExportCsvAction (one concrete workflow)
File: src/GSADUs.Revit.Addin/Workflows/Csv/ExportCsvAction.cs

Add IProjectSettingsProvider to the constructor.

Replace direct static calls to AppSettingsStore.Load() and GetEffectiveOutputDir with provider calls.

Keep logic otherwise identical (filters, overwrite behavior, etc.).

You can later apply the same pattern to:

ExportPdfAction

PdfWorkflowRunner

ExportImageAction

ExportRvtAction

but they are optional in Phase 1; focus on a small, testable subset first.

5. Leave UI mostly untouched in Phase 1
Files such as:

src/GSADUs.Revit.Addin/UI/SettingsWindow.xaml.cs

src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs

src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs

may still call AppSettingsStore directly in Phase 1. That is acceptable while the core orchestration and services are moved behind IProjectSettingsProvider.

Phase 2 – Introduce Extensible Storage–backed project settings
Objective: implement a real project-scoped settings store using Revit Extensible Storage, while retaining LegacyProjectSettingsProvider as a fallback where necessary.

1. Design Extensible Storage schema
Create a conceptual “ProjectSettings” schema with:

Fixed GUID.

Fields reflecting the project-scoped AppSettings members (as identified in settings-deep-dive.md), for example:

Output/log directories (if you want them shared)

Workflow catalog and selected workflow IDs

Staging parameters (width/height/buffer/mode, authorized IDs)

Audit/cleanup flags

CSV/PDF/Image/RVT export defaults

Shared parameter name and GUID

Settings version

Document this schema design in docs/settings-deep-dive.md or a new docs/settings-schema.md.

2. Implement EsProjectSettingsProvider
File: src/GSADUs.Revit.Addin/Infrastructure/EsProjectSettingsProvider.cs

Implement IProjectSettingsProvider to:

Read from the active Document’s Extensible Storage.

When no schema data exists yet, create default AppSettings from:

Hard-coded defaults OR

A one-time import from the legacy settings.json (via AppSettingsStore.Load()).

The Load() method should:

Attempt to read from ES.

If not present:

Optionally read from AppSettingsStore once.

Save to ES.

Return the initialized settings.

The Save() method should:

Write the updated settings to ES.

Optionally keep legacy settings.json in sync during the migration period (temporary).

GetEffectiveOutputDir / GetEffectiveLogDir should be updated to rely on ES values but maintain the same fallback logic (e.g. defaulting to a shared drive path) so behavior remains familiar.

3. Swap DI registration
In Startup.cs, once EsProjectSettingsProvider is stable:

Change DI registration to use ES instead of legacy:

csharp
Copy code
services.AddSingleton<IProjectSettingsProvider, EsProjectSettingsProvider>();
Optionally, keep LegacyProjectSettingsProvider available behind a feature flag or internal fallback if ES read/write fails, but the main code path should use ES.

Phase 3 – Tighten the model and remove AppSettingsStore dependencies
Objective: move from “ES storing a serialized AppSettings clone” to a cleaner, project-focused model and ensure all code goes through IProjectSettingsProvider.

1. Gradually stop exposing raw AppSettings
Over time, adjust IProjectSettingsProvider to expose more granular operations or a dedicated ProjectSettings DTO, for example:

ProjectSettings LoadProjectSettings(Document doc)

void SaveProjectSettings(Document doc, ProjectSettings settings)

and map between ProjectSettings and the existing AppSettings shape as needed.

This can be done incrementally:

For new code, depend on the more focused API.

For old code, keep using AppSettings until refactored.

2. Migrate remaining consumers away from AppSettingsStore
Search for remaining direct uses of:

AppSettingsStore.Load/Save

AppSettingsStore.GetEffectiveOutputDir/GetEffectiveLogDir

Hard-coded settings.json path

G-drive fallback strings

For each:

Replace with IProjectSettingsProvider calls.

If the caller is UI code, pass settings in/from a view-model that is itself fed by the provider.

Focus on feature slices:

CSV workflows (tab VM, window bindings).

PDF/Image/RVT workflows.

Logging and diagnostics.

Staging selection and cleanup.

3. Update UI to hide local path and respect project scope
Update:

SettingsWindow

BatchExportWindow

WorkflowManagerWindow

to:

Stop displaying %LOCALAPPDATA%\GSADUs\Revit\Addin\settings.json.

Treat all settings as project-level (or clearly mark any that are purely runtime toggles).

Use IProjectSettingsProvider to load/save.

Phase 4 – Remove legacy file-based settings and clean up
Objective: delete the old local settings implementation once the project-scoped provider is fully in place.

1. Mark legacy code as obsolete
Mark the following as [Obsolete] once you are confident nothing new should depend on them:

AppSettingsStore (static methods)

Any helpers that build the %LOCALAPPDATA%\GSADUs\Revit\Addin\settings.json path

Any explicit use of the G-drive fallback paths from AppSettings.cs

Fix compiler warnings by migrating remaining call sites to IProjectSettingsProvider.

2. Remove legacy code and references
After all call sites are migrated:

Remove AppSettingsStore and any JSON serialization helpers that are no longer used.

Remove the settings.json path from UI.

Remove docs that explicitly instruct users to edit/localize settings.json.

3. Final verification
Search the entire solution for:

settings.json

%LOCALAPPDATA%\\GSADUs\\Revit\\Addin

AppSettingsStore.

G:\Shared drives\GSADUs Projects\Our Models\0 - CATALOG\Output

Confirm no references remain.

Confirm all settings access flows through IProjectSettingsProvider (and any eventual ProjectSettings APIs).

Notes for tooling (Copilot / Codex)
Use docs/settings-deep-dive.md as the authoritative reference for current behavior and member classification.

Use this docs/settings-migration-plan.md as the authoritative reference for the migration sequence.

When using Copilot in Visual Studio for code edits, reference specific steps (e.g., “Phase 1, step 4.1”) so it follows the planned architecture rather than inventing a new one.

When using Codex (GitHub-connected chat), use it to:

Refine later phases (Extensible Storage details).

Generate additional documentation (e.g., settings-schema.md).

Propose incremental refactor steps tied to specific files and phases.

javascript
Copy code

You can paste this directly into `docs/settings-migration-plan.md` and then have Codex review it for completeness and alignment with `settings-deep-dive.md`.