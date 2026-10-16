"""Deterministic CombatTest-only suspended crane poses on the ordinary worker rig.

The retained rest skeleton and production skin are unchanged. Root travel and
three cable endpoints belong to the runtime suspension controller. Generate via
run-blender.py with both output directories staged.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("crane_retained_worker", ROOT / "tools/build-combat-zombie-3d-model.py")
zombie = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = zombie
spec.loader.exec_module(zombie)
common = zombie.common
FPS = 100
CLIPS = (("CraneIdle", 2., True), ("CraneSignal", .65, False),
         ("CraneDescend", 1., True), ("CraneStrike", 1.1, False),
         ("CraneRecover", 1., False), ("CraneAscend", 1., True),
         ("CraneTransport", 2., True), ("CraneStart", .6, False),
         ("CraneBrake", .7, False), ("CraneHurt", .55, False))
OUT = ROOT / "Assets/Resources/Combat"
SOURCE = ROOT / "ArtSource/CombatCrane"


def smooth(value):
    t = min(1., max(0., value))
    return t*t*(3.-2.*t)


def place(rig, name, start, end, width=1., roll=0.):
    bone = rig.pose.bones[name]
    start, end = Vector(start), Vector(end)
    rest = bone.bone
    direction = end-start
    # A world matrix with uniform width needs shear under a stretched parent.
    # FBX/Unity retain local TRS, so decomposing that matrix silently lengthens
    # the child again. Solve its axis in the actual inherited frame instead.
    frame=rest.convert_local_to_pose(Matrix.Identity(4),rest.matrix_local,
        parent_matrix=bone.parent.matrix if bone.parent else Matrix.Identity(4),
        parent_matrix_local=bone.parent.bone.matrix_local if bone.parent else Matrix.Identity(4))
    linear=frame.to_3x3();local_direction=linear.inverted()@direction
    q=Vector((0.,1.,0.)).rotation_difference(local_direction.normalized())
    if roll:q=q@Quaternion(Vector((0.,1.,0.)),math.radians(roll))
    bone.location=frame.inverted()@start
    bone.rotation_quaternion=q
    bone.scale=(width/(linear@(q@Vector((1.,0.,0.)))).length,
        direction.length/rest.length/(linear@(q@Vector((0.,1.,0.)))).length,
        width/(linear@(q@Vector((0.,0.,1.)))).length)
    bpy.context.view_layer.update()
    if (bone.head-start).length>.0001 or (bone.tail-end).length>.0001:
        raise ValueError("Crane local TRS lost an authored endpoint: "+name)


def pose(builder, kind, seconds):
    rig = builder.result.rig
    rig.animation_data.action = None
    builder._reset_pose()
    # The skin, topology, bind matrices and skeleton are unchanged. Broader
    # local cross-sections keep the long boom a mangled arm with a real elbow
    # and hand rather than a zero-volume point between stretched sleeves.
    sway = math.sin(seconds*math.tau/(2. if kind == "CraneIdle" else 1.))
    signal = math.sin(min(1., seconds/.65)*math.pi) if kind == "CraneSignal" else 0.
    strike = smooth((seconds-.25)/.30)*(1.-smooth((seconds-.55)/.55)) if kind == "CraneStrike" else 0.
    recoil = (1.-smooth(seconds))*.24 if kind == "CraneRecover" else 0.
    transport = kind in ("CraneTransport","CraneStart","CraneBrake")
    takeup = smooth(seconds/.6) if kind=="CraneStart" else 1.-smooth(seconds/.7) if kind=="CraneBrake" else 1. if transport else 0.
    hitch = math.sin(seconds/.6*math.pi)*.13 if kind=="CraneStart" else math.sin(seconds/.7*math.pi)*-.16 if kind=="CraneBrake" else 0.
    hurt = smooth(seconds/.07)*(1.-smooth((seconds-.12)/.43)) if kind=="CraneHurt" else 0.
    hoist = .035*math.sin(seconds*math.tau) if kind in ("CraneDescend","CraneAscend") else 0.
    lag = .025*sway + hitch
    p = Vector((.045-.035*hurt, .10+.035*takeup, .98+.025*hoist))
    place(rig,"pelvis",p,p+Vector((-.10,-.075,.15)),1.02,roll=-21.-8.*hurt)
    place(rig,"spine",(-.055,.025,1.13),(.015,-.09,1.25),1.05,roll=16.+14.*hurt)
    place(rig,"chest",(.015,-.09,1.25),(-.04,-.22,1.41-.04*takeup),1.12,roll=23.-17.*hurt)
    place(rig,"neck",(-.04,-.22,1.41-.04*takeup),(.07,-.32,1.38-.04*takeup),.92)
    # A compressed neck folds the human face into the collar. Slow inspection
    # belongs to the head alone; machinery does not track the hero each frame.
    look=math.sin(seconds*math.pi)*.34 if kind=="CraneIdle" else .11*sway if kind=="CraneTransport" else -.15*hurt
    head=(.07,-.32,1.38-.17*takeup)
    place(rig,"head",head,(.10,-.34,1.57-.17*takeup),1.03,roll=math.degrees(look)-12.)
    for side, sign in (("L",1.),("R",-1.)):
        shoulder=(sign*(.21 if side=="L" else .19),-.17,1.30 if side=="L" else 1.43-.04*takeup)
        place(rig,"clavicle."+side,(-.015,-.18,1.35),shoulder,1.05)
        if side == "R":
            # The mechanical hook owns reach. The flesh arm now winds up and
            # reels a short wrist socket instead of telescoping like a rod.
            elbow=(-.36-.035*hurt,-.43+.10*signal-.04*strike+.08*takeup+.08*hurt,1.49+.04*signal-.19*takeup-.05*strike)
            wrist=(-.30+lag,-.70+.16*signal-.15*strike+.12*recoil+.12*takeup+.13*hurt,1.15-.12*strike+.08*signal-.06*takeup+.09*hurt)
            tip=(wrist[0],wrist[1]+.015,wrist[2]-.17)
            place(rig,"upper_arm."+side,shoulder,elbow,1.12,roll=-9.)
            place(rig,"forearm."+side,elbow,wrist,1.10,roll=12.)
            place(rig,"hand."+side,wrist,tip,1.38,roll=-14.)
        else:
            elbow=(.55,.10,1.0+.025*hurt)
            wrist=(.48,.05,1.35+.02*signal)
            place(rig,"upper_arm."+side,shoulder,elbow,1.02)
            place(rig,"forearm."+side,elbow,wrist,1.05,roll=-18.)
            place(rig,"hand."+side,wrist,(.49,.06,1.19+.02*signal),1.12)
        hip=(sign*.105,.10,.99)
        if side=="L":
            knee=(.22,.34,.70)
            ankle=(.27,.52,1.03)
            foot=(.28,.69,.99)
        else:
            knee=(-.15,.17,.49+.03*takeup)
            ankle=(-.12+.015*sway,.22,.055+.08*takeup)
            foot=(-.12+.015*sway,.055,-.035+.08*takeup)
        place(rig,"thigh."+side,hip,knee,.99,roll=12. if side=="L" else -8.)
        place(rig,"shin."+side,knee,ankle,1.01)
        place(rig,"foot."+side,ankle,foot,1.03)
    # Production fingers are the existing authored cylindrical-grip blendshape,
    # applied after body sampling by NpcHandPose (right=1, left=.82).
    pelvis=rig.pose.bones["pelvis"];matrix=pelvis.matrix.copy()
    matrix.translation+=Vector((0.,.10*takeup,-.13*takeup));pelvis.matrix=matrix
    bpy.context.view_layer.update()
    return builder.snapshot_pose()


def author(builder):
    for name, seconds, loop in CLIPS:
        if name in builder.result.actions:
            bpy.data.actions.remove(builder.result.actions.pop(name).action)
        count=round(seconds*FPS)
        # Sample at 20 Hz; all authored takes are exported at the shared 100 Hz.
        # Enough samples resolve the .20s contact sweep without a dense IK job.
        samples=max(2,round(seconds*20))
        keys=[(i/samples,pose(builder,name,seconds*i/samples)) for i in range(samples+1)]
        builder._create_action(name,"combat_crane",seconds,loop,count,FPS,keys)
        print("Authored "+name,flush=True)


def measure(builder):
    signature=hashlib.sha256()
    records=[]
    for name,seconds,loop in CLIPS:
        action=builder.result.actions[name].action
        for curve in sorted(common.iter_action_fcurves(action),key=lambda c:(c.data_path,c.array_index)):
            signature.update(json.dumps([name,curve.data_path,curve.array_index,[[round(v,7) for v in key.co] for key in curve.keyframe_points]],separators=(",",":")).encode())
        samples=[]
        for t in (0.,seconds*.35,seconds*.5,seconds):
            pose(builder,name,t)
            samples.append(dict(seconds=round(t,5),bones_unity_axis_swap={n:[round(v,6) for v in (b.head.x,b.head.z,b.head.y)]
                for n,b in builder.result.rig.pose.bones.items() if n in ("pelvis","clavicle.L","clavicle.R","hand.R","head")}))
        records.append(dict(name=name,duration_seconds=seconds,loop=loop,samples=samples))
    return dict(generator="tools/build-combat-crane-3d-model.py",generator_version="crane-v3",rig="NpcHumanV2",
        test_only=True,root_motion=False,animation_events=0,retained_rest_skeleton=True,
        contact_start_seconds=.35,contact_end_seconds=.55,suspension_bones=["hand.L","hand.R","pelvis","foot.L","foot.R","head"],
        suspension_local_offsets_m=[[0,0,0]]*5+[[0,.25,0]],cable_length_m=1.,cable_axis_unity=[0,1,0],
        signature=signature.hexdigest(),clips=records)


def empty(name,parent,location):
    result=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(result)
    result.parent=parent;result.location=location
    return result


def part_mesh(name,parent,centre,size):
    bpy.ops.mesh.primitive_cube_add(size=1.,location=centre)
    result=bpy.context.object;result.name=name;result.scale=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    result.parent=parent
    return result


def join_parts(parts,name):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in parts:obj.select_set(True)
    bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join()
    parts[0].name=name


def unity_mesh(name,parent,vertices,faces):
    # The explicit axis swap reverses handedness; signed volume owns winding.
    vertices=[Vector((x,z,y)) for x,y,z in vertices]
    volume=sum(vertices[f[0]].dot(vertices[f[i]].cross(vertices[f[i+1]]))/6.
        for f in faces for i in range(1,len(f)-1))
    if abs(volume)<1.e-9:raise ValueError("Crane passive solid has no volume: "+name)
    if volume<0.:faces=[tuple(reversed(f)) for f in faces]
    mesh=bpy.data.meshes.new(name+"Mesh");mesh.from_pydata(vertices,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj);obj.parent=parent
    return obj


def ring_part(name,parent,centre,radius,inner,depth,plane="XY",sides=16):
    vertices=[]
    for layer in (-depth*.5,depth*.5):
        for r in (radius,inner):
            for i in range(sides):
                a=i*math.tau/sides
                local=(r*math.cos(a),r*math.sin(a),layer) if plane=="XY" else (r*math.cos(a),layer,r*math.sin(a))
                vertices.append(tuple(centre[j]+local[j] for j in range(3)))
    faces=[]
    for i in range(sides):
        n=(i+1)%sides
        faces.extend(((i,n,sides+n,sides+i),(2*sides+i,3*sides+i,3*sides+n,2*sides+n),
            (i,2*sides+i,2*sides+n,n),(sides+i,sides+n,3*sides+n,3*sides+i)))
    return unity_mesh(name,parent,vertices,faces)


def forged_hook(parent):
    path=((0.,.18,.042),(-.035,.27,.055),(-.105,.365,.065),(-.115,.455,.065),
        (-.055,.535,.060),(.045,.555,.052),(.13,.505,.043),(.15,.425,.031),(.105,.365,.016))
    vertices=[];sides=12
    for i,(x,y,radius) in enumerate(path):
        before=Vector(path[max(0,i-1)][:2]);after=Vector(path[min(len(path)-1,i+1)][:2])
        tangent=(after-before).normalized();normal=Vector((-tangent.y,tangent.x))
        for j in range(sides):
            a=j*math.tau/sides
            vertices.append((x+normal.x*radius*math.cos(a),y+normal.y*radius*math.cos(a),radius*.90*math.sin(a)))
    faces=[tuple(reversed(range(sides))),tuple(range((len(path)-1)*sides,len(path)*sides))]
    for ring in range(len(path)-1):
        for j in range(sides):
            n=(j+1)%sides;start=ring*sides
            faces.append((start+j,start+n,start+sides+n,start+sides+j))
    return unity_mesh("ForgedJHook",parent,vertices,faces)


def hook_assets():
    hook=empty("CraneHook",None,(0,0,0))
    pieces=[ring_part("HookEye",hook,(0.,.06,0.),.06,.027,.07),forged_hook(hook),
        ring_part("HookSwivel",hook,(0.,.145,0.),.062,.018,.10,"XZ",12)]
    join_parts(pieces,"CraneHookVisible")
    empty("ANCHOR_Cable",hook,(0,0,0))
    empty("ANCHOR_Impact",hook,(0,0,.48))
    socket=empty("CraneWristSocket",None,(0,0,0))
    pieces=[ring_part("WristCuff",socket,(0.,0.,0.),.09,.052,.15,"XZ",14),
        ring_part("CableGuide",socket,(0.,.09,0.),.076,.021,.06,"XZ",14)]
    for x in (-.074,.074):
        pieces.append(part_mesh("CuffClamp",socket,(x,0.,.005),(.028,.12,.10)))
    join_parts(pieces,"CraneWristSocketVisible")
    empty("ANCHOR_CableExit",socket,(0,0,.11))
    return hook,socket


def suspension_head_asset():
    head=empty("CraneSuspensionHead",None,(0,0,0))
    parts=[part_mesh("CraneCrossbeam",head,(0,0,.07),(1.30,.58,.10)),
        part_mesh("CraneClampLeft",head,(-.57,0,.15),(.16,.50,.14)),
        part_mesh("CraneClampRight",head,(.57,0,.15),(.16,.50,.14))]
    join_parts(parts,"CraneSuspensionHeadUpperVisible")
    swivel=empty("PIVOT_CableSwivel",head,(0,0,0))
    parts=[part_mesh("CraneRotatingLowerPlate",swivel,(0,0,-.05),(1.30,.58,.08)),
        ring_part("SwivelBearing",swivel,(0,0,0),.15,.07,.08,"XZ",14),
        part_mesh("CrownFeedBracket",swivel,(0,.36,-.05),(.24,.26,.08))]
    for x,y in ((-.30,-.10),(.30,-.10),(0,.16),(-.55,.20),(.55,.20),(0,.42)):
        bpy.ops.mesh.primitive_cylinder_add(vertices=10,radius=.09,depth=.10,location=(x,y,-.12),rotation=(math.pi/2,0,0))
        obj=bpy.context.object;obj.parent=swivel;parts.append(obj)
    join_parts(parts,"CraneSuspensionHeadSwivelVisible")
    for name,position in (("ANCHOR_CableLeft",(-.30,-.10,-.17)),("ANCHOR_CableRight",(.30,-.10,-.17)),("ANCHOR_CableRear",(0,.16,-.17)),
            ("ANCHOR_CableFootLeft",(-.55,.20,-.17)),("ANCHOR_CableFootRight",(.55,.20,-.17)),("ANCHOR_CableHead",(0,.42,-.17))):
        empty(name,swivel,position)
    return head


def passive_assets():
    head=suspension_head_asset()
    cable=empty("CraneCableSegment",None,(0,0,0))
    bpy.ops.mesh.primitive_cylinder_add(vertices=6,radius=.014,depth=1.,location=(0,0,.5))
    mesh=bpy.context.object;mesh.name="CraneCableVisible";mesh.parent=cable
    # All dimensions below are authored in Blender's X/depth/Z frame. Roots
    # are at the driven y=9.15m in Unity; their maximum height is +.27m.
    rails=empty("CraneRails",None,(0,0,0));rail_parts=[]
    for x in (-5.7,5.7):
        rail_parts.extend((part_mesh("RailWeb",rails,(x,0,.10),(.12,11.7,.20)),
            part_mesh("RailTop",rails,(x,0,.205),(.30,11.7,.07)),
            part_mesh("RailFoot",rails,(x,0,.015),(.34,11.7,.07))))
        for y in (-5.7,5.7):rail_parts.append(part_mesh("RailEndStop",rails,(x,y,.12),(.40,.16,.30)))
        for y in (-5.4,-3.6,-1.8,0.,1.8,3.6,5.4):
            rail_parts.append(part_mesh("RailShoe",rails,(x,y,-.035),(.56,.20,.08)))
        for y in (-5.5,5.5):
            # Fixed hangers meet the existing lower joists at world9.8075m.
            # Only these outer mounts rise above the moving gantry envelope;
            # the carriage/bridge retain their independent +.28m fan limit.
            if abs(x)-.20 <= 4.1:
                raise ValueError("A fixed crane hanger entered the fan clearance")
            rail_parts.append(part_mesh("FixedHanger",rails,(x,y,.47),(.10,.10,.44)))
            rail_parts.append(part_mesh("JoistBoltPlate",rails,(x,y,.675),(.40,.30,.06)))
            for dx in (-.12,.12):
                for dy in (-.085,.085):
                    bpy.ops.mesh.primitive_cylinder_add(vertices=6,radius=.025,depth=.03,location=(x+dx,y+dy,.705))
                    bolt=bpy.context.object;bolt.name="JoistBolt";bolt.parent=rails;rail_parts.append(bolt)
    join_parts(rail_parts,"CraneRailsVisible")
    bridge=empty("CraneBridge",None,(0,0,0))
    bridge_parts=[part_mesh("BridgeWeb",bridge,(0,0,.11),(11.6,.14,.18)),
        part_mesh("BridgeTop",bridge,(0,0,.215),(11.6,.44,.07)),
        part_mesh("BridgeFoot",bridge,(0,0,.015),(11.6,.44,.07))]
    for x in (-5.7,5.7):
        bridge_parts.append(part_mesh("BridgeEndTruck",bridge,(x,0,.10),(.40,.74,.20)))
    for x in (-5.35,5.35):bridge_parts.append(part_mesh("CarriageStop",bridge,(x,0,.11),(.10,.64,.28)))
    join_parts(bridge_parts,"CraneBridgeVisible")
    empty("ANCHOR_Carriage",bridge,(0,0,0))
    return head,cable,rails,bridge,*hook_assets()


def passive_measure(root):
    points=[];triangles=0;digest=hashlib.sha256()
    for obj in root.children_recursive:
        if obj.type!="MESH":continue
        vertices=[root.matrix_world.inverted()@obj.matrix_world@v.co for v in obj.data.vertices]
        points.extend(vertices)
        triangles+=sum(len(face.vertices)-2 for face in obj.data.polygons)
        digest.update(json.dumps([[[round(v,7) for v in p] for p in vertices],[list(f.vertices) for f in obj.data.polygons]],separators=(",",":")).encode())
    minimum=[min(p[i] for p in points) for i in range(3)]
    maximum=[max(p[i] for p in points) for i in range(3)]
    return dict(name=root.name,bounds_min=[round(minimum[i],6) for i in (0,2,1)],
        bounds_max=[round(maximum[i],6) for i in (0,2,1)],triangles=triangles,signature=digest.hexdigest(),
        anchors=[dict(name=obj.name,position=[round((root.matrix_world.inverted()@obj.matrix_world.translation)[i],6) for i in (0,2,1)])
            for obj in root.children_recursive if obj.type=="EMPTY"])


def export_passive(root,path):
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for obj in root.children_recursive:obj.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={"EMPTY","MESH"},
        axis_forward="-Z",axis_up="Y",add_leaf_bones=False,bake_anim=False,use_custom_props=True)


def preview(directory,source,measure_only=False,single=False):
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/"ArtSource/VillageLife/StationWorker.blend"))
    rig=bpy.data.objects["RIG_Player"]
    with bpy.data.libraries.load(str(source/"CraneActions.blend"),link=False) as (available,loaded):
        loaded.actions=[name for name,_,_ in CLIPS]
    actions={action.name.split(".")[0]:action for action in loaded.actions}
    scene=bpy.context.scene
    meshes=[]
    atlas=bpy.data.images.load(str(ROOT/"Assets/Resources/Combat/ZombieFace1.png"),check_existing=True)
    wardrobe=json.loads((ROOT/"Assets/Resources/VillageLife/StationWorker.json").read_text(encoding="utf8"))["wardrobe"]
    clothes={name for item in wardrobe["items"] for name in item["renderers"]}
    selected=[item for item in wardrobe["items"] if item["id"] in ("shirt.everyday","outerwear.everyday","trousers.everyday","boots.everyday")]
    covered={name for item in selected for name in item["covered_renderers"]}
    visible=({obj.name for obj in bpy.data.objects if obj.type=="MESH"}-clothes|{name for item in selected for name in item["renderers"]})-covered
    visible-={name for name in visible if name.endswith(".R") and name.startswith(("GEO_Hand","CLO_Glove"))}
    for obj in list(bpy.data.objects):
        if obj.type in ("CAMERA","LIGHT"):bpy.data.objects.remove(obj,do_unlink=True)
        elif obj.type=="MESH":
            obj.hide_render=obj.name not in visible
            if obj.hide_render:continue
            meshes.append(obj)
            if obj.data.shape_keys and "CylindricalGrip" in obj.data.shape_keys.key_blocks:
                obj.data.shape_keys.key_blocks["CylindricalGrip"].value=.82 if obj.name.endswith(".L") else 1.
            for material in obj.data.materials:
                if not material or not material.use_nodes:continue
                for node in material.node_tree.nodes:
                    if node.type=="TEX_IMAGE" and node.image and "Atlas" in node.image.name:node.image=atlas
    scene.render.engine="CYCLES";scene.cycles.samples=16
    scene.render.resolution_x=900;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    scene.world.color=(.018,.018,.018)
    camera_data=bpy.data.cameras.new("CraneReviewCamera");camera=bpy.data.objects.new("CraneReviewCamera",camera_data)
    scene.collection.objects.link(camera);camera.location=(3.,-4.,2.5)
    camera.rotation_euler=(Vector((0.,-.30,1.3))-camera.location).to_track_quat("-Z","Y").to_euler()
    camera_data.type="ORTHO";camera_data.ortho_scale=3.7;scene.camera=camera
    for name,position,energy,size in (("CraneReviewKey",(1.,-3.,4.),650.,3.),("CraneReviewRim",(-3.,1.,3.),450.,2.)):
        data=bpy.data.lights.new(name,"AREA");data.energy=energy;data.shape="DISK";data.size=size
        obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj);obj.location=position
        obj.rotation_euler=(Vector((0.,-.3,1.))-obj.location).to_track_quat("-Z","Y").to_euler()
    rig.animation_data_create()
    directory.mkdir(parents=True,exist_ok=True)
    bounds_records=[]
    envelopes=[]
    head_objects={obj.name for obj in meshes if "head" in obj.vertex_groups and all(group.name in ("head","neck") for group in obj.vertex_groups)}
    for name,duration,_ in (() if single else CLIPS):
        rig.animation_data.action=actions[name]
        minimum=Vector((float("inf"),)*3);maximum=-minimum;extreme={};head_radius=0.
        times=sorted(set([min(duration,i/20.) for i in range(math.ceil(duration*20)+1)]+[duration]))
        for time in times:
            scene.render.fps=FPS;scene.frame_set(round(time*FPS));bpy.context.view_layer.update()
            depsgraph=bpy.context.evaluated_depsgraph_get()
            for obj in meshes:
                evaluated=obj.evaluated_get(depsgraph)
                for corner in evaluated.bound_box:
                    point=evaluated.matrix_world@Vector(corner)
                    if obj.name in head_objects:head_radius=max(head_radius,(point-rig.pose.bones["head"].head).length)
                    for i in range(3):
                        if point[i]<minimum[i]:minimum[i]=point[i];extreme["min_"+str(i)]=obj.name
                        if point[i]>maximum[i]:maximum[i]=point[i];extreme["max_"+str(i)]=obj.name
        # These conservative evaluated bounds include every visible garment,
        # joint surface, hair and the closed existing hand shape at each key.
        envelopes.append(dict(clip=name,keys_hz=20,bounds_min_unity=[round(minimum[i],5) for i in (0,2,1)],
            bounds_max_unity=[round(maximum[i],5) for i in (0,2,1)],ordinary_worker_scale=1.0171429,extreme_objects=extreme,
            head_bone_radius=round(head_radius,5)))
        print("Measured full worker envelope "+name,flush=True)
    if not single:(directory/"CranePoseEnvelopes.json").write_text(json.dumps(envelopes,indent=2)+"\n",encoding="utf8")
    if measure_only:return
    hook,socket=hook_assets()
    metal=bpy.data.materials.new("CranePreviewForgedMetal");metal.use_nodes=True
    principled=metal.node_tree.nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value=(.17,.19,.17,1.)
    principled.inputs["Metallic"].default_value=.8;principled.inputs["Roughness"].default_value=.37
    rope=empty("CranePreviewAttackCable",None,(0,0,0))
    bpy.ops.mesh.primitive_cylinder_add(vertices=6,radius=.014,depth=1.,location=(0,0,.5))
    rope_mesh=bpy.context.object;rope_mesh.parent=rope
    for root in (hook,socket,rope):
        for obj in root.children_recursive:
            if obj.type=="MESH":obj.data.materials.append(metal)
    exit_anchor=next(obj for obj in socket.children if obj.name.startswith("ANCHOR_CableExit"))
    preview_head=suspension_head_asset();preview_head.location=(0,0,2.7);preview_head.rotation_euler.z=math.pi
    tethers=[]
    for i in range(6):
        tether=empty("CranePreviewLimbTether"+str(i),None,(0,0,0))
        bpy.ops.mesh.primitive_cylinder_add(vertices=6,radius=.014,depth=1.,location=(0,0,.5))
        bpy.context.object.parent=tether;tethers.append(tether)
    for root in (preview_head,*tethers):
        for obj in root.children_recursive:
            if obj.type=="MESH":obj.data.materials.append(metal)
    feed_names=("ANCHOR_CableLeft","ANCHOR_CableRight","ANCHOR_CableRear","ANCHOR_CableFootLeft","ANCHOR_CableFootRight","ANCHOR_CableHead")
    bones=("hand.L","hand.R","pelvis","foot.L","foot.R","head")
    views=(("CraneIdle",.5),) if single else (("CraneIdle",.5),("CraneTransport",.5),("CraneStart",.3),("CraneBrake",.35),("CraneStrike",.50),("CraneHurt",.12))
    for name,seconds in views:
        rig.animation_data.action=actions[name]
        scene.render.fps=FPS;scene.frame_set(round(seconds*FPS));bpy.context.view_layer.update()
        wrist=rig.pose.bones["hand.R"].head.copy();elbow=rig.pose.bones["forearm.R"].head.copy()
        direction=(wrist-elbow).normalized()
        socket.location=wrist;socket.rotation_mode="QUATERNION"
        socket.rotation_quaternion=Vector((0,0,1)).rotation_difference(direction)
        bpy.context.view_layer.update();feed=exit_anchor.matrix_world.translation.copy()
        hook.location=feed+Vector((-.08,-.70,-.13)) if name=="CraneStrike" else feed+Vector((0.,0.,-.08))
        hook.rotation_mode="QUATERNION";hook.rotation_quaternion=Vector((0,0,1)).rotation_difference(Vector((0,0,-1)))
        rope.location=feed;delta=hook.location-feed;rope.rotation_mode="QUATERNION"
        rope.rotation_quaternion=Vector((0,0,1)).rotation_difference(delta.normalized());rope.scale=(1,1,delta.length)
        bpy.context.view_layer.update()
        for i,tether in enumerate(tethers):
            top=next(obj for obj in preview_head.children_recursive if obj.name.startswith(feed_names[i])).matrix_world.translation
            end=rig.pose.bones[bones[i]].head.copy()
            if i==5:end+=rig.pose.bones["head"].matrix.to_quaternion()@Vector((0.,.25,0.))
            delta=end-top;tether.location=top;tether.rotation_mode="QUATERNION"
            tether.rotation_quaternion=Vector((0,0,1)).rotation_difference(delta.normalized());tether.scale=(1,1,delta.length)
        bpy.context.view_layer.update()
        points=[];depsgraph=bpy.context.evaluated_depsgraph_get()
        for obj in meshes:
            evaluated=obj.evaluated_get(depsgraph);mesh=evaluated.to_mesh()
            points.extend(evaluated.matrix_world@v.co for v in mesh.vertices);evaluated.to_mesh_clear()
        minimum=[min(p[i] for p in points) for i in range(3)];maximum=[max(p[i] for p in points) for i in range(3)]
        bounds_records.append(dict(clip=name,seconds=seconds,bounds_min_unity=[round(minimum[i],5) for i in (0,2,1)],
            bounds_max_unity=[round(maximum[i],5) for i in (0,2,1)],ordinary_worker_scale=1.0171429))
        scene.render.filepath=str(directory/(name+".png"));bpy.ops.render.render(write_still=True)
    (directory/"CranePoseBounds.json").write_text(json.dumps(bounds_records,indent=2)+"\n",encoding="utf8")
    if single:return
    (directory/"CranePoseEnvelopes.json").write_text(json.dumps(envelopes,indent=2)+"\n",encoding="utf8")
    for name,root,centre,size in (("CraneHook",hook,(0.,0.,.30),.92),("CraneWristSocket",socket,(0.,0.,.02),.45)):
        for other in (hook,socket,rope,preview_head,*tethers):
            for obj in other.children_recursive:obj.hide_render=other!=root
        for obj in meshes:obj.hide_render=True
        root.location=(0,0,0);root.rotation_quaternion=Quaternion((1,0,0,0))
        camera.location=(1.5,-2.,1.2);camera.rotation_euler=(Vector(centre)-camera.location).to_track_quat("-Z","Y").to_euler()
        camera_data.ortho_scale=size
        scene.render.filepath=str(directory/(name+".png"));bpy.ops.render.render(write_still=True)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir",type=Path,default=OUT)
    parser.add_argument("--source-dir",type=Path,default=SOURCE)
    parser.add_argument("--validate-only",action="store_true")
    parser.add_argument("--preview-dir",type=Path)
    parser.add_argument("--preview-only",action="store_true")
    parser.add_argument("--measure-only",action="store_true")
    parser.add_argument("--single-preview",action="store_true")
    parser.add_argument("--hook-only",action="store_true",help="Publish the action bank/new hook kit while preserving existing gantry FBX bytes")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    if args.preview_only:
        if args.preview_dir is None:raise ValueError("Preview-only requires --preview-dir")
        preview(args.preview_dir,args.source_dir,args.measure_only,args.single_preview);return
    builder=zombie.load_builder()
    author(builder);payload=measure(builder)
    author(builder)
    if payload!=measure(builder):raise ValueError("Crane bank generation is nondeterministic")
    passive=passive_assets()
    payload["models"]=[passive_measure(root) for root in passive]
    payload["transport_right_grip"]=1.;payload["transport_left_grip"]=.82
    payload["hook"]=dict(axis_unity=[0,1,0],impact_offset_m=.48,impact_radius_m=.18,mass_kg=9.,wrist_exit_m=.11,
        contact_points=[[0,.18,0],[-.10,.36,0],[-.08,.50,0],[.06,.54,0],[.14,.43,0]],contact_radii=[.05,.065,.07,.06,.07],
        core_length_m=.20,core_radius_m=.07,maximum_cable_m=6.)
    if args.hook_only:
        previous=json.loads((OUT/"Crane3D.json").read_text(encoding="utf8"))
        old={model["name"]:model["signature"] for model in previous["models"]}
        for model in payload["models"]:
            if model["name"] in old and model["name"]!="CraneSuspensionHead" and old[model["name"]]!=model["signature"]:
                raise ValueError("Hook-only generation changed retained gantry geometry: "+model["name"])
    if args.validate_only:
        if json.loads((args.output_dir/"Crane3D.json").read_text(encoding="utf8"))!=payload:
            raise ValueError("Published crane manifest differs from deterministic source")
    else:
        args.output_dir.mkdir(parents=True,exist_ok=True)
        builder.result.rig.animation_data.action=None;builder._reset_pose()
        common.export_animation_fbx(args.output_dir/"CraneActions.fbx",builder.result)
        for root in passive:
            if not args.hook_only or root.name in ("CraneHook","CraneWristSocket","CraneSuspensionHead"):
                export_passive(root,args.output_dir/(root.name+".fbx"))
        (args.output_dir/"Crane3D.json").write_text(json.dumps(payload,indent=2)+"\n",encoding="utf8")
        common.save_blend(args.source_dir/"CraneActions.blend")
    print("COMBAT CRANE ART CONTRACT OK "+payload["signature"],flush=True)


if __name__=="__main__":main()
