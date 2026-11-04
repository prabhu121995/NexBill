using Kiosk.Data;
using Kiosk.Models;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Services;

/// <summary>
/// Writes <c>staff_attendance</c>: the first time in and the last time out of each
/// person's trading day. Sign-in and sign-out call it for admins, supervisors and
/// cashiers; the till's punch popup calls it for waiters.
///
/// Two rules shape it:
/// <list type="bullet">
/// <item>Going in always lands on <b>today's</b> row. A row left open from yesterday
///       (a forgotten sign-out) stays open, and the admin panel shows it as having
///       no time out rather than stretching it into today's hours.</item>
/// <item>Going out closes the most recent open row from today or yesterday, so a
///       shift that runs past midnight is closed on the day it started.</item>
/// </list>
/// </summary>
public class Attendance(AppDbContext db, BusinessClock clock)
{
    /// <summary>Records a sign-in or punch-in. Returns the day's row.</summary>
    public async Task<StaffAttendance> ClockInAsync(int userId, string source, int recordedBy)
    {
        var today = clock.Today;
        var now = DateTime.UtcNow;

        var row = await db.StaffAttendance.FirstOrDefaultAsync(a => a.UserId == userId && a.BusinessDate == today);
        if (row is null)
        {
            row = new StaffAttendance
            {
                UserId = userId, BusinessDate = today, Source = source,
                FirstInAt = now, LastInAt = now, InCount = 1, InBy = recordedBy
            };
            db.StaffAttendance.Add(row);
            try
            {
                await db.SaveChangesAsync();
                return row;
            }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex))
            {
                // Another till created today's row a moment ago. Drop ours and fall
                // through to update theirs.
                db.Entry(row).State = EntityState.Detached;
                row = await db.StaffAttendance.FirstAsync(a => a.UserId == userId && a.BusinessDate == today);
            }
        }

        row.LastInAt = now;
        row.InCount++;
        row.InBy = recordedBy;
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>
    /// Records a sign-out or punch-out against the latest open row. Returns null when
    /// the person was not in — signed in before attendance existed, or already out.
    /// </summary>
    public async Task<StaffAttendance?> ClockOutAsync(int userId, int recordedBy)
    {
        var row = await OpenRowAsync(userId);
        if (row is null) return null;

        row.LastOutAt = DateTime.UtcNow;
        row.OutBy = recordedBy;
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>The latest row from today or yesterday that is still in, if any.</summary>
    public async Task<StaffAttendance?> OpenRowAsync(int userId)
    {
        var since = clock.Today.AddDays(-1);
        var latest = await db.StaffAttendance
            .Where(a => a.UserId == userId && a.BusinessDate >= since)
            .OrderByDescending(a => a.BusinessDate)
            .FirstOrDefaultAsync();
        return latest is { IsIn: true } ? latest : null;
    }

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };
}
