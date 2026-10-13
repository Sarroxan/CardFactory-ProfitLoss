"""
Renders Assets/CardFactory.ProfitLoss.ico, the app icon (exe, taskbar, window): concept A3.

White "P&L" in Chewy on the header's navy (CfDeepBlue) in a rounded square, over two
yellow bars with a white dot between them - the bars either side of CALCULATOR in the
header logo. Every size Windows uses is drawn on its own from the shapes (at 8x, then
reduced), rather than one picture shrunk to all of them.

Chewy is a free Google Font (apache/chewy); the app embeds a copy in Assets/Fonts.
    python tools/render-app-icon.py
"""
import os
from PIL import Image, ImageDraw, ImageFont

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(REPO, 'src', 'CardFactory.ProfitLoss.App', 'Assets')
CHEWY = os.path.join(ASSETS, 'Fonts', 'Chewy-Regular.ttf')
NAVY = (0x00, 0x4E, 0xA1, 255)     # CfDeepBlue, the header
YELLOW = (0xFF, 0xC9, 0x07, 255)   # CfYellow
WHITE = (255, 255, 255, 255)
SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]   # the sizes the old icon carried

def draw(px, ss=8):
    """The icon at px x px. Designed on a 1024 grid, drawn at px*ss, then reduced."""
    n = px * ss; k = n / 1024
    im = Image.new('RGBA', (n, n), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rounded_rectangle((40 * k, 40 * k, 984 * k, 984 * k), radius=210 * k, fill=NAVY)
    font = ImageFont.truetype(CHEWY, max(1, round(420 * k)))
    text = 'P&L'; track = 6 * k
    width = sum(font.getlength(c) for c in text) + track * (len(text) - 1)
    x = (n - width) / 2
    for c in text:
        d.text((x, 440 * k), c, font=font, fill=WHITE, anchor='lm'); x += font.getlength(c) + track
    def bar(x1, x2, y, h=50):
        d.rounded_rectangle((x1 * k, (y - h / 2) * k, x2 * k, (y + h / 2) * k), radius=h / 2 * k, fill=YELLOW)
    bar(190, 430, 735); bar(594, 834, 735)
    d.ellipse((488 * k, 711 * k, 536 * k, 759 * k), fill=WHITE)
    return im.resize((px, px), Image.LANCZOS)

if __name__ == '__main__':
    images = [draw(s) for s in SIZES]
    out = os.path.join(ASSETS, 'CardFactory.ProfitLoss.ico')
    images[-1].save(out, format='ICO', sizes=[(s, s) for s in SIZES], append_images=images[:-1])
    print(out, SIZES)
