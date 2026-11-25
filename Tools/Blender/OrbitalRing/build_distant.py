# SPDX-License-Identifier: Apache-2.0
import bpy
import bmesh
import math
import random
import json
import traceback
from pathlib import Path
from mathutils import Vector

import argparse
import sys
parser=argparse.ArgumentParser(description="Rebuild the distant ring from the packed high-detail source blend")
parser.add_argument("--output",type=Path,default=Path(__file__).resolve().parents[3] / ".qwen-work/blender/orbital_ring_20k_distant")
parser.add_argument("--render",action="store_true")
args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
OUT = args.output.resolve()
OUT.mkdir(parents=True, exist_ok=True)
random.seed(34171)
TAU = math.tau
R = 12.0
N = 72
STEP = TAU/N
BAY = R*STEP


class Builder:
    def __init__(self):
        self.vertices=[]
        self.faces=[]
        self.uv=[]
        self.smooth=[]
        self.parts=0

    def vertex(self,p):
        self.vertices.append(tuple(p))
        return len(self.vertices)-1

    def face(self, ids, tile, coords=None, smooth=False):
        self.faces.append(tuple(ids))
        self.smooth.append(smooth)
        if coords is None:
            coords=[(0,0),(1,0),(1,1),(0,1)] if len(ids)==4 else [(.5+.46*math.cos(j*TAU/len(ids)),.5+.46*math.sin(j*TAU/len(ids))) for j in range(len(ids))]
        col,row=tile%4,tile//4
        self.uv.extend(((col+.025+.95*u)/4,(3-row+.025+.95*v)/4) for u,v in coords)

    def tube(self, a, b, radius, tile=4, sides=8):
        a,b=Vector(a),Vector(b)
        d=(b-a).normalized()
        ref=Vector((0,0,1)) if abs(d.z)<.9 else Vector((1,0,0))
        u=d.cross(ref).normalized()
        v=d.cross(u).normalized()
        rings=[]
        for center in (a,b):
            rings.append([self.vertex(center+radius*(u*math.cos(j*TAU/sides)+v*math.sin(j*TAU/sides))) for j in range(sides)])
        for j in range(sides):
            k=(j+1)%sides
            self.face((rings[0][j],rings[0][k],rings[1][k],rings[1][j]),tile,smooth=True)
        self.face(rings[0][::-1],tile)
        self.face(rings[1],tile)
        self.parts+=1

    def rail(self,radius,y,thickness,tile=4,start=0,end=TAU,count=512,sides=8,wave=0):
        closed=abs(end-start-TAU)<.0001
        rings=[]
        for i in range(count+(not closed)):
            theta=start+(end-start)*i/count
            rc=radius+wave*math.sin(theta*N/2)
            rings.append([self.vertex(point(theta,rc-R+thickness*math.cos(j*TAU/sides),y+thickness*math.sin(j*TAU/sides))) for j in range(sides)])
        for i in range(count):
            k=(i+1)%len(rings)
            for j in range(sides):
                j1=(j+1)%sides
                self.face((rings[i][j],rings[i][j1],rings[k][j1],rings[k][j]),tile,smooth=True)
        if not closed:
            self.face(rings[0][::-1],tile)
            self.face(rings[-1],tile)
        self.parts+=1

    def panel(self,theta,length,width,y,tile=0,r=0,depth=.055,clip=.08,profile=0):
        # The chamfered outline and bevel are actual geometry, not a shader trick.
        a,b=length/2,width/2
        c=min(clip,length*.2,width*.2)
        outline=[(-a+c,-b),(a-c*.55,-b),(a,-b+c),(a,b-c*.5),(a-c,b),(-a+c*.5,b),(-a,b-c),(-a,-b+c)]
        if profile:
            outline=[(s+v*.27, v*(.82+.18*(s/a)) if profile%2 else v*(.82-.18*(s/a))) for s,v in outline]
        layers=[]
        bevel=min(.012,depth*.24,min(a,b)*.12)
        for ypos,inset in ((y-depth/2,bevel),(y-depth/2+bevel,0),(y+depth/2-bevel,0),(y+depth/2,bevel)):
            scale=1-inset/max(.06,min(a,b))
            layers.append([self.vertex(point(theta+s*scale/R,r+v*scale,ypos)) for s,v in outline])
        capuv=[(s/length+.5,v/width+.5) for s,v in outline]
        self.face(layers[0][::-1],tile,[(1-u,v) for u,v in capuv][::-1])
        self.face(layers[-1],tile,capuv)
        for layer in range(3):
            for j in range(8):
                k=(j+1)%8
                self.face((layers[layer][j],layers[layer][k],layers[layer+1][k],layers[layer+1][j]),tile)
        self.parts+=1

    def band(self,theta,length,r,y,width,thickness,tile=4,segments=4):
        rows=[]
        for i in range(segments+1):
            t=theta+(i/segments-.5)*length/R
            rows.append([self.vertex(point(t,rv,yv)) for rv,yv in ((r-thickness/2,y-width/2),(r+thickness/2,y-width/2),(r+thickness/2,y+width/2),(r-thickness/2,y+width/2))])
        for i in range(segments):
            for j in range(4):
                k=(j+1)%4
                self.face((rows[i][j],rows[i+1][j],rows[i+1][k],rows[i][k]),tile,[(i/segments,0),((i+1)/segments,0),((i+1)/segments,1),(i/segments,1)])
        self.face(rows[0][::-1],tile)
        self.face(rows[-1],tile)
        self.parts+=1


def point(theta,r=0,y=0):
    return Vector(((R+r)*math.cos(theta),y,(R+r)*math.sin(theta)))


def bolt(m,theta,r,y,side=1):
    m.tube(point(theta,r,y),point(theta,r,y+side*.018),.024,3,6)
    m.panel(theta,.028,.006,y+side*.021,8,r,depth=.006,clip=.001)


# Every construction call is retained. Only exact coincident duplicate sleeves
# are deduplicated; no unique bolt, fin, pipe, connector, housing or lamp is culled.
original_band=Builder.band
rail_cache=set()
call_stats={}
duplicate_rails=0
def note(name):call_stats[name]=call_stats.get(name,0)+1

def distant_tube(self,a,b,radius,tile=4,sides=8):
    note('tube')
    a,b=Vector(a),Vector(b)
    axis=b-a
    d=axis.normalized()
    ref=Vector((0,0,1)) if abs(d.z)<.9 else Vector((1,0,0))
    u=d.cross(ref).normalized();v=d.cross(u).normalized()
    if axis.length<radius*1.25:
        # Thin head/washer: a small textured cap retains the visible part.
        center=b
        ids=[self.vertex(center+radius*(u*x+v*y)) for x,y in ((-1,-1),(1,-1),(1,1),(-1,1))]
        self.face(ids,tile)
    else:
        # Crossed strips retain the full pipe length and apparent width from
        # different distant viewing angles, at four triangles per pipe piece.
        for direction in (u,v):
            ids=[self.vertex(p) for p in (a-direction*radius,b-direction*radius,b+direction*radius,a+direction*radius)]
            self.face(ids,tile)
    self.parts+=1

def distant_rail(self,radius,y,thickness,tile=4,start=0,end=TAU,count=512,sides=8,wave=0):
    global duplicate_rails
    note('rail')
    key=tuple(round(v,7) for v in (radius,y,thickness,start,end,wave))+(tile,)
    if key in rail_cache:
        duplicate_rails+=1
        return
    rail_cache.add(key)
    closed=abs(end-start-TAU)<.0001
    if closed:
        segments=MAJOR_SEG if thickness>.035 else FAR_SEG
        sides=3
        # Keep every longitudinal rail with its original centerline/radius.
        rows=[]
        for i in range(segments):
            t=start+(end-start)*i/segments
            rc=radius+wave*math.sin(t*N/2)
            rows.append([self.vertex(point(t,rc-R+thickness*math.cos(j*TAU/sides),y+thickness*math.sin(j*TAU/sides))) for j in range(sides)])
        for i in range(segments):
            ii=(i+1)%segments
            for j in range(sides):
                jj=(j+1)%sides
                self.face((rows[i][j],rows[i][jj],rows[ii][jj],rows[ii][j]),tile,smooth=True)
    else:
        # A short sleeve or hydraulic segment is represented by two crossing
        # strips along the same curved centerline, not removed.
        segments=1 if (end-start)*radius<.4 else 2
        for orientation in ('RADIAL','AXIAL'):
            rows=[]
            for i in range(segments+1):
                t=start+(end-start)*i/segments
                rr=radius-R+wave*math.sin(t*N/2)
                if orientation=='RADIAL':
                    rows.append([self.vertex(point(t,rr-thickness,y)),self.vertex(point(t,rr+thickness,y))])
                else:
                    rows.append([self.vertex(point(t,rr,y-thickness)),self.vertex(point(t,rr,y+thickness))])
            for i in range(segments):self.face((rows[i][0],rows[i+1][0],rows[i+1][1],rows[i][1]),tile,smooth=True)
    self.parts+=1

def distant_panel(self,theta,length,width,y,tile=0,r=0,depth=.055,clip=.08,profile=0):
    note('panel')
    # Retain every fin, connector shoe, bezel and screw slot in its original
    # location. Existing front/rear pairs make these two-sided cards suitable
    # for the intended distant view without spending faces on their thickness.
    yy=y+(-1 if y<0 else 1)*depth/2
    xy=[(-length/2,-width/2),(length/2,-width/2),(length/2,width/2),(-length/2,width/2)]
    ids=[self.vertex(point(theta+s/R,r+rr,yy)) for s,rr in xy]
    uv=[(0,0),(1,0),(1,1),(0,1)]
    if y<0:ids=ids[::-1];uv=[(1-u,v) for u,v in uv][::-1]
    self.face(ids,tile,uv)
    self.parts+=1

def distant_band(self,theta,length,r,y,width,thickness,tile=4,segments=4):
    note('band')
    original_band(self,theta,length,r,y,width,thickness,tile,segments=1 if length<.6 else 2)

Builder.tube=distant_tube
Builder.rail=distant_rail
Builder.panel=distant_panel
Builder.band=distant_band


geometry_source=(Path(__file__).parent / "geometry.py").read_text(encoding="utf-8")
trials=[]
for FAR_SEG in (60,56,52,48,44,40,36,32,28,24):
    MAJOR_SEG=max(60,FAR_SEG+24)
    random.seed(34171)
    rail_cache.clear();call_stats.clear();duplicate_rails=0
    m=Builder()
    exec(geometry_source)
    triangle_estimate=sum(len(face)-2 for face in m.faces)
    trials.append({'minor_pipe_segments':FAR_SEG,'major_pipe_segments':MAJOR_SEG,'triangles':triangle_estimate,'parts':m.parts,'exact_duplicate_sleeves':duplicate_rails})
    print('BUDGET_TRIAL',trials[-1],flush=True)
    assert m.parts+duplicate_rails==5014,(m.parts,duplicate_rails)
    if triangle_estimate<=20000:break
else:
    raise RuntimeError('Cannot meet budget without removing unique components')
assert triangle_estimate>19000,triangle_estimate

print('LOD_GEOMETRY',len(m.vertices),len(m.faces),m.parts,flush=True)
source=next(o for o in bpy.context.scene.objects if o.type=='MESH')
source.data.calc_loop_triangles();source_triangles=len(source.data.loop_triangles)
material=source.active_material
mesh=bpy.data.meshes.new('OrbitalRing_20K_Distant_Mesh')
mesh.from_pydata(m.vertices,[],m.faces);mesh.update()
uv=mesh.uv_layers.new(name='UV_Atlas')
uv.data.foreach_set('uv',[n for pair in m.uv for n in pair])
mesh.polygons.foreach_set('use_smooth',m.smooth)
bm=bmesh.new();bm.from_mesh(mesh)
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
mesh.update();mesh.materials.append(material)
source.data=mesh;source.name='ORBITAL_RING_20K_DISTANT'
obj=source
bpy.ops.object.select_all(action='DESELECT');obj.hide_set(False);obj.select_set(True)
bpy.context.view_layer.objects.active=obj
for mod in list(obj.modifiers):obj.modifiers.remove(mod)
mesh.calc_loop_triangles();rebuilt_triangles=len(mesh.loop_triangles)
print('BEFORE_BUDGET',rebuilt_triangles,flush=True)
assert rebuilt_triangles<=20000,rebuilt_triangles
tri=obj.modifiers.new('Explicit triangles','TRIANGULATE')
bpy.ops.object.modifier_apply(modifier=tri.name)
mesh=obj.data
mesh.validate(verbose=True,clean_customdata=True)
bm=bmesh.new();bm.from_mesh(mesh)
loose=[v for v in bm.verts if not v.link_faces]
if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
mesh.update();mesh.validate(verbose=True,clean_customdata=True)
mesh.calc_loop_triangles();count=len(mesh.loop_triangles)
assert count<=20000,count
assert count>19000,count
assert len(mesh.materials)==1
assert sum(o.type=='MESH' for o in bpy.context.scene.objects)==1
for other in list(bpy.data.meshes):
    if other.users==0:bpy.data.meshes.remove(other)
for other in list(bpy.data.materials):
    if other!=material and other.users==0:bpy.data.materials.remove(other)
material.use_backface_culling=False
images=[n.image for n in material.node_tree.nodes if n.type=='TEX_IMAGE']
(OUT/'textures').mkdir(exist_ok=True)
for image in images:
    name=image.name.replace('_4096','')+'.png'
    (OUT/'textures'/name).write_bytes(bytes(image.packed_file.data))
    image.filepath='//textures/'+name
obj['lod_triangle_budget']=20000;obj['actual_triangles']=count;obj['source_triangles']=source_triangles
obj['lod_method']='Distant LOD: retain every unique construction component; thin details represented by two-sided cards; no component culling'
report={'mesh_objects':1,'material_slots':len(mesh.materials),'material_datablocks':len(bpy.data.materials),
        'source_triangles':source_triangles,'rebuilt_triangles_before_final_adjustment':rebuilt_triangles,
        'triangles':count,'vertices':len(mesh.vertices),'polygons':len(mesh.polygons),
        'reduction_percent':round(100*(1-count/source_triangles),2),'textures_packed':True,
        'texture_resolution':[4096,4096],'mesh_validated':True,'construction_parts':m.parts,
        'simplification':'no unique part culling; lower cross-sections and thickness only','source_construction_calls':5014,'unique_components_retained':m.parts,'coincident_duplicates_removed':duplicate_rails,'budget_trials':trials,'primitive_calls':call_stats}
(OUT/'asset_report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
scene=bpy.context.scene
scene.render.engine='CYCLES';scene.cycles.samples=64;scene.cycles.use_denoising=True
scene.cycles.device='CPU'
try:
    prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='OPTIX';prefs.get_devices()
    for device in prefs.devices:device.use=device.type!='CPU'
    if any(device.type!='CPU' for device in prefs.devices):scene.cycles.device='GPU'
except Exception:pass
scene.camera=bpy.data.objects['Camera | Overall']
scene.render.resolution_x=1800;scene.render.resolution_y=1800;scene.render.resolution_percentage=100
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Orbital_Ring_20K_Distant.blend'),compress=True)
bpy.ops.export_scene.gltf(filepath=str(OUT/'Orbital_Ring_20K_Distant.glb'),export_format='GLB',use_selection=True,
                          export_materials='EXPORT',export_lights=False,export_cameras=False,export_animations=False)
print('LOD_SAVED',json.dumps(report),flush=True)
if args.render:
    scene.render.filepath=str(OUT/'Orbital_Ring_20K_Distant_Overall.png');bpy.ops.render.render(write_still=True)
    scene.camera=bpy.data.objects['Camera | Truss & armor detail']
    scene.render.resolution_x=1600;scene.render.resolution_y=1100
    scene.render.filepath=str(OUT/'Orbital_Ring_20K_Distant_Detail.png');bpy.ops.render.render(write_still=True)
    print('LOD_COMPLETE',flush=True)
