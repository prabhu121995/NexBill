namespace Kiosk.Models;

/// <summary>
/// Money put into or taken out of the till outside a bill, entered from the
/// Transactions popup. Mapped to <c>cash_transaction</c>.
///
/// Amount is always positive; <see cref="Type"/> gives its direction. Deleting only
/// clears <see cref="AuditableEntity.IsActive"/>, so a removed cash entry can still
/// be traced and its number is never handed out again.
/// </summary>
public class CashTransaction : AuditableEntity
{
    public const string TypeIn  = "IN";
    public const string TypeOut = "OUT";

    public int Id { get; set; }
    public string TransactionNo { get; set; } = string.Empty;
    public string Type { get; set; } = TypeOut;
    public int ModeId { get; set; }
    public TransactionMode? Mode { get; set; }
    public decimal Amount { get; set; }
    public string Remarks { get; set; } = string.Empty;

    /// <summary>The trading day it was entered on; the popup's date filter reads this.</summary>
    public DateOnly BusinessDate { get; set; }
}
