namespace Kiosk.Models;

/// <summary>
/// Backs the frameless status page. Kiosk windows have no address bar, so a bare
/// 404 or 403 from the framework would be a dead end — every status code staff
/// can actually reach is turned into a page with a way back.
/// </summary>
public class StatusViewModel
{
    public int Code { get; set; }

    /// <summary>Short headline, in till language rather than HTTP language.</summary>
    public string Title { get; set; } = "";

    /// <summary>One or two sentences on what to do next.</summary>
    public string Detail { get; set; } = "";

    /// <summary>Where the primary button goes: billing for staff, login when the session is gone.</summary>
    public bool BackToLogin { get; set; }

    public static StatusViewModel For(int code) => code switch
    {
        400 => new StatusViewModel
        {
            Code = code,
            Title = "That request was not understood",
            Detail = "The terminal sent something the server could not read. Go back and try the action again."
        },
        401 => new StatusViewModel
        {
            Code = code,
            Title = "Your shift session ended",
            Detail = "Sign-ins last 12 hours. Sign in again to carry on taking orders.",
            BackToLogin = true
        },
        403 => new StatusViewModel
        {
            Code = code,
            Title = "Not available on this account",
            Detail = "That screen is limited to admin and supervisor accounts. Ask a supervisor to sign in if you need it."
        },
        404 => new StatusViewModel
        {
            Code = code,
            Title = "That screen does not exist",
            Detail = "The link pointed somewhere this terminal does not have. Head back to billing and try from there."
        },
        429 => new StatusViewModel
        {
            Code = code,
            Title = "Too many requests from this terminal",
            Detail = "The server is pausing this till for a moment. Wait a minute, then try again."
        },
        408 or 504 => new StatusViewModel
        {
            Code = code,
            Title = "The server took too long",
            Detail = "The till could not get an answer in time. Check the network, then try again."
        },
        >= 500 => new StatusViewModel
        {
            Code = code,
            Title = "The server could not complete that",
            Detail = "Nothing was charged. Try again — if it keeps happening, the service or the database needs a look."
        },
        _ => new StatusViewModel
        {
            Code = code,
            Title = "That action could not be completed",
            Detail = "Head back to billing and try again."
        }
    };
}
