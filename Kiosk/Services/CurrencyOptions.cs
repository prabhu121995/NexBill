namespace Kiosk.Services;

/// <summary>
/// The currencies Admin &gt; Settings offers. Stored as the symbol on
/// <c>config_store.CurrencySymbol</c>; empty there means the shop country's own.
/// </summary>
public static class CurrencyOptions
{
    public static readonly IReadOnlyList<(string Symbol, string Label)> All =
    [
        ("$",  "$ (USD / SGD)"),
        ("₹",  "₹ (INR)"),
        ("£",  "£ (GBP)"),
        ("€",  "€ (EUR)"),
        ("RM", "RM (MYR)"),
    ];

    public static readonly IReadOnlySet<string> Symbols = All.Select(c => c.Symbol).ToHashSet();
}
