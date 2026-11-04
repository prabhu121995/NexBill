namespace Kiosk.Models;

/// <summary>
/// How money moved on a cash in / out entry: CASH, NETS, VISA and so on. Mapped to
/// <c>config_transaction_mode</c> and managed under Admin &gt; Transaction Modes.
/// </summary>
public class TransactionMode : AuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
