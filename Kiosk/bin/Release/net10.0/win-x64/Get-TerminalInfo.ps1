<#
  Shows this machine's identity so it can be registered as a terminal.
  Run it on any till you want to authorise, then enter the values yourself.

  Usage:  .\Get-TerminalInfo.ps1
#>
[CmdletBinding()]
param()

# Unique per Windows installation. Survives renaming the PC and swapping network
# adapters; regenerated only by reinstalling the OS.
$machineGuid = try {
    (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Cryptography' -Name MachineGuid -ErrorAction Stop).MachineGuid
} catch { '<could not read registry>' }

$os = (Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue).Caption
$model = try {
    $p = Get-CimInstance Win32_ComputerSystemProduct -ErrorAction Stop
    "$($p.Vendor) $($p.Name)".Trim()
} catch { 'unknown' }
$biosSerial = try {
    (Get-CimInstance Win32_BIOS -ErrorAction Stop).SerialNumber.Trim()
} catch { 'unknown' }

Write-Host ''
Write-Host 'Terminal identity' -ForegroundColor Cyan
Write-Host '-----------------------------------------------------------'
Write-Host ("  MachineGuid  : {0}" -f $machineGuid) -ForegroundColor Yellow
Write-Host ("  MachineName  : {0}" -f $env:COMPUTERNAME)
Write-Host '-----------------------------------------------------------'
Write-Host ("  OS           : {0}" -f $os)
Write-Host ("  Model        : {0}" -f $model)
Write-Host ("  BIOS serial  : {0}" -f $biosSerial)
Write-Host ''
Write-Host 'MachineGuid is the value the app matches on. MachineName is for' -ForegroundColor DarkGray
Write-Host 'readability only. OS, model and serial are reference details.' -ForegroundColor DarkGray
Write-Host ''
