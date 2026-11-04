namespace Kiosk.Models;

/// <summary>
/// A bill moved out of <c>Orders</c> by End of Day. Mapped to <c>order_archive</c>.
///
/// Same columns as <see cref="Order"/>, and the same <see cref="Id"/>: it is copied
/// across rather than generated, so a bill keeps its id for life and the live and
/// archive tables never hand out the same one (the live identity only ever counts
/// up). That is what lets every history query UNION the two without clashes — see
/// <see cref="Kiosk.Services.OrderHistory"/>.
///
/// Read-only once written: nothing edits or settles an archived bill.
/// </summary>
public class OrderArchive
{
    public int Id { get; set; }
    public DateOnly BusinessDate { get; set; }
    public int SeqNo { get; set; }
    public string OrderMode { get; set; } = string.Empty;
    public string TableNo { get; set; } = string.Empty;
    public int Guests { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public decimal SubTotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ServiceChargePercent { get; set; }
    public decimal ServiceChargeAmount { get; set; }
    public decimal GstPercent { get; set; }
    public decimal GstAmount { get; set; }
    public decimal NetTotal { get; set; }
    public string Status { get; set; } = string.Empty;
    public string BillType { get; set; } = string.Empty;
    public int SplitCount { get; set; }
    public decimal SplitAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? WaiterId { get; set; }
    public User? Waiter { get; set; }
    public int CreatedBy { get; set; }
    public int? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? TerminalId { get; set; }
    public TerminalConfig? Terminal { get; set; }

    /// <summary>
    /// The combined bill a merge tombstone points at. Not a foreign key here:
    /// both ends are always archived in the same End of Day, but the archive does
    /// not need the database to police a relationship nothing can change any more.
    /// </summary>
    public int? MergedIntoOrderId { get; set; }

    /// <summary>When End of Day moved the bill, UTC.</summary>
    public DateTime ArchivedAt { get; set; }

    /// <summary>The <see cref="User.Id"/> who ran End of Day.</summary>
    public int ArchivedBy { get; set; }

    public ICollection<OrderItemArchive> Items { get; set; } = [];
    public ICollection<OrderPaymentArchive> Payments { get; set; } = [];
}

/// <summary>A line of an archived bill. Mapped to <c>order_item_archive</c>; keeps its original id.</summary>
public class OrderItemArchive
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public OrderArchive? Order { get; set; }

    public string MenuItemExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public bool IsFOC { get; set; }
    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// A tender of an archived bill. Mapped to <c>order_payment_archive</c>. Moved
/// with the bill: <c>order_payment</c> hangs off <c>Orders</c> by foreign key, and
/// the reports name the cashier from who took the money.
/// </summary>
public class OrderPaymentArchive
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public OrderArchive? Order { get; set; }

    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Tendered { get; set; }
    public decimal ChangeGiven { get; set; }
    public int ReceivedBy { get; set; }
    public DateTime PaidAt { get; set; }
}
