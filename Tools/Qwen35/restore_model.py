"""Restore generated weight payloads and their fixed Unity GUID/import settings."""
import argparse
import hashlib
import json
import shutil
from pathlib import Path, PurePosixPath

from qwen_paths import DATA_ROOT, PROJECT_ROOT


def safe_target(root, relative):
    path = PurePosixPath(relative)
    if path.is_absolute() or '..' in path.parts or '\\' in relative:
        raise ValueError('Invalid payload path: ' + relative)
    result = (root / Path(*path.parts)).resolve()
    if not result.is_relative_to(root.resolve()):
        raise ValueError('Payload escapes model directory')
    return result


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def restore(export, project=PROJECT_ROOT, check_only=False, manifest=None):
    manifest = manifest or json.loads((Path(__file__).parent / 'model-payloads.lock.json').read_text(encoding='utf-8'))
    destination = safe_target(project, manifest['modelFolder'])
    # Check every source before changing the Unity checkout. This catches interrupted exports.
    for payload in manifest['payloads']:
        source = safe_target(export, payload['path'])
        if not source.is_file() or digest(source) != payload['sha256']:
            raise ValueError('Missing or different exported payload: ' + payload['path'])
    changed = 0
    for payload in manifest['payloads']:
        source = safe_target(export, payload['path'])
        target = safe_target(destination, payload['path'])
        meta = Path(str(target) + '.meta')
        same = target.is_file() and digest(target) == payload['sha256']
        same_meta = meta.is_file() and meta.read_text(encoding='utf-8-sig') == payload['meta']
        if check_only:
            if not same or not same_meta:
                raise ValueError('Checkout payload/import settings missing: ' + payload['path'])
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            if not same:
                temporary = target.with_name(target.name + '.part')
                shutil.copyfile(source, temporary)
                temporary.replace(target)
                changed += 1
            if not same_meta:
                meta.write_text(payload['meta'], encoding='utf-8')
    print('Model payloads ready:', len(manifest['payloads']), '; copied:', changed, '; Unity GUIDs preserved')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--export', type=Path, default=DATA_ROOT / 'exports/Qwen35-Full-Q4')
    parser.add_argument('--project', type=Path, default=PROJECT_ROOT)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    restore(args.export, args.project, args.check)
