# Third-party sources and asset licensing

The root Apache-2.0 license covers this project's original code, documentation,
procedural scene assets, and its owner-provided standee and orbital-ring artwork.
It does not replace the separate licenses/terms below.

| Component | Included or restored | Source / terms |
|---|---|---|
| ShaderGPT | Unity source is vendored with original GUIDs; Python exporter is fetched at a pinned commit | [Upstream](https://gitlab.com/lox9973/ShaderGPT), MIT; see `Assets/ThirdParty/ShaderGPT/LICENSE.txt` and `UPSTREAM.md` |
| Qwen3.5-0.8B | Original checkpoint is downloaded; generated PNG/EXR/tokenizer payloads are excluded from Git | [Model](https://huggingface.co/Qwen/Qwen3.5-0.8B), Apache-2.0; see `Assets/QwenWorld/Licenses/Qwen-Apache-2.0.txt` |
| Noto Sans CJK | Existing font included and reused for all four interface languages | SIL OFL 1.1; see `Assets/QwenWorld/Fonts/OFL.txt` |
| VRChat SDK / UdonSharp / ClientSim | Restore SDK 3.10.6 through VRChat Creator Companion; SDK package directories are excluded | [SDK documentation](https://creators.vrchat.com/sdk/), upstream package licenses/terms apply |
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

The project owner authorized Apache-2.0 for the Qwen character standee and the
provided Blender orbital-ring asset. Standee provenance is recorded in
`Docs/QwenGirl-Asset-Source.json`; the ring's export report is included beside its FBX.
The reference Qwen logo and its floor motif retain the upstream mark's identity;
no ownership of the Qwen trademark or affiliation with Qwen is claimed.
