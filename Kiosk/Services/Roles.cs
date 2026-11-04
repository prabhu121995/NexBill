namespace Kiosk.Services;

/// <summary>
/// The role codes held in <c>config_role.Code</c>. They travel in the auth cookie
/// as the claim <see cref="System.Security.Claims.ClaimTypes.Role"/>, so
/// <c>[Authorize(Roles = ...)]</c> matches on these exact strings.
/// </summary>
public static class Roles
{
    public const string Admin      = "ADMIN";
    public const string Supervisor = "SUPERVISOR";
    public const string Cashier    = "CASHIER";
    public const string Waiter     = "WAITER";

    /// <summary>
    /// Roles allowed into the admin panel. Used both as the <c>[Authorize]</c>
    /// filter and to decide whether login offers a choice of destination, so the
    /// gate and the chooser can never disagree.
    /// </summary>
    public const string AdminRoles = $"{Admin},{Supervisor}";

    /// <summary>True when the role may open the admin panel as well as billing.</summary>
    public static bool CanAdmin(string? roleCode) =>
        roleCode is Admin or Supervisor;

    /// <summary>
    /// The seeded roles. Code depends on each of them by name, so the admin panel
    /// lets them be renamed but never deleted.
    /// </summary>
    public static bool IsBuiltIn(string? roleCode) =>
        roleCode is Admin or Supervisor or Cashier or Waiter;

    /// <summary>
    /// False for roles that exist only to be picked on a bill, never to sign in.
    /// A waiter is chosen by the cashier when settling, so their account carries
    /// no username or password of its own and the login screen refuses them.
    /// </summary>
    public static bool CanSignIn(string? roleCode) =>
        roleCode is not Waiter;
}
