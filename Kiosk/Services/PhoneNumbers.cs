namespace Kiosk.Services;

/// <summary>
/// Phone numbers in <c>users.Phone</c> are typed by hand, so the same line can be
/// stored as <c>+91 98765 43210</c>, <c>098765-43210</c> or <c>9876543210</c>.
/// Sign-in has to accept all of them, which means comparing the digits rather than
/// the text.
/// </summary>
public static class PhoneNumbers
{
    /// <summary>Length of a local subscriber number, without any country code.</summary>
    private const int LocalDigits = 10;

    /// <summary>Everything but the digits removed. <c>""</c> when there are none.</summary>
    public static string Digits(string? value) =>
        value is null ? "" : new string(value.Where(char.IsDigit).ToArray());

    /// <summary>
    /// True when the text could be a phone number rather than a username: only
    /// digits and the punctuation people put between them, and enough digits to be
    /// worth looking up. Usernames may contain digits too, so this only decides
    /// which lookup runs first, never whether the account exists.
    /// </summary>
    public static bool LooksLikePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!value.All(c => char.IsDigit(c) || c is '+' or '-' or ' ' or '(' or ')')) return false;
        return Digits(value).Length >= 6;
    }

    /// <summary>
    /// True when two hand-typed numbers are the same line. Compares the last
    /// <see cref="LocalDigits"/> digits once both sides have that many, so a stored
    /// <c>+91 98765 43210</c> still matches a typed <c>9876543210</c>; shorter
    /// numbers (extensions, partial entries) must match digit for digit.
    /// </summary>
    public static bool SameNumber(string? a, string? b)
    {
        var x = Digits(a);
        var y = Digits(b);
        if (x.Length == 0 || y.Length == 0) return false;

        if (x.Length >= LocalDigits && y.Length >= LocalDigits)
            return x[^LocalDigits..] == y[^LocalDigits..];

        return x == y;
    }
}
