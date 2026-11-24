"""Reference cases for the exact world chat template, without a system message."""
import argparse
import json
from pathlib import Path
import torch
from transformers import AutoTokenizer
from qwen_paths import DATA_ROOT, MODEL_ROOT, shadergpt_vendor
from prepare_model import SMOKE, load_text_model, load_exporter, make_quant_class, write_json

@torch.inference_mode()
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--device", choices=("cpu","cuda"), default="cpu")
    args = parser.parse_args()
    folder = MODEL_ROOT
    tokenizer = AutoTokenizer.from_pretrained(folder, local_files_only=True)
    model = load_text_model(folder, 512)
    exporter = load_exporter(shadergpt_vendor())
    quant = make_quant_class(exporter)
    for parameter in model.parameters():
        if parameter.ndim == 2 and parameter.numel() >= 65536 and parameter.shape[-1] % 32 == 0:
            parameter.copy_(quant(parameter).dequantize())
    model.to(args.device)
    torch.backends.cuda.matmul.allow_tf32 = False
    cases = []
    for lang, question in SMOKE:
        prompt = '<|im_start|>user\n' + question + '<|im_end|>\n<|im_start|>assistant\n<think>\n\n</think>\n\n'
        inputs = tokenizer(prompt, return_tensors='pt').to(args.device)
        output = model.generate(**inputs, max_new_tokens=24, do_sample=False,
            pad_token_id=tokenizer.pad_token_id, eos_token_id=tokenizer.eos_token_id)
        generated = output[0, inputs.input_ids.shape[1]:].tolist()
        cases.append(dict(lang=lang, question=question, input_ids=inputs.input_ids[0].tolist(),
            generated_ids=generated, answer=tokenizer.decode(generated, skip_special_tokens=True)))
    name = 'world-reference-full.json'
    write_json(DATA_ROOT / 'validation' / name, {'template': 'user-assistant-no-system-v1', 'cases': cases})
    print(json.dumps(cases, ensure_ascii=False), flush=True)

if __name__ == '__main__':
    main()
