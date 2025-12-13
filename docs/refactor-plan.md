# Revit Add-in Refactor Plan (grounded in current code)

## 1) Target architecture (per Revit Document)

**Ownership model**
- **Catalog (source of truth):** One `WorkflowCatalogService` per Revit `Document`. This instance owns the authoritative `AppSettings` and supplies read-only projections; presenters/windows never keep private `_settings` copies.
- **ES provider:** One `EsProjectSettingsProvider` per `Document`, resolved through the document scope and supplied to the catalog/orchestrator. Provider remains stateless.
- **Save orchestrator:** One `ProjectSettingsSaveExternalEvent` (or successor) per document that owns the ExternalEvent, coalesces/gates requests, executes on the Revit API thread, and drives a strict `save -> reload-from-ES -> notify` sequence.
- **Notifier/Event stream:** One `WorkflowCatalogChangeNotifier` per document that only fires after the catalog reloads from ES, so all windows re-query the shared catalog rather than relying on cached snapshots.
- **Windows/Presenters:** Resolve the document-scoped catalog, orchestrator, and notifier; do not construct them ad-hoc. UI binds to projections and issues `RequestSave` through the orchestrator.

**Lifecycle & scoping**
- Maintain a `DocumentId`/`Document.UniqueId` keyed container that supplies catalog, ES provider, save orchestrator, and notifier. Dispose entries on `DocumentClosed` (unsubscribe notifiers, clear queues, dispose ExternalEvent if needed).

**Diagram**
```
[Window / Presenter]
    | (projections + RequestSave)
    v
[Catalog (per Doc)] <---- reload after save ----
    |                                   ^
    v                                   |
[Save Orchestrator (ExternalEvent queue)]
    |
    v
[ES Provider] -> Extensible Storage

[Notifier/Event Stream] --post-reload--> windows re-query
```

## 2) Primary refactor plan

### Tier 0 (1–3 days): Stop the bleeding
- **Goal:** Enforce document-scoped ownership, serialize saves with coalescing and deterministic completion, and eliminate ad-hoc construction/timer closes once sequencing is correct.
- **Changes:**
  - Introduce a document-scoped container (map or DI scope) that returns the single catalog, notifier, ES provider, and save orchestrator for that document. Replace `new WorkflowCatalogService(new EsProjectSettingsProvider(...))` and `new WorkflowCatalogChangeNotifier()` paths in `WorkflowManagerWindow.CreateCatalog` with scoped resolution; drop the private `_settings` cache in the window/presenter.
  - Move `ProjectSettingsSaveExternalEvent` to the document scope. Add in-flight gating and request coalescing; ensure `Execute` performs `catalog.Save` then `catalog.ReloadFromEs` (or equivalent) before invoking `WorkflowCatalogChangeNotifier.NotifyChanged` and callbacks. Prevent concurrent `ExternalEvent.Raise` overlap.
  - Update presenter/window save flow to await orchestrator completion instead of relying on the `DispatcherTimer` fallback; remove the timer only after deterministic completion is wired.
  - Align DI with the document scope: current `Startup.cs` registers `WorkflowCatalogService`, `WorkflowCatalogChangeNotifier`, `EsProjectSettingsProvider`, and `WorkflowManagerPresenter` as singletons, but windows bypass DI and create ad-hoc instances. Adjust registrations (e.g., factory that resolves per-document instances via the scoped container) and update consumers accordingly.
- **Acceptance criteria:**
  - Multiple windows for the same document see a single shared catalog/notifier/orchestrator; saves in one window surface in others after reload.
  - Rapid save requests for the same document coalesce into one ES write per flush; callbacks fire once per flush and only after reload+notify.
  - Save & Close waits for orchestrator completion; no premature closes from timers; UI observes the post-save state.
- **Risk:** Medium (threading and ExternalEvent sequencing changes).
- **Effort:** 1–3 days.
- **Rollback plan:** Keep current ad-hoc construction and timer path behind a flag; retain the existing ExternalEvent class (without coalescing) in a branch for quick revert.

### Tier 1 (1–2 weeks): Make it reliable
- **Goal:** Stabilize UI bindings, tracking, and error visibility.
- **Changes:**
  - Replace destructive `ObservableCollection.Clear()/rebuild` patterns with diff/apply or read-only projections to prevent selection loss and `InvalidOperationException` during binding refresh.
  - Move dirty/version tracking into the catalog with deterministic increments and `Saving/Saved` states that align with orchestrator callbacks; presenters read state instead of inferring from UI.
  - Centralize error/logging; replace silent catches (`catch { }` in window load, list wiring, validation handlers) with logged errors surfaced through dialogs/notifier.
- **Acceptance criteria:**
  - No WPF binding exceptions during refresh; selections persist across updates.
  - Dirty/version state matches persisted catalog state and resets only after reload from ES.
  - Save failures surface to logs and user dialogs; no swallowed exceptions in the save/notify path.
- **Risk:** Medium (binding adjustments may expose latent UI assumptions).
- **Effort:** 1–2 weeks.
- **Rollback plan:** Keep legacy clear/rebuild helpers and local dirty flags behind switches for temporary fallback.

### Tier 2 (longer): Make it clean
- **Goal:** Simplify model boundaries and window coordination after stability.
- **Changes:**
  - Split read models (immutable projections) from draft write models to isolate UI edits from the shared catalog state.
  - Add a window manager that prevents duplicate `WorkflowManagerWindow` instances per document and routes focus to the active window.
  - Remove legacy registries/commands that are unused once verified; clean DI registrations accordingly.
- **Acceptance criteria:**
  - UI binds to read-only projections; edits commit via catalog/orchestrator apply operations.
  - Attempting to open a second window for the same document reuses/focuses the existing one.
  - Unused legacy components removed without breaking ribbon command entrypoints.
- **Risk:** Medium-high.
- **Effort:** Longer-term/iterative.
- **Rollback plan:** Retain existing window creation and legacy registry wiring in branch; feature-flag draft/commit mode if needed.

## 3) Secondary plans (optional directions)

### Option A: Saveless autosave
- **Prerequisites:** Tier 0 document-scoped orchestrator with coalescing and reload-before-notify; stable projections.
- **Approach:** Trigger throttled/coalesced saves on mutations; show a "Saving…" state driven by orchestrator progress; keep manual Save as explicit flush if desired.
- **Tradeoffs:** Simpler UX and consistent state; increased ES writes mitigated by coalescing; depends on reliable error surfacing.

### Option B: Draft/commit
- **Prerequisites:** Catalog supports draft models and versioning (Tier 1); window manager prevents duplicate windows for confusion-free drafts.
- **Approach:** Edit on draft copies; `Apply` routes through orchestrator (save -> reload -> notify); `Cancel` discards drafts. Display pending-change badges per tab.
- **Tradeoffs:** Clearer intent and isolation; more model plumbing and UX polish required.

## 4) Deletion list (legacy/noise removal)
- **Delete after Tier 0:**
  - `DispatcherTimer` fallback in `SaveCloseBtn_Click` (WorkflowManagerWindow) once Save & Close awaits orchestrator completion.
  - Ad-hoc constructors in `WorkflowManagerWindow.CreateCatalog` (`new WorkflowCatalogService(...)`, `new WorkflowCatalogChangeNotifier()`) once document-scoped resolution is in place.
  - Presenter-owned `ProjectSettingsSaveExternalEvent` instantiation; move to document scope.
- **Needs confirmation:**
  - Legacy workflow registry/command wiring (`IWorkflow`, `IWorkflowRegistry` singletons in `Startup`) — verify ribbon entrypoints before removal.
  - Trace-listener bootstrap in `WorkflowManagerPresenter` constructor — centralize or remove if redundant.
- **Delete now (safe):** None confirmed.

## 5) Concrete implementation notes (aligned with current code)
- **Document scoping:** Introduce a `DocumentServices` map keyed by `DocumentId`/`Document.UniqueId` that creates/reuses `WorkflowCatalogService`, `EsProjectSettingsProvider`, `WorkflowCatalogChangeNotifier`, and `ProjectSettingsSaveExternalEvent`. Register a factory in `Startup` to resolve through this map instead of global singletons.
- **Class touch points:**
  - `Startup.cs`: change singleton registrations to factories that pull from the document scope; remove presenter registration as a singleton so windows obtain presenter instances bound to the scoped services.
  - `WorkflowManagerWindow`: replace `CreateCatalog` ad-hoc construction with resolution from the document scope; obtain notifier and orchestrator from the same scope; stop caching `_settings`.
  - `WorkflowManagerPresenter`: accept the scoped orchestrator via constructor; remove `new ProjectSettingsSaveExternalEvent` inside; request saves through `ISaveOrchestrator.RequestSave`.
  - `ProjectSettingsSaveExternalEvent`: add coalescing/in-flight gating; on `Execute`, perform `catalog.Save`, then `catalog.Load`/`RefreshCaches`, then invoke `WorkflowCatalogChangeNotifier.NotifyChanged`, then callbacks. Ensure dispatcher callbacks surface exceptions.
- **Key methods/events:**
  - `RequestSave(reason, onCompleted)`: queues/coalesces; only one in-flight execution per document.
  - `OnPersisted(version)` (or equivalent) invoked after reload to clear dirty state, update UI, and allow window close.
- **Disposal:** On `DocumentClosed`, dispose the document scope: clear ExternalEvent queues, unsubscribe notifier listeners, and drop catalog/provider instances.
- **Refresh pattern:** Replace direct `ObservableCollection.Clear()/Add` rebuilds with diff/apply helpers or `ReadOnlyObservableCollection` projections backed by immutable snapshots to preserve selection.

## Evidence
- **DI registrations (singleton):** `WorkflowCatalogService`, `WorkflowCatalogChangeNotifier`, `EsProjectSettingsProvider`, and `WorkflowManagerPresenter` are registered as singletons in `Startup.ConfigureServices` lines 45–59; windows currently bypass these by constructing new instances when DI resolution fails.【F:src/GSADUs.Revit.Addin/Infrastructure/DI/Startup.cs†L45-L59】
- **Ad-hoc catalog/notifier construction & timer fallback:** `WorkflowManagerWindow` constructs new catalog/provider/notifier instances in `CreateCatalog` and window ctor, caches `_settings`, and uses a `DispatcherTimer` fallback to close after two seconds regardless of persistence outcome (`SaveCloseBtn_Click`).【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L51-L102】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L133-L172】
- **ExternalEvent orchestrator:** `ProjectSettingsSaveExternalEvent` is presenter-owned, per-window, lacks coalescing/in-flight gating, performs only `catalog.Save(force: true)` without reload-before-notify, and dispatches callbacks on completion.【F:src/GSADUs.Revit.Addin/UI/ProjectSettingsSaveExternalEvent.cs†L9-L84】
- **Presenter wiring:** `WorkflowManagerPresenter` creates its own `ProjectSettingsSaveExternalEvent` and notifies via `WorkflowCatalogChangeNotifier` before any reload step; this reinforces the need for document-scoped orchestration and reload-before-notify sequencing.【F:src/GSADUs.Revit.Addin/UI/Presenters/WorkflowManagerPresenter.cs†L18-L87】【F:src/GSADUs.Revit.Addin/UI/Presenters/WorkflowManagerPresenter.cs†L740-L772】
