# SPDX-License-Identifier: Apache-2.0
"""Export the user-provided packed Blender props; run with Blender --background --python."""
import bpy, json
from pathlib import Path
root=Path(__file__).resolve().parents[3]
folder=root/'Assets/QwenWorld/Environment/SpawnLounge'
folder.mkdir(parents=True,exist_ok=True)
report={}
for filename,object_name in [('LightboxStand','LightboxStand'),('ParkBench','ParkBench')]:
    bpy.ops.wm.open_mainfile(filepath=str(Path(__file__).parent/(filename+'.blend')))
    obj=bpy.data.objects[object_name]
    obj.animation_data_clear()
    for modifier in list(obj.modifiers): obj.modifiers.remove(modifier)
    if filename=='LightboxStand':
        ad=bpy.data.materials.new('Advertisement'); obj.data.materials.append(ad)
        faces=[p for p in obj.data.polygons if abs(p.normal.y)>.99 and p.area>.8]
        if len(faces)!=2: raise RuntimeError('Expected two display faces: '+str(len(faces)))
        panel=[]
        for face in faces:
            verts=[obj.data.vertices[i].co for i in face.vertices]
            xmin,xmax=min(v.x for v in verts),max(v.x for v in verts)
            zmin,zmax=min(v.z for v in verts),max(v.z for v in verts)
            face.material_index=len(obj.data.materials)-1
            for loop in face.loop_indices:
                v=obj.data.vertices[obj.data.loops[loop].vertex_index].co
                u=(v.x-xmin)/(xmax-xmin)
                if face.normal.y>0: u=1-u
                obj.data.uv_layers.active.data[loop].uv=(u,(v.z-zmin)/(zmax-zmin))
            panel.append({'normal':list(face.normal),'y':float(verts[0].y),'x':[xmin,xmax],'height':[zmin,zmax]})
        report['displayFaces']=panel
    for image in bpy.data.images:
        if image.packed_file and image.size[0]>0:
            target=folder/(image.name if image.name.endswith('.png') else image.name+'.png')
            image.filepath_raw=str(target); image.file_format='PNG'; image.save()
    bpy.ops.object.select_all(action='DESELECT'); obj.hide_set(False); obj.select_set(True); bpy.context.view_layer.objects.active=obj
    bpy.ops.export_scene.fbx(filepath=str(folder/(filename+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,use_mesh_modifiers=True,mesh_smooth_type='OFF',add_leaf_bones=False,bake_anim=False,path_mode='STRIP',embed_textures=False)
    obj.data.calc_loop_triangles(); report[filename]={'triangles':len(obj.data.loop_triangles),'dimensions':list(obj.dimensions)}
(folder/'ModelSources.json').write_text(json.dumps({'source':'User-provided Blender props; packed originals kept in Tools/Blender/SpawnLounge.','exports':report},indent=2)+'\n',encoding='utf-8')
print('LOUNGE_EXPORT_FINISHED',json.dumps(report))
