"""Turns the raw RGBA files of CardArtPreview into PNGs. Usage: python -I tests/card-preview.py <dir>"""
import sys
from pathlib import Path

from PIL import Image

for raw in Path(sys.argv[1]).glob("*.rgba"):
    Image.frombytes("RGBA", (900, 1260), raw.read_bytes()).save(raw.with_suffix(".png"))
    print(f"made {raw.with_suffix('.png').name}")
