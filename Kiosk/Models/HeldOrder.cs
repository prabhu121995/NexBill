namespace Kiosk.Models;

/// <summary>
/// A cart parked with <b>Hold</b> (F6). Kept apart from <see cref="Order"/> on
/// purpose: a hold is not a bill, so it takes no order number and can never show
/// up in takings. The table only ever holds what is parked right now: recalling
/// or discarding a hold deletes its row, and its items with it.
/// </summary>
public class HeldOrder
{
    public int Id { get; set; }

    /// <summary>Trading date the hold was made on, in the cafe's local timezone.</summary>
    public DateOnly BusinessDate { get; set; }

    public string OrderMode { get; set; } = "dinein";
    public string TableNo { get; set; } = string.Empty;
    public int Guests { get; set; }
    public string CustomerName { get; set; } = "Walk-in";
    public string CustomerPhone { get; set; } = string.Empty;
    public decimal DiscountPercent { get; set; }

    /// <summary>Total as shown when held, for the list only. Recalling recalculates.</summary>
    public decimal NetTotal { get; set; }

    public int? WaiterId { get; set; }
    public User? Waiter { get; set; }

    /// <summary>Till the hold was made on. Any till can recall it.</summary>
    public int? TerminalId { get; set; }
    public TerminalConfig? Terminal { get; set; }

    /// <summary>Signed-in user who parked it. Plain int, like the other audit columns.</summary>
    public int HeldBy { get; set; } = AuditableEntity.SystemUserId;
    public DateTime HeldAt { get; set; } = DateTime.UtcNow;

    public ICollection<HeldOrderItem> Items { get; set; } = [];
}

public class HeldOrderItem
{
    public int Id { get; set; }
    public int HeldOrderId { get; set; }
    public HeldOrder? HeldOrder { get; set; }

    public string MenuItemExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public bool IsFOC { get; set; }
    public string Note { get; set; } = string.Empty;
}
