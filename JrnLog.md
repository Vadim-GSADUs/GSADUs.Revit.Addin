# GSADUs Journal Logging Plan

## Phase 0 — TraceListener Rolling Log (source of truth)

Goal: Capture minimal, high-signal breadcrumbs to a per-run file with near-zero code.

### 0.1 Initialize once per batch run
- Place at batch entry (ExternalCommand or coordinator).
- Example:
	```csharp
	using System.Diagnostics;

	static class RunLog
	{
		public static string CorrId { get; private set; }
		public static string FilePath { get; private set; }
		static bool _inited;

		public static void Begin(string name)
		{
			if (_inited) return;
			CorrId = Guid.NewGuid().ToString("N")[..8];

			var dir = Path.Combine(Path.GetTempPath(), "GSADUs", "Runs");
			Directory.CreateDirectory(dir);
			FilePath = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{name}_{CorrId}.log");

			Trace.Listeners.Add(new TextWriterTraceListener(FilePath));
			Trace.AutoFlush = true;

			Trace.WriteLine($"BEGIN {name} corr={CorrId}");
			_inited = true;
		}

		public static void End(string name, long elapsedMs)
		{
			Trace.WriteLine($"END {name} {elapsedMs}ms corr={CorrId}");
		}

		public static void Step(string label) => Trace.WriteLine($"STEP {label} corr={CorrId}");
		public static void Fail(string where, Exception ex) =>
			Trace.WriteLine($"FAIL {where} ex={ex.GetType().Name} msg={ex.Message} corr={CorrId}");
	}
	```

Usage:
```csharp
var sw = Stopwatch.StartNew();
RunLog.Begin("BatchExport");
// … work …
RunLog.End("BatchExport", sw.ElapsedMilliseconds);
```

### 0.2 Minimal breadcrumbs to add now
- Entry/exit: `BEGIN/END BatchExport`
- Sheet set: `STEP CollectSheets count=<n>`
- PDF setup: `STEP PdfConfig printer="<name>" views=<n>`
- Per file: `STEP PdfExport start sheet=<name>|<id> -> "<path>"`
- After call: `STEP PdfExport done ok=<true|false>`
- Transactions: `STEP TX <name> start|commit|rollback`
- Guards/failures: `STEP SKIP reason=<text>` and `FAIL <where> ex=... msg=...`

### 0.3 Post-condition checks (cheap and local)
Right after export/print of each file:
```csharp
bool ok = File.Exists(outPath) && new FileInfo(outPath).Length > 0;
RunLog.Step($"PdfExport done ok={ok} path=\"{outPath}\"");
```

### 0.4 Placement checklist
- Batch entry method
- Before each Transaction start and after Commit/RollBack
- Before configuring PrintManager and after
- Immediately before the export/print call and immediately after
- On every early‐return and catch block

### 0.5 Rotation and retention
- One file per run by timestamp + CorrId.
- Store under `%TEMP%\GSADUs\Runs`.
- Optional: delete files older than N days on startup:
	```csharp
	foreach (var f in Directory.EnumerateFiles(dir, "*.log")
					 .Where(p => File.GetCreationTimeUtc(p) < DateTime.UtcNow.AddDays(-14)))
		File.Delete(f);
	```

### 0.6 Quick verification
- Force a probe: `Trace.WriteLine("PROBE corr=" + RunLog.CorrId);`
- Open the newest file. Confirm BEGIN, STEP, END appear in order.
- If file is empty: check that code runs on the same process and that `TextWriterTraceListener` was added once.

### 0.7 Optional extras (still trivial)
- Mirror critical lines to Revit Journal:
	```csharp
	uiapp.Application.WriteJournalComment($"[GSADUs]|{RunLog.CorrId}|{message}", true);
	```
- Include environment header once:
	```csharp
	Trace.WriteLine($"ENV revit={app.VersionNumber} user={Environment.UserName} machine={Environment.MachineName} corr={RunLog.CorrId}");
	```

### 0.8 Acceptance
- A failed run shows the last breadcrumb before stall and any FAIL … line.
- A successful run shows `PdfExport done ok=true` for each target and an END … ms line.
- Log file path is surfaced to you after the run (optional toast or console line).

## Phase 1 — Read-only Inventories via Copilot
- Use AI-Prompt read-only queries to map batch export entrypoints and PDF/Print call sites.
- Inventory entrypoints, transactions, coordinators, and all PDF/Print-related calls.
- Output: Bullet lists with file paths, method signatures, and concise code snippets.

## Phase 2 — Minimal Logging Scaffold
- Add a static logger (`JrnLog`) and a correlation scope (`CorrScope`).
- Begin usage at batch entry and around PDF export.
- Logger emits standardized lines to both Revit journal and system trace.

## Phase 3 — Breadcrumb Taxonomy and Placements
- Emit short, grep-friendly lines with Doc, Tx, and targets.
- Required placements:
	- Entry/exit of batch: `BEGIN/END BatchExport`
	- TransactionGroup: `TG START <name>`, `TG ASSIMILATE/ROLLBACK`
	- Transaction: `TX START <name>`, `TX COMMIT/ROLLBACK`
	- Key steps: `OPEN`, `CLONE_SAVEAS`, `COLLECT_SHEETS`, `PDF_CONFIG`, `PDF_EXPORT`, `PDF_EXPORT_DONE`, `ARTIFACT_CHECK`, `CLEANUP`
	- Failure/guard: `SKIP`, `FAIL`, `ROLLBACK`

## Phase 4 — PDF “No Output” Post-Condition
- After export/print, check the artifact and log verdict.
- Log PrintManager state and active ViewSet ids before printing.
- Surface silent failures (empty view sets, invalid printer).

## Phase 5 — Failure Visibility
- Add a failures preprocessor to surface API failures that do not throw.
- Log severity and description for each failure message.
- Call on transactions wrapping export/print.

## Phase 6 — Journal Query Shape
- Standardize log lines for fast grep and summarization.
- Prefix: `[GSADUs]|<corr>|...`
- Example lines:
	- `[GSADUs]|<id>|STEP PDF_CONFIG printer="..." range=... views=...`
	- `[GSADUs]|<id>|PDF_EXPORT sheet=... path="..."`
	- `[GSADUs]|<id>|ARTIFACT_CHECK type=PDF path="..." exists=... size=...`
	- `[GSADUs]|<id>|TX COMMIT ... -> Committed`

## Phase 7 — Triage Loop for “PDFs Missing, Run Says Done”
- Inspect breadcrumbs for guard paths, misconfigurations, and silent failures.
- Use artifact checks and transaction outcomes to diagnose missing PDFs.

## Phase 8 — Acceptance Signal
- Successful run yields:
	- One `BEGIN BatchExport` and matching `END OK BatchExport ...ms`.
	- For each sheet/view: `PDF_EXPORT ...` followed by `ARTIFACT_CHECK ... exists=true`.
	- No `TX ROLLBACK` without adjacent `FAIL` or `FAILURE` line.

---

This outline will be iterated and expanded as implementation progresses. Prompts and code scaffolds will be added in subsequent phases.
