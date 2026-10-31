"""Renders Assets/editor/wordmark.png: PERSONIFY in the Side Hustle hub's slab wordmark style.

The hub's wordmark.png (SideHustle/Assets/hub) is a Courier-style bold slab in caps, off-white on transparent,
45px tall with a 41px cap height and a 39.6px letter advance, drawn at twice its CSS size. This draws Courier New
Bold to the same metrics so the two read as one family.

    python tools/wordmark.py
"""
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

FONT = "C:/Windows/Fonts/courbd.ttf"
TEXT = "PERSONIFY"
COLOR = (233, 233, 228, 255)
HEIGHT = 45       # output height in pixels, same as the hub wordmark
CAP = 41          # cap height in pixels
ADVANCE = 39.6    # letter advance in pixels
WIDTH = 30        # width of a full-width letter (H) in pixels
WEIGHT = 3        # stroke thickening (max filter size), the hub slab is heavier than Courier New Bold

font = ImageFont.truetype(FONT, 100)
l, t, r, b = font.getbbox("H")
font = ImageFont.truetype(FONT, round(100 * CAP / (b - t)))
hl, ht, hr, hb = font.getbbox("H")
# Courier is wider than the hub's slab at the same cap height, so each letter is squeezed to the hub's letter
# width and set on the hub's advance, centred in its cell.
squeeze = WIDTH / (hr - hl)
w = round(ADVANCE * (len(TEXT) - 1) + WIDTH) + 4
img = Image.new("RGBA", (w, HEIGHT), (0, 0, 0, 0))
for i, ch in enumerate(TEXT):
    cell = Image.new("RGBA", (200, 200), (0, 0, 0, 0))
    ImageDraw.Draw(cell).text((50, 50 - ht), ch, font=font, fill=COLOR)
    cell = cell.filter(ImageFilter.MaxFilter(WEIGHT))
    l, t, r, b = cell.getbbox()
    glyph = cell.crop((l, t, r, b)).resize((r - l, CAP), Image.LANCZOS)
    glyph = glyph.resize((max(1, round(glyph.width * squeeze)), glyph.height), Image.LANCZOS)
    x = round(2 + i * ADVANCE + (WIDTH - glyph.width) / 2)
    img.alpha_composite(glyph, (x, 2))
out = os.path.join(os.path.dirname(__file__), "..", "Assets", "editor", "wordmark.png")
img.save(out, optimize=True)
print(out, img.size)
