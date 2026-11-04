namespace Kiosk.Models;

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public string MenuItemExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public bool IsFOC { get; set; }
    public string Note { get; set; } = string.Empty;
}
