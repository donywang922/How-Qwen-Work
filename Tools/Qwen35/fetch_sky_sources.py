"""Download NASA source images outside Assets; retain provenance and SHA-256."""
from pathlib import Path
from qwen_paths import DATA_ROOT
from concurrent.futures import ThreadPoolExecutor
import argparse
import hashlib
import json
import requests

SOURCES = [
    dict(file='starmap_2020_4k.exr',
         url='https://svs.gsfc.nasa.gov/vis/a000000/a004800/a004851/starmap_2020_4k.exr',
         page='https://svs.gsfc.nasa.gov/4851/',
         credit='NASA/Goddard Space Flight Center Scientific Visualization Studio; Gaia DR2: ESA/Gaia/DPAC.',
         description='Catalog-derived celestial star map, linear half-float EXR. Not a single photograph.'),
    dict(file='world.200407.3x21600x10800.jpg',
         url='https://assets.science.nasa.gov/content/dam/science/esd/eo/images/bmng/bmng-base/july/world.200407.3x21600x10800.jpg',
         page='https://science.nasa.gov/earth/earth-observatory/blue-marble-next-generation/base-map/',
         credit='NASA Earth Observatory; Reto Stoeckli, NASA/GSFC; MODIS/Terra and Aqua.',
         description='Blue Marble Next Generation, July 2004 cloud-free composite.'),
    dict(file='BlackMarble_2016_3km.jpg',
         url='https://assets.science.nasa.gov/content/dam/science/esd/eo/images/imagerecords/144000/144898/BlackMarble_2016_3km.jpg',
         page='https://science.nasa.gov/earth/earth-observatory/earth-at-night/maps/',
         credit='NASA Earth Observatory; Suomi NPP/VIIRS night-light observations, 2016.',
         description='Color night map. Dark background is removed before compositing the city lights.'),
]


def download(source, directory):
    path=directory/source['file']
    resolved=source['url']
    if not path.exists():
        partial=path.with_suffix(path.suffix+'.part')
        with requests.get(source['url'],stream=True,timeout=(15,120)) as response:
            response.raise_for_status()
            if 'text/html' in response.headers.get('Content-Type',''):
                raise RuntimeError(f"Expected an image, received HTML: {response.url}")
            resolved=response.url
            with partial.open('wb') as output:
                for chunk in response.iter_content(1024*1024):
                    output.write(chunk)
        partial.replace(path)
    result=dict(source,resolved_url=resolved,bytes=path.stat().st_size,sha256=hashlib.sha256(path.read_bytes()).hexdigest())
    print(f"Ready {path.name}: {result['bytes']:,} bytes",flush=True)
    return result


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--directory',type=Path,default=(DATA_ROOT / 'art/sky/nasa'))
    args=parser.parse_args();args.directory.mkdir(parents=True,exist_ok=True)
    with ThreadPoolExecutor(max_workers=4) as pool:
        results=list(pool.map(lambda source:download(source,args.directory),SOURCES))
    manifest=dict(sources=results,usage='https://www.nasa.gov/nasa-brand-center/images-and-media/',
                  processing='Offline artistic composite; not a NASA-produced image or a scientific reconstruction of a specific orbit/time.')
    (args.directory/'source-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
