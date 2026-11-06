using System.Globalization;

namespace Kiosk.Services;

/// <summary>
/// The country the shop trades in, picked by <c>Pos:Country</c> in appsettings.json
/// (Singapore when it is left out). It decides the shop's clock, how dates, times
/// and numbers read, and the currency on every screen and bill - never the server's
/// own zone or language, so the app can be hosted in a different country from the
/// shop without a single time moving.
///
/// Each country carries its zone under both names: Windows ids for Windows hosts,
/// IANA ids for Linux hosts and for the browser, which only understands IANA.
/// </summary>
public sealed record ShopCountry(
    string Name,
    string WindowsZone,
    string IanaZone,
    string Culture,
    string DateFormat,
    string TimeFormat,
    string CurrencyCode,
    string CurrencySymbol)
{
    public static readonly IReadOnlyDictionary<string, ShopCountry> Known =
        new Dictionary<string, ShopCountry>(StringComparer.OrdinalIgnoreCase)
        {
            // 05 Nov 2026, 02:30 pm - UTC+8 - $12.50
            ["Singapore"] = new("Singapore", "Singapore Standard Time", "Asia/Singapore", "en-SG", "dd MMM yyyy", "hh:mm tt", "SGD", "$"),
            // 05-11-2026, 12:00 pm - UTC+5:30 - ₹12.50
            ["India"]     = new("India", "India Standard Time", "Asia/Kolkata", "en-IN", "dd-MM-yyyy", "hh:mm tt", "INR", "₹"),
        };

    /// <summary>Used when <c>Pos:Country</c> is not set.</summary>
    public const string DefaultCountry = "Singapore";

    public CultureInfo CultureInfo => CultureInfo.GetCultureInfo(Culture);

    /// <summary>
    /// The payment QR the customer screen shows: a UPI intent in India, a PayNow
    /// (SGQR) code in Singapore. Admin &gt; Settings asks for that scheme's details.
    /// </summary>
    public bool UsesUpi => CurrencyCode == "INR";
    public string QrScheme => UsesUpi ? "UPI" : "PayNow";

    /// <summary>
    /// The configured country, with any date/time format overrides applied. Left
    /// out, it is Singapore. A country that is set but unknown stops startup: a typo
    /// quietly becoming some other country, or the server's zone, would stamp bills
    /// with the wrong day, which is the one thing this setting exists to prevent.
    /// </summary>
    public static ShopCountry FromConfig(IConfiguration config)
    {
        var name = config["Pos:Country"];
        if (string.IsNullOrWhiteSpace(name)) name = DefaultCountry;
        if (!Known.TryGetValue(name.Trim(), out var country))
            throw new InvalidOperationException(
                $"Pos:Country in appsettings.json must be one of: {string.Join(", ", Known.Keys)} (got \"{name}\").");

        var dateFormat = config["Pos:DateFormat"];
        var timeFormat = config["Pos:TimeFormat"];
        return country with
        {
            DateFormat = string.IsNullOrWhiteSpace(dateFormat) ? country.DateFormat : dateFormat.Trim(),
            TimeFormat = string.IsNullOrWhiteSpace(timeFormat) ? country.TimeFormat : timeFormat.Trim(),
        };
    }
}
