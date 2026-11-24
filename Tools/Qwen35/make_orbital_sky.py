"""Offline NASA panorama bake; no runtime generation, copies or CRTs.

Observational/catalog-derived imagery. Globe orientation, lighting, atmosphere
and exposure are artistic choices, not a reconstruction of a specific orbit.
"""
from pathlib import Path
from qwen_paths import DATA_ROOT
import argparse
import hashlib
import json
import os
import shutil
os.environ.setdefault('OPENCV_IO_ENABLE_OPENEXR','1')
import cv2
import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS=300_000_000  # Known 21600x10800 NASA map.


def srgb_to_linear(v):
    return np.where(v<=.04045,v/12.92,((v+.055)/1.055)**2.4)


def linear_to_srgb(v):
    v=np.clip(v,0,1)
    return np.where(v<=.0031308,v*12.92,1.055*v**(1/2.4)-.055)


def load_color(path,width):
    with Image.open(path) as image:
        if image.size[0]!=2*image.size[1]:
            raise ValueError(f'Expected 2:1 spherical texture: {path}')
        image.draft('RGB',(width,width//2))
        data=np.asarray(image.convert('RGB').resize((width,width//2),Image.Resampling.LANCZOS),dtype=np.float32)/255
    return srgb_to_linear(data)


def sample(image,u,v):
    height,width=image.shape[:2]
    x=((u%1)*width-.5).astype(np.float32)
    y=np.clip(v*height-.5,0,height-1).astype(np.float32)
    return cv2.remap(image,x,y,cv2.INTER_LINEAR,borderMode=cv2.BORDER_WRAP)


def smoothstep(low,high,value):
    t=np.clip((value-low)/(high-low),0,1)
    return t*t*(3-2*t)


def bake(path,width=4096,source_dir=(DATA_ROOT / 'art/sky/nasa')):
    if width<512 or width%2:
        raise ValueError('Panorama width must be even and at least 512.')
    manifest=json.loads((source_dir/'source-manifest.json').read_text(encoding='utf-8'))
    for entry in manifest['sources']:
        source=source_dir/entry['file']
        if hashlib.sha256(source.read_bytes()).hexdigest()!=entry['sha256']:
            raise ValueError(f'Source checksum mismatch: {source}')
    stars=cv2.imread(str(source_dir/'starmap_2020_4k.exr'),cv2.IMREAD_UNCHANGED)
    if stars is None or stars.shape!=(2048,4096,3) or not np.all(np.isfinite(stars)):
        raise ValueError('NASA star EXR is missing or invalid.')
    stars=stars[...,::-1].copy()  # BGR -> linear RGB; EXR is not sRGB.
    day=load_color(source_dir/'world.200407.3x21600x10800.jpg',8192)
    night=load_color(source_dir/'BlackMarble_2016_3km.jpg',4096)
    # Isolate warm lights from the color product's blue/dark surface background.
    city_signal=np.maximum(night[...,0]-.65*night[...,1]-.25*night[...,2]-.003,0)
    night*=smoothstep(.002,.025,city_signal)[...,None]
    print('Loaded and verified three NASA source maps.',flush=True)
    height=width//2
    u=(np.arange(width,dtype=np.float32)+.5)/width
    longitude=(.5-u)*2*np.pi  # Unity Skybox/Panoramic convention.
    yaw,elevation=np.deg2rad(66),np.deg2rad(-35)
    center=np.array([np.sin(yaw)*np.cos(elevation),np.sin(elevation),np.cos(yaw)*np.cos(elevation)],dtype=np.float32)
    angular_radius=np.deg2rad(49);radius=np.sin(angular_radius)
    # Put the complete solar disc behind the upper-left Earth limb. The visible
    # spherical cap must remain on the night side; halo is added only outside it.
    limb_up=np.array([0,1,0],dtype=np.float32)-center*center[1]
    limb_up/=np.linalg.norm(limb_up)
    limb_west=np.array([-np.cos(yaw),0,np.sin(yaw)],dtype=np.float32)
    limb_axis=.80*limb_up+.60*limb_west;limb_axis/=np.linalg.norm(limb_axis)
    solar_radius=np.deg2rad(.2666)
    sun_offset=angular_radius-np.deg2rad(.35)
    light=center*np.cos(sun_offset)+limb_axis*np.sin(sun_offset)
    light/=np.linalg.norm(light)
    if sun_offset+solar_radius>=angular_radius:
        raise ValueError('Solar disc must be completely occulted by Earth.')
    eye=-center
    up=np.array([0,1,0],dtype=np.float32)-eye*eye[1];up/=np.linalg.norm(up)
    observer_latitude,observer_longitude=np.deg2rad(22),np.deg2rad(100)
    north=eye*np.sin(observer_latitude)+up*np.cos(observer_latitude)
    meridian=(eye-north*np.sin(observer_latitude))/np.cos(observer_latitude)
    east=np.cross(north,meridian)
    # A rigid rotation preserves the catalog's star relationships.
    a,b=np.deg2rad(24),np.deg2rad(18)
    rotation=np.array([[np.cos(a),-np.sin(a)*np.cos(b),np.sin(a)*np.sin(b)],
                       [np.sin(a),np.cos(a)*np.cos(b),-np.cos(a)*np.sin(b)],
                       [0,np.sin(b),np.cos(b)]],dtype=np.float32)
    image=np.empty((height,width,3),dtype=np.uint8)
    max_visible_incidence=-1.0
    visible_day_pixels=0
    rng=np.random.default_rng(351008)  # Sub-LSB quantization dither only.
    for start in range(0,height,128):
        end=min(start+128,height)
        latitude=(.5-(np.arange(start,end,dtype=np.float32)+.5)/height)*np.pi
        dx=np.cos(latitude[:,None])*np.cos(longitude[None,:])
        dy=np.broadcast_to(np.sin(latitude[:,None]),dx.shape)
        dz=np.cos(latitude[:,None])*np.sin(longitude[None,:])
        sx,sy,sz=(sum(rotation[row,i]*d for i,d in enumerate((dx,dy,dz))) for row in range(3))
        col=sample(stars,.5-np.arctan2(sz,sx)/(2*np.pi),.5-np.arcsin(np.clip(sy,-1,1))/np.pi)*.24
        dot=dx*center[0]+dy*center[1]+dz*center[2]
        disc=dot*dot-(1-radius*radius)
        mask=(disc>=0)&(dot>0)
        depth=dot-np.sqrt(np.maximum(disc,0))
        nx,ny,nz=((d*depth-c)/radius for d,c in zip((dx,dy,dz),center))
        normals=(nx,ny,nz)
        geo_north=sum(n*c for n,c in zip(normals,north))
        geo_east=sum(n*c for n,c in zip(normals,east))
        geo_meridian=sum(n*c for n,c in zip(normals,meridian))
        earth_u=.5+(np.arctan2(geo_east,geo_meridian)+observer_longitude)/(2*np.pi)
        earth_v=.5-np.arcsin(np.clip(geo_north,-1,1))/np.pi
        albedo=sample(day,earth_u,earth_v)
        emission=sample(night,earth_u,earth_v)
        incidence=nx*light[0]+ny*light[1]+nz*light[2]
        if np.any(mask):
            max_visible_incidence=max(max_visible_incidence,float(incidence[mask].max()))
            visible_day_pixels+=int(np.count_nonzero(incidence[mask]>0))
        # Art-directed day/night overlay: the projected disc centre keeps the
        # night lights, while the day-map share increases continuously to the rim.
        # This is a display blend, independent of the occulted Sun's irradiance.
        projected_radius=np.clip(np.sqrt(np.maximum(1-dot*dot,0))/radius,0,1)
        day_mix=.22+.68*np.power(projected_radius,1.65)
        night_surface=albedo*.010+np.array([.0015,.0045,.010],dtype=np.float32)
        day_surface=albedo*.62
        surface=night_surface*(1-day_mix[...,None])+day_surface*day_mix[...,None]
        surface+=emission*(1-.65*day_mix[...,None])*1.65
        facing=np.sqrt(np.maximum(disc,0))/radius
        surface+=np.power(1-np.clip(facing,0,1),10)[...,None]*np.array([.00015,.0006,.0018],dtype=np.float32)
        col[mask]=surface[mask]
        angle=np.arccos(np.clip(dot,-1,1))
        limb_dot=dx*limb_axis[0]+dy*limb_axis[1]+dz*limb_axis[2]
        limb_alignment=limb_dot/np.sqrt(np.maximum(1-dot*dot,1e-8))
        backlight=np.power(np.clip((limb_alignment+1)*.5,0,1),14)
        rim=np.exp(-np.square((angle-angular_radius)/.0055))
        outside=(angle>angular_radius)&(dot>0)
        delta=np.maximum(angle-angular_radius,0)
        halo=np.exp(-delta/.018)*outside
        outer_halo=np.exp(-delta/.045)*outside
        col+=rim[...,None]*np.array([.045,.16,.38])*(.18+.82*backlight[...,None])
        col+=halo[...,None]*np.array([.006,.026,.085])*(.16+.84*backlight[...,None])
        col+=outer_halo[...,None]*np.array([.0008,.0035,.011])*(.10+.90*backlight[...,None])
        # No sun sprite is drawn. Only the part of the corona/scattering halo
        # beyond the solid Earth's silhouette is visible.
        sun_dot=dx*light[0]+dy*light[1]+dz*light[2]
        sun_angle=np.arccos(np.clip(sun_dot,-1,1))
        corona=(.30*np.exp(-sun_angle/.010)+.045*np.exp(-sun_angle/.036))*outside
        col+=corona[...,None]*np.array([1.0,.92,.77])
        rgb=linear_to_srgb(col)*255+rng.uniform(-.35,.35,col.shape)
        image[start:end]=np.clip(np.rint(rgb),0,255).astype(np.uint8)
    if visible_day_pixels or max_visible_incidence>0:
        raise ValueError(f'Occulted-sun setup exposes a sunlit surface: {max_visible_incidence}')
    path.parent.mkdir(parents=True,exist_ok=True)
    backup=(DATA_ROOT / 'validation/sky/before-nasa')
    if path.exists() and not (backup/path.name).exists():
        backup.mkdir(parents=True,exist_ok=True);shutil.copy2(path,backup/path.name)
    Image.fromarray(image).save(path)
    provenance=dict(source_manifest=str(source_dir/'source-manifest.json'),output=str(path),width=width,height=height,
                    sha256=hashlib.sha256(path.read_bytes()).hexdigest(),sources=manifest['sources'],
                    star_exposure=.24,earth_subobserver_latitude=22,earth_subobserver_longitude=100,
                    lighting='Artist-directed radial day/night overlay: night centre, increasing daytime share toward the rim; widened blue atmospheric halo and occulted-sun glow.',
                    earth_angular_radius_degrees=float(np.rad2deg(angular_radius)),
                    sun_direction=light.tolist(),sun_offset_degrees=float(np.rad2deg(sun_offset)),
                    solar_disc_radius_degrees=float(np.rad2deg(solar_radius)),
                    solar_occultation_margin_degrees=float(np.rad2deg(angular_radius-sun_offset-solar_radius)),
                    max_visible_surface_sun_incidence=max_visible_incidence,visible_day_pixels=visible_day_pixels,
                    night_surface_fill=.010,night_ambient_rgb=[.0015,.0045,.010],city_light_gain=1.65,daylight_map_exposure=.62,
                    daylight_mix_center=.22,daylight_mix_edge=.90,daylight_radial_power=1.65,
                    atmosphere_core_width_radians=.0055,halo_outer_scale_radians=.018,halo_far_scale_radians=.045,
                    compositing='Catalog stars plus cloud-free Earth July 2004 and night lights 2016. Artist-chosen lighting/atmosphere/placement; not a specific orbit/time or NASA-produced artwork.',
                    runtime='One static RGB panorama. No source EXR/JPEG files are copied into Assets.')
    (source_dir.parent/'bake-manifest.json').write_text(json.dumps(provenance,indent=2),encoding='utf-8')
    print(f'Baked NASA panorama: {path}, {width}x{height}, {path.stat().st_size:,} bytes',flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--width',type=int,default=4096)
    parser.add_argument('--source-dir',type=Path,default=(DATA_ROOT / 'art/sky/nasa'))
    parser.add_argument('--output',type=Path,default=Path('Assets/QwenWorld/Environment/OrbitalSky.png'))
    args=parser.parse_args();bake(args.output,args.width,args.source_dir)
