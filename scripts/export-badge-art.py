"""Exports a generated badge medal to the WebP file the app serves.

The badge art is generated as a large PNG with a transparent background (the
prompts and the style guide are in badges.md, at the repository root). This
script:

  1. finds the medal (the pixels at least half opaque) and its centre;
  2. clears the faint noise the generator leaves outside the circle and makes
     the almost opaque pixels inside it fully opaque;
  3. crops the circle and resizes it with Lanczos to 192 px, enough for the
     largest medal on screen (4rem) at 3x pixel density;
  4. writes AcademiaAuditiva/wwwroot/img/badges/<key>.webp.

The original PNGs stay out of the repository. To replace a badge, export its
new PNG over the old file: pages add a hash of the file to its URL, so browsers
fetch the new art.

usage: python scripts/export-badge-art.py <key> <medal.png>
requires: Python 3.9+ and Pillow 9.1+ (pip install pillow)
"""
import argparse
import math
import re
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
CATALOG = ROOT / "AcademiaAuditiva" / "Services" / "Gamification" / "BadgeCatalog.cs"
OUT_DIR = ROOT / "AcademiaAuditiva" / "wwwroot" / "img" / "badges"
SIZE = 192
QUALITY = 90


def badge_keys():
    return set(re.findall(r'public const string \w+ = "([a-z0-9_]+)";', CATALOG.read_text(encoding="utf-8")))


def circle(size, cx, cy, r):
    mask = Image.new("L", size, 0)
    ImageDraw.Draw(mask).ellipse((cx - r, cy - r, cx + r, cy + r), fill=255)
    return mask


def medal(path):
    image = Image.open(path).convert("RGBA")
    alpha = image.getchannel("A")
    if alpha.getextrema()[0] == 255:
        sys.exit(f"{path}: the background is not transparent; cut it out first")
    box = alpha.point(lambda a: 255 if a >= 128 else 0).getbbox()
    if box is None:
        sys.exit(f"{path}: no medal found")
    x0, y0, x1, y1 = box[0], box[1], box[2] - 1, box[3] - 1
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    r = max(x1 - x0, y1 - y0) / 2 + 1

    alpha = ImageChops.multiply(alpha, circle(image.size, cx, cy, r + 2))
    opaque = alpha.point(lambda a: 255 if a >= 240 else a)
    image.putalpha(Image.composite(opaque, alpha, circle(image.size, cx, cy, r - 3)))

    side = math.ceil(2 * r) + 2
    left, top = round(cx - side / 2), round(cy - side / 2)
    fill = 100 * (x1 - x0 + 1) / image.width
    return image.crop((left, top, left + side, top + side)), fill


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("key", help="badge key, as in BadgeKeys (e.g. first_session)")
    parser.add_argument("png", type=Path, help="the generated medal, with a transparent background")
    args = parser.parse_args()

    if args.key not in badge_keys():
        sys.exit(f"{args.key}: not a badge key in {CATALOG.relative_to(ROOT)}")

    crop, fill = medal(args.png)
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    out = OUT_DIR / f"{args.key}.webp"
    crop.resize((SIZE, SIZE), Image.Resampling.LANCZOS).save(out, "WEBP", quality=QUALITY, method=6)
    print(f"{out.relative_to(ROOT)}: {out.stat().st_size / 1024:.1f} KB (medal fills {fill:.1f}% of the PNG)")
    if not 85 <= fill <= 97:
        print("  check the PNG: the style guide asks for a medal filling about 92% of the canvas")


if __name__ == "__main__":
    main()
