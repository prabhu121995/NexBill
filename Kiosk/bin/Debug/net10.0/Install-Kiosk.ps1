<#
  Makes the kiosk launch like an installed desktop app rather than a .bat someone
  has to find: a Desktop and Start-menu shortcut with the app icon, plus a
  scheduled task so the till comes up by itself after a power cut.

  It asks nothing. Everything it needs is the SETTINGS block below - open this
  file, set $url (localhost by default, a domain if the app is hosted elsewhere),
  save, and run:

      .\Install-Kiosk.ps1

  Every run REPLACES the previous install: any kiosk shortcut and any kiosk logon
  task already on this account is removed first, then a fresh one is created from
  the current settings. So changing the address, or the shortcut name, is edit and
  re-run - never uninstall by hand first, and nothing is left behind under an old
  name or pointing at an old server.

      .\Install-Kiosk.ps1 -Status      # what is installed, and where it points
      .\Install-Kiosk.ps1 -Uninstall   # remove the shortcut and the task

  Run it once per terminal, from the published folder (or from the client kit), as
  the account the till will actually be used on.

  The task is registered AT LOGON FOR THE CURRENT USER, deliberately - not at
  boot as SYSTEM. The default connection string is SQL LocalDB
  ((localdb)\MSSQLLocalDB), which is a per-user instance that does not exist for
  a service account, so a boot-time task would start the app against a database
  it cannot reach. Switch to SQL Express or a real server first if you need the
  till up before anyone logs in, then change -AtLogOn to -AtStartup below.
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$Status
)

$ErrorActionPreference = 'Stop'

# ================================================================== SETTINGS
# Everything worth changing is here. Edit, save, run the script again.
# ---------------------------------------------------------------------------

# The address this terminal opens. Baked into the shortcut and the logon task, so
# staff never type it.
#   app runs on this machine : http://localhost:5034
#   hosted elsewhere         : https://pos.yourdomain.com
$url = 'http://localhost:5034'

# Name on the Desktop icon, in the Start menu, and of the scheduled task.
# Safe to rename - the next run finds the old one by what it points at and
# removes it, whatever it was called.
$appName = 'NexBill'

# $true registers the logon task so the till comes back after a power cut.
# $false installs the shortcut only (what you want on a development machine).
$autoStart = $true

# Seconds the logon task waits before launching, so the network and LocalDB are
# up before the app probes them.
$autoStartDelaySeconds = 30

# Tooltip on the shortcut.
$shortcutDescription = 'Open the NexBill till and customer display'

# ---------------------------------------------------------------------------

$launcher = Join-Path $PSScriptRoot 'Start-Kiosk.bat'

# Published folder keeps the icon under wwwroot; a client kit (terminal talking to
# a hosted server, no app files) carries it loose beside the scripts.
#
# app.ico before favicon.ico: both are written by scripts/make-icons.py, but the
# shortcut wants the 48px and 256px frames the desktop and the Start menu ask for,
# and a hand-dropped favicon.ico is often 16/32 only - which Windows then scales
# into the blur that looks like no icon was set at all.
$icon = @(
    (Join-Path $PSScriptRoot 'wwwroot\app.ico'),
    (Join-Path $PSScriptRoot 'app.ico'),
    (Join-Path $PSScriptRoot 'wwwroot\favicon.ico'),
    (Join-Path $PSScriptRoot 'favicon.ico')
) | Where-Object { Test-Path $_ } | Select-Object -First 1

$desktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) "$appName.lnk"
$startMenuLink = Join-Path ([Environment]::GetFolderPath('Programs')) "$appName.lnk"

function Get-KioskShortcut {
    <#
      Every shortcut on this account's Desktop or Start menu that launches a
      kiosk, found by WHAT IT POINTS AT rather than by its name - so a previous
      install is still found after $appName is changed, or after the folder was
      moved, and a rename never orphans an icon nothing can clean up.
    #>
    $shell = New-Object -ComObject WScript.Shell
    foreach ($root in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) {
        if (-not (Test-Path $root)) { continue }
        foreach ($file in @(Get-ChildItem -Path $root -Filter '*.lnk' -Recurse -ErrorAction SilentlyContinue)) {
            $sc = $null
            try { $sc = $shell.CreateShortcut($file.FullName) } catch { continue }
            # A .lnk to a URL, a control-panel item or a dead drive has no target
            # path at all, and Split-Path refuses an empty string.
            $target = "$($sc.TargetPath)".Trim('"').Trim()
            if ([string]::IsNullOrWhiteSpace($target)) { continue }
            if ((Split-Path $target -Leaf) -eq 'Start-Kiosk.bat') {
                [pscustomobject]@{
                    Path      = $file.FullName
                    Target    = $sc.TargetPath
                    Arguments = $sc.Arguments
                }
            }
        }
    }
}

function Get-KioskTask {
    <#
      Same idea for the scheduled task: match on the launcher it runs, not on the
      task name, so a renamed task is still found and replaced.
    #>
    foreach ($task in @(Get-ScheduledTask -ErrorAction SilentlyContinue)) {
        $action = $null
        try { $action = $task.Actions | Select-Object -First 1 } catch { continue }
        if (-not $action) { continue }
        $exe = "$($action.Execute)".Trim('"').Trim()
        if ([string]::IsNullOrWhiteSpace($exe)) { continue }
        if ((Split-Path $exe -Leaf) -eq 'Start-Kiosk.bat') { $task }
    }
}

function Remove-KioskInstall {
    <#
      Clears whatever is installed and reports how many pieces went. Used both by
      -Uninstall and at the top of every install, so a run never stacks a second
      icon or leaves a task pointing at the old address.
    #>
    $removed = 0

    foreach ($sc in @(Get-KioskShortcut)) {
        Remove-Item $sc.Path -Force -ErrorAction SilentlyContinue
        Write-Host "Removed  : $($sc.Path)"
        $removed++
    }

    foreach ($task in @(Get-KioskTask)) {
        Unregister-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath -Confirm:$false
        Write-Host "Removed  : scheduled task '$($task.TaskName)'"
        $removed++
    }

    return $removed
}

# ---------------------------------------------------------------- status
if ($Status) {
    Write-Host "Terminal : $appName"
    Write-Host "Settings : $url, auto-start $(if ($autoStart) { 'on' } else { 'off' })"
    Write-Host ''

    $shortcuts = @(Get-KioskShortcut)
    if ($shortcuts) {
        foreach ($sc in $shortcuts) {
            Write-Host ("Shortcut : {0}" -f $sc.Path)
            Write-Host ("           -> {0} {1}" -f (Split-Path $sc.Target -Leaf), $sc.Arguments)
        }
    } else {
        Write-Host 'Shortcut : not installed'
    }

    $tasks = @(Get-KioskTask)
    if ($tasks) {
        foreach ($task in $tasks) {
            $action = $task.Actions | Select-Object -First 1
            Write-Host ("Auto-start: '{0}' {1}, runs as {2}" -f $task.TaskName, $task.State, $task.Principal.UserId)
            Write-Host ("            {0} {1}" -f (Split-Path $action.Execute -Leaf), $action.Arguments)
        }
    } else {
        Write-Host 'Auto-start: not registered'
    }
    exit 0
}

# ---------------------------------------------------------------- uninstall
if ($Uninstall) {
    if ((Remove-KioskInstall) -eq 0) { Write-Host 'Nothing installed for this account.' }
    Write-Host 'Done. The app itself is untouched - delete the folder to finish removing it.'
    exit 0
}

# ---------------------------------------------------------------- checks
if (-not (Test-Path $launcher)) {
    Write-Error "Start-Kiosk.bat is not next to this script ($PSScriptRoot). Run it from the published folder."
    exit 1
}
if (-not $icon) {
    Write-Warning "No app.ico or favicon.ico beside this script or under wwwroot - the shortcuts will use the generic icon. Run scripts\make-icons.py and publish again."
}

# A folder that arrived by zip, USB or network share carries Mark of the Web on
# every file, and Windows then asks "the publisher could not be verified - do you
# want to run this software?" on EVERY launch. Not once: the shortcut and the
# logon task both run Start-Kiosk.bat, and the warning for a .bat has no "always
# ask before opening this file" box to untick, so staff would dismiss a dialog
# before every shift - and the logon task would sit on it unanswered after a
# power cut, which looks exactly like a till that failed to come back.
#
# So strip the mark here, at install time, from the files in this folder Windows
# actually executes. Top level only - that is where the launchers live, and a
# published folder's dependencies are loaded by an already-trusted process.
$blocked = @(
    Get-ChildItem -Path $PSScriptRoot -File |
        Where-Object { $_.Extension -in @('.bat', '.cmd', '.ps1', '.exe', '.ico') } |
        Where-Object { Get-Item -LiteralPath $_.FullName -Stream Zone.Identifier -ErrorAction SilentlyContinue }
)
if ($blocked) {
    $blocked | Unblock-File
    Write-Host "Unblocked: $(($blocked | ForEach-Object { $_.Name }) -join ', ')"
}

# $PSScriptRoot itself on a UNC path is a separate cause of the same dialog that
# unblocking cannot fix - the zone is the share, not a tag on the file.
if ($PSScriptRoot -like '\\*') {
    Write-Warning "This folder is on a network share ($PSScriptRoot). Windows treats every launch from it as untrusted whatever we unblock - copy the folder to a local disk (C:\NexBill) and run this again from there."
}

# $url is typed by hand in the settings block, so check it here rather than
# letting a typo become a shortcut that opens nothing.
$url = "$url".Trim().TrimEnd('/')
if ($url -notmatch '^[a-zA-Z][a-zA-Z0-9+.-]*://') { $url = "http://$url" }

$parsed = $null
try { $parsed = [uri]$url } catch { }
if (-not $parsed -or -not $parsed.IsAbsoluteUri -or -not $parsed.Host) {
    Write-Error "`$url in the settings block is not a valid address: $url"
    exit 1
}
if ($parsed.Scheme -notin @('http', 'https')) {
    Write-Error "`$url must be http or https, got '$($parsed.Scheme)'."
    exit 1
}
if ($parsed.Scheme -eq 'http' -and $parsed.Host -notin @('localhost', '127.0.0.1', '::1')) {
    Write-Warning "$url is plain HTTP across the network - staff passwords would cross it in clear text. Use HTTPS for anything off this machine."
}

Write-Host "Installing: $appName -> $url"
Write-Host ''

# ---------------------------------------------------------------- replace previous
# Unconditional, and before anything is written: a re-run is how the address and
# the name are changed, so the old icon and the old task must go whatever they
# were called and wherever they pointed.
if ((Remove-KioskInstall) -eq 0) { Write-Host 'Previous : none found, installing fresh' }

# Always written out, never left to Start-Kiosk's own default, so the shortcut and
# the task state plainly where this terminal points - and -Status can read it back.
$arguments = "-Url `"$url`""

# ---------------------------------------------------------------- shortcuts
if ($icon) { Write-Host "Icon     : $icon" }

$shell = New-Object -ComObject WScript.Shell
foreach ($link in @($desktopLink, $startMenuLink)) {
    $sc = $shell.CreateShortcut($link)
    $sc.TargetPath = $launcher
    $sc.Arguments = $arguments
    $sc.WorkingDirectory = $PSScriptRoot
    $sc.Description = $shortcutDescription
    # Minimised: the launcher's console is plumbing, not part of the app.
    $sc.WindowStyle = 7
    if ($icon) { $sc.IconLocation = "$icon,0" }
    $sc.Save()
    Write-Host "Created  : $link"
}

# ---------------------------------------------------------------- auto start
if (-not $autoStart) {
    Write-Host 'Skipped  : auto-start ($autoStart is $false)'
} else {
    $action = New-ScheduledTaskAction -Execute $launcher -Argument $arguments -WorkingDirectory $PSScriptRoot
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    $trigger.Delay = "PT$($autoStartDelaySeconds)S"

    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -StartWhenAvailable -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) `
        -ExecutionTimeLimit (New-TimeSpan -Seconds 0)
    # A till is never idle-stopped mid-service.
    $settings.DisallowStartIfOnBatteries = $false
    $settings.IdleSettings.StopOnIdleEnd = $false

    $principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" `
        -LogonType Interactive -RunLevel Limited

    Register-ScheduledTask -TaskName $appName -Action $action -Trigger $trigger `
        -Settings $settings -Principal $principal `
        -Description "Starts $appName after logon." | Out-Null

    Write-Host "Created  : scheduled task '$appName' (at logon for $env:USERNAME, $autoStartDelaySeconds s delay)"
}

Write-Host ''
Write-Host "Done. '$appName' is on the Desktop and in the Start menu."
Write-Host "Points at: $url"
if ($autoStart) { Write-Host 'It will also start by itself the next time this user logs in.' }
Write-Host ''
Write-Host 'Moving to another server later? Set $url at the top of this script and run'
Write-Host 'it again - it replaces what is installed now. Check it any time with:'
Write-Host '    .\Install-Kiosk.ps1 -Status'
