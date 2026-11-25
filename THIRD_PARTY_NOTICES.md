# Third-party sources and asset licensing

The root Apache-2.0 license covers this project's original code, documentation,
procedural scene assets, and its owner-provided orbital-ring artwork.
It does not replace the separate licenses/terms below.

| Component | Included or restored | Source / terms |
|---|---|---|
| ShaderGPT | Unity source is vendored with original GUIDs; Python exporter is fetched at a pinned commit | [Upstream](https://gitlab.com/lox9973/ShaderGPT), MIT; see `Assets/ThirdParty/ShaderGPT/LICENSE.txt` and `UPSTREAM.md` |
| Qwen3.5-0.8B | Original checkpoint is downloaded; generated PNG/EXR/tokenizer payloads are excluded from Git | [Model](https://huggingface.co/Qwen/Qwen3.5-0.8B), Apache-2.0; see `Assets/QwenWorld/Licenses/Qwen-Apache-2.0.txt` |
| Qwen standee and edited expressions | AI-generated illustration by ZipZipPipe; project edits: background extraction and closed-eye expression | [Pixiv artwork 148186519](https://www.pixiv.net/artworks/148186519); custom terms: attribution required, noncommercial use only. See [full record](Docs/QwenGirl-Usage-Terms.md). Not Apache-2.0. |
| Noto Sans CJK | Existing font included and reused for all four interface languages | SIL OFL 1.1; see `Assets/QwenWorld/Fonts/OFL.txt` |
| VRChat SDK / UdonSharp / ClientSim | Restore SDK 3.10.6 through VRChat Creator Companion; SDK package directories are excluded | [SDK documentation](https://creators.vrchat.com/sdk/), upstream package licenses/terms apply |
| QvPen 3.3.15 / ureishi | VPM dependency; used intact for the lightbox side pen rack | [Upstream](https://github.com/ureishi/QvPen); upstream terms apply, not the project's Apache-2.0 grant |
| Unity packages | Package manifests/lock retained; Package Manager restores dependencies | Their respective package licenses, not the root project license |

## Sky imagery

The shipped sky is an offline artistic composite, not a scientific reconstruction
of a particular orbit or time. It is not NASA-produced or agency-endorsed.

- Stars: [Deep Star Maps 2020](https://svs.gsfc.nasa.gov/4851/), NASA/Goddard Space Flight Center Scientific Visualization Studio; Gaia DR2, ESA/Gaia/DPAC; Ernie Wright.
- Earth: [Blue Marble Next Generation](https://science.nasa.gov/earth/earth-observatory/blue-marble-next-generation/base-map/), NASA Earth Observatory; Reto Stoeckli, NASA/GSFC; MODIS/Terra and Aqua.
- Night lights: [Black Marble](https://science.nasa.gov/earth/earth-observatory/earth-at-night/maps/), NASA Earth Observatory; Suomi NPP/VIIRS, 2016.
- Consult [NASA media guidelines](https://www.nasa.gov/nasa-brand-center/images-and-media/) and the source pages for agency/third-party conditions. The root Apache license does not relicense agency imagery.

`fetch_sky_sources.py` records source URLs, credits and hashes when fetching originals.
The final composite already lives in the Unity project; rebuilding it is optional.

## Project artwork and marks

The project owner authorized Apache-2.0 for the provided Blender orbital-ring asset.
The AI-generated Qwen illustration is by [ZipZipPipe](https://www.pixiv.net/users/18604994), from [AI娘化](https://www.pixiv.net/artworks/148186519), with custom attribution and noncommercial terms.
Background extraction and expression editing were performed for this project; edited textures retain the source conditions.
The collection description explicitly assigns CC BY-NC-SA 4.0 to a DeepSeek design; this is not assigned to the Qwen illustration.
See `Docs/QwenGirl-Usage-Terms.md` and `Docs/QwenGirl-Asset-Source.json`; the ring export report is included beside its FBX.
The reference Qwen logo and its floor motif retain the upstream mark's identity;
no ownership of the Qwen trademark or affiliation with Qwen is claimed.
