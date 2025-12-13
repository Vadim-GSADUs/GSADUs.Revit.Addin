# Targeted refactor plan (Revit add-in, WPF/ExternalEvent)

## Probing Questions (max 12)
1) How many `WorkflowCatalogService` instances are created per Revit document?
   - Verifies: whether windows spin up separate catalogs, causing divergent `_settings` snapshots.
   - Search: constructors/usages of `WorkflowCatalogService`, especially in `WorkflowManagerWindow`, `BatchExportWindow`, commands.
   - Plan change: if only one instance exists already, skip DI scoping work and focus on cache refresh.
2) Do presenters/viewmodels hold long-lived `_settings` or clone collections instead of re-querying the catalog?
   - Verifies: stale snapshot usage leading to overwrite/dirty glitches.
   - Search: fields named `_settings`, `AppSettings`, or cached `ObservableCollection` copies in presenters/viewmodels.
   - Plan change: if they already re-query, prioritize save-queue and binding stability instead.
3) How does `ProjectSettingsSaveExternalEvent` queue saves—does it coalesce or allow reentrancy?
   - Verifies: risk of overlapping ES writes and out-of-order callbacks.
   - Search: `RequestSave`, `_pendingCallbacks`, `_saveInProgress`, or similar flags in save external event class.
   - Plan change: if coalescing already exists, tighten post-save reload/notifications instead of queue redesign.
4) Are bound collections cleared/rebuilt (e.g., `RefreshCaches`) or updated incrementally?
   - Verifies: source of selection loss and WPF instability.
   - Search: `ObservableCollection.Clear`, `RefreshCaches`, or `new ObservableCollection` assignments on bound properties.
   - Plan change: if incremental updates exist, focus on immutable read models rather than collection diffing.
5) Where are dispatcher hops/timers used around Save & Close or catalog notifications?
   - Verifies: sequencing hacks masking API-thread timing issues.
   - Search: `DispatcherTimer`, `BeginInvoke`, `InvokeAsync` in windows/presenters around save/close, notifier handlers.
   - Plan change: if minimal, keep UI flow; otherwise, replace with explicit save pipeline callbacks.
6) How are errors handled for ES saves/loads?
   - Verifies: silent failures via `catch { }` that hide state corruption.
   - Search: `catch { }`, logging calls, or empty catch blocks around ES provider and save external event.
   - Plan change: if logging exists, focus on user notification; if not, add structured error surface in Tier 0.
7) Does the save pipeline reload from ES before notifying other windows?
   - Verifies: whether listeners operate on stale caches after save.
   - Search: post-save callbacks in presenter/notifier, calls to `Load`/`Refresh` after `Save`.
   - Plan change: if reload already happens, concentrate on single-source catalog access.
8) How are dirty flags computed and reset?
   - Verifies: inconsistent button enabling due to ad-hoc dirty tracking.
   - Search: `SetDirty`, `_isDirty`, `IsDirty`, manual event detaches around mutations.
   - Plan change: if centralized, deprioritize dirty refactor; otherwise, add versioned tracking in Tier 1.
9) Are modeless windows prevented from opening multiple instances or mutating while commands execute?
   - Verifies: window coordination and shared state safety.
   - Search: static window instances/sentinels in window classes or external commands launching windows.
   - Plan change: if window manager already exists, skip and focus on catalog/saves; else add coordination in Tier 2.
10) Are Batch Export reads read-only projections or mutable shared collections?
    - Verifies: whether Batch Export can mutate shared state or overwrite Workflow Manager changes.
    - Search: Batch Export viewmodel bindings to catalog collections and any setters/mutations.
    - Plan change: if read-only projections already used, reduce scope of read model work.

## Minimum evidence to paste
- Constructors/usages of `WorkflowCatalogService` (windows, presenters, external commands) showing scope/lifecycle.
- `ProjectSettingsSaveExternalEvent` implementation: queue flags, callback management, error handling.
- Any `RefreshCaches` or collection clearing in catalog/presenters (with bindings).
- Save/close flow in `WorkflowManagerWindow` (timers/dispatchers) and notifier handlers in other windows.
- Dirty tracking implementation (`IsDirty`/`SetDirty` or equivalents) in presenters/catalog.

## Primary refactor plan (Tier 0/1/2)
### Tier 0: Stop the bleeding (1–3 days)
- **Single catalog per document, injected everywhere**
  - Impact: eliminates divergent `_settings` and overwrites; ensures one source of truth.
  - Risk: low (scoping/DI wiring), Effort: low.
  - Acceptance: all windows/commands resolve the same catalog instance for a given `Document`; new instances only when document changes.
- **Serialize + coalesce saves through one pipeline**
  - Impact: prevents overlapping ES writes and stale callbacks; removes need for timer-based close.
  - Risk: low-medium (threading), Effort: low-medium.
  - Acceptance: `RequestSave` is idempotent while in-flight; saves execute sequentially; completion raises once per flush.
- **Post-save reload before notifying**
  - Impact: listeners always see authoritative state; fixes dirty/refresh inconsistencies.
  - Risk: low, Effort: low.
  - Acceptance: after each persisted save, catalog reloads from ES and then emits change event consumed by windows.
- **Expose/save failure visibly**
  - Impact: surfaces hidden corruption; aids support.
  - Risk: low, Effort: low.
  - Acceptance: no empty catch blocks around ES load/save; errors logged and shown once to UI.

### Tier 1: Make it reliable (1–2 weeks)
- **Stable read models & incremental updates**
  - Impact: stops selection loss and WPF binding churn.
  - Risk: medium (binding changes), Effort: medium.
  - Acceptance: bound collections aren’t cleared; updates are diffed or provided as immutable snapshots.
- **Deterministic dirty/version tracking in catalog**
  - Impact: consistent button enablement and Save visibility.
  - Risk: low-medium, Effort: medium.
  - Acceptance: a version/dirty token increments on mutation; UIs bind to it instead of manual handler toggles.
- **Unified save orchestrator (API-thread sequencing)**
  - Impact: removes dispatcher timers; predictable close flow.
  - Risk: medium, Effort: medium.
  - Acceptance: save pipeline steps are ordered: enqueue → API-thread ES write → reload catalog → notify → UI closes without timers.

### Tier 2: Make it clean (longer-term)
- **Read/write separation with draft objects**
  - Impact: isolates UI editing from live store; simplifies rollback.
  - Risk: medium, Effort: high.
  - Acceptance: UI edits apply to draft model; commit merges via catalog; read models are immutable.
- **Window coordination service for modeless windows**
  - Impact: prevents multiple instances and state conflicts during commands.
  - Risk: low-medium, Effort: medium.
  - Acceptance: single instance per window type per document; commands respect window state before running.
- **Centralized telemetry/logging policy**
  - Impact: quicker diagnosis, fewer silent failures.
  - Risk: low, Effort: medium.
  - Acceptance: all save/load/notification paths log structured errors and surface user-friendly alerts.

## Secondary options (A/B)
- **Option A: Saveless auto-save with “Saving…” state**
  - Prerequisites: coalescing save queue; post-save reload contract; UI indicator binding to save in-flight flag.
  - Removes: explicit Save & Close flow and dispatcher timers; reduces manual dirty toggles.
- **Option B: Draft/commit model**
  - Prerequisites: stable read models; catalog-managed drafts; merge/apply pipeline on API thread.
  - Removes: cross-window overwrite risk and ad-hoc dirty tracking by isolating uncommitted edits.

## Top 5 traps to avoid
1. Spawning new `WorkflowCatalogService` per window or command.
2. Clearing/replacing bound `ObservableCollection` instances instead of diffing.
3. Raising `ExternalEvent` repeatedly without coalescing/back-pressure.
4. Using dispatcher timers to mask save sequencing issues.
5. Swallowing exceptions around ES reads/writes or dispatcher callbacks.
