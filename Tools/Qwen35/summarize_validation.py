"""Summarize available local evidence without depending on another machine's history."""
import json
from datetime import datetime, timezone
from pathlib import Path

from qwen_paths import DATA_ROOT


def main():
    root = DATA_ROOT / 'validation'
    reports = {}
    names = ['gpu-parity.json','world-smoke.json','full-project/validation.json',
             'console-layout/validation.json','observation-platform/validation.json',
             'presentation-update/validation.json','localization/validation.json',
             'no-system/validation.json','source-import.json','open-source.json']
    for name in names:
        path = root / name
        if path.is_file():
            reports[name] = json.loads(path.read_text(encoding='utf-8-sig'))
    packages = []
    for platform in ['PC','Android']:
        path = root / ('bundle-' + platform + '.json')
        if not path.is_file():
            continue
        record = json.loads(path.read_text(encoding='utf-8-sig'))
        bundle = Path(record['path'])
        record['existsAtRecordedPath'] = bundle.is_file()
        record['sizeMatchesReport'] = bundle.is_file() and bundle.stat().st_size == record['bytes']
        packages.append(record)
    result = {'collectedAtUtc':datetime.now(timezone.utc).isoformat(),
        'scope':'Available local reports; presence is not proof that a report covers the latest source. Missing reports do not prevent building.',
        'promptTemplate':'user-assistant-no-system-v1','reports':reports,'packages':packages}
    output = root / 'summary.json'
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('LOCAL_SUMMARY_READY',output)


if __name__ == '__main__':
    main()
