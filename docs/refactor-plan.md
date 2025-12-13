# Revit Add-in Refactor Plan

## 1) Target architecture (per Revit Document)

**Ownership model**
- **Catalog (source of truth):** Single `WorkflowCatalogService` instance keyed by `Document`. Holds authoritative `AppSettings` and exposes immutable/read-only projections to windows.
- **ES provider:** Stateless `EsProjectSettingsProvider` resolved per document; invoked only via orchestrated save/load.
- **Save orchestrator:** Single `ProjectSettingsSaveExternalEvent` (or successor) per document that queues/coalesces save requests, executes on Revit API thread, enforces in-flight gating, and publishes completion with persisted version.
- **Notifier/Event stream:** Single `WorkflowCatalogChangeNotifier` per document. Emits post-save events **after** catalog reload from ES; consumers re-query catalog instead of holding private snapshots.
- **Windows/Presenters:** Consume catalog projections; never store private `_settings`. All mutations go through catalog; persistence goes through orchestrator; refresh uses notifier-driven reload.

**Lifecycle & scoping**
- Instances keyed by `DocumentId` in a map or DI scoped to document. Dispose orchestrator/notifier/catalog when document closes (unsubscribe, clear queues).

**Diagram**
```
[Window / Presenter]
    |  (read/write via projections)
    v
[Catalog (per Doc, source of truth)]
    |    ^
    |    | reload-after-save
    v    |
[Save Orchestrator (ExternalEvent queue)]
    |
    v
[ES Provider (stateless)]
    |
    v
[Extensible Storage]

[Notifier/Event Stream] <------ post-save after reload ------^
       ^                                                    |
       |------------------- listeners re-query -------------|
```

## 2) Primary refactor plan

### Tier 0 (1–3 days): Stop the bleeding
- **Goal:** Enforce document-scoped singletons for catalog/notifier/save orchestrator; serialize saves with coalescing and reload-before-notify.
- **Changes:**
  - Replace ad-hoc `new WorkflowCatalogService`/`new WorkflowCatalogChangeNotifier` with document-scoped resolver (map or DI scope). Remove window-owned `_settings` caches.
  - Centralize `ProjectSettingsSaveExternalEvent` per document; add in-flight flag + coalescing queue; ensure `Execute` loads from ES, updates catalog, then notifies.
  - Adjust save callbacks to await orchestrator completion; disable `DispatcherTimer` close fallback once deterministic completion path exists.
- **Acceptance criteria:**
  - Opening multiple windows for same document shows shared data; saves from one are visible in others after reload.
  - Rapid consecutive saves produce one ES write per flush; callbacks fire once per flush in order.
  - Notifications always follow reload; no stale UI snapshots; close waits on actual completion (no timer fire).
- **Risk:** Low-medium (scoping refactor touches window creation; ExternalEvent queue changes thread-sensitive).
- **Effort:** 1–3 days.
- **Rollback:** Reintroduce per-window instances and timer fallback behind feature flags; keep existing ExternalEvent queue implementation in branch for quick revert.

### Tier 1 (1–2 weeks): Make it reliable
- **Goal:** Stabilize bindings and dirty/version tracking; remove silent failures.
- **Changes:**
  - Replace destructive `ObservableCollection.Clear()/rebuild` with diff-based updates or read-only projections; preserve selection.
  - Move dirty/version tracking into catalog with deterministic increments and `Saving/Saved` status.
  - Centralize logging/error handling; remove `catch { }` swallowing; surface failures through notifier/UI.
- **Acceptance criteria:**
  - No WPF `InvalidOperationException` from collection refresh during typical operations.
  - Dirty state reflects catalog version and resets only after persisted reload.
  - Errors propagate to logs/user notification; no silent failures in save/notify pipeline.
- **Risk:** Medium (binding changes may expose latent UI assumptions).
- **Effort:** 1–2 weeks.
- **Rollback:** Keep old refresh method and dirty flags behind toggles; can revert to Clear/rebuild if blocking issues arise (with known selection loss).

### Tier 2 (longer): Make it clean
- **Goal:** Simplify model boundaries and window coordination.
- **Changes:**
  - Split read models (immutable projections) from draft write models (mutable copy merged by catalog) to decouple UI grids from domain state.
  - Add lightweight window manager preventing duplicate windows per document and coordinating refresh/focus.
  - Remove unused legacy registry/commands after confirmation; clean DI registrations.
- **Acceptance criteria:**
  - UI binds to read-only projections; edits occur on draft and apply commits through catalog.
  - Attempting to open duplicate window reuses existing instance or focuses it.
  - Unused legacy components removed without breaking ribbon commands.
- **Risk:** Medium-high (touches UX flows, potential command wiring impacts).
- **Effort:** Longer-term/iterative.
- **Rollback:** Retain current window creation paths and legacy registrations in branch; feature-flag draft/commit if needed.

## 3) Secondary plans (optional directions)

### Option A: Saveless autosave
- **Prerequisites:** Tier 0 coalescing save orchestrator and reload-before-notify contract; stable catalog projections.
- **Approach:** Every mutation triggers throttled/coalesced save; UI shows "Saving…" status until orchestrator completes; remove explicit Save buttons or demote to manual flush.
- **Tradeoffs:** Simpler UX; consistent state across windows; potential increased ES writes mitigated by coalescing. Requires reliable failure surfacing.

### Option B: Draft/commit
- **Prerequisites:** Catalog supports draft models and versioning; deterministic dirty tracking (Tier 1).
- **Approach:** Edits occur on draft copy; Apply commits through save orchestrator (reload then notify); Cancel discards draft. Windows show pending changes badge.
- **Tradeoffs:** Strong isolation between windows; clearer user intent; more code to manage drafts and conflict checks.

## 4) Deletion list (legacy/noise removal)
- **Delete after Tier 0:** `DispatcherTimer` fallback in Save & Close once orchestrator completion is reliable and awaited.
- **Needs confirmation:** Legacy `IWorkflow`/`IWorkflowRegistry` registrations (remove if no ribbon commands rely on them); trace-listener bootstrapping per presenter (centralize or remove if unused); ad-hoc `EsProjectSettingsProvider` instantiations (replace with scoped resolver once scoping proven safe); exception-swallowing in `ProjectSettingsSaveExternalEvent` (replace with surfaced errors).
- **Delete now (safe):** None identified without confirming usage.

## 5) Concrete implementation notes
- **Scoping/keys:** Use `DocumentId`-keyed dictionary or DI scope keyed by `Document` to provide catalog, notifier, save orchestrator, and ES provider. Dispose entries on `DocumentClosed` (unsubscribe notifiers, clear queues).
- **Interfaces/boundaries:**
  - `IWorkflowCatalog` exposes read-only projections, mutation methods, `Version`, `IsDirty`, `Saving` status, and `RequestSave(reason)` delegating to orchestrator.
  - `ISaveOrchestrator` handles `RequestSave(reason, continuation)`; coalesces requests; raises ExternalEvent; on execute performs `LoadFromES -> SaveToES -> Catalog.Reload(version) -> Notifier.Notify(version)`.
  - `INotifier` publishes `CatalogChanged(DocumentId, version)` events; listeners re-query catalog.
- **Key methods/events:**
  - `RequestSave(reason)` from presenters; `OnPersisted(version)` callback invoked after reload to update UI state/close windows.
  - `Catalog.ReloadFromEs()` invoked only inside orchestrator post-save to ensure source-of-truth alignment.
- **Threading:** Save orchestrator owns ExternalEvent; ensures single in-flight execution with queue depth >1 coalesced. UI callbacks use dispatcher with explicit exception logging.
- **Window creation:** Windows resolve catalog/notifier/orchestrator from document scope; no ad-hoc `new` when DI fails—fail fast with clear error.
- **Refresh pattern:** Replace `ObservableCollection` clears with diff/apply helpers or expose `ReadOnlyObservableCollection` backed by immutable snapshots; presenters update selections via stable IDs.

## Evidence references
- Multiple catalog/notifier instances and window-owned ExternalEvent queues: architecture review and discovery findings.【F:docs/architecture-review.md†L5-L87】【F:docs/discovery-pass.md†L5-L60】
- Save pipeline issues (no coalescing, reload gap, timer fallback, exception swallowing): review/discovery sections.【F:docs/architecture-review.md†L14-L63】【F:docs/discovery-pass.md†L17-L55】
- Binding refresh and exception swallowing risks: review findings.【F:docs/architecture-review.md†L23-L35】【F:docs/architecture-review.md†L61-L74】
- Legacy candidates and confirmation list: discovery findings.【F:docs/discovery-pass.md†L55-L89】
