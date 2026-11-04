using Microsoft.Win32;

namespace Kiosk.Services;

/// <summary>
/// Reads the identity of the machine this app is running on.
///
/// Only meaningful because the app is installed per till and bound to loopback —
/// the server and the browser are the same machine. If the POS were ever served
/// over a LAN, this would describe the server, not the cashier's PC.
/// </summary>
public class TerminalIdentity
{
    public string MachineGuid { get; }
    public string MachineName { get; } = Environment.MachineName;

    public TerminalIdentity()
    {
        MachineGuid = ReadWindowsGuid() ?? ReadLinuxMachineId() ?? string.Empty;
    }

    private static string? ReadWindowsGuid()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch
        {
            return null;
        }
    }

    // Linux equivalent, for a Raspberry Pi till. Same semantics: a GUID fixed at
    // OS install. Note an SD-card reimage generates a new one.
    private static string? ReadLinuxMachineId()
    {
        foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
        {
            try
            {
                if (File.Exists(path))
                {
                    var id = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrEmpty(id)) return id;
                }
            }
            catch
            {
                // Unreadable; try the next location.
            }
        }
        return null;
    }
}
