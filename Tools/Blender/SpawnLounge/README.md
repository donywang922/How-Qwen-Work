# Spawn lounge props

Packed Blender originals supplied by the project owner: `LightboxStand.blend` and `ParkBench.blend`.
The scratch copies are not a project dependency. Unity consumes the exported FBX files and textures.

Run with Blender 5.3 (from the repository root):

```powershell
blender --background --factory-startup --python-exit-code 1 --python Tools/Blender/SpawnLounge/export_assets.py
```

The exporter keeps only each named prop, removes the lightbox's Blender UV-warp animation,
assigns a separate advertisement material to its two display faces and preserves the frame atlas UVs.
The Unity shader supplies the three-page scroll. Studio floor/camera/lights are excluded from FBX.
Furniture geometry: lightbox 318 triangles, bench 2,508 triangles.

See `Docs/Qwen35-Spawn-Lounge.md` for the Unity installer and advertisement regeneration.
