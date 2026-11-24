"""Text-only Qwen3.5 export, symmetric group-32 Q4, optional corpus-based vocabulary trimming.

All model downloads are handled separately. This script runs against local files.
The reference uses the same quantized scalar weights as the shader export.
"""
from __future__ import annotations
import argparse
import gc
import json
import math
import os
from pathlib import Path
import re
import sys
import time
from qwen_paths import DATA_ROOT, MODEL_ROOT, shadergpt_vendor

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

os.environ.setdefault("OPENCV_IO_ENABLE_OPENEXR", "1")
os.environ.setdefault("HF_HUB_OFFLINE", "1")
os.environ.setdefault("TOKENIZERS_PARALLELISM", "false")

import numpy as np
import torch
from safetensors import safe_open
from transformers import AutoTokenizer, Qwen3_5ForCausalLM, Qwen3_5TextConfig

LANGUAGES = ("zh", "en", "ja", "ko")
SMOKE = [
    ("zh", "请用中文简短回答：水通常在多少摄氏度结冰？"),
    ("en", "Answer briefly in English: what is two plus three?"),
    ("ja", "日本語で短く答えてください。空は何色ですか？"),
    ("ko", "한국어로 짧게 답하세요. 물은 보통 몇 도에서 얼어요?"),
]

def write_json(path, obj):
    path.write_text(json.dumps(obj, ensure_ascii=False, indent=2, allow_nan=False) + "\n", encoding="utf-8")

def load_exporter(vendor):
    directory = vendor / "Python~"
    filename = directory / "convert_hf_model.py"
    code = filename.read_text(encoding="utf-8")
    old_header = "mmproj_file=None, force_quant:Callable|None=None):"
    new_header = "mmproj_file=None, force_quant:Callable|None=None, quant_factory=None):"
    old_factory = "qdict[data] = CustomQuantTensor(pdata)"
    new_factory = "qdict[data] = (quant_factory or CustomQuantTensor)(pdata)"
    if old_header in code:
        assert old_factory in code
        code = code.replace(old_header, new_header, 1).replace(old_factory, new_factory, 1)
        filename.write_text(code, encoding="utf-8")
    else:
        assert new_header in code and new_factory in code, "Unexpected exporter revision"
    sys.path.insert(0, str(directory))
    import convert_hf_model as exporter
    return exporter

def make_quant_class(exporter):
    from quantization import QTensor
    class Group32Q4(QTensor):
        """4-bit symmetric weights with FP32 scale metadata in ShaderGPT's QS format."""
        bits = 4
        def __init__(self, data):
            self.data = data
        @staticmethod
        def quantize(data):
            shape = data.shape
            if shape[-1] % 32:
                raise ValueError(f"Group-32 Q4 cannot quantize shape {shape}")
            group = data.float().reshape(shape[0], -1, 32)
            scale = group.abs().amax(-1, keepdim=True).clamp_min(1e-20) / 7
            # ShaderGPT stores 11 mantissa bits. Round the reference to the same scale first.
            scale = ((scale.contiguous().view(torch.int32) >> 12) << 12).view(torch.float32)
            q = (group / scale).round().clamp(-8, 7)
            return q, scale
        def dequantize(self):
            data = self._permute(self.data)
            q, scale = self.quantize(data)
            return (q * scale).reshape_as(data)
        def export(self):
            data = self._permute(self.data)
            q, scale = self.quantize(data)
            q = (q + 8).reshape(data.shape).cpu().numpy().astype(np.uint8)
            scale = scale.squeeze(-1).cpu().numpy().astype(np.float32)
            zero = np.full_like(scale, 8)
            return self._export_QSZ(q, scale, zero, 4)
    return Group32Q4

def load_text_model(folder, context):
    config = json.loads((folder / "config.json").read_text())
    text_config = Qwen3_5TextConfig(**config["text_config"])
    text_config.max_position_embeddings = context
    text_config._attn_implementation = "eager"
    # Nonpersistent rotary buffers must be initialized on a real device as well.
    # Loading checkpoint Parameters with assign=True then replaces random weights.
    model = Qwen3_5ForCausalLM(text_config)
    files = sorted(folder.glob("*.safetensors"))
    weights = {}
    for filename in files:
        with safe_open(filename, framework="pt", device="cpu") as source:
            for key in source.keys():
                prefix = "model.language_model."
                if key.startswith(prefix):
                    weights["model." + key[len(prefix):]] = source.get_tensor(key)
    if not weights:
        raise RuntimeError("No text tensors found in checkpoint")
    weights["lm_head.weight"] = weights["model.embed_tokens.weight"]
    model.load_state_dict(weights, strict=True, assign=True)
    model.tie_weights()
    del weights
    return model.float().eval()

@torch.inference_mode()
def collect_reference(model, tokenizer, output, smoke, max_new, device):
    model.to(device)
    model.config._attn_implementation = "eager"
    torch.backends.cuda.matmul.allow_tf32 = False
    ids = tokenizer.encode("Hello, world!", add_special_tokens=False)
    # Sequential reference is the actual baked single-token inference path.
    cache, hidden = None, []
    for position, token in enumerate(ids):
        result = model(input_ids=torch.tensor([[token]], device=device), past_key_values=cache,
            use_cache=True, output_hidden_states=True)
        cache = result.past_key_values
        hidden.extend(result.hidden_states[-1][0, -1].float().cpu().tolist())
    logits = result.logits[0, -1].float().cpu()
    top = torch.topk(logits, 8)
    write_json(output / "testcase.json", {"input_ids": ids, "hidden_states": hidden,
        "logits": logits.tolist()})
    write_json(output / "reference.json", {"input_ids": ids, "expected_next_token": top.indices[0].item(),
        "top_ids": top.indices.tolist(), "top_logits": top.values.tolist(),
        "reference_dtype": "float32", "cache_path": "sequential"})
    # Also compare prefill to recurrence; keep this separate from quantization quality.
    batch = model(input_ids=torch.tensor([ids], device=device), use_cache=False)
    error = float((batch.logits[0, -1].float().cpu() - logits).abs().max())
    results = {"prefill_vs_step_max_logit_error": error, "answers": []}
    if smoke:
        for lang, question in SMOKE:
            prompt = tokenizer.apply_chat_template([{"role": "user", "content": question}],
                tokenize=False, add_generation_prompt=True, enable_thinking=False)
            inputs = tokenizer(prompt, return_tensors="pt").to(device)
            start = time.monotonic()
            generated = model.generate(**inputs, max_new_tokens=max_new, do_sample=False,
                pad_token_id=tokenizer.pad_token_id, eos_token_id=tokenizer.eos_token_id)
            text = tokenizer.decode(generated[0, inputs.input_ids.shape[1]:], skip_special_tokens=True)
            results["answers"].append({"lang": lang, "prompt": question, "answer": text,
                "generated_ids": generated[0, inputs.input_ids.shape[1]:].cpu().tolist(),
                "seconds": time.monotonic() - start})
            print(f"SMOKE {lang}: {text}", flush=True)
    write_json(output / "smoke-results.json", results)
    model.cpu()
    torch.cuda.empty_cache()

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", type=Path, default=MODEL_ROOT)
    parser.add_argument("--vendor", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--precision", choices=("fp32", "q4"), default="q4")
    parser.add_argument("--context", type=int, default=512)
    parser.add_argument("--smoke", action="store_true")
    parser.add_argument("--max-new", type=int, default=32)
    parser.add_argument("--device", default="cuda")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    vendor = args.vendor or shadergpt_vendor()
    exporter = load_exporter(vendor)
    tokenizer = AutoTokenizer.from_pretrained(args.model, local_files_only=True)
    model = load_text_model(args.model, args.context)
    quant_class = make_quant_class(exporter)
    quantized = []
    if args.precision == "q4":
        with torch.no_grad():
            for name, parameter in model.named_parameters():
                if parameter.ndim == 2 and parameter.numel() >= 65536 and parameter.shape[-1] % 32 == 0:
                    q = quant_class(parameter)
                    parameter.copy_(q.dequantize())
                    quantized.append(name)
        print(f"Quantized {len(quantized)} matrices to group-32 Q4", flush=True)
    collect_reference(model, tokenizer, args.output, args.smoke, args.max_new, args.device)
    from convert_hf_tokenizer import export_tokenizer
    export_tokenizer(tokenizer, args.output)
    exporter.export_model(model, args.output, force_write=True,
        force_quant=(lambda name, shape: len(shape) == 2 and math.prod(shape) >= 65536 and shape[-1] % 32 == 0)
            if args.precision == "q4" else None,
        quant_factory=quant_class)
    manifest = {"source": "Qwen/Qwen3.5-0.8B", "revision": "2fc06364715b967f1860aea9cf38778875588b17",
        "text_only": True, "precision": args.precision, "context": args.context,
        "parameters": sum(p.numel() for p in model.parameters()), "vocab_size": model.config.vocab_size,
        "embedding_head_shared": model.lm_head.weight is model.model.embed_tokens.weight,
        "quantized_matrices": quantized, "trimming": None,
        "quality_note": "Smoke tests are functional checks, not multilingual quality certification.",
        "files": [{"path": str(p.relative_to(args.output)), "bytes": p.stat().st_size}
            for p in sorted(args.output.rglob("*")) if p.is_file() and p.name != "export-manifest.json"]}
    manifest["total_export_bytes"] = sum(row["bytes"] for row in manifest["files"])
    write_json(args.output / "export-manifest.json", manifest)
    print(f"EXPORTED {args.output}: {manifest['total_export_bytes'] / 1e6:.1f} MB", flush=True)

if __name__ == "__main__":
    main()
