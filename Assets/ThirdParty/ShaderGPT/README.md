# ShaderGPT

Run small language models directly in Unity, using only pixel shaders—no compute shaders required! ShaderGPT is designed for creators, hobbyists, and developers who want to experiment with AI models in real-time, with minimal setup.

## What is ShaderGPT?

ShaderGPT lets you run popular transformer models (like Llama family) and state-space models (like Mamba family) inside Unity, using your GPU for fast inference. It's built for compatibility with UGC platforms that only allow pixel shaders, so everything works with standard graphics hardware.

**Tested on Unity 2022.3+ and 6000.0+ with D3D11/12, Vulkan, and GLES3.**

**vs. [Unity Sentis](https://docs.unity3d.com/Packages/com.unity.ai.inference@latest)**: ShaderGPT is a tensor library with both PyTorch-like tensor operators and transformers-like model implementations. Sentis is an ONNX inference engine with multiple backends (compute/pixel/CPU). Choose ShaderGPT for learning transformers, pixel-shader-only environments and direct model control, or Sentis for ONNX models and broader backend support.

## Features

- **Easy Import**: Convert Hugging Face models with a simple Python script, then import them into Unity with one click.
- **Model Support**: Run a wide range of transformer models, including:
  [FalconH1](https://huggingface.co/docs/transformers/main/en/model_doc/falcon_h1),
  [Gemma](https://huggingface.co/docs/transformers/main/en/model_doc/gemma),
  [Gemma3](https://huggingface.co/docs/transformers/main/en/model_doc/gemma3),
  [GPT2](https://huggingface.co/docs/transformers/main/en/model_doc/gpt2),
  [GPT-Neo](https://huggingface.co/docs/transformers/main/en/model_doc/gpt_neo),
  [GPT-NeoX](https://huggingface.co/docs/transformers/main/en/model_doc/gpt_neox),
  [GraniteMoeHybrid](https://huggingface.co/docs/transformers/main/en/model_doc/granitemoehybrid),
  [LFM2](https://huggingface.co/docs/transformers/main/en/model_doc/lfm2),
  [LFM2-VL](https://huggingface.co/docs/transformers/main/en/model_doc/lfm2_vl),
  [Llama2](https://huggingface.co/docs/transformers/main/en/model_doc/llama2),
  [Llama3](https://huggingface.co/docs/transformers/main/en/model_doc/llama3),
  [Mamba](https://huggingface.co/docs/transformers/main/en/model_doc/mamba),
  [Mamba2](https://huggingface.co/docs/transformers/main/en/model_doc/mamba2),
  [ModernBERT](https://huggingface.co/docs/transformers/main/en/model_doc/modernbert),
  [Moonshine](https://huggingface.co/docs/transformers/main/en/model_doc/moonshine),
  [Phi](https://huggingface.co/docs/transformers/main/en/model_doc/phi),
  [Phi3](https://huggingface.co/docs/transformers/main/en/model_doc/phi3),
  [Qwen2](https://huggingface.co/docs/transformers/main/en/model_doc/qwen2),
  [Qwen3](https://huggingface.co/docs/transformers/main/en/model_doc/qwen3),
  [Qwen3.5](https://huggingface.co/docs/transformers/main/en/model_doc/qwen3_5),
  [SmolLM3](https://huggingface.co/docs/transformers/main/en/model_doc/smollm3),
  [SmolVLM](https://huggingface.co/docs/transformers/main/en/model_doc/smolvlm),
  [T5](https://huggingface.co/docs/transformers/main/en/model_doc/t5),
  [FLAN-T5](https://huggingface.co/docs/transformers/main/en/model_doc/flan-t5),
  [VITS](https://huggingface.co/docs/transformers/main/en/model_doc/vits),
  [Whisper](https://huggingface.co/docs/transformers/main/en/model_doc/whisper).
- **Quantization**: Supports 4~8-bit GPTQ and GGUF models for faster, smaller deployments.
- **Editor Tools**: Import, test, and bake models for production—all from the Unity Editor.
- **Example Scenes**: Ready-to-run demos for both runtime and pre-baked inference.
- **Platform Compatible**: Works in restricted environments where only pixel shaders are allowed.

## Performance

ShaderGPT is well-optimized for pixel shaders and delivers competitive performance. Here's a comparison with [llama.cpp](https://github.com/ggml-org/llama.cpp) on **RTX 4080 Laptop 12GB** (base clock):

| Platform  | Model                         | Eval count   | Eval rate    | VRAM |
|-----------|-------------------------------|--------------|--------------|------|
| ShaderGPT | LFM2.5-1.2B-Instruct-QAD-Q4_0 |  1000 tokens | 217 tokens/s | 1.2GB|
| ShaderGPT | LFM2.5-1.2B-Instruct-QAD-Q4_0 | 10000 tokens | 199 tokens/s | 1.2GB|
| llama.cpp | LFM2.5-1.2B-Instruct-QAD-Q4_0 |  1000 tokens | 343 tokens/s | 0.8GB|
| llama.cpp | LFM2.5-1.2B-Instruct-QAD-Q4_0 | 10000 tokens | 319 tokens/s | 0.9GB|

Despite using only pixel shaders (no compute), ShaderGPT achieves ~60% of llama.cpp's speed—impressive considering llama.cpp uses highly optimized CUDA kernels unavailable in Unity.

## Quick Start

### Convert a Model

- Open a terminal and run:
   ```bash
   cd Python~
   pip install -r requirements.txt
   python convert_hf_model.py <huggingface_model> <output_directory>
   ```
- Example:
   ```bash
   python convert_hf_model.py roneneldan/TinyStories-33M ../Example/TinyStories-33M
   ```
- This downloads the model and prepares it for Unity.

### Import into Unity

- In Unity, select the converted model folder.
- Right-click and choose `ShaderGPT/ImportModel`.
- The editor will set up everything for you: textures, components, and materials.

### Test and Bake

- Set your model’s `Task` to `Test` for validation, or `Bake` to create optimized assets for deployment.

### Try the Example Scenes

- Open `Example/Main.unity` for a live demo.
- Open `Example/Udon.unity` for a pre-baked example (compatible with VRChat/Udon).

## Why Use ShaderGPT?

- **No compute shaders needed**—just pixel shaders, so it works everywhere Unity does.
- **Fast and lightweight**—optimized for small models and real-time use.
- **Open and hackable**—all code is C#, Python, and HLSL, so you can customize anything.