<#
  Builds the folder you hand to a terminal when the app is hosted elsewhere
  (an on-prem IIS box or any server the tills can reach). The terminal does not need the app, .NET, or
  the database - only a browser and these few files - so do not copy the whole
  published folder to a till.

      .\Build-ClientKit.ps1 -Url https://pos.example.com
      .\Build-ClientKit.ps1 -Url https://pos.example.com -OutputPath D:\handout\till-kit

  Copy the resulting folder to each terminal, then double-click Install-Kiosk.bat
  inside it as the account the till will be used on - no PowerShell prompt and no
  execution-policy flag to type. -Url is written into that copy's
  settings block, so the installer on the till asks nothing and nobody types the
  address twice.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Url,

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

# Not a param() default: Windows PowerShell 5.1 can leave $PSScriptRoot empty there.
if (-not $OutputPath) { $OutputPath = Join-Path $PSScriptRoot 'client-kit' }

$Url = $Url.TrimEnd('/')

# Catch the obvious mistakes now rather than on the till.
try { $parsed = [uri]$Url } catch { Write-Error "Not a valid URL: $Url"; exit 1 }
if ($parsed.Scheme -notin @('http', 'https')) {
    Write-Error "The URL must be http or https, got '$($parsed.Scheme)'."
    exit 1
}
if ($parsed.Host -in @('localhost', '127.0.0.1', '::1')) {
    Write-Error "'$Url' points at the terminal itself. A client kit is for a hosted server - pass the address staff will actually reach."
    exit 1
}
if ($parsed.Scheme -eq 'http') {
    Write-Warning "$Url is plain HTTP. Staff passwords would cross the network in clear text - use HTTPS for anything off this machine."
}

# ---------------------------------------------------------------- collect
$items = @(
    @{ Source = Join-Path $PSScriptRoot 'Start-Kiosk.ps1';  Required = $true },
    @{ Source = Join-Path $PSScriptRoot 'Start-Kiosk.bat';  Required = $true },
    @{ Source = Join-Path $PSScriptRoot 'Install-Kiosk.ps1'; Required = $true },
    @{ Source = Join-Path $PSScriptRoot 'Install-Kiosk.bat'; Required = $true },
    # app.ico is the one the shortcut wants - it carries the 48px and 256px
    # frames. favicon.ico comes along only as the fallback for a kit built from a
    # tree where make-icons.py has not been run.
    @{ Source = Join-Path $PSScriptRoot 'wwwroot\app.ico';     Required = $false },
    @{ Source = Join-Path $PSScriptRoot 'app.ico';             Required = $false },
    @{ Source = Join-Path $PSScriptRoot 'wwwroot\favicon.ico'; Required = $false },
    @{ Source = Join-Path $PSScriptRoot 'favicon.ico';         Required = $false }
)

$missing = $items | Where-Object { $_.Required -and -not (Test-Path $_.Source) }
if ($missing) {
    Write-Error ("Missing from $PSScriptRoot : " + (($missing | ForEach-Object { Split-Path $_.Source -Leaf }) -join ', '))
    exit 1
}

$null = New-Item -ItemType Directory -Force -Path $OutputPath

$copied = @()
foreach ($item in $items) {
    if (-not (Test-Path $item.Source)) { continue }
    # The icon lands loose in the kit; Install-Kiosk.ps1 looks for it both ways.
    Copy-Item $item.Source -Destination (Join-Path $OutputPath (Split-Path $item.Source -Leaf)) -Force
    $copied += Split-Path $item.Source -Leaf
}
if (-not ($copied | Where-Object { $_ -in @('app.ico', 'favicon.ico') })) {
    Write-Warning 'No app.ico or favicon.ico found - shortcuts on the terminal will use the generic icon. Run scripts\make-icons.py first.'
}

# ---------------------------------------------------------------- bake the url in
# Install-Kiosk.ps1 asks nothing; it reads $url from its own settings block. Write
# the address into the copy going out, so on the till it really is one command.
$installer = Join-Path $OutputPath 'Install-Kiosk.ps1'
$patched = 0
$lines = @(Get-Content $installer | ForEach-Object {
    # Only the settings line, which assigns a quoted literal - not the
    # normalising "$url = "$url".Trim()..." further down the file.
    if ($_ -match "^\`$url\s*=\s*'") { $patched++; "`$url = '$Url'" } else { $_ }
})
if ($patched -ne 1) {
    Write-Error "Expected one `$url line in Install-Kiosk.ps1, found $patched. Set it by hand in $installer."
    exit 1
}
Set-Content -Path $installer -Value $lines -Encoding utf8

# ---------------------------------------------------------------- instructions
@"
NexBill - terminal setup
=========================

Server: $Url

This folder is everything a till needs. The application itself runs on the
server above; nothing here needs .NET, SQL Server, or the published app.

Install (once per terminal, signed in as the account the till will be used on):

    DOUBLE-CLICK  Install-Kiosk.bat

That is the whole thing - no PowerShell, nothing to type, it asks nothing. The address above is already set in
the script, so it puts a "NexBill" icon on the Desktop and in the Start menu,
and makes the till start by itself after a reboot. Staff just double-click the
icon.

Copy the folder to a LOCAL DISK first - C:\NexBill is fine. Installing from the
share you copied it from leaves every launch in Windows' untrusted zone, and
staff get "the publisher could not be verified" before each shift. The installer
clears that tag off its own folder, and warns you if it is running from a share.

Windows may ask once, for the .bat itself, whether to run software from an
unknown publisher - say yes. The installer clears that tag off the whole folder
as it runs, so staff never see the question again.

Running it again replaces whatever it installed before, so there is never an
uninstall step first. To point this till somewhere else, open Install-Kiosk.ps1
in Notepad, change the `$url line near the top, save, and double-click
Install-Kiosk.bat again.

To start it right now without installing:

    .\Start-Kiosk.bat -Url $Url

Notes for whoever sets this up:

  * Relaunching replaces the windows that are open. Pass -AllowSecondInstance to
    run two tills on one machine on purpose.
  * To check what is installed and where it points, open a command prompt here
    and run:  Install-Kiosk.bat -Status
  * The auto-start task is registered for the account that ran the installer.
    Install under the account the till actually uses, or it will not fire.
  * With the app hosted remotely, the power button signs the shift out and
    returns to the login screen, but cannot close the till window - the server
    has no way to reach this machine. Use Alt+F4 for the till window itself. The
    customer display does close: the launcher stays running and takes it down
    with the till, however the till was ended.
  * That launcher is the hidden PowerShell process behind each till. Leave it
    alone - if it is killed, closing the till leaves the customer display lit.
  * No internet, no till. The screens are served by $Url.

To remove it again, from a command prompt in this folder:

    Install-Kiosk.bat -Uninstall
"@ | Set-Content -Path (Join-Path $OutputPath 'README.txt') -Encoding utf8

Write-Host "Kit      : $OutputPath"
foreach ($name in $copied) { Write-Host "  + $name" }
Write-Host '  + README.txt'
Write-Host ''
Write-Host "Server   : $Url (already set in the kit's Install-Kiosk.ps1)"
Write-Host 'Next     : copy this folder to each terminal (local disk, not a share)'
Write-Host '           and double-click Install-Kiosk.bat'
