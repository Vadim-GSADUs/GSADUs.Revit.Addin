# Save the Home-PC bootstrap script into the repo and document it in MigrationPlan.md, then commit and push.
$Repo = "C:\GSADUs\Dev"
$BootPath = Join-Path $Repo "Powershell\Install-HomePC-Bootstrap.ps1"
New-Item -ItemType Directory -Path (Split-Path $BootPath) -Force | Out-Null

@'
# Save as: C:\GSADUs\Dev\Install-HomePC-Bootstrap.ps1 (copied here for versioning)
# Purpose: Configure Git + SSH, clone/attach repo, auto-push hook, auto-pull tasks.

$ErrorActionPreference = "Stop"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
  ).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
  throw "Run this script in PowerShell as Administrator."
}

$UserName   = "Vadim-GSADUs"
$UserEmail  = "Vadim@gsadus.com"
$RepoSsh    = "git@github.com:Vadim-GSADUs/GSADUs.Revit.Addin.git"
$RepoRoot   = "C:\GSADUs\Dev"
$SshDir     = Join-Path $env:USERPROFILE ".ssh"
$KeyPriv    = Join-Path $SshDir "id_ed25519"
$KeyPub     = "$KeyPriv.pub"

git --version | Out-Null
git config --global user.name  "$UserName"
git config --global user.email "$UserEmail"
git config --global init.defaultbranch main
git config --global pull.rebase true
git config --global rebase.autostash true
git config --global fetch.prune true

New-Item -ItemType Directory -Path $SshDir -Force | Out-Null
if (-not (Test-Path $KeyPriv)) {
  ssh-keygen -t ed25519 -C $UserEmail -f $KeyPriv -N "" | Out-Null
  Write-Host "`nPublic key for GitHub (add in Settings → SSH and GPG keys):" -ForegroundColor Cyan
  Get-Content $KeyPub
}

try { ssh -T git@github.com } catch { Write-Host "SSH test ran. Add key if needed, then rerun ssh -T." -ForegroundColor Yellow }

New-Item -ItemType Directory -Path $RepoRoot -Force | Out-Null
$gitDir = Join-Path $RepoRoot ".git"
if (-not (Test-Path $gitDir)) {
  if ((Get-ChildItem $RepoRoot -Force | Where-Object { $_.Name -ne ".git" }).Count -eq 0) {
    git clone $RepoSsh $RepoRoot
  } else {
    git -C $RepoRoot init
    git -C $RepoRoot remote remove origin 2>$null
    git -C $RepoRoot remote add origin $RepoSsh
    git -C $RepoRoot fetch origin
    git -C $RepoRoot checkout -B main origin/main
  }
} else {
  git -C $RepoRoot remote set-url origin $RepoSsh
  git -C $RepoRoot fetch origin
  git -C $RepoRoot pull --rebase --autostash
}

$HookBat = Join-Path $RepoRoot ".git\hooks\post-commit.bat"
@'
@echo off
git rev-parse --is-inside-work-tree >NUL 2>&1 || goto :eof
for /f "usebackq delims=" %%A in (`git rev-parse --symbolic-full-name --abbrev-ref "@{u}" 2^>NUL`) do set "UP=%%A"
if not defined UP ( git push -u origin HEAD ) else ( git push )
'@ | Set-Content -Path $HookBat -Encoding ASCII

$ToolsDir = Join-Path $RepoRoot "Tools"
New-Item -ItemType Directory -Path $ToolsDir -Force | Out-Null
$AutoPull = Join-Path $ToolsDir "AutoPull.ps1"
@"
# Auto-pull for GSADUs repo
`$repo = "$RepoRoot"
git -C `$repo fetch --all
git -C `$repo pull --rebase --autostash
"@ | Set-Content -Path $AutoPull -Encoding ASCII

Import-Module ScheduledTasks
$Action   = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$AutoPull`""
$Trigger1 = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$Trigger2 = New-ScheduledTaskTrigger -Once -At ((Get-Date).AddMinutes(1)) -RepetitionInterval (New-TimeSpan -Hours 2) -RepetitionDuration ([TimeSpan]::MaxValue)
$Principal= New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest

Register-ScheduledTask -TaskName "GSADUs_AutoPull_OnLogon" -Action $Action -Trigger $Trigger1 -Principal $Principal -Force | Out-Null
Register-ScheduledTask -TaskName "GSADUs_AutoPull_2h"     -Action $Action -Trigger $Trigger2 -Principal $Principal -Force | Out-Null
Start-ScheduledTask    -TaskName "GSADUs_AutoPull_2h"

git -C $RepoRoot status -sb
Get-ScheduledTask -TaskName GSADUs_AutoPull_OnLogon,GSADUs_AutoPull_2h |
  Get-ScheduledTaskInfo | Select-Object TaskName,LastRunTime,LastTaskResult
Write-Host "`nHome PC bootstrap complete." -ForegroundColor Green
'@ | Set-Content -Path $BootPath -Encoding ASCII

# Append a run-book section to MigrationPlan.md
$Plan = Join-Path $Repo "MigrationPlan.md"
@"
## Home PC bootstrap (run later)
**File:** Powershell\\Install-HomePC-Bootstrap.ps1  
**Run as:** Administrator PowerShell

Steps:
1) On Home PC, open elevated PowerShell.
2) Run: `powershell -ExecutionPolicy Bypass -File "C:\GSADUs\Dev\Powershell\Install-HomePC-Bootstrap.ps1"`
3) If SSH says permission denied, add the printed public key to GitHub → Settings → SSH and GPG keys, then: `ssh -T git@github.com`.
4) Verify scheduled tasks: GSADUs_AutoPull_OnLogon, GSADUs_AutoPull_2h show LastTaskResult = 0.
"@ | Add-Content -Path $Plan -Encoding UTF8

# Commit and push
git -C $Repo add Powershell/Install-HomePC-Bootstrap.ps1 MigrationPlan.md
git -C $Repo commit -m "Add Home PC bootstrap script and run-book section"
git -C $Repo push
