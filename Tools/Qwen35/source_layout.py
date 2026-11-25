"""The public source archive mirrors the repository's Unity/Git exclusions."""
from pathlib import Path

ROOT_FILES = {'LICENSE','NOTICE','README.md','README.zh-CN.md','THIRD_PARTY_NOTICES.md',
              '.gitignore','.gitattributes','.editorconfig','qwen-local.example.json'}
ROOT_DIRECTORIES = {'Assets','ProjectSettings','Packages','Tools','Docs'}
EXCLUDED_PREFIXES = ('Packages/com.vrchat.base/','Packages/com.vrchat.worlds/',
    'Packages/com.vrchat.core.vpm-resolver/','Assets/UdonSharp/','Assets/Scenes/',
    'Assets/SerializedUdonPrograms/','Assets/QwenWorld/Generated/Build/')
EXCLUDED_FILES = {'Assets/UdonSharp.meta','Assets/Scenes.meta','Assets/SerializedUdonPrograms.meta',
    'Assets/QwenWorld/Generated/Build.meta','Docs/Qwen35-Validation-Summary.json'}


def is_source_path(relative):
    name = str(relative).replace('\\','/')
    parts = name.split('/')
    if len(parts) == 1:
        return name in ROOT_FILES
    if parts[0] not in ROOT_DIRECTORIES or name in EXCLUDED_FILES:
        return False
    if any(p in {'__pycache__','.git','.idea','.vs'} for p in parts):
        return False
    if name.startswith(EXCLUDED_PREFIXES) or name.endswith(('.pyc','.part','.tmp','.vrcw','.blend1','.blend2')):
        return False
    if name.startswith('Assets/QwenWorld/Generated/Bake.unity'):
        return False
    if name.startswith('Assets/QwenWorld/Models/'):
        if name.endswith(('.png','.png.meta','.exr','.exr.meta','/tokenizer.json','/tokenizer.json.meta')):
            return False
    return True


def source_files(root):
    for name in sorted(ROOT_FILES):
        path = root / name
        if path.is_file():
            yield path
    for name in sorted(ROOT_DIRECTORIES):
        for path in sorted((root / name).rglob('*')):
            if path.is_file() and is_source_path(path.relative_to(root)):
                yield path
