# Rebuilds Snake.exe from the C# sources next to this script, using the C#
# compiler that ships with Windows. No .NET SDK, no downloads, no project file.
#
#   Right-click -> Run with PowerShell
#
# The finished Snake.exe lands in the folder above this one, next to the web
# folder. Pass -Capture to also build the headless test binary used to check
# the 3D game frame by frame.
param([switch]$Capture)

$ErrorActionPreference = 'Stop'

# Everything is found relative to this script, so the folder can live anywhere.
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
$fw   = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc  = Join-Path $fw 'csc.exe'
$ico  = Join-Path $root 'Snake.ico'
$src  = @("$here\Launcher.cs", "$here\Program.cs", "$here\Snake3D.cs")

if (-not (Test-Path $csc)) { throw "C# compiler not found at $csc" }

# WinForms + GDI+ for the flat board, WPF for the launcher and the night sky.
$refs = "$fw\System.dll", "$fw\System.Core.dll", "$fw\System.Drawing.dll",
        "$fw\System.Windows.Forms.dll", "$fw\System.Xaml.dll",
        "$fw\WPF\PresentationCore.dll", "$fw\WPF\PresentationFramework.dll",
        "$fw\WPF\WindowsBase.dll"
$refArg = '/reference:' + ($refs -join ';')

Get-Process Snake, Snake3DCap -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

& $csc /nologo /target:winexe /optimize+ /platform:anycpu /win32icon:$ico `
    /out:"$root\Snake.exe" $refArg $src
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
"Snake.exe  ok  ->  $root\Snake.exe"

if ($Capture) {
    $cap = Join-Path $env:TEMP 'snake3dcap'
    New-Item -ItemType Directory -Force $cap | Out-Null
    & $csc /nologo /target:winexe /define:CAPTURE /platform:anycpu `
        /out:"$cap\Snake3DCap.exe" $refArg $src
    if ($LASTEXITCODE -ne 0) { throw 'capture build failed' }
    "capture    ok  ->  $cap"
}

if (-not $Capture) { Start-Sleep -Seconds 2 }
