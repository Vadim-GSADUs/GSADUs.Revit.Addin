# Targeted discovery – workflow catalog & save pipeline

## Instance graph & lifetimes

| Component | Instantiation path | Lifetime / scope | Notes & risks |
|-----------|--------------------|------------------|---------------|
| WorkflowCatalogService | Registered as singleton in `ServiceBootstrap.ConfigureServices` and also constructed ad-hoc in `WorkflowManagerWindow.CreateCatalog` (per-window for document or ActiveUIDocument) | DI singleton (global) plus per-window `new` instances keyed by document parameter | Multiple catalogs can exist simultaneously for the same document (DI singleton + window-local), leading to divergent in-memory settings and unsynchronized saves. 【F:src/GSADUs.Revit.Addin/Infrastructure/DI/Startup.cs†L45-L59】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L51-L102】 |
| EsProjectSettingsProvider | Registered as singleton in DI; numerous ad-hoc `new EsProjectSettingsProvider` calls across windows/commands (document-dependent resolver) | DI singleton (global) plus ad-hoc transient factories per window/command; document resolved on each call | Each consumer may resolve a different `Document` via lambda; simultaneous instances per document are common. No caching; each new instance reloads settings independently. 【F:src/GSADUs.Revit.Addin/Infrastructure/DI/Startup.cs†L55-L59】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L88-L102】【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L38-L80】 |
| ProjectSettingsSaveExternalEvent | Constructed inside `WorkflowManagerPresenter` with current dispatcher and catalog | Presenter-owned per-window instance (transient) | Each Workflow Manager window spawns its own `ExternalEvent` + callback queue. If multiple windows open, raises are isolated; queued callbacks are drained per handler but shared catalog is not guaranteed. 【F:src/GSADUs.Revit.Addin/UI/Presenters/WorkflowManagerPresenter.cs†L16-L87】【F:src/GSADUs.Revit.Addin/UI/ProjectSettingsSaveExternalEvent.cs†L9-L83】 |
| WorkflowCatalogChangeNotifier | Registered as singleton in DI; window falls back to `new` when DI missing | DI singleton (global) or ad-hoc singleton-like per window | Batch Export subscribes to DI notifier when available; if Workflow Manager constructs a private instance, Batch Export listeners never fire, so cross-window refreshes fail. 【F:src/GSADUs.Revit.Addin/Infrastructure/DI/Startup.cs†L45-L59】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L51-L56】【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L38-L80】 |

## Save pipeline maps

### "Save & Close" from Workflow Manager
1. Button click in `WorkflowManagerWindow.SaveCloseBtn_Click` disables button, starts 2s `DispatcherTimer` fallback. 【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L133-L172】
2. Calls `_presenter.SaveSettings` → `RequestCatalogSave`. 【F:src/GSADUs.Revit.Addin/UI/Presenters/WorkflowManagerPresenter.cs†L742-L765】
3. `RequestCatalogSave` enqueues callback into `ProjectSettingsSaveExternalEvent.RequestSave` (thread-safe queue) and calls `_externalEvent.Raise()`. 【F:src/GSADUs.Revit.Addin/UI/ProjectSettingsSaveExternalEvent.cs†L24-L35】
4. Revit invokes `ProjectSettingsSaveExternalEvent.Execute`; it calls `_catalog.Save(force:true)` (no document reload), catches exceptions, logs success/failure, copies callbacks. 【F:src/GSADUs.Revit.Addin/UI/ProjectSettingsSaveExternalEvent.cs†L37-L80】
5. For each callback, dispatcher `BeginInvoke` executes completion on UI thread. Workflow presenter callback triggers `WorkflowCatalogChangeNotifier.NotifyChanged()` on success before invoking continuation; window continuation closes dialog. 【F:src/GSADUs.Revit.Addin/UI/Presenters/WorkflowManagerPresenter.cs†L747-L765】【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L146-L171】
6. No coalescing beyond FIFO queue; every call to `RequestSave` raises the same `ExternalEvent` and drains callbacks after each execution.
7. No reload-from-ES after save; `_catalog` retains in-memory settings and change flags are cleared by `Save()`. 【F:src/GSADUs.Revit.Addin/Services/WorkflowCatalogService.cs†L104-L121】

Ordering risks: fallback timer may close window before callback if save hangs; multiple windows have independent external events so ordering across them is undefined. No back-pressure besides queue; successive `Raise` calls will execute sequentially per Revit’s external event scheduling but callers do not await completion before issuing new saves.

### "Duplicate workflow" then save
1. UI command `DuplicateSelectedCommand` calls `_catalog.Duplicate(...)` directly; this marks catalog dirty and refreshes collections but does not persist. 【F:src/GSADUs.Revit.Addin/UI/ViewModels/WorkflowManagerViewModel.cs†L13-L55】【F:src/GSADUs.Revit.Addin/Services/WorkflowCatalogService.cs†L79-L100】
2. User clicks Save (tab-level) or Save & Close: path matches above, invoking `ProjectSettingsSaveExternalEvent` to persist. No implicit save occurs on duplication.
3. Reload-from-ES does not happen post-save, so cloned data in memory remains source of truth.

### Batch Export refresh on save
- Batch Export subscribes to `WorkflowCatalogChangeNotifier` (DI singleton). On notification, it `BeginInvoke` refresh of workflow list. 【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L38-L80】
- If Workflow Manager instantiated its own notifier (when DI resolution fails or different provider), Batch Export never receives updates; user must reopen window to see changes. No batching/coalescing; each Notify triggers a refresh invocation.

## Legacy / grandfathered candidates
- **Fallback close timer in Save & Close** – `DispatcherTimer` closes window after 2s even if save hasn’t completed; likely diagnostic crutch that can mask failed saves. 【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L140-L172】 (Needs confirmation)
- **Trace listener bootstrapping in presenter** – File trace setup wrapped in broad try/catch, initializes per presenter instance; likely diagnostic legacy. 【F:src/GSADUs.Revit.Addin/UI/Presenters/WorkflowManagerPresenter.cs†L26-L87】 (Keep, but isolate)
- **Legacy workflow registry/commands** – DI registers `IWorkflow`/`IWorkflowRegistry` as “legacy command registry,” may be unused by current UI-driven catalog. 【F:src/GSADUs.Revit.Addin/Infrastructure/DI/Startup.cs†L38-L44】 (Needs confirmation)
- **Multiple ad-hoc `EsProjectSettingsProvider` instantiations** across UI windows/commands; likely historical fallback for missing DI leading to redundant loaders. 【F:src/GSADUs.Revit.Addin/UI/WorkflowManagerWindow.xaml.cs†L88-L102】【F:src/GSADUs.Revit.Addin/UI/BatchExportWindow.xaml.cs†L38-L80】 (Keep, but isolate)
- **Callback queue without result propagation** – `ProjectSettingsSaveExternalEvent` swallows exceptions and returns `false` silently to callbacks; no user feedback besides optional dialog. Potentially leftover diagnostic suppression. 【F:src/GSADUs.Revit.Addin/UI/ProjectSettingsSaveExternalEvent.cs†L37-L80】 (Needs confirmation)

## Top confirmation questions
1. Should Workflow Manager and Batch Export share a single catalog/notifier per `Document`, or is isolation across windows intentional?
2. Is the 2-second fallback close timer still required for UX, or can Save & Close await the actual callback?
3. Are the legacy `IWorkflow`/`IWorkflowRegistry` registrations still exercised by any commands or ribbon entries?
4. Do any features rely on ad-hoc `EsProjectSettingsProvider` instances instead of the DI singleton (e.g., cross-document operations)?
5. Should save failures surface to users (dialogs/log) rather than being swallowed in `ProjectSettingsSaveExternalEvent`?
