"""Maintainer tool: regenerate the embedded CC0 mask from its pinned source PNG. Requires Pillow."""
from pathlib import Path
from PIL import Image, ImageOps
import hashlib,json
root=Path(__file__).resolve().parents[1]
path=root/'assets/icon-manifest.json';manifest=json.loads(path.read_text(encoding='utf-8'))
source=root/'assets/kenney-arrow_rotate.png'
assert hashlib.sha256(source.read_bytes()).hexdigest()==manifest['sourceSha256']
alpha=Image.open(source).convert('RGBA').getchannel('A');alpha=alpha.crop(alpha.getbbox());alpha.thumbnail((52,52),Image.Resampling.LANCZOS)
mask=Image.new('L',(64,64));mask.paste(alpha,((64-alpha.width)//2,(64-alpha.height)//2))
content=ImageOps.flip(mask).tobytes()
assert hashlib.sha256(content).hexdigest()==manifest['embeddedSha256']
(root/'src/ChainedBeasts.Rerolls/Assets/Reroll.alpha').write_bytes(content)
print('CC0 source and embedded graphic hashes verified.')
