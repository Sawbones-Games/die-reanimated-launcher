"""Renders the launcher's icon (src/Launcher/Assets/launcher.ico + .png) from the brand: the dark teal tile,
the teal hairline, "DIE" in Oswald Bold and the red bar the website uses as its section marker. No game art.

    python tools/make-icon.py        (needs Pillow; the font is in the repository)
"""
import os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "src", "Launcher", "Assets")
FONT = os.path.join(ASSETS, "Fonts", "Oswald-Bold.ttf")

def render(size):
    s = size
    im = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    tile = Image.new("RGBA", (s, s)); td = ImageDraw.Draw(tile)
    for y in range(s):                                   # #0f2b27 → #030909, the window's own backdrop gradient
        t = y / s
        td.line([(0, y), (s, y)], fill=(int(15 + (3 - 15) * t), int(43 + (9 - 43) * t), int(39 + (9 - 39) * t), 255))
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, s - 1, s - 1), radius=max(2, s // 8), fill=255)
    im.paste(tile, (0, 0), mask)
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((0, 0, s - 1, s - 1), radius=max(2, s // 8), outline=(41, 78, 68, 255), width=max(1, s // 64))
    f = ImageFont.truetype(FONT, int(s * 0.52))
    bb = d.textbbox((0, 0), "DIE", font=f)
    d.text(((s - (bb[2] - bb[0])) // 2 - bb[0], int(s * 0.14) - bb[1]), "DIE", font=f, fill=(224, 221, 208, 255))
    bar_y = int(s * 0.72)
    d.rectangle((int(s * 0.2), bar_y, int(s * 0.8), bar_y + max(2, s // 12)), fill=(196, 24, 0, 255))
    return im

sizes = [16, 24, 32, 48, 64, 128, 256]
ims = [render(x) for x in sizes]
ims[-1].save(os.path.join(ASSETS, "launcher.png"))
ims[-1].save(os.path.join(ASSETS, "launcher.ico"), format="ICO", sizes=[(x, x) for x in sizes], append_images=ims[:-1])
print("wrote launcher.ico / launcher.png")
