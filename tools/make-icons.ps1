# Generates app icons from tools\logo-source.png (red M on black):
# the M becomes opaque metro red on transparent, composited on white tiles.
# Run: powershell -File tools\make-icons.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
$projRoot = Split-Path -Parent $scriptRoot
$outDir = Join-Path $projRoot 'src\MetroModern.Web\assets'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$srcPath = Join-Path $scriptRoot 'logo-source.png'

# pass 1: black bg -> transparent, the M -> solid metro red #da2128 (per-pixel)
$src = New-Object System.Drawing.Bitmap($srcPath)
$W = $src.Width; $H = $src.Height
$metroRed = [System.Drawing.Color]::FromArgb(255, 218, 33, 40)

# converted master: transparent bg + red M (500x500)
$logo = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
for ($y = 0; $y -lt $H; $y++) {
    for ($x = 0; $x -lt $W; $x++) {
        $p = $src.GetPixel($x, $y)
        $t = [Math]::Min(1.0, [Math]::Max(0.0, ($p.R - 8) / 210.0))
        $logo.SetPixel($x, $y, [System.Drawing.Color]::FromArgb([int][Math]::Round(255 * $t), $metroRed))
    }
}
$src.Dispose()
$logo.Save((Join-Path $outDir 'logo-white.png'), [System.Drawing.Imaging.ImageFormat]::Png)
# pass 2: tiles + splash = white bg + scaled logo
function New-Tile([int]$size, [string]$name) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::White)
    $msz = [float]($size * 0.78)
    $g.DrawImage($logo, ([float](($size - $msz) / 2)), ([float](($size - $msz) / 2)), $msz, $msz)
    $g.Dispose()
    $bmp.Save((Join-Path $outDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "OK $name ($size px)"
}

New-Tile 44  'Square44x44Logo.scale-100.png'
New-Tile 150 'Square150x150Logo.scale-100.png'
New-Tile 50  'StoreLogo.scale-100.png'

# splash 620x300
$bmp = New-Object System.Drawing.Bitmap(620, 300)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.Clear([System.Drawing.Color]::White)
$msz = [float](300 * 0.92)
$g.DrawImage($logo, ([float]((620 - $msz) / 2)), ([float](($300 - $msz) / 2)), $msz, $msz)
$g.Dispose()
$bmp.Save((Join-Path $outDir 'SplashScreen.scale-100.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
$logo.Dispose()
Write-Host 'OK SplashScreen.scale-100.png (620x300)'
Write-Host "done -> $outDir"
