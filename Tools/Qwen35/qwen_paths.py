"""Portable local work paths; model payloads are generated, never fetched at runtime."""
import json
import os
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[2]


def resolve_data_root(project_root=PROJECT_ROOT, environ=None):
    environ = os.environ if environ is None else environ
    config = project_root / '.qwen-local.json'
    local = json.loads(config.read_text(encoding='utf-8-sig')) if config.exists() else {}
    value = environ.get('QWEN_DATA_ROOT') or local.get('dataRoot') or '.qwen-work'
    path = Path(os.path.expandvars(str(value))).expanduser()
    return (path if path.is_absolute() else project_root / path).resolve()


DATA_ROOT = resolve_data_root()
MODEL_ROOT = DATA_ROOT / 'models/Qwen3.5-0.8B'


def shadergpt_vendor():
    lock = json.loads((Path(__file__).parent / 'sources.lock.json').read_text(encoding='utf-8'))
    commit = lock['exporter']['commit']
    expected = DATA_ROOT / 'vendor' / ('ShaderGPT-' + commit)
    if (expected / 'Python~').is_dir():
        return expected
    # Accept the upstream archive's doubled commit directory, without selecting arbitrary versions.
    candidates = sorted((DATA_ROOT / 'vendor').glob('ShaderGPT-' + commit + '*/Python~'))
    if not candidates:
        raise FileNotFoundError('Pinned ShaderGPT exporter is missing; run Initialize-Qwen.ps1.')
    return candidates[0].parent
