# Records the whole screen with ffmpeg while you do something by hand (install the server, install a terminal, set up the
# Android app on a phone mirrored to the screen) and writes the step marks the video builder needs.
#
#   .\record-screen.ps1 -Out <repo>\artifacts\demo-video-i18n\record -Name 00-setup
#
# While it records: press ENTER each time you START a new step and type the step id (from narration.<lang>.json, e.g.
# what-goes-where, server-pc, install-postgresql, server-setup, admin-and-https, finish-and-check, install-terminals,
# phones-browser, android-app, first-sign-in, roles) — or just ENTER to keep the previous id. Type  q  and ENTER to stop.
# Output: <Out>\<Name>.mp4 and <Out>\<Name>.json ({ video, fps, steps: [{ id, start, end }] }) — the same shape the
# desktop harness writes, so  node demo-video-i18n.mjs  cuts and narrates it as chapter 0.
param(
    [Parameter(Mandatory)] [string] $Out,
    [string] $Name = '00-setup',
    [int] $Fps = 15,
    [string] $Ffmpeg = $(if ($env:FFMPEG) { $env:FFMPEG } else { 'ffmpeg' })
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Out | Out-Null
$mp4 = Join-Path $Out "$Name.mp4"
$json = Join-Path $Out "$Name.json"
if (Test-Path $mp4) { Remove-Item $mp4 }

# -y overwrite; gdigrab = the Windows desktop; even dimensions for yuv420p; stdin stays open so 'q' can stop ffmpeg cleanly
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $Ffmpeg
$psi.Arguments = "-y -loglevel error -f gdigrab -framerate $Fps -i desktop -vf `"crop=trunc(iw/2)*2:trunc(ih/2)*2`" -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -movflags +faststart `"$mp4`""
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$proc = [System.Diagnostics.Process]::Start($psi)
$clock = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host "Recording the screen to $mp4 ... ENTER = new step (then the step id), q + ENTER = stop."

$steps = New-Object System.Collections.ArrayList
$current = $null
while ($true) {
    $line = Read-Host 'step id'
    $t = [Math]::Round($clock.Elapsed.TotalSeconds, 3)
    if ($line -eq 'q') { break }
    if ($current) { $current.end = $t }
    $id = if ($line) { $line.Trim() } elseif ($current) { $current.id } else { 'step-1' }
    $current = [ordered]@{ id = $id; start = $t; end = $null }
    [void] $steps.Add($current)
    Write-Host ("  {0,8:0.0}s  {1}" -f $t, $id)
}
$end = [Math]::Round($clock.Elapsed.TotalSeconds, 3)
if ($current) { $current.end = $end }
$proc.StandardInput.WriteLine('q')
$proc.WaitForExit(15000) | Out-Null
if (-not $proc.HasExited) { $proc.Kill() }

[ordered]@{ video = "$Name.mp4"; fps = $Fps; steps = @($steps | ForEach-Object { [ordered]@{ id = $_.id; step = $_.id; start = $_.start; end = $_.end; included = $true } }) } |
    ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 $json
Write-Host "wrote $json ($($steps.Count) steps, $([Math]::Round($end))s)"
