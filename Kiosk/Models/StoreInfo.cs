namespace Kiosk.Models;

/// <summary>
/// The shop's own details, printed on bills and shown under Admin &gt; Settings.
/// Mapped to <c>config_store</c>, which holds a single row.
///
/// Textual, so it does not fit <see cref="TerminalSetting"/>'s decimal value.
/// </summary>
public class StoreInfo : AuditableEntity
{
    public int Id { get; set; }

    /// <summary>
    /// The product name on the login, till and admin headers. Seeded only — the
    /// admin panel shows it read-only.
    /// </summary>
    public string AppName { get; set; } = string.Empty;

    public string StoreName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    /// <summary>GSTIN, 15 characters when present.</summary>
    public string GstNo { get; set; } = string.Empty;
}
