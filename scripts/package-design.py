"""Create a standalone offline HTML and ZIP for this design phase."""
from pathlib import Path
import base64
import html
import zipfile

root = Path(__file__).resolve().parents[1]
output = root.parent / 'output'
output.mkdir(exist_ok=True)
source = (root / 'prototype/index.html').read_text(encoding='utf-8')
for name in ['Manrope', 'SpaceGrotesk']:
    data = base64.b64encode((root / f'prototype/assets/{name}.ttf').read_bytes()).decode()
    source = source.replace(f'url(assets/{name}.ttf)', f'url(data:font/ttf;base64,{data})')
notices = '\n\n'.join((root / f'LICENSES/{name}-OFL.txt').read_text(encoding='utf-8') for name in ['Manrope', 'SpaceGrotesk'])
source = source.replace('</body>', '<template id="font-license-notices">' + html.escape(notices) + '</template></body>')
standalone = output / 'LocalControl-Prototype.html'
standalone.write_text(source, encoding='utf-8')
bundle = output / 'LocalControl-0.1-Design-and-Architecture.zip'
with zipfile.ZipFile(bundle, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for item in sorted(root.rglob('*')):
        if item.is_file() and not any(part in ['node_modules','bin','obj','__pycache__'] for part in item.relative_to(root).parts):
            z.write(item, Path('LocalControl') / item.relative_to(root))
    z.write(standalone, 'LocalControl/LocalControl-Prototype.html')
    if (output / 'LocalControl-Visual-Concept.png').exists():
        z.write(output / 'LocalControl-Visual-Concept.png', 'LocalControl/design/Visual-Concept.png')
with zipfile.ZipFile(bundle) as z:
    assert z.testzip() is None
print(f'{standalone.name}: {standalone.stat().st_size} bytes')
print(f'{bundle.name}: {bundle.stat().st_size} bytes')
