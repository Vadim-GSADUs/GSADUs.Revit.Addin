# Revit Add-in Refactor Plan (grounded in current code)

## Anchor: latest main (2025-12-12 planning commits)
- **5337121 Add workflow catalog discovery notes (#14)** — adds discovery findings into `docs/discovery-pass.md` (evidence for current architecture/state).
- **718bb59 Add targeted refactor probing and plan (#13)** — planning-only commit (no file-level stat output in git show; treat as reference for earlier planning thread).

## 1) Target architecture (per Revit Document)

**Ownership model**
- **Catalog (source of truth):** One `WorkflowCatalogService` per Revit `Document`. This instance owns the authoritative `AppSettings` and supplies read-only projections; presenters/windows never keep private `_settings` copies.
- **ES provider:** One `EsProjectSettingsProvider` per `Document`, resolved through the document scope and supplied to the catalog/orchestrator. Provider remains stateless.
- **Save orchestrator:** One `ProjectSettingsSaveExternalEvent` (or successor) per document that owns the ExternalEvent, coalesces/gates requests, executes on the Revit API thread, and drives the canonical flow: mutate in-memory catalog -> `SaveToES` -> `ReloadFromES` (rehydrate) -> increment catalog version/clear dirty -> notify subscribers with `(DocumentKey, Version, ReasonFlags)`.
- **Notifier/Event stream:** One `WorkflowCatalogChangeNotifier` per document that only fires after the catalog reloads from ES, so all windows re-query the shared catalog rather than relying on cached snapshots.
- **Windows/Presenters:** Resolve the document-scoped catalog, orchestrator, and notifier; do not construct them ad-hoc. UI binds to projections and issues `RequestSave` through the orchestrator.

**Save pipeline default (deterministic) flow**
- Canonical flow (no UI blocking): **mutate in-memory catalog -> `SaveToES` via document-scoped ExternalEvent -> `ReloadFromES` (rehydrate persisted output) -> increment catalog version/clear dirty -> notify subscribers with `(DocumentKey, Version, ReasonFlags)`**. UI surfaces may show non-blocking "Saving…" based on notifier completion but must never block on modal joins.
- "LoadFromES before Save" is **not** the default. It should only exist if explicit merge/conflict logic is introduced; in the current single-user model, pre-loads can reintroduce stale state and nondeterminism and risk modal deadlocks when paired with ExternalEvent waits.

**Lifecycle & scoping**
- Key the document scope by **`Document.UniqueId` (preferred for stability); `DocumentId` is acceptable only with reliable disposal on close**.
- Per-document owned services: `WorkflowCatalogService`, `EsProjectSettingsProvider`, `ProjectSettingsSaveExternalEvent` (or successor), and `WorkflowCatalogChangeNotifier`.
- Dispose the scope on Revit document closure via `Application.DocumentClosed` (wire subscription in the ExternalApplication `OnStartup` hook) to clear queues, dispose ExternalEvent handles, and drop notifier subscriptions.

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

- **Goal:** Enforce document-scoped ownership, serialize saves with coalescing and deterministic completion, and eliminate ad-hoc construction/timer closes once sequencing is correct.
- **Changes:**
	- Introduce a document-scoped container (map or DI scope) keyed by `Document.UniqueId` (or `DocumentId` if disposed on close) that returns the single catalog, notifier, ES provider, and save orchestrator for that document. Replace `new WorkflowCatalogService(new EsProjectSettingsProvider(...))` and `new WorkflowCatalogChangeNotifier()` paths in `WorkflowManagerWindow.CreateCatalog` with scoped resolution; drop the private `_settings` cache in the window/presenter.
	- Move `ProjectSettingsSaveExternalEvent` to the document scope. Add in-flight gating and request coalescing; ensure `Execute` performs `catalog.Save` then `catalog.ReloadFromEs` (or equivalent) before invoking `WorkflowCatalogChangeNotifier.NotifyChanged` and callbacks. Prevent concurrent `ExternalEvent.Raise` overlap.
	- Update presenter/window save flow to use non-blocking status indicators only; do **not** block modal windows waiting for ExternalEvent completion. Remove the `DispatcherTimer` close fallback only after deterministic notify sequencing is wired and UI is modeless.
	- Align DI with the document scope: current `Startup.cs` registers `WorkflowCatalogService`, `WorkflowCatalogChangeNotifier`, `EsProjectSettingsProvider`, and `WorkflowManagerPresenter` as global singletons; windows bypass DI and create ad-hoc instances. Add a factory keyed by document and update consumers accordingly.
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

## 4) Modal Risk Inventory (saveless/modeless focus)
| UI surface | How opened today | Modal risk | Persistence trigger | Recommended change |
| --- | --- | --- | --- | --- |
| Workflow Manager (`WorkflowManagerWindow`) | `ShowDialog` from Batch Export and Settings; singleton guard only per-process static | **Modal**; owns `_presenter.SaveSettings` and `ProjectSettingsSaveExternalEvent` callbacks, plus `DispatcherTimer` close fallback | Saves workflows via presenter/external event | Convert to **modeless** `Show()` with per-document window manager; remove `DispatcherTimer` close; no modal waiting on ExternalEvent. |
| Batch Export (`BatchExportWindow`) | `ShowDialog` from `BatchRunCoordinator.RunOnce` | **Modal**; loads settings, can launch Workflow Manager modally; refresh depends on settings reload | Uses ES settings, can open Workflow Manager and reload after close | Make **modeless** and refresh via notifier; open Workflow Manager modeless; ensure saves are orchestrator-driven without blocking dialogs. |
| Settings (`SettingsWindow`) | `ShowDialog` from Batch Export; opens Workflow Manager modally | **Modal**; writes settings directly via provider; can open nested modal Workflow Manager | Imports/exports and manages workflows | Convert to **modeless**; rely on shared catalog and notifier; remove nested modal Workflow Manager; use status indicator only. |
| Rename Workflow dialog | `ShowDialog` from presenter | **Modal nested** within Workflow Manager; blocks UI thread during rename | Mutates catalog and triggers save | Replace with inline rename/editor panel in modeless Workflow Manager; avoid nested dialogs. |
| Selection/Audit subdialogs (Categories/Elements pickers, Batch confirm MessageBoxes) | `ShowDialog` or `MessageBox.Show` | Modal but short-lived; do not wait on ExternalEvent | No direct ES save, mainly selection | Prefer Revit-native prompts or non-blocking panels; ensure no ExternalEvent waits are tied to them. |

## 5) Saveless + Modeless Architecture
- **Window strategy:** All long-lived configuration surfaces (Workflow Manager, Batch Export, Settings) run **modeless** via `Show()` and are managed per document by a window manager that activates existing instances instead of spawning duplicates. Static `_activeInstance` in `WorkflowManagerWindow` becomes document-keyed to avoid cross-document bleed.
- **Persistence strategy:** Any mutation calls `RequestSave` on the document-scoped ExternalEvent orchestrator. UI never blocks on completion; status badges clear when the notifier fires after reload. Save coalescing/in-flight gating remains in orchestrator (no modal join semantics).
- **Error handling:** Notify users via non-blocking banners/dialogs on dispatcher after notifier reports failure; never hold windows in "Saving…". Exceptions in ExternalEvent are logged and surfaced without locking UI.

## 6) Refactor steps (saveless/modeless, minimal risk)
1. **Add per-document WindowManager** that tracks modeless instances (Workflow Manager, Batch Export, Settings) keyed by `Document.UniqueId`; wire cleanup on `DocumentClosed` from ExternalApplication startup.
2. **Convert Workflow Manager to modeless**: switch callers from `ShowDialog` to `Show`, remove `_activeInstance` static in favor of document-scoped window manager, and delete `DispatcherTimer` close fallback once notifier-driven completion is in place.
3. **Convert Batch Export to modeless**: open via window manager, listen to notifier for workflow list refresh instead of waiting on modal Workflow Manager close; remove modal chain.
4. **Convert Settings to modeless**: open via window manager, replace nested modal Workflow Manager launches with focus/activate; hook notifier to refresh settings view-model; keep import/export prompts non-blocking.
5. **Replace modal rename dialog** with inline rename editor/panel; remove `ShowDialog` dependency in presenter and route rename mutations through the catalog + orchestrator; ensure save status indicator is non-blocking.
6. **Re-validate save orchestrator**: keep coalescing/in-flight gating; completion callbacks only update status indicators—not window lifetime. Ensure notifier fires after reload so windows refresh without modal waits.

## 7) Acceptance criteria & manual Revit checks
- Rename a workflow, close/reopen Workflow Manager: rename persists and is visible in Batch Export without Revit restart.
- Batch Export reflects workflow changes pushed from Workflow Manager without reopening the document.
- No window remains stuck in "Saving…"; modeless windows can be closed even while a save is in-flight, and notifier clears status afterward.
- Workflow duplication flow does not crash; notifier refresh keeps selections stable.

## 8) Deletion list (legacy/noise removal)
- **Delete after Tier 0:**
	- `DispatcherTimer` fallback in `SaveCloseBtn_Click` (WorkflowManagerWindow) once Save & Close awaits orchestrator completion.
	- Ad-hoc constructors in `WorkflowManagerWindow.CreateCatalog` (`new WorkflowCatalogService(...)`, `new WorkflowCatalogChangeNotifier()`) once document-scoped resolution is in place.
	- Presenter-owned `ProjectSettingsSaveExternalEvent` instantiation; move to document scope.
- **Needs confirmation:**
	- Legacy workflow registry/command wiring (`IWorkflow`, `IWorkflowRegistry` singletons in `Startup`) — verify ribbon entrypoints before removal.
	- Trace-listener bootstrap in `WorkflowManagerPresenter` constructor — centralize or remove if redundant.
- **Delete now (safe):** None confirmed.

## 9) Concrete implementation notes (aligned with current code)
- **Document scoping:** Introduce a `DocumentServices` map keyed by `Document.UniqueId` (preferred; fall back to `DocumentId` only if disposal on close is guaranteed) that creates/reuses `WorkflowCatalogService`, `EsProjectSettingsProvider`, `WorkflowCatalogChangeNotifier`, and `ProjectSettingsSaveExternalEvent`. Register a factory in `Startup` to resolve through this map instead of global singletons, and subscribe to `Application.DocumentClosed` from the ExternalApplication `OnStartup` to dispose the per-document entry.
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

## Evidence (current code hotspots)
- `Startup.cs` registers `WorkflowCatalogService`, `WorkflowCatalogChangeNotifier`, `EsProjectSettingsProvider`, and `WorkflowManagerPresenter` as singletons (global lifetimes rather than document-scoped).
- `WorkflowManagerWindow` constructs new catalog/provider/notifier instances in `CreateCatalog` and the window constructor, caches `_settings`, and uses a `DispatcherTimer` fallback to close after two seconds regardless of persistence outcome (`SaveCloseBtn_Click`).
- `ProjectSettingsSaveExternalEvent` is presenter-owned, per-window, lacks coalescing/in-flight gating, performs only `catalog.Save(force: true)` without reload-before-notify, and dispatches callbacks on completion.
- `WorkflowManagerPresenter` creates its own `ProjectSettingsSaveExternalEvent` and notifies via `WorkflowCatalogChangeNotifier` before any reload step; this is why document-scoped orchestration and reload-before-notify sequencing matter.
