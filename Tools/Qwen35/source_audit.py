"""Audit/export source without initializing a repository or uploading anything."""
import argparse
import json
import re
import zipfile
from datetime import datetime, timezone
from pathlib import Path

from qwen_paths import DATA_ROOT, PROJECT_ROOT
from source_layout import source_files

PRIVATE_PATH = re.compile(r'[A-Z]:[\\/]+Users[\\/]+|\.codex[/\\]|' + '(?:thread|plugin)' + r'://', re.I)
SECRET = re.compile(r'\b(?:gh[pousr]_[A-Za-z0-9_]{20,}|hf_[A-Za-z0-9]{20,}|sk-[A-Za-z0-9]{24,})\b')
WORLD_ID = re.compile(r'blueprintId:\s*wrld_[0-9a-f-]{36}', re.I)
TEXT_SUFFIXES = {'.cs','.py','.ps1','.md','.txt','.json','.yaml','.yml','.unity','.prefab',
                 '.mat','.meta','.asset','.shader','.cginc','.asmdef'}


def audit(root=PROJECT_ROOT):
    files = list(source_files(root))
    issues = []
    artwork = root / 'Docs/QwenGirl-Asset-Source.json'
    if artwork.exists() and json.loads(artwork.read_text(encoding='utf-8-sig')).get('licenseStatus') == 'unverified':
        issues.append({'file':'Docs/QwenGirl-Asset-Source.json','issue':'standee author/redistribution permission is unverified; do not publish the complete source package yet'})
    for path in files:
        name = path.relative_to(root).as_posix()
        if path.is_symlink():
            issues.append({'file':name,'issue':'symlink'})
        if path.stat().st_size > 100 * 1024 * 1024:
            issues.append({'file':name,'issue':'file exceeds ordinary Git limit'})
        if path.suffix not in TEXT_SUFFIXES:
            continue
        raw = path.read_bytes()
        if b'\0' in raw[:2048]:
            continue
        text = raw.decode('utf-8-sig',errors='replace')
        # Do not print matching values: credentials should never enter an audit log.
        if PRIVATE_PATH.search(text): issues.append({'file':name,'issue':'personal path or chat reference'})
        if SECRET.search(text): issues.append({'file':name,'issue':'possible credential'})
        if WORLD_ID.search(text): issues.append({'file':name,'issue':'published world binding'})
    return files, {'passed':not issues,'checkedAtUtc':datetime.now(timezone.utc).isoformat(),
        'files':len(files),'bytes':sum(p.stat().st_size for p in files),
        'largestFileBytes':max((p.stat().st_size for p in files),default=0),'issues':issues,
        'scope':'Candidate source files only; local caches, SDK packages and generated model payloads excluded. Pattern scan is not an exhaustive security review.'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--package',action='store_true')
    args = parser.parse_args()
    files, result = audit()
    output = DATA_ROOT / 'validation/open-source.json'
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    if result['issues']:
        print(json.dumps(result,ensure_ascii=False,indent=2))
        raise SystemExit(1)
    if args.package:
        archive = DATA_ROOT / 'releases/How-Qwen-Work-source.zip'
        archive.parent.mkdir(parents=True,exist_ok=True)
        with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as package:
            for path in files:
                package.write(path,path.relative_to(PROJECT_ROOT).as_posix())
        print('SOURCE_PACKAGE_READY',archive)
    print('SOURCE_AUDIT_PASSED',result['files'],'files;',result['bytes'],'bytes')
