# Packages src/MetroModern.Web into a signed .appx using Windows SDK tools.
# Prereqs: Windows 10 SDK (makeappx.exe, signtool.exe) + tools\make-cert.ps1 run once.
# Usage: powershell -File tools\build-web.ps1 [-Version 1.0.1.0]
param(
    [string]$Version = ''
)
$ErrorActionPreference = 'Stop'

$projRoot = Split-Path -Parent $PSScriptRoot
$src = Join-Path $projRoot 'src\MetroModern.Web'
$stage = Join-Path $projRoot 'build\stage'
$outDir = Join-Path $projRoot 'build\out'
$cert = Join-Path $projRoot 'build\cert\MetroModern.pfx'
$certPwd = 'metromodern'

# --- locate SDK bin (newest installed kit; kits may live on a non-C: drive) ---
$makeappx = $null; $signtool = $null
$kitRoots = @('C:\Program Files (x86)\Windows Kits\10\bin', 'C:\Program Files\Windows Kits\10\bin', 'T:\Windows Kits\10\bin')
foreach ($kitsRoot in $kitRoots) {
    if (-not (Test-Path $kitsRoot)) { continue }
    $verDirs = Get-ChildItem $kitsRoot -Directory | Sort-Object Name -Descending
    foreach ($vd in $verDirs) {
        $c1 = Join-Path $vd.FullName 'x86\makeappx.exe'
        $c2 = Join-Path $vd.FullName 'x64\makeappx.exe'
        $exe = if (Test-Path $c1) { $c1 } elseif (Test-Path $c2) { $c2 } else { $null }
        if ($exe) {
            $binDir = Split-Path -Parent $exe
            $makeappx = Join-Path $binDir 'makeappx.exe'
            $signtool = Join-Path $binDir 'signtool.exe'
            break
        }
    }
    if ($makeappx) { break }
}
if (-not $makeappx) { throw 'makeappx.exe not found: install the Windows 10 SDK first (see README).' }
Write-Host "sdk bin: $(Split-Path -Parent $makeappx)"

# --- read version from manifest if not given ---
if (-not $Version) {
    [xml]$mx = Get-Content (Join-Path $src 'AppxManifest.xml') -Encoding UTF8
    $Version = $mx.Package.Identity.Version
}

# --- stage ---
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item (Join-Path $src '*') $stage -Recurse -Force

$outAppx = Join-Path $outDir "MetroModern_${Version}_neutral.appx"
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

# --- pack ---
& $makeappx pack /d $stage /p $outAppx /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed: $LASTEXITCODE" }
Write-Host "packed: $outAppx"

# --- sign ---
if (-not (Test-Path $cert)) { throw 'No signing cert. Run: powershell -File tools\make-cert.ps1' }
& $signtool sign /fd SHA256 /a /f $cert /p $certPwd $outAppx
if ($LASTEXITCODE -ne 0) { throw "signtool failed: $LASTEXITCODE" }

# --- verify + copy cert for the phone ---
& $signtool verify /pa $outAppx
Copy-Item (Join-Path $projRoot 'build\cert\MetroModern.cer') $outDir -Force
Write-Host "SIGNED OK: $outAppx"
Write-Host "Deploy:  powershell -File tools\deploy-web.ps1 -Ip <phone-ip> [-Pin nnnnnn]"
