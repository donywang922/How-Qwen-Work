"""Download only the pinned public Qwen checkpoint, with bounded-memory progress."""
from __future__ import annotations
import argparse
import hashlib
import json
import time
import urllib.request
from pathlib import Path
from qwen_paths import DATA_ROOT

REPO = "Qwen/Qwen3.5-0.8B"
REVISION = "2fc06364715b967f1860aea9cf38778875588b17"
FILES = ["config.json", "tokenizer.json", "tokenizer_config.json", "chat_template.jinja",
         "vocab.json", "merges.txt", "model.safetensors.index.json", "LICENSE",
         "model.safetensors-00001-of-00001.safetensors"]

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=(DATA_ROOT / 'models/Qwen3.5-0.8B'))
    parser.add_argument("--offline", action="store_true")
    args = parser.parse_args()
    locked = json.loads((Path(__file__).parent / "sources.lock.json").read_text(encoding="utf-8"))["model"]
    assert locked["repository"] == REPO and locked["revision"] == REVISION
    expected = {entry["name"]: entry["sha256"] for entry in locked["files"]}
    args.output.mkdir(parents=True, exist_ok=True)
    manifest = {"repository": REPO, "revision": REVISION, "files": []}
    for filename in FILES:
        destination = args.output / filename
        url = f"https://huggingface.co/{REPO}/resolve/{REVISION}/{filename}"
        complete = destination.exists() and destination.stat().st_size > 0
        if not complete:
            if args.offline:
                raise FileNotFoundError("Model file is not cached: " + filename)
            part = destination.with_name(destination.name + ".part")
            for attempt in range(5):
                offset = part.stat().st_size if part.exists() else 0
                request = urllib.request.Request(url, headers={"Range": f"bytes={offset}-"} if offset else {})
                try:
                    with urllib.request.urlopen(request, timeout=90) as response:
                        resumed = offset and response.status == 206
                        if not resumed:
                            offset = 0
                        total = int(response.headers.get("Content-Length", 0)) + offset
                        copied, last = offset, time.monotonic()
                        with part.open("ab" if resumed else "wb") as output:
                            while block := response.read(4 * 1024 * 1024):
                                output.write(block)
                                copied += len(block)
                                if time.monotonic() - last > 10:
                                    print(f"{filename}: {copied / 1e6:.1f}/{total / 1e6:.1f} MB", flush=True)
                                    last = time.monotonic()
                        if total and copied != total:
                            raise IOError(f"Truncated download: {copied} != {total}")
                    part.replace(destination)
                    break
                except Exception as error:
                    print(f"Retry {attempt + 1}: {filename}: {type(error).__name__}", flush=True)
                    if attempt == 4:
                        raise
                    time.sleep(2)
        with destination.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
        if digest != expected[filename]:
            raise ValueError("Model checksum mismatch: " + filename)
        manifest["files"].append({"name": filename, "bytes": destination.stat().st_size, "sha256": digest})
        print(f"Ready: {filename} ({destination.stat().st_size / 1e6:.1f} MB)", flush=True)
    (args.output / "download-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

if __name__ == "__main__":
    main()
