namespace Kiosk.Models;

/// <summary>
/// A staff role. Mapped to <c>config_role</c>. Fixed set for now — per-role
/// permissions can be added later without changing this table.
/// </summary>
public class RoleConfig : AuditableEntity
{
    public int Id { get; set; }

    /// <summary>Stable key used in code and in the auth cookie, e.g. "CASHIER".</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Label shown in the UI, e.g. "Cashier".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Order the roles appear in pickers.</summary>
    public int SortOrder { get; set; }

    public ICollection<User> Users { get; set; } = [];
}
