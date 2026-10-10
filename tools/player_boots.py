"""Production burgundy eight-eye footwear on the unchanged Hero foot rig.

The source upper/sole are closed solids. Angular toe endpoints are separate
native FBX channels, not fictitious multi-frame channels. Only BootLeft and
BootRight atlas pixels belong to this module.
"""
from __future__ import annotations

import hashlib
import math
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parent))

import atlas_kit

try:
    import bpy
    import character_joint_surfaces
    from mathutils import Vector, Quaternion
except ImportError:
    bpy = None

CONTRACT = "hero_footwear_v1"
REFERENCE = "https://www.drmartens.com/uk/en_gb/1460-burgundy-smooth-leather-lace-up-boots-burgundy/p/27277626"
TOE_ANGLES = (15, 30, 45)
BURGUNDY = (57, 20, 28, 255)
LEATHER_LIGHT = (69, 26, 35, 255)
LEATHER_DARK = (41, 16, 22, 255)
LACE = (15, 14, 15, 255)
EYELET = (31, 30, 31, 255)
WELT = (165, 135, 62, 255)
SOLE = (30, 27, 23, 255)


def _smooth(value):
    t = max(0., min(1., value))
    return t * t * (3. - 2. * t)


def _outline(ankle, toe, scale):
    """Round toe, broad ball and heel, retaining the prior support extremes."""
    a, t = ankle.y / scale, toe.y / scale
    xy = ((0,t),(.025,t+.004),(.046,t+.021),(.063,t+.070),
          (.065,a-.052),(.058,a+.018),(.050,a+.060),(.026,a+.073),
          (0,a+.073),(-.026,a+.073),(-.050,a+.060),(-.058,a+.018),
          (-.065,a-.052),(-.063,t+.070),(-.046,t+.021),(-.025,t+.004))
    return [Vector((ankle.x+x*scale,y*scale,0)) for x,y in xy]


def _heel_height(point, ankle, scale):
    return .012 * scale * max(0., min(1., (point.y-ankle.y-.018*scale)/(.055*scale)))


def _sole_under(point, ankle, scale):
    # Toe/ball and rear heel supports keep their original heights. The waist
    # under the arch is recessed, so the heel reads as a separate solid mass.
    arch=max(0.,1-abs((point.y-ankle.y)/scale-.018)/.042)
    return _heel_height(point,ankle,scale)+.018*scale*arch


def _closed_rings(rings):
    sides = len(rings[0]); vertices = [p for ring in rings for p in ring]
    faces = []
    for row in range(len(rings)-1):
        for j in range(sides):
            a=row*sides+j; b=row*sides+(j+1)%sides
            faces.append((a,b,b+sides,a+sides))
    # A real transverse ball edge is retained through the sole underside.
    # Splitting the cap adds no triangles over the former single ngon.
    front=(13,14,15,0,1,2,3); rear=tuple(range(3,14))
    faces.extend((tuple(reversed(front)),tuple(reversed(rear))))
    top=(len(rings)-1)*sides
    faces.extend((tuple(top+j for j in front),tuple(top+j for j in rear)))
    volume=sum(vertices[f[0]].dot(vertices[f[i]].cross(vertices[f[i+1]]))/6
               for f in faces for i in range(1,len(f)-1))
    if volume<0: faces=[tuple(reversed(f)) for f in faces]
    return vertices,faces


def _replace(obj, geometry):
    vertices,faces=geometry; old=obj.data
    materials=list(old.materials)
    mesh=bpy.data.meshes.new(obj.name+"_EightEyeMesh")
    mesh.from_pydata([tuple(p-obj.location) for p in vertices],[],faces)
    mesh.update(calc_edges=True)
    for material in materials: mesh.materials.append(material)
    obj.data=mesh; bpy.data.meshes.remove(old)
    for group in list(obj.vertex_groups): obj.vertex_groups.remove(group)
    for face in mesh.polygons: face.use_smooth=len(face.vertices)==4


def _ankle_keys(obj, side, ankle, scale):
    obj.shape_key_add(name="Basis")
    for label,angle in (("Dorsiflex",45),("Plantarflex",30)):
        key=obj.shape_key_add(name=f"BootAnkle{label}.{side}")
        for vertex,target in zip(obj.data.vertices,key.data):
            point=vertex.co+obj.location
            weights=character_joint_surfaces._weights(obj,vertex.index)
            mix=weights.get("shin."+side,0.)
            compression=math.sqrt(1-4*mix*(1-mix)*math.sin(math.radians(angle)*.5)**2)
            radial=point-ankle; radial.x=0
            target.co=vertex.co+radial*(1/compression-1)
        key.value=0


def _toe_keys(obj, side, pivot, scale):
    if not obj.data.shape_keys: obj.shape_key_add(name="Basis")
    for angle in TOE_ANGLES:
        key=obj.shape_key_add(name=f"BootToeRoll{angle}.{side}")
        for vertex,target in zip(obj.data.vertices,key.data):
            point=vertex.co+obj.location
            field=_smooth((pivot.y-point.y)/(.025*scale))
            rotated=Quaternion((1,0,0),-math.radians(angle)*field)@(point-pivot)+pivot
            target.co=rotated-obj.location
        key.value=0


def _upper_uv(obj,api,side,ankle,scale):
    """Front UV follows the instep then rises up the eight-eye shaft."""
    region="BootLeft" if side=="L" else "BootRight"
    x,y,w,h=api.clothing_region(region); half=w//2; size=api.CLOTHING_ATLAS_SIZE
    layer=obj.data.uv_layers.new(name="UVMap"); obj[api.CLOTHING_ATLAS_REGION_PROP]=region
    points=[v.co+obj.location for v in obj.data.vertices]
    low=min(p.z for p in points);high=max(p.z for p in points)
    front=min(p.y for p in points);back=max(p.y for p in points)
    # Only the bottom rim samples the welt row. The leather toe and crown stay
    # above those pixels; eyelets begin where the crown meets the instep.
    stations=(.050,.14,.20,.23,.62,.81,1.)
    for face in obj.data.polygons:
        sidewall=len({index//16 for index in face.vertices})>1
        segment=face.vertices[0]%16
        is_front=sidewall and segment in (0,1,14,15)
        for loop in face.loop_indices:
            index=obj.data.loops[loop].vertex_index;point=points[index]
            if not sidewall:
                u=.5+(point.x-ankle.x)/(.14*scale)
                v=.75+(point.y-ankle.y)/(.50*scale)
                panel=x
            elif is_front:
                u=max(0.,min(1.,.5+(point.x-ankle.x)/(.106*scale)))
                v=stations[index//16];panel=x+half
            else:
                u=(back-point.y)/(back-front);v=(point.z-low)/(high-low);panel=x
            layer.data[loop].uv=((panel+1+u*(half-2))/size,(y+1+v*(h-2))/size)
    layer.active_render=True


def author(builder, api):
    """Call once after the existing upper and separate sole have been created."""
    rows={p.obj.name:p.obj for p in builder.result.parts}; scale=builder.scale
    for side in ("L","R"):
        ankle=builder.points["ankle."+side]; toe=builder.points["toe."+side]
        outline=_outline(ankle,toe,scale); rings=[]
        centre=Vector((ankle.x,ankle.y-.0585*scale,0))
        for level in (0,1,2):
            ring=[]
            for point in outline:
                p=point.copy()
                p.x=ankle.x+(p.x-ankle.x)*(.975 if level==0 else .95)
                p.y=centre.y+(p.y-centre.y)*(.975 if level==0 else .986)
                if level<2: p.z=_heel_height(point,ankle,scale)+( .029 if level==0 else .041)*scale
                else:
                    along=max(0.,min(1.,(point.y-toe.y)/(ankle.y+.073*scale-toe.y)))
                    p.z=(.066+.030*_smooth(along))*scale
                ring.append(p)
            rings.append(ring)
        # A separate crown gives the toe/vamp genuine low dome curvature.
        # Its front remains plain leather; the lacing starts at the instep.
        a,t=ankle.y/scale,toe.y/scale
        crown=((0,t+.025,.084),(.023,t+.029,.082),(.041,t+.044,.076),(.054,t+.079,.079),
               (.054,a-.026,.096),(.052,a+.021,.105),(.047,a+.051,.105),(.025,a+.068,.104),
               (0,a+.071,.104),(-.025,a+.068,.104),(-.047,a+.051,.105),(-.052,a+.021,.105),
               (-.054,a-.026,.096),(-.054,t+.079,.079),(-.041,t+.044,.076),(-.023,t+.029,.082))
        rings.append([Vector((ankle.x+x*scale,y*scale,z*scale)) for x,y,z in crown])
        for z,rx,ry,cy in ((.112,.053,.048,.012),(.142,.050,.043,.014),(.170,.052,.044,.016)):
            rings.append([Vector((ankle.x+rx*scale*math.sin(j*math.tau/16),
                                 ankle.y+cy*scale-ry*scale*math.cos(j*math.tau/16),z*scale)) for j in range(16)])
        upper=rows["CLO_Boot."+side]; _replace(upper,_closed_rings(rings))
        for vertex in upper.data.vertices:
            point=vertex.co+upper.location
            shin=_smooth((point.z/scale-.055)/.100)
            character_joint_surfaces._assign(upper,vertex.index,{"shin."+side:shin,"foot."+side:1-shin})
        _upper_uv(upper,api,side,ankle,scale)
        _ankle_keys(upper,side,ankle,scale)
        sole_rings=[]
        for level,inset in ((0,1),(1,.978),(2,.975)):
            ring=[]
            for point in outline:
                p=point.copy(); p.x=ankle.x+(p.x-ankle.x)*inset
                p.y=centre.y+(p.y-centre.y)*inset
                bottom=_sole_under(point,ankle,scale)
                top=_heel_height(point,ankle,scale)+.029*scale
                p.z=bottom if level==0 else (bottom+(top-bottom)*.48 if level==1 else top)
                ring.append(p)
            sole_rings.append(ring)
        sole=rows["CLO_BootSole."+side]; _replace(sole,_closed_rings(sole_rings))
        for vertex in sole.data.vertices:
            character_joint_surfaces._assign(sole,vertex.index,{"foot."+side:1.})
        for face in sole.data.polygons: face.use_smooth=False
        api.assign_boot_uv(sole,"BootLeft" if side=="L" else "BootRight")
        pivot=Vector((ankle.x,toe.y+.070*scale,.030*scale))
        for obj in (upper,sole):
            _toe_keys(obj,side,pivot,scale)
            obj["bp_wardrobe_slot"]="boots"; obj["bp_footwear_contract"]=CONTRACT
            obj["bp_ball_pivot_blender"]=tuple(pivot)
            obj["bp_ball_contact_blender"]=(pivot.x,pivot.y,0.)
            obj["bp_footwear_scale"]=scale
        upper["bp_eight_eyelet_rows"]=8
        upper["bp_ankle_section_indices"]=tuple(range(4*16,5*16))
        character_joint_surfaces._surface(builder.result,upper)


def manifest(result):
    rows={p.obj.name:p.obj for p in result.parts}; feet=[]
    point=lambda values: dict(zip(("x","y","z"),map(float,values)))
    for side in ("L","R"):
        upper=rows["CLO_Boot."+side]
        if upper.get("bp_footwear_contract")!=CONTRACT: return {}
        feet.append({"side":side,"foot_bone":"foot."+side,"shin_bone":"shin."+side,
            "renderers":["CLO_Boot."+side,"CLO_BootSole."+side],
            "ball_pivot_blender":point(upper["bp_ball_pivot_blender"]),
            "ball_contact_blender":point(upper["bp_ball_contact_blender"]),
            "forward_blender":point((0,-1,0)),"lateral_blender":point((1,0,0)),"up_blender":point((0,0,1)),
            "toe_shapes":[{"shape":f"BootToeRoll{angle}.{side}","angle_degrees":angle} for angle in TOE_ANGLES],
            "ankle_dorsiflex_shape":"BootAnkleDorsiflex."+side,"ankle_dorsiflex_max_degrees":45,
            "ankle_plantarflex_shape":"BootAnklePlantarflex."+side,"ankle_plantarflex_max_degrees":30,
            "ankle_weight_curve":"linear","eyelet_rows":8,
            "ankle_section_blender":[point(upper.data.vertices[i].co+upper.location)
                                      for i in upper["bp_ankle_section_indices"]]})
    return {"footwear":{"contract":CONTRACT,"source_space":"blender_z_up_minus_y_forward",
                        "reference_url":REFERENCE,"feet":feet}}


def paint(canvas,api):
    """Only the two existing boot rectangles are rewritten."""
    rect=api.atlas_rect_bottom_left; line=api.atlas_line_bottom_left
    for region in ("BootLeft","BootRight"):
        x,y,w,h=api.clothing_region(region); half=w//2
        rect(canvas,x,y,x+w,y+h,BURGUNDY)
        # Smooth leather stays dark; only narrow seams and restrained wear.
        line(canvas,x+5,y+19,x+7,y+41,LEATHER_LIGHT)
        line(canvas,x+9,y+13,x+14,y+47,LEATHER_DARK)
        line(canvas,x+14,y+47,x+27,y+51,LEATHER_DARK)
        line(canvas,x+3,y+18,x+12,y+19,LEATHER_LIGHT)
        for panel in (x,x+half):
            rect(canvas,panel,y,panel+half,y+3,SOLE)
            line(canvas,panel+1,y+5,panel+half-2,y+5,LEATHER_DARK)
            for stitch in range(2,half-2,3):
                rect(canvas,panel+stitch,y+4,panel+stitch+2,y+5,WELT)
        front=x+half
        rect(canvas,front+11,y+31,front+21,y+63,LEATHER_DARK)
        for index in range(8):
            yy=y+33+index*4
            for xx in (front+8,front+23):
                rect(canvas,xx-1,yy-1,xx+2,yy+2,EYELET)
                rect(canvas,xx,yy,xx+1,yy+1,LACE)
            if index<7:
                line(canvas,front+9,yy,front+22,yy+4,LACE)
                line(canvas,front+22,yy,front+9,yy+4,LACE)
        line(canvas,front+5,y+29,front+26,y+29,LEATHER_DARK)


def patch_png(source,output,api):
    """Patch an existing atlas, proving every non-footwear pixel is identical."""
    width,height,pixels=atlas_kit.read_generated_png(Path(source))
    canvas=atlas_kit.PixelCanvas(width,height); canvas.pixels[:]=pixels
    paint(canvas,api)
    regions=[api.clothing_region(name) for name in ("BootLeft","BootRight")]
    for y in range(height):
        for x in range(width):
            # PNG rows are top-down; the authored sub-rects are bottom-up.
            owned=any(rx<=x<rx+rw and ry<=height-1-y<ry+rh for rx,ry,rw,rh in regions)
            offset=(y*width+x)*4
            if not owned and canvas.pixels[offset:offset+4]!=pixels[offset:offset+4]:
                raise RuntimeError("Footwear paint changed an unrelated atlas pixel")
    canvas.write_png(Path(output))
    return hashlib.sha256(Path(output).read_bytes()).hexdigest()


def _volume(obj,points):
    obj.data.calc_loop_triangles()
    return abs(sum(points[t.vertices[0]].dot(points[t.vertices[1]].cross(points[t.vertices[2]]))/6
                   for t in obj.data.loop_triangles))


def toe_weights(angle):
    result={a:0. for a in TOE_ANGLES}; angle=max(0.,min(45.,angle))
    if angle<=15: result[15]=angle/15
    elif angle<=30: result[15]=1-(angle-15)/15; result[30]=(angle-15)/15
    else: result[30]=1-(angle-30)/15; result[45]=(angle-30)/15
    return result


def validate(result,errors):
    """Actual closed geometry, support footprint and composed flex are checked."""
    rows={p.obj.name:p.obj for p in result.parts}
    proof=[]
    for side in ("L","R"):
        upper=rows["CLO_Boot."+side]; sole=rows["CLO_BootSole."+side]
        scale=float(upper.get("bp_footwear_scale",1.))
        if upper.get("bp_eight_eyelet_rows")!=8: errors.append("Eight-eye footwear is missing eyelet rows")
        pivot=Vector(upper["bp_ball_pivot_blender"])
        for obj in (upper,sole):
            points=[v.co+obj.location for v in obj.data.vertices]; baseline=_volume(obj,points)
            edge_counts={}
            for face in obj.data.polygons:
                for i,a in enumerate(face.vertices):
                    edge=tuple(sorted((a,face.vertices[(i+1)%len(face.vertices)])))
                    edge_counts[edge]=edge_counts.get(edge,0)+1
            if any(count!=2 for count in edge_counts.values()) or baseline<=1e-7:
                errors.append(obj.name+" is not a closed positive footwear solid")
            keys=obj.data.shape_keys.key_blocks
            for angle in (0,7.5,15,22.5,30,37.5,45):
                posed=[p.copy() for p in points]
                for endpoint,weight in toe_weights(angle).items():
                    key=keys[f"BootToeRoll{endpoint}.{side}"]
                    for i in range(len(posed)): posed[i]+=(key.data[i].co-obj.data.vertices[i].co)*weight
                ratio=_volume(obj,posed)/baseline
                if not .75<=ratio<=1.25: errors.append(f"{obj.name} toe flex volume {ratio:.4f} at {angle:g}")
                if angle and max(p.z for p in posed if p.y<pivot.y-.025*scale)<=max(p.z for p in points if p.y<pivot.y-.025*scale):
                    errors.append(obj.name+" toe endpoint does not lift the rounded forefoot")
                proof.append({"renderer":obj.name,"toe_angle":angle,"volume_ratio":round(ratio,6)})
            for vertex in obj.data.vertices:
                weights=character_joint_surfaces._weights(obj,vertex.index)
                if set(weights)-{"shin."+side,"foot."+side} or abs(sum(weights.values())-1)>1e-6:
                    errors.append(obj.name+" changed the supported foot skin palette"); break
        sole_points=[v.co+sole.location for v in sole.data.vertices[:16]]
        ankle=result.rig.data.bones["foot."+side].head_local
        toe=result.rig.data.bones["foot."+side].tail_local
        if abs(min(p.y for p in sole_points)-toe.y)>2e-6 or abs(max(p.y for p in sole_points)-ankle.y-.073*scale)>2e-6 or abs(max(abs(p.x-ankle.x) for p in sole_points)-.065*scale)>2e-6:
            errors.append(sole.name+" changed the existing support footprint")
        heel=[p for p in sole_points if p.y>ankle.y+.070*scale]
        if abs(min(p.z for p in sole_points))>2e-6 or any(abs(p.z-.012*scale)>2e-6 for p in heel):
            errors.append(sole.name+" changed toe ground or the 12 mm heel support")
        ring=list(upper["bp_ankle_section_indices"]); basis=[upper.data.vertices[i].co+upper.location for i in ring]
        area=character_joint_surfaces._section_area(basis)
        for angle in (-30,-15,0,15,30,45):
            label="Dorsiflex" if angle>=0 else "Plantarflex"; maximum=45 if angle>=0 else 30
            key=upper.data.shape_keys.key_blocks[f"BootAnkle{label}.{side}"]
            rotation=Quaternion((1,0,0),-math.radians(angle)); posed=[]
            for index in ring:
                point=upper.data.vertices[index].co+upper.location+(key.data[index].co-upper.data.vertices[index].co)*abs(angle)/maximum
                skin=character_joint_surfaces._weights(upper,index)
                posed.append(point*skin.get("shin."+side,0)+(rotation@(point-ankle)+ankle)*skin.get("foot."+side,0))
            ratio=character_joint_surfaces._section_area(posed)/area
            if not .95<=ratio<=1.05: errors.append(f"Boot ankle {side} area {ratio:.4f} at {angle:g}")
            proof.append({"renderer":upper.name,"ankle_angle":angle,"section_area_ratio":round(ratio,6)})
    return proof


def _main():
    """Focused isolated source proof, lit studies and native FBX shape check."""
    import argparse
    import importlib.util
    import json
    from types import SimpleNamespace
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source",type=Path,required=True)
    parser.add_argument("--output-dir",type=Path,required=True)
    parser.add_argument("--review-atlas",type=Path)
    parser.add_argument("--no-previews",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    root=Path(__file__).resolve().parents[1]
    spec=importlib.util.spec_from_file_location("hero_boot_source",root/"tools/build-player-3d-model-v2.py")
    api=importlib.util.module_from_spec(spec);sys.modules[spec.name]=api;spec.loader.exec_module(api)
    bpy.ops.wm.open_mainfile(filepath=str(args.source.resolve()))
    rig=next(o for o in bpy.data.objects if o.type=="ARMATURE")
    rig.animation_data_clear()
    for bone in rig.pose.bones: bone.matrix_basis.identity()
    parts=[SimpleNamespace(obj=bpy.data.objects[name]) for side in ("L","R")
           for name in ("CLO_Boot."+side,"CLO_BootSole."+side)]
    result=SimpleNamespace(rig=rig,parts=parts)
    scale=json.loads(api.DEFAULT_MANIFEST.read_text(encoding="utf-8"))["height_m"]/1.75
    points={}
    for side in ("L","R"):
        points["ankle."+side]=rig.data.bones["foot."+side].head_local.copy()
        points["toe."+side]=rig.data.bones["foot."+side].tail_local.copy()
    author(SimpleNamespace(result=result,points=points,scale=scale),api)
    errors=[]; proof=validate(result,errors)
    if errors: raise RuntimeError("Footwear source failed:\n"+"\n".join(errors))
    args.output_dir.mkdir(parents=True,exist_ok=True)
    atlas=args.review_atlas or args.output_dir/"boot-clothing-atlas.png"
    if not args.review_atlas: patch_png(api.DEFAULT_CLOTHING_ATLAS,atlas,api)
    image=bpy.data.images.load(str(atlas.resolve()),check_existing=False)
    for part in parts:
        for material in part.obj.data.materials:
            if material.use_nodes:
                for node in material.node_tree.nodes:
                    if node.type=="TEX_IMAGE" and node.image: node.image=image
    if not args.no_previews:
        scene=bpy.context.scene;scene.render.engine="CYCLES";scene.cycles.samples=12
        scene.render.resolution_x=scene.render.resolution_y=512;scene.render.resolution_percentage=100
        scene.world.color=(.10,.10,.10)
        for obj in list(scene.objects):
            if obj.type in {"CAMERA","LIGHT"}: bpy.data.objects.remove(obj,do_unlink=True)
            elif obj.type=="MESH": obj.hide_render=obj not in [part.obj for part in parts]
        target=Vector((.096,-.055,.090))
        for location,energy,size in (((1,-2,2),220,1.5),((-1,-1,1),100,1.5),((1,2,1),160,1.0)):
            light=bpy.data.lights.new("Boot study light","AREA");light.energy=energy;light.size=size
            obj=bpy.data.objects.new("Boot study light",light);scene.collection.objects.link(obj)
            obj.location=location;obj.rotation_euler=(target-obj.location).to_track_quat("-Z","Y").to_euler()
        data=bpy.data.cameras.new("Boot study camera");camera=bpy.data.objects.new("Boot study camera",data)
        scene.collection.objects.link(camera);scene.camera=camera;data.type="ORTHO";data.ortho_scale=.40
        studies=(("standing-side",(1,.03,.08),0,0),("standing-front",(.05,-1,.08),0,0),
                 ("toe-flex45-side",(1,.03,.08),45,0),("ankle-dorsi45-side",(1,.03,.08),0,45),
                 ("ankle-plantar30-side",(1,.03,.08),0,-30))
        for name,offset,toe_angle,ankle_angle in studies:
            for side in ("L","R"):
                bone=rig.pose.bones["foot."+side];bone.rotation_mode="QUATERNION"
                axis=bone.bone.matrix_local.to_3x3().inverted()@Vector((1,0,0))
                bone.rotation_quaternion=Quaternion(axis,-math.radians(ankle_angle))
                for stem in ("CLO_Boot.","CLO_BootSole."):
                    obj=bpy.data.objects[stem+side]
                    obj.hide_render=side=="R" and name!="standing-front"
                    for key in obj.data.shape_keys.key_blocks:
                        if key.name!="Basis": key.value=0
                    for angle,weight in toe_weights(toe_angle).items(): obj.data.shape_keys.key_blocks[f"BootToeRoll{angle}.{side}"].value=weight
                    if stem=="CLO_Boot.":
                        label="Dorsiflex" if ankle_angle>=0 else "Plantarflex"
                        obj.data.shape_keys.key_blocks[f"BootAnkle{label}.{side}"].value=abs(ankle_angle)/(45 if ankle_angle>=0 else 30)
            view_target=Vector((0,target.y,target.z)) if name=="standing-front" else target
            data.ortho_scale=.47 if name=="standing-front" else .40
            camera.location=view_target+Vector(offset);camera.rotation_euler=(view_target-camera.location).to_track_quat("-Z","Y").to_euler()
            bpy.context.view_layer.update();scene.render.filepath=str((args.output_dir/(name+".png")).resolve())
            bpy.ops.render.render(write_still=True);print("BOOT STUDY",name,flush=True)
    for bone in rig.pose.bones: bone.matrix_basis.identity()
    for part in parts:
        for key in part.obj.data.shape_keys.key_blocks:
            if key.name!="Basis": key.value=0
    bpy.context.view_layer.update()
    data=manifest(result);data["validation"]=proof
    data["pair_triangles"]=sum(len(part.obj.data.loop_triangles) for part in parts)
    fbx=args.output_dir/"boot-native-fbx-probe.fbx"
    bpy.ops.object.select_all(action="DESELECT");rig.select_set(True)
    for part in parts: part.obj.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=str(fbx.resolve()),use_selection=True,
        object_types={"ARMATURE","MESH"},add_leaf_bones=False,bake_anim=False,
        use_mesh_modifiers=False,axis_forward="-Z",axis_up="Y")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(fbx.resolve()),use_anim=False)
    imported={o.name:o for o in bpy.data.objects if o.type=="MESH"}
    for side in ("L","R"):
        for stem in ("CLO_Boot.","CLO_BootSole."):
            obj=imported[stem+side];keys=obj.data.shape_keys.key_blocks
            expected={f"BootToeRoll{angle}.{side}" for angle in TOE_ANGLES}
            if stem=="CLO_Boot.": expected|={"BootAnkleDorsiflex."+side,"BootAnklePlantarflex."+side}
            if expected!={key.name for key in keys if key.name!="Basis"}:
                raise RuntimeError("Native FBX did not retain separate footwear endpoint channels: "+obj.name)
    data["native_fbx_endpoint_channels_verified"]=True
    (args.output_dir/"boot-source-proof.json").write_text(json.dumps(data,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print("Footwear source and native FBX endpoint proof passed; pair triangles="+str(data["pair_triangles"]),flush=True)


if __name__=="__main__": _main()
