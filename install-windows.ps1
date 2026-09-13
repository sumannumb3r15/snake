# Windows installer for Snake.
#
#   Right-click -> Run with PowerShell
#
# Copies the app into your user profile (no admin needed), puts it on the Start
# menu and the Desktop, and registers it so it shows up in
# Settings > Apps > Installed apps with a working Uninstall button.
#
#   .\install-windows.ps1            install
#   .\install-windows.ps1 -Uninstall remove it again
param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$src     = Split-Path -Parent $MyInvocation.MyCommand.Path
$dest    = Join-Path $env:LOCALAPPDATA 'Snake'
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$desktop = [Environment]::GetFolderPath('Desktop')
$regKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SnakeGame'
$shortcuts = @((Join-Path $startMenu 'Snake.lnk'), (Join-Path $desktop 'Snake.lnk'))

function Remove-Shortcuts { foreach ($s in $shortcuts) { Remove-Item $s -Force -ErrorAction SilentlyContinue } }

if ($Uninstall) {
  Remove-Shortcuts
  Remove-Item $regKey -Recurse -Force -ErrorAction SilentlyContinue
  if (Test-Path $dest) { Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue }
  Write-Host "Snake removed. Your high scores are kept in $env:APPDATA\SnakeApp" -ForegroundColor Green
  Start-Sleep -Seconds 2
  return
}

if (-not (Test-Path (Join-Path $src 'Snake.exe'))) {
  Write-Host "Snake.exe is not next to this script - run build.ps1 first." -ForegroundColor Red
  Start-Sleep -Seconds 4
  return
}

Get-Process Snake -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $src 'Snake.exe') $dest -Force
Copy-Item (Join-Path $src 'Snake.ico') $dest -Force
if (Test-Path (Join-Path $src 'docs')) {
  Copy-Item (Join-Path $src 'docs') $dest -Recurse -Force
}

$ws = New-Object -ComObject WScript.Shell
foreach ($path in $shortcuts) {
  $lnk = $ws.CreateShortcut($path)
  $lnk.TargetPath       = Join-Path $dest 'Snake.exe'
  $lnk.WorkingDirectory = $dest
  $lnk.IconLocation     = Join-Path $dest 'Snake.ico'
  $lnk.Description      = "What's up? Two snakes - pick one."
  $lnk.Save()
}

# so it appears in Settings > Apps with a real Uninstall button
New-Item -Path $regKey -Force | Out-Null
$size = [int]((Get-ChildItem $dest -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
$props = @{
  DisplayName     = 'Snake (2D & 3D)'
  DisplayVersion  = '1.0.0'
  Publisher       = 'Built locally'
  DisplayIcon     = (Join-Path $dest 'Snake.ico')
  InstallLocation = $dest
  EstimatedSize   = $size
  NoModify        = 1
  NoRepair        = 1
  UninstallString = "powershell -ExecutionPolicy Bypass -File `"$dest\install-windows.ps1`" -Uninstall"
}
foreach ($k in $props.Keys) {
  New-ItemProperty -Path $regKey -Name $k -Value $props[$k] `
    -PropertyType $(if ($props[$k] -is [int]) { 'DWord' } else { 'String' }) -Force | Out-Null
}
Copy-Item $MyInvocation.MyCommand.Path $dest -Force

Write-Host ""
Write-Host "  Snake installed to $dest" -ForegroundColor Green
Write-Host "  Start menu and Desktop shortcuts created."
Write-Host "  Remove it any time from Settings > Apps > Installed apps."
Write-Host ""
Start-Sleep -Seconds 3
