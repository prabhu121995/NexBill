namespace Kiosk.Models;

/// <summary>
/// Shared audit columns. <c>CreatedBy</c> / <c>UpdatedBy</c> hold a
/// <see cref="User.Id"/>; <see cref="SystemUserId"/> stands in for rows the app
/// wrote itself, such as seeded master data. They are plain ints rather than
/// foreign keys so seeding does not depend on a user row existing first.
/// </summary>
public abstract class AuditableEntity
{
    /// <summary>Written to <c>CreatedBy</c> when no signed-in user is responsible.</summary>
    public const int SystemUserId = 0;

    public bool IsActive { get; set; } = true;
    public int CreatedBy { get; set; } = SystemUserId;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
