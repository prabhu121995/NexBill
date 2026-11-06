namespace Kiosk.Models;

/// <summary>
/// One tender against a settled bill. A bill paid one way has one row; a mixed
/// bill (part cash, part card or UPI) has one row per method, so the cash drawer
/// and the card terminal can each be reconciled against their own total.
/// The rows of a bill always add up to its <see cref="Order.NetTotal"/>.
/// </summary>
public class OrderPayment
{
    public const string Cash  = "cash";
    public const string Card  = "card";
    public const string Qr    = "qr";
    /// <summary>Split between people; the per-person amounts are on the order itself.</summary>
    public const string Split = "split";

    public static readonly string[] Methods = [Cash, Card, Qr, Split];

    /// <summary>Longest method: a transaction mode name (config_transaction_mode.Name).</summary>
    public const int MethodMaxLength = 30;

    /// <summary>
    /// The method a transaction mode is saved under. The CASH mode is the built-in
    /// "cash" so the drawer total, tendered and change keep working; every other
    /// mode (NETS, VISA...) is saved under its own name.
    /// </summary>
    public static string MethodForMode(string modeName) =>
        string.Equals(modeName, "CASH", StringComparison.OrdinalIgnoreCase) ? Cash : modeName;

    /// <summary>
    /// Credit sales in the settlement: card and the card-terminal modes. Cash, QR,
    /// split and any mode with CASH in its name (KIOSK CASH) are plain sales.
    /// </summary>
    public static bool IsCredit(string method) =>
        method is not (Cash or Qr or Split or "unrecorded")
        && !method.Contains("CASH", StringComparison.OrdinalIgnoreCase);

    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }

    /// <summary>"cash", "card", "qr" (QR / UPI), "split", or a transaction mode name (NETS, VISA...).</summary>
    public string Method { get; set; } = Cash;

    /// <summary>What this method paid towards the bill, change already taken off.</summary>
    public decimal Amount { get; set; }

    /// <summary>Cash handed over, for the cash row only; 0 otherwise.</summary>
    public decimal Tendered { get; set; }
    public decimal ChangeGiven { get; set; }

    /// <summary>Signed-in user who took the payment. Plain int, like the other audit columns.</summary>
    public int ReceivedBy { get; set; } = AuditableEntity.SystemUserId;
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
}
