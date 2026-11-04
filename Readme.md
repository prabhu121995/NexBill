# NexBill Kiosk

ASP.NET Core MVC billing terminal for a cafe, built to run on a two-monitor till:
the cashier bills on one screen, the customer sees their order live on the other.

- **Framework:** .NET 10 (`net10.0`), ASP.NET Core MVC
- **Data:** EF Core 10.0.12, SQL Server, database `POS`
- **Front end:** Razor views with Tailwind, no build step — every asset is
  served locally, so the till works with the internet down

---

## Project layout

| Path | Purpose |
|---|---|
| `Kiosk/Views/Account/Login.cshtml` | Sign-in screen |
| `Kiosk/Views/Admin/Index.cshtml` | Admin panel |
| `Kiosk/Views/Home/Index.cshtml` | The till / billing screen |
| `Kiosk/Views/Home/CustomerDisplay.cshtml` | Customer-facing screen (second monitor) |
| `Kiosk/Views/Shared/Error.cshtml` / `Status.cshtml` | Frameless error and 404/403 pages |
| `Kiosk/Views/Shared/_KioskHead.cshtml` | Shared `<head>` for those two pages |
| `Kiosk/Views/Shared/_AppIcons.cshtml` | Favicon, touch icon and manifest links, included by every page |
| `Kiosk/Controllers/AccountController.cs` | Sign-in, sign-out, the auth cookie |
| `Kiosk/Controllers/AdminUsersController.cs` | Admin panel Users & Roles: staff accounts (`users`) and roles (`config_role`) |
| `Kiosk/Services/PhoneNumbers.cs` | Digit-only phone matching for sign-in |
| `Kiosk/Services/Roles.cs` | Role codes and who may reach the admin panel |
| `Kiosk/Services/DisplayHub.cs` | Relays cart state from till to display |
| `Kiosk/Services/ScreenInfo.cs` | Reads the Windows monitor layout (P/Invoke) |
| `Kiosk/Services/KioskSession.cs` | Closes the windows the launcher opened |
| `Kiosk/Data/AppDbContext.cs` | EF model, audit-field defaults |
| `Kiosk/Data/DbSeeder.cs` | Seeds categories and menu items on first run |
| `Kiosk/Start-Kiosk.ps1` / `.bat` | Dual-screen kiosk launcher |
| `Kiosk/Install-Kiosk.bat` | Double-click to set a terminal up (wraps the installer) |
| `Kiosk/Install-Kiosk.ps1` | Shortcuts and auto-start, run once per terminal |
| `Kiosk/Build-ClientKit.ps1` | Packs the launcher for terminals of a hosted server |
| `Kiosk/scripts/vendor-fonts.py` | Regenerates `wwwroot/lib/fonts` from Google Fonts |
| `Kiosk/scripts/make-icons.py` | Regenerates the app icon in every size — see [App icon](#app-icon) |
| `Kiosk/wwwroot/lib/{tailwind,lucide,qrcodejs,fonts}` | Vendored front-end assets |
| `index.html`, `login.html`, `admin.html` (repo root) | **Reference designs only — do not modify** |

### Endpoints

| Route | Method | Auth | Purpose |
|---|---|---|---|
| `/login` | GET | open | Sign-in form; redirects away if already signed in |
| `/api/account/login` | POST | open | Checks credentials, issues the cookie, returns the screen to open |
| `/logout` | POST | signed in | Clears the cookie |
| `/` | GET | signed in | Till / billing screen |
| `/admin` | GET | `ADMIN`, `SUPERVISOR` | Admin panel |
| `/admin/api/dashboard` | GET | `ADMIN`, `SUPERVISOR` | Today's dashboard figures, read live from the database |
| `/admin/api/reports?from=&to=` | GET | `ADMIN`, `SUPERVISOR` | Sales for a range of trading dates (default today, max 366 days): totals, hourly amount + order count, category, cashier and waiter breakdowns. Settled bills only |
| `/admin/api/charges` | POST | `ADMIN`, `SUPERVISOR` | Saves the service charge and tax rates to `config_terminal_setting` |
| `/admin/api/store` | POST | `ADMIN`, `SUPERVISOR` | Saves Store Name, Address, Phone and GST No. to the single `config_store` row (App Name is read-only) |
| `/admin/api/users` | GET, POST | `ADMIN`, `SUPERVISOR` | Lists / adds staff accounts. Passwords are never returned |
| `/admin/api/users/{id}` | PUT, DELETE | `ADMIN`, `SUPERVISOR` | Edits (blank password keeps it) / deletes an account with no bills against it |
| `/admin/api/users/{id}/active` | POST | `ADMIN`, `SUPERVISOR` | Enables or disables sign-in for an account |
| `/admin/api/roles` | GET, POST | read: both; write: `ADMIN` | Lists / adds roles |
| `/admin/api/roles/{id}` | PUT, DELETE | `ADMIN` | Renames or reorders a role / deletes an unused, non built-in role |
| `/admin/api/roles/{id}/active` | POST | `ADMIN` | Switches a role on or off (off = its staff cannot sign in) |
| `/admin/api/attendance?date=yyyy-MM-dd` | GET | `ADMIN`, `SUPERVISOR` | One day of `staff_attendance`: first in, last out, hours per person |
| `/customer-display` | GET | open | Customer screen |
| `/api/billing/*` | — | signed in | Orders, checkout, recall, merge |
| `/api/billing/waiters` | GET | signed in | Staff the settle dialog can credit a bill to |
| `/api/billing/attendance/waiters` | GET | signed in | Every active waiter, and whether they are punched in |
| `/api/billing/attendance/punch-in` | POST | signed in + antiforgery | Punches a waiter in (`{ waiterId }`) |
| `/api/billing/attendance/punch-out` | POST | signed in + antiforgery | Punches a waiter out (`{ waiterId }`) |
| `/api/billing/hold` | POST | signed in | Parks the cart in `held_order` (Hold / F6) |
| `/api/billing/held` | GET | signed in | Holds still parked, from every till |
| `/api/billing/held/{id}/recall` | POST | signed in | Takes a hold back onto the till and deletes it from `held_order` |
| `/api/billing/held/{id}/discard` | POST | signed in | Throws a hold away, deleting it |
| `/api/billing/eod` | GET | `ADMIN`, `SUPERVISOR` | What End of Day would move now, for the confirm dialog |
| `/api/billing/eod` | POST | `ADMIN`, `SUPERVISOR` | End of Day: moves closed bills to the archive tables |
| `/api/display/state` | POST | signed in | Till pushes cart state |
| `/api/display/stream` | GET | open | SSE stream the display subscribes to |
| `/api/screens` | GET | open | Monitor layout (loopback callers only) |
| `/api/terminal/close` | POST | signed in | Closes the launcher's kiosk windows (loopback callers only) |
| `/error/{code}` | GET | open | Frameless status page for 404, 403, 401, 5xx |

### Users & Roles rules

Enforced server side in `AdminUsersController`; the page greys out the same actions:

- A **Supervisor** manages every account except Admin ones, and cannot grant the Admin role. Only an **Admin** can add, rename, switch off or delete roles.
- Nobody can deactivate, delete or change the role of their **own** account. The Admin role cannot be switched off, and the **last active Admin** cannot be removed or demoted.
- **Waiter is picker-only:** no username or password, cannot sign in, only listed on the Settle Payment waiter picker. Moving a waiter to a sign-in role asks for a username and password.
- Built-in roles (Admin, Supervisor, Cashier, Waiter) can be renamed, not deleted. A role code is fixed once created, because the auth cookie and `[Authorize]` match on it. New roles open Billing POS only.
- Staff with bills or holds against them cannot be deleted. Deactivate them instead.
- Disabling an account or role blocks the **next** sign-in. A session that is already signed in stays valid until its cookie expires (12 h sliding).

---

## Everything is served locally

There are **no CDN references anywhere in the views.** Tailwind, lucide,
qrcodejs and all three fonts live under `Kiosk/wwwroot/lib`:

| Asset | Path | Notes |
|---|---|---|
| Tailwind | `lib/tailwind/tailwind.min.js` | Play CDN build 3.4.17 — still configured at runtime via `tailwind.config`, still no build step |
| lucide | `lib/lucide/lucide.min.js` | UMD build; pages keep calling `lucide.createIcons()` |
| qrcodejs | `lib/qrcodejs/qrcode.min.js` | PayNow QR on the customer screen |
| Fonts | `lib/fonts/fonts.css` + `lib/fonts/files/*.woff2` | Plus Jakarta Sans, JetBrains Mono, Chakra Petch; latin + latin-ext only, 156 KB total |

**Why it matters:** a café's internet going down used to take every screen's
styling, every icon and the PayNow QR with it — the till would still take orders
but would be unreadable. A terminal must not depend on a CDN it cannot reach.

To add a weight or family, edit the URL in `Kiosk/scripts/vendor-fonts.py` and
re-run it; it rewrites `fonts.css` and re-downloads the `.woff2` files:

```bash
python Kiosk/scripts/vendor-fonts.py Kiosk/wwwroot/lib/fonts
```

> Some files in `lib/fonts/files` are **variable** fonts covering a weight range,
> so one file backs several `@font-face` weights. The name carries the first
> weight that referenced it — `fonts.css` is the source of truth, not the filename.

---

## How the two screens stay in sync

The till posts cart state to `/api/display/state`; the display subscribes to
`/api/display/stream` over Server-Sent Events. `DisplayHub` keeps the last cart
in memory and replays it the moment a display connects, so opening the customer
screen mid-order shows the current basket immediately.

**This deliberately does not use `BroadcastChannel`.** That only works between
windows in one browser profile, and the launcher gives the customer window its
own `--user-data-dir` (required for `--kiosk` to apply). Routing through the
server also means the display can run in a different browser, or on a separate
machine pointed at the till.

The till pushes on every billing change and also runs a 200 ms watcher that
posts only when the serialised state actually differs — so no mutation path can
be missed, and unchanged ticks cost nothing.

---

## Running in development

```bash
cd Kiosk
dotnet run
```

Then open <http://localhost:5034>.

First run seeds categories and menu items, plus the first admin account
(**`admin` / `admin123`** — see [Login credentials](#login-credentials)). The
seeder is guarded by a `Categories.Any()` check, so restarts don't duplicate
data.

> **Razor views compile at build time.** Editing a `.cshtml` file and refreshing
> the browser does nothing — you must rebuild and restart. This has caused
> confusion before: the symptom is that *some* behaviour updates (code already
> compiled) while your new changes appear to be ignored.

---

## Kiosk mode (dual screen)

```
Kiosk\Start-Kiosk.bat
```

It detects everything at runtime — nothing is hardcoded to a particular machine:

1. Finds Chrome, then Edge, across standard per-user and per-machine paths.
2. Enumerates monitors via `Screen.AllScreens`, picks the primary for the till
   and the next one for the customer display.
3. Starts the app if it isn't already responding.
4. Opens both windows fullscreen (`--kiosk`), each with its own profile.

Works with one monitor (opens the till only), three monitors, any resolution,
and either monitor as primary.

### What to install on a client machine

**Not a single file — a small folder.** `Install-Kiosk.ps1` on its own will not
run: the shortcut and the logon task it creates both execute `Start-Kiosk.bat`,
which has to sit beside it. What you carry depends on where the app itself runs.

Setting a till up is one double-click — `Install-Kiosk.bat`. Everything below is
what that double-click does, and how to check it worked.

**The till is a thin client** (app hosted on a server — the usual case). Build the
kit on your machine, once, then copy the folder to each terminal:

```powershell
.\Build-ClientKit.ps1 -Url https://pos.example.com
```

| In the kit | Why it has to come |
|---|---|
| `Install-Kiosk.bat` | **what you double-click** — no PowerShell, nothing to type |
| `Install-Kiosk.ps1` | the installer itself — shortcuts and auto-start |
| `Start-Kiosk.bat` | what the shortcut and the task actually execute |
| `Start-Kiosk.ps1` | the launcher — monitor detection, both browser windows |
| `app.ico` | the Desktop and Start-menu icon |
| `README.txt` | generated setup notes, with the address already filled in |

Nothing else. **No .NET, no SQL Server, no published app on the till** — only
Chrome or Edge. The kit's `$url` is already set, so the installer asks nothing.
Full walk-through: [Terminals as thin clients](#terminals-as-thin-clients).

**The app runs on that same machine.** Carry the whole `dotnet publish` output
and install the prerequisites there: the ASP.NET Core runtime and SQL LocalDB or
Express. Leave `$url` on localhost. See [Deployment checklist](#deployment-checklist).

A plain publish **no longer includes the scripts** — the default server build is
hosted, where the till gets a client kit instead and the scripts on the server
would never run. For this model, ask for them:

```bash
dotnet publish Kiosk.csproj -c Release -o ./publish
```

Either way: copy the folder to a **local disk** (`C:\NexBill`), and run the
installer **signed in as the account the till will be used on** — the auto-start
task is per-user and fires for no one else.

### Installing it as a desktop app

Run this once on each terminal, from the published folder, **as the account the
till will be used on** — double-click:

```
Install-Kiosk.bat
```

**It asks nothing.** The configuration lives in a `SETTINGS` block at the top of
the script — `$url` defaults to localhost, which is right when the app runs on
the same machine:

```powershell
$url = 'http://localhost:5034'        # or https://pos.yourdomain.com
$appName = 'NexBill'                 # Desktop icon, Start menu, task name
$autoStart = $true                    # $false = shortcut only, no logon task
$autoStartDelaySeconds = 30
$shortcutDescription = 'Open the NexBill till and customer display'
```

Hosted elsewhere? Open the script, change `$url`, save, run it again. A missing
scheme is read as `http://`, a plain-HTTP address off this machine warns about
clear-text passwords (it still installs), and a `$url` that isn't a usable
address stops the run instead of producing a shortcut that opens nothing.

```
Installing: NexBill -> http://localhost:5034

Previous : none found, installing fresh
Created  : C:\Users\ADMIN\Desktop\NexBill.lnk
Created  : C:\Users\ADMIN\...\Start Menu\Programs\NexBill.lnk
Created  : scheduled task 'NexBill' (at logon for ADMIN, 30 s delay)
```

**Every run replaces the previous install.** Before it writes anything it removes
any kiosk shortcut and any kiosk logon task already on the account — found by
*what they point at*, not by name, so a run after you change `$appName` cleans up
the old icon and task too, and one pointing at an old server can't survive and
keep launching behind you:

```
Installing: NexBill -> https://pos.yourdomain.com

Removed  : C:\Users\ADMIN\Desktop\Old Till Name.lnk
Removed  : scheduled task 'NexBill Till'
Created  : C:\Users\ADMIN\Desktop\NexBill.lnk
...
```

So changing the address or the name is *edit and re-run* — never uninstall by
hand first, and nothing is left orphaned. The other two commands:

```
Install-Kiosk.bat -Status      # what is installed, and where it points
Install-Kiosk.bat -Uninstall   # remove the shortcut and the task
```

`-Status` reads the address back out of whatever is installed:

```
Terminal : NexBill
Settings : http://localhost:5034, auto-start on

Shortcut : C:\Users\ADMIN\Desktop\NexBill.lnk
           -> Start-Kiosk.bat -Url "http://localhost:5034"
Shortcut : C:\Users\ADMIN\...\Start Menu\Programs\NexBill.lnk
           -> Start-Kiosk.bat -Url "http://localhost:5034"
Auto-start: 'NexBill' Ready, runs as ADMIN
            Start-Kiosk.bat -Url "http://localhost:5034"
```

#### Step by step, if Windows scripting is new to you

**No Administrator rights are needed** — the shortcuts go in your own profile and
the task is registered for your own account.

1. **Open the folder** holding `Install-Kiosk.bat` in File Explorer (the published
   folder, or the client kit on a terminal). Make sure it is on a local disk, not
   still on the share or stick you copied it from.
2. **Set the address, if it isn't localhost.** Right-click `Install-Kiosk.ps1` →
   *Edit* (or open it in Notepad), change the `$url` line near the top to the
   server address, save, close. A client kit already has this filled in.
3. **Double-click `Install-Kiosk.bat`.** That is the whole installation. It asks
   nothing, prints what it removed and what it created, and waits on *Press any
   key* so you can read it.

   Windows may ask once, about the `.bat` itself, whether to run software from an
   unknown publisher — say yes. It is asked once, by you, at setup; staff never
   see it (see the next paragraph for why).

   You never have to type `-ExecutionPolicy Bypass`: the wrapper passes it for
   you. Windows blocks scripts by default, and a script copied from a network
   share or a USB stick stays blocked even after you change the policy — this
   gets past both without altering any machine setting. Run `Install-Kiosk.ps1`
   directly instead and you are back to typing the flag yourself.

   The installer then **clears that blocked tag** from the launchers beside it
   (`Start-Kiosk.bat`, the `.ps1` files, `app.ico`, `Kiosk.exe` if present) and
   prints which ones it cleared. That is not tidiness: the shortcut and the logon
   task both run `Start-Kiosk.bat`, and Windows asks *"the publisher could not be
   verified — do you want to run this software?"* every time a tagged `.bat` is
   executed. There is no "don't ask again" box on that dialog, so without this
   step staff would dismiss it before every shift, and the logon task would sit
   on it unanswered after a power cut — which looks exactly like a till that
   failed to come back.

   **Run the kit from a local disk, not from the share you copied it from.** A
   folder opened over `\\server\share` is in the untrusted zone by location, so
   the same dialog returns however many times the files are unblocked. The
   installer warns when it notices it is running from a UNC path.
4. **Check it worked:**

   Arguments need a prompt, since a double-click cannot pass one. Click the
   address bar in File Explorer, type `cmd`, press Enter, then:

   ```
   Install-Kiosk.bat -Status
   ```
5. **Start the till** by double-clicking *NexBill* on the Desktop.

> Installing on your own development machine? Set `$autoStart = $false`. Left on,
> the till launches fullscreen across your monitors every time you log in, which
> is right for a counter and irritating for a desk.

To undo everything:

```
Install-Kiosk.bat -Uninstall
```

> **The task runs at logon as the interactive user, not at boot as SYSTEM, and
> that is deliberate.** It dates from when the connection string was SQL LocalDB,
> a *per-user* instance that does not exist for a service account. The app uses
> LocalDB again (`dbNexBill`), so keep the trigger at logon until it moves to a server database.

### App icon

The Desktop and Start-menu shortcuts, the browser tab, and the taskbar tile
while the till is running all show the same mark — a two-tone shield carrying the N,
on the app's olive. None of those files are drawn by hand: `scripts/make-icons.py`
renders one master and writes every size from it, so they cannot drift apart.

The mark itself is `scripts/app-icon.svg`, with `scripts/app-icon.png` as its
1024px export (Pillow cannot read SVG). After editing the SVG, re-export the PNG
and run:

```
python scripts/make-icons.py wwwroot scripts/app-icon.png --plain
```

| File | Used by |
|---|---|
| `wwwroot/app.ico` | the Desktop and Start-menu shortcuts, stamped on by `Install-Kiosk.ps1` |
| `wwwroot/favicon.ico` | the browser tab, and the taskbar tile Chrome gives an `--app` window |
| `wwwroot/icons/*.png`, `wwwroot/site.webmanifest` | an installed PWA, and a tablet used as a customer display |

`app.ico` and `favicon.ico` hold the same image. They are separate files so the
installer has one it knows carries the 48px and 256px frames the desktop and the
Start menu ask for — a hand-dropped 16/32-only `favicon.ico` is exactly what
Windows blurs into something that reads as *no icon was set*.

#### Putting the café's own logo in

Export it as a square PNG, 512px or larger, transparent background:

```
python scripts/make-icons.py wwwroot cafe-logo.png
```

It is centred on the same olive tile, so one shop's till and the next still
look like the same product. Add `--plain` after the filename if the logo carries
its own background and should run edge to edge. Pillow cannot read SVG — export
to PNG first. Then publish, and re-run `Install-Kiosk.ps1` on each terminal (or
rebuild the client kit) so the terminals get the new file.

#### Two caches will keep showing you the old icon

Neither is a failed install:

* **Chrome** keeps an app window's icon per profile, under
  `%LOCALAPPDATA%\KioskPOS\till\Web Applications`. Delete the `till` and
  `display` profile folders and relaunch. That also signs the shift out, which
  on a terminal is usually wanted anyway.
* **Explorer** caches shortcut icons. `ie4uinit.exe -show` refreshes it, or sign
  out and back in.

The two windows open as Chrome **app windows** (`--app=`) rather than plain
kiosk windows, and that is what puts this icon on the taskbar instead of a
Chrome tile; `--kiosk` still does the fullscreen, no-browser-UI part.

### Relaunching replaces the running till

Running `Start-Kiosk` again **closes the previous till and customer display and
starts fresh.** It does not refuse, and it does not stack a second till on the
same monitor — two would fight over the same screens and the same customer
display, and the usual reason to run it again is wanting a clean start.

The launcher records each window's pid in `%LOCALAPPDATA%\KioskPOS\session`, and
on the next run:

1. Resolves each pid, confirming the process is **still `chrome` or `msedge`** —
   a pid is reused the moment its process ends, so the number alone is never
   enough. Anything else is refused and the stale file deleted.
2. Sends `WM_CLOSE` (what Alt+F4 sends) and waits up to 5 s.
3. Falls back to `taskkill /T /F` only for a window that will not go.

**Closing properly is what gets you back to the login screen.** A killed Chrome
looks like a crash, and Chrome restores a crashed session's session cookies — so
a hard kill brings the last cashier's sign-in back with it. Asking first avoids
that; the crash-restore flags in `$commonArgs` cover the cases where it never
gets to ask.

Pass `-AllowSecondInstance` on a desk genuinely being run as two stations — it
leaves whatever is running alone and opens another window alongside it.

Against a remote/IIS host, pass the real URL and the script will only open the
screens rather than trying to self-host:

```
Start-Kiosk.bat -Url https://pos.yourdomain.local
```

### Why a launcher instead of `window.open`

Chrome **clamps window placement to the current screen** unless the page holds
the `window-management` permission — it accepts out-of-screen coordinates, then
silently ignores them. So an in-page popup cannot be put on the second monitor
without a permission prompt. Chrome's own command line isn't subject to that, so
`--window-position` works, and `--kiosk` gives true fullscreen with no prompt and
no user gesture.

The page still has a popup fallback for when it's opened manually in a browser,
but it needs pop-ups allowed for the site and can only go fullscreen after one
click in the display window.

On two monitors that fallback must stay out of the way, or the till ends up with
a popup display stacked on top of the launcher's kiosk window. `Start-Kiosk.ps1`
opens the till at `/?launcher=1` to say so — but that marker survives only one
navigation: `[Authorize]` bounces the till to `/login`, and sign-in redirects to
a bare `/`. So the login page latches the flag (reading it out of `ReturnUrl`)
into `sessionStorage` under `kiosk.launcherDisplay`, and billing reads the latch
rather than the address bar. `sessionStorage` is per tab, which is exactly the
window the launcher owns, and it holds through every reload, sign-out and
sign-in for the rest of the shift. A till opened by hand in another tab gets no
latch and still opens its own popup, which is what it should do.

---

## Charges

The service charge and tax rates the shop bills at are rows in
`config_terminal_setting`, not constants in a view.

| Column | Type | Notes |
|---|---|---|
| `Id` | `int` identity | PK |
| `Service` | `nvarchar(30)` | Stable key — `SERVICE_CHARGE` or `GST`. Unique |
| `Value` | `decimal(5,2)` | The percentage |
| + audit | | `IsActive`, `CreatedBy`, `CreatedAt`, `UpdatedBy`, `UpdatedAt` |

A row per charge rather than a column per charge: adding a levy later is an
`INSERT` the admin panel can make, not a migration. The cost is that the type is
fixed — `Value` is `decimal(5,2)`, so this table holds rates and amounts, and
anything textual or boolean belongs elsewhere.

Despite the name it is **not keyed by terminal**. These are the rates the whole
shop bills at, and a till taxing differently from the one beside it would be a
bug, not a feature. The name follows the till's own *Terminal Settings* panel,
which is where staff see the values.

`DbSeeder.SeedChargesAsync` inserts `SERVICE_CHARGE` = 10 and `GST` = 5, guarded
the same way as the roles: only missing keys are inserted, so a rate an admin
changed survives every restart, and a charge added to that list later still
lands on databases that are already seeded.

### Who reads and who writes

| Screen | Reads | Writes |
|---|---|---|
| Admin Panel → Settings → Tax & Billing | On page load | `POST /admin/api/charges` — the only writer |
| Till (`/`) | On page load, into `AppState` and the summary labels | Never |
| Customer display | On page load, for the labels before the first cart arrives | Never |

The till's own **Terminal Settings** panel still has Service Charge % and GST %
boxes, and they still work — but only for the cart in front of the cashier, and
they are never written back. That is deliberate: a cashier can zero the service
charge on a bill they are arguing about without quietly changing what the shop
charges everyone else. The shop's rate moves only from the Admin Panel.

`Services/Charges.cs` holds the key names, the defaults and the single
`LoadAsync` so the four callers cannot drift apart. A missing or deactivated row
reads as the seeded default rather than zero — a till that silently stopped
charging service would not be noticed until the takings were counted.

> Tills pick up a change **on their next page load**, not live. There is no push
> for this; the SSE channel carries cart state only. Reload the till (or let the
> next shift open it) after changing a rate.

Everything else under Admin → Settings — Store Info, the print and sound
toggles — is still placeholder markup and saves nothing.

---

## PayNow QR on the customer screen

Terminal Settings → **PayNow QR on customer screen** (default **off**). When on,
the customer display splits: order on the left, a QR panel on the right showing
the amount due. It regenerates live as the cart changes.

Configure **PayNow registered to** (UEN or mobile), the **ID**, and the
**business name** shown in the payer's banking app. Without an ID the QR encodes
the amount only and the panel displays a warning — it is not payable. Settings
persist in `localStorage` (a kiosk relaunches daily and a silently missing QR
would not be noticed).

### Implementation notes

PayNow is **not** a URI scheme. It is an EMVCo merchant-presented QR (SGQR):
a tag-length-value payload ending in a CRC-16/CCITT-FALSE checksum computed over
the whole string *including* the trailing `6304` tag. Built in
`CustomerDisplay.cshtml`:

| Tag | Value | Meaning |
|---|---|---|
| `00` | `01` | Payload format indicator |
| `01` | `12` | Dynamic QR — single use, fixed amount |
| `26` | `SG.PAYNOW` + proxy type (`0` mobile / `2` UEN) + proxy + `0` | Merchant account; final `0` locks the amount |
| `53` | `702` | SGD (ISO 4217 numeric) |
| `54` | e.g. `23.75` | Transaction amount |
| `58` / `60` | `SG` / `Singapore` | Country, city |
| `59` | Business name | Max 25 chars, uppercased |
| `62` | Order sequence number | Bill reference |
| `63` | CRC | 4 hex chars |

A wrong CRC makes banking apps reject the code with no useful error, so the
checksum is validated against the standard CRC-16/CCITT-FALSE check value
(`"123456789"` → `29B1`).

> **Currency is fixed to SGD.** PayNow is Singapore-only. The Currency selector
> in Terminal Settings still changes display symbols, but the QR always encodes
> `702`/SGD. Don't bill in another currency while the QR is on.

> **Not reconciled.** The QR shows what to pay; nothing confirms the payment
> arrived. The cashier still settles the order on the till. Automatic
> reconciliation needs a payment-gateway integration.

---

## Sign-in and roles

Every billing and admin screen requires a sign-in. The `users` table is the only
account store — there is no built-in or fallback login, so a user row is the only
way in.

The first field takes **either the username or the phone number** on the account,
so staff who only know their own number can still sign in. The username is tried
first (unique and indexed); the phone fallback only runs when the text could be a
number at all. Phone numbers are matched on their *digits*, because `users.Phone`
is typed by hand — `+91 98765 43210`, `098765-43210` and `9876543210` all reach
the same account, and once both sides have ten digits only the last ten are
compared, so a stored country code does not have to be retyped. `users.Phone` has
no unique index, so when two accounts share a line the one whose password fits is
signed in.

`AccountController` checks the credentials, then issues a cookie
(`kiosk.auth`, 12 h sliding) carrying the user id, display name and the role code
from `config_role.Code`. The role code is written as `ClaimTypes.Role`, so
`[Authorize(Roles = ...)]` matches on `ADMIN` / `SUPERVISOR` / `CASHIER`
directly. **`WAITER` accounts never sign in** (`Roles.CanSignIn`): they exist only
to be picked on the bill, and the login call refuses them even if an older row
still holds a password.

### Where each role lands

The login form carries an **Open** picker — *Billing POS* or *Admin Panel*,
defaulting to Billing — so signing in takes one step and lands directly on the
chosen screen. There is no second "where to?" page.

| Role | Picks Billing POS | Picks Admin Panel |
|---|---|---|
| `CASHIER` (and roles added in the panel) | Billing screen | Signed in, told *"Cashier accounts cannot open the Admin Panel"*, then sent to billing |
| `WAITER` | Refused: *"Waiter accounts do not sign in"* | Refused |
| `ADMIN`, `SUPERVISOR` | Billing screen | Admin panel |

**The picker is a request, not a grant.** It is sent as `destination` on the
sign-in call and the server only honours it after checking the role, so choosing
*Admin Panel* on a cashier account grants nothing — and it is checked *after* the
password, so it cannot be used to discover which accounts are admins. The gate
that actually protects the panel is `[Authorize(Roles = ...)]` on
`AdminController`; a cashier who types `/admin` is still bounced back to billing.

The split lives in one place, `Roles.CanAdmin` / `Roles.AdminRoles`
(`Kiosk/Services/Roles.cs`), used by the `[Authorize]` filter, the sign-in
response and the header buttons alike — so the gate and the UI cannot drift
apart. Adding a role to the admin side is a one-line change there.

Switching screens afterwards does not go through `/login`: billing has a shield
button in the header (rendered only for admin roles) and the admin panel has
**Open POS** in its sidebar. Visiting `/login` while already signed in never
shows the form again — it redirects to whichever screen the role starts on.

### What stays open

The customer display runs in its own browser profile — the launcher gives that
window its own `--user-data-dir`, so it has a separate cookie jar and can never
hold the till's sign-in. `/customer-display`, `/api/display/stream` and
`/api/screens` are therefore `[AllowAnonymous]`, as is `/api/terminal/close` —
see below for what guards it instead. Everything else on `HomeController` is
gated at the class, so a new billing endpoint is protected by default.

An unauthenticated call to anything under `/api` gets a bare **401**, not the
login page's HTML — otherwise a `fetch` would parse the form as a successful
response.

### Signing out vs closing the terminal

One power button in the billing header, opening a menu with both ways off the
till — they differ only in whether the window survives, so a cashier reaching
for one looks in the same place for the other:

- **Close terminal** ends the shift *and* shuts the window and the customer
  display down.
- **Logout** ends the shift and returns to `/login`; the window stays open.

The login screen carries a **Close Terminal** button of its own, top right. A
till parked at the sign-in screen is the normal state at the end of a day, and
before this it could only be closed from inside a session somebody had to start
first.

Both menu entries warn when the cart has items, and **both sign out** — closing a terminal
without ending the shift meant the next launch reopened straight onto the
previous cashier's till, which is both a surprise and a way for one person's
sign-in to ring up another person's bills. Sign-out is a POST with an antiforgery
token, so a prefetched or mistyped link cannot drop a cashier mid-order.

**Signing out clears the customer display.** Both ways off the till, and the
admin panel's Logout, take the signed-out user's cart off the screen and out of
`DisplayHub`'s replay snapshot, so the display returns to its welcome screen and
a display opened later cannot replay the order. The cart is cleared only if that
user pushed it, so an admin signing out on another PC cannot blank a cashier's
live order. A session that expires by itself (12 hours idle) has no sign-out
request, so its cart stays on the display until someone next uses the till.

**How "close terminal" actually closes.** Chrome refuses `window.close()` on a
window a script did not open, so the page cannot shut itself down — it used to
fall back to telling staff to press Alt+F4, which is not something a desktop app
should ever ask. Now the launcher writes each window's pid to
`%LOCALAPPDATA%\KioskPOS\session`, and the power button calls
`POST /api/terminal/close`, which closes them from the server side:

- **It signs the shift out first.** `SignOutAsync` runs before anything is
  closed, so the cookie is expired on that very response. A no-op when the call
  came from the login screen, where there is no cookie to clear.
- **Loopback callers only, and an antiforgery token required.** The endpoint is
  `[AllowAnonymous]`, because the login screen's power button has no session to
  present and the shift it would end is already over. The token is what replaces
  `[Authorize]` as the cross-site guard, and it has to be: loopback says nothing
  about *who* asked, since a page on any site can have the local browser post
  here. What that page cannot do is read this origin's token or set the
  `RequestVerificationToken` header carrying it. The till's power button sends it
  too, where it previously leaned on the auth cookie being `SameSite=Lax`.
- **Windows are resolved before the response, closed after it.** "A pid file
  exists" is not the same claim as "there is a live window to close" — answering
  with the former made the till skip its fallback and appear to do nothing when
  the files were stale. Closing happens in `Response.OnCompleted`, because
  closing the till mid-reply leaves the page unable to tell a clean close from a
  dropped request.
- **Windows are asked to close, not killed.** `CloseMainWindow()` (the same
  `WM_CLOSE` Alt+F4 sends) with a 5 s grace period, then `Kill` only if ignored.
  This is load-bearing: killing Chrome outright looks like a crash, and Chrome
  **restores a crashed session's session cookies** — which is exactly how a
  closed terminal came back already signed in. `Start-Kiosk.ps1` also passes
  `--disable-session-crashed-bubble`, `--hide-crash-restore-bubble` and
  `--restore-last-session=false` for the times it never gets to ask, such as a
  power cut.
- **A recycled pid cannot be collateral.** The process must still be `chrome` or
  `msedge` before anything is sent to it; stale pid files are logged and deleted.
- **The service stays up**, so the next launch is instant and the scheduled task
  has nothing to restart.
- **The POST goes before `window.close()`, not after.** It used to be the other
  way round, and the browser began tearing the document down while the request
  that closes the windows was still in flight — staff watched the page blank out
  into browser furniture for a second or two. The server-side close is the real
  path and closes both windows, so it goes first; `window.close()` is now only
  the fallback for a window the page's own script opened. The fetch carries
  `keepalive` so it survives the teardown either way.
- **Moving between screens must not add to the window's history.** Chrome also
  allows `window.close()` while a window's history holds a single entry. That is
  the only close available when the server has no window to close, such as a
  hosted app, so logout and the Admin/Billing links use `location.replace`. A
  plain form submit or link adds an entry, and Close Terminal then shows the
  Alt+F4 message after logout.
- **A closing cover is painted before any of it.** Full-screen, branded, and
  never taken down on the way out — Chrome needs a moment to close a window and
  whatever is on screen then is what staff see. It is the same colour as the page
  canvas and the same colour is set on `<html>`, which is what Chrome keeps
  painting once the document is gone, so the window disappears out of a solid
  frame rather than a white flash.
- **Opened by hand rather than by the launcher?** Nothing resolves and the
  endpoint says so. The till — already signed out — keeps the cover up, swaps its
  note for the Alt+F4 hint and returns itself to `/login` behind it, so there is
  no flash of a signed-out till. The login screen drops the cover instead and
  shows the hint in its error strip, since its own power button is still there.

### Login credentials

There is exactly one credential in the source tree — the first-run admin.
Everything else is a row somebody added to `users`.

| Account | Username | Password | Role | Where it comes from |
|---|---|---|---|---|
| Administrator | `admin` | `admin123` | `ADMIN` | Seeded by `SeedFirstAdminAsync` while `users` is empty |

`SeedFirstAdminAsync` (`Kiosk/Data/DbSeeder.cs`) inserts that row only while
`users` is empty, because otherwise a fresh database has no account at all — not
even one that could create the first account. It never runs again once any user
row exists, so deleting the row does not bring it back while other staff remain.

> **Change that password before the till takes real orders.** It is a known
> credential, it is published here, and it is logged as a warning on the
> start-up that creates it.

Sign in at <http://localhost:5034/login> with either the username **or** the
phone number on the account, plus the password, and pick *Billing POS* or
*Admin Panel* on the **Open** picker.

#### Adding the other accounts

Use **Admin Panel → Users & Roles**. Choosing the Waiter role there hides the
username and password fields: the panel stores an internal `w-<name>` username
(the column is required and unique) and an empty password. To insert by hand
instead, `RoleId` is a `config_role.Id`, looked up by its code:

```sql
INSERT INTO users (Username, DisplayName, Phone, Password, RoleId, IsActive, CreatedBy, CreatedAt)
VALUES ('cashier1', 'Cashier One', '9876543210', 'cash123',
        (SELECT Id FROM config_role WHERE Code = 'CASHIER'), 1, 0, GETUTCDATE());
```

Swap `'CASHIER'` for `'SUPERVISOR'` or `'ADMIN'` for the other
roles — those four codes are seeded on every start and are the only ones the
`[Authorize]` filters match. Notes on the columns:

- **`Username` is unique and matched case-insensitively**, so `Cashier1` and
  `cashier1` are the same account. The **password is case-sensitive.**
- **`Phone` is optional** and has no unique index. It is matched on digits
  only, so `+91 98765 43210` and `9876543210` reach the same account. If two
  accounts share a number, the one whose password fits is signed in.
- **`Password` is plain text, by client request** — see *Known constraints*.
  Treat the column as a credential: never log it or send it to the browser.
- **Waiters** need only a name (phone optional): username any unique value,
  password `''`. They never sign in.
- **`IsActive = 0` hides a waiter from the settle dialog** but keeps their
  sales history; accounts with bills against them cannot be deleted.

A waiter only appears on the **Settle Payment** picker if the account is active
*and* on the active `WAITER` role — a cashier who also serves tables needs a
`WAITER` account.

---

## Who a bill belongs to

Three columns on `Orders` answer three different questions, and they are not
interchangeable:

| Column | Question | Set when |
|---|---|---|
| `WaiterId` | Who served the table | Picked on the **Settle Payment** dialog |
| `CreatedBy` | Who rang the bill up | Insert, from the signed-in user — never rewritten |
| `UpdatedBy` / `UpdatedAt` | Who last changed it, in practice who settled it | Every update to an existing bill |

`CreatedBy` and `UpdatedBy` are split because a bill is routinely opened by one
person and closed by another: one cashier saves it unpaid with **Order**, another
settles it hours later. Collapsing them into one column would lose whichever end
mattered. Both are plain ints holding a `User.Id`, matching the audit columns on
the master tables, with `0` (`AuditableEntity.SystemUserId`) for bills that predate
sign-in.

The picker is a row of cards, not a dropdown — a tap on a card beats opening a
list on a touch screen, and the row scrolls sideways, so it reads the same with
three waiters or thirty. *Unassigned* is the first card, so clearing the
attribution is also one tap. The selected card is scrolled into view when the
dialog opens, which matters on a recalled bill credited to someone far along the
row.

**The waiter picker lists active accounts on the active `WAITER` role** — nothing
else, so a cafe where the cashier also serves either gives them a `WAITER` account
or the filter in `HomeController.SelectableWaiters` widens. An empty list is not an
error: the dialog says so and the bill saves unassigned, which is also the right
answer for counter and take-away sales.

The id is re-checked server side on every save (`SelectableWaiters.AnyAsync`), so a
stale dialog cannot attribute a bill to someone who has since been deactivated —
it is rejected with a message rather than written through. Deactivating a waiter
removes them from the picker but keeps their history: the foreign key is
`Restrict`, so an account with sales against it cannot be deleted.

A recalled bill restores its waiter, and the open-bills list shows the name, so two
open tables can be told apart by who is serving them. A **merged** bill takes
whatever the picker shows when it is settled — the absorbed bills keep their own
`WaiterId` on their tombstone rows.

---

## How a bill was paid

**Settle Payment** has five tabs: Cash, Card, QR / UPI, **Mixed** and Split.
**Mixed** is one guest paying with two tenders: type the cash part (it opens on
an even half, and **Half** resets it), pick **Card** or **QR / UPI** for the
balance, and the balance is worked out for you. Both parts must be more than
zero; otherwise it is a plain cash or card bill and the other tabs apply.
**Split** is different: several people sharing one bill.

Each settled bill writes its tenders to `order_payment`, one row per method:

| Column | Meaning |
|---|---|
| `Method` | `cash`, `card`, `qr` (QR / UPI) or `split` |
| `Amount` | What this method paid towards the bill |
| `Tendered` / `ChangeGiven` | Cash handed over and change returned (cash rows only) |
| `ReceivedBy` / `PaidAt` | Signed-in user who took it, and when |

A mixed bill has two rows, so the cash drawer and the card or UPI terminal each
reconcile against their own total. The server rejects a settle whose rows do not
add up to the bill's `NetTotal` (within one cent). Split bills record a single
`split` row; the per-person amounts stay on the order (`SplitCount` /
`SplitAmount`). Bills settled before this table existed have no rows.

---

## Held orders

**Hold** (F6) parks the cart in its own pair of tables, `held_order` and
`held_order_item`, not in `Orders`. A hold is not a bill: it takes no order
number and never appears in takings. It gets a number only once it is recalled
and then saved with **Order** or settled.

Because holds live in the database rather than in the page, they survive a
reload, a sign-out or a restart, and **any till can recall a hold made on
another**. The Held list shows which till and which user parked each one.

`held_order` only ever contains what is parked right now. **Recalling or
discarding a hold deletes its row**, and its `held_order_item` rows go with it by
cascade. The recalled cart then lives on the till until it is saved with **Order**
or settled, which writes it to `Orders` like any other bill. The delete is a
single conditional `DELETE`, so if two tills tap the same hold at once only one
gets the cart; the other is told it is gone.

- The Held list is not limited to today — a hold left over at closing is still
  there in the morning to recall or discard.
- A bill recalled from **Open Orders** (or one with a merge staged) cannot be
  held: it already has a row in `Orders`. Save it with **Order** instead.
- Recalling needs an empty cart, so a hold never silently replaces work in progress.
- The total stored on a hold is for the list only; recalling recalculates at
  the current rates.

---

## End of Day archive

The **EOD** icon in the till header (Admin and Supervisor only) moves bills out of
the live tables into archive tables, so `Orders`, `OrderItems` and `order_payment`
only ever hold the day in progress:

| Live table | Archive table |
|---|---|
| `Orders` | `order_archive` |
| `OrderItems` | `order_item_archive` |
| `order_payment` | `order_payment_archive` |

Payments move too: `order_payment` hangs off `Orders` by foreign key, and the
reports name the cashier from who took the money.

- **What moves:** every bill except today's unpaid ones (and merge tombstones
  pointing at them), which the till still needs to recall, merge and settle. An
  unpaid bill from an earlier day is archived as it is — the till only recalls
  today's, so it would otherwise sit in `Orders` for ever.
- **One transaction.** `OrderHistory.ArchiveAsync` copies with `INSERT … SELECT`
  and deletes in a single SQL batch, so a bill is never in both tables or neither.
- **Ids are kept.** Archive rows carry the live row's `Id` (not an identity), so a
  bill has one id for life. This relies on the live identity only counting up —
  never `DBCC CHECKIDENT … RESEED` or `TRUNCATE` the live order tables.
- **Order numbers carry on.** The next number is read across both tables, so EOD
  run mid-day continues at #023 rather than restarting at #001.
- **Reads use UNION ALL.** Dashboard, reports, the admin Orders list and bill
  detail, and the "dish / waiter has history" checks read through
  `OrderHistory.AllOrders()` / `AllOrderItems()` / `AllPayments()`, which
  `UNION ALL` live and archive. ALL, not UNION: a bill is in exactly one table, so
  there is nothing to de-duplicate. Every figure is the same before and after EOD.
  The Orders list and bill detail return `archived: true` for archived bills.
- **Writes stay live.** Placing, settling, merging and recalling only touch
  `Orders`; an archived bill is history and cannot be changed.
- **Indexes:** `order_archive` has non-clustered `(BusinessDate DESC, Status)` and
  `(BusinessDate DESC, WaiterId)`; `order_payment_archive` has
  `(PaidAt DESC, Method)`. The clustered key is `Id`. Archive column types match
  the live tables exactly — EF refuses a UNION whose two sides differ — so a
  column added to `Orders` must be added to `order_archive` (and to the
  `INSERT` in `ArchiveAsync`) in the same migration.
- Held carts (`held_order`) are not bills and are not archived.

## Staff attendance

`staff_attendance` holds **one row per person per trading day**: the first time
in, the last time in, the last time out and how many times they came in.

- **Admin, Supervisor, Cashier** are clocked by signing in and out. Sign-in
  starts the day's row (or bumps it on a later sign-in); Logout and **Close
  Terminal** both stamp the time out.
- **Waiters** never sign in, so the till punches them: the green user button in
  the billing header opens *Waiter Attendance* — pick a waiter and **Punch In**;
  everyone on shift is listed below with a **Punch Out** button. The badge on the
  button counts who is on shift.
- **Admin panel → Attendance** shows one day at a time: who came in, first in,
  last out, hours, times in, and status (*On shift*, *Out*, *No logout*).

Rules worth knowing:

- **Hours = first in to last out.** A cashier who signs out for lunch and back in
  has one row, and the break sits inside the hours.
- Going in always lands on **today's** row. Going out closes the latest open row
  from today or yesterday, so a shift past midnight closes on the day it began.
- A row still open from an earlier day is shown as **No logout**; its hours stop
  at the last time out recorded rather than running on forever.
- A cookie that simply expires is not a sign-out, so a till left signed in
  overnight shows as *No logout* the next day.
- Two tills signed in as the same person share one row; signing out on either
  closes it.
- Attendance never blocks sign-in or sign-out: a failed write is logged and the
  sign-in/out goes ahead.
- A user with attendance rows cannot be deleted (the FK is `Restrict`);
  deactivate them instead, as with staff who have bills.

---

## Shop country, time zone and date format

The shop's country is set in `appsettings.json`, not taken from the server.
Left out (or empty), it is **Singapore**:

```json
"Pos": {
  "Country": "Singapore",
  "DateFormat": "",
  "TimeFormat": ""
}
```

| `Country` | Time zone | Dates | Times | Numbers | Currency |
|---|---|---|---|---|---|
| `Singapore` (default) | UTC+8 (`Asia/Singapore`) | `05 Nov 2026` | `02:30 pm` | `100,000` | SGD `$` |
| `India` | UTC+5:30 (`Asia/Kolkata`) | `05-11-2026` | `02:30 pm` | `1,00,000` | INR `₹` |

`DateFormat` and `TimeFormat` are optional .NET format strings that override the
country's (`dd/MM/yyyy`, `HH:mm` for a 24-hour clock); leave them empty to use
the defaults above.

This is what makes the server's location irrelevant. Every timestamp is stored
in UTC; `BusinessClock` turns it into the shop's day and time, and the screens
format through `Views/Shared/_ShopClock.cshtml` with the same zone and locale —
the till PC's own zone and language are ignored too. The trading day, order
numbers, End of Day, attendance, every date and time on screen, and the currency
on the till, admin panel and customer display all follow it. (A cashier can
still switch the till's currency symbol for the session from POS Settings.)

* An **unknown** `Country` (a typo, or one not in the list) **stops the app at
  startup** with a message naming the supported countries, rather than silently
  becoming Singapore or the server's zone and dating bills wrongly.
* Changing the country of a shop that already has bills is safe for stored
  data (it is all UTC), but the trading day boundary moves, so do it after End
  of Day.
* To add a country, add a line to `ShopCountry.Known` in
  `Services/ShopCountry.cs` with its Windows and IANA zone ids, culture,
  formats and currency.

## Deployment checklist

### 1. Database

Set the connection string in `Kiosk/appsettings.json`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=YOUR-SQL-HOST;Database=POS;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

- LocalDB (`(localdb)\MSSQLLocalDB`) is **development only** — it won't exist on a server.
- Prefer Windows auth (`Trusted_Connection=True`). If you must use SQL auth, keep
  the credentials out of source control — use environment variables, user-secrets,
  or IIS configuration editor, not a committed `appsettings.json`.

### 2. Migrations

Nothing applies migrations at startup, so a fresh machine starts with no schema:

```bash
dotnet ef database update --project Kiosk
```

`config_terminal_setting` is seeded on every start with the service charge
(**10%**) and tax (**5%**) rows, inserting only what is missing — a rate an admin
has since changed is never reset by a restart. See **Charges** below.

`config_store` holds one row of store details — App Name, Store Name, Address,
Phone, GST No. — seeded once while the table is empty. Admin › Settings edits
everything except App Name, which is shown read-only; change it in the table if
it ever needs to differ from `NexBill`. App Name is the product name printed on
every header (Admin sidebar, till, login, customer display) and browser tab; it
is read through `AppBranding`, which falls back to `NexBill` if the row is
missing or the database is down.

`config_transaction_mode` holds the Mode dropdown of the till's Transactions popup
(cash in / cash out, stored in `cash_transaction`). It is seeded once while the
table is empty, so a mode an admin deleted does not return on restart. Run the
migration **before** starting a new build: the seed reads this table at start-up,
so the app will not start until it exists. Admin › Transaction Modes manages it.
Deleting a cash entry only sets `IsActive = 0`, so its number is never reused.
Cash entries are not part of End of Day or the sales reports.

Staff logins live in two tables. `config_role` is seeded on every start with the
four fixed roles (`ADMIN`, `SUPERVISOR`, `CASHIER`, `WAITER`), and an `admin`
account is seeded once while `users` is empty — see **Sign-in and roles** above,
including the warning about changing its password, and the note under Known
constraints about how `users.Password` is stored.

### 3. Publish

```bash
dotnet publish Kiosk/Kiosk.csproj -c Release -o C:\inetpub\pos
```

That output holds the app only. The till-side scripts are **not** in it: add
`-p:KioskScripts=PreserveNewest` to the command above if the till runs the app
from this very folder, and they land beside `Kiosk.exe` ready to install from
(no SDK needed on the till).

Hosting the app instead — on any box the tills reach over the network — wants
the plain command. The terminal never touches this folder; it gets a client kit,
built from the source tree:

```powershell
cd Kiosk
.\Build-ClientKit.ps1 -Url https://pos.example.com
```

`Build-ClientKit.ps1` copies the four scripts and `app.ico` from the folder it
sits in, so run it from `Kiosk\` and it needs nothing from `publish\`.

### 3a. Install on the terminal

From the published folder, signed in as the till's own account, double-click:

```
Install-Kiosk.bat
```

See [Installing it as a desktop app](#installing-it-as-a-desktop-app) — including
why the auto-start task runs at logon rather than at boot.

### 4. IIS app pool settings — required, not optional

The SSE stream is a long-lived connection and `DisplayHub` holds cart state in
process memory. Defaults will break it:

| Setting | Value | Why |
|---|---|---|
| Idle Time-out | `0` | Default 20 min kills the app; the display goes blank |
| Maximum Worker Processes | `1` | A web garden puts till and display in separate processes with separate `DisplayHub` instances — the cart never arrives |
| Start Mode | `AlwaysRunning` | Avoids a cold start on the first order |
| Regular Time Interval (recycling) | `0` | A recycle drops every open SSE stream |
| Dynamic compression | **disabled** for `api/display/stream` | IIS buffers compressed responses and stalls SSE until the buffer fills |

Also turn on forwarded headers if terminating HTTPS at IIS (see
[4a](#4a-rate-limiting-and-forwarded-headers)), or `UseHttpsRedirection()` can
cause redirect loops.

### 4a. Rate limiting and forwarded headers

Every request is rate-limited per client IP, using fixed-window counters set in
`appsettings.json`:

```json
"RateLimiting": {
  "Enabled": true,
  "Global": { "PermitLimit": 600, "WindowSeconds": 60 },
  "Login":  { "PermitLimit": 5,   "WindowSeconds": 60 }
},
"ForwardedHeaders": {
  "Enabled": false,
  "KnownProxies": []
}
```

| Limit | Applies to | When exceeded |
|---|---|---|
| `Global` | Every request: pages, static files, `/api` | Pages show the "Too many requests" status page; `/api` calls get `429` + `{ "error": ... }` |
| `Login` | `POST /api/account/login`, counted on top of `Global` | The sign-in screen shows "Too many sign-in attempts. Wait a minute and try again." |

- Every rejection carries a `Retry-After` header and logs a `Kiosk.RateLimit`
  warning with the client IP, which is the first place to look when a till
  reports the message.
- `/error/*` is exempt, so the page that explains a rejection can still render.
- `Login` counts every attempt, successful or not. Several staff signing in one
  after another on the same till all share its 5 per minute. Raise it if shift
  change hits it.
- `Global` is set to catch a runaway client, not a busy till. A cold load of
  billing pulls a few dozen assets, and the customer display posts on every cart
  change. If a busy till ever sees the status page, raise this first.
- To switch both off, set `"Enabled": false`. A restart is needed: limits are
  read at startup.
- Environment-variable form, e.g. in IIS:
  `RateLimiting__Login__PermitLimit=10`.

**Behind IIS, a load balancer or any reverse proxy, set `ForwardedHeaders:Enabled` to
`true`.** Without it, the app sees every till as the proxy's IP, so all tills
share one bucket. One till mistyping a password five times would then lock out
sign-in shop-wide. Put the proxy addresses in `KnownProxies` when they are fixed
(IIS on the same box: `["127.0.0.1", "::1"]`). Leave it empty for a load balancer whose
addresses change, but only when the app's port cannot be reached except through
the load balancer. Otherwise a client can send its own `X-Forwarded-For` and pick its own
IP. On the shop LAN with tills connecting directly, leave it `false`.

### 5. Virtual directories

Already handled — client-side calls are built from `@Url.Content("~/")`, so the
app works at a site root or under a sub-application such as `https://server/pos`.
If you add new `fetch` calls, **build them from `BASE`**, never from `/api/...`.

---

## Terminals as thin clients

The till does not have to run the app. Point it at a server on the network and the
terminal needs nothing but a browser — the launcher already detects a non-local
URL and opens the two screens instead of trying to self-host.

### Hosting it on the shop LAN

The common small setup: the app runs on one PC behind the counter, the other
tills point at it by IP. Build the kit with that address — the trailing slash is
trimmed for you:

```powershell
.\Build-ClientKit.ps1 -Url http://192.168.1.6:5034
```

It warns that this is plain HTTP and builds anyway. On an isolated shop LAN that
is a reasonable trade; understand what you are accepting — **staff passwords and
the session cookie cross the network in clear text**, readable by anything else
on that network, including the guest Wi-Fi if it is not a separate VLAN.

Then four things on the server PC, none of which the kit can do for you:

1. **Bind to all interfaces, not loopback.** The default is `localhost`, which
   accepts loopback only — every till gets *connection refused* while the browser
   on the server itself works perfectly. Start the published app with:

   ```
   set ASPNETCORE_URLS=http://0.0.0.0:5034
   Kiosk.exe
   ```

2. **Serve HTTP only — do not also configure an HTTPS endpoint.**
   `app.UseHttpsRedirection()` is unconditional. With no HTTPS port to find it
   logs a warning and passes requests through, which is what you want here. Give
   it one (for instance by launching the `https` profile, which binds 7150) and
   every till is redirected to `https://192.168.1.6:7150`, whose development
   certificate is issued for `localhost` — so the till shows a certificate error
   and nothing loads.

3. **Open the port in Windows Firewall** on the server, or the tills time out:

   ```powershell
   New-NetFirewallRule -DisplayName 'NexBill 5034' -Direction Inbound `
       -Protocol TCP -LocalPort 5034 -Action Allow -Profile Private
   ```

   `-Profile Private` keeps it off any public network the PC later joins.

4. **Pin the address.** `192.168.1.6` from a DHCP pool will eventually be handed
   to something else, and then every till is dead at once with no obvious cause.
   Reserve it on the router, or give the PC a static address.

Keep the server PC awake, too — sleep takes the whole shop's tills down.

### Step by step

**1. Build the client kit**, once, naming the address staff will reach:

```
.\Build-ClientKit.ps1 -Url https://pos.example.com
```

That produces a `client-kit` folder holding `Start-Kiosk.ps1`,
`Start-Kiosk.bat`, `Install-Kiosk.ps1`, `Install-Kiosk.bat`, `app.ico` and a
`README.txt`. The
address is **written into that copy's `$url` setting**, so the installer on the
till needs no editing and asks nothing. It refuses a `localhost` URL and warns on
plain `http`. **Do not copy the published app to a till** — it needs none of it.

**2. On each terminal**, copy that folder anywhere local (`C:\NexBill` is fine),
then **signed in as the account the till will actually be used on**, double-click:

```
Install-Kiosk.bat
```

Nothing to type — the kit's copy already points at `https://pos.example.com`. You
get a *NexBill* icon on the Desktop and in the Start menu, and a logon task so
the till comes back by itself after a power cut. The address is baked into both,
so nobody types it again.

**3. Staff double-click the icon.** That is the whole daily workflow.

### Changing the URL later

Edit `$url` at the top of that terminal's `Install-Kiosk.ps1` and run it again.
It removes the shortcut and task it installed before — whatever they were named,
whatever they pointed at — and writes new ones:

```powershell
$url = 'https://pos-new.example.com'
```

```
Install-Kiosk.bat
```

Or for a one-off without touching the install:

```
.\Start-Kiosk.bat -Url https://pos-new.example.com
```

> **Edit the `$url` line and nothing else — don't hand-edit the shortcut.** On an
> installed terminal the address sits in three places: that settings line, the
> shortcut's `-Url` argument, and the logon task's. The re-run rewrites the last
> two for you. Change only the shortcut's properties and the task keeps the old
> address, so the till comes back on the *old* server after a reboot while the
> desktop icon opens the new one — a split that is miserable to diagnose. Check
> all three agree with `Install-Kiosk.bat -Status`.

Handing out fresh kits after a move? Rebuild instead, so new terminals need no
editing at all — `.\Build-ClientKit.ps1 -Url https://pos-new.example.com`.

Because this is per-terminal by hand, point the tills at a DNS name you control
and change the record rather than the terminals. The next migration then touches
no terminal at all.

### What behaves differently against a remote host

- **The launcher will not start the app.** It only self-hosts for `localhost`;
  for anything else it waits for the site to answer, up to
  `-StartTimeoutSeconds` (90 by default), because an idle site can take well over
  3 seconds on its first request. It then fails with `Cannot reach <url>` and the
  last error rather than silently opening a dead window.
- **The power button signs out but cannot close the till window.**
  `/api/terminal/close` finds the windows through pid files under the launching
  user's `LOCALAPPDATA`, so it only resolves when the app and the browser are the
  same user on the same machine. A remote server looks on its own disk, finds
  nothing, and answers `launcherSession: false`. The shift still ends and the page
  returns to `/login`; **Alt+F4** closes the till window. This is the one place
  the desktop-app feel degrades, and it is a fair reason to keep the app on the
  till.
- **The customer display does still close, because the launcher closes it.**
  `Start-Kiosk.ps1` stays running for the shift and owns both windows: whatever
  ends the till — the power button, Alt+F4, a crash, Task Manager — the display
  goes with it. It has to be done locally; the display cannot close itself either,
  since Chrome refuses `window.close()` on a window no script opened, which is why
  a hosted till used to be left with a lit customer screen showing the welcome
  card. Pass `-NoWatch` to get the old launch-and-exit behaviour.
- **`/api/screens` returns an empty array**, as designed — it describes the
  *server's* monitors, which say nothing about the cashier's desk. The launcher
  reads monitors client-side, so dual-screen placement is unaffected.

---

## Known constraints

- **`/api/screens` describes the server's monitors.** It's only meaningful when
  the browser and server are the same machine, so it returns an empty array for
  non-loopback callers rather than handing back another machine's layout. Under
  IIS this is expected — use the launcher, which reads monitors client-side.
- **`ScreenInfo` is Windows-only** (`user32.dll`). It returns an empty list
  elsewhere; callers already degrade gracefully.
- **Cart state is in-memory and single-process.** It is not shared across
  servers and does not survive a restart. Fine for one till; a multi-till
  deployment would need the state keyed per terminal and moved out of process.
- **Menu items have no image column.** The customer display uses letter avatars
  tinted from a hash of the dish name. Photos would need a schema change plus files.
- **Audit columns are `CreatedBy`/`UpdatedBy int` + `CreatedAt`/`UpdatedAt
  datetime`,** shared by `Categories`, `MenuItems`, `config_terminal`,
  `config_role`, `config_terminal_setting`, `config_store` and `users`. The `By` columns hold a `users.Id`, with
  `AuditableEntity.SystemUserId` (0) for rows the app wrote itself — seeded
  master data, for instance. They are deliberately *not* foreign keys: seeding
  runs before any user row exists. Saving the charges in the Admin Panel is so
  far the only screen that stamps them, so `UpdatedBy`/`UpdatedAt` stay null on
  every other table until an edit screen sets them.
- **`users.Password` is plain text, by client request.** Logins are a short
  mix of digits and letters stored exactly as typed, so a DB read is a read of
  every staff password. Keep the database off shared hosts, grant it to the app
  account only, and never return the column to the browser or write it to a log.
  Switching to a hash later is a column widening plus one migration.
- **Usernames are matched case-insensitively, passwords are not.** The sign-in
  lookup lowercases both sides (`u.Username.ToLower() == username`) rather than
  relying on the database collation, so `Cashier` and `cashier` are the same
  account on any server. The password is compared in C# with `!=`, so its case
  matters.
- **The phone fallback compares in memory, not in SQL.** SQL Server cannot strip
  punctuation from `users.Phone` in a predicate EF can translate, so the accounts
  that *have* a number are loaded and compared in C# (`Kiosk/Services/PhoneNumbers.cs`).
  `users` holds one row per staff member — not customers — so this stays a handful
  of rows; it is the one lookup that would need rethinking if staff ever numbered
  in the thousands. A username sign-in never touches it.
- **Sign-in is not rate limited.** A failed attempt is logged with the username
  or phone number as typed and nothing else; there is no lockout, delay or attempt counter. On a till
  behind a counter that is the intended trade-off, but do not expose `/login` to
  an untrusted network as it stands.
- **One sign-in per browser profile, not per terminal.** The cookie is the whole
  session, so a shared till keeps whoever signed in last until someone signs out
  or 12 h pass. Nothing records who rang a bill either: `Order` is not an
  `AuditableEntity` and has no `CreatedBy` column, so attributing takings to a
  cashier needs a schema change as well as reading the claim on checkout.
- **The dashboard reads the database; the rest of the admin panel does not.**
  The four KPI cards, the hourly chart, top sellers and recent orders all come
  from `/admin/api/dashboard` — nothing on that section is hard-coded, and the
  refresh button in the top bar re-reads it. The panel also re-polls every 60 s,
  but only while the tab is visible *and* the dashboard section is the one on
  screen, so an admin panel left open on another tab costs nothing. The Menu,
  Orders, Staff and Reports sections are still front-end demo data.
- **What the dashboard figures actually mean.** Worth knowing before anyone
  reconciles them against a till report:
  - *Today Revenue* and *Orders* count bills with `Status = "completed"` on the
    current `BusinessDate`. Unpaid bills (`"ordered"`) are excluded, and so are
    merge tombstones (`"merged"`), which keep their amounts for history and would
    otherwise be counted a second time on the combined bill.
  - The *vs yesterday* lines compare against the previous `BusinessDate`. With no
    takings yesterday the card says "No baseline" rather than quoting a
    percentage off zero.
  - *Active Staff* is **users who signed in today**, counted off `users.LastLoginAt`
    — there is no shift table, so a sign-in is the only shift signal the schema
    has. Someone who works a shift without signing in does not appear.
  - *Top Sellers* excludes FOC lines: they were given away, so they are not sales.
- **Dashboard hours depend on `Pos:Country`.** `Orders.CreatedAt` and
  `users.LastLoginAt` are stored in UTC, so the hourly chart's buckets and the
  "signed in today" window are both derived by converting through
  `BusinessClock` (`ToLocal` / `UtcWindow`) in the country's zone — the same
  setting that drives daily order numbering. A missing or unknown country stops
  startup rather than falling back to the server's zone. The "2 min ago" column is
  the exception: it is sent as UTC and aged by the browser, so it stays right
  even on a machine set to another zone.
- **A page cannot self-fullscreen on load** — browsers require a user gesture.
  This is why kiosk mode is launched from the command line.
- **Static-asset compression is off in Debug, on purpose.** SDK 10.0.401 keeps
  Debug's pre-compressed assets in `obj\` under names containing a literal `{0}`
  and `MapStaticAssets` fails to resolve them: any browser sending
  `Accept-Encoding: gzip` gets **200 with an empty body**. It went unnoticed for
  as long as every script came from a CDN; serving Tailwind and the fonts locally
  made it break every screen in development. `<CompressionEnabled>false</CompressionEnabled>`
  under the Debug `PropertyGroup` in `Kiosk.csproj` is the workaround. **Publish
  is unaffected** — it writes real `.gz`/`.br` files next to each asset, verified
  serving Brotli correctly — so production keeps its compression. Re-test and
  drop the property when the SDK fixes it.
- **The scaffolded Bootstrap layout is gone.** `_Layout.cshtml`,
  `_Layout.cshtml.css` and the dead `Privacy.cshtml` (it had no controller action,
  so the navbar link 404'd) were deleted, and `_ViewStart.cshtml` now sets
  `Layout = null`. Every screen is a full-window page that owns its own `<html>`;
  defaulting to no layout means a new view cannot accidentally render a navbar and
  footer inside a frameless kiosk window. `wwwroot/lib/bootstrap`, `jquery` and
  `_ValidationScriptsPartial.cshtml` are now unreferenced but were left in place.
- **The auto-start task is per-user.** `Install-Kiosk.ps1` registers it for the
  account that ran it. Installing under one account and running the till under
  another gets you shortcuts but no auto-start.

---

## Troubleshooting

| Symptom | Cause |
|---|---|
| View edits seem ignored | Razor compiles at build time — rebuild and restart |
| Nobody can sign in on a fresh database | Migrations not applied, so `users` does not exist and the first-admin seed never ran. Run `dotnet ef database update` and restart |
| Sign-in returns 400 with no message | The antiforgery token on the page no longer matches the session. Reload `/login` |
| A `fetch` suddenly returns 401 | The 12 h cookie expired. The page needs reloading to sign in again |
| Admin panel redirects straight back to billing | That account's role is not `ADMIN` or `SUPERVISOR` — check `users.RoleId` against `config_role` |
| Changed a rate in the Admin Panel but the till still bills the old one | Rates are read at page load, not pushed. Reload the till. If it persists, a cashier has overridden it in the till's own Terminal Settings for that session |
| Service charge or tax is billing at 10% / 5% after being set to something else | The `config_terminal_setting` row is missing or `IsActive = 0`, which reads as the seeded default. Saving from the Admin Panel reactivates it |
| Customer display never opens, with no message on the till | The till was opened without the launcher's second-screen window and Chrome blocked the popup. Allow pop-ups for the site in that browser profile, or start it with `Start-Kiosk.bat` on two monitors |
| Customer display stuck on "Welcome" | SSE stream blocked; check app pool idle timeout and dynamic compression |
| Display opens on the wrong monitor | Launched from the page instead of `Start-Kiosk.bat` — Chrome clamps placement without the `window-management` permission |
| A second, windowed display pops up over the launcher's customer screen | The `launcher=1` latch was lost — the till was opened by hand rather than by `Start-Kiosk.bat`, or `sessionStorage` is blocked for the profile. Relaunch with `Start-Kiosk.bat` |
| Customer window isn't fullscreen | `--kiosk` was ignored because the profile was shared; each window needs its own `--user-data-dir` |
| Cart updates stop after idle | App pool recycled or timed out; set Idle Time-out and Regular Time Interval to `0` |
| Every screen is unstyled, console says `tailwind is not defined` | Static assets served with an empty body. In development check `<CompressionEnabled>false</CompressionEnabled>` is still in `Kiosk.csproj`; see Known constraints |
| Relaunching closed the till I wanted to keep | That is the default — a relaunch replaces the running windows. Pass `-AllowSecondInstance` to open another one alongside instead |
| Relaunch leaves the old window open | Its pid file was stale or named a non-browser process, so the launcher would not touch it. Check `%LOCALAPPDATA%\KioskPOS\session` and close the window by hand |
| Works in a browser on the server, `Cannot reach` from every till | The app is bound to `localhost`, so it accepts loopback only. Start it with `ASPNETCORE_URLS=http://0.0.0.0:<port>`, and open the port in Windows Firewall. See [Hosting it on the shop LAN](#hosting-it-on-the-shop-lan) |
| `Cannot reach https://...` on a terminal | The launcher waited the full `-StartTimeoutSeconds`, so this is not a slow first request: the hosted site is down, the name does not resolve, or TLS failed. The message ends with the last error. Open the URL in a normal browser on that machine first |
| Another café's order appears on a customer display | One `DisplayHub` is shared by every terminal on the server. See [Terminals as thin clients](#terminals-as-thin-clients) — it must be keyed per terminal before two sites share one server |
| Customer display blank only when hosted | The load balancer is buffering, compressing, or timing out `/api/display/stream`, or the till and display landed on different instances |
| Customer display stays on screen after closing the till | The launcher that tears the pair down is not running — it was killed, or the till was started with `-NoWatch`. On one monitor instead: the till page closes its own popup, so this means the page errored before that. Relaunch with `Start-Kiosk.bat` |
| Power button still says "Press Alt+F4" | The window was not opened by `Start-Kiosk.bat`, so there is no live pid to close — or the app is hosted remotely, where it cannot close a window on your machine. You are signed out either way and the page returns to `/login` |
| Reopening the till lands on billing, not the login screen | The previous shift was not signed out. Check the browser is closing cleanly rather than being killed — a crash-restored Chrome session brings its session cookies back with it |
| Shortcut shows the generic or a blurry icon | No `app.ico` beside the installer, or an old 16/32-only `favicon.ico`. Run `scripts/make-icons.py`, publish, re-run `Install-Kiosk.ps1`; the installer warns when it finds no icon at all. If the file is right, it is Explorer's icon cache — `ie4uinit.exe -show` |
| Taskbar shows the Chrome icon, not the app's | Chrome cached the app-window icon for that profile. Delete `%LOCALAPPDATA%\KioskPOS\till` and `...\display`, then relaunch |
| “The publisher could not be verified” on every launch | The kit still carries the downloaded-from-elsewhere tag Windows puts on files arriving by zip, USB or network share, and a tagged `.bat` re-asks every single time. Re-run `Install-Kiosk.ps1` — it strips the tag and prints what it cleared. Still asking? The folder is being run from `\\server\share`; copy it to a local disk (`C:\NexBill`) and install from there |
| Shortcut works but the till doesn't start after a reboot | The scheduled task is registered per user; it only fires for the account that ran `Install-Kiosk.ps1` |
| Totals update but notes/discounts don't | A new mutation path isn't posting — the 200 ms watcher should catch it; check the browser console for failed POSTs |
| Banking app rejects the PayNow QR | Check the UEN/mobile is exactly as registered for PayNow; mobile must be `+65…`. A CRC or TLV length error also causes a silent reject |
| QR panel blank, says "library unavailable" | `qrcodejs` is loaded from cdnjs — the till needs internet, or host the library locally |
