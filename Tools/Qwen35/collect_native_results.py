"""Extract this world's native regression markers without copying personal logs."""
import argparse
import hashlib
import json
import re
from datetime import datetime, timezone
from pathlib import Path

from qwen_paths import DATA_ROOT


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--log', type=Path)
    parser.add_argument('--device', default='unspecified')
    args = parser.parse_args()
    logs = Path.home() / 'AppData/LocalLow/VRChat/VRChat'
    source = args.log or max(logs.glob('output_log*.txt'), key=lambda p: p.stat().st_mtime)
    lines = source.read_text(encoding='utf-8-sig', errors='replace').splitlines()
    markers = [line for line in lines if 'QWEN_NATIVE_' in line or 'QWEN_EXHIBIT_' in line]
    assert any('QWEN_NATIVE_VALIDATION_PASS' in line for line in markers), 'Native regression incomplete'
    assert not any('QWEN_NATIVE_VALIDATION_FAIL' in line for line in markers), 'Native regression failed'
    assert any('QWEN_NATIVE_CONTROLS_PASS' in line for line in markers), 'Controls not verified'
    assert any('QWEN_NATIVE_OBSERVATION_PASS' in line for line in markers), 'Observation platform not verified'
    assert any('QWEN_EXHIBIT_BENCH_DONE' in line for line in markers), 'Benchmark incomplete'
    cases = []
    for line in markers:
        match = re.search(r'QWEN_NATIVE_CASE_PASS (\w+) tokens=(\d+) seconds=([\d.]+)', line)
        if match:
            cases.append({'language': match[1], 'tokens': int(match[2]),
                          'seconds': float(match[3]), 'exactTokenSequence': True})
    reference = json.loads((DATA_ROOT / 'validation/world-reference-full.json').read_text(encoding='utf-8-sig'))
    assert reference.get('template') == 'user-assistant-no-system-v1', 'Refresh world reference first'
    assert [(case['language'], case['tokens']) for case in cases] == [(case['lang'], len(case['generated_ids'])) for case in reference['cases']]
    frames = [json.loads(line.split('QWEN_EXHIBIT_FRAME_REPORT ', 1)[1])
              for line in markers if 'QWEN_EXHIBIT_FRAME_REPORT ' in line]
    assert len(frames) == 5 and all(frame['samples'] > 0 for frame in frames)
    assert any('QWEN_EXHIBIT_RELIEF_TARGET' in line for line in markers), 'Outdated camera benchmark'
    bundle = DATA_ROOT / 'builds/QwenLaboratory-SDK-Validation-PC.vrcw'
    with bundle.open('rb') as stream:
        bundle_hash = hashlib.file_digest(stream, 'sha256').hexdigest()
    result = {
        'passed': True, 'collectedAtUtc': datetime.now(timezone.utc).isoformat(),
        'sourceFile': source.name, 'device': args.device,
        'scope': 'One actual desktop VRChat client. Frame intervals include frame cap and CPU/GPU waits; not isolated GPU timings or VR/multiplayer guarantees.',
        'cases': cases, 'pauseAndSingleStep': True, 'frames': frames,
        'observationPlatform': {'outerSpawn': True, 'riseAndDescentCarry': True,
                                'fallReset': True, 'hiddenAtLowest': True, 'voidRecovery': True},
        'benchmarkBundle': str(bundle), 'bundleSha256AtRun': bundle_hash,
    }
    output = DATA_ROOT / 'validation/full-project'
    output.mkdir(parents=True, exist_ok=True)
    (output / 'native-client-regression.json').write_text(
        json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    (output / 'native-project-markers.log').write_text('\n'.join(markers) + '\n', encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False))


if __name__ == '__main__':
    main()
