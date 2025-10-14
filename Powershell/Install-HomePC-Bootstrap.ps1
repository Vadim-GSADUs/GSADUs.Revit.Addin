# 1) Write the bootstrap script
$BootPath = "C:\GSADUs\Dev\Powershell\Install-HomePC-Bootstrap.ps1"
New-Item -ItemType Directory -Force -Path (Split-Path $BootPath) | Out-Null

@'
# Home PC bootstrap for GSADUs.Revit.Addin
# Run in an elevated PowerShell (Run as Administrator).

$ErrorActionPreference = "Stop"

# --- Admin check ---
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
  ).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
  throw "Run this script in PowerShell as Administrator."
}

# --- Inputs ---
$UserName  = "Vadim-GSADUs"
$UserEmail = "Vadim@gsadus.com"
$RepoSsh   = "git@github.com:Vadim-GSADUs/GSADUs.Revit.Addin.git"
$RepoRoot  = "C:\GSADUs\Dev"

# --- Git global config (idempotent) ---
git --version | Out-Null
git config --global user.name  "$UserName"
git config --global user.email "$UserEmail"
git config --global init.defaultbranch main
git config --global pull.rebase true
git config --global rebase.autostash true
git config --global fetch.prune true

# --- SSH key (id_ed25519) ---
$SshDir  = Join-Path $env:USERPROFILE ".ssh"
$KeyPriv = Join-Path $SshDir "id_ed25519"
$KeyPub  = "$KeyPriv.pub"
New-Item -ItemType Directory -Force -Path $SshDir | Out-Null
if (-not (Test-Path $KeyPriv)) {
  ssh-keygen -t ed25519 -C $UserEmail -f $KeyPriv -N "" | Out-Null
  Write-Host "`nAdd this SSH public key in GitHub Settings > SSH and GPG keys:" -ForegroundColor Cyan
  Get-Content $KeyPub
  Write-Host "`nAfter adding, run:  ssh -T git@github.com" -ForegroundColor Yellow
}

# Try SSH auth (will print success or publickey error)
try { ssh -T git@github.com } catch { Write-Host "SSH test executed." -ForegroundColor Yellow }

# --- Repo attach/clone ---
New-Item -ItemType Directory -Force -Path $RepoRoot | Out-Null
$GitDir = Join-Path $RepoRoot ".git"
if (-not (Test-Path $GitDir)) {
  # If empty, clone. If not empty, init and attach remote.
  $nonGit = Get-ChildItem $RepoRoot -Force | Where-Object { $_.Name -ne ".git" }
  if ($nonGit.Count -eq 0) {
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

# --- Optional: post-commit auto-push hook (local only) ---
$HookBat = Join-Path $RepoRoot ".git\hooks\post-commit.bat"
@'
@echo off
git rev-parse --is-inside-work-tree >NUL 2>&1 || goto :eof
for /f "usebackq delims=" %%A in (`git rev-parse --symbolic-full-name --abbrev-ref "@{u}" 2^>NUL`) do set "UP=%%A"
if not defined UP ( git push -u origin HEAD ) else ( git push )
'@ | Set-Content -Path $HookBat -Encoding ASCII

# --- AutoPull scheduled tasks (On logon and every 2 hours) ---
$ToolsDir = Join-Path $RepoRoot "Tools"
New-Item -ItemType Directory -Force -Path $ToolsDir | Out-Null
$AutoPull = Join-Path $ToolsDir "AutoPull.ps1"
@"
# Auto-pull for GSADUs repo
`$repo = "$RepoRoot"
git -C `$repo fetch --all
git -C `$repo pull --rebase --autostash
"@ | Set-Content -Path $AutoPull -Encoding UTF8

Import-Module ScheduledTasks
$Action    = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$AutoPull`""
$Trigger1  = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$Trigger2  = New-ScheduledTaskTrigger -Once -At ((Get-Date).AddMinutes(1)) -RepetitionInterval (New-TimeSpan -Hours 2) -RepetitionDuration ([TimeSpan]::MaxValue)
$Principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest

Register-ScheduledTask -TaskName "GSADUs_AutoPull_OnLogon" -Action $Action -Trigger $Trigger1 -Principal $Principal -Force | Out-Null
Register-ScheduledTask -TaskName "GSADUs_AutoPull_2h"     -Action $Action -Trigger $Trigger2 -Principal $Principal -Force | Out-Null
Start-ScheduledTask    -TaskName "GSADUs_AutoPull_2h"

# --- Final info ---
git -C $RepoRoot status -sb
Get-ScheduledTask -TaskName GSADUs_AutoPull_OnLogon,GSADUs_AutoPull_2h |
  Get-ScheduledTaskInfo | Select-Object TaskName,LastRunTime,LastTaskResult
Write-Host "`nBootstrap complete." -ForegroundColor Green
'@ | Set-Content -Path $BootPath -Encoding UTF8

# 2) Run it
powershell -ExecutionPolicy Bypass -File $BootPath
