# SPDX-License-Identifier: Apache-2.0
"""Run in background Blender with the distant .blend open; writes an FBX only."""
import argparse
import sys
from pathlib import Path

import bpy

parser = argparse.ArgumentParser()
parser.add_argument('--output',type=Path,default=Path(__file__).resolve().parents[3] / '.qwen-work/blender/exports/Orbital_Ring_20K_Distant.fbx')
args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
meshes = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
if len(meshes) != 1:
    raise RuntimeError('Open the single-mesh distant source, not the high-detail source.')
obj = meshes[0]
obj.data.calc_loop_triangles()
if len(obj.data.loop_triangles) != 19954:
    raise RuntimeError('Expected the approved 19,954-triangle distant mesh.')
args.output.parent.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='DESELECT')
obj.hide_set(False)
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.export_scene.fbx(filepath=str(args.output.resolve()),use_selection=True,object_types={'MESH'},
    axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,
    use_mesh_modifiers=True,mesh_smooth_type='OFF',add_leaf_bones=False,bake_anim=False,
    path_mode='AUTO',embed_textures=False)
print('QWEN_ORBITAL_FBX_EXPORTED',args.output)
