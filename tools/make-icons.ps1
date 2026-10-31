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

# rounded red tile + white metro M (the proven-safe drawing ops from v1.0.4)
function New-Tile([int]$size, [string]$name) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $r = [int]($size * 0.18)
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $redBrush = New-Object System.Drawing.SolidBrush($red)
    $g.FillPath($redBrush, $path)
    $whiteBrush = New-Object System.Drawing.SolidBrush($white)
    Fill-MetroM $g ($size * 0.14) ($size * 0.10) ($size * 0.72) $whiteBrush
    $g.Dispose()
    $bmp.Save((Join-Path $outDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $path.Dispose()
    $redBrush.Dispose()
    $whiteBrush.Dispose()
    Write-Host "OK $name ($size px)"
}

New-Tile 44  'Square44x44Logo.scale-100.png'
New-Tile 150 'Square150x150Logo.scale-100.png'
New-Tile 50  'StoreLogo.scale-100.png'

# splash: red bg + big white metro M (v1.0.4-proven ops, all-positive coords)
$bmp = New-Object System.Drawing.Bitmap(620, 300)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear($red)
$whiteBrush = New-Object System.Drawing.SolidBrush($white)
Fill-MetroM $g 130 20 360 $whiteBrush
$g.Dispose()
$bmp.Save((Join-Path $outDir 'SplashScreen.scale-100.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
$whiteBrush.Dispose()
Write-Host 'OK SplashScreen.scale-100.png (620x300)'
Write-Host "done -> $outDir"
