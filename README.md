# How Qwen Work

[中文说明](README.zh-CN.md)

A VRChat world that runs the **Qwen3.5-0.8B text model on local GPU textures**
and visualizes its weights, reusable state buffers, and operator reads/writes.
The PC world performs inference. Android clients view shared chat and core status.

Original project code and owner-provided artwork: **Apache-2.0**.
Third-party components retain their own licenses; see [NOTICE](NOTICE) and
[third-party notices](THIRD_PARTY_NOTICES.md).

## What is in this repository

- One authored scene: `Assets/QwenWorld/Scenes/QwenLaboratory.unity`.
- Runtime Udon, shaders, scene installers, materials, geometry, baked lighting,
  font, sky composite, standee and orbital-ring assets.
- Vendored ShaderGPT Unity source at its pinned upstream commit.
- Scripts and locked sources/import metadata to restore model payloads.

Generated model PNG/EXR files and the generated tokenizer JSON are **not stored in
Git**. `Tools/Qwen35/model-payloads.lock.json` records their hashes and original
Unity import metadata/GUIDs. Local files are kept on the developer's machine.
The source also excludes Unity caches, SDK package copies, local preferences,
compiled Udon caches, test reports, generated platform scenes and world bundles.

## First checkout (Windows)

Requirements: Unity **2022.3.22f1**, VRChat Creator Companion, and Python **3.12**.
Install/restore **VRChat Worlds SDK 3.10.6** using Creator Companion; the version is
recorded in `Packages/vpm-manifest.json`. Unity Package Manager restores Unity packages.
Do not copy another person's `Library`, SDK login settings, or Python virtual environment.

Before opening the main scene in Unity, run from the project directory:

```powershell
./Tools/Qwen35/Initialize-Qwen.ps1
```

This creates an isolated Python environment, installs pinned dependencies,
downloads the pinned public Qwen checkpoint and ShaderGPT converter, quantizes
and exports the text weights, then restores all generated payloads **with their
original Unity GUIDs**. The default conversion uses CPU; `-TorchBackend cu128`
selects the documented CUDA wheel set. It does not retrain the model or run the
four-language smoke suite. Initial downloads/conversion can take substantial time.

Close Unity before command-line preparation. Then initialize the imported checkout:

```powershell
./Tools/Qwen35/Invoke-Qwen.ps1 Setup
```

Alternatively use **Qwen World > Setup > Initialize source checkout** inside Unity.
This compiles Udon and checks model/scene references; it does not recreate the
scene, bake lighting, run inference or upload anything.

If you already have an export from the locked toolchain:

```powershell
./Tools/Qwen35/Initialize-Qwen.ps1 -FromExport 'E:/QwenExport/Qwen35-Full-Q4'
```

The restore script verifies every payload checksum before changing the checkout.
It restores `.meta` contents from the committed recipe, so an early Unity import
cannot permanently lose the original model GUIDs. Hash mismatches stop restoration;
use the pinned source/tool versions rather than silently replacing model data.

## Local paths

The work-directory priority is: command-line `-DataRoot`, `QWEN_DATA_ROOT`, ignored
`.qwen-local.json`, then `<project>/.qwen-work`. Python and editor code use the same
configuration. The project has **no required drive letter or user directory**.

Copy `qwen-local.example.json` to `.qwen-local.json` if needed. `unityPath` is optional;
commands also accept `-UnityPath` / `QWEN_UNITY_PATH` and try the installed Hub editor.
The work directory holds raw models, the Python environment, converter cache,
exports, logs, local validation reports and build results. It is not a source dependency.

## Use the world

Approach the central table to obtain the free keyboard. A nearby holder retains
control until leaving the range; input then passes automatically to another nearby PC player.
Tab or a double trigger opens the personal menu. Select Chinese, English, Japanese
or Korean locally; the question-mark button explains controls and display conventions.
Language preference lasts for the current session. Chat remains in its original language.

Requests contain user/assistant history, the current question and the model's
assistant generation prefix. There is **no system message or reply-language instruction**.

Texture visibility, chat, inference progress, core highlight and wish events are shared.
Actual CRT values and read/write links remain local to the executing client.
The personal observation deck is independently adjustable from 0 to 5.5 metres;
it hides at its lowest setting and resets after falling.

## Build and optional checks

```powershell
./Tools/Qwen35/Invoke-Qwen.ps1 BuildPC
./Tools/Qwen35/Invoke-Qwen.ps1 BuildAndroid
./Tools/Qwen35/Invoke-Qwen.ps1 BuildAll
```

These produce **local bundles only**. BuildAll builds both targets and restores
Win64/main-scene settings; it does not require historical test archives, Python,
old reports, or a retired compact model. Outputs go under the configured work directory.
`Summary` optionally collects whatever local reports exist; missing reports are not failures.

Use scoped checks for changes instead of rebuilding/testing the entire project:

| Command | Purpose |
|---|---|
| Compile | Compile Udon only |
| Localization / Console / Observation / Motion | Focused UI, geometry or motion checks |
| SourceCheck / SourcePackage | Audit public source; optionally create a source ZIP, never upload |
| Reference | Generate four-language reference sequences for the current no-system template |
| Smoke / Validate / NativeTest | Optional inference/runtime checks; run Reference first |
| Export / Bake / Rebuild | Advanced model/graph or complete scene regeneration; not required for a normal checkout |

The native-client regression reads generated references rather than hardcoded
sequences from the former system-message template. Reference generation is optional
and separate from preparing assets or building the world.

### Updating an existing published world

The committed scene has **no published-world binding**. Maintainers may keep a
`worldBlueprintId` in ignored `.qwen-local.json`. **Qwen World > Build > Open local
publish copy** creates an ignored, platform-appropriate copy with that binding
and matching baked-lighting metadata. Continue through the SDK panel yourself.
Do not edit the generated copy as the authored scene. No script in this repository
uploads a world or requires another person's SDK credentials.

## Source release

```powershell
./Tools/Qwen35/Invoke-Qwen.ps1 SourceCheck
./Tools/Qwen35/Invoke-Qwen.ps1 SourcePackage
```

The source ZIP excludes SDK packages, generated model payloads/import files,
compiled Udon, local configuration and reports. The import recipe stays in `Tools`.
A candidate-source scan checks file size, personal-path patterns, likely tokens and
published-world bindings; it is not a guarantee that arbitrary future additions contain no secrets.

See [implementation](Docs/Qwen35-Implementation.md),
[scene/interaction specification](Docs/Qwen35-Central-Exhibit-Design.zh-CN.md) and
[sky provenance](Docs/Qwen35-Orbital-Sky.md) for technical details.
