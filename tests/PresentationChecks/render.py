from pathlib import Path
import json,sys,base64
from PIL import Image,ImageDraw,ImageFont

folder=Path(sys.argv[1])
checks=[]
for source in sorted(folder.glob('*.json')):
    data=json.loads(source.read_text())
    if 'commands' not in data: continue
    image=Image.new('RGBA',(data['width'],data['height']),(39,25,48,255))
    overflow=[]
    for command in data['commands']:
        x,y,w,h=[command[k] for k in ('x','y','width','height')]
        color=tuple(round(v*255) for v in command['color'])
        if command['kind']=='rect':
            layer=Image.new('RGBA',image.size)
            if command.get('pixels'):
                tex=Image.frombytes('RGBA',(command['textureWidth'],command['textureHeight']),base64.b64decode(command['pixels'])).transpose(Image.Transpose.FLIP_TOP_BOTTOM)
                alpha=tex.getchannel('A').point(lambda n: round(n*color[3]/255))
                tex=Image.new('RGBA',tex.size,color);tex.putalpha(alpha)
                tex=tex.resize((round(w),round(h)),Image.Resampling.BILINEAR)
                layer.alpha_composite(tex,(round(x),round(y)))
            else:
                ImageDraw.Draw(layer).rectangle((round(x),round(y),round(x+w),round(y+h)),fill=color)
            image=Image.alpha_composite(image,layer)
            continue
        font=ImageFont.truetype('C:/Windows/Fonts/arialbd.ttf' if command['bold'] else 'C:/Windows/Fonts/arial.ttf',command['size'])
        canvas=Image.new('RGBA',(max(1,round(w)),max(1,round(h))))
        draw=ImageDraw.Draw(canvas)
        lines=[]
        for paragraph in command['text'].split('\n'):
            if not command['wrap']: lines.append(paragraph); continue
            current=''
            for word in paragraph.split():
                nextline=(current+' '+word).strip()
                if current and draw.textlength(nextline,font=font)>w: lines.append(current);current=word
                else:current=nextline
            lines.append(current)
        lineheight=command['size']*1.16
        height=len(lines)*lineheight
        if height>h+1 or any(draw.textlength(line,font=font)>w+1 for line in lines):
            overflow.append({'text':command['text'],'box':[w,h],'neededHeight':height})
        ty=0 if command['align']=='UpperLeft' else (h-height)/2
        for line in lines:
            length=draw.textlength(line,font=font)
            tx=(w-length)/2 if command['align']=='MiddleCenter' else w-length if command['align']=='MiddleRight' else 0
            draw.text((tx,ty),line,font=font,fill=color,anchor='lt')
            ty+=lineheight
        image.alpha_composite(canvas,(round(x),round(y)))
    draw=ImageDraw.Draw(image)
    draw.text((18,14),'REROLL LAYOUT PREVIEW / SAMPLE DATA / NOT A GAME SCREENSHOT',font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',12),fill=(198,185,208))
    image.convert('RGB').save(source.with_suffix('.png'))
    checks.append({'sample':source.name,'overflow':overflow})
(folder/'layout-check.json').write_text(json.dumps(checks,indent=2))
print(json.dumps({'samples':len(checks),'samplesWithTextOverflow':[x for x in checks if x['overflow']]},indent=2))
