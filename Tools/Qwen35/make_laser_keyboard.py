"""Draw the console's fixed laser key layout using the project's OFL font."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT=Path(__file__).resolve().parents[2]
width,height=1680,600
ink=Image.new('RGBA',(width,height))
draw=ImageDraw.Draw(ink)
font=ImageFont.truetype(str(ROOT/'Assets/QwenWorld/Fonts/NotoSansCJKsc-Regular.otf'),31)
rows=[
 [(x,1) for x in ['Esc','1','2','3','4','5','6','7','8','9','0','-','=','⌫']],
 [('Tab',1.5)]+[(x,1) for x in 'QWERTYUIOP']+[('[',1),(']',1),('\\',1)],
 [('Caps',1.75)]+[(x,1) for x in 'ASDFGHJKL']+[(';',1),("'",1),('Enter',2.0)],
 [('Shift',2.25)]+[(x,1) for x in 'ZXCVBNM']+[(',',1),('.',1),('/',1),('Shift',2.5)],
 [('Ctrl',1.25),('Win',1.25),('Alt',1.25),('SPACE',6.0),('Alt',1.25),('Fn',1.25),('Ctrl',1.25)]
]
margin=30
for row,keys in enumerate(rows):
 unit=(width-2*margin)/sum(size for _,size in keys)
 x=margin
 for label,size in keys:
  box=(round(x+6),round(28+row*111),round(x+size*unit-6),round(28+row*111+94))
  draw.rounded_rectangle(box,radius=9,outline=(12,149,239,245),width=5)
  cx=(box[0]+box[2])/2;cy=(box[1]+box[3])/2
  draw.text((cx,cy),label,font=font,anchor='mm',fill=(165,230,255,255),stroke_width=1,stroke_fill=(13,92,142,245))
  x+=size*unit
draw.rounded_rectangle((9,8,width-10,height-9),radius=18,outline=(64,140,238,150),width=2)
glow=ink.filter(ImageFilter.GaussianBlur(5))
glow.putalpha(glow.getchannel('A').point(lambda value:int(value*.50)))
result=Image.alpha_composite(glow,ink)
output=ROOT/'Assets/QwenWorld/Environment/WhiteExhibit/LaserKeyboard.png'
result.save(output)
print(output)
