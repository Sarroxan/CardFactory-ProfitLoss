"""
Draws Assets/DailyBriefing-2.png - page 2 of the Daily Briefing as the app prints it since
16 Nov 2026 (owner, concept S3) - from page 2 of the store's own form.

The form's page 2 had a Stamp Count inside Handover. The owner replaced it with Add-on Sales:
Sales & Customer and Operations get 7 lines each, Handover keeps its bar, and the left part of
the Handover box holds the "Add-on Sales" label with "Sold" / "£ Value" headings (the app writes
the rows - Services/DailyBriefingSheet.cs, FillPageTwo). The title, the staple box, the two
blue bars, the footer and Completed by / Signed off are the form's own, untouched.

tools/render-daily-briefing.py makes the form's page 2 (DailyBriefing-2-form.png) and runs
this. To run it on its own:
    pip install playwright && python -m playwright install chromium
    python tools/draw-briefing-page2.py path/to/DailyBriefing-2-form.png

Positions are in px on the page drawn 1684 wide (1 pt = 2.0003 px); the result is 3508 x 2481
(300 dpi), the size of the form render.
"""
import os
import sys
import tempfile

from PIL import Image
from playwright.sync_api import sync_playwright

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(REPO, 'src', 'CardFactory.ProfitLoss.App', 'Assets')
W, H = 1684, 1191           # drawing size; the screenshot is taken at 3508 / 1684
K = 2.0003                  # px per pt
NAVY, TINT, LINE = '#004EA1', '#E3ECF7', '#9DB7DD'


def cut_bar(form, box, out):
    """A blue section bar cut from the form, the grey watermark around it made white."""
    scale = form.width / W
    bar = form.crop(tuple(round(v * scale) for v in box))
    px = bar.load()
    for x in range(bar.width):
        for y in range(bar.height):
            r, g, b = px[x, y]
            if r > 220 and g > 220 and b > 220 and abs(r - b) < 12:
                px[x, y] = (255, 255, 255)
    bar.save(out)


def page_html(form_png, focus_bar, handover_bar):
    fonts = os.path.join(ASSETS, 'Fonts').replace('\\', '/')
    o = [f'''<!doctype html><html><head><meta charset="utf-8"><style>
@font-face{{font-family:Chewy;src:url("file:///{fonts}/Chewy-Regular.ttf")}}
body{{margin:0}}.pg{{position:relative;width:{W}px;height:{H}px;background:url("file:///{form_png}");background-size:{W}px {H}px;overflow:hidden}}
.a{{position:absolute}}
.f{{position:absolute;border:3px solid {NAVY};border-radius:24px;box-sizing:border-box;background:#fff}}
.p{{position:absolute;background:#FFC907;border-radius:20px;font-family:Chewy;color:{NAVY};font-size:22px;padding:2px 14px;line-height:1.1}}
.l{{position:absolute;height:1.6px;background:{LINE}}}
.t{{position:absolute;white-space:nowrap;line-height:1;font-family:Chewy;color:{NAVY};text-align:center}}
</style></head><body><div class="pg">''']

    def frame(x0, y0, x1, y1): o.append(f'<div class="f" style="left:{x0}px;top:{y0}px;width:{x1-x0}px;height:{y1-y0}px"></div>')
    def pill(x, y, t): o.append(f'<div class="p" style="left:{x}px;top:{y}px">{t}</div>')
    def lines(x0, x1, ys): o.extend(f'<div class="l" style="left:{x0}px;top:{y}px;width:{x1-x0}px"></div>' for y in ys)
    def bar(img, y): o.append(f'<img class="a" src="file:///{img}" style="left:80px;top:{y}px;width:1550px;height:108px">')
    def label(x_pt, y_pt, w_pt, t): o.append(f'<div class="t" style="left:{x_pt*K:.1f}px;top:{y_pt*K:.1f}px;width:{w_pt*K:.1f}px;font-size:{10*K:.1f}px">{t}</div>')

    # Everything between the title and Completed by is drawn again.
    o.append('<div class="a" style="left:62px;top:172px;width:1578px;height:900px;background:#fff"></div>')
    bar(focus_bar, 172)
    frame(108, 291, 857, 617); pill(130, 303, 'Sales &amp; Customer')
    frame(872, 291, 1621, 617); pill(894, 303, 'Operations')
    ys = list(range(365, 617 - 14, 36))
    lines(130, 835, ys); lines(894, 1599, ys)
    bar(handover_bar, 623)
    frame(108, 737, 1621, 1063)
    lines(640, 1599, list(range(782, 1050, 36)))
    o.append(f'<div class="a" style="left:618px;top:752px;width:3px;height:296px;background:{TINT}"></div>')
    pill(130, 749, 'Add-on Sales')
    top = 737 / K
    label(300 - 118, top + 10, 46, 'Sold')        # over the Sold boxes (182-228 pt)
    label(300 - 68, top + 10, 68, '£ Value')      # over the £ boxes (232-300 pt)
    o.append('</div></body></html>')
    return ''.join(o)


def main(form_page2):
    form = Image.open(form_page2).convert('RGB')
    work = tempfile.mkdtemp()
    focus_bar = os.path.join(work, 'bar-focus.png').replace('\\', '/')
    handover_bar = os.path.join(work, 'bar-handover.png').replace('\\', '/')
    cut_bar(form, (80, 172, 1630, 280), focus_bar)
    cut_bar(form, (80, 628, 1630, 736), handover_bar)
    html = os.path.join(work, 'page2.html')
    with open(html, 'w', encoding='utf-8') as f:
        f.write(page_html(os.path.abspath(form_page2).replace('\\', '/'), focus_bar, handover_bar))
    out = os.path.join(ASSETS, 'DailyBriefing-2.png')
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = browser.new_page(viewport={'width': W, 'height': H}, device_scale_factor=form.width / W)
        page.goto('file:///' + html.replace('\\', '/'))
        page.evaluate('document.fonts.ready')
        page.screenshot(path=out, clip={'x': 0, 'y': 0, 'width': W, 'height': H})
        browser.close()
    print('wrote', out, Image.open(out).size)


if __name__ == '__main__':
    if len(sys.argv) != 2:
        sys.exit('usage: python tools/draw-briefing-page2.py path/to/DailyBriefing-2-form.png')
    main(sys.argv[1])
