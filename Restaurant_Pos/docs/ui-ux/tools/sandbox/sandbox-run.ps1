# Runs INSIDE Windows Sandbox (LogonCommand of setup.wsb, hidden window, sandbox user = administrator, no UAC prompt).
# Installs PostgreSQL, the HotelPOS server and a terminal exactly as docs/ops/01 says, driving the real wizards with
# UI Automation and a visible mouse, while ffmpeg records the sandbox screen. Writes C:\Setup\out\00-setup.mp4 and
# 00-setup.json (step marks for demo-video-i18n.mjs), run.log and a screenshot per step for diagnosis, then done.txt and
# shuts the sandbox down. Mapped folder: <repo>\artifacts\sandbox-setup -> C:\Setup (in\ = installers + ffmpeg, out\ = results).
$ErrorActionPreference = 'Continue'
$In = 'C:\Setup\in'; $Out = 'C:\Setup\out'; $Rec = 'C:\rec'
New-Item -ItemType Directory -Force $Out, "$Out\shots", $Rec | Out-Null
$LogFile = "$Out\run.log"
function Log([string] $m) { $line = "{0} {1}" -f (Get-Date).ToString('HH:mm:ss'), $m; Add-Content -Path $LogFile -Value $line; }

$DbPassword = 'HotelPos@123'      # the hotelpos database user (PostgreSQL itself comes inside the server package)
$AdminPassword = 'Admin@123'      # first admin password (must be changed at the first sign-in)
$NewAdminPassword = 'Admin@2026'

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Native {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
}
"@
$AE = [System.Windows.Automation.AutomationElement]
$Root = $AE::RootElement
$Scope = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]

# ---------------------------------------------------------------- screen, screenshots, recording
# Edge updates itself in the sandbox and then pops a "What's new" window over whatever is on screen: stop the updater first.
try {
    Get-Service -Name 'edgeupdate', 'edgeupdatem', 'MicrosoftEdgeElevationService' -ErrorAction SilentlyContinue | ForEach-Object { Stop-Service $_ -Force -ErrorAction SilentlyContinue; Set-Service $_.Name -StartupType Disabled -ErrorAction SilentlyContinue }
    Get-ScheduledTask -TaskName 'MicrosoftEdgeUpdate*' -ErrorAction SilentlyContinue | Disable-ScheduledTask -ErrorAction SilentlyContinue | Out-Null
    Get-Process -Name 'MicrosoftEdgeUpdate', 'msedge' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    foreach ($dir in @("${env:ProgramFiles(x86)}\Microsoft\EdgeUpdate", "$env:LOCALAPPDATA\Microsoft\EdgeUpdate")) {
        if (Test-Path $dir) { Rename-Item $dir ($dir + '.off') -ErrorAction SilentlyContinue }
    }
    Log 'Edge updater disabled'
} catch { Log "Edge updater: $_" }
function Close-Edge() { Get-Process -Name 'msedge', 'MicrosoftEdgeUpdate' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }

$Screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$W = [int]($Screen.Width / 2) * 2; $H = [int]($Screen.Height / 2) * 2
Log "screen $($Screen.Width)x$($Screen.Height), recording ${W}x${H}"
$shotNo = 0
function Shot([string] $name) {
    $script:shotNo++
    try {
        $bmp = New-Object System.Drawing.Bitmap $Screen.Width, $Screen.Height
        $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size); $g.Dispose()
        $bmp.Save(("{0}\shots\{1:00}-{2}.png" -f $Out, $script:shotNo, $name)); $bmp.Dispose()
    } catch { Log "shot failed: $_" }
}

$ffmpeg = New-Object System.Diagnostics.Process
$ffmpeg.StartInfo.FileName = "$In\ffmpeg.exe"
$ffmpeg.StartInfo.Arguments = "-y -loglevel error -f gdigrab -framerate 15 -draw_mouse 1 -video_size ${W}x${H} -i desktop -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -movflags +faststart `"$Rec\00-setup.mp4`""
$ffmpeg.StartInfo.UseShellExecute = $false
$ffmpeg.StartInfo.RedirectStandardInput = $true
$ffmpeg.StartInfo.CreateNoWindow = $true
[void] $ffmpeg.Start()
Start-Sleep -Milliseconds 1500
$Clock = [System.Diagnostics.Stopwatch]::StartNew()
Log "recording started"

$Steps = New-Object System.Collections.ArrayList
$Current = $null
function Step([string] $id) {
    $t = [Math]::Round($Clock.Elapsed.TotalSeconds, 3)
    if ($script:Current) { $script:Current.end = $t }
    $script:Current = [ordered]@{ id = $id; step = $id; start = $t; end = $null; included = $true }
    [void] $Steps.Add($script:Current)
    Log "STEP $id at ${t}s"
}
function EndSteps() { if ($script:Current) { $script:Current.end = [Math]::Round($Clock.Elapsed.TotalSeconds, 3) } }

# ---------------------------------------------------------------- UI Automation helpers
# The largest visible top-level window whose title matches (an app may own several windows with the same title).
function Wait-Window([string] $titleRegex, [int] $timeoutSec = 60) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
        $best = $null; $bestArea = -1
        foreach ($w in $Root.FindAll($Scope::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
            if ($w.Current.Name -match $titleRegex -and -not $w.Current.IsOffscreen) {
                $r = $w.Current.BoundingRectangle; $area = $r.Width * $r.Height
                if ($area -gt $bestArea) { $best = $w; $bestArea = $area }
            }
        }
        if ($best) { return $best }
        Start-Sleep -Milliseconds 500
    }
    Log "window /$titleRegex/ not found in ${timeoutSec}s"; Shot "no-window"
    return $null
}
function Find-Elements($parent, $controlType, [string] $nameRegex = '', [switch] $IncludeOffscreen) {
    if ($null -eq $parent) { return @() }
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $controlType)
    $all = $parent.FindAll($Scope::Descendants, $cond)
    $list = @()
    foreach ($e in $all) { if (($nameRegex -eq '' -or $e.Current.Name -match $nameRegex) -and ($IncludeOffscreen -or -not $e.Current.IsOffscreen)) { $list += $e } }
    return $list
}
# Logs what UI Automation sees in a window (diagnosis when a control is not found).
function Dump-Tree($win, [string] $tag, [int] $max = 60) {
    if ($null -eq $win) { Log "dump ${tag}: no window"; return }
    $all = $win.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $lines = @(); $i = 0
    foreach ($e in $all) {
        if ($i++ -ge $max) { break }
        $r = $e.Current.BoundingRectangle
        $geo = if ($r.IsEmpty -or [double]::IsInfinity($r.X)) { 'no-rect' } else { "@$([int]$r.X),$([int]$r.Y) $([int]$r.Width)x$([int]$r.Height)" }
        $lines += "$($e.Current.ControlType.ProgrammaticName -replace 'ControlType.','') '$($e.Current.Name)' id=$($e.Current.AutomationId) $geo$(if ($e.Current.IsOffscreen) { ' off' })"
    }
    Log ("dump $tag ($($all.Count) elements): " + ($lines -join ' || '))
}
# Scrolls the window content down (mouse wheel over its centre) so controls below the fold come on screen.
function Scroll-Down($win, [int] $notches = 6) {
    if ($null -eq $win) { return }
    $r = $win.Current.BoundingRectangle
    Move-Mouse ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
    $down = [BitConverter]::ToUInt32([BitConverter]::GetBytes([int32](-120)), 0)   # WHEEL_DELTA backwards as the DWORD mouse_event wants
    for ($i = 0; $i -lt $notches; $i++) { [Native]::mouse_event(0x0800, 0, 0, $down, [UIntPtr]::Zero); Start-Sleep -Milliseconds 80 }
    Start-Sleep -Milliseconds 500
}
# Clicks just below a label (WPF TextBlock) to focus the input under it: a fallback when the input itself is not exposed.
function Click-BelowLabel($win, [string] $labelRegex, [int] $dy = 40) {
    $t = Wait-Element $win $CT::Text $labelRegex 5
    if ($null -eq $t) { return $false }
    $r = $t.Current.BoundingRectangle
    Click-At ([int]($r.X + [Math]::Min(120, $r.Width / 2))) ([int]($r.Bottom + $dy)) "below label '$($t.Current.Name)'"
    return $true
}
function Wait-Element($parent, $controlType, [string] $nameRegex = '', [int] $timeoutSec = 30) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
        $found = @(Find-Elements $parent $controlType $nameRegex)
        if ($found.Count -gt 0) { return $found[0] }
        Start-Sleep -Milliseconds 400
    }
    Log "element $($controlType.ProgrammaticName) /$nameRegex/ not found in ${timeoutSec}s"; Shot "no-element"
    return $null
}
function Move-Mouse([int] $x, [int] $y) {
    $p = [System.Windows.Forms.Cursor]::Position
    for ($i = 1; $i -le 14; $i++) {
        $t = $i / 14.0; $e = $t * $t * (3 - 2 * $t)  # ease in-out
        [void] [Native]::SetCursorPos([int]($p.X + ($x - $p.X) * $e), [int]($p.Y + ($y - $p.Y) * $e))
        Start-Sleep -Milliseconds 18
    }
}
function Click-Element($el, [string] $what = '') {
    if ($null -eq $el) { Log "click skipped ($what): no element"; return $false }
    try { $hwnd = [IntPtr] $el.Current.NativeWindowHandle; if ($hwnd -ne [IntPtr]::Zero) { [void] [Native]::SetForegroundWindow($hwnd) } } catch { }
    $r = $el.Current.BoundingRectangle
    $x = [int]($r.X + $r.Width / 2); $y = [int]($r.Y + $r.Height / 2)
    # Under the taskbar or off the screen a mouse click would hit something else (seen: the Edge taskbar icon): invoke instead.
    if ($y -gt $Screen.Height - 60 -or $x -gt $Screen.Width - 2 -or $y -lt 0 -or $x -lt 0) {
        try {
            $ip = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern); $ip.Invoke()
            Log "invoked $what '$($el.Current.Name)' (centre $x,$y is off the usable screen)"; Start-Sleep -Milliseconds 350; return $true
        } catch { Log "invoke failed for $what ($_); clicking anyway" }
    }
    Move-Mouse $x $y
    Start-Sleep -Milliseconds 220
    [Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 70; [Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 350
    Log "clicked $what '$($el.Current.Name)'"
    return $true
}
function Click-Button($win, [string] $nameRegex, [int] $timeoutSec = 30, [string] $fallbackKeys = '') {
    $b = Wait-Element $win $CT::Button $nameRegex $timeoutSec
    if ($null -eq $b -and $fallbackKeys) {
        try { if ($win) { [void] [Native]::SetForegroundWindow([IntPtr] $win.Current.NativeWindowHandle) } } catch { }
        [System.Windows.Forms.SendKeys]::SendWait($fallbackKeys); Log "button /$nameRegex/ by keys $fallbackKeys"; Start-Sleep -Milliseconds 500
        return $true
    }
    return (Click-Element $b "button")
}
function Type-Fields($win, [string[]] $values, [int] $firstIndex, [int] $expected, [string] $what) {
    # Fills $values into the window's Edit controls starting at $firstIndex; when UI Automation shows fewer than
    # $expected edits (controls not exposed), the first field is assumed focused and the values are typed with Tab.
    $edits = @(Find-Elements $win $CT::Edit)
    Log "$what edits: $($edits.Count)"
    if ($edits.Count -ge $expected) {
        for ($i = 0; $i -lt $values.Count; $i++) { Fill-Edit $edits[$firstIndex + $i] $values[$i] "$what $i" }
    } else {
        try { if ($win) { [void] [Native]::SetForegroundWindow([IntPtr] $win.Current.NativeWindowHandle) } } catch { }
        for ($t = 0; $t -lt $firstIndex; $t++) { [System.Windows.Forms.SendKeys]::SendWait('{TAB}'); Start-Sleep -Milliseconds 150 }
        for ($i = 0; $i -lt $values.Count; $i++) { if ($i -gt 0) { [System.Windows.Forms.SendKeys]::SendWait('{TAB}'); Start-Sleep -Milliseconds 150 }; Type-Text $values[$i] }
        Log "$what typed blind"
    }
}
function Type-Text([string] $text, [int] $perCharMs = 45) {
    foreach ($ch in $text.ToCharArray()) {
        $s = [string] $ch
        if ('+^%~(){}[]'.Contains($s)) { $s = '{' + $s + '}' }
        [System.Windows.Forms.SendKeys]::SendWait($s)
        Start-Sleep -Milliseconds $perCharMs
    }
}
function Fill-Edit($el, [string] $text, [string] $what = '') {
    if (-not (Click-Element $el "edit $what")) { return }
    [System.Windows.Forms.SendKeys]::SendWait('^a'); Start-Sleep -Milliseconds 80
    Type-Text $text
    Start-Sleep -Milliseconds 300
}
function Page-Text($win) {
    if ($null -eq $win) { return '' }
    $names = @()
    foreach ($e in (Find-Elements $win $CT::Text)) { $names += $e.Current.Name }
    return ($names -join ' | ')
}
function Wait-Text($win, [string] $regex, [int] $timeoutSec = 60) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
        if ((Page-Text $win) -match $regex) { return $true }
        Start-Sleep -Milliseconds 700
    }
    Log "text /$regex/ not seen in ${timeoutSec}s: $(Page-Text $win)"; return $false
}

# A plain mouse click at screen coordinates (used for controls that UI Automation does not expose).
function Click-At([int] $x, [int] $y, [string] $what = '') {
    Move-Mouse $x $y; Start-Sleep -Milliseconds 220
    [Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 70; [Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 350; Log "clicked $what at $x,$y"
}

try {
Shot 'desktop'
# The installers are read from the mapped host folder, which the sandbox reads slowly: copy them to a local folder first.
foreach ($f in 'HotelPOS-Server-Setup-0.1.1.exe', 'HotelPOS-Client-Setup-0.1.1.exe') { Copy-Item "$In\$f" "$Rec\$f" -Force }
Log 'installers copied locally'

# ================================================================ 2. HotelPOS Server Setup: database pages
Step 'server-setup'
Start-Process "$Rec\HotelPOS-Server-Setup-0.1.1.exe" -ArgumentList "/LOG=$Out\diag-server-setup.log"
$srv = Wait-Window 'Setup - HotelPOS Server' 180
if (-not $srv) { Log "server setup log tail: $((Get-Content "$Out\diag-server-setup.log" -ErrorAction SilentlyContinue | Select-Object -Last 5) -join ' | ')" }
Start-Sleep -Seconds 3; Shot 'srv-welcome'
# Inno Setup exposes none of its controls to UI Automation either: pages are driven by their accelerators (Alt+N Next,
# Alt+I Install, Alt+F Finish) and the fields are typed into the focused first edit (Tab moves on). Inno 6 shows no
# Welcome page: the first page is the destination folder, then the custom pages of server.iss.
function Inno-Keys([string] $keys, [string] $what) {
    $w = Wait-Window 'Setup - HotelPOS' 10
    if ($w) { try { [void] [Native]::SetForegroundWindow([IntPtr] $w.Current.NativeWindowHandle) } catch { } }
    [System.Windows.Forms.SendKeys]::SendWait($keys); Log "$what ($keys)"; Start-Sleep -Milliseconds 600
}
function Inno-Type([string[]] $values, [int] $tabsFirst = 0) {
    $w = Wait-Window 'Setup - HotelPOS' 10
    if ($w) { try { [void] [Native]::SetForegroundWindow([IntPtr] $w.Current.NativeWindowHandle) } catch { } }
    for ($t = 0; $t -lt $tabsFirst; $t++) { [System.Windows.Forms.SendKeys]::SendWait('{TAB}'); Start-Sleep -Milliseconds 200 }
    for ($i = 0; $i -lt $values.Count; $i++) { if ($i -gt 0) { [System.Windows.Forms.SendKeys]::SendWait('{TAB}'); Start-Sleep -Milliseconds 200 }; Type-Text $values[$i] }
    Start-Sleep -Milliseconds 400
}
# A message box from the installer ("Setup" with an OK button) is logged, captured and dismissed.
function Dismiss-MessageBox([string] $tag) {
    # Inno's message box (class #32770, title "Setup") is not listed by UI Automation at all: find it through Win32,
    # capture the screen for the log and press Enter (OK is the default button).
    $hwnd = [Native]::FindWindow('#32770', 'Setup')
    if ($hwnd -ne [IntPtr]::Zero) {
        Log "installer message box ($tag) found via Win32"; Shot "message-$tag"
        [void] [Native]::SetForegroundWindow($hwnd); Start-Sleep -Milliseconds 300
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}'); Start-Sleep -Seconds 1
        return $true
    }
    foreach ($w in $Root.FindAll($Scope::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($w.Current.Name -ne 'Setup' -and $w.Current.Name -notlike 'Setup - HotelPOS*') { continue }
        $ok = @(Find-Elements $w $CT::Button '^OK$')
        if ($ok.Count -gt 0) {
            Log "installer message ($tag): $(Page-Text $w)"; Shot "message-$tag"
            if (-not (Click-Element $ok[0] 'OK')) { [System.Windows.Forms.SendKeys]::SendWait('{ENTER}') }
            Start-Sleep -Seconds 1
            return $true
        }
    }
    return $false
}

Shot 'srv-destination'
Inno-Keys '%n' 'Destination: Next'                   # -> PostgreSQL: bundled (default) or existing
Start-Sleep -Seconds 3; Shot 'srv-postgres-choice'
Inno-Keys '%n' 'PostgreSQL choice: Next'             # -> Database
Start-Sleep -Seconds 2; Shot 'srv-database'
Inno-Type @($DbPassword) 4                           # Server, Port, Database, User, [Password], API port
Start-Sleep -Seconds 1; Shot 'srv-database-filled'

# ================================================================ 3. Administrator + HTTPS
Step 'admin-and-https'
Inno-Keys '%n' 'Database: Next'                      # -> Administrator (the superuser page is skipped: Setup generates it)
Start-Sleep -Seconds 2; [void] (Dismiss-MessageBox 'database')
Inno-Type @($AdminPassword, $AdminPassword) 0
Shot 'srv-admin'
Start-Sleep -Seconds 1
Inno-Keys '%n' 'Administrator: Next'                 # -> HTTPS certificate (leave empty)
Start-Sleep -Seconds 2; [void] (Dismiss-MessageBox 'admin')
Shot 'srv-https'
Start-Sleep -Seconds 3

# ================================================================ 4. Install, finish, health check
Step 'finish-and-check'
Inno-Keys '%n' 'HTTPS: Next'                         # -> Ready to install
Start-Sleep -Seconds 2; [void] (Dismiss-MessageBox 'https'); Shot 'srv-ready'
Inno-Keys '%i' 'Ready: Install'
Log 'server installing'
# the installer configures the service and waits until the API is healthy before it shows the Finished page.
# Watch three things: the API health, a message box, and the installer process itself; log and shoot while waiting.
function Top-Windows() { $n = @(); foreach ($w in $Root.FindAll($Scope::Children, [System.Windows.Automation.Condition]::TrueCondition)) { if ($w.Current.Name) { $n += $w.Current.Name } }; return ($n -join ' | ') }
function Health-Ok() { try { $r = Invoke-WebRequest -UseBasicParsing -Uri 'http://localhost:5000/health' -TimeoutSec 3; return ($r.Content -match '"status":"Healthy"') } catch { return $false } }
$healthy = $false; $sw = [System.Diagnostics.Stopwatch]::StartNew(); $lastShot = -60; $msg = $false
while ($sw.Elapsed.TotalMinutes -lt 8) {
    if (Health-Ok) { $healthy = $true; break }
    if (Dismiss-MessageBox 'install') { $msg = $true; break }
    $setupProc = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'HotelPOS-Server-Setup*' }
    if (-not $setupProc) { Log 'installer process gone'; break }
    if ($sw.Elapsed.TotalSeconds - $lastShot -ge 60) { $lastShot = $sw.Elapsed.TotalSeconds; Shot 'srv-installing'; Log "installing... windows: $(Top-Windows)" }
    Start-Sleep -Seconds 3
}
Log "health during install: $healthy after $([int]$sw.Elapsed.TotalSeconds)s (message box: $msg); service: $((Get-Service HotelPOSApi -ErrorAction SilentlyContinue).Status)"
Start-Sleep -Seconds 8
Shot 'srv-finished'
Log "windows now: $(Top-Windows)"
Inno-Keys '%f' 'Finished: Finish'
Start-Sleep -Seconds 2
$healthy = $false
for ($i = 0; $i -lt 30 -and -not $healthy; $i++) { try { $h = Invoke-RestMethod -Uri 'http://localhost:5000/health' -TimeoutSec 3; $healthy = ($h.data.status -eq 'Healthy'); } catch { Start-Sleep -Seconds 2 } }
Log "health: $healthy"
# Show the health check in a console (Edge is avoided: in the sandbox it updates itself and pops a page over the app).
$cmd = "`$host.UI.RawUI.WindowTitle = 'HotelPOS server health'; Write-Host ''; Write-Host '  PS> Invoke-RestMethod http://localhost:5000/health' -ForegroundColor Yellow; Write-Host ''; (Invoke-RestMethod http://localhost:5000/health).data | ConvertTo-Json -Depth 3; Write-Host ''; Write-Host '  The HotelPOS API service is running and answers Healthy.' -ForegroundColor Green"
Start-Process 'powershell.exe' -ArgumentList '-NoExit', '-NoProfile', '-Command', $cmd
$con = Wait-Window 'HotelPOS server health|Windows PowerShell' 20
if ($con) { try { [void] [Native]::ShowWindow([IntPtr] $con.Current.NativeWindowHandle, 3) } catch { } }
Start-Sleep -Seconds 8; Shot 'health'
Get-Process -Name 'powershell' -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like 'HotelPOS server health*' -or $_.MainWindowTitle -like 'Windows PowerShell*' } | Stop-Process -Force -ErrorAction SilentlyContinue
Close-Edge
Start-Sleep -Seconds 1

# ================================================================ 5. Terminal: client setup + first run
Step 'install-terminals'
Start-Process "$Rec\HotelPOS-Client-Setup-0.1.1.exe"
$cli = Wait-Window 'Setup - HotelPOS' 180
Start-Sleep -Seconds 3; Shot 'cli-destination'
Inno-Keys '%n' 'Client destination: Next'            # -> Ready to install
Start-Sleep -Seconds 2; Shot 'cli-ready'
Inno-Keys '%i' 'Client ready: Install'
$exe = 'C:\Program Files\HotelPOS\Client\HotelPOS.Desktop.exe'
$sw = [System.Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalMinutes -lt 5 -and -not (Test-Path $exe)) { Start-Sleep -Seconds 2 }
Log "client exe present: $(Test-Path $exe) after $([int]$sw.Elapsed.TotalSeconds)s"
Start-Sleep -Seconds 6
Shot 'cli-finished'
Inno-Keys '%f' 'Client finished: Finish'             # "Start HotelPOS" is ticked: the app starts
Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like 'Stack Builder*' } | Stop-Process -Force -ErrorAction SilentlyContinue
Close-Edge
$app = Wait-Window '^HotelPOS' 90
Start-Sleep -Seconds 5
if ($app) { try { $h = [IntPtr] $app.Current.NativeWindowHandle; [void] [Native]::ShowWindow($h, 3); [void] [Native]::SetForegroundWindow($h) } catch { } }
Start-Sleep -Seconds 2; Shot 'app-first-run'
$app = Wait-Window '^HotelPOS' 10
Dump-Tree $app 'first-run'
$edits = @(Find-Elements $app $CT::Edit)
Log "first-run edits: $($edits.Count)"
if ($edits.Count -ge 1) { Fill-Edit $edits[0] 'http://localhost:5000' 'server address' }
elseif (Click-BelowLabel $app '^Server address$') { [System.Windows.Forms.SendKeys]::SendWait('^a'); Type-Text 'http://localhost:5000' }
if (-not (Click-Button $app 'Test connection' 8)) { Log 'Test connection button not found' }
Start-Sleep -Seconds 3; Shot 'app-test-connection'
$edits = @(Find-Elements $app $CT::Edit)
if ($edits.Count -ge 2) { Fill-Edit $edits[1] 'BILLING-01' 'terminal name' }
elseif (Click-BelowLabel $app '^Terminal name$') { [System.Windows.Forms.SendKeys]::SendWait('^a'); Type-Text 'BILLING-01' }
$combo = @(Find-Elements $app $CT::ComboBox)
if ($combo.Count -ge 1) { [void] (Click-Element $combo[0] 'terminal type') }
elseif (-not (Click-BelowLabel $app '^Terminal type$')) { Log 'terminal type combo not found' }
Start-Sleep -Milliseconds 800
$item = Wait-Element $Root $CT::ListItem '^Billing$' 5
if ($item) { [void] (Click-Element $item 'Billing') } else { [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Log 'Billing item not found' }
Start-Sleep -Seconds 1; Shot 'app-first-run-filled'
Scroll-Down $app 20
Shot 'app-first-run-scrolled'
if (-not (Click-Button $app '^Save$' 8)) { [System.Windows.Forms.SendKeys]::SendWait('{ENTER}'); Log 'Save by Enter' }
Start-Sleep -Seconds 4; Shot 'app-sign-in'

# ================================================================ 6. First sign-in: admin, forced password change, settings, backup
Step 'first-sign-in'
Close-Edge
$app = Wait-Window '^HotelPOS' 30
try { [void] [Native]::SetForegroundWindow([IntPtr] $app.Current.NativeWindowHandle) } catch { }
Dump-Tree $app 'sign-in' 40
# The button right below an edit (the primary action of a card) when the button's name is not exposed.
function Click-ButtonBelow($win, $edit, [string] $what) {
    if ($null -eq $edit) { return $false }
    $er = $edit.Current.BoundingRectangle
    $best = $null; $bestDy = 1e9
    foreach ($b in (Find-Elements $win $CT::Button)) { $br = $b.Current.BoundingRectangle; $dy = $br.Y - $er.Bottom; if ($dy -ge 0 -and $dy -lt $bestDy -and $br.Right -gt $er.X -and $br.X -lt $er.Right) { $best = $b; $bestDy = $dy } }
    if ($best) { return (Click-Element $best $what) }
    return $false
}
$edits = @(Find-Elements $app $CT::Edit)
Log "sign-in edits: $($edits.Count)"
if ($edits.Count -ge 2) { Fill-Edit $edits[0] 'admin' 'username'; Fill-Edit $edits[1] $AdminPassword 'password' }
else { if (Click-BelowLabel $app '^Username$') { Type-Text 'admin' }; if (Click-BelowLabel $app '^Password$') { Type-Text $AdminPassword } }
if (-not (Click-Button $app '^Sign in$' 5)) {
    if (-not (Click-ButtonBelow $app $(if ($edits.Count -ge 2) { $edits[1] } else { $null }) 'Sign in (by position)')) { [System.Windows.Forms.SendKeys]::SendWait('{ENTER}'); Log 'Sign in by Enter' }
}
Start-Sleep -Seconds 5; Shot 'app-change-password'
Close-Edge
$app = Wait-Window '^HotelPOS' 10
Dump-Tree $app 'change-password' 40
$edits = @(Find-Elements $app $CT::Edit)
Log "change-password edits: $($edits.Count)"
if ($edits.Count -ge 3) { Fill-Edit $edits[0] $AdminPassword 'current'; Fill-Edit $edits[1] $NewAdminPassword 'new'; Fill-Edit $edits[2] $NewAdminPassword 'confirm' }
else { if (Click-BelowLabel $app '^Current password$') { Type-Text $AdminPassword; [System.Windows.Forms.SendKeys]::SendWait('{TAB}'); Type-Text $NewAdminPassword; [System.Windows.Forms.SendKeys]::SendWait('{TAB}'); Type-Text $NewAdminPassword } }
if (-not (Click-Button $app 'Save password' 5)) {
    if (-not (Click-ButtonBelow $app $(if ($edits.Count -ge 3) { $edits[2] } else { $null }) 'Save password (by position)')) { [System.Windows.Forms.SendKeys]::SendWait('{ENTER}'); Log 'Save password by Enter' }
}
Start-Sleep -Seconds 7; Shot 'app-dashboard'
# Restaurant settings and Backup through the command palette (Ctrl+K)
[System.Windows.Forms.SendKeys]::SendWait('^k'); Start-Sleep -Milliseconds 900; Type-Text 'Settings'; Start-Sleep -Milliseconds 900; [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
Start-Sleep -Seconds 5; Shot 'app-settings'
[System.Windows.Forms.SendKeys]::SendWait('^k'); Start-Sleep -Milliseconds 900; Type-Text 'Backup'; Start-Sleep -Milliseconds 900; [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
Start-Sleep -Seconds 4; Shot 'app-backup'
[void] (Click-Button $app 'BACKUP NOW' 10)
Start-Sleep -Seconds 8; Shot 'app-backup-done'
Start-Sleep -Seconds 3
}
catch { Log "FAILED: $($_.Exception.Message) at $($_.InvocationInfo.ScriptLineNumber)"; Shot 'failed' }
finally {
    EndSteps
    Start-Sleep -Seconds 2
    try { $ffmpeg.StandardInput.Write('q'); $ffmpeg.StandardInput.Flush() } catch { }
    if (-not $ffmpeg.WaitForExit(20000)) { $ffmpeg.Kill() }
    Log "recording stopped at $([Math]::Round($Clock.Elapsed.TotalSeconds))s"
    [ordered]@{ video = '00-setup.mp4'; fps = 15; steps = @($Steps) } | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 "$Out\00-setup.json"
    Copy-Item "$Rec\00-setup.mp4" "$Out\00-setup.mp4" -Force
    # diagnostics for the host: installer log, API logs, services, processes
    try {
        New-Item -ItemType Directory -Force "$Out\diag" | Out-Null
        Copy-Item 'C:\Program Files\HotelPOS\Server\configure-server.log' "$Out\diag\" -ErrorAction SilentlyContinue
        Copy-Item 'C:\ProgramData\HotelPOS\Api\logs\*' "$Out\diag\" -ErrorAction SilentlyContinue
        Copy-Item 'C:\ProgramData\HotelPOS\pg\*.log' "$Out\diag\" -ErrorAction SilentlyContinue
        Copy-Item 'C:\ProgramData\HotelPOS\pg\data\log\*' "$Out\diag\" -ErrorAction SilentlyContinue
        Get-ChildItem 'C:\ProgramData\HotelPOS' -Recurse -Depth 2 -ErrorAction SilentlyContinue | Select-Object FullName, Length | Out-String | Set-Content "$Out\diag\programdata.txt"
        Get-Service -Name 'HotelPOSApi', 'postgresql*' -ErrorAction SilentlyContinue | Format-Table -AutoSize | Out-String | Set-Content "$Out\diag\services.txt"
        Get-Process | Sort-Object Name | Select-Object Name, Id, MainWindowTitle | Format-Table -AutoSize | Out-String | Set-Content "$Out\diag\processes.txt"
        Get-ChildItem 'C:\Program Files\HotelPOS' -Recurse -Depth 1 -ErrorAction SilentlyContinue | Select-Object FullName | Out-String | Set-Content "$Out\diag\program-files.txt"
    } catch { Log "diag failed: $_" }
    Log "done"
    Set-Content "$Out\done.txt" (Get-Date).ToString('s')
    Start-Sleep -Seconds 3
    shutdown.exe /s /t 2 /f
}
