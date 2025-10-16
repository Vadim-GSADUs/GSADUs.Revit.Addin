<#
.SYNOPSIS
  Extracts the most recent Copilot chat snapshot from Visual Studio and exports
  a readable log plus a raw copy.

.NOTES
  Default paths match your layout.
  Heuristics handle JSON, NDJSON, TXT. If schema unknown, falls back to raw export.
#>

[CmdletBinding()]
param(
  [string]$SolutionRoot = 'C:\GSADUs\Dev\GSADUs.Revit.BatchExport.sln',
  [string]$SnapshotsDir = 'C:\GSADUs\Dev\.vs\CopilotSnapshots',
  [string]$OutputDir    = 'C:\GSADUs\Dev\logs',
  [ValidateSet('txt','md','json')]
  [string]$OutputFormat = 'md',
  [switch]$IncludeSystemMessages,      # include tool/system messages if present
  [switch]$VerboseParsing,             # echo parsing decisions
  [switch]$OpenAfter,                  # open exported file after completion
  [switch]$DiscoverOnly                # list found candidates and exit
)

function Write-Info($msg){ Write-Host "[info] $msg" -ForegroundColor Cyan }
function Write-Warn($msg){ Write-Warning $msg }

# Helper: tolerant DateTime parsing
function Convert-ToDateTime([object]$ts){
  if ($ts -is [DateTime]) { return $ts }
  if ($null -eq $ts) { return (Get-Date) }
  $s = [string]$ts
  if ([string]::IsNullOrWhiteSpace($s)) { return (Get-Date) }
  $out = [DateTime]::MinValue
  if ([DateTime]::TryParse($s, [ref]$out)) { return $out }
  $styles = [System.Globalization.DateTimeStyles]::AssumeUniversal -bor [System.Globalization.DateTimeStyles]::AdjustToUniversal
  $formats = @('O','yyyy-MM-ddTHH:mm:ss.fffZ','yyyy-MM-ddTHH:mm:ssZ','yyyy-MM-dd HH:mm:ss')
  foreach ($fmt in $formats) {
    $tmp = [DateTime]::MinValue
    if ([DateTime]::TryParseExact($s, $fmt, $null, $styles, [ref]$tmp)) { return $tmp }
  }
  return (Get-Date)
}

# 1) Resolve/Guardrails
# Try to auto-detect snapshots dir under the solution's .vs tree if the provided path doesn't exist
if (-not (Test-Path $SnapshotsDir)) {
  if ($SolutionRoot -and (Test-Path $SolutionRoot) -and ($SolutionRoot -like '*.sln')) {
    $solutionDir = Split-Path -Parent $SolutionRoot
    $vsDir = Join-Path $solutionDir '.vs'
    if (Test-Path $vsDir) {
      $exts = @('.json','.ndjson','.log','.txt')
      $candidate = Get-ChildItem -Path $vsDir -Recurse -File -ErrorAction SilentlyContinue |
                   Where-Object { $_.Extension -in $exts -and $_.FullName -match 'Copilot|Chat' } |
                   Sort-Object LastWriteTime -Descending |
                   Select-Object -First 1
      if ($candidate) {
        $SnapshotsDir = $candidate.DirectoryName
        if ($VerboseParsing) { Write-Info "Auto-detected SnapshotsDir: $SnapshotsDir" }
      }
    }
  }
}

if (-not (Test-Path $SnapshotsDir)) { throw "SnapshotsDir not found: $SnapshotsDir" }
if (-not (Test-Path (Split-Path $OutputDir -Parent))) {
  throw "Parent of OutputDir not found: $(Split-Path $OutputDir -Parent)"
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# 2) Pick the most recent snapshot-like file
$patterns = @('.json','.ndjson','.log','.txt','.sqlite','.db')
$files = Get-ChildItem -Path $SnapshotsDir -Recurse -File -ErrorAction SilentlyContinue |
         Where-Object { $_.Extension -in $patterns } |
         Sort-Object LastWriteTime -Descending

if (-not $files -or $files.Count -eq 0) {
  $attempted = @($SnapshotsDir)
  if ($VerboseParsing) { Write-Info "No files in provided dir. Trying common locations..." }

  $candidates = New-Object System.Collections.Generic.List[string]
  # Solution .vs folder
  if ($SolutionRoot -and (Test-Path $SolutionRoot) -and ($SolutionRoot -like '*.sln')) {
    $solutionDir = Split-Path -Parent $SolutionRoot
    $candidates.Add((Join-Path $solutionDir '.vs')) | Out-Null
  }
  # VS Code stable
  if ($env:APPDATA) {
    $candidates.Add((Join-Path $env:APPDATA 'Code\User\globalStorage\github.copilot-chat\conversations')) | Out-Null
    # Older Copilot Chat storage path fallback
    $candidates.Add((Join-Path $env:APPDATA 'Code\User\globalStorage\github.copilot-chat')) | Out-Null
    # VS Code Insiders
    $candidates.Add((Join-Path $env:APPDATA 'Code - Insiders\User\globalStorage\github.copilot-chat\conversations')) | Out-Null
  }
  # Visual Studio ServiceHub logs (heuristic)
  if ($env:LOCALAPPDATA) {
    $candidates.Add((Join-Path $env:LOCALAPPDATA 'Microsoft\VisualStudio')) | Out-Null
    $candidates.Add((Join-Path $env:LOCALAPPDATA 'Microsoft\VisualStudio\**\ServiceHub\Logs')) | Out-Null
    $candidates.Add((Join-Path $env:LOCALAPPDATA 'Microsoft\VSCommon\**\ServiceHub\Logs')) | Out-Null
    $candidates.Add((Join-Path $env:LOCALAPPDATA 'Temp')) | Out-Null
  }
  if ($env:TEMP) { $candidates.Add($env:TEMP) | Out-Null }

  $found = @()
  foreach ($root in $candidates) {
    if (-not (Test-Path $root)) { continue }
    $attempted += $root
  $hits = Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in $patterns -and ($_.FullName -match 'copilot|chat|conversation') } |
            Sort-Object LastWriteTime -Descending
    if ($hits) { $found += $hits }
  }
  if ($found) {
    $files = $found | Sort-Object LastWriteTime -Descending
    # Adjust SnapshotsDir to the directory of the latest file for future relative references
    $SnapshotsDir = ($files | Select-Object -First 1).DirectoryName
    if ($VerboseParsing) { Write-Info "Using detected snapshot directory: $SnapshotsDir" }
  }
  else {
    $attempted += ($candidates | Sort-Object -Unique)
    $attempted = $attempted | Sort-Object -Unique
    $list = ($attempted | Where-Object { $_ } | ForEach-Object { " - $_" }) -join [Environment]::NewLine
    throw "No snapshot files found. Paths attempted:${([Environment]::NewLine)}$list"
  }
}

if (-not $files) { throw "No snapshot files found under $SnapshotsDir" }

# Prefer non-database files for latest snapshot selection
$latest = $files | Where-Object { $_.Extension -notin @('.db','.sqlite') } | Select-Object -First 1
if (-not $latest) { $latest = $files | Select-Object -First 1 }
Write-Info "Latest snapshot: $($latest.FullName)  (LastWriteTime=$($latest.LastWriteTime))"

if ($DiscoverOnly) {
  Write-Info "DiscoverOnly set. Showing top 10 candidates and exiting."
  $files | Select-Object -First 10 FullName, LastWriteTime, Length | Format-Table | Out-Host
  return
}

# 3) Define output paths
$stamp = (Get-Date -Format "yyyyMMdd_HHmmss")
$base  = "CopilotChat_$stamp"
$rawOut = Join-Path $OutputDir ($base + '_RAW' + $latest.Extension)
$logOut = Join-Path $OutputDir ($base + '.' + $OutputFormat)

# 4) Always keep a raw copy
Copy-Item -Path $latest.FullName -Destination $rawOut -Force
Write-Info "Raw snapshot copied to: $rawOut"

# 5) Try to parse into a clean, linear chat log
$script:messages = New-Object System.Collections.Generic.List[object]

function Add-Msg([string]$role,[string]$content,[object]$ts){
  if ([string]::IsNullOrWhiteSpace($content)) { return }
  $dt = Convert-ToDateTime $ts
  $script:messages.Add([pscustomobject]@{
    timestamp = $dt
    role      = $role
    content   = $content.Trim()
  }) | Out-Null
}

$ext = $latest.Extension.ToLowerInvariant()

try {
  switch ($ext) {
    '.json' {
      $json = Get-Content -Raw -Path $latest.FullName | ConvertFrom-Json -ErrorAction Stop

      # Heuristics: look for common shapes
      if ($null -ne $json.messages) {
        foreach ($m in $json.messages) {
          $role = $m.role ?? $m.author ?? 'unknown'
          $txt  = $m.content ?? $m.text ?? $m.message ?? ($m.body?.text) ?? ''
          $ts   = $m.timestamp ?? $m.createdAt ?? (Get-Date)
          if (-not $IncludeSystemMessages -and ($role -match 'system|tool|function')) { continue }
          Add-Msg $role $txt ([DateTime]$ts)
        }
      }
      elseif ($null -ne $json.conversation) {
        foreach ($m in $json.conversation) {
          $role = $m.role ?? 'unknown'
          $txt  = $m.text ?? $m.content ?? ''
          $ts   = $m.time ?? (Get-Date)
          if (-not $IncludeSystemMessages -and ($role -match 'system|tool|function')) { continue }
          Add-Msg $role $txt ([DateTime]$ts)
        }
      }
      else {
        if ($VerboseParsing) { Write-Info "JSON schema not recognized. Falling back to raw-only." }
      }
    }

    '.ndjson' {
      Get-Content -Path $latest.FullName | ForEach-Object {
        if ([string]::IsNullOrWhiteSpace($_)) { return }
        try {
          $obj = $_ | ConvertFrom-Json -ErrorAction Stop
          $role = $obj.role ?? $obj.author ?? 'unknown'
          $txt  = $obj.content ?? $obj.text ?? $obj.message ?? ''
          $ts   = $obj.timestamp ?? $obj.createdAt ?? (Get-Date)
          if (-not $IncludeSystemMessages -and ($role -match 'system|tool|function')) { return }
          Add-Msg $role $txt ([DateTime]$ts)
        } catch {
          if ($VerboseParsing) { Write-Warn "NDJSON line not JSON: $_" }
        }
      }
    }

    default {
      # Plain text or log: copy lines that look like chat. Very lightweight.
      $lines = Get-Content -Path $latest.FullName
      $regex = '^\s*\[(?<ts>[\d\-T:\.Z\s\/]+)\]\s*(?<role>User|Assistant|System|Tool)\s*:\s*(?<msg>.+)$'
      foreach ($ln in $lines) {
        $m = [regex]::Match($ln, $regex)
        if ($m.Success) {
          $role = $m.Groups['role'].Value
          if (-not $IncludeSystemMessages -and ($role -match 'System|Tool')) { continue }
          $ts = $m.Groups['ts'].Value
          $msg = $m.Groups['msg'].Value
          Add-Msg $role $msg ([DateTime]$ts)
        }
      }
      if (-not $messages.Count -and $VerboseParsing) {
        Write-Info "No structured lines matched in text log."
      }
    }
  }
}
catch {
  Write-Warn "Parsing error: $($_.Exception.Message). Exporting raw only."
}

# 6) Emit formatted output
$msgList = $script:messages
if ($msgList.Count -gt 0) {
  $messages = $msgList | Sort-Object timestamp, role

  switch ($OutputFormat) {
    'json' {
      $messages | ConvertTo-Json -Depth 6 | Out-File -FilePath $logOut -Encoding UTF8
    }
    'txt' {
      $sb = New-Object System.Text.StringBuilder
      foreach ($m in $messages) {
        [void]$sb.AppendLine(("[{0}] {1}: {2}" -f ($m.timestamp.ToString("s")), $m.role, $m.content))
      }
      $sb.ToString() | Out-File -FilePath $logOut -Encoding UTF8
    }
    'md' {
      $sb = New-Object System.Text.StringBuilder
  [void]$sb.AppendLine("# Copilot Chat (latest snapshot)")
      [void]$sb.AppendLine()
  [void]$sb.AppendLine("*Solution:* ``$SolutionRoot``")
  [void]$sb.AppendLine("*Snapshot:* ``$($latest.FullName)``  ")
      [void]$sb.AppendLine("*Exported:* $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')`n")
      foreach ($m in $messages) {
        $role = ($m.role -replace '^\s+|\s+$','')
        [void]$sb.AppendLine(("**{0}** · {1}" -f $role, $m.timestamp.ToString("yyyy-MM-dd HH:mm:ss")))
        [void]$sb.AppendLine()
        [void]$sb.AppendLine($m.content)
        [void]$sb.AppendLine()
        [void]$sb.AppendLine("---")
      }
      $sb.ToString() | Out-File -FilePath $logOut -Encoding UTF8
    }
  }
  Write-Info "Parsed chat exported to: $logOut"
}
else {
  $logOut = $rawOut
  Write-Warn "No messages parsed. Raw snapshot is your artifact: $rawOut"
}

# 7) Exit summary
$result = [pscustomobject]@{
  SolutionRoot = $SolutionRoot
  SnapshotFile = $latest.FullName
  RawCopy      = $rawOut
  Export       = $logOut
  ParsedCount  = $msgList.Count
}

if ($OpenAfter) {
  try { Start-Process $logOut } catch { Write-Warn "Failed to open export: $($_.Exception.Message)" }
}

$result | Format-List
