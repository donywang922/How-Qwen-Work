# ShaderGPT provenance

Upstream: https://gitlab.com/lox9973/ShaderGPT

Pinned commit: `99bf46ed6ccdb4ada05526ea115a315b56e00f88`.

The Script, Shader, Editor and Udon directories are vendored with their original
Unity GUIDs. LICENSE.txt is the upstream license. Python conversion tools are kept
outside Assets, under the configured work directory/vendor. Initialize-Qwen.ps1
fetches and verifies the pinned upstream archive. The preparation script applies
a small quant_factory hook to the local Python converter; the original archive
is not modified.

Project changes are documented in Docs/Qwen35-Implementation.md. The upstream
examples and pretrained TinyStories assets are not included in the world.
