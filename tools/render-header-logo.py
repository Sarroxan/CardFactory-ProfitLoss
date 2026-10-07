"""
Renders Assets/HeaderLogoTitle.png at 3x: the smiley (HeaderSmiley.png, unchanged),
"Card Factory", "Profit & Loss" in Chewy and CALCULATOR between two yellow bars.
6B.58: concept F2. 6B.60: S1 (+2px title letter spacing) and white lettering for the
navy header.

Fonts are not in the repo; both are free Google Fonts:
  Chewy-Regular.ttf        github.com/google/fonts (apache/chewy) - the title font, 6B.47
  Fredoka[wdth,wght].ttf   github.com/google/fonts (ofl/fredoka)
Set FONTS to their folder, then:  python tools/render-header-logo.py
"""
from PIL import Image, ImageDraw, ImageFont
S=3
import os
FONTS = os.environ.get('FONTS', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'fonts'))
F = FONTS.rstrip('/') + '/'
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
def fred(px):
    f=ImageFont.truetype(F+'Fredoka[wdth,wght].ttf',px); f.set_variation_by_axes([600,100]); return f
def render(path, title_px=42, track=0.0, xscale=1.0, ink=(0x13,0x30,0x8A,255), bar=(0xFF,0xD1,0x00,255)):
    chewy=ImageFont.truetype(F+'Chewy-Regular.ttf',title_px*S)
    cf=fred(14*S); calc=fred(12*S)
    smiley=Image.open(os.path.join(REPO,'src','CardFactory.ProfitLoss.App','Assets','HeaderSmiley.png')).convert('RGBA')
    # title rendered on its own, then stretched horizontally
    t='Profit & Loss'; tr=track*S
    tw=sum(chewy.getlength(c) for c in t)+tr*(len(t)-1)
    tb=ImageDraw.Draw(Image.new('RGBA',(1,1))).textbbox((0,0),t,font=chewy)
    timg=Image.new('RGBA',(int(tw)+4*S, tb[3]-tb[1]+4*S),(0,0,0,0)); td=ImageDraw.Draw(timg); x=0
    for c in t:
        td.text((x,-tb[1]+S),c,font=chewy,fill=ink); x+=chewy.getlength(c)+tr
    if xscale!=1.0: timg=timg.resize((int(timg.width*xscale),timg.height),Image.LANCZOS)
    titlew=timg.width-4*S
    sm=78*S; gap=12*S
    W=int(sm+gap+titlew+3*S); H=84*S
    img=Image.new('RGBA',(W,H),(0,0,0,0)); d=ImageDraw.Draw(img)
    img.alpha_composite(smiley.resize((sm,sm),Image.LANCZOS),(0,(H-sm)//2))
    x0=sm+gap
    def draw_text(x,y,text,font,ls=0):
        for ch in text:
            d.text((x,y),ch,font=font,fill=ink); x+=font.getlength(ch)+ls
    b_cf=d.textbbox((0,0),'Card Factory',font=cf); b_ca=d.textbbox((0,0),'CALCULATOR',font=calc)
    h_cf=b_cf[3]-b_cf[1]; h_ti=timg.height-4*S; h_ca=b_ca[3]-b_ca[1]
    g1=5*S; g2=7*S
    y=(H-(h_cf+g1+h_ti+g2+h_ca))//2
    draw_text(x0-b_cf[0],y-b_cf[1],'Card Factory',cf); y+=h_cf+g1
    img.alpha_composite(timg,(int(x0),int(y-S))); y+=h_ti+g2
    ls=int(3.5*S); cw=sum(calc.getlength(c) for c in 'CALCULATOR')+ls*9
    barh=4*S; bgap=7*S
    side=(titlew-cw-2*bgap)/2; cy=y+h_ca/2
    d.rounded_rectangle((x0,cy-barh/2,x0+side,cy+barh/2),radius=barh/2,fill=bar)
    draw_text(x0+side+bgap,y-b_ca[1],'CALCULATOR',calc,ls)
    d.rounded_rectangle((x0+side+bgap+cw+bgap,cy-barh/2,x0+titlew,cy+barh/2),radius=barh/2,fill=bar)
    img.save(path)
    return (round(W/S),H//S)
if __name__ == '__main__':
    A = os.path.join(REPO, 'src', 'CardFactory.ProfitLoss.App', 'Assets')
    # Stage 6B.60: F2 with S1 letter spacing (+2px between the letters of "Profit & Loss";
    # the yellow bars follow the title's width), in white for the navy header.
    print(render(os.path.join(A, 'HeaderLogoTitle.png'), track=2.0, ink=(255, 255, 255, 255)))
