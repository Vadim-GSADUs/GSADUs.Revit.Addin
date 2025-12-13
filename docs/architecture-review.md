# Revit Add-in Architecture Assessment

## Observed structural smells

- **Multiple workflow catalog instances**: `WorkflowManagerWindow` creates a new `WorkflowCatalogService` per window when a `Document` is passed and otherwise falls back to DI or a new instance. This allows multiple in-memory catalogs pointing at the same Extensible Storage without coordination, leading to stale data and divergent `ObservableCollection` instances across windows.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L37-L102】
- **Mutable backing state exposed directly to UI**: `WorkflowCatalogService` keeps a mutable `AppSettings` reference and repopulates `ObservableCollection` caches by clearing and re-adding items on every change. Any other view model bound to the collections sees destructive refreshes and selection loss, and the service mixes state, cache, and persistence concerns in one class.【F:src/GSADUs.Revit.Addin/Services/WorkflowCatalogService.cs†L14-L128】
- **Event-notify without source-of-truth refresh**: `WorkflowManagerPresenter` notifies other windows after asynchronous saves via `WorkflowCatalogChangeNotifier`, but those windows rely on cached `_settings` loaded at construction and only refresh via ad-hoc handlers (e.g., `BatchExportWindow` queues a UI dispatcher call). There is no authoritative re-load from ES after saves, so subscribers operate on stale in-memory snapshots.【F:src/GSADUs.Revit.Addin/UI/Presenters/WorkflowManagerPresenter.cs†L747-L758】【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L43-L134】
- **ExternalEvent used as persistence queue without back-pressure**: `ProjectSettingsSaveExternalEvent` simply enqueues callbacks and raises the ExternalEvent every request. There is no coalescing, throttling, or guarding against reentrancy while a save is in-flight, so repeated UI changes can queue redundant writes and out-of-order callback execution. Callbacks are replayed on the WPF dispatcher asynchronously, making button enable/disable and close flows racy.【F:src/GSADUs.Revit.Addin/UI/ProjectSettingsSaveExternalEvent.cs†L17-L83】
- **Overlapping dirty/refresh logic**: The presenter manually detaches/re-attaches `PropertyChanged` handlers around every list mutation and uses `RefreshCaches` (which clears collections) followed by selection restoration. This pattern appears in multiple places and indicates the ViewModels are tightly coupled to the catalog’s collection implementation rather than a stable domain model, encouraging stale selections and double event firing.【F:src/GSADUs.Revit.Addin/UI/Presenters/WorkflowManagerPresenter.cs†L216-L249】【F:src/GSADUs.Revit.Addin/UI/Presenters/WorkflowManagerPresenter.cs†L669-L739】
- **UI flow depends on timers and dispatcher hops**: `WorkflowManagerWindow`’s Save & Close uses a fallback `DispatcherTimer` to close the window if the save callback lags, masking threading/persistence uncertainty instead of fixing sequencing. Modeless windows combined with dispatcher hops make state transitions brittle.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L133-L172】

## Crash/instability risks

- **Concurrent ExternalEvent raises** causing overlapping ES transactions and callback reentrancy; failure paths swallow exceptions and continue UI closing, so state loss is silent.【F:src/GSADUs.Revit.Addin/UI/ProjectSettingsSaveExternalEvent.cs†L24-L80】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L140-L170】
- **Collection clearing while bound**: Clearing and repopulating `ObservableCollection` instances (via `RefreshCaches`) while views are bound risks `InvalidOperationException` in WPF if enumerated during updates, and it forces selection resets observed as “stale” or “dirty” toggling.【F:src/GSADUs.Revit.Addin/Services/WorkflowCatalogService.cs†L33-L43】【F:src/GSADUs.Revit.Addin/UI/Presenters/WorkflowManagerPresenter.cs†L216-L249】
- **Multiple catalogs per document**: Batch Export and Workflow Manager each load settings independently at construction and keep their own `_settings` snapshots. If one window saves while the other mutates its cached copy, later saves overwrite the first without merge, leading to data corruption and “lost” changes.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L52-L102】【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L43-L134】
- **Dispatcher exception swallowing**: Numerous `catch { }` blocks wrap dispatcher invokes and collection operations, hiding binding/serialization errors that could leave ViewModels partially updated and commands disabled unpredictably.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L112-L120】【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L117-L134】

## Data flow and ownership (target model)

- **Workflow Manager window**: binds to a single `WorkflowCatalogService` instance injected from a DI container. The presenter reads/writes workflows through that service but does not own settings; it requests persistence via a save orchestrator.
- **Batch Export window**: consumes read-only projections from the same catalog service (or a read-through query service) and registers as a listener for persisted changes. It should not maintain its own `_settings` copy; instead, it should request a fresh snapshot from the catalog when notified.
- **Workflow catalog service**: single source of truth per Revit document. Owns the in-memory `AppSettings` and exposes immutable read models to UI (or change-tracked collections). Responsible for coordinating persistence via a single save queue.
- **ES provider/writer**: stateless component that serializes/deserializes `AppSettings` from Extensible Storage. Should not be cached per window; invoked by the catalog to load/update state atomically.

Data flow sketch:

```
UI Window (Workflow Manager / Batch Export)
    -> Presenter/ViewModel -> Catalog Service (singleton per document)
        -> ES Provider (load/save) on Revit API thread via single save queue
    <- Catalog change notifications (post-save) used to re-query catalog for fresh snapshot
```

## Refactor plan

### Tier 0: Stop the bleeding (1–3 days)
- **Enforce single catalog instance per document**: Remove per-window construction and resolve a scoped/singleton catalog through DI; reload settings from ES on notifier events instead of sharing stale copies. Impact: prevents divergent state and overwrite; Effort: low; Risk: low because it centralizes existing logic.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L88-L102】【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L43-L134】
- **Serialize saves with coalescing**: Gate `ProjectSettingsSaveExternalEvent.RequestSave` to drop/merge duplicate requests while one is running; invoke a single completion per flush. Impact: avoids race conditions and silent failures; Effort: low-medium; Risk: low since logic is localized.【F:src/GSADUs.Revit.Addin/UI/ProjectSettingsSaveExternalEvent.cs†L24-L80】
- **Load-after-save contract**: After a successful save, force the catalog to reload from ES before notifying listeners, and have listeners refresh their view models from the catalog rather than mutating cached `_settings`. Impact: addresses stale UI and dirty flags; Effort: medium; Risk: low.【F:src/GSADUs.Revit.Addin/UI/Presenters/WorkflowManagerPresenter.cs†L747-L758】【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L117-L134】

### Tier 1: Make it reliable (1–2 weeks)
- **Introduce change-tracked domain model**: Replace `ObservableCollection` clearing with a change-tracked collection (e.g., `ReadOnlyObservableCollection` + diff updates) so bindings remain stable and selections persist. Impact: stabilizes UI state; Effort: medium; Risk: medium due to binding changes.【F:src/GSADUs.Revit.Addin/Services/WorkflowCatalogService.cs†L33-L76】
- **Deterministic dirty state**: Move dirty tracking into the catalog with explicit version/timestamp increments and expose to view models; remove ad-hoc `SetDirty(false)` and handler detaches. Impact: consistent button enablement; Effort: medium; Risk: low-medium.【F:src/GSADUs.Revit.Addin/UI/Presenters/WorkflowManagerPresenter.cs†L252-L292】【F:src/GSADUs.Revit.Addin/UI/Presenters/WorkflowManagerPresenter.cs†L669-L739】
- **Unify notifier/persistence pipeline**: Replace manual dispatcher timers and notifier patterns with a single save pipeline that (a) queues save requests, (b) performs ES writes on API thread, (c) reloads catalog, (d) raises a strongly-typed event. Impact: predictable sequencing between UI actions and Revit API; Effort: medium; Risk: medium.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L133-L172】【F:src/GSADUs.Revit.Addin/UI/ProjectSettingsSaveExternalEvent.cs†L24-L80】

### Tier 2: Make it clean (longer-term)
- **Separate read models from write models**: Presenters/ViewModels consume immutable DTOs; edits happen against a detached draft that’s merged by the catalog. Impact: eliminates cross-tab coupling and refresh churn; Effort: high; Risk: medium.
- **Modeless window coordination**: Introduce a lightweight window manager service to prevent multiple instances and to coordinate focus/refresh without static fields. Impact: removes singleton static hacks; Effort: medium; Risk: low-medium.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L21-L36】【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L21-L41】
- **Central error/logging policy**: Replace `catch { }` with structured logging and user-facing error channels; failures should not silently continue. Impact: faster diagnostics and fewer hidden corruptions; Effort: medium; Risk: low.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L112-L120】【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L117-L134】

## Save/close vs. saveless

Given settings live in ES and the catalog can own change-tracking, a **saveless model** is appropriate:
- **Requirements**: single save queue (ExternalEvent) with coalescing/throttling; every mutation enqueues a save and applies in-memory immediately; post-save reload refreshes bindings; UI exposes a “saving…” indicator rather than a modal close flow.
- **Rationale**: avoids timing hacks (DispatcherTimer), keeps modeless windows consistent, and leverages the single-user assumption—no need for explicit “Save & Close” once atomic queued saves are reliable.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L133-L172】【F:src/GSADUs.Revit.Addin/UI/ProjectSettingsSaveExternalEvent.cs†L24-L80】

## Concrete inspection checklist

- Locate all `ObservableCollection` usages and confirm whether items are mutated or collections are replaced/cleared (`WorkflowCatalogService.RefreshCaches`, presenter refresh routines).【F:src/GSADUs.Revit.Addin/Services/WorkflowCatalogService.cs†L33-L43】【F:src/GSADUs.Revit.Addin/UI/Presenters/WorkflowManagerPresenter.cs†L216-L249】
- Trace dispatcher usage, especially `DispatcherTimer` and `BeginInvoke` around save/close flows and catalog change handling.【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L133-L172】【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L117-L134】
- Identify every catalog service instantiation to ensure only one per document (Workflow Manager, Batch Export, any commands/utilities).【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L88-L102】【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L109-L134】
- Catalog refresh callers: find `RefreshCaches` invocations and redundant refresh/selection restore cycles; decide where a re-load from ES should occur instead.【F:src/GSADUs.Revit.Addin/Services/WorkflowCatalogService.cs†L33-L76】【F:src/GSADUs.Revit.Addin/UI/Presenters/WorkflowManagerPresenter.cs†L669-L739】
- ES writes: confirm error handling/logging around `EsProjectSettingsProvider.Save` and `ProjectSettingsSaveExternalEvent`; ensure failures bubble to UI instead of silent catch-all.【F:src/GSADUs.Revit.Addin/Infrastructure/EsProjectSettingsProvider.cs†L69-L153】【F:src/GSADUs.Revit.Addin/UI/ProjectSettingsSaveExternalEvent.cs†L37-L83】
- Window modality/modelessness: review static singleton patterns and how ExternalCommand execution launches windows, ensuring commands don’t run while modeless windows mutate shared state.

## Top 5 highest ROI changes

1. Make `WorkflowCatalogService` a single scoped instance per document and force all windows to use it (no per-window `_settings`).
2. Add a coalescing save queue (single ExternalEvent) that reloads settings from ES before firing change notifications.
3. Replace collection clears with incremental updates or stable read models to keep bindings and dirty flags consistent.
4. Centralize dirty/version tracking in the catalog and expose a deterministic “saving/saved” status instead of timers.
5. Remove silent catches; route errors through a shared logger and user notifier so persistence issues aren’t hidden.

## Top 5 traps to avoid

1. Creating new `WorkflowCatalogService` instances per window—causes divergent state and overwrites.
2. Clearing/replacing bound `ObservableCollection` instances—breaks selection and triggers binding errors.
3. Raising ExternalEvent repeatedly without throttling—can interleave transactions and callbacks unpredictably.
4. Using dispatcher timers to mask save sequencing—hides real race conditions and risks silent data loss.
5. Swallowing exceptions in dispatchers/binding handlers—leads to undiagnosed crashes and disabled commands.
