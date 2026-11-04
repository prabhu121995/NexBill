namespace Kiosk.Services;

/// <summary>
/// Supplies the trading date used for daily order numbering.
///
/// Order numbers must roll over at the cafe's local midnight, not UTC midnight.
/// In Singapore those are eight hours apart, so numbering off a UTC date would
/// reset the counter at 8am — in the middle of the morning rush.
/// </summary>
public class BusinessClock(IConfiguration config)
{
    private readonly TimeZoneInfo _zone = Resolve(config["Pos:TimeZone"]);

    public string ZoneId => _zone.Id;

    public DateTime LocalNow => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _zone).DateTime;

    public DateOnly Today => DateOnly.FromDateTime(LocalNow);

    /// <summary>
    /// A stored UTC timestamp as the cafe's wall clock read at that moment. Audit
    /// columns are written in UTC, so anything reported by hour of day — the hourly
    /// sales chart, "2 min ago" — has to come back through here first.
    /// </summary>
    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);

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

    private static TimeZoneInfo Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Local;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Misconfigured zone must not take the till down; fall back to the host's.
            return TimeZoneInfo.Local;
        }
    }
}
