# Generates app icon PNGs from tools\logo-source.png AS-IS (red M on black square).
# The image is scaled to fill the tile/splash completely. System.Drawing only.
# Run: powershell -File tools\make-icons.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
$projRoot = Split-Path -Parent $scriptRoot
$outDir = Join-Path $projRoot 'src\MetroModern.Web\assets'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$srcPath = Join-Path $scriptRoot 'logo-source.png'

$src = [System.Drawing.Image]::FromFile($srcPath)

# tiles: the image scaled to fill the entire tile edge-to-edge
function New-Tile([int]$size, [string]$name) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()
    $bmp.Save((Join-Path $outDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "OK $name ($size px)"
}

New-Tile 44  'Square44x44Logo.scale-100.png'
New-Tile 150 'Square150x150Logo.scale-100.png'
New-Tile 50  'StoreLogo.scale-100.png'

# splash 620x300: the image centered, scaled to cover
$bmp = New-Object System.Drawing.Bitmap(620, 300)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.Clear([System.Drawing.Color]::Black)
$msz = [float](300 * 1.05)
$g.DrawImage($src, ([float]((620 - $msz) / 2)), ([float](($300 - $msz) / 2)), $msz, $msz)
$g.Dispose()
$bmp.Save((Join-Path $outDir 'SplashScreen.scale-100.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
$src.Dispose()
Write-Host 'OK SplashScreen.scale-100.png (620x300)'
Write-Host "done -> $outDir"
