# Serves this folder over your local network, so a phone, tablet or Mac on the
# same Wi-Fi can open the game and install it.
#
#   Right-click -> Run with PowerShell     (plain: this PC only)
#   Run from an ADMIN PowerShell           (needed to let other devices connect)
#
# Stop it with Ctrl+C.
param([int]$Port = 8080)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$admin = ([Security.Principal.WindowsPrincipal] `
          [Security.Principal.WindowsIdentity]::GetCurrent()
         ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

$listener = New-Object System.Net.HttpListener
if ($admin) { $listener.Prefixes.Add("http://+:$Port/") }
else        { $listener.Prefixes.Add("http://localhost:$Port/") }

try { $listener.Start() }
catch {
  Write-Host "Could not listen on port $Port. Is something else using it?" -ForegroundColor Red
  Write-Host $_.Exception.Message
  return
}

$ip = (Get-NetIPAddress -AddressFamily IPv4 |
       Where-Object { $_.IPAddress -notlike '127.*' -and $_.PrefixOrigin -ne 'WellKnown' } |
       Select-Object -First 1 -ExpandProperty IPAddress)

Write-Host ""
Write-Host "  Snake is being served from $root" -ForegroundColor Green
Write-Host ""
Write-Host "  On this PC        http://localhost:$Port/"
if ($admin -and $ip) {
  Write-Host "  On your phone/Mac http://${ip}:$Port/   (same Wi-Fi)" -ForegroundColor Cyan
  Write-Host ""
  Write-Host "  If the phone cannot reach it, allow PowerShell through Windows Firewall"
  Write-Host "  on Private networks, or run:  netsh advfirewall firewall add rule ``"
  Write-Host "      name=`"Snake $Port`" dir=in action=allow protocol=TCP localport=$Port"
} else {
  Write-Host "  Other devices     run this again from an ADMIN PowerShell" -ForegroundColor Yellow
}
Write-Host ""
Write-Host "  Ctrl+C to stop." -ForegroundColor DarkGray
Write-Host ""

$types = @{
  '.html'='text/html; charset=utf-8'; '.js'='text/javascript; charset=utf-8';
  '.json'='application/json'; '.webmanifest'='application/manifest+json';
  '.png'='image/png'; '.svg'='image/svg+xml'; '.ico'='image/x-icon';
  '.css'='text/css; charset=utf-8'; '.txt'='text/plain; charset=utf-8'
}

try {
  while ($listener.IsListening) {
    $ctx = $listener.GetContext()
    $rel = [uri]::UnescapeDataString($ctx.Request.Url.AbsolutePath.TrimStart('/'))
    if ([string]::IsNullOrWhiteSpace($rel)) { $rel = 'index.html' }

    # keep requests inside the folder
    $full = [IO.Path]::GetFullPath((Join-Path $root $rel))
    if (-not $full.StartsWith([IO.Path]::GetFullPath($root), [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path $full -PathType Leaf)) {
      $ctx.Response.StatusCode = 404
      $ctx.Response.Close()
      continue
    }

    $ext = [IO.Path]::GetExtension($full).ToLower()
    $ctx.Response.ContentType = if ($types.ContainsKey($ext)) { $types[$ext] } else { 'application/octet-stream' }
    $bytes = [IO.File]::ReadAllBytes($full)
    $ctx.Response.ContentLength64 = $bytes.Length
    $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
    $ctx.Response.Close()
    Write-Host ("  {0}  {1}" -f (Get-Date -Format 'HH:mm:ss'), $rel) -ForegroundColor DarkGray
  }
}
finally { $listener.Stop(); $listener.Close() }
