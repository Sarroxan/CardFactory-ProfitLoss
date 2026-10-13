"""
The header logo as vector shapes for MainWindow.xaml, so it is drawn sharp at whatever
size the dashboard is scaled to, instead of a 991x252 picture shrunk to 330x84 (soft).

Same layout as tools/render-header-logo.py (6B.58 concept F2, 6B.60 S1, white): the
smiley (still HeaderSmiley.png), "Card Factory" and CALCULATOR in Fredoka SemiBold,
"Profit & Loss" in Chewy with +2px letter spacing, and the two yellow bars. Each letter
is placed exactly where that script places it (letter by letter, no kerning), and its
outline is taken from the font file, so the result matches the old artwork.

Fonts are not in the repo; both are free Google Fonts (see render-header-logo.py):
  Chewy-Regular.ttf, Fredoka[wdth,wght].ttf
Set FONTS to their folder, then:  python tools/header-logo-xaml.py
Prints the XAML <Path> elements, and writes header-logo-check.png - the paths drawn at
3x beside the old artwork - for comparison.
"""
import os
from PIL import Image, ImageDraw, ImageFont
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen

S = 3                                   # the old artwork's scale; XAML is in 1x units
FONTS = os.environ.get('FONTS', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'fonts')).rstrip('/') + '/'
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FREDOKA = next(F for F in (FONTS + 'Fredoka[wdth,wght].ttf', FONTS + 'Fredoka.ttf') if os.path.exists(F))
CHEWY = FONTS + 'Chewy-Regular.ttf'
YELLOW = '#FFD100'

def pil_fred(px):
    f = ImageFont.truetype(FREDOKA, px); f.set_variation_by_axes([600, 100]); return f

fred_tt = instancer.instantiateVariableFont(TTFont(FREDOKA), {'wght': 600, 'wdth': 100})
chewy_tt = TTFont(CHEWY)

def outline(tt, ch, size_px, x, baseline):
    """SVG/XAML path data for one character, size in px at 3x, placed at (x, baseline), in 1x units."""
    cmap = tt.getBestCmap(); gs = tt.getGlyphSet(); upm = tt['head'].unitsPerEm
    name = cmap.get(ord(ch))
    if name is None or ch == ' ':
        return ''
    k = size_px / upm / S
    pen = SVGPathPen(gs, ntos=lambda v: ('%.2f' % v).rstrip('0').rstrip('.'))
    gs[name].draw(TransformPen(pen, (k, 0, 0, -k, x / S, baseline / S)))
    return pen.getCommands()

def layout(title_px=42, track=2.0):
    """The same arithmetic as render-header-logo.py's render(); returns shapes in 3x units."""
    chewy = ImageFont.truetype(CHEWY, title_px * S)
    cf = pil_fred(14 * S); calc = pil_fred(12 * S)
    t = 'Profit & Loss'; tr = track * S
    tw = sum(chewy.getlength(c) for c in t) + tr * (len(t) - 1)
    probe = ImageDraw.Draw(Image.new('RGBA', (1, 1)))
    tb = probe.textbbox((0, 0), t, font=chewy)
    timg_w = int(tw) + 4 * S
    titlew = timg_w - 4 * S
    sm = 78 * S; gap = 12 * S
    W = int(sm + gap + titlew + 3 * S); H = 84 * S
    x0 = sm + gap
    b_cf = probe.textbbox((0, 0), 'Card Factory', font=cf); b_ca = probe.textbbox((0, 0), 'CALCULATOR', font=calc)
    h_cf = b_cf[3] - b_cf[1]; h_ti = (tb[3] - tb[1] + 4 * S) - 4 * S; h_ca = b_ca[3] - b_ca[1]
    g1 = 5 * S; g2 = 7 * S
    y = (H - (h_cf + g1 + h_ti + g2 + h_ca)) // 2
    glyphs = []   # (font, size_px_3x, char, x_3x, top_y_3x, pil_font)
    def put(x, top, text, tt, size, font, ls=0):
        for ch in text:
            glyphs.append((tt, size, ch, x, top, font)); x += font.getlength(ch) + ls
    put(x0 - b_cf[0], y - b_cf[1], 'Card Factory', fred_tt, 14 * S, cf); y += h_cf + g1
    put(int(x0), int(y - S) + (-tb[1] + S), t, chewy_tt, title_px * S, chewy, tr); y += h_ti + g2
    ls = int(3.5 * S); cw = sum(calc.getlength(c) for c in 'CALCULATOR') + ls * 9
    barh = 4 * S; bgap = 7 * S
    side = (titlew - cw - 2 * bgap) / 2; cy = y + h_ca / 2
    bars = [(x0, cy - barh / 2, x0 + side, cy + barh / 2),
            (x0 + side + bgap + cw + bgap, cy - barh / 2, x0 + titlew, cy + barh / 2)]
    put(x0 + side + bgap, y - b_ca[1], 'CALCULATOR', fred_tt, 12 * S, calc, ls)
    return W, H, sm, glyphs, bars

def build():
    W, H, sm, glyphs, bars = layout()
    data = []
    for tt, size, ch, x, top, font in glyphs:
        # PIL's default anchor puts the top of the line (the ascender) at y.
        baseline = top + font.getmetrics()[0]
        d = outline(tt, ch, size, x, baseline)
        if d: data.append(d)
    return W, H, sm, ' '.join(data), bars

def xaml(W, H, sm, data, bars):
    f = lambda v: ('%.2f' % (v / S)).rstrip('0').rstrip('.')
    out = []
    out.append(f'<Image Canvas.Left="0" Canvas.Top="{f((H - sm) // 2)}" Width="{f(sm)}" Height="{f(sm)}" Source="Assets/HeaderSmileyFace.png" Stretch="Uniform" RenderOptions.BitmapScalingMode="HighQuality" />')
    out.append(f'<Path Fill="White" Data="F1 {data}" />')
    for (x1, y1, x2, y2) in bars:
        r = f((y2 - y1) / 2)
        out.append(f'<Rectangle Canvas.Left="{f(x1)}" Canvas.Top="{f(y1)}" Width="{f(x2 - x1)}" Height="{f(y2 - y1)}" RadiusX="{r}" RadiusY="{r}" Fill="{YELLOW}" />')
    return f(W), f(H), out

def check(W, H, sm, data, bars, path):
    """Draws the path data at 3x with FreeType (non-zero fill, as WPF's F1) on navy, beside
    the old artwork on navy, and returns the largest pixel difference."""
    from fontTools.pens.freetypePen import FreeTypePen
    from fontTools.svgLib.path import parse_path
    pen = FreeTypePen(None)
    parse_path(data, TransformPen(pen, (S, 0, 0, S, 0, 0)))   # data without WPF's F1 prefix
    mask = pen.image(width=W, height=H, transform=(1, 0, 0, -1, 0, H), contain=False, evenOdd=False)
    navy = (0, 78, 161, 255)
    new = Image.new('RGBA', (W, H), navy)
    smiley = Image.open(os.path.join(REPO, 'src', 'CardFactory.ProfitLoss.App', 'Assets', 'HeaderSmiley.png')).convert('RGBA')
    new.alpha_composite(smiley.resize((sm, sm), Image.LANCZOS), (0, (H - sm) // 2))
    white = Image.new('RGBA', (W, H), (255, 255, 255, 255))
    new = Image.composite(white, new, mask.getchannel('A') if mask.mode in ('RGBA', 'LA') else mask.convert('L'))
    d = ImageDraw.Draw(new)
    for (x1, y1, x2, y2) in bars:
        d.rounded_rectangle((x1, y1, x2, y2), radius=(y2 - y1) / 2, fill=(0xFF, 0xD1, 0x00, 255))
    old = Image.new('RGBA', (W, H), navy)
    old.alpha_composite(Image.open(os.path.join(REPO, 'src', 'CardFactory.ProfitLoss.App', 'Assets', 'HeaderLogoTitle.png')).convert('RGBA'))
    sheet = Image.new('RGB', (W, H * 2 + 10), (255, 255, 255))
    sheet.paste(old.convert('RGB'), (0, 0)); sheet.paste(new.convert('RGB'), (0, H + 10))
    sheet.save(path)
    from PIL import ImageChops
    diff = ImageChops.difference(old.convert('L'), new.convert('L'))
    hist = diff.histogram(); total = W * H
    big = sum(hist[64:])
    return max(i for i, v in enumerate(hist) if v), big / total

if __name__ == '__main__':
    W, H, sm, data, bars = build()
    w, h, lines = xaml(W, H, sm, data, bars)
    print(f'<!-- canvas {w} x {h} -->')
    print('\n'.join(lines))
    mx, frac = check(W, H, sm, data, bars, os.environ.get('CHECK_PNG', 'header-logo-check.png'))
    print(f'<!-- check: max pixel difference {mx}/255; pixels differing by >25%: {frac:.3%} -->')
