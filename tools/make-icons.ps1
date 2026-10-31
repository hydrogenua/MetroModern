# Generates app icon PNGs (Moscow Metro white "M" on full-bleed red square)
# into ../src/MetroModern.Web/assets. Uses System.Drawing only.
# Run: powershell -File tools\make-icons.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
$outDir = Join-Path (Split-Path -Parent $scriptRoot) 'src\MetroModern.Web\assets'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$red = [System.Drawing.Color]::FromArgb(255, 218, 33, 40)   # #da2128
$white = [System.Drawing.Color]::White

# Moscow-metro "M" (sharp peaks, hanging center V, serif feet) in a 100x100 box (y down)
$leftLeg  = @(13, 71, 37, 18, 44, 18, 31, 71)
$rightLeg = @(56, 18, 63, 18, 87, 71, 69, 71)
$vLeft    = @(38, 18, 46, 18, 50, 77)
$vRight   = @(54, 18, 62, 18, 50, 77)
$footL    = @(10, 71, 36, 71, 36, 80, 10, 80)
$footR    = @(64, 71, 90, 71, 90, 80, 64, 80)
$shapes   = @($leftLeg, $vLeft, $vRight, $rightLeg, $footL, $footR)

function Fill-MetroM([System.Drawing.Graphics]$g, [float]$ox, [float]$oy, [float]$sz, [System.Drawing.Brush]$brush) {
    $s = $sz / 100.0
    foreach ($shape in $shapes) {
        $n = [int]($shape.Count / 2)
        $pts = New-Object 'System.Drawing.PointF[]' $n
        for ($i = 0; $i -lt $n; $i++) {
            $pts[$i] = New-Object System.Drawing.PointF(($ox + $shape[2 * $i] * $s), ($oy + $shape[2 * $i + 1] * $s))
        }
        $g.FillPolygon($brush, $pts)
    }
}

# full-bleed white square + red metro M (the M body spans x 14..86, y 18..79 of the box)
function New-Tile([int]$size, [string]$name) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::White)
    $redBrush = New-Object System.Drawing.SolidBrush($red)
    Fill-MetroM $g ($size * 0.03) ($size * 0.055) ($size * 0.94) $redBrush
    $g.Dispose()
    $bmp.Save((Join-Path $outDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $redBrush.Dispose()
    Write-Host "OK $name ($size px)"
}

New-Tile 44  'Square44x44Logo.scale-100.png'
New-Tile 150 'Square150x150Logo.scale-100.png'
New-Tile 50  'StoreLogo.scale-100.png'

# splash: white bg + big red metro M
$bmp = New-Object System.Drawing.Bitmap(620, 300)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::White)
$redBrush = New-Object System.Drawing.SolidBrush($red)
Fill-MetroM $g 126 -29 369 $redBrush
$g.Dispose()
$bmp.Save((Join-Path $outDir 'SplashScreen.scale-100.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
$redBrush.Dispose()
Write-Host 'OK SplashScreen.scale-100.png (620x300)'
Write-Host "done -> $outDir"
