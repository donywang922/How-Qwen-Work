"""Derive linear relief data from the supplied Qwen icon, preserving its contours.

RGB is not a decal: RG stores world X/Z surface slopes, B footprint coverage,
A the folded-face finish. The right-hand wordmark is excluded before processing.
"""
import argparse
import json
from pathlib import Path
from qwen_paths import DATA_ROOT
import numpy as np
from PIL import Image
from scipy.ndimage import binary_fill_holes, gaussian_filter

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('source', type=Path, nargs='?', default=Path(__file__).parent / 'Source/QwenLogoReference.jpg')
    args = parser.parse_args()
    source = Image.open(args.source).convert('RGB')
    # This supplied 1280x434 image places only the icon in the first 385 pixels.
    crop = np.asarray(source.crop((60, 65, 375, 380)), dtype=np.float32) / 255
    saturation = crop.max(axis=2) - crop.min(axis=2)
    purple = np.clip((saturation - .035) / .16, 0, 1)
    footprint = binary_fill_holes(purple > .45).astype(np.float32)
    size = 1024
    def enlarge(data):
        return np.asarray(Image.fromarray(data).resize((size, size), Image.Resampling.BICUBIC))
    footprint = np.clip(gaussian_filter(enlarge(footprint), 1.8), 0, 1)
    face = np.clip(gaussian_filter(enlarge(purple), 1.2), 0, 1) * footprint
    # A 2mm raised imprint plus a 1mm fold, over a 7.2m square footprint.
    height = gaussian_filter(footprint * .002 + face * .001, 2.0)
    dz, dx = np.gradient(height, 7.2 / size)
    # Source rows run toward world -Z. The signed data therefore flips that slope.
    slopes = np.stack((-dx, dz), axis=-1)
    packed = np.empty((size, size, 4), dtype=np.uint8)
    packed[..., :2] = np.rint(np.clip(slopes / .20, -1, 1) * 127 + 128).astype(np.uint8)
    packed[..., 2] = np.rint(footprint * 255).astype(np.uint8)
    packed[..., 3] = np.rint(face * 255).astype(np.uint8)
    output = ROOT / 'Assets/QwenWorld/Environment/WhiteExhibit/QwenFloorRelief.png'
    Image.fromarray(packed).save(output)
    report = (DATA_ROOT / 'validation/floor-logo')
    report.mkdir(parents=True, exist_ok=True)
    Image.fromarray(np.uint8(255 * footprint)).save(report / 'icon-footprint.png')
    Image.fromarray(np.uint8(255 * height / .003)).save(report / 'icon-height.png')
    (report / 'texture.json').write_text(json.dumps({
        'source': str(args.source), 'sourceCrop': [60, 65, 375, 380],
        'resolution': size, 'worldWidth': 7.2, 'maxHeightMetres': float(height.max()),
        'maxSlope': float(np.abs(slopes).max()), 'wordmarkIncluded': False,
        'channels': {'RG': 'signed world X/Z normal slopes', 'B': 'silhouette', 'A': 'folded face finish'},
    }, indent=2), encoding='utf-8')
    print(output)


if __name__ == '__main__':
    main()
