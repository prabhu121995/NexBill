namespace Kiosk.Models;

public class Category : AuditableEntity
{
    /// <summary>
    /// Colours a main category can take. The first 14 are the order the till used to
    /// hand out by rail position, so backfilled rows keep the colour they already
    /// showed. All are dark enough for white text on the selected tile.
    /// </summary>
    public static readonly string[] Palette =
    [
        "#7540cc", "#0e9f6e", "#d97706", "#e11d48", "#2563eb", "#0891b2", "#9333ea",
        "#ca8a04", "#db2777", "#16a34a", "#ea580c", "#4f46e5", "#0d9488", "#64748b",
        "#c026d3", "#65a30d", "#0284c7", "#be123c", "#7c3aed", "#b45309"
    ];

    public int Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// Main categories only: a #rrggbb tint for the rail tile, sub category tabs and
    /// dish cards on the till. Empty on sub categories, which take their parent's.
    /// </summary>
    public string Color { get; set; } = string.Empty;

    public int Level { get; set; } = 1;
    public string? ParentExternalId { get; set; }
    public int SortOrder { get; set; }

    public ICollection<MenuItem> MenuItems { get; set; } = [];
    public ICollection<MenuItem> SubMenuItems { get; set; } = [];

    /// <summary>True for a lowercase or uppercase #rrggbb value - the only shape the till's styling accepts.</summary>
    public static bool IsValidColor(string? c) =>
        c is { Length: 7 } && c[0] == '#' && c.Skip(1).All(Uri.IsHexDigit);
}
