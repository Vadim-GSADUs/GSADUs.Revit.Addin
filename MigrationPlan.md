# GSADUs Revit Add-in Migration Plan

Scope: Migrate the working codebase from Google Drive to a local drive, set up GitHub for multi-user workflows, and preserve the current build/deploy behavior for Revit. All paths and facts below come from the current workspace.

---

## 0) Current State (as discovered)

- Current workspace root
  - G:\Shared drives\GSADUs Projects\Our Models\0 - CATALOG\Config\GSADUs Tools\
- Projects
  - G:\Shared drives\GSADUs Projects\Our Models\0 - CATALOG\Config\GSADUs Tools\src\GSADUs.Revit.Addin\GSADUs.Revit.Addin.csproj
- .NET targets
  - net8.0-windows
- Build outputs (from `src\GSADUs.Revit.Addin\GSADUs.Revit.Addin.csproj`)
  - Debug|x64 → `bin\x64\Debug\`
  - Release|x64 → `bin\x64\Release\`
  - `AppendTargetFrameworkToOutputPath=false`, `AppendRuntimeIdentifierToOutputPath=false`
- Automatic post-build deploy (MSBuild Target `DeployToRevitFolder`)
  - `DeployDir`: `C:\GSADUs\RevitAddin\`
  - Copies: DLL, PDB (if present), and any `*.json` from the build output directory
- External references
  - NuGet: `Microsoft.Extensions.DependencyInjection` (8.0.0)
  - Direct DLLs: `$(ProgramFiles)\Autodesk\Revit 2026\RevitAPI.dll`, `$(ProgramFiles)\Autodesk\Revit 2026\RevitAPIUI.dll`
- Deployment/installer PowerShell script
  - Path (relative to repo root): `..\Powershell\Install-GSADUsAddin.ps1`
  - Full path (from current layout): G:\Shared drives\GSADUs Projects\Our Models\0 - CATALOG\Config\Powershell\Install-GSADUsAddin.ps1
  - Script uses `$ProjectRoot = "G:\\Shared drives\\GSADUs Projects\\Our Models\\0 - CATALOG\\Config\\GSADUs Tools\\src\\GSADUs.Revit.Addin"`
  - Script writes `.addin` to: `$env:ProgramData\Autodesk\Revit\Addins\2026\GSADUs.BatchExport.addin`
  - Script deploys DLL to: `C:\GSADUs\RevitAddin\` (same as MSBuild target)
- Environment variables referenced
  - `GSADUS_LOG_DIR` (optional, used by `src\GSADUs.Revit.Addin\Logging\RunLog.cs`)
- Solution file
  - No `.sln` found in the workspace

- New destination root (provided): `C:\GSADUs\Dev`
- GitHub account (provided): https://github.com/Vadim-GSADUs

---

## 1) Migration Goals

- Move the codebase from
  - G:\Shared drives\GSADUs Projects\Our Models\0 - CATALOG\Config\GSADUs Tools\
  - to
  - C:\GSADUs\Dev\
- Preserve build outputs and automatic deploy to `C:\GSADUs\RevitAddin\`.
- Ensure Revit loads the add-in via `.addin` pointing at the deployed DLL under `C:\GSADUs\RevitAddin\`.
- Initialize a new Git repository and push to a new GitHub repository under `https://github.com/Vadim-GSADUs`.
- Enable multiple machines to work locally using the same local path `C:\GSADUs\Dev\` to avoid path drift.

---

## 1.1) Validated Findings (from diagnostics on GSADUS-VADIM, 2025-10-13)

- Hardware/OS: Windows 11 Home build 26100 on GSADUS-VADIM (AMD Ryzen 7 9700X, 8 cores/16 threads, 16 GB RAM).
- PowerShell 7.5.3; Execution Policy is RemoteSigned-equivalent.
- Visual Studio Community 2022 v17.14 at `C:\Program Files\Microsoft Visual Studio\2022\Community`.
- .NET SDK and runtime available (`dotnet` CLI present).
- Git and Git LFS installed, but global identity (`user.name`, `user.email`) not set; no SSH keys detected (should be generated for GitHub SSH).
- Revit add-in registration file: `C:\ProgramData\Autodesk\Revit\Addins\2026\GSADUs.BatchExport.addin`.
- Deployed DLL: `C:\GSADUs\RevitAddin\GSADUs.Revit.Addin.dll` (333 KB, SHA-256: 34B18E8F7FD6C9BB45D22989F50EFB5F4A1D733ABAFC5447DB07BBE6456E5C49).
- No other Revit versions detected; environment targets Revit 2026.
- `C:\GSADUs\Dev` exists but has no `.sln` or `.csproj` yet; only the deployed DLL was detected.
- No JSON config files present in `bin\`.
- Code relocation from Drive to `C:\GSADUs\Dev\` is still pending.
- Main workspace: `G:\...GSADUs Tools` (5,772 files, ~241 MB).
- Sub-folder `Powershell` contains 16 scripts (~68 KB), including `Install-GSADUsAddin.ps1`.
- Largest files are Copilot session artifacts under `.vs`; these should be excluded from Git.

---

## 2) Pre-Migration Checks (Pending Configuration)

Collect and confirm these settings before executing steps 3–9.

- Revit version: **Confirmed** Revit 2026 in use (no other versions detected).
- Visual Studio: **Confirmed** Community 2022 v17.14.
- PowerShell: **Confirmed** 7.5.3, RemoteSigned-equivalent.
- .NET CLI: **Confirmed** present.
- Git: **Confirmed** installed, but global identity and SSH keys not set.
- Deploy folder: **Confirmed** as current target (`C:\GSADUs\RevitAddin\`).
- Addin registration: **Confirmed** for Revit 2026.
- TODOs:
  - Set Git global identity (`user.name`, `user.email`) and generate SSH key on each PC.
  - Decide SSH vs HTTPS, repo name, visibility.
  - Rerun diagnostics on each additional PC to capture parity.
  - Exclude Copilot `.vs` artifacts from Git.
  - Relocate codebase from Drive to `C:\GSADUs\Dev\` and create `.sln`/`.csproj`.

---

## 3) Code Relocation

1) Create destination directory
   - `C:\GSADUs\Dev\`

2) Copy the codebase from the current location (preserving structure)
   - Source: `G:\Shared drives\GSADUs Projects\Our Models\0 - CATALOG\Config\GSADUs Tools\`
   - Destination: `C:\GSADUs\Dev\`
   - Example (Windows):
     - `robocopy "G:\Shared drives\GSADUs Projects\Our Models\0 - CATALOG\Config\GSADUs Tools" "C:\GSADUs\Dev" /E`

3) Include the PowerShell installer script in the new repo layout
   - Current script path (outside the repo folder): `G:\Shared drives\GSADUs Projects\Our Models\0 - CATALOG\Config\Powershell\Install-GSADUsAddin.ps1`
   - Copy to: `C:\GSADUs\Dev\Powershell\Install-GSADUsAddin.ps1`
   - This installer script becomes tracked in Git to avoid drift (add and commit in Section 5.1).

---

## 4) Path Updates

A) Update PowerShell installer script at `C:\GSADUs\Dev\Powershell\Install-GSADUsAddin.ps1`
- Change `$ProjectRoot` from
  - `G:\Shared drives\GSADUs Projects\Our Models\0 - CATALOG\Config\GSADUs Tools\src\GSADUs.Revit.Addin`
  - to
  - `C:\GSADUs\Dev\src\GSADUs.Revit.Addin`
- Verify/adjust `$RevitYear` per machine (current default: `2026`).

B) Validate MSBuild deploy dir in `C:\GSADUs\Dev\src\GSADUs.Revit.Addin\GSADUs.Revit.Addin.csproj`
- Current `DeployDir`: `C:\GSADUs\RevitAddin\`
- Keep as-is if this remains the deployment location. Otherwise, update the property and ensure `.addin` points to the same path.

C) Revit API references
- Current HintPaths:
  - `$(ProgramFiles)\Autodesk\Revit 2026\RevitAPI.dll`
  - `$(ProgramFiles)\Autodesk\Revit 2026\RevitAPIUI.dll`
- Confirm these paths exist on each machine or adjust for the installed version(s).

---

## 5) Initialize Git Locally (C:\GSADUs\Dev)

> Status: directory exists but contains no `.sln` or `.csproj`. First action is to copy the full codebase from
> `G:\Shared drives\GSADUs Projects\Our Models\0 - CATALOG\Config\GSADUs Tools\` to `C:\GSADUs\Dev\`, then create or add the solution.

### 5.1) Local init (C:\GSADUs\Dev)
- Initialize a Git repository at `C:\GSADUs\Dev\`
- Create `.gitignore` suitable for .NET/WPF (e.g., `bin/`, `obj/`, `.vs/`, `*.user`, `*.suo`, `*.cache`, `*.log`)
- Optionally create a `.sln` that includes `src\GSADUs.Revit.Addin\GSADUs.Revit.Addin.csproj`
- Add all files including `Powershell\Install-GSADUsAddin.ps1` and commit initial state

### 5.2) Remote creation (GitHub)
- Create a new repository under `https://github.com/Vadim-GSADUs` (via web or API)
- Decide repository name and visibility (public/private)
- Choose protocol (SSH or HTTPS), add remote URL
- Push `main` branch to the remote

---

## 6) Multi-User Setup

- Standardize local working path per machine: `C:\GSADUs\Dev\`
- Each user clones/pulls the same repository into this path.
- Build behavior remains consistent across machines due to `DeployDir` and `.addin` installation pointing to `C:\GSADUs\RevitAddin\`.

---

## 7) Build and Deploy Verification

1) Ensure `C:\GSADUs\RevitAddin\` exists (MSBuild target will `MakeDir` if missing).

2) Build the project from `C:\GSADUs\Dev\src\GSADUs.Revit.Addin\GSADUs.Revit.Addin.csproj` (Debug or Release).

3) Verify post-build deploy copied files into `C:\GSADUs\RevitAddin\`:
- `GSADUs.Revit.Addin.dll`
- `GSADUs.Revit.Addin.pdb` (if present)
- any `*.json` produced in the build output

4) Install `.addin` if using the installer script (creates/updates under ProgramData):
- Run: `C:\GSADUs\Dev\Powershell\Install-GSADUsAddin.ps1` (adjust `$RevitYear` if needed)
- Confirm `.addin` at: `%ProgramData%\Autodesk\Revit\Addins\2026\GSADUs.BatchExport.addin`
- The `.addin` points to assembly path under `C:\GSADUs\RevitAddin\GSADUs.Revit.Addin.dll`

5) Start Revit and validate the add-in loads and the Batch Export command appears.

---

## 8) Environment Variables

- Optional environment variable for logs: `GSADUS_LOG_DIR`
  - If not set, logs default to:
    - `%USERPROFILE%\Documents\GSADUs\Runs` (preferred) or
    - `%LOCALAPPDATA%\GSADUs\Runs`
  - Decide whether to standardize a log directory per machine and set `GSADUS_LOG_DIR` accordingly.

---

## 9) Validation Checklist

- [x] Diagnostics captured on GSADUS-VADIM; artifacts listed in section 15.
- [ ] Repo present at `C:\GSADUs\Dev\` with `.gitignore` and first commit
- [ ] Remote created on GitHub and initial push completed
- [ ] `C:\GSADUs\Dev\Powershell\Install-GSADUsAddin.ps1` updated `$ProjectRoot`
- [ ] Build success for `C:\GSADUs\Dev\src\GSADUs.Revit.Addin\GSADUs.Revit.Addin.csproj`
- [ ] Files deployed to `C:\GSADUs\RevitAddin\`
- [ ] `.addin` exists at `%ProgramData%\Autodesk\Revit\Addins\2026\GSADUs.BatchExport.addin`
- [ ] Revit loads add-in and Batch Export command runs
- [ ] Multi-user machines cloned to `C:\GSADUs\Dev\` and can build/deploy

---

## 10) Optional Integrations

### 10.1) Google Drive Sync (optional)
- List Google Drive folders that must remain in use (full paths)
- List artifacts to sync to Drive after builds (extensions and target paths)
- Define any automated copy/sync tasks needed post-build

### 10.2) Automation / CI (optional)
- Decide on Git hooks (e.g., auto-push) and/or Task Scheduler (e.g., auto-pull interval)
- Choose CI (e.g., GitHub Actions) and define build workflow scope if needed

---

## 11) Constraints and Backups

- Corporate policies restricting Git hooks, scheduled tasks, or SSH keys
- Backup requirements and retention rules
- Any path constraints beyond `C:\GSADUs\Dev\` (repo root) and `C:\GSADUs\RevitAddin\` (deploy)

---

## 12) Implementation Scripts

- Finalized scripts and hooks will be stored in-repo after confirmation:
  - `C:\GSADUs\Dev\Powershell\Install-GSADUsAddin.ps1` (installer/deployer)
  - Git hooks (e.g., `.git\hooks\post-commit` if adopted)
  - Any Task Scheduler XML exports (if adopted)
  - CI workflow file(s) (if adopted)

---

## 13) Open Questions

- Repository name, visibility, and protocol (SSH/HTTPS)
- Revit year(s) to target and whether multi-version support is required
- Whether to create a `.sln` and naming convention
- Machines and usernames for per-machine configs (if any)
- Git LFS usage and exact extensions (if any)
- Google Drive sync details and target locations (if adopting 10.1)
- CI requirements and scope (if adopting 10.2)
- Corporate restrictions and backup policy specifics

---

## 15) Diagnostics Artifacts (GSADUS-VADIM, 2025-10-13)

Saved under `C:\GSADUs\Diagnostics\`:
- `SystemBaseline_20251013_141706.json`
- `RevitEnvironment_20251013_141714.json`
- `DevRepoState_20251013_141714.json`
- `DriveUsage_20251013_141714.json`

(See <attachments> above for file contents. You may not need to search or read the file again.)
