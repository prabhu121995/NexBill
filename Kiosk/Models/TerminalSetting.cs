namespace Kiosk.Models;

/// <summary>
/// One numeric terminal setting, keyed by name. Mapped to
/// <c>config_terminal_setting</c>.
///
/// A row per setting rather than a column per setting: adding a levy later is an
/// INSERT the admin panel can make on its own, not a migration. The trade-off is
/// that the type is fixed — <see cref="Value"/> is decimal(5,2), so this table
/// holds rates and amounts, and anything textual or boolean belongs elsewhere.
///
/// The table is not keyed by terminal despite the name: these are the charges the
/// whole shop bills at, and a till that taxed differently from the one beside it
/// would be a bug, not a feature. The name follows the till's own
/// "Terminal Settings" screen, which is where staff see these values.
/// </summary>
public class TerminalSetting : AuditableEntity
{
    public int Id { get; set; }

    /// <summary>
    /// Stable key used in code, e.g. "SERVICE_CHARGE". Unique. Never shown to
    /// staff — the UI carries its own labels, so renaming one is a view change and
    /// not a data migration.
    /// </summary>
    public string Service { get; set; } = string.Empty;

    /// <summary>The rate or amount. A percentage for both charges seeded today.</summary>
    public decimal Value { get; set; }
}
