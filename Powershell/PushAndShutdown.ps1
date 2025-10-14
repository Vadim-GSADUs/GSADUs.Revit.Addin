param(
  [string]$RepoPath = "C:\GSADUs\Dev",
  [string]$Branch   = "main",
  [string]$Message  = $(Get-Date -Format "yyyy-MM-dd HH:mm:ss 'auto-commit'")
)

# Fail fast
$ErrorActionPreference = "Stop"

# Go to repo
Set-Location $RepoPath
if (-not (Test-Path ".git")) { throw "Not a git repo: $RepoPath" }

# Ensure branch exists and is checked out
git rev-parse --verify $Branch *> $null || throw "Branch not found: $Branch"
git checkout $Branch | Out-Null

# Stage all changes (mods, deletes, new files)
git add -A

# Commit if anything is staged
if (-not (git diff --cached --quiet)) {
  git commit -m "$Message"
} else {
  Write-Host "Nothing to commit."
}

# Push to upstream
git push -u origin $Branch
shutdown /s /t 900 /c "Shutdown in 15 minutes. Use 'shutdown /a' to abort."
