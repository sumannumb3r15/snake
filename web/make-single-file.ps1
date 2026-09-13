# Folds the web game into ONE self-contained file, Snake.html, with the icons
# embedded as data URIs. Nothing to unzip, nothing beside it - mail it, AirDrop
# it, drop it on a USB stick, double-click to play.
#
# Note: a single file can be *played* anywhere, but it cannot be *installed* to
# a phone's home screen. Android and iOS only install a web app that is served
# from a URL, never one opened from local storage. Installing needs the link.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out  = Join-Path (Split-Path -Parent $here) 'Snake.html'

$html = Get-Content (Join-Path $here 'index.html') -Raw -Encoding UTF8

function DataUri($file) {
  $b = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $here $file)))
  "data:image/png;base64,$b"
}
$i192 = DataUri 'icon-192.png'

# The manifest and service worker both need to be fetched as separate files, so
# they cannot come along. Drop the links rather than leave them 404-ing.
$html = $html -replace '<link rel="manifest"[^>]*>\r?\n', ''
$html = $html -replace '<link rel="apple-touch-icon"[^>]*>', "<link rel=`"apple-touch-icon`" href=`"$i192`">"
$html = $html -replace '<link rel="icon"[^>]*>', "<link rel=`"icon`" href=`"$i192`">"

# Mark it, so it is obvious which build someone is looking at.
$html = $html -replace '(?m)^<title>Snake</title>', "<title>Snake</title>`r`n<!-- single-file build - everything inline -->"

[IO.File]::WriteAllText($out, $html, (New-Object Text.UTF8Encoding $false))
"{0}  ({1:N0} KB)" -f $out, ((Get-Item $out).Length / 1KB)
