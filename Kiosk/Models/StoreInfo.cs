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

    /// <summary>
    /// Currency symbol on every screen and bill. Empty means the shop country's
    /// own symbol (Pos:Country in appsettings.json).
    /// </summary>
    public string CurrencySymbol { get; set; } = string.Empty;

    /// <summary>
    /// Show a payment QR for the amount due on the customer screen: PayNow in
    /// Singapore, UPI in India (Pos:Country). The PayNow* columns hold either.
    /// </summary>
    public bool PayNowEnabled { get; set; }

    /// <summary>"uen" or "mobile" for PayNow, "upi" in India.</summary>
    public string PayNowType { get; set; } = "uen";

    /// <summary>The PayNow UEN / +65 mobile, or the UPI ID (shop@okaxis) in India.</summary>
    public string PayNowProxy { get; set; } = string.Empty;

    /// <summary>Business / payee name shown in the customer's banking or UPI app, max 25.</summary>
    public string PayNowMerchantName { get; set; } = string.Empty;
}
