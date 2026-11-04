using System.Diagnostics;

namespace Kiosk.Services;

/// <summary>
/// The handshake between <c>Start-Kiosk.ps1</c> and the running app.
///
/// Chrome refuses <c>window.close()</c> on a window a script did not open, so the
/// till cannot close itself from the page — it used to tell staff to press Alt+F4,
/// which is not something a desktop app should ever ask. Instead the launcher
/// records the pid of each browser window it started, and the power button asks
/// the server to close them.
///
/// The directory is under the launching user's LOCALAPPDATA, so this only lines up
/// when the browser and the app are the same user on the same machine — exactly
/// the kiosk case. Under IIS, or when the app was started by hand, nothing
/// resolves and the page falls back to signing out and returning to /login.
/// </summary>
public sealed class KioskSession
{
    /// <summary>Must match $sessionDir in Start-Kiosk.ps1.</summary>
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KioskPOS", "session");

    /// <summary>Only these may be closed, so a recycled pid cannot take down something else.</summary>
    private static readonly string[] BrowserNames = ["chrome", "msedge"];

    /// <summary>Customer display first, so the till is the last thing to disappear.</summary>
    private static readonly string[] WindowFiles = ["display.pid", "till.pid"];

    /// <summary>How long a window gets to shut down cleanly before it is killed.</summary>
    private static readonly TimeSpan GracefulExit = TimeSpan.FromSeconds(5);

    private readonly ILogger<KioskSession> _log;

    public KioskSession(ILogger<KioskSession> log) => _log = log;

    /// <summary>A live kiosk window the launcher opened.</summary>
    public sealed record OpenWindow(string File, Process Process);

    /// <summary>
    /// The kiosk windows that are still open, clearing any pid file that no longer
    /// resolves. Call this before answering the caller: "a pid file exists" is not
    /// the same claim as "there is a window I can close", and answering with the
    /// former would have the till skip its fallback and appear to do nothing.
    /// </summary>
    public IReadOnlyList<OpenWindow> ResolveOpenWindows()
    {
        var open = new List<OpenWindow>();

        foreach (var file in WindowFiles)
        {
            var path = Path.Combine(Dir, file);
            if (!File.Exists(path)) continue;

            var proc = Resolve(path, file);
            if (proc is not null)
                open.Add(new OpenWindow(file, proc));
            else
                // A stale pid file is worse than none: it makes the page promise a
                // clean close it cannot deliver.
                Delete(path);
        }

        return open;
    }

    /// <summary>Closes the given windows and forgets them. Returns how many went away.</summary>
    public int Close(IReadOnlyList<OpenWindow> windows)
    {
        var closed = 0;

        foreach (var window in windows)
        {
            try
            {
                if (CloseOne(window)) closed++;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not close kiosk window from {File}", window.File);
            }
            finally
            {
                Delete(Path.Combine(Dir, window.File));
            }
        }

        return closed;
    }

    private Process? Resolve(string path, string file)
    {
        try
        {
            if (!int.TryParse(File.ReadAllText(path).Trim(), out var pid)) return null;

            var proc = Process.GetProcessById(pid);

            // A pid is reused the moment its process ends, so never act on the number
            // alone — it has to still be the browser the launcher started.
            if (!BrowserNames.Contains(proc.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                _log.LogWarning("Ignoring pid {Pid} from {File}: it is now '{Name}', not a browser",
                    pid, file, proc.ProcessName);
                return null;
            }

            return proc;
        }
        catch (ArgumentException)
        {
            // No process with that id: the window was already closed.
            return null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not read kiosk session file {File}", file);
            return null;
        }
    }

    private bool CloseOne(OpenWindow window)
    {
        var proc = window.Process;

        // Ask first, kill second. A clean shutdown is not politeness here: killing
        // Chrome outright looks like a crash, and on the next launch it restores the
        // crashed session — including its SESSION COOKIES. That is what made a closed
        // terminal reopen straight onto the previous cashier's till, still signed in.
        // WM_CLOSE is the same thing Alt+F4 sends, which kiosk mode already honours.
        var askedNicely = false;
        try
        {
            askedNicely = proc.CloseMainWindow();
        }
        catch (InvalidOperationException)
        {
            // Exited between resolving and here; the checks below settle it.
        }

        if (askedNicely && proc.WaitForExit((int)GracefulExit.TotalMilliseconds))
        {
            _log.LogInformation("Closed kiosk window from {File} (pid {Pid})", window.File, proc.Id);
            return true;
        }

        if (proc.HasExited) return true;

        _log.LogWarning("Kiosk window from {File} (pid {Pid}) ignored the close request after {Seconds}s; killing it",
            window.File, proc.Id, GracefulExit.TotalSeconds);
        proc.Kill(entireProcessTree: true);
        return true;
    }

    private static void Delete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
