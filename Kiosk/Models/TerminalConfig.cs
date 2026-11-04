namespace Kiosk.Models;

/// <summary>
/// A till authorised to run the POS. Mapped to <c>config_terminal</c>.
/// </summary>
public class TerminalConfig : AuditableEntity
{
    public int Id { get; set; }

    /// <summary>Short reference, e.g. "T01".</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name shown on the till header, e.g. "Terminal 1".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The value the app matches on: Windows MachineGuid, or /etc/machine-id on Linux.
    /// Unique per OS install and unaffected by renaming the machine.
    /// </summary>
    public string MachineGuid { get; set; } = string.Empty;

    /// <summary>Hostname, stored for readability only — never matched on.</summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>Last time this terminal opened the till screen.</summary>
    public DateTime? LastSeenAt { get; set; }
}
