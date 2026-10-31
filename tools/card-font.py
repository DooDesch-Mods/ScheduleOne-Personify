"""Bakes the glyph atlases the NPC card art draws its text with (Cards/CardArt.cs).

Usage: python -I tools/card-font.py <fonts-dir> <out-dir>
  fonts-dir holds bigshoulders-800.ttf, archivo-700.ttf, archivo-400.ttf (SIL Open Font License; copies ship with
  Sideload under Assets/selftest/fonts).
  out-dir receives <face>.json (metrics) and <face>.bin (zlib-compressed 8-bit coverage, row-major, top row first).

The atlas is raw coverage instead of a PNG so the card art decodes it with System.IO.Compression alone, in the game
and in the off-game preview tool.
"""
import json
import sys
import zlib
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

CHARS = "".join(chr(c) for c in range(32, 127)) + "".join(chr(c) for c in range(0xA0, 0x100)) + "’–—"

FACES = {
    # name: (file, pixel size)
    "display": ("bigshoulders-800.ttf", 112),
    "label": ("archivo-700.ttf", 30),
    "small": ("archivo-400.ttf", 24),
}


def bake(font_path: Path, size: int):
    font = ImageFont.truetype(str(font_path), size)
    ascent, descent = font.getmetrics()
    glyphs = {}
    cells = []
    for ch in CHARS:
        left, top, right, bottom = font.getbbox(ch, anchor="ls")
        w, h = max(0, right - left), max(0, bottom - top)
        adv = font.getlength(ch)
        img = None
        if w > 0 and h > 0:
            img = Image.new("L", (w, h), 0)
            ImageDraw.Draw(img).text((-left, -top), ch, font=font, fill=255, anchor="ls")
        cells.append((ch, img, left, top, adv))

    # Shelf packing into a 1024-wide atlas.
    width, x, y, shelf = 1024, 1, 1, 0
    placed = []
    for ch, img, left, top, adv in cells:
        w, h = (img.size if img else (0, 0))
        if x + w + 1 > width:
            x, y, shelf = 1, y + shelf + 1, 0
        placed.append((ch, img, x, y, left, top, adv))
        x += w + 1
        shelf = max(shelf, h)
    height = y + shelf + 1
    atlas = Image.new("L", (width, height), 0)
    for ch, img, gx, gy, left, top, adv in placed:
        w, h = (img.size if img else (0, 0))
        if img:
            atlas.paste(img, (gx, gy))
        # ox/oy: offset of the glyph box from the pen position on the baseline (oy negative = above).
        glyphs[ch] = {"x": gx, "y": gy, "w": w, "h": h, "ox": left, "oy": top, "adv": round(adv, 2)}
    meta = {"size": size, "ascent": ascent, "descent": descent, "width": width, "height": height, "glyphs": glyphs}
    return meta, zlib.compress(atlas.tobytes(), 9)


def main() -> int:
    fonts, out = Path(sys.argv[1]), Path(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)
    for name, (file, size) in FACES.items():
        meta, data = bake(fonts / file, size)
        (out / f"{name}.json").write_text(json.dumps(meta, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
        (out / f"{name}.bin").write_bytes(data)
        print(f"baked {name}: {file} at {size}px, atlas {meta['width']}x{meta['height']}, {len(data) // 1024} KB")
    return 0


if __name__ == "__main__":
    sys.exit(main())
