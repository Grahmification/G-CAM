#!/usr/bin/env python3
"""Regenerate the placeholder CommandManager icons.

SOLIDWORKS wants two sets of images, one file per size (20/32/40/64/96/128):

  main<size>.png      a single icon for the CommandGroup itself
  buttons<size>.png   a horizontal STRIP containing every button's icon,
                      so the file is (button_count * size) wide by size tall

These are throwaway placeholders - letter glyphs on coloured rounded squares -
so the toolbar is legible before real artwork exists. Replace the PNGs (or edit
BUTTONS below and re-run) when designing the real icons.

    python tools/make-placeholder-icons.py
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

# Keep in step with GCamCommands.cs - order defines the image index of each button.
BUTTONS = [
    ("G", (0xB0, 0x2E, 0x34)),  # Generate All
    ("J", (0x2D, 0x7D, 0xD2)),  # New Job
    ("O", (0x1F, 0x8A, 0x9B)),  # New Operation
    ("T", (0x3F, 0x9E, 0x5C)),  # Tool Library
    ("P", (0xC6, 0x6A, 0x1E)),  # Post Process
    ("S", (0x7A, 0x4D, 0xB0)),  # Simulate
]
MAIN = ("G", (0x37, 0x47, 0x5A))  # the CommandGroup's own icon

SIZES = [20, 32, 40, 64, 96, 128]
OUT = Path(__file__).resolve().parent.parent / "src" / "GCam.AddIn" / "Resources" / "icons"


def font_for(size):
    for name in ("segoeuib.ttf", "arialbd.ttf", "DejaVuSans-Bold.ttf"):
        try:
            return ImageFont.truetype(name, int(size * 0.62))
        except OSError:
            continue
    return ImageFont.load_default()


def draw_glyph(img, x0, size, letter, colour):
    d = ImageDraw.Draw(img)
    pad = max(1, size // 10)
    radius = max(2, size // 5)
    d.rounded_rectangle(
        [x0 + pad, pad, x0 + size - pad - 1, size - pad - 1],
        radius=radius, fill=colour + (255,),
    )
    f = font_for(size)
    box = d.textbbox((0, 0), letter, font=f)
    tx = x0 + (size - (box[2] - box[0])) / 2 - box[0]
    ty = (size - (box[3] - box[1])) / 2 - box[1]
    d.text((tx, ty), letter, font=f, fill=(255, 255, 255, 255))


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for size in SIZES:
        main_img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        draw_glyph(main_img, 0, size, *MAIN)
        main_img.save(OUT / f"main{size}.png")

        strip = Image.new("RGBA", (size * len(BUTTONS), size), (0, 0, 0, 0))
        for i, (letter, colour) in enumerate(BUTTONS):
            draw_glyph(strip, i * size, size, letter, colour)
        strip.save(OUT / f"buttons{size}.png")

    print(f"wrote {2 * len(SIZES)} files to {OUT}")
    print(f"button strips are {len(BUTTONS)} icons wide")


if __name__ == "__main__":
    main()
