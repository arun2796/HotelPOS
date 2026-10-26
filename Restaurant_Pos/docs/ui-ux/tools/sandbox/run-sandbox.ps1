# Records the set-up chapter of the walkthrough video automatically in Windows Sandbox (a clean, disposable Windows
# without UAC prompts). Needs the feature:  Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM
# Stages <repo>\artifacts\sandbox-setup\in (both installers, ffmpeg.exe, sandbox-run.ps1), writes
# setup.wsb, starts the sandbox maximised, waits for out\done.txt and copies 00-setup.mp4/.json into <RecordDir>.
#   .\run-sandbox.ps1                       (defaults below)
#   .\run-sandbox.ps1 -NoCopy               keep the result in artifacts\sandbox-setup\out only
param(
    [string] $Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path,
    [string] $RecordDir = '',
    [int] $TimeoutMinutes = 45,
    [switch] $NoCopy
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path 'C:\Windows\System32\WindowsSandbox.exe')) { throw 'Windows Sandbox is not enabled on this PC.' }
if (-not $RecordDir) { $RecordDir = Join-Path $Repo 'artifacts\demo-video-en\record' }
$Stage = Join-Path $Repo 'artifacts\sandbox-setup'
$In = Join-Path $Stage 'in'; $Out = Join-Path $Stage 'out'
New-Item -ItemType Directory -Force $In, $Out | Out-Null
Get-ChildItem $Out -Recurse -File | Remove-Item -Force

# stage inputs (only when missing or newer)
$ffmpegSrc = (Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\Gyan.FFmpeg*\*\bin\ffmpeg.exe" | Select-Object -First 1).FullName
foreach ($pair in @(
        @{ src = $ffmpegSrc; dst = 'ffmpeg.exe' },
        @{ src = (Get-ChildItem (Join-Path $Repo 'artifacts\installers\HotelPOS-Server-Setup-*.exe') | Sort-Object Name | Select-Object -Last 1).FullName; dst = 'HotelPOS-Server-Setup-0.1.1.exe' },
        @{ src = (Get-ChildItem (Join-Path $Repo 'artifacts\installers\HotelPOS-Client-Setup-*.exe') | Sort-Object Name | Select-Object -Last 1).FullName; dst = 'HotelPOS-Client-Setup-0.1.1.exe' },
        @{ src = (Join-Path $PSScriptRoot 'sandbox-run.ps1'); dst = 'sandbox-run.ps1' })) {
    $dst = Join-Path $In $pair.dst
    if (-not (Test-Path $dst) -or (Get-Item $pair.src).LastWriteTime -gt (Get-Item $dst).LastWriteTime) { Copy-Item $pair.src $dst -Force; Write-Host "staged $($pair.dst)" }
}

$wsb = Join-Path $Stage 'setup.wsb'
@"
<Configuration>
  <VGpu>Default</VGpu>
  <Networking>Default</Networking>
  <MemoryInMB>6144</MemoryInMB>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$Stage</HostFolder>
      <SandboxFolder>C:\Setup</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -WindowStyle Hidden -ExecutionPolicy Bypass -File C:\Setup\in\sandbox-run.ps1</Command>
  </LogonCommand>
</Configuration>
"@ | Set-Content -Encoding UTF8 $wsb

Write-Host "starting Windows Sandbox ($wsb)"
Start-Process 'C:\Windows\System32\WindowsSandbox.exe' -ArgumentList "`"$wsb`""

# maximise the sandbox window so the inner desktop gets the full screen
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class SbNative { [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd); }
"@
$deadline = (Get-Date).AddSeconds(90); $maximised = $false
while ((Get-Date) -lt $deadline -and -not $maximised) {
    $p = Get-Process -Name 'WindowsSandboxClient', 'WindowsSandbox', 'WindowsSandboxRemoteSession' -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($p) { [void] [SbNative]::ShowWindow($p.MainWindowHandle, 3); $maximised = $true; Write-Host "sandbox window maximised ($($p.ProcessName))" } else { Start-Sleep -Seconds 2 }
}
if (-not $maximised) { Write-Warning 'sandbox window not found to maximise; recording keeps the default size' }

$done = Join-Path $Out 'done.txt'
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while ((Get-Date) -lt $deadline -and -not (Test-Path $done)) { Start-Sleep -Seconds 10 }
if (-not (Test-Path $done)) { Write-Warning "no done.txt after $TimeoutMinutes min; log so far:"; Get-Content (Join-Path $Out 'run.log') -ErrorAction SilentlyContinue | Select-Object -Last 20; exit 1 }
Get-Content (Join-Path $Out 'run.log') | Select-Object -Last 40
if (-not $NoCopy) {
    New-Item -ItemType Directory -Force $RecordDir | Out-Null
    Copy-Item (Join-Path $Out '00-setup.mp4'), (Join-Path $Out '00-setup.json') $RecordDir -Force
    Write-Host "copied 00-setup.mp4/.json to $RecordDir"
}
