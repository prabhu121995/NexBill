<#
  Starts the NexBill kiosk: the till on the primary monitor and the customer
  display fullscreen on the secondary one.

  Monitors, browser location and window geometry are all resolved at runtime,
  so nothing is tied to a particular machine. Works with any monitor count,
  resolution or arrangement.
#>
[CmdletBinding()]
param(
    [string]$Url = 'http://localhost:5000',
    [int]$StartTimeoutSeconds = 90,

    # Leave a running till alone and open another one alongside it. Only sane on a
    # multi-monitor desk being used as two stations; by default a relaunch replaces
    # whatever is already open.
    [switch]$AllowSecondInstance,

    # Launch and exit, instead of staying to tear the pair down together. For
    # development, where a launcher that holds the terminal for the whole shift is
    # just in the way. A till installed with this would leave the customer display
    # on screen after the till window goes.
    [switch]$NoWatch
)

Add-Type -AssemblyName System.Windows.Forms

# A trailing slash would build "https://host//customer-display", which breaks path
# routing under a sub-application. Normalise once, here.
$Url = $Url.TrimEnd('/')

# Windows PowerShell 5.1 can still negotiate TLS 1.0 by default on some builds,
# which a load balancer will refuse outright - the reachability probe below
# would then report the site as down when it is perfectly healthy.
try {
    [Net.ServicePointManager]::SecurityProtocol =
        [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls11
} catch { }

# Shared with Kiosk.Services.KioskSession, which reads these pid files so the
# power button on the till can actually close these windows. Keep both in step.
$profileRoot = Join-Path $env:LOCALAPPDATA 'KioskPOS'
$sessionDir = Join-Path $profileRoot 'session'
$null = New-Item -ItemType Directory -Force -Path $sessionDir

function Get-LiveWindow {
    <#
      Returns the process recorded in a session pid file if it is still a running
      browser, else $null - clearing the file on the way, so a stale pid never
      makes the app promise a clean close it cannot deliver.
    #>
    param([string]$Name)

    $file = Join-Path $sessionDir $Name
    if (-not (Test-Path $file)) { return $null }

    $pidText = (Get-Content $file -ErrorAction SilentlyContinue | Select-Object -First 1)
    $browserPid = 0
    if ([int]::TryParse(("$pidText").Trim(), [ref]$browserPid)) {
        $proc = Get-Process -Id $browserPid -ErrorAction SilentlyContinue
        # A pid is reused as soon as its process ends, so check the name too.
        if ($proc -and $proc.ProcessName -in @('chrome', 'msedge')) { return $proc }
    }

    Remove-Item $file -Force -ErrorAction SilentlyContinue
    return $null
}

function Set-SessionWindow {
    param([string]$Name, [System.Diagnostics.Process]$Process)
    if ($Process) { Set-Content -Path (Join-Path $sessionDir $Name) -Value $Process.Id -Encoding ascii }
}

function Clear-SessionWindow {
    <#
      Forgets a window, but only if the pid file still names the process we mean.
      A relaunch that replaced this instance has already written its own pid
      there, and deleting that would strip the NEW till of the only way the power
      button has to close it.
    #>
    param([string]$Name, [int]$ExpectedPid)

    $file = Join-Path $sessionDir $Name
    $raw = (Get-Content $file -ErrorAction SilentlyContinue | Select-Object -First 1)
    $recorded = 0
    if ([int]::TryParse(("$raw").Trim(), [ref]$recorded) -and $recorded -eq $ExpectedPid) {
        Remove-Item $file -Force -ErrorAction SilentlyContinue
    }
}

function Close-KioskWindow {
    <#
      Shuts one recorded window down. Asks first with WM_CLOSE - the same thing
      Alt+F4 sends, which kiosk mode honours - because a killed Chrome looks like a
      crash and comes back restoring the crashed session, SESSION COOKIES INCLUDED.
      That is what made a relaunched till reopen already signed in. Killing is the
      fallback for a window that will not go.
    #>
    param(
        [System.Diagnostics.Process]$Process,
        [string]$Label,
        # Prefixes the log lines. The same close is used to replace a previous
        # instance and to tear this one down, and the two read very differently in
        # a log.
        [string]$Context = 'Replacing'
    )

    Write-Host "$($Context): closing $Label (pid $($Process.Id))..."
    try { $null = $Process.CloseMainWindow() } catch { }

    if (-not $Process.WaitForExit(5000)) {
        Write-Warning "The $Label ignored the close request; killing it."
        # Windows PowerShell is .NET Framework, where Process.Kill() takes no
        # arguments, so taskkill does the tree.
        & taskkill.exe /PID $Process.Id /T /F 2>&1 | Out-Null
        $null = $Process.WaitForExit(5000)
    }
}

# ---------------------------------------------------------------- replace previous
# A relaunch REPLACES the running till rather than refusing or stacking a second
# one on the same monitor. Two tills would fight over the same screens and the same
# customer display, and the common reason to run this again is wanting a clean
# start - closing the browser properly also ends the shift, so the new window lands
# on the login screen instead of the last cashier's till.
if ($AllowSecondInstance) {
    Write-Host 'Instance : -AllowSecondInstance given, leaving any running till alone'
} else {
    $replaced = 0
    foreach ($window in @(
        @{ File = 'display.pid'; Label = 'customer display' },
        @{ File = 'till.pid'; Label = 'till' }
    )) {
        $proc = Get-LiveWindow $window.File
        if ($proc) {
            Close-KioskWindow -Process $proc -Label $window.Label
            Remove-Item (Join-Path $sessionDir $window.File) -Force -ErrorAction SilentlyContinue
            $replaced++
        }
    }
    if ($replaced -eq 0) { Write-Host 'Instance : nothing running, starting fresh' }
}

function Find-Browser {
    $candidates = @(
        "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
        "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
        "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe",
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
    )
    foreach ($path in $candidates) { if (Test-Path $path) { return $path } }

    $cmd = Get-Command 'chrome.exe', 'msedge.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd) { return $cmd.Source }
    return $null
}

function Test-AppUp {
    param([string]$Address, [int]$TimeoutSec = 3)
    try {
        $null = Invoke-WebRequest -Uri $Address -UseBasicParsing -TimeoutSec $TimeoutSec
        return $true
    } catch {
        $script:lastProbeError = $_.Exception.Message
        return $false
    }
}

# Works both from a published folder (Kiosk.exe sits alongside this script) and
# from the source tree during development (dotnet run against the .csproj).
function Start-PosService {
    $exe = Join-Path $PSScriptRoot 'Kiosk.exe'
    if (Test-Path $exe) {
        Write-Host 'Service  : starting published build...'
        Start-Process $exe -WorkingDirectory $PSScriptRoot -WindowStyle Minimized
        return $true
    }

    $project = @(
        (Join-Path $PSScriptRoot 'Kiosk.csproj'),
        (Join-Path $PSScriptRoot 'Kiosk\Kiosk.csproj')
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($project) {
        Write-Host 'Service  : starting from source (dotnet run)...'
        Start-Process 'dotnet' -ArgumentList "run --no-launch-profile --project `"$project`"" `
            -WorkingDirectory (Split-Path $project) -WindowStyle Minimized
        return $true
    }

    return $false
}

# ---------------------------------------------------------------- browser
$browser = Find-Browser
if (-not $browser) {
    Write-Error 'Neither Chrome nor Edge was found. Install one, or pass its path.'
    exit 1
}
Write-Host "Browser  : $browser"

# ---------------------------------------------------------------- service
$isLocal = ([uri]$Url).Host -in @('localhost', '127.0.0.1', '::1')

# A hosted site that has gone idle takes far longer than 3 seconds to answer its
# first request, and answers instantly once woken - so a single short probe fails
# the first launch of the day and the second one works. Give it a long probe, and
# the same wait a local start gets.
$probeTimeout = if ($isLocal) { 3 } else { 15 }

if (-not (Test-AppUp $Url $probeTimeout)) {
    # Only self-host when the URL is local. Against IIS or a domain the server
    # owns the app lifetime and this launcher just opens the two screens.
    if ($isLocal) {
        if (-not (Start-PosService)) {
            Write-Error "Found neither Kiosk.exe nor Kiosk.csproj next to this script ($PSScriptRoot)."
            exit 1
        }
    } else {
        Write-Host "Service  : no answer yet, waiting up to $StartTimeoutSeconds seconds ($lastProbeError)"
    }

    $deadline = (Get-Date).AddSeconds($StartTimeoutSeconds)
    while (-not (Test-AppUp $Url $probeTimeout)) {
        if ((Get-Date) -gt $deadline) {
            if ($isLocal) {
                Write-Error "The POS service did not come up within $StartTimeoutSeconds seconds."
            } else {
                Write-Error "Cannot reach $Url after $StartTimeoutSeconds seconds. Check the site is running and the host is correct. Last error: $lastProbeError"
            }
            exit 1
        }
        Start-Sleep -Milliseconds 800
    }
}
Write-Host "Service  : up at $Url"

# ---------------------------------------------------------------- monitors
$screens = [System.Windows.Forms.Screen]::AllScreens

$primary = $screens | Where-Object { $_.Primary } | Select-Object -First 1
if (-not $primary) { $primary = $screens[0] }

$secondary = $screens | Where-Object { $_.DeviceName -ne $primary.DeviceName } | Select-Object -First 1

foreach ($s in $screens) {
    $role = if ($s.DeviceName -eq $primary.DeviceName) { 'till     ' }
            elseif ($secondary -and $s.DeviceName -eq $secondary.DeviceName) { 'customer ' }
            else { 'unused   ' }
    Write-Host ("Monitor  : {0} {1} at {2},{3}  {4}x{5}" -f `
        $role, $s.DeviceName, $s.Bounds.X, $s.Bounds.Y, $s.Bounds.Width, $s.Bounds.Height)
}

# Separate profiles so --kiosk actually applies to the customer window. Safe
# because cart state travels over the server, not via browser-local channels.
#
# The restore flags matter for sign-in, not just for tidiness: Chrome restores a
# crashed session's SESSION COOKIES, so a till that went down hard would come back
# already signed in as whoever closed it. The power button now shuts the browser
# down cleanly, and these cover the times it does not get the chance - a power cut,
# or Alt+F4 on a wedged window.
$commonArgs = @(
    '--no-first-run',
    '--no-default-browser-check',
    '--disable-features=TranslateUI',
    '--disable-session-crashed-bubble',
    '--hide-crash-restore-bubble',
    '--restore-last-session=false'
)

# ---------------------------------------------------------------- till window
# The address goes in as --app=, not as a bare argument: an app window is its own
# thing on the taskbar and takes ITS ICON FROM THE SITE, so staff see the NexBill
# cup rather than a Chrome tile. --kiosk still does the fullscreen, no-UI part.
# Chrome caches that icon per profile under KioskPOS\till\Web Applications, so a
# changed favicon only shows after the profile folder is deleted.
#
# launcher=1 tells the page not to also open a popup display.
$tillUrl = if ($secondary) { "$Url/?launcher=1" } else { $Url }

$tillArgs = $commonArgs + @(
    "--user-data-dir=$(Join-Path $profileRoot 'till')",
    '--kiosk',
    "--window-position=$($primary.Bounds.X),$($primary.Bounds.Y)",
    "--window-size=$($primary.Bounds.Width),$($primary.Bounds.Height)",
    "--app=$tillUrl"
)
$tillProc = Start-Process $browser -ArgumentList $tillArgs -PassThru
Set-SessionWindow 'till.pid' $tillProc
Write-Host "Launched : till (fullscreen, pid $($tillProc.Id))"

# ---------------------------------------------------------------- customer window
if ($secondary) {
    Start-Sleep -Milliseconds 1200  # let the first profile finish initialising

    $displayArgs = $commonArgs + @(
        "--user-data-dir=$(Join-Path $profileRoot 'display')",
        '--kiosk',
        "--window-position=$($secondary.Bounds.X),$($secondary.Bounds.Y)",
        "--window-size=$($secondary.Bounds.Width),$($secondary.Bounds.Height)",
        "--app=$Url/customer-display"
    )
    $displayProc = Start-Process $browser -ArgumentList $displayArgs -PassThru
    Set-SessionWindow 'display.pid' $displayProc
    Write-Host "Launched : customer display (fullscreen, pid $($displayProc.Id))"
} else {
    # The till opens its own popup here, which it does own a handle to, so there
    # is no pid to record.
    Remove-Item (Join-Path $sessionDir 'display.pid') -Force -ErrorAction SilentlyContinue
    Write-Host 'Launched : single monitor detected - the till will open its own display window'
}

Write-Host ''
Write-Host 'Both windows run fullscreen with no browser UI.'
Write-Host 'Close with the power button on the till - Alt+F4 is only the fallback.'
Write-Host 'Running this again replaces these windows; -AllowSecondInstance keeps them.'

# ---------------------------------------------------------------- hold the pair together
# Why this process now stays alive for the whole shift rather than exiting here.
#
# The power button closes both windows by asking the SERVER to do it, and the
# server can only find them through the pid files under the launching user's
# LOCALAPPDATA. That lines up when the app and the browser are the same user on
# the same machine. On a thin-client till it does not: the app is on another box,
# looks in its own LOCALAPPDATA, finds nothing, and answers that there is no
# launcher session. The till then says Alt+F4 - and the customer display, told to
# shut down over the display channel, is refused window.close() by Chrome because
# no script opened it, so it falls back to the welcome card and stays lit.
#
# No server-side change can fix that. Closing a window on this machine needs
# something running on this machine, so the launcher stays and owns the pair:
# whatever ends the till window - the power button, Alt+F4, a crash, Task Manager
# - the customer display goes with it. The display is the one that matters. A
# till that has vanished is obvious; a customer screen still lit after closing
# time is what someone has to walk over and deal with.
if ($NoWatch) {
    Write-Host ''
    Write-Host 'Watcher  : -NoWatch given, exiting now.'
    Write-Host '           Closing the till will leave the customer display on screen.'
    return
}

if (-not $displayProc) {
    # Single monitor: the display is a popup inside the till's own browser process
    # and goes when the till does. There is no second window to hold on for.
    Write-Host 'Watcher  : not needed - on one monitor the display lives in the till window.'
    return
}

Write-Host ''
Write-Host 'Watcher  : holding. The customer display will close when the till does.'
Write-Host '           Leave this window alone - it is what tears the pair down.'

# Nothing left to report until the till goes, and a kiosk should not carry a
# "Windows PowerShell" button in the taskbar all shift. Hidden rather than
# minimised, since the shortcut's minimised console is exactly what staff would
# otherwise find and click.
try {
    Add-Type -Namespace Kiosk -Name Win -MemberDefinition '
        [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);'
    $console = [Kiosk.Win]::GetConsoleWindow()
    if ($console -ne [IntPtr]::Zero) { $null = [Kiosk.Win]::ShowWindow($console, 0) }  # SW_HIDE
} catch { }

try { $tillProc.WaitForExit() }
catch {
    # Lost the handle somehow; poll instead, so a till that ends is still noticed.
    while (Get-Process -Id $tillProc.Id -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 2 }
}

Write-Host "Till     : closed (pid $($tillProc.Id))."
Clear-SessionWindow 'till.pid' $tillProc.Id

# Our own handle, not the pid file: a relaunch that replaced us while we waited
# has already written its own display there, and that window is not ours to close.
if ($displayProc.HasExited) {
    Write-Host 'Display  : already closed.'
} else {
    Close-KioskWindow -Process $displayProc -Label 'customer display' -Context 'Teardown'
}
Clear-SessionWindow 'display.pid' $displayProc.Id

Write-Host 'Done.'
