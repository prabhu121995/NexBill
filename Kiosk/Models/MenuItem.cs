namespace Kiosk.Models;

public class MenuItem : AuditableEntity
{
    public int Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool IsVeg { get; set; }

    public string CategoryExternalId { get; set; } = string.Empty;
    public Category? Category { get; set; }
}
