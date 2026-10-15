"""
Renders Assets/CardFactory.ProfitLoss.ico, the app icon (exe, taskbar, window): logo S2
(owner, 15 Oct 2026).

The Card Factory smiley - Assets/HeaderSmileyFace.png, the same image the header and
section bars use, placed as it is - large on the header's navy (CfDeepBlue) in a rounded
square, with a white badge holding three rounded bars (the last one yellow) at its lower
right. Every size Windows uses is drawn on its own (at 8x, then reduced), rather than one
picture shrunk to all of them.
    python tools/render-app-icon.py
"""
import os
from PIL import Image, ImageDraw

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(REPO, 'src', 'CardFactory.ProfitLoss.App', 'Assets')
SMILEY = os.path.join(ASSETS, 'HeaderSmileyFace.png')
NAVY = (0x00, 0x4E, 0xA1, 255)     # CfDeepBlue, the header
YELLOW = (0xFF, 0xC9, 0x07, 255)   # CfYellow
WHITE = (255, 255, 255, 255)
SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]

def draw(px, ss=8):
    """The icon at px x px. Designed on a 100 grid (as the concept), drawn at px*ss, then reduced."""
    n = px * ss; k = n / 100
    im = Image.new('RGBA', (n, n), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rounded_rectangle((0, 0, n - 1, n - 1), radius=22 * k, fill=NAVY)
    face = Image.open(SMILEY).convert('RGBA').resize((round(72 * k), round(72 * k)), Image.LANCZOS)
    im.alpha_composite(face, (round(8 * k), round(8 * k)))
    d = ImageDraw.Draw(im)
    r, w = 19 * k, 3 * k
    d.ellipse((74 * k - r - w / 2, 74 * k - r - w / 2, 74 * k + r + w / 2, 74 * k + r + w / 2), fill=NAVY)
    d.ellipse((74 * k - r + w / 2, 74 * k - r + w / 2, 74 * k + r - w / 2, 74 * k + r - w / 2), fill=WHITE)
    for x, y, h, c in ((64, 74, 9, NAVY), (71.5, 69, 14, NAVY), (79, 64, 19, YELLOW)):
        d.rounded_rectangle((x * k, y * k, (x + 5) * k, (y + h) * k), radius=2.5 * k, fill=c)
    return im.resize((px, px), Image.LANCZOS)

if __name__ == '__main__':
    images = [draw(s) for s in SIZES]
    out = os.path.join(ASSETS, 'CardFactory.ProfitLoss.ico')
    images[-1].save(out, format='ICO', sizes=[(s, s) for s in SIZES], append_images=images[:-1])
    print(out, SIZES)
