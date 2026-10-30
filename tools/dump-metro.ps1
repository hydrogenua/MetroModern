# Dumps Yandex Metro scheme data (yandex.ru web API) into ../data/<alias>.json(.gz)
# and regenerates the app's city manifest src/MetroModern.Web/js/data/cities.js
#
# Reverse-engineered flow:
#   1) GET /metro/<city-page>            -> establishes cookie session
#   2) GET /metro/api/scheme/getMetroSchemeBySchemeId?ajax=1&id=<schemeId>&lang=ru
#        -> {"csrfToken": "..."}        bootstrap token
#   3) GET same URL + csrfToken + sessionId (client-generated) + s (cache buster)
#        -> full scheme JSON
#
# Usage:
#   powershell -File tools\dump-metro.ps1                     # all CIS cities
#   powershell -File tools\dump-metro.ps1 -Alias minsk,moscow # specific cities
param(
    [string[]]$Alias = @(),
    [switch]$All
)
$ErrorActionPreference = 'Continue'

$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
$projRoot = Split-Path -Parent $scriptRoot
$dataDir = Join-Path $projRoot 'data'
$webDataDir = Join-Path $projRoot 'src\MetroModern.Web\js\data'
foreach ($d in @($dataDir, $webDataDir)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null } }

# metro systems of the ex-USSR (from yandex.ru/metro availableCities)
$cityTable = @(
    @{ name = 'Москва';            alias = 'moscow' },
    @{ name = 'Санкт-Петербург';   alias = 'saint-petersburg' },
    @{ name = 'Нижний Новгород';   alias = 'nizhny-novgorod' },
    @{ name = 'Новосибирск';       alias = 'novosibirsk' },
    @{ name = 'Екатеринбург';      alias = 'ekaterinburg' },
    @{ name = 'Казань';            alias = 'kazan' },
    @{ name = 'Самара';            alias = 'samara' },
    @{ name = 'Волгоград';         alias = 'volgograd' },
    @{ name = 'Минск';             alias = 'minsk' },
    @{ name = 'Киев';              alias = 'kiev' },
    @{ name = 'Харьков';           alias = 'kharkiv' },
    @{ name = 'Днепр';             alias = 'dnipro' },
    @{ name = 'Алматы';            alias = 'almaty' },
    @{ name = 'Астана';            alias = 'astana' },
    @{ name = 'Баку';              alias = 'baku' },
    @{ name = 'Тбилиси';           alias = 'tbilisi' },
    @{ name = 'Ереван';            alias = 'yerevan' },
    @{ name = 'Ташкент';           alias = 'tashkent' }
)

# scheme ids scraped from https://yandex.ru/metro/ (availableCities)
$ids = @{
    'moscow' = 'sc34974011'; 'saint-petersburg' = 'sc60983525'; 'nizhny-novgorod' = 'sc77792237'
    'novosibirsk' = 'sc02877545'; 'ekaterinburg' = 'sc58473698'; 'kazan' = 'sc63288776'
    'samara' = 'sc33333931'; 'volgograd' = 'sc03517743'; 'minsk' = 'sc31709615'
    'kiev' = 'sc21078879'; 'kharkiv' = 'sc12039691'; 'dnipro' = 'sc47063979'
    'almaty' = 'sc37730841'; 'astana' = 'sc17923552'; 'baku' = 'sc54283234'
    'tbilisi' = 'sc46903964'; 'yerevan' = 'sc95957238'; 'tashkent' = 'sc19351236'
}

if ($Alias.Count -gt 0) {
    $cities = $cityTable | Where-Object { $Alias -contains $_.alias }
} else {
    $cities = $cityTable
}

$ua = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36'
$headers = @{ Referer = 'https://yandex.ru/metro/moscow'; 'X-Requested-With' = 'XMLHttpRequest' }

Invoke-WebRequest -Uri 'https://yandex.ru/metro/moscow' -UserAgent $ua -Headers @{Referer='https://yandex.ru/'} -SessionVariable web -UseBasicParsing | Out-Null

$manifest = @()
$ok = 0
foreach ($city in $cities) {
    $alias = $city.alias
    $schemeId = $ids[$alias]
    if (-not $schemeId) { Write-Host "skip $alias (no scheme id)"; continue }

    $bootUrl = "https://yandex.ru/metro/api/scheme/getMetroSchemeBySchemeId?ajax=1&id=$schemeId&lang=ru"
    try { $boot = Invoke-RestMethod -Uri $bootUrl -WebSession $web -UserAgent $ua -Headers $headers -TimeoutSec 40 }
    catch { Write-Host "FAIL $alias (boot): $($_.Exception.Message)"; continue }
    if (-not $boot.csrfToken) { Write-Host "FAIL $alias (no csrfToken)"; continue }

    $ts = [DateTimeOffset]::Now.ToUnixTimeMilliseconds()
    $sid = "{0}26565171-{1}-zbdsax5h2v5ebfy5-BAL" -f $ts, (Get-Random -Minimum 1000000000 -Maximum 9999999999)
    $sparam = Get-Random -Minimum 300000000 -Maximum 1800000000

    $url = "https://yandex.ru/metro/api/scheme/getMetroSchemeBySchemeId?ajax=1" +
           "&csrfToken=$([uri]::EscapeDataString($boot.csrfToken))" +
           "&id=$schemeId&lang=ru&s=$sparam&sessionId=$sid"
    try { $resp = Invoke-WebRequest -Uri $url -WebSession $web -UserAgent $ua -UseBasicParsing -Headers $headers -TimeoutSec 60 }
    catch { Write-Host "FAIL $alias (data): $($_.Exception.Message)"; continue }
    $raw = $resp.Content

    $json = $null
    try { $json = $raw | ConvertFrom-Json } catch {}
    if ($json -and $json.data -and $json.data.schemeData) {
        [IO.File]::WriteAllText((Join-Path $dataDir "$alias.json"), $raw, (New-Object Text.UTF8Encoding $false))
        Copy-Item (Join-Path $dataDir "$alias.json") (Join-Path $webDataDir "$alias.json") -Force
        $bytes = [Text.Encoding]::UTF8.GetBytes($raw)
        $ms = New-Object IO.MemoryStream
        $gz = New-Object IO.Compression.GZipStream($ms, [IO.Compression.CompressionLevel]::Optimal)
        $gz.Write($bytes, 0, $bytes.Length); $gz.Dispose()
        [IO.File]::WriteAllBytes((Join-Path $dataDir "$alias.json.gz"), $ms.ToArray())
        $stations = 0
        try { $stations = $json.data.schemeData.stations.Count } catch {}
        Write-Host ("OK {0} ({1}): {2:N0} bytes, stations={3}" -f $city.name, $alias, $bytes.Length, $stations)
        $manifest += @{ name = $city.name; alias = $alias }
        $ok++
    } else {
        Write-Host "FAIL $alias : $($raw.Substring(0, [Math]::Min(120, $raw.Length)))"
    }
    Start-Sleep -Milliseconds 400
}

# regenerate the app's city manifest (UTF-8, no BOM)
$lines = @('/* generated by tools/dump-metro.ps1 - metro systems of the ex-USSR */', 'window.MetroCities = [')
foreach ($c in $manifest) {
    $lines += ('  { name: ' + '"' + $c.name + '", alias: "' + $c.alias + '" },')
}
$lines += '];'
[IO.File]::WriteAllLines((Join-Path $webDataDir 'cities.js'), $lines, (New-Object Text.UTF8Encoding $false))
Write-Host "manifest: $ok cities -> $webDataDir\cities.js"
