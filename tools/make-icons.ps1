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
$leftLeg  = @(20, 68, 36, 20, 40, 20, 33, 68)
$rightLeg = @(60, 20, 64, 20, 80, 68, 67, 68)
$vLeft    = @(32, 20, 40, 20, 50, 79)
$vRight   = @(60, 20, 68, 20, 50, 79)
$footL    = @(12, 68, 40, 68, 40, 75, 12, 75)
$footR    = @(60, 68, 88, 68, 88, 75, 60, 75)
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

# full-bleed red square + white M centered (the M body spans x 12..88, y 20..79 of the box)
function New-Tile([int]$size, [string]$name) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear($red)
    $whiteBrush = New-Object System.Drawing.SolidBrush($white)
    Fill-MetroM $g ($size * 0.079) ($size * 0.083) ($size * 0.842) $whiteBrush
    $g.Dispose()
    $bmp.Save((Join-Path $outDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $whiteBrush.Dispose()
    Write-Host "OK $name ($size px)"
}

New-Tile 44  'Square44x44Logo.scale-100.png'
New-Tile 150 'Square150x150Logo.scale-100.png'
New-Tile 50  'StoreLogo.scale-100.png'

# splash: red bg + big white M
$bmp = New-Object System.Drawing.Bitmap(620, 300)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear($red)
$whiteBrush = New-Object System.Drawing.SolidBrush($white)
Fill-MetroM $g 112 -46 395 $whiteBrush
$g.Dispose()
$bmp.Save((Join-Path $outDir 'SplashScreen.scale-100.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
$whiteBrush.Dispose()
Write-Host 'OK SplashScreen.scale-100.png (620x300)'
Write-Host "done -> $outDir"
