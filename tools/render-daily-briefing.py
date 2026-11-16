"""
Makes Assets/DailyBriefing-1.png and -2.png, the blank Daily Briefing the app fills in
(Services/DailyBriefingSheet.cs), from the store's own form PDF.

Page 1's printed times (10:00 ... 21:00) are removed - only that text, the background
under it is untouched - because the app writes the times itself to match the hours the
store traded. Both pages are rendered at 300 dpi.

Page 2 is not the form's page 2 any more (owner, 16 Nov 2026, S3: Add-on Sales in place of
the Stamp Count). The form's page 2 is saved as DailyBriefing-2-form.png in a temporary folder
and tools/draw-briefing-page2.py draws the app's page 2 from it (needs playwright).

The form PDF is not in the repo (it is the store's document). With it to hand:
    pip install pymupdf pillow
    python tools/render-daily-briefing.py path/to/Daily_Briefing.pdf

If the form changes, the box positions in DailyBriefingSheet.cs (PDF points) must be
re-read too, e.g. with pdfplumber: page.curves gives every rounded box's bbox.
"""
import os
import subprocess
import sys
import tempfile
import pymupdf
from PIL import Image

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(REPO, 'src', 'CardFactory.ProfitLoss.App', 'Assets')
TIMES = [f'{h}:00' for h in range(10, 22)]

def main(pdf):
    doc = pymupdf.open(pdf)
    first = doc[0]
    removed = 0
    for t in TIMES:
        for r in first.search_for(t):
            if r.x0 < 90:                                   # the time column only
                first.add_redact_annot(r, fill=False); removed += 1
    first.apply_redactions(images=pymupdf.PDF_REDACT_IMAGE_NONE,
                           graphics=pymupdf.PDF_REDACT_LINE_ART_NONE,
                           text=pymupdf.PDF_REDACT_TEXT_REMOVE)
    print('times removed:', removed)
    out = os.path.join(ASSETS, 'DailyBriefing-1.png')
    first.get_pixmap(dpi=300).save(out)
    Image.open(out).convert('RGB').save(out, optimize=True)
    print(out)
    form2 = os.path.join(tempfile.mkdtemp(), 'DailyBriefing-2-form.png')
    doc[1].get_pixmap(dpi=300).save(form2)
    subprocess.run([sys.executable, os.path.join(REPO, 'tools', 'draw-briefing-page2.py'), form2], check=True)

if __name__ == '__main__':
    main(sys.argv[1])
