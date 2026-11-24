"""Bake a periodic orientation field for differently brushed hexagonal floor regions.

RG stores doubled-angle direction, B excludes the padded Qwen silhouette,
and A stores grain phase. The visible floor fits within one unique period.
"""
from pathlib import Path
from qwen_paths import DATA_ROOT
import json
import numpy as np
from scipy.ndimage import gaussian_filter, distance_transform_edt, map_coordinates
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]


def main():
    size, columns, rows = 1024, 18, 15
    width, height = 25.92, 24.94153162899183
    xp, zp = width / columns, height / rows
    x, z = np.meshgrid((np.arange(size) + .5) * width / size,
                       (size - np.arange(size) - .5) * height / size)
    ids = np.zeros((size, size), np.int32)
    nearest = np.full((size, size), np.inf)
    column = np.floor(x / xp).astype(np.int32)
    for ox in (-1, 0, 1):
        cx = column + ox
        stagger = cx % 2 * .5
        row = np.floor(z / zp - stagger).astype(np.int32)
        for oz in (-1, 0, 1):
            cz = row + oz
            distance = (x - cx * xp) ** 2 + (z - (cz + stagger) * zp) ** 2
            closer = distance < nearest
            nearest = np.minimum(nearest, distance)
            ids[closer] = ((cx % columns) * rows + cz % rows)[closer]
    rng = np.random.default_rng(20261124)
    # A proper three-colouring of the triangular lattice. Both periods preserve
    # this colouring, including the wrap seam passing through world X/Z = 0.
    q, r = np.meshgrid(np.arange(columns), np.arange(rows), indexing='ij')
    families = ((q - r + q // 2) % 3).ravel()
    degrees = np.empty(columns * rows)
    for family in range(3):
        cells = np.flatnonzero(families == family)
        offsets = (np.arange(len(cells)) + .5) / len(cells) * 28 - 14
        degrees[cells] = (family * 60 + rng.permutation(offsets)) % 180
    assert len(np.unique(np.round(degrees, 6))) == columns * rows
    # Verify all six toroidal neighbours using periodic lattice distances.
    centers = np.stack((q.ravel() * xp, (r.ravel() + q.ravel() % 2 * .5) * zp), axis=-1)
    separation = np.abs(centers[:, None] - centers[None, :])
    separation = np.minimum(separation, np.array([width, height]) - separation)
    neighbours = np.isclose(np.linalg.norm(separation, axis=-1), zp, atol=1e-5)
    assert np.all(neighbours.sum(axis=1) == 6)
    difference = np.abs(degrees[:, None] - degrees[None, :])
    difference = np.minimum(difference, 180 - difference)
    min_neighbour_difference = float(difference[neighbours].min())
    assert min_neighbour_difference >= 32
    angles = np.deg2rad(degrees)
    axes = np.stack((np.cos(2 * angles), np.sin(2 * angles)), axis=-1)
    orientation = axes[ids]
    for channel in range(2):
        orientation[..., channel] = gaussian_filter(orientation[..., channel], .012 * size / width, mode='wrap')
    # Reuse the exact logo footprint, dilated by 12cm with a 5cm soft transition.
    relief = np.asarray(Image.open(ROOT / 'Assets/QwenWorld/Environment/WhiteExhibit/QwenFloorRelief.png').convert('RGBA'))
    distance = distance_transform_edt(relief[..., 2] < 128) * (7.2 / relief.shape[0])
    logo_coverage = np.clip((distance - .12) / .05, 0, 1)
    logo_coverage = logo_coverage * logo_coverage * (3 - 2 * logo_coverage)
    wx = (x + width * .5) % width - width * .5
    wz = (z + height * .5) % height - height * .5
    coords = np.stack(((.5 - wz / 7.2) * relief.shape[0] - .5,
                       (wx / 7.2 + .5) * relief.shape[1] - .5))
    brush_coverage = map_coordinates(logo_coverage, coords, order=1, mode='constant', cval=1)
    phase = rng.uniform(0, 1, columns * rows)[ids]
    data = np.dstack((orientation * .5 + .5, brush_coverage, phase))
    output = ROOT / 'Assets/QwenWorld/Environment/WhiteExhibit/HexBrushDirections.png'
    Image.fromarray(np.rint(np.clip(data, 0, 1) * 255).astype(np.uint8)).save(output)
    report = (DATA_ROOT / 'validation/visual/floor')
    report.mkdir(parents=True, exist_ok=True)
    (report / 'texture.json').write_text(json.dumps({
        'resolution': size, 'hexFlatToFlatMetres': height / rows,
        'worldTileX': width, 'worldTileZ': height, 'cells': columns * rows,
        'uniqueDirections': columns * rows, 'minNeighbourAxisDifferenceDegrees': min_neighbour_difference,
        'allVisibleDirectionsUnique': min(width, height) > 22.4,
        'logoPaddingMetres': .12, 'logoTransitionMetres': .05, 'logoBrushExcluded': True,
        'linearData': True, 'seamless': True, 'macroSurfaceTilt': False,
        'RG': 'cos(2 theta), sin(2 theta)', 'B': 'brush coverage (zero in padded logo)', 'A': 'local grain phase',
    }, indent=2), encoding='utf-8')
    print(output)


if __name__ == '__main__':
    main()
