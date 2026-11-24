"""Fetch the pinned ShaderGPT Python exporter; never install it inside Assets."""
import argparse
import hashlib
import json
import stat
import urllib.request
import zipfile
from pathlib import Path, PurePosixPath

from qwen_paths import DATA_ROOT


def install(cache_root, offline=False):
    lock = json.loads((Path(__file__).parent / 'sources.lock.json').read_text(encoding='utf-8'))['exporter']
    cache_root.mkdir(parents=True, exist_ok=True)
    archive = cache_root / ('ShaderGPT-' + lock['commit'][:7] + '.zip')
    if not archive.exists():
        if offline:
            raise FileNotFoundError('Pinned exporter archive is not cached; rerun without -Offline.')
        temporary = archive.with_suffix('.zip.part')
        with urllib.request.urlopen(lock['url'], timeout=90) as source, temporary.open('wb') as target:
            while block := source.read(1024 * 1024):
                target.write(block)
        temporary.replace(archive)
    with archive.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    if digest != lock['archiveSha256']:
        raise ValueError('Exporter archive checksum differs from sources.lock.json.')
    target_root = cache_root / ('ShaderGPT-' + lock['commit'])
    with zipfile.ZipFile(archive) as package:
        for entry in package.infolist():
            parts = PurePosixPath(entry.filename).parts
            if len(parts) < 2 or parts[1] != 'Python~' or entry.is_dir():
                continue
            if '..' in parts or stat.S_ISLNK(entry.external_attr >> 16):
                raise ValueError('Unsafe archive member')
            target = (target_root / Path(*parts[1:])).resolve()
            if not target.is_relative_to(target_root.resolve()):
                raise ValueError('Archive member escapes exporter directory')
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(package.read(entry))
    if not (target_root / 'Python~/convert_hf_model.py').is_file():
        raise ValueError('Archive has no ShaderGPT converter')
    print('Pinned exporter ready:', target_root)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--offline', action='store_true')
    args = parser.parse_args()
    install(DATA_ROOT / 'vendor', args.offline)
