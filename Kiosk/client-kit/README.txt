NexBill - terminal setup
=========================

Server: http://localhost:5034

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
in Notepad, change the $url line near the top, save, and double-click
Install-Kiosk.bat again.

To start it right now without installing:

    .\Start-Kiosk.bat -Url http://localhost:5034

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
  * No internet, no till. The screens are served by http://localhost:5034.

To remove it again, from a command prompt in this folder:

    Install-Kiosk.bat -Uninstall
