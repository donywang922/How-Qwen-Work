# SPDX-License-Identifier: Apache-2.0
"""Rebuild the offline reference-link QR textures used by the spawn lightbox."""
import json
from pathlib import Path
import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
FOLDER = ROOT / 'Assets/QwenWorld/Environment/SpawnLounge'
LINKS = [
    (0, 'NASA 星图 / Deep Star Maps', 'https://svs.gsfc.nasa.gov/4851/'),
    (0, 'NASA 地球 / Blue Marble', 'https://science.nasa.gov/earth/earth-observatory/blue-marble-next-generation/base-map/'),
    (0, 'NASA 夜光 / Black Marble', 'https://science.nasa.gov/earth/earth-observatory/earth-at-night/maps/'),
    (1, 'Qwen3.5-0.8B / Qwen', 'https://huggingface.co/Qwen/Qwen3.5-0.8B'),
    (1, 'ShaderGPT / lox9973', 'https://gitlab.com/lox9973/ShaderGPT'),
    (1, '千问立绘 / ZipZipPipe', 'https://www.pixiv.net/artworks/148186519'),
    (2, 'QvPen / ureishi', 'https://github.com/ureishi/QvPen'),
    (2, 'VRChat Worlds SDK', 'https://creators.vrchat.com/worlds/'),
    (2, 'UdonSharp', 'https://udonsharp.docs.vrchat.com/'),
    (2, 'Creator Companion', 'https://vcc.docs.vrchat.com/'),
]

if __name__ == '__main__':
    FOLDER.mkdir(parents=True, exist_ok=True)
    items = []
    encoder = cv2.QRCodeEncoder_create()
    decoder = cv2.QRCodeDetector()
    for index, (page, title, url) in enumerate(LINKS):
        qr = encoder.encode(url)
        # Keep only the symbol. Its quiet zone comes from the poster's dark empty space,
        # rather than a rectangular white background baked into this asset.
        ys, xs = np.where(qr < 128)
        symbol = qr[ys.min():ys.max()+1, xs.min():xs.max()+1]
        check = cv2.copyMakeBorder(symbol, 4, 4, 4, 4, cv2.BORDER_CONSTANT, value=255)
        check = cv2.resize(check, None, fx=8, fy=8, interpolation=cv2.INTER_NEAREST)
        decoded, _, _ = decoder.detectAndDecode(check)
        if decoded != url:
            raise RuntimeError('QR round-trip failed: ' + title)
        name = f'LinkQR-{index:02}.png'
        rgba = np.zeros((*symbol.shape, 4), dtype=np.uint8)
        rgba[:, :, :3] = (224, 232, 236)  # BGRA: soft ivory modules, transparent gaps.
        rgba[:, :, 3] = 255 - symbol
        rgba = cv2.resize(rgba, None, fx=8, fy=8, interpolation=cv2.INTER_NEAREST)
        cv2.imwrite(str(FOLDER / name), rgba)
        items.append(dict(page=page, title=title, url=url, qr=name))
    (FOLDER / 'ReferenceLinks.json').write_text(json.dumps({'links':items}, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print('LOUNGE_QR_ROUNDTRIP_PASSED', len(items))
