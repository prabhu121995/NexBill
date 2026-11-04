namespace Kiosk.Models;

public class Order
{
    public int Id { get; set; }

    /// <summary>Trading date this order belongs to, in the cafe's local timezone.</summary>
    public DateOnly BusinessDate { get; set; }

    /// <summary>Order number within <see cref="BusinessDate"/>; restarts at 1 each day.</summary>
    public int SeqNo { get; set; }
    public string OrderMode { get; set; } = "dinein";
    public string TableNo { get; set; } = string.Empty;
    public int Guests { get; set; }
    public string CustomerName { get; set; } = "Walk-in";
    public string CustomerPhone { get; set; } = string.Empty;
    public decimal SubTotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ServiceChargePercent { get; set; }
    public decimal ServiceChargeAmount { get; set; }
    public decimal GstPercent { get; set; }
    public decimal GstAmount { get; set; }
    public decimal NetTotal { get; set; }
    public string Status { get; set; } = "completed";

    /// <summary>How the bill was paid: "single" or "split". A merge is not a bill type.</summary>
    public string BillType { get; set; } = "single";

    /// <summary>People the bill was split between. 1 for a normal bill.</summary>
    public int SplitCount { get; set; } = 1;

    /// <summary>
    /// The first person's share, not an exact average. On an even split odd cents
    /// go to the earlier payers; the cashier can also set custom amounts per person.
    /// </summary>
    public decimal SplitAmount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The waiter the bill belongs to, for service reports and tips. A real
    /// <see cref="User"/> row, not free text, so a name change does not orphan past
    /// sales. Null is normal: take-away and counter bills have no waiter, and so do
    /// orders rung before this column existed.
    /// </summary>
    public int? WaiterId { get; set; }
    public User? Waiter { get; set; }

    /// <summary>
    /// The signed-in staff member who first rang this bill, and the one who last
    /// changed it. An order is often opened by one person and settled by another, so
    /// the two are tracked separately — <c>CreatedBy</c> is who took the order,
    /// <c>UpdatedBy</c> is who settled it.
    ///
    /// <see cref="AuditableEntity.SystemUserId"/> stands in for the rows that
    /// predate sign-in. Plain ints rather than foreign keys, matching the audit
    /// columns on the master tables.
    /// </summary>
    public int CreatedBy { get; set; } = AuditableEntity.SystemUserId;
    public int? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Till that rang this order. Null for orders placed before terminals existed.</summary>
    public int? TerminalId { get; set; }
    public TerminalConfig? Terminal { get; set; }

    /// <summary>
    /// Set on a bill that was absorbed by a merge, pointing at the bill that now
    /// holds its items. Such a row keeps its SeqNo as a tombstone but has its
    /// money zeroed, so takings never double count.
    /// </summary>
    public int? MergedIntoOrderId { get; set; }
    public Order? MergedInto { get; set; }

    public ICollection<OrderItem> Items { get; set; } = [];

    /// <summary>How the bill was paid, one row per method. Empty until settled.</summary>
    public ICollection<OrderPayment> Payments { get; set; } = [];
}
