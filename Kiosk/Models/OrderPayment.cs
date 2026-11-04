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

    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }

    /// <summary>"cash", "card", "qr" (QR / UPI) or "split".</summary>
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
