# C:\GSADUs\Dev\Powershell\Install-GSADUsAddin.ps1
# GSADUs Revit Add-in installer with auto-detect (net8.0-windows aware)

param([switch]$Force)

$ErrorActionPreference = "Stop"

# --- Config ---
$RevitYear      = 2026
$DeployDir      = "C:\GSADUs\RevitAddin"
$BackupDir      = Join-Path $DeployDir "Backups"
$AddinFileName  = "GSADUs.BatchExport.addin"
$AppFullClass   = "GSADUs.Revit.Addin.Startup"
$CmdFullClass   = "GSADUs.Revit.Addin.BatchExportCommand"
$VendorId       = "GSADUs"
$VendorDesc     = "GSADUs Tools"
$AppAddInId     = "8F0A0000-0000-4000-9000-000000000002"
$CmdAddInId     = "E5B48D43-DD13-4A93-BD12-5D3A523C53FD"

# --- Project root ---
$ProjectRoot = "C:\GSADUs\Dev\src\GSADUs.Revit.Addin"

# --- Locate latest build output (x64 Debug/Release) ---
$binRoot = Join-Path $ProjectRoot "bin\x64"
if (-not (Test-Path $binRoot)) { throw "Build output not found: $binRoot. Build the project first." }

$dll = Get-ChildItem -Path $binRoot -Recurse -Filter "GSADUs.Revit.Addin.dll" -File |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $dll) { throw "GSADUs.Revit.Addin.dll not found under $binRoot. Build the project." }

$srcDir = Split-Path -Path $dll.FullName -Parent

# --- Prepare deploy dirs ---
New-Item -ItemType Directory -Force -Path $DeployDir  | Out-Null
New-Item -ItemType Directory -Force -Path $BackupDir  | Out-Null

# --- Gather files to copy (dll, pdb, *.json including .deps.json) ---
$files = @()
$files += $dll
$pdb = [IO.Path]::ChangeExtension($dll.FullName, ".pdb")
if (Test-Path $pdb) { $files += Get-Item $pdb }
$files += Get-ChildItem -Path (Join-Path $srcDir "*.json") -File -ErrorAction SilentlyContinue

# --- Copy with freshness check unless -Force ---
foreach ($f in $files) {
    $dst = Join-Path $DeployDir $f.Name
    $doCopy = $true
    if (-not $Force -and (Test-Path $dst)) {
        $dstInfo = Get-Item $dst
        if ($f.LastWriteTime -le $dstInfo.LastWriteTime) { $doCopy = $false }
    }
    if ($doCopy) {
        Copy-Item -LiteralPath $f.FullName -Destination $dst -Force
        Unblock-File -Path $dst -ErrorAction SilentlyContinue
    }
}

# --- Write .addin under ProgramData ---
$addinDir  = Join-Path $env:ProgramData ("Autodesk\Revit\Addins\" + $RevitYear)
New-Item -ItemType Directory -Force -Path $addinDir | Out-Null
$addinPath = Join-Path $addinDir $AddinFileName

if (Test-Path $addinPath) {
    $stamp = Get-Date -Format yyyyMMdd_HHmmss
    Copy-Item $addinPath (Join-Path $BackupDir "$AddinFileName.bak_$stamp") -Force
}

$dstDll = Join-Path $DeployDir "GSADUs.Revit.Addin.dll"
$xml = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>GSADUs Tools</Name>
    <Assembly>$dstDll</Assembly>
    <AddInId>$AppAddInId</AddInId>
    <FullClassName>$AppFullClass</FullClassName>
    <VendorId>$VendorId</VendorId>
    <VendorDescription>$VendorDesc</VendorDescription>
  </AddIn>
  <AddIn Type="Command">
    <Name>GSADUs Batch Export</Name>
    <Assembly>$dstDll</Assembly>
    <AddInId>$CmdAddInId</AddInId>
    <FullClassName>$CmdFullClass</FullClassName>
    <VendorId>$VendorId</VendorId>
    <VendorDescription>$VendorDesc</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

# fix potential typo in variable name
$xml = $xml -replace '\$CmdFullClassName', $CmdFullClass
$xml | Set-Content -Path $addinPath -Encoding UTF8

# --- Report ---
$hash = if (Test-Path $dstDll) { (Get-FileHash $dstDll -Algorithm SHA256).Hash } else { "<missing>" }
Write-Host "`nDeployed:" -ForegroundColor Green
Write-Host ("  DLL:   {0}" -f $dstDll)
Write-Host ("  PDB:   {0}" -f (Join-Path $DeployDir "GSADUs.Revit.Addin.pdb"))
Write-Host ("  JSON:  {0}" -f ((Get-ChildItem "$DeployDir\*.json" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name) -join ", "))
Write-Host ("  ADDIN: {0}" -f $addinPath)
Write-Host ("  SHA256 DLL: {0}" -f $hash)
Write-Host "Done."
