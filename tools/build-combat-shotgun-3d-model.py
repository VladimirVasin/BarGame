"""Deterministic test-polygon double barrel and bone-only production-hero bank.

Run with tools/run-blender.py; --validate-only measures the published FBX
geometry, hinged contacts, animation endpoints and the independent rebuild.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys
import importlib.util
import bpy
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("shotgun_pistol_helpers", ROOT / "tools/build-combat-pistol-3d-model.py")
pistol = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = pistol
spec.loader.exec_module(pistol)
combat, kit, common, hero = pistol.combat, pistol.kit, pistol.common, pistol.hero
FPS = 100
SURFACES = {"Steel": (.19, .215, .205), "WornSteel": (.39, .405, .36),
            "Wood": (.31, .225, .15), "WoodWear": (.43, .33, .225),
            "Rubber": (.095, .105, .085), "Brass": (.56, .43, .23),
            "Shell": (.36, .15, .115), "Lead": (.37, .39, .36), "Flash": (1., .64, .22)}
kit.SURFACES = SURFACES
SUPPORT = (-.012, .043, .22)
SUPPORT_WEIGHT = .9
HINGE = (0., .09, .11)
BREAK_DEGREES = 48.
ANCHORS = {"Grip": (0., 0., 0.), "Hinge": HINGE,
           "SupportGrip": SUPPORT, "Muzzle": (0., .11, .71),
           "MuzzleLeft": (-.019, .11, .71), "MuzzleRight": (.019, .11, .71),
           "ChamberLeft": (-.019, .11, .115), "ChamberRight": (.019, .11, .115),
           "ShellLeft": (-.019, .11, .115), "ShellRight": (.019, .11, .115),
           "StockContact": (0., .07, -.29),
           "MuzzleFlash": (0., .11, .76)}
SHELL_GRIP = (0., 0., -.03)
COLLISION = [dict(center=[0., .087, .41], size=[.078, .10, .62]),
             dict(center=[0., .04, -.025], size=[.084, .18, .27]),
             dict(center=[0., .062, -.22], size=[.063, .158, .27])]
CLIPS = (("ShotgunRest", 4., True), ("ShotgunRaise", .35, False),
         ("ShotgunAim", 4., True), ("ShotgunFire", .55, False),
         ("ShotgunReload", 2.8, False), ("ShotgunLower", .35, False))
FIRE_STOPS = ((0., "aim", 0., 0., 0., 0., 0.),
              (.04, "kick", 19., .045, .055, -1., -2.),
              (.10, "absorb", 14., .075, .035, -4., -6.),
              (.23, "return", 5., .034, .012, -2.5, -3.5),
              (.39, "settle", -1.5, -.006, -.003, .6, .8),
              (.55, "aim", 0., 0., 0., 0., 0.))
FIRE_POSES = {name: (pitch, back, up, spine, chest) for _, name, pitch, back, up, spine, chest in FIRE_STOPS}
# The action timeline owns the shell transactions; no animation event does.
RELOAD_STOPS = ((0., "aim"), (.4, "open"), (.65, "eject"),
                (.95, "pouch"), (1.30, "left_align"), (1.6, "left_seat"),
                (1.8, "pouch"), (2.0, "right_align"), (2.2, "right_seat"),
                (2.4, "close"), (2.8, "aim"))


def profiled_stock():
    # Four shaped rings give the familiar curved wrist, comb and shoulder butt.
    rings = ((-.345, .025, -.012, .136), (-.27, .029, -.012, .132),
             (-.12, .022, -.005, .094), (-.045, .018, -.04, .041))
    vertices = [(x, y, z) for z, half, low, high in rings for x, y in
                ((-half, low), (half, low), (half, high), (-half, high))]
    faces = [(3, 2, 1, 0), (12, 13, 14, 15)]
    faces.extend((i*4+j, i*4+(j+1)%4, (i+1)*4+(j+1)%4, (i+1)*4+j)
                 for i in range(3) for j in range(4))
    if kit.bp.signed_volume((vertices, faces)) < 0:
        faces = [tuple(reversed(face)) for face in faces]
    return vertices, faces


def make_shell(name, spent=False):
    s = kit.Item(name)
    # Seat is the rear rim. A live shell's crimp closes the bore; a spent shell
    # has an actual dark hollow lip rather than a bullet glued onto its mouth.
    s.rod("Brass", (0., 0., -.006), (0., 0., .012), .0102, sides=12)
    s.rod("WornSteel", (0., 0., -.0063), (0., 0., -.006), .003, sides=10)
    if spent:
        body = kit.bp.u_rotated(kit.ring([(.012,.0095),(.059,.0095),(.059,.008),(.014,.008)],12),(90,0,0))
        s.add("Shell", body)
        s.rod("Rubber", (0.,0.,.013), (0.,0.,.014), .008, sides=12)
    else:
        s.rod("Shell", (0., 0., .012), (0., 0., .059), .0095, sides=12)
        for angle in range(0, 180, 60):
            a = math.radians(angle)
            s.rod("Rubber", (math.cos(a)*-.007,math.sin(a)*-.007,.0591),
                  (math.cos(a)*.007,math.sin(a)*.007,.0591), .0006, sides=4)
    s.anchor("Seat", (0.,0.,0.)); s.anchor("Grip", SHELL_GRIP); s.anchor("Centre", (0.,0.,.027))
    return s


def make_items():
    gun = kit.Item("Shotgun")
    # Side-by-side bores, raised centre rib and one bead. No branding, engraving
    # or tactical attachments: dark worn steel and plain repaired timber.
    gun.add("Wood", profiled_stock())
    gun.box("Rubber", (0., .062, -.35), (.055,.142,.009), chamfer=.002)
    gun.box("Steel", (0., .069, .055), (.067,.062,.126), chamfer=.009)
    gun.box("Steel", (0., .094, .087), (.065,.036,.047), chamfer=.005)
    for x in (-.019,.019):
        # Ring profile rotated onto +Z makes open barrels, including real bores.
        barrel = kit.bp.u_rotated(kit.ring([(.112,.017),(.71,.014),(.71,.0103),(.112,.0103)],12),(90,0,0))
        gun.add("Steel", kit.translated(barrel, (x,.11,0.)), "Hinge")
        gun.rod("WornSteel", (x,.124,.19), (x,.122,.68), .0013, "Hinge", sides=5)
    gun.box("Steel", (0., .125, .412), (.012,.006,.588), "Hinge", chamfer=.001)
    gun.add("Brass", kit.ellipsoid((0.,.132,.696),(.005,.005,.005),8,4), "Hinge")
    gun.box("Wood", (0., .071, .28), (.054,.049,.234), "Hinge", chamfer=.010)
    for x in (-.0265,.0265):
        for z in (.20,.235,.27,.305,.34,.375):
            gun.box("WoodWear", (x,.073,z), (.0018,.021,.002), "Hinge", chamfer=.0003)
    # Straight wrist handle and open double-trigger guard preserve finger space.
    gun.add("Wood", kit.bp.u_rotated(kit.bp.u_box((0.,-.009,-.023),(.036,.085,.057),.006),(9.,0.,0.)))
    for a,b in (((0.,.029,.018),(0.,-.027,.02)),((0.,-.027,.02),(0.,-.027,.098)),
                 ((0.,-.027,.098),(0.,.037,.105))):
        gun.rod("Steel",a,b,.0035,sides=6)
    for z in (.044,.068): gun.rod("WornSteel",(0.,.037,z),(0.,.002,z-.007),.0025,sides=6)
    gun.rod("Steel",(-.039,.09,.11),(.039,.09,.11),.009,sides=10)
    gun.box("WornSteel",(-.015,.111,.051),(.041,.007,.013),chamfer=.002)
    for x in (-.034,.034):
        gun.rod("WornSteel",(x,.067,.057),(x*1.01,.067,.057),.004,sides=8)
        for z in (-.24,-.19): gun.box("WoodWear",(x*.82,.065,z),(.001,.005,.046),chamfer=.0002)
    for name,point in ANCHORS.items():
        parent = "Hinge" if name in ("SupportGrip","Muzzle","MuzzleLeft","MuzzleRight","ChamberLeft","ChamberRight","ShellLeft","ShellRight","MuzzleFlash") else None
        gun.anchor(name,point,parent)
    live = make_shell("Shell")
    for group in ("ShellLeft","ShellRight"):
        for (_,role),solids in live.parts.items():
            for solid in solids: gun.add(role,kit.translated(solid,ANCHORS[group]),group)
    for angle in (0.,45.):
        flash = kit.bp.u_rotated(kit.bp.u_box((0.,0.,0.),(.095,.026,.002),.0004),(0.,0.,angle))
        gun.add("Flash",kit.translated(flash,ANCHORS["MuzzleFlash"]),"MuzzleFlash")
    pellet = kit.Item("Pellet")
    pellet.add("Lead", kit.ellipsoid((0.,0.,0.),(.0084,.0084,.0084),8,4))
    pellet.anchor("Centre",(0.,0.,0.))
    return [gun,live,make_shell("SpentShell",True),pellet]


def payload(items):
    signature = pistol.geometry_signature(items)
    if signature != pistol.geometry_signature(make_items()): raise ValueError("Shotgun rebuild is not deterministic")
    data = kit.manifest(items,signature)
    data.update(generator="tools/build-combat-shotgun-3d-model.py",generator_version="1.0.0",test_only=True)
    data["models"][0]["collision_shapes"] = dict(space="grip_local_unity_metres",boxes=COLLISION)
    data["mechanism"] = dict(hinge="Hinge",hinge_axis=[1.,0.,0.],break_degrees=BREAK_DEGREES,
        chambers=["ChamberLeft","ChamberRight"],seated_shells=["ShellLeft","ShellRight"],
        shell_grip=SHELL_GRIP,reload_stops=[dict(seconds=t,phase=p) for t,p in RELOAD_STOPS],
        open_seconds=.4,eject_seconds=.65,left_seat_seconds=1.6,right_seat_seconds=2.2,close_seconds=2.4)
    data["actions"] = dict(source="ArtSource/Combat/CombatActions.blend",root_motion=False,animation_events=0,
        fps=FPS,bone_only=True,support_grip_weight=SUPPORT_WEIGHT,
        support_grip=SUPPORT,support_forward=[0.,1.,0.],support_up=[-1.,0.,0.],
        clips=[dict(name=n,duration_seconds=d,loop=l) for n,d,l in CLIPS],
        endpoints="Raise Rest→Aim; Lower Aim→Rest; Fire/Reload Aim→Aim",
        fire_recoil=dict(interpolation="smoothstep",stops=[dict(seconds=t,barrel_rise_degrees=p,
            grip_back_m=b,grip_up_m=u,spine_degrees=s,chest_degrees=c) for t,_,p,b,u,s,c in FIRE_STOPS]))
    for (group,_),solids in items[0].parts.items():
        if group == "MuzzleFlash": continue
        for points,_ in solids:
            for point in points:
                if not any(all(abs(point[i]-box["center"][i]) <= box["size"][i]*.5+.001 for i in range(3)) for box in COLLISION):
                    raise ValueError("Shotgun drop collision misses geometry " + str(point))
    return data


def frame_point(centre,up,forward,local):
    return centre + forward.cross(up)*local[0] + up*local[1] + forward*local[2]


def reload_target(kind,centre,up,forward):
    if kind == "pouch": return Vector((.25,-.12,.97)),Vector((0.,-1.,1.)).normalized(),Vector((-1.,0.,0.))
    hinged = Quaternion(forward.cross(up), math.radians(-BREAK_DEGREES))
    # The reflected source rotates in the inverse direction to Unity +X.
    bore = hinged @ forward; barrel_up = hinged @ up
    chamber = Vector(ANCHORS["ChamberLeft" if kind.startswith("left") else "ChamberRight"])
    local = Vector(HINGE) + Vector((chamber.x-HINGE[0],0.,0.))
    point = frame_point(centre,up,forward,local) + barrel_up*(chamber.y-HINGE[1]) + bore*(chamber.z-HINGE[2])
    if kind.endswith("align"): point -= bore*.075
    point += bore*SHELL_GRIP[2]
    return point,bore,barrel_up


def author_actions():
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / "ArtSource/Combat/CombatActions.blend"))
    rig = bpy.data.objects["RIG_Player"]
    rig.animation_data.action = bpy.data.actions["CombatReady"]
    bpy.context.scene.frame_set(0); bpy.context.view_layer.update()
    config = common.BuildConfig(None,None,None,None,None,None,None,1.75,20260919,"apose")
    builder = combat.CombatBuilder(config,hero.DEFAULT_FACE_ATLAS,hero.DEFAULT_CLOTHING_ATLAS)
    builder.result = common.BuildResult(root=rig.parent,rig=rig,collections={},materials={},parts=[])
    builder.points = builder.create_pose_points(); base = builder.snapshot_pose()
    rig.animation_data.action = None
    for action in list(bpy.data.actions): bpy.data.actions.remove(action)
    for obj in list(bpy.data.objects):
        if obj not in (rig,rig.parent): bpy.data.objects.remove(obj,do_unlink=True)
    common.ANIMATION_FPS = FPS; bpy.context.scene.render.fps = FPS

    def solve(side,target,axis,palm):
        rotation = pistol.hand_rotation(builder,side,axis,palm)
        rest = rig.data.bones["hand."+side]
        delta = rotation @ rest.matrix_local.to_3x3().inverted()
        wrist = target-delta @ (builder.hand_frame(side)[0]-rest.head_local)
        # Elbow behind the fingers prevents a bent wrist, also for crosswise support.
        distal = axis.cross(palm).normalized() if side == "L" else palm.cross(axis).normalized()
        shoulder = rig.pose.bones["upper_arm."+side].head
        pole = (wrist-distal*rig.data.bones["forearm."+side].length-shoulder).normalized()

        try: builder.solve_arm(side,wrist,rotation,pole)
        except ValueError as error: raise ValueError(f"Shotgun target {tuple(target)} axis {tuple(axis)}: {error}") from error

    def pose(kind):
        builder._reset_pose(); builder._apply_pose(base)
        up,forward = Vector((0.,0.,1.)),Vector((0.,-1.,0.))
        centre = Vector((-.155,-.34,1.25))
        if kind in FIRE_POSES:
            pitch,back,rise,spine,chest = FIRE_POSES[kind]
            for name,value in (("spine",spine),("chest",chest),("head",-(spine+chest)*.35)):
                bone = rig.pose.bones[name]
                bone.rotation_quaternion = bone.rotation_quaternion @ Quaternion(Vector((1.,0.,0.)),math.radians(value))
            bpy.context.view_layer.update()
            turn = Quaternion(Vector((1.,0.,0.)),math.radians(-pitch))
            up,forward = turn@up,turn@forward; centre += Vector((0.,back,rise))
        elif kind == "rest":
            turn = Quaternion(Vector((1.,0.,0.)),math.radians(8.))
            up,forward = turn@up,turn@forward; centre = Vector((-.13,-.26,1.15))
        else:
            # Bring the breech down in front of the body while keeping the
            # stock in the right hand. The left hand alone handles ammunition.
            turn = Quaternion(Vector((1.,0.,0.)),math.radians(-12.))
            up,forward = turn@up,turn@forward; centre = Vector((-.115,-.29,1.17))
        clavicle = rig.pose.bones["clavicle.L"]
        matrix = clavicle.matrix.copy()
        turn = Quaternion(Vector((0.,0.,1.)),math.radians(-25.))
        clavicle.matrix = Matrix.Translation(matrix.translation)@turn.to_matrix().to_4x4()@matrix.to_3x3().to_4x4()
        bpy.context.view_layer.update()
        solve("R",centre,up,up.cross(forward))
        left = frame_point(centre,up,forward,SUPPORT)
        left_axis,left_palm = up.cross(forward),up
        if kind in ("left_align","left_seat","right_align","right_seat","pouch"):
            left,left_axis,left_palm = reload_target(kind,centre,up,forward)
        elif kind in ("open","eject","close"):
            turn = Quaternion(forward.cross(up),math.radians(-BREAK_DEGREES))
            left = frame_point(centre,up,forward,HINGE) + turn@(left-frame_point(centre,up,forward,HINGE))
            left_axis,left_palm = up.cross(forward),turn@up
        solve("L",left,left_axis,left_palm)

        print("SHOTGUN POSE",kind,[(side,round(math.degrees((rig.pose.bones["hand."+side].head-rig.pose.bones["forearm."+side].head).angle(rig.pose.bones["hand."+side].tail-rig.pose.bones["hand."+side].head)),3)) for side in ("R","L")],flush=True)
        return builder.snapshot_pose()

    poses = {name:pose(name) for name in dict.fromkeys(("rest",*FIRE_POSES,*(n for _,n in RELOAD_STOPS)))}
    tracks = {"ShotgunRaise":((0.,"rest"),(.35,"aim")),"ShotgunLower":((0.,"aim"),(.35,"rest")),
              "ShotgunFire":tuple((t,n) for t,n,*_ in FIRE_STOPS),"ShotgunReload":RELOAD_STOPS}

    def pin_support(current):
        builder._reset_pose(); builder._apply_pose(current)
        hand = rig.pose.bones["hand.R"]; delta=hand.matrix@hand.bone.matrix_local.inverted()
        contact,axis,palm=builder.hand_frame("R")
        up=(delta.to_3x3()@axis).normalized(); normal=(delta.to_3x3()@palm).normalized(); forward=normal.cross(up)
        solve("L",frame_point(delta@contact,up,forward,SUPPORT),up.cross(forward),up)
        return builder.snapshot_pose()

    for name,duration,loop in CLIPS:
        keys=[]
        for frame in range(round(duration*FPS)+1):
            seconds=frame/FPS
            if loop: current=poses["rest" if name=="ShotgunRest" else "aim"]
            else:
                stops=tracks[name]; index=min(len(stops)-2,next((i for i in range(len(stops)-1) if seconds<=stops[i+1][0]+1e-8),len(stops)-2))
                a,b=stops[index:index+2]; weight=combat.dialogue.smooth((seconds-a[0])/(b[0]-a[0]))
                current=builder.blend(poses[a[1]],poses[b[1]],weight)
            if name in ("ShotgunRest","ShotgunAim","ShotgunFire","ShotgunRaise","ShotgunLower"):
                current=pin_support(current)
            keys.append((frame/round(duration*FPS),current))
        builder._create_action(name,"combat_shotgun",duration,loop,round(duration*FPS),FPS,keys)
    return builder


def validate_actions(builder):
    rig=builder.result.rig; endpoints={}; maximum_wrist={}; contact_error=0.
    lower=("root","pelvis","thigh.L","thigh.R","shin.L","shin.R","foot.L","foot.R")
    reference=None
    for name,duration,_ in CLIPS:
        rig.animation_data.action=builder.result.actions[name].action
        endpoints[name]=[]; maximum_wrist[name]=0.
        for frame in range(round(duration*FPS)+1):
            bpy.context.scene.frame_set(frame); bpy.context.view_layer.update()
            if frame in (0,round(duration*FPS)):
                endpoints[name].append({bone.name:bone.matrix_basis.copy() for bone in rig.pose.bones})
            if reference is None: reference={bone:rig.pose.bones[bone].matrix_basis.copy() for bone in lower}
            for bone in lower:
                if max(abs(v) for row in rig.pose.bones[bone].matrix_basis-reference[bone] for v in row)>.00001:
                    raise ValueError("Shotgun lower body moved "+name+"/"+bone)
            frames={}
            for side in ("R","L"):
                hand=rig.pose.bones["hand."+side]; fore=rig.pose.bones["forearm."+side]
                bend=math.degrees((hand.head-fore.head).angle(hand.tail-hand.head))
                maximum_wrist[name]=max(maximum_wrist[name],bend)
                if bend>50.: raise ValueError(f"Shotgun wrist {name}/{frame}/{side} bends {bend:.3f}")
                delta=hand.matrix@hand.bone.matrix_local.inverted(); centre,axis,palm=builder.hand_frame(side)
                frames[side]=(delta@centre,(delta.to_3x3()@axis).normalized(),(delta.to_3x3()@palm).normalized())
            if name!="ShotgunReload":
                centre,up,palm=frames["R"]; forward=palm.cross(up).normalized()
                error=(frame_point(centre,up,forward,SUPPORT)-frames["L"][0]).length
                contact_error=max(contact_error,error)
                if error>.00002 or frames["L"][1].dot(up.cross(forward))<.9999 or frames["L"][2].dot(up)<.9999:
                    raise ValueError("Shotgun foreend support contact differs "+name)
    for a,i,b,j in (("ShotgunRaise",0,"ShotgunRest",0),("ShotgunRaise",1,"ShotgunAim",0),
                    ("ShotgunLower",0,"ShotgunAim",0),("ShotgunLower",1,"ShotgunRest",0),
                    ("ShotgunFire",0,"ShotgunAim",0),("ShotgunFire",1,"ShotgunAim",0),
                    ("ShotgunReload",0,"ShotgunAim",0),("ShotgunReload",1,"ShotgunAim",0)):
        for bone in endpoints[a][i]:
            if max(abs(v) for row in endpoints[a][i][bone]-endpoints[b][j][bone] for v in row)>.00002:
                raise ValueError("Shotgun action endpoint mismatch "+a+"/"+bone)
    measured=[]
    rig.animation_data.action=builder.result.actions["ShotgunReload"].action
    for second,kind in RELOAD_STOPS:
        if not kind.endswith("seat"): continue
        bpy.context.scene.frame_set(round(second*FPS)); bpy.context.view_layer.update()
        hand=rig.pose.bones["hand.R"]; delta=hand.matrix@hand.bone.matrix_local.inverted()
        centre,axis,palm=builder.hand_frame("R"); up=(delta.to_3x3()@axis).normalized(); forward=(delta.to_3x3()@palm).cross(up).normalized()
        expected,_,_=reload_target(kind,delta@centre,up,forward)
        hand=rig.pose.bones["hand.L"]; delta=hand.matrix@hand.bone.matrix_local.inverted()
        error=(delta@builder.hand_frame("L")[0]-expected).length
        if error>.00002: raise ValueError("Shotgun shell seat contact mismatch "+kind)
        measured.append(dict(seconds=second,phase=kind,contact_error_m=round(error,7)))
    print("SHOTGUN GROUNDED ENDPOINTS / FOREEND CONTACT / WRISTS / SHELL SEATS OK",flush=True)
    return dict(maximum_wrist_degrees={k:round(v,4) for k,v in maximum_wrist.items()},maximum_support_error_m=round(contact_error,7),shell_seats=measured)


def verify_mechanism(model_dir):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(model_dir/"Shotgun.fbx"),use_anim=False)
    parts={obj.name.split(".")[0]:obj for obj in bpy.data.objects if obj.type=="EMPTY"}
    for name in ("SupportGrip","MuzzleLeft","MuzzleRight","ChamberLeft","ChamberRight","ShellLeft","ShellRight"):
        if parts[name].parent!=parts["Hinge"]: raise ValueError("Shotgun hinged child detached "+name)
    before={name:obj.matrix_world.translation.copy() for name,obj in parts.items()}
    pivot=parts["Hinge"]; matrix=pivot.matrix_world.copy(); p=matrix.translation.copy()
    turn=Matrix.Rotation(math.radians(-BREAK_DEGREES),4,"X")
    pivot.matrix_world=Matrix.Translation(p)@turn@Matrix.Translation(-p)@matrix
    bpy.context.view_layer.update()
    for name in ("MuzzleLeft","MuzzleRight","ChamberLeft","ChamberRight"):
        expected=p+turn.to_3x3()@(before[name]-p)
        if (parts[name].matrix_world.translation-expected).length>.00001: raise ValueError("Shotgun hinge axis mismatch "+name)
    if (parts["Grip"].matrix_world.translation-before["Grip"]).length>.00001: raise ValueError("Shotgun hinge moved grip")
    print("SHOTGUN EXPORTED BREAK HINGE / TWO CHAMBERS / FIXED GRIP OK",flush=True)


def verify_bank(path,reference):
    rig,actions=combat.import_bank(path)
    if set(actions)!={n for n,_,_ in CLIPS}: raise ValueError("Shotgun bank contains missing/foreign clips")
    actual=motion_samples(rig,actions); position=angle=0.
    for name,track in reference.items():
        for source,imported in zip(track,actual[name]):
            for bone,a in source.items():
                b=imported[bone]; position=max(position,(a.translation-b.translation).length)
                cosine=min(1.,abs(a.to_quaternion().normalized().dot(b.to_quaternion().normalized())))
                angle=max(angle,math.degrees(2.*math.acos(cosine)))
    if position>.0002 or angle>.08: raise ValueError(f"Shotgun published bank differs {position}m/{angle}deg")
    print("SHOTGUN PUBLISHED BONE MOTION ROUND TRIP OK",flush=True)


def motion_samples(rig,actions):
    result={}
    for name,duration,_ in CLIPS:
        rig.animation_data.action=actions[name]; first,last=actions[name].frame_range; track=[]
        for index in range(round(duration*20)+1):
            frame=first+(last-first)*index/round(duration*20)
            bpy.context.scene.frame_set(math.floor(frame),subframe=frame%1.); bpy.context.view_layer.update()
            track.append({bone.name:rig.matrix_world@bone.matrix for bone in rig.pose.bones})
        result[name]=track
    return result


def preview(model_dir,source_dir,destination):
    """Bounded review with the actual hero geometry, materials and hand morphs."""
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/"ArtSource/Combat/CombatActions.blend"))
    combat.pack_hero_atlases()
    with bpy.data.libraries.load(str(source_dir/"ShotgunActions.blend"),link=False) as (available,selected):
        selected.actions=[name for name in available.actions if name in {n for n,_,_ in CLIPS}]
    actions={action.name.split(".")[0]:action for action in selected.actions}; rig=bpy.data.objects["RIG_Player"]
    config=common.BuildConfig(None,None,None,None,None,None,None,1.75,20260919,"apose")
    builder=combat.CombatBuilder(config,hero.DEFAULT_FACE_ATLAS,hero.DEFAULT_CLOTHING_ATLAS)
    builder.result=common.BuildResult(root=rig.parent,rig=rig,collections={},materials={},parts=[])
    for obj in bpy.data.objects:
        if obj.type=="MESH" and obj.data.shape_keys is not None:
            shape=obj.data.shape_keys.key_blocks.get("CylindricalGrip")
            if shape is not None: shape.value=SUPPORT_WEIGHT if obj.name.endswith(".L") else 1.
    before=set(bpy.data.objects); bpy.ops.import_scene.fbx(filepath=str(model_dir/"Shotgun.fbx"),use_anim=False)
    gun=next(obj for obj in bpy.data.objects if obj not in before and obj.parent is None); imported=gun.matrix_world.copy()
    hinge=next(obj for obj in gun.children_recursive if obj.name.split(".")[0]=="Hinge"); rest=hinge.matrix_basis.copy()
    before=set(bpy.data.objects); bpy.ops.import_scene.fbx(filepath=str(model_dir/"Shell.fbx"),use_anim=False)
    shell=next(obj for obj in bpy.data.objects if obj not in before and obj.parent is None); shell_imported=shell.matrix_world.copy()
    scene=bpy.context.scene; scene.render.engine="BLENDER_EEVEE"; scene.render.resolution_x=960; scene.render.resolution_y=720
    scene.render.resolution_percentage=100; scene.render.image_settings.file_format="PNG"
    scene.camera.data.type="ORTHO"; scene.camera.data.ortho_scale=1.8
    destination.mkdir(parents=True,exist_ok=True)
    for name,second in (("ShotgunRest",0.),("ShotgunAim",0.),("ShotgunFire",.1),("ShotgunReload",.65),("ShotgunReload",1.6),("ShotgunReload",2.2)):
        rig.animation_data.action=actions[name]; scene.frame_set(round(second*FPS)); bpy.context.view_layer.update()
        hand=rig.pose.bones["hand.R"]; delta=hand.matrix@hand.bone.matrix_local.inverted()
        centre,axis,palm=builder.hand_frame("R"); up=(delta.to_3x3()@axis).normalized(); forward=(delta.to_3x3()@palm).cross(up).normalized()
        rotation=Matrix((forward.cross(up),forward,up)).transposed().to_4x4()
        gun.matrix_world=Matrix.Translation(delta@centre)@rotation@imported
        hinge.matrix_basis=rest.copy(); bpy.context.view_layer.update()
        if name=="ShotgunReload":
            pivot=hinge.matrix_world.copy(); point=pivot.translation.copy()
            turn=Quaternion(forward.cross(up),math.radians(-BREAK_DEGREES)).to_matrix().to_4x4()
            hinge.matrix_world=Matrix.Translation(point)@turn@Matrix.Translation(-point)@pivot
        for obj in gun.children_recursive:
            if obj.name.startswith("MuzzleFlash"): obj.hide_render=True
            if obj.name.startswith("ShellLeft") or obj.name.startswith("ShellRight"):
                obj.hide_render=name=="ShotgunReload"
        holding=name=="ShotgunReload" and second in (1.6,2.2)
        for obj in shell.children_recursive: obj.hide_render=not holding
        if holding:
            hand=rig.pose.bones["hand.L"]; delta=hand.matrix@hand.bone.matrix_local.inverted()
            centre,axis,palm=builder.hand_frame("L"); bore=(delta.to_3x3()@axis).normalized(); up=(delta.to_3x3()@palm).normalized()
            rotation=Matrix((bore.cross(up),bore,up)).transposed().to_4x4()
            shell.matrix_world=Matrix.Translation(delta@centre)@rotation@Matrix.Translation(Vector((0.,-SHELL_GRIP[2],0.)))@shell_imported
        scene.camera.location=(-3.5,-1.4,1.6); common.look_at(scene.camera,Vector((0.,-.20,1.0)))
        scene.render.filepath=str(destination/(name+"-"+str(round(second*FPS))+".png"))
        bpy.ops.render.render(write_still=True)
        print("SHOTGUN REVIEW FRAME "+name+"/"+str(second),flush=True)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--model-dir",type=Path,default=ROOT/"Assets/Resources/CombatShotgun")
    parser.add_argument("--source-dir",type=Path,default=ROOT/"ArtSource/Combat")
    parser.add_argument("--validate-only",action="store_true")
    parser.add_argument("--preview-dir",type=Path)
    parser.add_argument("--preview-only",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    if args.preview_only:
        if args.preview_dir is None: raise ValueError("Preview-only requires --preview-dir")
        preview(args.model_dir,args.source_dir,(ROOT/args.preview_dir).resolve())
        return
    items=make_items(); data=payload(items); builder=author_actions()
    data["actions"]["animation_signature"]=combat.action_curve_signature(builder,tuple(n for n,_,_ in CLIPS))
    data["actions"]["contact_validation"]=validate_actions(builder)
    reference=motion_samples(builder.result.rig,{n:r.action for n,r in builder.result.actions.items()})
    manifest=args.model_dir/"CombatShotgun3D.json"
    if args.validate_only:
        if json.loads(manifest.read_text(encoding="utf8"))!=json.loads(json.dumps(data)): raise ValueError("Shotgun manifest differs from deterministic rebuild")
    else:
        args.model_dir.mkdir(parents=True,exist_ok=True); args.source_dir.mkdir(parents=True,exist_ok=True)
        common.export_animation_fbx(args.model_dir/"ShotgunActions.fbx",builder.result)
        common.save_blend(args.source_dir/"ShotgunActions.blend")
        kit.SURFACES=SURFACES; roots=kit.build_objects(items)
        common.save_blend(args.source_dir/"CombatShotgun.blend")
        for item,root in zip(items,roots): kit.export(root,args.model_dir/(item.name+".fbx"))
        manifest.write_text(json.dumps(data,indent=2)+"\n",encoding="utf8")
    kit.verify_fbx(args.model_dir,data); verify_mechanism(args.model_dir); verify_bank(args.model_dir/"ShotgunActions.fbx",reference)
    if args.preview_dir: preview(args.model_dir,args.source_dir,(ROOT/args.preview_dir).resolve())
    print("COMBAT SHOTGUN DETERMINISM / PUBLISHED ASSETS OK",flush=True)


if __name__=="__main__": main()
