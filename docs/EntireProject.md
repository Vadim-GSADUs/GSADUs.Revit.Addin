# Plan: Enable `EntireProject` Runs for CSV Only

## Decisions
- Scope support:
  - RVT, PDF, Image: `SelectionSet` only (default and enforced in UI).
  - CSV: allow `SelectionSet` and `EntireProject` (default remains `SelectionSet`).
- Mixed selections: If any set-scoped workflow is selected, at least one set is required. If all selected workflows are CSV with `Scope == EntireProject`, zero sets are allowed.

## 1) Workflow authoring constraints
- Constrain `Scope` options in Workflow Manager:
  - PDF/Image/RVT tabs expose only `SelectionSet`.
  - CSV tab exposes `SelectionSet` and `EntireProject`.
- Keep default seeds as `SelectionSet` for all workflow types; do not auto-downgrade saved CSV `EntireProject` scopes.

## 2) Batch Export window validation
- Allow proceeding with zero sets only when all selected workflows are CSV with `Scope == EntireProject`.
- If any selected workflow is set-scoped (or non-CSV), require at least one set; otherwise, block with a clear message.
- Continue persisting `SelectedWorkflowIds` as-is.

## 3) Coordinator preflight and set resolution
- Remove the early "No Selection Filters found" guard when the selected workflows are all CSV with `Scope == EntireProject`.
- After dialog:
  - If all selected workflows are CSV/EntireProject: continue even when `selectedFilterElems.Count == 0`.
  - Otherwise: maintain current behavior (require resolved sets).

## 4) Scope-aware execution path
- Project-scoped CSV run:
  - Execute actions once outside the per-set loop.
  - Bypass staging (`TryToggleCurrentSet`, `TryStageMove`, `TryRestoreStage`).
  - Pass empty or null set context (e.g., `setName = string.Empty`, `preserveUids = []`).
- Set-scoped run (existing):
  - Keep current per-set iteration and staging behavior.

## 5) Actions
- CSV: already tolerates empty set context and has scope-aware default patterns (`{FileName} {ViewName}` for `EntireProject`). No changes required.
- PDF/Image/RVT: remain set-scoped; no action changes required.

## 6) Telemetry & logging
- Optionally log whether a run was project-wide CSV vs per-set; if logging to the batch CSV file, consider a synthetic key like `EntireProject` for project runs or skip set-keyed writes.

## 7) Testing checklist
- Zero sets + CSV(EntireProject): dialog proceeds; coordinator runs once; files exported; no staging.
- Zero sets + PDF/Image/RVT: blocked.
- Mixed selection: requires at least one set; with sets, only set-scoped workflows execute per set; CSV(EntireProject) either runs once or is disallowed for mixing (based on chosen policy).
