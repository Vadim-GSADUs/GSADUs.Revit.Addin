# Revit Add-in Refactor Plan

## 1) Target architecture (per Revit Document)

**Ownership model**
- **Catalog (source of truth):** Single `WorkflowCatalogService` instance keyed by **`Document.UniqueId`**. Holds authoritative `AppSettings` and exposes immutable/read-only projections to windows.
- **ES provider:** Stateless `EsProjectSettingsProvider` resolved per document; invoked only via orchestrated save/load.
- **Save orchestrator:** Single `ProjectSettingsSaveExternalEvent` (or successor) per document that queues/coalesces save requests, executes on Revit API thread, enforces in-flight gating, and publishes completion with persisted version.
- **Notifier/Event stream:** Single `WorkflowCatalogChangeNotifier` per document. Emits post-save events **after** catalog reload from ES; consumers re-query catalog instead of holding private snapshots.
- **Windows/Presenters:** Consume catalog projections; never store private `_settings`. All mutations go through catalog; persistence goes through orchestrator; refresh uses notifier-driven reload.

**Lifecycle & scoping**
- Instances keyed by **`Document.UniqueId`** in a map or document-scoped DI container.
- Scope is disposed on `DocumentClosed` (unsubscribe notifiers, clear save queues, release references).
- `DocumentId` is not used as a key because it is session-scoped and not stable across document reloads.

**Diagram**

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


## 2) Primary refactor plan

### Tier 0 (1–3 days): Stop the bleeding
- **Goal:** Enforce document-scoped singletons for catalog/notifier/save orchestrator; serialize saves with coalescing and reload-before-notify.
- **Changes:**
  - Replace ad-hoc `new WorkflowCatalogService` / `new WorkflowCatalogChangeNotifier` with a document-scoped resolver keyed by `Document.UniqueId`. Remove window-owned `_settings` caches.
  - Centralize `ProjectSettingsSaveExternalEvent` per document; add in-flight flag + coalescing queue.
  - **Canonical save flow (default, single-user):**
    1) mutate in-memory catalog  
    2) **SaveToES** (ExternalEvent on API thread)  
    3) **ReloadFromES** (or rehydrate from persisted output)  
    4) increment catalog version / clear dirty  
    5) notify subscribers
  - “LoadFromES before Save” is **not** the default; it is only valid if explicit merge/conflict logic is introduced later.
  - Adjust Save & Close to await orchestrator completion; **remove `DispatcherTimer` close fallback only after deterministic completion exists**.
- **Acceptance criteria:**
  - Multiple windows for the same document show shared data; saves from one are visible in others after reload.
  - Rapid consecutive saves coalesce into a single ES write per flush; callbacks fire once per flush, in order.
  - Notifications always follow reload; no stale UI snapshots.
  - **If persistence fails, Save & Close does not close the window and surfaces the error** (no optimistic close).
- **Risk:** Low–medium (scoping refactor touches window creation; ExternalEvent queue is thread-sensitive).
- **Effort:** 1–3 days.
- **Rollback:** Reintroduce per-window instances and timer fallback behind feature flags; keep prior ExternalEvent implementation on a branch for quick revert.

### Tier 1 (1–2 weeks): Make it reliable
- **Goal:** Stabilize bindings and dirty/version tracking; remove silent failures.
- **Changes:**
  - Replace destructive `ObservableCollection.Clear()/rebuild` with diff-based updates or read-only projections; preserve selection via stable IDs.
  - Move dirty/version tracking into catalog with deterministic increments and `Saving/Saved` status.
  - Centralize logging/error handling; remove `catch { }` swallowing; surface failures through notifier/UI.
- **Acceptance criteria:**
  - No WPF `InvalidOperationException` from collection refresh during typical operations.
  - Dirty state reflects catalog version and resets only after persisted reload.
  - Errors propagate to logs/user notification; no silent failures in save/notify pipeline.
- **Risk:** Medium (binding changes may expose latent UI assumptions).
- **Effort:** 1–2 weeks.
- **Rollback:** Keep old refresh method and dirty flags behind toggles; revert to Clear/rebuild if blocking issues arise (with known selection loss).

### Tier 2 (longer): Make it clean
- **Goal:** Simplify model boundaries and window coordination.
- **Changes:**
  - Split read models (immutable projections) from draft write models (mutable copy merged by catalog) to decouple UI grids from domain state.
  - Add lightweight window manager preventing duplicate windows per document and coordinating refresh/focus.
  - Remove unused legacy registry/commands after confirmation; clean DI registrations.
- **Acceptance criteria:**
  - UI binds to read-only projections; edits occur on drafts and apply commits through catalog.
  - Attempting to open a duplicate window reuses or focuses the existing instance.
  - Unused legacy components removed without breaking ribbon commands.
- **Risk:** Medium–high (touches UX flows and command wiring).
- **Effort:** Longer-term/iterative.
- **Rollback:** Retain current window creation paths and legacy registrations on a branch; feature-flag draft/commit if needed.

## 3) Secondary plans (optional directions)

### Option A: Saveless autosave
- **Prerequisites:** Tier 0 coalescing save orchestrator and reload-before-notify contract; stable catalog projections.
- **Approach:** Every mutation triggers throttled/coalesced save; UI shows “Saving…” status until completion; remove or demote explicit Save buttons.
- **Tradeoffs:** Simpler UX; consistent state across windows; mitigated ES write volume via coalescing. Requires reliable failure surfacing.

### Option B: Draft/commit
- **Prerequisites:** Catalog supports drafts and versioning; deterministic dirty tracking (Tier 1).
- **Approach:** Edits occur on a draft copy; Apply commits through orchestrator (save → reload → notify); Cancel discards draft.
- **Tradeoffs:** Strong isolation between windows; clearer user intent; more code to manage drafts and conflicts.

## 4) Deletion list (legacy/noise removal)
- **Delete after Tier 0:** `DispatcherTimer` fallback in Save & Close once orchestrator completion is reliable and awaited.
- **Needs confirmation:** Legacy `IWorkflow` / `IWorkflowRegistry` registrations (remove if no ribbon commands rely on them); trace-listener bootstrapping per presenter; ad-hoc `EsProjectSettingsProvider` instantiations (replace with scoped resolver); exception swallowing in save orchestration.
- **Delete now (safe):** None identified without confirming usage.

## 5) Concrete implementation notes
- **Scoping/keys:** Use a `Document.UniqueId`-keyed dictionary or document-scoped DI container to provide catalog, notifier, save orchestrator, and ES provider. Dispose entries on `DocumentClosed`.
- **Interfaces/boundaries:**
  - `IWorkflowCatalog`: read-only projections, mutation methods, `Version`, `IsDirty`, `Saving`, and `RequestSave(reason)`.
  - `ISaveOrchestrator`: `RequestSave(reason, continuation)`; coalesces requests; raises ExternalEvent; on execute performs **SaveToES → ReloadFromES → Notifier.Notify(version)**.
  - `INotifier`: publishes `CatalogChanged(DocumentUniqueId, version)`; listeners re-query catalog.
- **Key methods/events:** `RequestSave(reason)` from presenters; `OnPersisted(version)` after reload to update UI state/close windows.
- **Threading:** Orchestrator owns ExternalEvent; single in-flight execution with coalescing. UI callbacks use dispatcher with explicit exception logging.
- **Window creation:** Windows resolve services from document scope; no ad-hoc `new` on DI failure—fail fast with clear error.
- **Refresh pattern:** Replace collection clears with diff/apply helpers or expose `ReadOnlyObservableCollection` backed by immutable snapshots; maintain selection via stable IDs.

## Evidence references
- Multiple catalog/notifier instances and window-owned ExternalEvent queues: architecture review and discovery findings.
- Save pipeline issues (no coalescing, reload gap, timer fallback, exception swallowing): review/discovery sections.
- Binding refresh and exception swallowing risks: review findings.
- Legacy candidates and confirmation list: discovery findings.
