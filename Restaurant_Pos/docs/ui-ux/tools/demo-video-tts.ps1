# Speaks each line of a JSON array [{ id, text }] to <OutDir>\<id>.wav with a Windows voice (offline, System.Speech).
# Used by demo-video.mjs; run alone for a voice test:  .\demo-video-tts.ps1 -Lines lines.json -OutDir wav -Voice 'Microsoft Zira Desktop'
param(
    [Parameter(Mandatory)] [string] $Lines,
    [Parameter(Mandatory)] [string] $OutDir,
    [string] $Voice = 'Microsoft Zira Desktop',
    [int] $Rate = 0
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
New-Item -ItemType Directory -Force $OutDir | Out-Null
$items = Get-Content -Raw -Encoding UTF8 $Lines | ConvertFrom-Json
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$synth.SelectVoice($Voice)
$synth.Rate = $Rate
$done = 0
foreach ($item in $items) {
    $path = Join-Path $OutDir "$($item.id).wav"
    if (Test-Path $path) { continue }
    $synth.SetOutputToWaveFile($path)
    $synth.Speak([string] $item.text)
    $synth.SetOutputToNull()
    $done++
}
$synth.Dispose()
Write-Host "spoke $done of $($items.Count) lines with $Voice"
