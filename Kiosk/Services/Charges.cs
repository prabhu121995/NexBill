using Kiosk.Data;
using Kiosk.Models;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Services;

/// <summary>
/// The service charge and tax rates the shop bills at, read from
/// <c>config_terminal_setting</c>. One place so the till, the customer display,
/// the admin panel and the seeder cannot drift apart on either the key names or
/// the fallback values.
/// </summary>
public static class Charges
{
    /// <summary><see cref="TerminalSetting.Service"/> key for the service charge percentage.</summary>
    public const string ServiceChargeKey = "SERVICE_CHARGE";

    /// <summary><see cref="TerminalSetting.Service"/> key for the GST / tax percentage.</summary>
    public const string GstKey = "GST";

    /// <summary>What a fresh database is seeded with, and the fallback below.</summary>
    public const decimal DefaultServiceCharge = 10m;
    public const decimal DefaultGst = 5m;

    public record Rates(decimal ServiceCharge, decimal Gst);

    /// <summary>
    /// Both rates in one round trip. A missing or deactivated row falls back to the
    /// seeded default rather than billing at zero: a till that silently stopped
    /// charging service would not be noticed until the day's takings were counted.
    /// </summary>
    public static async Task<Rates> LoadAsync(AppDbContext db)
    {
        var rows = await db.TerminalSettings
            .Where(s => s.IsActive && (s.Service == ServiceChargeKey || s.Service == GstKey))
            .ToDictionaryAsync(s => s.Service, s => s.Value);

        return new Rates(
            rows.TryGetValue(ServiceChargeKey, out var sc) ? sc : DefaultServiceCharge,
            rows.TryGetValue(GstKey, out var gst) ? gst : DefaultGst);
    }
}
