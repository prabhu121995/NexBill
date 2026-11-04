namespace Kiosk.Models;

/// <summary>
/// One staff member's day on the floor. Mapped to <c>staff_attendance</c>, one row
/// per user per trading date.
///
/// Admins, supervisors and cashiers are clocked by signing in and out; waiters never
/// sign in, so a cashier punches them in and out from the till instead. Both land
/// here so the admin panel reads one table for "who worked today, and how long".
///
/// The row keeps the first time in and the last time out, not every session: a
/// cashier who signs out for lunch and back in again still has one row, with the
/// hours counted from the first sign-in to the final sign-out.
/// </summary>
public class StaffAttendance
{
    public const string SourceLogin = "LOGIN";
    public const string SourcePunch = "PUNCH";

    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Trading date of the first time in, in the cafe's local timezone.</summary>
    public DateOnly BusinessDate { get; set; }

    /// <summary>How the day was clocked: <see cref="SourceLogin"/> or <see cref="SourcePunch"/>.</summary>
    public string Source { get; set; } = SourceLogin;

    /// <summary>First sign-in or punch-in of the day, UTC. Never moves once written.</summary>
    public DateTime FirstInAt { get; set; }

    /// <summary>Most recent sign-in or punch-in, UTC. Later than <see cref="LastOutAt"/> while they are in.</summary>
    public DateTime LastInAt { get; set; }

    /// <summary>Most recent sign-out or punch-out, UTC. Null until the first one.</summary>
    public DateTime? LastOutAt { get; set; }

    /// <summary>Number of times in that day: sign-ins, or punch-ins.</summary>
    public int InCount { get; set; } = 1;

    /// <summary>Signed-in user who recorded the last in / out. The person themselves for a sign-in.</summary>
    public int InBy { get; set; } = AuditableEntity.SystemUserId;
    public int? OutBy { get; set; }

    /// <summary>True while they are on shift: in, and not out since.</summary>
    public bool IsIn => LastOutAt is null || LastInAt > LastOutAt;
}
