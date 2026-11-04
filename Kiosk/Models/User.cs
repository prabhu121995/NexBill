namespace Kiosk.Models;

/// <summary>
/// A staff member who can sign in. Mapped to <c>users</c>.
/// </summary>
public class User : AuditableEntity
{
    public int Id { get; set; }

    /// <summary>Login name. Unique, stored lower case so sign-in is case-insensitive.</summary>
    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Phone { get; set; }

    /// <summary>
    /// Sign-in secret, stored as typed. Mixed digits and letters, kept in plain text
    /// at the client's request, so treat the whole column as a credential: never log
    /// it, never send it to the browser, and compare it server side only.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    public int RoleId { get; set; }
    public RoleConfig? Role { get; set; }

    public DateTime? LastLoginAt { get; set; }
}
