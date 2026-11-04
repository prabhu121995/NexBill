using Kiosk.Data;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Services;

/// <summary>
/// The application name shown on every header and browser tab, read from
/// <c>config_store.AppName</c>. Scoped, so it is read at most once per request
/// however many places a page prints it.
/// </summary>
public class AppBranding(AppDbContext db, ILogger<AppBranding> log)
{
    /// <summary>Used when the row is missing or the database cannot be reached.</summary>
    public const string DefaultName = "NexBill";

    private string? name;

    public async Task<string> NameAsync()
    {
        if (name is not null) return name;

        try
        {
            var stored = await db.StoreInfo.AsNoTracking()
                .OrderBy(s => s.Id)
                .Select(s => s.AppName)
                .FirstOrDefaultAsync();
            name = string.IsNullOrWhiteSpace(stored) ? DefaultName : stored.Trim();
        }
        catch (Exception ex)
        {
            // The error page prints the name too, and it is often rendering
            // because the database is down — it must not throw a second time.
            log.LogWarning(ex, "Could not read the app name; using the default");
            name = DefaultName;
        }

        return name;
    }
}
