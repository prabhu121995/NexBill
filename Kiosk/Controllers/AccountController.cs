using System.Security.Claims;
using Kiosk.Data;
using Kiosk.Models;
using Kiosk.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Controllers;

/// <summary>
/// Staff sign-in. The <c>users</c> table is the only source of accounts — there is
/// no fallback or built-in login, so an empty table means nobody can sign in.
/// </summary>
public class AccountController(AppDbContext db, Attendance attendance, DisplayHub displayHub, ILogger<AccountController> log) : Controller
{
    /// <summary>
    /// Claim holding the role code from <c>config_role.Code</c>. Also written as
    /// <see cref="ClaimTypes.Role"/> so <c>[Authorize(Roles = ...)]</c> works.
    /// </summary>
    public const string RoleNameClaim = "kiosk:role_name";

    [HttpGet]
    [Route("login")]
    public IActionResult Login()
    {
        // Already signed in: never show the form again, or a second sign-in would
        // silently replace the current shift. Send them to the screen their role
        // starts on — switching between the two afterwards is done from the
        // header buttons, not from here.
        if (User.Identity?.IsAuthenticated == true)
        {
            return Roles.CanAdmin(User.FindFirstValue(ClaimTypes.Role))
                ? RedirectToAction(nameof(AdminController.Index), "Admin")
                : RedirectToAction(nameof(HomeController.Index), "Home");
        }

        return View();
    }

    /// <summary>
    /// Validates the credentials against <c>users</c> and issues the auth cookie.
    /// Answers JSON so the sign-in card can show the error inline, and returns the
    /// screen to open.
    ///
    /// The identifier is either the username or the account's phone number — staff
    /// at the counter know their own number and often not the name an admin typed
    /// for them, so both are accepted and the password is what actually proves the
    /// identity.
    ///
    /// <c>destination</c> from the form is a request, never a grant: an account
    /// that cannot reach the admin panel is signed in and sent to billing with a
    /// <c>notice</c>, so picking the wrong button costs nothing and reveals
    /// nothing. The gate that actually protects the panel is
    /// <see cref="AdminController"/>'s <c>[Authorize]</c>.
    /// </summary>
    [HttpPost]
    [Route("api/account/login")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(Kiosk.Services.RateLimiting.LoginPolicy)]
    public async Task<IActionResult> SignIn([FromBody] LoginRequest req)
    {
        var identifier = (req.Identifier ?? "").Trim();
        var password = req.Password ?? "";

        if (identifier.Length == 0 || password.Length == 0)
            return BadRequest(new { error = "Enter your username or phone number, and your password" });

        var user = await FindAccountAsync(identifier, password);

        // One message for every failure. Saying which half was wrong would confirm
        // that an account exists.
        const string rejected = "Invalid username / phone number or password";

        if (user is null || user.Password != password)
        {
            log.LogWarning("Failed sign-in for {Identifier}", identifier);
            return Unauthorized(new { error = rejected });
        }

        if (!user.IsActive)
            return Unauthorized(new { error = "This account is disabled. Ask a supervisor." });

        // A role that was switched off must not grant access, and a RoleId with no
        // row behind it has nothing to authorise against.
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == user.RoleId);
        if (role is null || !role.IsActive)
            return Unauthorized(new { error = "This account has no active role. Ask a supervisor." });

        var roleCode = role.Code;

        // Waiters are picked on the bill, not signed in. Older waiter rows may still
        // hold a password from before that rule, so the role is checked as well.
        if (!Roles.CanSignIn(roleCode))
            return Unauthorized(new { error = $"{role.Name} accounts do not sign in. The cashier picks the waiter when settling a bill." });

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.GivenName, user.DisplayName),
            new(ClaimTypes.Role, roleCode),
            new(RoleNameClaim, role.Name)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // The first sign-in of the day starts the shift in staff_attendance.
        await TryClockAsync(() => attendance.ClockInAsync(user.Id, StaffAttendance.SourceLogin, user.Id), user.Id);

        var canAdmin = Roles.CanAdmin(roleCode);
        var wantsAdmin = string.Equals(req.Destination, "admin", StringComparison.OrdinalIgnoreCase);

        return Ok(new
        {
            displayName = user.DisplayName,
            role        = roleCode,
            roleName    = role.Name,
            canAdmin,
            redirect    = wantsAdmin && canAdmin
                ? Url.Action(nameof(AdminController.Index), "Admin")
                : Url.Action(nameof(HomeController.Index), "Home"),
            notice      = wantsAdmin && !canAdmin
                ? $"{role.Name} accounts cannot open the Admin Panel. Opening Billing POS."
                : null
        });
    }

    /// <summary>
    /// Finds the account the identifier names: the username first, then the phone
    /// number. Usernames are unique and indexed, so that lookup is a single seek;
    /// the phone fallback only runs when the text could be a number at all.
    ///
    /// Phone numbers are matched on their digits, because <c>users.Phone</c> is
    /// typed by hand and the punctuation never matches what someone taps at the
    /// terminal. SQL Server cannot strip that punctuation in a translatable
    /// predicate, so the accounts that have a number are compared in memory —
    /// <c>users</c> holds one row per staff member, not customers, so this stays a
    /// handful of rows.
    /// </summary>
    private async Task<User?> FindAccountAsync(string identifier, string password)
    {
        var username = identifier.ToLowerInvariant();

        var byUsername = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username);
        if (byUsername is not null) return byUsername;

        if (!PhoneNumbers.LooksLikePhone(identifier)) return null;

        var matches = (await db.Users.Where(u => u.Phone != null && u.Phone != "").ToListAsync())
            .Where(u => PhoneNumbers.SameNumber(u.Phone, identifier))
            .ToList();

        // Nothing stops two accounts sharing a line — a handset on the counter, or
        // a number reissued after someone left. Prefer the one the password fits so
        // a shared number still signs the right person in; with none fitting, hand
        // back any match and let the caller reject it with the usual message.
        return matches.FirstOrDefault(u => u.Password == password) ?? matches.FirstOrDefault();
    }

    /// <summary>
    /// Clears the auth cookie. POST only, so a prefetched or mistyped link cannot
    /// sign a cashier out mid-order.
    ///
    /// <c>[Authorize]</c> is what makes an expired session land back on the login
    /// page: the antiforgery token is bound to the signed-in identity, so without
    /// the gate a stale page would post it anonymously and get a bare 400.
    /// </summary>
    [Authorize]
    [HttpPost]
    [Route("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await ClockOutCurrentUserAsync(HttpContext, attendance, log);
        displayHub.ClearFor(User.FindFirstValue(ClaimTypes.NameIdentifier));
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect(Url.Action(nameof(Login))!);
    }

    /// <summary>
    /// Stamps the sign-out time in <c>staff_attendance</c> for whoever is signed in.
    /// Shared with the till's Close Terminal, which signs the shift out too.
    /// </summary>
    internal static async Task ClockOutCurrentUserAsync(HttpContext http, Attendance attendance, ILogger log)
    {
        if (!int.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return;
        try
        {
            await attendance.ClockOutAsync(userId, userId);
        }
        catch (Exception ex)
        {
            // Attendance is a record, not a gate: a failed write must never keep
            // someone signed in on a shared till.
            log.LogError(ex, "Could not record sign-out for user {UserId}", userId);
        }
    }

    /// <summary>Same rule on the way in: a failed attendance write never blocks sign-in.</summary>
    private async Task TryClockAsync(Func<Task> write, int userId)
    {
        try
        {
            await write();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Could not record sign-in for user {UserId}", userId);
        }
    }
}

public class LoginRequest
{
    /// <summary>The username or the phone number on the account. Either signs in.</summary>
    public string? Identifier { get; set; }

    public string? Password { get; set; }

    /// <summary>"admin" or "billing". Anything else is treated as billing.</summary>
    public string? Destination { get; set; }
}
