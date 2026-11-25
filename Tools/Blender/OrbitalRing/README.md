# Orbital-ring Blender sources

These are copies of the original project-owned Blender sources, licensed under
Apache-2.0. The original authoring files outside this repository were not changed.

| File | Purpose | Triangles |
|---|---|---:|
| `Orbital_Ring_Source.blend` | Full-detail editable source, including material and presentation rig | 474,944 |
| `Orbital_Ring_20K_Distant.blend` | Approved distant mesh used to produce the Unity FBX | 19,954 |

Both files contain **five packed texture images**, one mesh, one material, and
no linked external blend libraries. Image paths are project-relative; packed
images allow either blend to be opened without downloading separate textures.
Geometry is unchanged from the original files. See `source-files.json`.

The files are outside `Assets`: Blender is only needed to edit/re-export the ring,
not to open/build the Unity world. The Unity FBX and five texture images remain in
`Assets/QwenWorld/Environment/ImportedOrbitalRing`.

## Rebuild the distant geometry

Verified source-file handling uses Blender 5.3. Run from the repository root:

```text
blender --background --factory-startup Tools/Blender/OrbitalRing/Orbital_Ring_Source.blend --python-exit-code 1 --python Tools/Blender/OrbitalRing/build_distant.py
```

`geometry.py` contains the editable component layout. `build_distant.py` supplies
the low-sided rails/two-sided details and enforces the original component-count
and 20K triangle budget. It uses the packed source material. Outputs go under
`.qwen-work/blender/orbital_ring_20k_distant` by default. Use arguments after `--`
to set `--output <directory>` or request optional `--render` previews. Default
execution does not render images. The build script was ported without rerunning
its full geometry/re-render process during source packaging.

## Export an FBX for Unity

```text
blender --background --factory-startup Tools/Blender/OrbitalRing/Orbital_Ring_20K_Distant.blend --python-exit-code 1 --python Tools/Blender/OrbitalRing/export_for_unity.py
```

The FBX goes under `.qwen-work/blender/exports`. This exporter selects only the
approved mesh and does not export the camera/light rig. To adopt an edited mesh,
replace the existing Unity FBX while retaining its `.meta`, then refresh the ring
through the project's editor tooling. No script overwrites the Unity FBX automatically.
Unity handles ring placement, Earth-facing rotation and outer-edge scale; these
world-space transforms are not baked into the authoring blends.
