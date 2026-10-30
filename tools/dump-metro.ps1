# Dumps Yandex Metro scheme data (yandex.ru web API) into ../data/<city>.json(.gz)
# Reverse-engineered flow:
#   1) GET /metro/<city-page>            -> establishes cookie session
#   2) GET /metro/api/scheme/getMetroSchemeBySchemeId?ajax=1&id=<schemeId>&lang=ru
#        -> {"csrfToken": "..."}       bootstrap token
#   3) GET same URL + csrfToken + sessionId (client-generated) + s (cache buster)
#        -> full scheme JSON
param(
    [string[]]$SchemeIds = @('sc34974011','sc60983525'),
    [switch]$OnlyPrompt,
    [string]$OutDir = ''
)

$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
if (-not $OutDir) { $OutDir = Join-Path $scriptRoot '..' }
if ((Get-Item $OutDir).PSProvider -match 'Registry|None') { $OutDir = Join-Path (Split-Path -Parent $scriptRoot) 'data' }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

$ua = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36'
$headers = @{ Referer = 'https://yandex.ru/metro/moscow'; 'X-Requested-With' = 'XMLHttpRequest' }

Invoke-WebRequest -Uri 'https://yandex.ru/metro/moscow' -UserAgent $ua -Headers @{Referer='https://yandex.ru/'} -SessionVariable web -UseBasicParsing | Out-Null

$stats = Invoke-WebRequest -Uri 'https://yandex.ru/metro/moscow' -UserAgent $ua -Headers @{Referer='https://yandex.ru/'} -WebSession $web -UseBasicParsing
# city slugs by scheme id (extend as needed)
$slugMap = @{
    'sc34974011' = 'moscow'
    'sc60983525' = 'saint-petersburg'
    'sc27186246' = 'ekaterinburg'   # id not verified, example slot
}

foreach ($schemeId in $SchemeIds) {
    $city = if ($slugMap.ContainsKey($schemeId)) { $slugMap[$schemeId] } else { $schemeId }

    $bootUrl = "https://yandex.ru/metro/api/scheme/getMetroSchemeBySchemeId?ajax=1&id=$schemeId&lang=ru"
    $boot = Invoke-RestMethod -Uri $bootUrl -WebSession $web -UserAgent $ua -Headers $headers
    if (-not $boot.csrfToken) { Write-Error "No csrfToken for $schemeId"; continue }

    $ts = [DateTimeOffset]::Now.ToUnixTimeMilliseconds()
    $sid = "{0}26565171-{1}-zbdsax5h2v5ebfy5-BAL" -f $ts, (Get-Random -Minimum 1000000000 -Maximum 9999999999)
    $sparam = Get-Random -Minimum 300000000 -Maximum 1800000000

    $url = "https://yandex.ru/metro/api/scheme/getMetroSchemeBySchemeId?ajax=1" +
           "&csrfToken=$([uri]::EscapeDataString($boot.csrfToken))" +
           "&id=$schemeId&lang=ru&s=$sparam&sessionId=$sid"
    $resp = Invoke-WebRequest -Uri $url -WebSession $web -UserAgent $ua -UseBasicParsing -Headers $headers
    $raw = $resp.Content

    $json = $null
    try { $json = $raw | ConvertFrom-Json } catch {}
    if ($json -and $json.data -and $json.data.schemeData) {
        [IO.File]::WriteAllText((Join-Path $OutDir "$city.json"), $raw, (New-Object Text.UTF8Encoding $false))
        $bytes = [Text.Encoding]::UTF8.GetBytes($raw)
        $ms = New-Object IO.MemoryStream
        $gz = New-Object IO.Compression.GZipStream($ms, [IO.Compression.CompressionLevel]::Optimal)
        $gz.Write($bytes, 0, $bytes.Length); $gz.Dispose()
        [IO.File]::WriteAllBytes((Join-Path $OutDir "$city.json.gz"), $ms.ToArray())
        Write-Host ("OK {0} ({1}): {2:N0} bytes json, {3:N0} gz, stations={4}" -f $city, $schemeId, $bytes.Length, $ms.Length, $json.data.schemeData.stations.Count)
    } else {
        Write-Error "Failed for $schemeId : $($raw.Substring(0, [Math]::Min(200, $raw.Length)))"
    }
}
