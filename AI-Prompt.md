# AI Prompt Engineering Template
*For ChatGPT → VS Code Copilot Workflow*

## Context & Operating Principles

**My Development Style**: Surgical precision with fast feedback loops. I design changes systematically, apply them deterministically, and validate immediately.

**Core Rules**:
- One prompt = explicit file edits only
- Zero fallbacks or "auto-fixes" — invalid / unsupported inputs must be rejected or left unset; never normalize, coerce, silently default, substitute, or introduce helper wrappers to mimic prior behavior
- Only named files may change
- All code (and only code) lives in fenced blocks inside the EDITS section; all other sections are plain text
- Always include acceptance criteria (first item: Build succeeds unless read‑only)
- No diff/patch fragments or partial line edits; provide full definitive code for each changed scope
- **Maintain lean LOC**: Remove stale/unused code, avoid redundancies
- **No speculative abstractions**: Implement only what is required now
- **Identifier integrity**: Never substitute one identifier for another (e.g. name vs ID) unless the task explicitly demands it
- **Stupid-Simple approach**: No over-engineering, no unnecessary options

### Common Ground Safeguards (2025 Consolidation)
1. Single imperative action per prompt (Add / Remove / Refactor / Replace) — no mixed bundles.
2. READ ONLY inventory precedes any destructive removal / consolidation; never mix discovery with mutation.
3. Hard removal: REMOVE means DELETE (no stubs, hidden conditionals, fallback filters, or silent guards).
4. No fallback normalization: invalid or unsupported inputs remain invalid (reject or leave unset; no coercion or silent substitution).
5. Behavioral evidence: Acceptance includes ≥1 observable runtime / semantic verification (log pattern, return/shape, eligibility rule, explicit error path) plus build success.
6. Lean LOC: Net LOC decreases or stays flat unless required new behavior is added.
7. Micro-scope: ≤2–3 identifiers OR ≤1–2 UI elements per phase for rename/remove/refactor unless prior phase accepted.
8. Explicit forbidden list in CONSTRAINTS (non-target files, disallowed renames, no fallback logic, no scope expansion).
9. User destructive override: explicit user DELETE instruction proceeds (ask only if irreversibility ambiguous).
10. Truth source: Capabilities verified from authoritative definitions (not UI screenshots or inferred artifacts).

## Standard Prompt Template

```
<index>) <path/to/file.ext> → <concise goal>

CONSTRAINTS
- Edit ONLY this file [or: Edit ONLY these files: fileA.ext, fileB.ext, fileC.ext]
- ZERO fallbacks. ZERO "helpful" additional changes.
- [Domain-specific constraints]

TASK
- [Single, clear objective]

EDITS
- [Bulleted, specific changes]
- [Complete code in fenced blocks with file headers]

ACCEPTANCE
- Build succeeds (unless READ ONLY)
- No duplicate entities are created if an identifier already exists; repeat actions update in place
- [Specific runtime/behavior verification]
```

> ChatGPT OUTPUT RULE: When emitting a full prompt for Copilot, wrap the entire prompt (from PROMPT START to PROMPT END) inside a single outer four‑backtick fenced block labeled `markdown` so inner triple backtick code fences are preserved.

---

## Common Patterns (A–D)

### Pattern A: Single File Feature
```
1) path/to/target/file.ext → add validation helper

CONSTRAINTS
- Edit ONLY this file

TASK
- Add input validation method and integrate at entry point

EDITS
- Add validation helper method
- Call from existing entry point

ACCEPTANCE
- Invalid inputs show specific error message
```

### Pattern B: Multi-File Coordination  
```
2) Multi-file → add logging wrapper

CONSTRAINTS
- Edit ONLY these files: path/to/logger.ext, path/to/wrapper.ext, path/to/consumer.ext

TASK
- Create wrapper component and integrate at load point

EDITS
- [Complete code for each file]

ACCEPTANCE
- All log entries route through wrapper
```

### Pattern C: Read-Only Investigation
```
3) Search → find API signature

CONSTRAINTS
- READ ONLY. Do not modify files. Do NOT include code fences unless returning exact found signatures verbatim.

TASK
- Locate <ClassName> and print public method signatures

ACCEPTANCE
- Reply with exact signatures only (no extra commentary)
```

### Pattern D: Code Cleanup
```
4) path/to/service/file.ext → remove unused methods

CONSTRAINTS
- Edit ONLY this file

TASK
- Delete unused methods, consolidate duplicate logic

EDITS
- Remove: OldMethod(), LegacyHelper(), UnusedUtil()
- Consolidate duplicate validation logic

ACCEPTANCE
- All functionality preserved, LOC reduced
```

### Pattern E: Two-Phase Interface / Public Surface Change (E1 READ ONLY → E2 EDIT)
````markdown
PROMPT START
E1) READ ONLY → inventory implementers & call sites of <InterfaceOrPublicType.Member>

CONSTRAINTS
- READ ONLY. Do not modify files.

TASK
- List all implementers, adapters/proxies, and direct call sites of <Interface.Member>
- Include full file paths
- Include signature variants and overload counts
- Note guard/branch patterns and exception handling around calls

EDITS
- (None – READ ONLY)

ACCEPTANCE
- Bullet list only (no code fences)
- Each implementer path listed once
- Each call site grouped by file
PROMPT END
````
````markdown
PROMPT START
E2) EDIT → apply interface member change <describe change>

CONSTRAINTS
- Edit ONLY these files: <all implementers from E1>, <interface/base>, <adapters if any>
- May expand list ONLY if new dependency uncovered; must enumerate explicitly.

TASK
- Add/modify member <NewSignature>
- Update every implementer & proxy
- Adjust all call sites found in E1

EDITS
- Update interface/base declaration
- Provide full updated implementations (one fenced block per file)
- Adjust call sites accordingly

ACCEPTANCE
- Build succeeds
- No missing-member or override errors
- All implementers updated; no stale signature usages
PROMPT END
````

### Pattern F: One-Time Migration / Idempotent Script
````markdown
PROMPT START
F) path/to/migration/script.ext → one-time data migration

CONSTRAINTS
- Edit ONLY this file (plus optional backup helper listed if created)

TASK
- Create idempotent migration: backup → transform → verify → exit safely on re-run

EDITS
- Add backup step (skip if already exists)
- Perform transformation
- Log summary (items processed, skipped)
- Safe re-run detection (no double transforms)

ACCEPTANCE
- Build succeeds
- Backup file created (once)
- Re-running performs zero additional changes
PROMPT END
````

### Pattern G: Guideline Improvement Suggestion (READ ONLY + PROPOSE)
````markdown
PROMPT START
G) READ ONLY → propose improvement to AI Prompt Guideline

CONSTRAINTS
- READ ONLY with respect to solution code; DO NOT reference any project / framework / stack / proprietary names.
- Suggestions must remain GENERIC (use placeholders like <Feature Name>, path/to/file.ext, <Section Title>). No real file paths, extensions tied to a stack, or tech-specific APIs.

TASK
- Identify clarity gap / redundancy / missing safeguard in current guideline.
- Provide: (1) Issue summary, (2) Rationale, (3) Proposed wording (before → after), (4) Impact assessment (benefit, risk, scope).

EDITS
- (No direct edits performed) – Only a proposal.

ACCEPTANCE
- Output is bullet list sections: Issue, Rationale, Proposed Text (with BEFORE / AFTER), Impact.
- No project-specific identifiers, technologies, or solution file names.
- Uses generic placeholders only.
PROMPT END
````

### Pattern H: Read-Only Inventory (Pre-Deletion / Cleanup Mapping)
````markdown
PROMPT START
H) READ ONLY → inventory occurrences of <TokenOrIdentifier>

CONSTRAINTS
- READ ONLY. Do not modify files.

TASK
- List each occurrence with file path and concise trimmed line snippet for <TokenOrIdentifier> (no full file dumps).

EDITS
- (None – READ ONLY)

ACCEPTANCE
- Plain bullet list (no code fences)
- Each file grouped; occurrences concise
- Output ready to justify follow-up REMOVE / REFACTOR prompt
PROMPT END
````

---

## Advanced Protocols

### Interface Change Protocol
1. Phase E1 (READ ONLY inventory) → gather implementers, proxies, call sites, guards, exception handling.
2. Phase E2 (EDIT) → update interface + every implementer + all call sites.
3. If ripple exceeds manageable scope, split into additional phases (E3+, each within prompt size limits).

### Scope-Level Edit Rule
- Edit the smallest valid scope (method / constructor / class) necessary.
- Whole-file replacements ONLY when unavoidable (e.g., pervasive using additions or signature ripple).
- Acceptance adds: “No unrelated changes outside edited scopes.”

### Ripple Safety Valve
- If change touches public surface (interface, public class, base type, DI symbol, XAML name), AI MAY extend CONSTRAINTS but ONLY with files enumerated from the Phase E1 inventory.
- Must NOT add files silently; explicit list required.

### Additive vs Breaking Guidance
- Prefer additive members (with default / optional behavior) to avoid ripple.
- Use breaking changes ONLY when semantics fundamentally change (e.g., new required return data), then invoke Interface Change Protocol.

### Acceptance Extensions
- Always start with: “Build succeeds.”
- Interface edits: “No missing member/override errors. All implementers updated.”
- Behavioral changes: “New paths are logged; failure paths surface errors; no silent catches.”
- Cleanup: “No unrelated edits outside declared scopes.”
 - Add common ground: “≥1 observable runtime / semantic verification (log pattern, return shape, eligibility rule, or explicit error path) included in ACCEPTANCE.”

### Read-Only Answer Format
- Inventory / discovery phases must respond as bullet points (no code fences) unless verbatim signatures are required (then wrap signatures in backticks, not fenced blocks).
- When enumerating call sites, always show the first argument value(s) actually passed (or state “<no arguments>”).

### Minimalism Guard
- No speculative abstractions.
- No diff/patch fragments.
- Do not delete code unless Phase E1 inventory shows it is unused.

**Deletion Semantics (Common Ground)**
- REMOVE = physical deletion (not commented, hidden behind conditionals, replaced with inert stubs, or guarded silently).
- No surrogate fallback/normalization layers to simulate removed capability.
- Cite preceding READ ONLY inventory (Pattern H / E1) when performing destructive removal.

### Guideline Improvement Process
- Use Pattern G for proposing changes; never inline-edit the guideline without prior approved proposal.
- Proposals MUST: cite exact section heading, state problem succinctly, give neutral BEFORE/AFTER text using only generic placeholders.
- Forbidden in proposals: concrete tech stacks, proprietary product names, real repository paths, language-specific code (unless illustrating fence structure generically as ```language with placeholders).
- Accepted proposals can then be turned into a follow-up EDIT prompt explicitly listing the guideline file under CONSTRAINTS.

### Logging & Telemetry (Generic Policy)
- On success: write <Workflow>_ExportDate and <Workflow>_ExportSig only after confirming artifact exists.
- On failure: log error; DO NOT write success markers.
- Acceptance: “No success fields on failure; error log entry present.”

### Prompt Size & Chunking
- Large changes must be split into phases.
- Each phase ≤ <MAX_FILES_PER_PHASE> files AND ≤ <MAX_CODE_BLOCKS_PER_PHASE> inner code fences.
- Never merge distinct phases into one prompt.

---

## ChatGPT Prompt Guidelines

**Structure**: `<index>) <path/to/file.ext> → <goal>`

**Mandatory Sections**: CONSTRAINTS | TASK | EDITS | ACCEPTANCE  

**Key Rules**:
- COMPLETE file paths (never truncate with "...")
- ZERO fallbacks or alternative approaches  
- Multi-file prompts when dependencies require it
- Code ONLY in EDITS fenced blocks; all other sections plain text
- Lean code: remove unused, avoid redundancies, keep simple
- No speculative abstractions
- No diff/patch fragments or partial line edits

### Single Outer Fence Rule
- Entire prompt MUST be inside one outer fence using four backticks: ````markdown (open) … ```` (close)
- PROMPT START / PROMPT END live inside that outer fence
- Inner triple backtick code fences appear ONLY inside EDITS (one per file)
- Do NOT nest additional outer fences or split the prompt into multiple fenced blocks
- Nothing the model needs Copilot to see may live outside the outer fence
- Multi-phase workflows: each phase = its own outer fence
- Read-only prompts: still use outer fence; no inner code fences unless returning verbatim signatures
- Self-check: exactly 1 opening ````markdown and 1 closing ````; no stray extra four‑backtick fences

**Code Block Rules**:
- Code goes ONLY in the EDITS section
- Always use proper fences: ```language
- Always include file header: `// FILE: path/to/file.ext`
- NO code snippets in CONSTRAINTS, TASK, or ACCEPTANCE sections
- NO inline code in prompt narrative – describe in plain text instead

**Quality Checklist**:
- [ ] Exact file paths, no truncation
- [ ] Single clear objective
- [ ] Code ONLY in EDITS section with proper fences
- [ ] Plain text in CONSTRAINTS, TASK, ACCEPTANCE (no code snippets)
- [ ] ZERO fallbacks offered
- [ ] Specific, testable acceptance criteria
- [ ] Remove dead code, consolidate duplicates
- [ ] No speculative abstractions added
- [ ] No diff/patch fragments; full code blocks only
- [ ] Exactly one outer four‑backtick fenced block
- [ ] PROMPT START / PROMPT END inside outer fence
- [ ] No orphan inner code fences; each inside EDITS
- [ ] For read-only prompt: bullet list output, no code fences (unless verbatim signatures)
- [ ] Interface change: Phase E1 inventory performed before E2 edit
- [ ] If deletions: supported by Phase E1 evidence
- [ ] Prompt size within limits (<MAX_FILES_PER_PHASE>, <MAX_CODE_BLOCKS_PER_PHASE>)
- [ ] If Pattern G: proposal uses ONLY generic placeholders; no project/stack specifics
 - [ ] No fallback / normalization introduced (invalid inputs rejected or left unset)
 - [ ] Hard removal semantics applied (no stubs / hidden guards) where REMOVE requested
 - [ ] READ ONLY inventory performed prior to destructive removal / consolidation
 - [ ] Acceptance includes ≥1 observable runtime / semantic verification (beyond build)
 - [ ] Net LOC reduced or flat unless new required functionality added

---

## Operational Additions (2025-09 Update)

The following refinements extend (do not replace) existing rules. If any ambiguity arises, the stricter/more specific rule prevails.

**Scope**
- One change per prompt; one file per edit phase (unless a formally declared multi-file protocol like Interface Change Protocol is in effect).

**Paths & Targets**
- Always name exact file paths and method/function names to be edited.
- Do not reference broad folders or patterns as edit targets.

**Constraints Enhancements**
- Explicitly state what must NOT be touched (non-target files, identifiers, layers).
- Preserve existing identifiers; no renames unless explicitly requested.
- Zero placeholder identifiers in emitted code (no Foo, Bar, Tmp unless they already exist as real entities).

**Task Statement**
- Exactly one imperative action (e.g., "Add X", "Replace Y", "Remove Z", "Refactor A into B").

**EDITS Section Specificity**
- Show only the final code for each changed scope (method/class/file) with header: `// FILE: <exact/path>`.
- Omit unrelated unchanged code unless whole-file replacement is truly required (and justified by prior protocol phase).

**ACCEPTANCE Additions**
- Must include: "Build (or compile) succeeds" plus 1–2 concrete observable runtime checks that prove success (e.g., a method now returns filtered results; an error path raises a specific exception message).
- Define what observable evidence confirms success (e.g., log line pattern, return value shape, absence of prior bug condition).

**Truth Source Discipline**
- Read from the authoritative system of record, not transient logs, caches, or in-memory mirrors.
- After any write, re-read from the system of record before logging success or emitting markers.

**Layer Separation**
- UI/presentation layer never mutates state directly.
- All writes flow through a single designated writer/service boundary.
- No nested transactions at the UI level; deeper layers manage transactional scope in isolation.

**Blocking Rule**
- Do not schedule background work while a blocking (modal) dialog is open; either defer until closure or use a non-blocking surface/context.

**Deterministic Apply Flow**
- Sequence: compute → diff → apply.
- Only apply the minimal diff required; writer logic must be idempotent (safe on re-run with no duplicate side-effects).

**Instrumentation Hygiene**
- Temporary probes (timers, counters, verbose logs) may be added only adjacent to entry or commit points.
- All temporary probes must be removed in a follow-up cleanup prompt (or the same prompt if acceptance explicitly requires post-verification removal).

**Guardrails / Forbidden API Zones**
- Add defensive checks or CI guard scripts that fail if restricted APIs (e.g., transaction APIs, direct file IO, threading primitives) appear in forbidden folders or layers.
- Acceptance for such prompts should include: "Fails build/validation if restricted API is introduced in guarded scope."

**Rollback / Safety**
- Before multi-file or stateful edits, establish a checkpoint (e.g., list of targeted files + their hashes) in the prompt narrative when feasible.
- Prefer decomposing into smaller sequential prompts to minimize rework risk on rejection.

**Rule Interaction Notes**
- These additions complement existing Minimalism, Interface Change Protocol, and Read-Only patterns.
- If a required verification conflicts with keeping edits minimal, state the trade-off explicitly in CONSTRAINTS.

**Quality Checklist Additions**
- [ ] Single imperative TASK verb
- [ ] Identifiers preserved (no silent renames)
- [ ] Post-write re-read performed before logging success (when writes occur)
- [ ] Diff-minimal edit (no unrelated scope changes)
- [ ] Idempotent behavior (safe to re-run without duplication)
- [ ] No forbidden APIs in guarded layers