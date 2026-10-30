# Tiny static file server for local preview (desktop browser QA of scheme data / JS app).
# Usage: powershell -File tools\devserver.ps1 [-Root <dir>] [-Port 8765]
param(
    [string]$Root = '',
    [int]$Port = 8765
)
$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
if (-not $Root) { $Root = Join-Path (Split-Path -Parent $scriptRoot) 'out' }

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://127.0.0.1:$Port/")
$listener.Start()
Write-Host "serving $Root at http://127.0.0.1:$Port/  (Ctrl+C to stop)"

$mime = @{
    '.svg' = 'image/svg+xml'; '.html' = 'text/html; charset=utf-8'; '.js' = 'text/javascript; charset=utf-8'
    '.css' = 'text/css; charset=utf-8'; '.json' = 'application/json; charset=utf-8'; '.png' = 'image/png'
    '.jpg' = 'image/jpeg'; '.ico' = 'image/x-icon'; '.gz' = 'application/gzip'
}

while ($listener.IsListening) {
    $ctx = $listener.GetContext()
    try {
        $rel = $ctx.Request.Url.AbsolutePath -replace '^/', ''
        if ($rel -eq '') { $rel = 'index.html' }
        $file = Join-Path $Root ($rel -replace '/', '\')
        if ((Test-Path $file -PathType Leaf) -and ((Get-Item $file).FullName.StartsWith((Get-Item $Root).FullName))) {
            $bytes = [IO.File]::ReadAllBytes($file)
            $ext = [IO.Path]::GetExtension($file).ToLowerInvariant()
            $ctx.Response.ContentType = if ($mime.ContainsKey($ext)) { $mime[$ext] } else { 'application/octet-stream' }
            $ctx.Response.Headers['Cache-Control'] = 'no-cache'
            $ctx.Response.ContentLength64 = $bytes.Length
            $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        } else {
            $ctx.Response.StatusCode = 404
        }
    } catch { try { $ctx.Response.StatusCode = 500 } catch {} }
    finally { try { $ctx.Response.Close() } catch {} }
}
