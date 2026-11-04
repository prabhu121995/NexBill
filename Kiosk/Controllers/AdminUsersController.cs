using System.Security.Claims;
using Kiosk.Data;
using Kiosk.Models;
using Kiosk.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Controllers;

/// <summary>
/// The Users &amp; Roles page of the admin panel: staff accounts in <c>users</c> and
/// the roles in <c>config_role</c>. Same gate and route prefix as
/// <see cref="AdminController"/>, kept apart only because that file is already long.
///
/// Three rules hold everywhere below:
/// <list type="bullet">
/// <item>Only an Admin can create, change or remove an Admin account, or touch roles.
///       A supervisor runs the floor staff but cannot promote anyone past themselves.</item>
/// <item>Nobody can switch off, delete or re-role their own account, so the panel
///       cannot lock out the person using it.</item>
/// <item>There is always at least one active Admin left who can still sign in.</item>
/// </list>
/// The password is never sent to the browser, in line with <see cref="User.Password"/>.
/// </summary>
[Authorize(Roles = Roles.AdminRoles)]
[Route("admin")]
public class AdminUsersController(AppDbContext db, BusinessClock clock) : Controller
{
    // ===================== USERS =====================

    [HttpGet]
    [Route("api/users")]
    public async Task<IActionResult> Users()
    {
        var users = await db.Users
            .OrderBy(u => u.Role!.SortOrder).ThenBy(u => u.DisplayName)
            .Select(u => new
            {
                id          = u.Id,
                username    = u.Username,
                displayName = u.DisplayName,
                phone       = u.Phone,
                roleId      = u.RoleId,
                roleCode    = u.Role != null ? u.Role.Code : "",
                roleName    = u.Role != null ? u.Role.Name : "",
                isActive    = u.IsActive,
                lastLoginAt = u.LastLoginAt,
                createdAt   = u.CreatedAt
            })
            .ToListAsync();

        // Staff with bills or holds against their name stay for the history, so the
        // page offers delete only where it will work.
        var used = (await db.AllOrders().Where(o => o.WaiterId != null).Select(o => o.WaiterId!.Value).Distinct().ToListAsync())
            .Concat(await db.HeldOrders.Where(h => h.WaiterId != null).Select(h => h.WaiterId!.Value).Distinct().ToListAsync())
            .Concat(await db.StaffAttendance.Select(a => a.UserId).Distinct().ToListAsync())
            .ToHashSet();

        return Ok(users.Select(u => new
        {
            u.id, u.username, u.displayName, u.phone, u.roleId, u.roleCode, u.roleName, u.isActive,
            // Tagged UTC so the browser renders "2 h ago" right on any timezone.
            lastLoginAt = u.lastLoginAt is DateTime t ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : (DateTime?)null,
            canSignIn = Roles.CanSignIn(u.roleCode),
            inUse = used.Contains(u.id)
        }));
    }

    [HttpPost]
    [Route("api/users")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddUser([FromBody] UserRequest req)
    {
        var (v, error) = await ValidateUserAsync(req, existing: null);
        if (error is not null) return error;

        var user = new User
        {
            Username    = v.Username,
            DisplayName = v.DisplayName,
            Phone       = v.Phone,
            Password    = v.Password!,
            RoleId      = v.Role.Id,
            IsActive    = req.IsActive ?? true,
            CreatedBy   = CurrentUserId()
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return Ok(new { id = user.Id });
    }

    [HttpPut]
    [Route("api/users/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UserRequest req)
    {
        var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound(new { error = "User not found" });

        if (Forbidden(user) is { } denied) return denied;

        var (v, error) = await ValidateUserAsync(req, existing: user);
        if (error is not null) return error;

        var self = user.Id == CurrentUserId();
        if (self && v.Role.Id != user.RoleId)
            return Conflict(new { error = "You cannot change your own role" });
        if (self && req.IsActive == false)
            return Conflict(new { error = "You cannot deactivate your own account" });

        var newActive = req.IsActive ?? user.IsActive;
        if (await WouldLoseLastAdminAsync(user, v.Role, newActive))
            return Conflict(new { error = "This is the last active Admin. Add another Admin first." });

        user.Username    = v.Username;
        user.DisplayName = v.DisplayName;
        user.Phone       = v.Phone;
        user.RoleId      = v.Role.Id;
        user.IsActive    = newActive;
        // Blank on edit means "keep the current password".
        if (v.Password is not null) user.Password = v.Password;
        user.UpdatedBy = CurrentUserId();
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { id = user.Id });
    }

    [HttpPost]
    [Route("api/users/{id:int}/active")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetUserActive(int id, [FromBody] CategoryActiveRequest req)
    {
        var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound(new { error = "User not found" });

        if (Forbidden(user) is { } denied) return denied;
        if (!req.IsActive && user.Id == CurrentUserId())
            return Conflict(new { error = "You cannot deactivate your own account" });
        if (await WouldLoseLastAdminAsync(user, user.Role, req.IsActive))
            return Conflict(new { error = "This is the last active Admin. Add another Admin first." });

        user.IsActive  = req.IsActive;
        user.UpdatedBy = CurrentUserId();
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { id = user.Id, isActive = user.IsActive });
    }

    /// <summary>
    /// Hard delete, only for someone with no bills or holds against them - the
    /// foreign keys are Restrict, and a waiter's history must keep their name.
    /// Anyone else is deactivated instead.
    /// </summary>
    [HttpDelete]
    [Route("api/users/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound(new { error = "User not found" });

        if (Forbidden(user) is { } denied) return denied;
        if (user.Id == CurrentUserId())
            return Conflict(new { error = "You cannot delete your own account" });
        if (await WouldLoseLastAdminAsync(user, null, false))
            return Conflict(new { error = "This is the last active Admin. Add another Admin first." });

        var used = await db.AllOrders().AnyAsync(o => o.WaiterId == id) || await db.HeldOrders.AnyAsync(h => h.WaiterId == id);
        if (used)
            return Conflict(new { error = $"{user.DisplayName} has bills on record. Deactivate the account instead." });
        if (await db.StaffAttendance.AnyAsync(a => a.UserId == id))
            return Conflict(new { error = $"{user.DisplayName} has attendance on record. Deactivate the account instead." });

        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return Ok(new { id });
    }

    private record UserValues(string Username, string DisplayName, string? Phone, string? Password, RoleConfig Role);

    private async Task<(UserValues Values, IActionResult? Error)> ValidateUserAsync(UserRequest req, User? existing)
    {
        // Stored lower case: sign-in lower-cases what is typed before comparing.
        var username    = (req.Username ?? "").Trim().ToLowerInvariant();
        var displayName = (req.DisplayName ?? "").Trim();
        var phone       = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim();
        var password    = string.IsNullOrEmpty(req.Password) ? null : req.Password;
        var none        = new UserValues(username, displayName, phone, password, new RoleConfig());

        if (displayName.Length is 0 or > 100)
            return (none, BadRequest(new { error = "Name is required (max 100 characters)" }));

        // Kept to what sign-in by phone could match, even for staff who do not sign in.
        if (phone is not null && (phone.Length > 25 || !PhoneNumbers.LooksLikePhone(phone)))
            return (none, BadRequest(new { error = "Phone must be at least 6 digits (spaces, +, - and brackets allowed)" }));

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == req.RoleId);
        if (role is null)
            return (none, BadRequest(new { error = "Choose a role" }));

        if (!Roles.CanSignIn(role.Code))
        {
            // Picker-only staff (waiters). The column is unique and required, so they
            // still get a username, but an internal one nobody types; an existing row
            // keeps whatever it had. The password is cleared - nothing to sign in with.
            username = existing is not null && !Roles.CanSignIn(existing.Role?.Code)
                ? existing.Username
                : await NewInternalUsernameAsync(displayName, existing?.Id);
            password = "";
        }
        else
        {
            if (username.Length is < 3 or > 50 || !username.All(ch => ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-'))
                return (none, BadRequest(new { error = "Username must be 3-50 characters: letters, digits, dot, dash or underscore" }));

            // A new account, or a waiter being moved onto a sign-in role, has no
            // password yet to keep.
            if (password is null && (existing is null || existing.Password == ""))
                return (none, BadRequest(new { error = $"Set a password - {role.Name} accounts sign in" }));
            if (password is not null && (password.Length is < 4 or > 100 || password.Trim() != password))
                return (none, BadRequest(new { error = "Password must be 4-100 characters with no leading or trailing spaces" }));
        }

        // An existing account may stay on a role that has since been switched off,
        // but nobody is newly put on one: they could not sign in.
        if (!role.IsActive && role.Id != existing?.RoleId)
            return (none, BadRequest(new { error = $"The {role.Name} role is inactive" }));
        if (role.Code == Roles.Admin && !IsAdmin())
            return (none, StatusCode(StatusCodes.Status403Forbidden, new { error = "Only an Admin can grant the Admin role" }));

        var lower = username.ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Id != (existing == null ? 0 : existing.Id) && u.Username.ToLower() == lower))
            return (none, Conflict(new { error = $"The username \"{username}\" is already taken" }));

        return (new UserValues(username, displayName, phone, password, role), null);
    }

    /// <summary>
    /// A username for staff who never sign in: "w-" and the name, made unique. It
    /// only fills the required column; the panel shows "No sign-in" instead.
    /// </summary>
    private async Task<string> NewInternalUsernameAsync(string displayName, int? excludeId)
    {
        var slug = new string(displayName.ToLowerInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        var baseName = "w-" + (slug.Length == 0 ? "waiter" : slug[..Math.Min(slug.Length, 40)]);

        var name = baseName;
        for (var n = 2; await db.Users.AnyAsync(u => u.Id != excludeId && u.Username.ToLower() == name); n++)
            name = $"{baseName}-{n}";
        return name;
    }

    /// <summary>A supervisor may manage everyone except Admin accounts.</summary>
    private IActionResult? Forbidden(User target) =>
        target.Role?.Code == Roles.Admin && !IsAdmin()
            ? StatusCode(StatusCodes.Status403Forbidden, new { error = "Only an Admin can change an Admin account" })
            : null;

    /// <summary>
    /// True when this change would leave no active Admin with an active role -
    /// nobody could then reach the admin panel to undo it.
    /// </summary>
    private async Task<bool> WouldLoseLastAdminAsync(User user, RoleConfig? newRole, bool newActive)
    {
        var wasAdmin = user.IsActive && user.Role is { Code: Roles.Admin, IsActive: true };
        var staysAdmin = newActive && newRole is { Code: Roles.Admin, IsActive: true };
        if (!wasAdmin || staysAdmin) return false;

        return !await db.Users.AnyAsync(u => u.Id != user.Id && u.IsActive
                                             && u.Role != null && u.Role.IsActive && u.Role.Code == Roles.Admin);
    }

    // ===================== ATTENDANCE =====================

    /// <summary>
    /// One trading day of <c>staff_attendance</c>: who came in, the first time in,
    /// the last time out and the hours between. Hours run from first in to last out,
    /// so a break taken by signing out sits inside them.
    ///
    /// A row still "in" is on shift when it is today's (or yesterday's, past
    /// midnight, with nothing newer) and counts up to now; an older one had its
    /// sign-out forgotten and is flagged instead of being counted to now.
    /// </summary>
    [HttpGet]
    [Route("api/attendance")]
    public async Task<IActionResult> AttendanceDay([FromQuery] DateOnly? date)
    {
        var today = clock.Today;
        var day = date ?? today;
        if (day > today) day = today;

        var rows = await db.StaffAttendance
            .Where(a => a.BusinessDate == day)
            .OrderBy(a => a.FirstInAt)
            .Select(a => new
            {
                a.Id, a.UserId, a.Source, a.FirstInAt, a.LastInAt, a.LastOutAt, a.InCount, a.InBy, a.OutBy,
                name     = a.User!.DisplayName,
                roleCode = a.User.Role != null ? a.User.Role.Code : "",
                roleName = a.User.Role != null ? a.User.Role.Name : ""
            })
            .ToListAsync();

        // Yesterday's open rows are still a live shift unless the person has come in
        // again today, which means the old row's sign-out was simply missed.
        var inToday = day == today.AddDays(-1)
            ? (await db.StaffAttendance.Where(a => a.BusinessDate == today).Select(a => a.UserId).ToListAsync()).ToHashSet()
            : new HashSet<int>();

        // Who punched a waiter in or out, by name rather than id.
        var byIds = rows.Select(r => r.InBy).Concat(rows.Where(r => r.OutBy != null).Select(r => r.OutBy!.Value)).Distinct().ToList();
        var names = await db.Users.Where(u => byIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        var now = DateTime.UtcNow;
        var list = rows.Select(r =>
        {
            var isIn = r.LastOutAt is null || r.LastInAt > r.LastOutAt;
            var onShift = isIn && (day == today || (day == today.AddDays(-1) && !inToday.Contains(r.UserId)));
            var missedOut = isIn && !onShift;

            // On shift: up to now. Out: up to the last out. Missed: up to the last out
            // seen, if any, since the time they actually left is unknown.
            DateTime? until = onShift ? now : r.LastOutAt;
            var minutes = until is DateTime u ? (int)Math.Max(0, (u - r.FirstInAt).TotalMinutes) : (int?)null;

            return new
            {
                id        = r.Id,
                userId    = r.UserId,
                name      = r.name,
                roleCode  = r.roleCode,
                roleName  = r.roleName,
                source    = r.Source,
                firstInAt = Utc(r.FirstInAt),
                lastInAt  = Utc(r.LastInAt),
                lastOutAt = r.LastOutAt is DateTime o ? Utc(o) : (DateTime?)null,
                inCount   = r.InCount,
                onShift,
                missedOut,
                minutes,
                inBy      = r.Source == StaffAttendance.SourcePunch ? names.GetValueOrDefault(r.InBy) : null,
                outBy     = r.Source == StaffAttendance.SourcePunch && r.OutBy is int ob ? names.GetValueOrDefault(ob) : null
            };
        }).ToList();

        return Ok(new
        {
            date         = day.ToString("yyyy-MM-dd"),
            isToday      = day == today,
            staff        = list.Count,
            onShift      = list.Count(r => r.onShift),
            missedOut    = list.Count(r => r.missedOut),
            totalMinutes = list.Sum(r => r.minutes ?? 0),
            rows         = list
        });
    }

    private static DateTime Utc(DateTime t) => DateTime.SpecifyKind(t, DateTimeKind.Utc);

    // ===================== ROLES =====================
    // The code travels in the auth cookie and [Authorize] matches on it, so it is
    // fixed once created. The four built-in roles carry meaning in code (admin
    // access, the waiter picker) and cannot be deleted; Admin cannot be switched off.

    [HttpGet]
    [Route("api/roles")]
    public async Task<IActionResult> RoleList()
    {
        var roles = await db.Roles
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
            .Select(r => new
            {
                id        = r.Id,
                code      = r.Code,
                name      = r.Name,
                sortOrder = r.SortOrder,
                isActive  = r.IsActive,
                users     = r.Users.Count(),
                activeUsers = r.Users.Count(u => u.IsActive)
            })
            .ToListAsync();

        return Ok(roles.Select(r => new
        {
            r.id, r.code, r.name, r.sortOrder, r.isActive, r.users, r.activeUsers,
            builtIn   = Roles.IsBuiltIn(r.code),
            canAdmin  = Roles.CanAdmin(r.code),
            canSignIn = Roles.CanSignIn(r.code)
        }));
    }

    [HttpPost]
    [Route("api/roles")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRole([FromBody] RoleRequest req)
    {
        if (!IsAdmin()) return AdminOnly();

        var (name, error) = await ValidateRoleAsync(req, excludeId: null);
        if (error is not null) return error;

        var code = (req.Code ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0)
            code = new string(name.ToUpperInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');
        if (code.Length is 0 or > 20 || !code.All(ch => char.IsAsciiLetterUpper(ch) || char.IsAsciiDigit(ch) || ch == '_'))
            return BadRequest(new { error = "Code must be 1-20 characters: A-Z, 0-9 or underscore" });
        if (await db.Roles.AnyAsync(r => r.Code == code))
            return Conflict(new { error = $"A role with code {code} already exists" });

        var role = new RoleConfig
        {
            Code      = code,
            Name      = name,
            SortOrder = req.SortOrder ?? ((await db.Roles.MaxAsync(r => (int?)r.SortOrder) ?? 0) + 1),
            IsActive  = true,
            CreatedBy = CurrentUserId()
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        return Ok(new { id = role.Id, code = role.Code });
    }

    /// <summary>Renames a role or moves it in the pickers. The code stays.</summary>
    [HttpPut]
    [Route("api/roles/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRole(int id, [FromBody] RoleRequest req)
    {
        if (!IsAdmin()) return AdminOnly();

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (role is null) return NotFound(new { error = "Role not found" });

        var (name, error) = await ValidateRoleAsync(req, excludeId: id);
        if (error is not null) return error;

        role.Name = name;
        if (req.SortOrder is int sort) role.SortOrder = sort;
        role.UpdatedBy = CurrentUserId();
        role.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { id = role.Id });
    }

    /// <summary>
    /// Switching a role off stops everyone on it from signing in (see
    /// <see cref="AccountController"/>), so Admin and the caller's own role are refused.
    /// </summary>
    [HttpPost]
    [Route("api/roles/{id:int}/active")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRoleActive(int id, [FromBody] CategoryActiveRequest req)
    {
        if (!IsAdmin()) return AdminOnly();

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (role is null) return NotFound(new { error = "Role not found" });

        if (!req.IsActive && role.Code == Roles.Admin)
            return Conflict(new { error = "The Admin role cannot be switched off" });
        if (!req.IsActive && role.Code == User.FindFirstValue(ClaimTypes.Role))
            return Conflict(new { error = "You cannot switch off your own role" });

        role.IsActive  = req.IsActive;
        role.UpdatedBy = CurrentUserId();
        role.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { id = role.Id, isActive = role.IsActive });
    }

    [HttpDelete]
    [Route("api/roles/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRole(int id)
    {
        if (!IsAdmin()) return AdminOnly();

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (role is null) return NotFound(new { error = "Role not found" });

        if (Roles.IsBuiltIn(role.Code))
            return Conflict(new { error = $"{role.Name} is a built-in role. Switch it off instead." });

        var count = await db.Users.CountAsync(u => u.RoleId == id);
        if (count > 0)
            return Conflict(new { error = $"{role.Name} has {count} user(s). Move them to another role first." });

        db.Roles.Remove(role);
        await db.SaveChangesAsync();
        return Ok(new { id });
    }

    private async Task<(string Name, IActionResult? Error)> ValidateRoleAsync(RoleRequest req, int? excludeId)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length is 0 or > 50)
            return (name, BadRequest(new { error = "Role name is required (max 50 characters)" }));
        if (req.SortOrder is < 0 or > 999)
            return (name, BadRequest(new { error = "Order must be between 0 and 999" }));

        var names = await db.Roles.Where(r => r.Id != excludeId).Select(r => r.Name).ToListAsync();
        if (names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
            return (name, Conflict(new { error = $"A role named \"{name}\" already exists" }));

        return (name, null);
    }

    private bool IsAdmin() => User.IsInRole(Roles.Admin);

    private ObjectResult AdminOnly() =>
        StatusCode(StatusCodes.Status403Forbidden, new { error = "Only an Admin can change roles" });

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : AuditableEntity.SystemUserId;
}

public class UserRequest
{
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }

    /// <summary>Required on add; left empty on edit to keep the current one.</summary>
    public string? Password { get; set; }

    public int RoleId { get; set; }

    /// <summary>Left null on edit to keep the current state.</summary>
    public bool? IsActive { get; set; }
}

public class RoleRequest
{
    public string? Name { get; set; }

    /// <summary>Only read on add; derived from the name when empty.</summary>
    public string? Code { get; set; }

    public int? SortOrder { get; set; }
}
