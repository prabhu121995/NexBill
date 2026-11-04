"""Write every icon size Windows and the browser want, from one 4096px master.

    python scripts/make-icons.py wwwroot                 # the drawn cup mark
    python scripts/make-icons.py wwwroot cafe-logo.png   # a real logo instead

Outputs, all from that one master so they cannot drift apart:

    app.ico                     shortcut icon the installer stamps on the .lnk
    favicon.ico                 browser tab / Chrome app-window icon
    icons/icon-192.png          web manifest
    icons/icon-512.png          web manifest
    icons/icon-maskable-512.png web manifest, full-bleed for Android's mask
    icons/apple-touch-icon.png  iPad used as a customer display

PUTTING A REAL LOGO IN: pass it as the second argument - a square PNG of 512px
or more, transparent background. It is centred on the same lavender tile, so one
cafe's logo and the next look like the same product. Pass --plain after it to
drop the tile and use the artwork edge to edge. Export SVG to PNG first; Pillow
does not read SVG. Then publish, and re-run Install-Kiosk.ps1 on each terminal.

The drawn mark below is only so a fresh checkout has something better than the
stock .NET icon. Its palette is the lavender from the Tailwind config in the
views - keep them in step if the theme changes.
"""
import os
import sys

from PIL import Image, ImageDraw

args = [a for a in sys.argv[1:] if not a.startswith("--")]
flags = {a for a in sys.argv[1:] if a.startswith("--")}

DEST = args[0] if args else "wwwroot"
SOURCE = args[1] if len(args) > 1 else None
PLAIN = "--plain" in flags

# lavender-500 -> lavender-700, same values as tailwind.config in every view.
TOP = (140, 96, 220)
BOTTOM = (100, 50, 180)
GLYPH = (255, 255, 255)

# Drawn 4x over the largest output (1024) so that downsampling, not PIL's own
# edge handling, is what antialiases every size.
MASTER = 4096

# Sizes Windows actually asks for: 16/32 in Explorer and the taskbar, 48 on the
# desktop, 256 for the large-icon view and the Start menu tile.
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def gradient(size, radius_frac):
    """The rounded tile: a vertical lavender gradient behind a rounded mask."""
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


def cup(size, scale):
    """The cup, saucer and handle, as a white layer on transparent.

    `scale` shrinks the glyph without moving its centre, so the maskable variant
    can keep everything inside Android's 80% safe zone.
    """
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)

    def x(f):
        return round(size * (0.5 + (f - 0.5) * scale))

    y = x  # square canvas, so one mapping serves both axes

    def w(f):
        return max(round(size * f * scale), 1)

    # Body: flat rim, rounded base. Deliberately heavy - at 16px the silhouette
    # is all that survives, so it has to carry the icon on its own.
    d.rounded_rectangle((x(0.25), y(0.35), x(0.62), y(0.67)),
                        radius=w(0.11), corners=(False, False, True, True), fill=GLYPH)

    # Handle: an arc, not a ring. It sweeps past vertical at both ends so the
    # tips land back inside the body - at -72/72 they stopped short and the
    # handle read as a separate "D" floating beside the cup.
    d.arc((x(0.56), y(0.41), x(0.80), y(0.60)), start=-130, end=130,
          fill=GLYPH, width=w(0.055))

    # Saucer, with a gap under the cup so the two shapes stay distinct.
    d.rounded_rectangle((x(0.17), y(0.72), x(0.78), y(0.775)),
                        radius=w(0.028), fill=GLYPH)

    return layer


def logo(size, scale):
    """A supplied logo, fitted into the same square the drawn cup occupies.

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
    art.alpha_composite(logo(MASTER, scale) if SOURCE else cup(MASTER, scale))
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
    print("source: drawn cup mark")

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
