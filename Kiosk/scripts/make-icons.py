"""Write every icon size Windows and the browser want, from one 4096px master.

    python scripts/make-icons.py wwwroot                 # the drawn storefront mark
    python scripts/make-icons.py wwwroot --theme=night   # same mark, another theme tile
    python scripts/make-icons.py wwwroot shop-logo.png   # a real logo instead

Outputs, all from that one master so they cannot drift apart:

    app.ico                     shortcut icon the installer stamps on the .lnk
    favicon.ico                 browser tab / Chrome app-window icon
    icons/icon-192.png          web manifest
    icons/icon-512.png          web manifest
    icons/icon-maskable-512.png web manifest, full-bleed for Android's mask
    icons/apple-touch-icon.png  iPad used as a customer display

PUTTING A REAL LOGO IN: pass it as the second argument - a square PNG of 512px
or more, transparent background. It is centred on the same olive tile, so one
shop's logo and the next look like the same product. Pass --plain after it to
drop the tile and use the artwork edge to edge. Export SVG to PNG first; Pillow
does not read SVG. Then publish, and re-run Install-Kiosk.ps1 on each terminal.

TILE COLOUR: --theme= picks the tile gradient - olive (the default, the app's own
#828746), orange, blue, teal, green, red, charcoal, or the older lavender, deep,
night and mint. Olive is the `brand` colour in tailwind.config in the views -
keep it in step if the theme changes. The awning stripes take a shade of the tile's colour.
"""
import os
import sys

from PIL import Image, ImageDraw

args = [a for a in sys.argv[1:] if not a.startswith("--")]
flags = {a for a in sys.argv[1:] if a.startswith("--")}

DEST = args[0] if args else "wwwroot"
SOURCE = args[1] if len(args) > 1 else None
PLAIN = "--plain" in flags

def hex_rgb(h):
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))


# Tile gradient top -> bottom, then the awning stripe, each a Tailwind shade.
THEMES = {
    "olive":    ("#828746", "#5e6133", "#74793f"),  # brand-500 -> 700, the app's own colour
    "orange":   ("#f97316", "#b43c0c", "#d0480c"),
    "blue":     ("#3b82f6", "#1d4ed8", "#2563eb"),  # blue-500 -> 700
    "teal":     ("#14b8a6", "#0f766e", "#0d9488"),  # teal-500 -> 700
    "green":    ("#16a34a", "#166534", "#16a34a"),  # green-600 -> 800
    "red":      ("#ef4444", "#b91c1c", "#dc2626"),  # red-500 -> 700
    "charcoal": ("#475569", "#1e293b", "#475569"),  # slate-600 -> 800
    "lavender": ("#8c60dc", "#6432b4", "#7540cc"),  # the app's earlier theme
    "deep":     ("#7540cc", "#432279", "#7540cc"),
    "night":    ("#522995", "#231046", "#522995"),
    "mint":     ("#10b981", "#047857", "#059669"),
}
THEME = next((f.split("=", 1)[1] for f in flags if f.startswith("--theme=")), "olive")
if THEME not in THEMES:
    sys.exit(f"unknown --theme={THEME}; choose from {', '.join(THEMES)}")

TOP, BOTTOM, ACCENT = (hex_rgb(h) for h in THEMES[THEME])
GLYPH = (255, 255, 255)

# Drawn 4x over the largest output (1024) so that downsampling, not PIL's own
# edge handling, is what antialiases every size.
MASTER = 4096

# Sizes Windows actually asks for: 16/32 in Explorer and the taskbar, 48 on the
# desktop, 256 for the large-icon view and the Start menu tile.
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def gradient(size, radius_frac):
    """The rounded tile: a vertical gradient behind a rounded mask."""
    tile = Image.new("RGB", (1, size))
    px = tile.load()
    for y in range(size):
        t = y / max(size - 1, 1)
        px[0, y] = tuple(round(a + (b - a) * t) for a, b in zip(TOP, BOTTOM))
    tile = tile.resize((size, size), Image.NEAREST).convert("RGBA")

    mask = Image.new("L", (size, size), 0)
    if radius_frac:
        ImageDraw.Draw(mask).rounded_rectangle(
            (0, 0, size - 1, size - 1), radius=round(size * radius_frac), fill=255)
    else:
        mask.paste(255, (0, 0, size, size))
    tile.putalpha(mask)
    return tile


def storefront(size, scale):
    """Awning, walls and door, as a layer on transparent - the shop the till runs.

    `scale` shrinks the glyph without moving its centre, so the maskable variant
    can keep everything inside Android's 80% safe zone. Coordinates are in a
    0-100 box, the same one the design mock-ups were drawn in.
    """
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)

    def x(f):
        return round(size * (0.5 + (f / 100 - 0.5) * scale))

    y = x  # square canvas, so one mapping serves both axes

    def w(f):
        return max(round(size * f / 100 * scale), 1)

    # Walls: open at the top, where the awning sits on them.
    d.line([(x(28), y(46)), (x(28), y(76)), (x(72), y(76)), (x(72), y(46))],
           fill=GLYPH, width=w(6), joint="curve")

    # Door, standing on the floor line.
    d.rounded_rectangle((x(44), y(58), x(56), y(76)), radius=w(1.5), fill=GLYPH)

    # Awning: a flared band with a scalloped hem. At 16px only this silhouette
    # survives, so it is the widest, heaviest part of the mark.
    d.polygon([(x(25), y(22)), (x(75), y(22)), (x(81), y(40)), (x(19), y(40))], fill=GLYPH)
    for left, right in ((19, 36), (36, 50), (50, 64), (64, 81)):
        d.pieslice((x(left), y(33), x(right), y(47)), start=0, end=180, fill=GLYPH)

    # Stripes on the awning.
    for top, bottom in ((36, 35), (50, 50), (64, 65)):
        d.line([(x(top), y(22)), (x(bottom), y(40))], fill=ACCENT, width=w(3))

    return layer


def logo(size, scale):
    """A supplied logo, fitted into the same square the drawn mark occupies.

    Aspect ratio is kept and the result centred, so a wide wordmark comes out
    letterboxed rather than squashed - squashing is the one thing nobody would
    accept, blank margins are merely a sign the logo wants a square version.
    """
    src = Image.open(SOURCE).convert("RGBA")
    box = max(round(size * 0.78 * scale), 1)

    # Scaled explicitly rather than with thumbnail(), which only ever shrinks -
    # the master is 4096px, so any ordinary logo needs scaling UP to it and
    # thumbnail() would leave it as a speck in the middle of the tile.
    ratio = min(box / src.width, box / src.height)
    fitted = src.resize((max(round(src.width * ratio), 1),
                         max(round(src.height * ratio), 1)), Image.LANCZOS)

    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    layer.alpha_composite(fitted, ((size - fitted.width) // 2, (size - fitted.height) // 2))
    return layer


def master(radius_frac=0.225, scale=1.0):
    # --plain is for a logo that already has its own background and would look
    # like a sticker stuck on top of ours. The 1/0.78 cancels the inset `logo`
    # applies, so the artwork reaches the edges.
    if SOURCE and PLAIN:
        art = Image.new("RGBA", (MASTER, MASTER), (0, 0, 0, 0))
        art.alpha_composite(logo(MASTER, scale / 0.78))
        return art

    art = gradient(MASTER, radius_frac)
    art.alpha_composite(logo(MASTER, scale) if SOURCE else storefront(MASTER, scale))
    return art


def resize(art, size):
    return art.resize((size, size), Image.LANCZOS)


if SOURCE:
    print(f"source: {SOURCE}{' (plain)' if PLAIN else ''}")
    with Image.open(SOURCE) as probe:
        if min(probe.size) < 512:
            print(f"  warning: {probe.width}x{probe.height} is small for a 256px icon "
                  f"- the large sizes will look soft. 512px or more is better.")
else:
    print(f"source: drawn storefront mark, {THEME} tile")

rounded = master()
os.makedirs(os.path.join(DEST, "icons"), exist_ok=True)

# One multi-size .ico serves both the shortcut and the tab. Pillow writes the
# whole set into a single file; Windows and Chrome each pick the size they need.
frames = [resize(rounded, s) for s in ICO_SIZES]
for name in ("app.ico", "favicon.ico"):
    path = os.path.join(DEST, name)
    frames[-1].save(path, format="ICO",
                    sizes=[(s, s) for s in ICO_SIZES], append_images=frames[:-1])
    print(f"  wrote {name} ({', '.join(f'{s}px' for s in ICO_SIZES)})")

for name, size in (("icon-192.png", 192), ("icon-512.png", 512),
                   ("apple-touch-icon.png", 180)):
    resize(rounded, size).save(os.path.join(DEST, "icons", name), format="PNG")
    print(f"  wrote icons/{name}")

# Maskable: square to the edges and the glyph pulled in, because the launcher
# crops this one to whatever shape the platform likes.
resize(master(radius_frac=0, scale=0.78), 512).save(
    os.path.join(DEST, "icons", "icon-maskable-512.png"), format="PNG")
print("  wrote icons/icon-maskable-512.png")
