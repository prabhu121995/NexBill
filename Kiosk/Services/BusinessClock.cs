using System.Globalization;

namespace Kiosk.Services;

/// <summary>
/// The shop's clock: the trading date used for daily order numbering, and how a
/// date or time reads anywhere in the app.
///
/// Everything is worked out from UTC and the zone of <c>Pos:Country</c>, never the
/// server's local clock or language, so the app can be hosted in another country
/// from the shop. Order numbers must roll over at the shop's midnight, not UTC
/// midnight: in Singapore those are eight hours apart, so numbering off a UTC date
/// would reset the counter at 8am - in the middle of the morning rush.
/// </summary>
public class BusinessClock(IConfiguration config)
{
    private readonly ShopCountry _country = ShopCountry.FromConfig(config);
    private readonly TimeZoneInfo _zone = Resolve(ShopCountry.FromConfig(config));

    public ShopCountry Country => _country;

    public string ZoneId => _zone.Id;

    /// <summary>The zone as the browser knows it, for formatting times on the screens.</summary>
    public string IanaZone => _country.IanaZone;

    public CultureInfo Culture => _country.CultureInfo;

    public DateTime LocalNow => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _zone).DateTime;

    public DateOnly Today => DateOnly.FromDateTime(LocalNow);

    /// <summary>
    /// A stored UTC timestamp as the shop's wall clock read at that moment. Audit
    /// columns are written in UTC, so anything reported by hour of day - the hourly
    /// sales chart, "2 min ago" - has to come back through here first.
    /// </summary>
    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);

    // Display formats, from the country (or the Pos:DateFormat / Pos:TimeFormat overrides).

    public string FormatTime(DateTime utc) => ToLocal(utc).ToString(_country.TimeFormat, Culture);

    public string FormatDate(DateOnly date) => date.ToString(_country.DateFormat, Culture);

    public string FormatDateTime(DateTime utc) =>
        $"{FormatDate(DateOnly.FromDateTime(ToLocal(utc)))}, {FormatTime(utc)}";

    /// <summary>Day and month only, for lists that are already scoped to a year.</summary>
    public string FormatShortDate(DateOnly date) => date.ToString("dd MMM", Culture);

    public string FormatShortDateTime(DateTime utc) =>
        $"{FormatShortDate(DateOnly.FromDateTime(ToLocal(utc)))}, {FormatTime(utc)}";

    /// <summary>The shop's wall clock right now, in the full date-and-time format.</summary>
    public string FormatNow() => FormatDateTime(DateTime.UtcNow);

    /// <summary>
    /// The half-open UTC window [from, to) covering one local trading date, for
    /// filtering UTC timestamp columns by business date in SQL rather than in memory.
    /// </summary>
    public (DateTime From, DateTime To) UtcWindow(DateOnly localDate)
    {
        var start = localDate.ToDateTime(TimeOnly.MinValue);
        return (ToUtc(start), ToUtc(start.AddDays(1)));
    }

    /// <summary>
    /// Local wall clock to UTC. A time that does not exist locally (the hour a DST
    /// jump skips) would throw, so it is nudged forward past the gap instead.
    /// </summary>
    private DateTime ToUtc(DateTime local)
    {
        if (_zone.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _zone);
    }

    /// <summary>
    /// Windows id first, then IANA, so the same country works on a Windows or a
    /// Linux host. Never the host's own zone: a zone the host cannot find stops
    /// startup rather than putting the server's time on bills.
    /// </summary>
    private static TimeZoneInfo Resolve(ShopCountry country)
    {
        foreach (var id in new[] { country.WindowsZone, country.IanaZone })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // Try the other naming scheme.
            }
        }
        throw new InvalidOperationException(
            $"This server has no time zone for {country.Name} ({country.WindowsZone} / {country.IanaZone}).");
    }
}
