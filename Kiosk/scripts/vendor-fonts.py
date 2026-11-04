"""Vendor the three Google fonts into wwwroot/lib/fonts so the kiosk works offline.

Keeps only the latin / latin-ext subsets - the till is English-only and the
cyrillic/greek/vietnamese blocks would triple the payload for nothing.
"""
import os, re, sys, urllib.request

DEST = sys.argv[1]
CSS_URL = ("https://fonts.googleapis.com/css2"
           "?family=Chakra+Petch:wght@600;700"
           "&family=JetBrains+Mono:wght@500;700"
           "&family=Plus+Jakarta+Sans:wght@400;500;600;700;800"
           "&display=swap")
# Without a modern UA Google serves ttf; with it we get woff2.
UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36")

os.makedirs(os.path.join(DEST, "files"), exist_ok=True)


def get(url, binary=False):
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read() if binary else r.read().decode("utf-8")


css = get(CSS_URL)

# Each @font-face is preceded by a /* subset */ comment.
blocks = re.findall(r"/\*\s*([a-z0-9-]+)\s*\*/\s*(@font-face\s*\{.*?\})", css, re.S)
keep = [(s, b) for s, b in blocks if s in ("latin", "latin-ext")]
print(f"{len(blocks)} faces in CSS, keeping {len(keep)} latin/latin-ext")

out, seen = [], {}
for subset, block in keep:
    for url in re.findall(r"url\((https://[^)]+)\)", block):
        if url not in seen:
            fam = re.search(r"font-family:\s*'([^']+)'", block).group(1)
            wgt = re.search(r"font-weight:\s*(\d+)", block).group(1)
            ext = url.rsplit(".", 1)[-1]
            name = f"{fam.replace(' ', '')}-{wgt}-{subset}.{ext}"
            open(os.path.join(DEST, "files", name), "wb").write(get(url, binary=True))
            seen[url] = name
            print("  saved", name)
        block = block.replace(url, f"files/{seen[url]}")
    out.append(f"/* {subset} */\n{block}")

open(os.path.join(DEST, "fonts.css"), "w", encoding="utf-8").write(
    "/* Vendored from Google Fonts for offline kiosk use. Regenerate with\n"
    "   scripts/vendor-fonts.py if a weight or family is added to a view. */\n\n"
    + "\n\n".join(out) + "\n")
print("wrote fonts.css")
