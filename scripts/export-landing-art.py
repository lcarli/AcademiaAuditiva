"""Exports a generated home page illustration to the WebP file the app serves.

The home page art is generated as a large PNG with a transparent background
(the prompts and the style guide are in docs/landing-art.md). This script:

  1. checks that the background is transparent;
  2. clears the faint noise the generator leaves around the art and makes the
     almost opaque pixels fully opaque;
  3. crops the art and centres it, with a small margin, on a canvas of its
     shape: 1200x800 for the wide scenes and 480x480 for the square ones, twice
     their largest size on screen, so they stay sharp on high-density screens;
  4. writes AcademiaAuditiva/wwwroot/img/landing/<name>.webp.

With --preview it writes only <png name>-preview.png next to the PNG and
leaves the app alone: the art on the soft shape the page draws behind it, in
the light theme (top) and the dark theme (bottom), at full size and at about
the smallest size on screen.

The original PNGs stay out of the repository. To replace an image, export its
new PNG over the old file.

usage: python scripts/export-landing-art.py <name> <art.png> [--preview]
requires: Python 3.9+ and Pillow 9.1+ (pip install pillow)
"""
import argparse
import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "AcademiaAuditiva" / "wwwroot" / "img" / "landing"
QUALITY = 90

# Canvas size and about the smallest width the page shows the image at, in px.
WIDE = ((1200, 800), 340)
SQUARE = ((480, 480), 140)
ART = {
    "hero": WIDE,
    "student": WIDE,
    "teacher": WIDE,
    "step-1": SQUARE,
    "step-2": SQUARE,
    "step-3": SQUARE,
    "faq": SQUARE,
    "final": SQUARE,
}
MARGIN = 0.04
NOISE = 10  # alpha below this is generator noise
SOLID = 246  # alpha above this is meant to be opaque

# The page background and the soft shape behind the art (--aa-bg and
# --aa-accent-soft in wwwroot/css/aa-design-system.css). The shape is an
# ellipse inset by the margin.
THEMES = (("#FFFFFF", "#EFEDFF"), ("#0B1220", "#1E1C48"))


def load(path):
    image = Image.open(path).convert("RGBA")
    alpha = image.getchannel("A")
    w, h = image.size
    corners = [alpha.getpixel((x, y)) for x in (0, w - 1) for y in (0, h - 1)]
    if alpha.getextrema()[0] == 255 or min(corners) >= NOISE:
        sys.exit(f"{path}: the background is not transparent; ask for a transparent background or cut it out first")
    image.putalpha(alpha.point(lambda a: 0 if a < NOISE else 255 if a > SOLID else a))
    return image


def fit(image, size):
    box = image.getchannel("A").getbbox()
    if box is None:
        sys.exit("no art found: the PNG is empty")
    art = image.crop(box)
    w, h = size
    scale = min(w * (1 - 2 * MARGIN) / art.width, h * (1 - 2 * MARGIN) / art.height)
    art = art.resize((max(1, round(art.width * scale)), max(1, round(art.height * scale))), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", size, (0, 0, 0, 0))
    canvas.paste(art, ((w - art.width) // 2, (h - art.height) // 2))
    touches = box[0] == 0 or box[1] == 0 or box[2] == image.width or box[3] == image.height
    return canvas, scale, touches


def backdrop(size, colour):
    # Drawn 4x larger and scaled down for a smooth edge.
    w, h = size
    k = 4
    layer = Image.new("RGBA", (w * k, h * k), (0, 0, 0, 0))
    mx, my = math.floor(w * MARGIN * k), math.floor(h * MARGIN * k)
    ImageDraw.Draw(layer).ellipse((mx, my, w * k - mx - 1, h * k - my - 1), fill=colour)
    return layer.resize(size, Image.Resampling.LANCZOS)


def preview(art, small_width, path):
    w, h = art.size
    sw, sh = small_width, round(h * small_width / w)
    pad = 40
    sheet = Image.new("RGBA", (w + sw + 3 * pad, 2 * (h + 2 * pad)))
    for row, (page, shape) in enumerate(THEMES):
        top = row * (h + 2 * pad)
        sheet.paste(Image.new("RGBA", (sheet.width, h + 2 * pad), page), (0, top))
        tile = Image.new("RGBA", art.size, page)
        tile.alpha_composite(backdrop(art.size, shape))
        tile.alpha_composite(art)
        sheet.paste(tile, (pad, top + pad))
        sheet.paste(tile.resize((sw, sh), Image.Resampling.LANCZOS), (w + 2 * pad, top + pad))
    sheet.convert("RGB").save(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("name", choices=ART, help="which image, as in docs/landing-art.md")
    parser.add_argument("png", type=Path, help="the generated art, with a transparent background")
    parser.add_argument(
        "--preview", action="store_true", help="only write <png name>-preview.png next to the PNG; the app is left alone"
    )
    args = parser.parse_args()

    size, small_width = ART[args.name]
    art, scale, touches = fit(load(args.png), size)
    if args.preview:
        sheet = args.png.with_name(f"{args.png.stem}-preview.png")
        preview(art, small_width, sheet)
        print(f"preview: {sheet} (art scaled {scale:.2f}x)")
    else:
        OUT_DIR.mkdir(parents=True, exist_ok=True)
        out = OUT_DIR / f"{args.name}.webp"
        art.save(out, "WEBP", quality=QUALITY, method=6)
        print(f"{out.relative_to(ROOT)}: {out.stat().st_size / 1024:.1f} KB (art scaled {scale:.2f}x)")
    if touches:
        print("  check the PNG: the art touches its edge, so part of it may be cut off")
    if scale > 1.25:
        print("  check the PNG: the art is small in it and gets blurry when enlarged; ask for art that fills the canvas")


if __name__ == "__main__":
    main()
