"""Deterministic isolated corpse palettes and NpcHumanV2 bone-only action bank.

The ordinary worker's topology/rest skeleton remains untouched. This pack is
CombatTest-only and never enters DefaultNpcCatalog. Run with run-blender.py's
staged --output-dir/--source-dir mappings; failed generation publishes nothing.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import io
import json
import math
from pathlib import Path
import struct
import sys
import wave

import bpy
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("zombie_combat", ROOT / "tools/build-combat-test-3d-model.py")
combat = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = combat
spec.loader.exec_module(combat)
common = combat.common
from atlas_kit import PixelCanvas, read_generated_png

FPS = 100
CATCH_SECONDS=6.4
CATCH_FPS=30
CATCH_CLIP="ZombieCatchFlop"
CATCH_STARTS=(.19,.83,1.46,2.13,2.89,3.61,4.39,5.22)
CLIPS = (("ZombieIdle", 4., True), ("ZombieWalk", 1.6, True),
         ("ZombieAttackRight", 1.77, False), ("ZombieAttackLeft", 1.77, False),
         ("ZombieHit", .45, False), ("ZombieLoadAdjust", 2.4, False),
         ("ZombieWatch", 2.8, False))
WALK_DISTANCE = .72
WALK_SECONDS = 1.6
WALK_ROOT_KEYS=((0.,0.),(.10,.03),(.30,.17),(.42,.44),(.50,.50),(.60,.53),(.80,.67),(.92,.94),(1.,1.))
PREPARE, ACTIVE, RECOVER = .6, .22, .95
OUT = ROOT / "Assets/Resources/Combat"
SOURCE = ROOT / "ArtSource/CombatZombie"
# Actual bone-local coordinates (Y follows the chest, -Z is the worker's back).
# The crate's socket stores the complete production chest rest matrix as well.
CRATE_CENTRE = Vector((0., -.220, -.100))
CRATE_SIZE = Vector((1.24, 1.20, .70))
RAMS=((Vector((-.38,.348,-.178)),Vector((.38,.348,-.178)),.032),
      (Vector((-.38,.348,-.351)),Vector((.38,.348,-.351)),.032))
HEAD_BURSTS={"ZombieIdle":((1.05,(12.,22.,-8.)),(2.53,(-10.,-24.,7.))),
    "ZombieWalk":((.47,(11.,-21.,8.)),(1.275,(-12.,23.,-7.))),
    "ZombieLoadAdjust":((.96,(-12.,22.,7.)),),
    "ZombieWatch":((1.05,(13.,-24.,-8.)),(1.22,(-6.,10.,4.))),
    "ZombieAttackRight":((.25,(-10.,18.,6.)),),"ZombieAttackLeft":((.25,(-10.,-18.,-6.)),),
    "ZombieHit":((.11,(12.,-18.,7.)),)}
CATCH = ((-.17,.00,-.22,.85,13.,-12.),(.12,.51,-.31,.91,-16.,14.),
         (.03,-.17,-.42,.91,-8.,-8.),(-.03,.60,-.52,.94,19.,9.),
         (-.18,.20,-.65,.93,-14.,-17.),(.23,.13,-.64,.84,67.,19.),
         (-.04,.47,-.77,.94,-9.,-16.),(.05,.05,-.80,.92,12.,18.))
CATCH = tuple((x,y-.52,z,length,yaw,roll) for x,y,z,length,yaw,roll in CATCH)
CATCH = tuple((x,y+.08,z+.35,length,yaw,roll) for x,y,z,length,yaw,roll in CATCH)
FUSION_PATHS = (((-.18,.16,.025),(-.23,.25,.060),(-.32,.34,.070),(-.42,.38,.055)),
                ((.18,.14,.015),(.25,.26,.080),(.34,.34,.095),(.43,.38,.060)),
                ((0.,-.23,.050),(.05,-.35,.140),(-.04,-.46,.240),(0.,-.54,.265)))


def smooth(value):
    t = min(1., max(0., value))
    return t*t*t*(10.-15.*t+6.*t*t)


def surge(value):
    t = min(1., max(0., value))
    return t*t*(3.-2.*t)


def load_builder():
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / "ArtSource/VillageLife/StationWorker.blend"))
    rig = bpy.data.objects["RIG_Player"]
    bpy.context.view_layer.objects.active=rig
    bpy.ops.object.mode_set(mode="EDIT")
    for name in ("spine","chest","neck","head","clavicle.L","clavicle.R"):
        rig.data.edit_bones[name].use_connect=False
    bpy.ops.object.mode_set(mode="OBJECT")
    config = common.BuildConfig(None, None, None, None, None, None, None, 1.75, 20261013, "apose")
    builder = combat.CombatBuilder(config, combat.hero.DEFAULT_FACE_ATLAS, combat.hero.DEFAULT_CLOTHING_ATLAS)
    builder.result = common.BuildResult(root=rig.parent, rig=rig, collections={}, materials={}, parts=[])
    builder.points = builder.create_pose_points()
    rig.animation_data_create().action = None
    builder._reset_pose()
    builder.footwork_support_targets = {s: rig.data.bones["foot."+s].head_local.copy() for s in ("L", "R")}
    builder.footwork_support_targets["L"].x=.325
    builder.footwork_support_targets["R"].x=-.325
    builder.footwork_support_rotations = {s: rig.data.bones["foot."+s].matrix_local.to_quaternion() for s in ("L", "R")}
    builder.hinge_references = common.calibrate_hinge_references(rig)
    builder.deformation = []
    # Only this actual production rest skeleton is needed for a bone-only
    # export. Removing scene copies avoids reevaluating the worker's modular
    # garments and authored joint modifiers on every dense IK sample.
    for obj in list(bpy.data.objects):
        if obj not in (rig, rig.parent): bpy.data.objects.remove(obj, do_unlink=True)
    bpy.context.view_layer.update()
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    rig.parent.name = "ROOT_Player"
    bpy.context.scene.render.fps = FPS
    common.ANIMATION_FPS = FPS
    return builder


def rotation(bone, delta):
    bone.matrix = Matrix.Translation(bone.head.copy()) @ (delta @ bone.matrix.to_quaternion()).to_matrix().to_4x4()
    bpy.context.view_layer.update()


def translate(bone,delta):
    matrix=bone.matrix.copy();matrix.translation+=delta;bone.matrix=matrix
    bpy.context.view_layer.update()


def score(seconds, keys):
    """Authored pause/snap/hold timing, never a random Animator speed change."""
    for (a, x), (b, y) in zip(keys, keys[1:]):
        if seconds <= b:
            return x+(y-x)*smooth((seconds-a)/(b-a))
    return keys[-1][1]


def head_burst(kind,seconds):
    result=Vector((0.,0.,0.))
    for start,rotation_degrees in HEAD_BURSTS[kind]:
        weight=score(seconds,((start,0.),(start+.04,1.),(start+.13,1.),(start+.19,0.)))
        result+=Vector(rotation_degrees)*weight
    return result


def pose_limp_arm(builder, side):
    rig = builder.result.rig
    upper, fore, hand = (rig.pose.bones[n+"."+side] for n in ("upper_arm", "forearm", "hand"))
    chest = rig.pose.bones["chest"].matrix
    shoulder=upper.head.copy()
    sign=1. if side=="L" else -1.
    target=chest@Vector((sign*(.38 if side=="L" else .39),.425,.075 if side=="L" else .105))
    axis=(target-shoulder).normalized()
    rest=upper.bone
    q=(rest.tail_local-rest.head_local).normalized().rotation_difference(axis)@rest.matrix_local.to_quaternion()
    upper.matrix=Matrix.Translation(shoulder)@q.to_matrix().to_4x4();bpy.context.view_layer.update()
    elbow=upper.tail.copy()
    down=Vector((0.,0.,-1.))
    rest=fore.bone
    q=(rest.tail_local-rest.head_local).normalized().rotation_difference(down)@rest.matrix_local.to_quaternion()
    elongation=1.12 if side=="L" else 1.24
    fore.matrix=Matrix.Translation(elbow)@q.to_matrix().to_4x4()@Matrix.Diagonal(Vector((.90,elongation,.90,1.)))
    bpy.context.view_layer.update()
    wrist=fore.tail.copy()
    rest=hand.bone
    q=Quaternion(down,math.radians(18. if side=="L" else -24.))@(rest.tail_local-rest.head_local).normalized().rotation_difference(down)@rest.matrix_local.to_quaternion()
    hand.matrix=Matrix.Translation(wrist)@q.to_matrix().to_4x4();bpy.context.view_layer.update()


def foot_target(builder, side, seconds):
    t = seconds/WALK_SECONDS
    start,end=(.60,.92) if side=="L" else (.10,.42)
    swing=min(1.,max(0.,(t-start)/(end-start)))
    stance=not start<t<end
    lift=math.sin(swing*math.pi)**2 if not stance else 0.
    ankle = builder.support(side)
    # Long planted support, then a short low drag. Root metres cancel exactly
    # throughout stance; both soles bear the load for 36% of the slow cycle.
    ankle.y += WALK_DISTANCE*(score(t,WALK_ROOT_KEYS)-smooth(swing))+(-.16 if side=="L" else .11)
    ankle.z += (.023 if side == "L" else .030)*lift
    return ankle, stance, lift


def attack_foot(builder, side, name, seconds):
    ankle = builder.support(side)
    if not PREPARE < seconds < PREPARE+ACTIVE:
        return ankle, True, 0.
    t = (seconds-PREPARE)/ACTIVE
    root = .7*surge(t)
    leading = side == ("L" if name == "ZombieAttackLeft" else "R")
    if leading:
        swing = min(1.,t/.5)
        travelled = .7*smooth(swing)
        stance = t >= .5
    else:
        swing = max(0.,(t-.5)/.5)
        travelled = .7*smooth(swing)
        stance = t <= .5
    ankle.y += root-travelled
    lift = math.sin(swing*math.pi)**2
    ankle.z += (.050 if leading else .045)*lift
    return ankle, stance, lift


def pose(builder, kind, seconds):
    rig = builder.result.rig
    rig.animation_data.action = None
    builder._reset_pose()
    phase = seconds/WALK_SECONDS if kind == "ZombieWalk" else 0.
    load = score(seconds, ((0.,0.),(.9,-.5),(1.9,-1.),(2.18,.35),(2.9,.35),(3.9,0.),(4.,0.))) if kind == "ZombieIdle" else 0.
    if kind == "ZombieLoadAdjust":
        load = score(seconds, ((0.,0.),(.95,-1.),(1.25,.5),(1.75,.5),(2.4,0.)))
    # The feet keep their exact world support trajectory. Weight is jerked up
    # through the torso only after each dragging step has found its floor.
    gait = score(phase, ((0.,0.),(.10,-.4),(.42,-.8),(.50,-1.),(.60,-.2),(.92,-.8),(1.,0.))) if kind == "ZombieWalk" else 0.
    sway = math.sin(phase*math.tau) if kind == "ZombieWalk" else 0.
    prep = follow = hit = shoulder = 0.
    if kind.startswith("ZombieAttack"):
        prep = smooth(seconds/PREPARE)*(1.-smooth((seconds-PREPARE)/ACTIVE))
        follow = smooth((seconds-PREPARE)/ACTIVE)*(1.-smooth((seconds-PREPARE-ACTIVE)/RECOVER))
        shoulder = 1. if kind == "ZombieAttackLeft" else -1.
    if kind == "ZombieHit":
        hit = smooth(seconds/.09)*(1.-smooth((seconds-.09)/.36))
    pelvis = rig.pose.bones["pelvis"]
    matrix = pelvis.matrix.copy()
    walking_drop = .025 if kind == "ZombieWalk" else 0.
    step_load = .100*math.sin(min(1.,max(0.,(seconds-PREPARE)/ACTIVE))*math.pi)**2 if kind.startswith("ZombieAttack") else 0.
    matrix.translation += Vector((.012*sway+.018*shoulder*follow, -.020*follow+.035*hit,
        -.195-walking_drop-step_load+.018*load+.012*gait-.012*prep-.025*follow+
        (.003*math.sin(phase*math.tau*4.) if kind=="ZombieWalk" else 0.)))
    pelvis.matrix = matrix
    bpy.context.view_layer.update()
    # Retain the production rest rig and skin. Translations compress the real
    # weighted human torso without scaling the crate or its inherited frame.
    translate(rig.pose.bones["spine"],(rig.pose.bones["spine"].head-pelvis.head).normalized()*-.045)
    rotation(rig.pose.bones["spine"], Quaternion(Vector((1., 0., 0.)), math.radians(65.-5.*load-3.*gait-7.*prep+6.*follow-6.*hit)))
    translate(rig.pose.bones["chest"],(rig.pose.bones["chest"].head-rig.pose.bones["spine"].head).normalized()*-.070)
    rotation(rig.pose.bones["chest"], Quaternion(Vector((0., 1., 0.)), math.radians(4.+2.*sway+9.*shoulder*(prep+follow))))
    rotation(rig.pose.bones["chest"], Quaternion(Vector((1., 0., 0.)), math.radians(15.-2.*load+3.*follow)))
    translate(rig.pose.bones["neck"],(rig.pose.bones["neck"].head-rig.pose.bones["chest"].head).normalized()*-.055)
    rotation(rig.pose.bones["neck"],Quaternion(Vector((1.,0.,0.)),math.radians(-28.)))
    translate(rig.pose.bones["head"],(rig.pose.bones["head"].head-rig.pose.bones["neck"].head).normalized()*-.020)
    translate(rig.pose.bones["head"],rig.pose.bones["chest"].matrix.to_3x3()@Vector((0.,.215,.110)))
    chest_frame=rig.pose.bones["chest"].matrix
    for side in ("L","R"):
        clavicle=rig.pose.bones["clavicle."+side]
        translate(clavicle,chest_frame.to_3x3()@Vector((-.025 if side=="L" else .015,.025,-.085 if side=="L" else -.035)))
        rotation(clavicle,Quaternion(chest_frame.to_3x3()@Vector((0.,1.,0.)),math.radians(18. if side=="L" else -8.)))
    watch = score(seconds, ((0.,0.),(.55,0.),(.78,1.),(1.75,1.),(1.96,-.4),(2.32,-.4),(2.8,0.))) if kind == "ZombieWatch" else 0.
    head=rig.pose.bones["head"]
    # The neck remains hyperextended against the collapsed torso. Retain the
    # ordinary human head basis, but make the actual face look world-forward.
    burst=head_burst(kind,seconds)
    target=Quaternion(Vector((0.,0.,1.)),math.radians(10.*watch+burst.y))@Quaternion(Vector((0.,1.,0.)),math.radians(-8.*watch+burst.z))@Quaternion(Vector((1.,0.,0.)),math.radians(3.*hit+2.*follow+burst.x))@head.bone.matrix_local.to_quaternion()
    head.matrix=Matrix.Translation(head.head.copy())@target.to_matrix().to_4x4()
    bpy.context.view_layer.update()
    for side in ("L", "R"):
        pose_limp_arm(builder, side)
    for side in ("L", "R"):
        ankle = builder.support(side)
        foot_rotation = builder.footwork_support_rotations[side]
        if kind == "ZombieWalk":
            ankle, _, lift = foot_target(builder, side, seconds)
            foot_rotation = Quaternion(Vector((1., 0., 0.)), math.radians(-8.*lift)) @ foot_rotation
        elif kind.startswith("ZombieAttack"):
            ankle, _, lift = attack_foot(builder, side, kind, seconds)
            foot_rotation = Quaternion(Vector((1., 0., 0.)), math.radians(-7.*lift)) @ foot_rotation
        builder.recovery_leg(side, ankle, (0., -1., 0.), foot_rotation)
    return builder.snapshot_pose()


def author(builder):
    for name, duration, loop in CLIPS:
        if name in builder.result.actions:
            bpy.data.actions.remove(builder.result.actions.pop(name).action)
        count = round(duration*FPS)
        keys = []
        for f in range(count+1):
            keys.append((f/count, pose(builder, name, duration*f/count)))
            if f and f % 100 == 0: print(f"{name} {f}/{count} source poses",flush=True)
        builder._create_action(name, "combat_zombie", duration, loop, count, FPS, keys)
        print("Authored "+name, flush=True)


def crate_atlas():
    canvas = PixelCanvas(512, 256)
    for y in range(256):
        for x in range(256):
            grain = 5.*math.sin(x*.29+math.sin(y*.071)*1.2)+3.*math.sin(x*.91+y*.013)
            noise = ((x*73+y*193+x*y*11)%31-15)*.24
            if y < 192:
                n = round(76.+grain+noise-13.*math.exp(-((y-130.)/25.)**2))
                colour = (n+9,n+12,n+5,255)
            else:
                n = round(57.+noise+4.*math.sin(x*.31+y*.19))
                rust = max(0.,math.sin(x*.14+math.sin(y*.18))*math.cos(y*.28))
                colour = (n+round(29*rust),n+round(9*rust),n-round(8*rust),255)
            canvas.put(x,y,colour)
    # Long faded cuts, dark water tide marks and small bright worn fibres.
    for i in range(23):
        x = (i*47+19)%256
        y = (i*71+17)%175
        canvas.line(x,y,min(255,x+1),min(191,y+14+i%19),(111,116,100,255))
    for y in range(256):
        for x in range(256,512):
            noise=((x*73+y*193+x*y*11)%31-15)*.35
            mottle=math.sin(x*.17+math.sin(y*.11))*math.sin(y*.21)*5.
            if y<128:
                if x<384:
                    scale=3. if ((x//4+(y//4)%2)%3==0 and y%4==0) else 0.
                    base=(83.+mottle+scale,95.+mottle+scale,87.+mottle)
                else:base=(137.+mottle,140.+mottle,117.+mottle)
            elif x<384:
                vein=4.*math.exp(-((x-298.-5.*math.sin(y*.055))/3.)**2)
                base=(101.+mottle-vein,109.+mottle,98.+mottle+vein)
            elif y<192:
                base=(16.,23.,20.) if x<448 else (66.+mottle,76.+mottle,68.+mottle)
            else:base=(48.+mottle,59.+mottle,52.+mottle) if x<448 else (151.,155.,137.)
            canvas.put(x,y,tuple(max(0,min(255,round(v+noise))) for v in base)+(255,))
    return canvas.png_bytes()


def crate_geometry():
    """Slatted fish box. All dimensions are actual production chest-local m."""
    bp = combat.kit.bp
    parts = []
    board_count=core_count=0
    def add(geometry,role):
        verts,faces=geometry
        if bp.signed_volume(geometry)<0.:faces=[tuple(reversed(f)) for f in faces]
        if bp.signed_volume((verts,faces)) <= 1e-11:raise ValueError("Degenerate fused crate solid")
        parts.append(((verts,faces),role))
    def box(centre,size,metal=False,core=False):
        nonlocal board_count,core_count
        geometry = bp.u_box(centre,size,min(.004,min(size)*.18))
        warped=[]
        for xx,yy,zz in geometry[0]:
            depth=min(1.,max(0.,(-zz-.10)/.70))
            warped.append((xx*(1.+.035*depth*(.8+.2*math.sin(yy*6.))),
                CRATE_CENTRE.y+(yy-CRATE_CENTRE.y)*(1.+.025*depth*math.cos(xx*4.)),
                zz+.010*depth*math.sin(xx*5.+yy*4.)))
        geometry=(warped,geometry[1])
        if core or metal:
            core_count+=1;part=f"Core{core_count:02}"
        else:
            board_count+=1;part=f"Board{board_count:02}"
        add(geometry,("Metal" if metal else "Timber")+"@"+part)
    x,y,z = CRATE_CENTRE
    w,h,d = CRATE_SIZE
    for i in range(7):
        xx=x+(i-3)*w/7.
        if abs(xx)<.24:
            # A real central opening lets the compressed human pelvis emerge
            # through the box floor; no floor plate bisects his living body.
            box((xx,-.685,z+d/2-.025),(w/7.-.008,.27,.050))
        else:box((xx,y,z+d/2-.025),(w/7.-.008,h-.018,.050))
    # Two real underside braces sit under the hands. They belong to the crate,
    # rather than asking human arms to span the unreachable enlarged corners.
    for xx in (-.393,.393):box((xx,-.08,.212),(.092,.29,.054))
    for side in (-1.,1.):
        for end in (-1.,1.):
            box((side*(w/2-.037),y+end*(h/2-.037),z),(.074,.074,d),core=True)
        for level in range(4):
            zz=z+d/2-.082-level*.173
            box((side*(w/2-.019),y,zz),(.038,h-.065,.112))
            if side<0.:
                box((0.,y+side*(h/2-.019),zz),(w-.066,.038,.112))
            elif level==1:
                # The head and raised shoulder masses force a broad opening
                # through the lower front wall, with actual timber reveals.
                for sign in (-1.,1.):box((sign*.4635,.361,zz),(.247,.038,.112))
            elif level==0:
                # The lowest front board is unbroken: the human is wedged
                # behind the timber, with his legs leaving through the floor.
                box((0.,.361,zz),(w-.066,.038,.112))
            else:
                # The two straight, thick front boards own the visible ram.
                # Their authored capsules sit fully in this real wood.
                board_count+=1
                add(bp.u_box((0.,.345,zz),(w-.066,.070,.096),.004),f"Timber@Board{board_count:02}")
        box((side*(w/2-.030),y,z-d/2+.030),(.060,h,.060))
        box((0.,y+side*(h/2-.030),z-d/2+.030),(w-.060,.060,.060))
        for end in (-1.,1.):
            box((side*(w/2+.002),y+end*(h/2-.082),z),(.012,.050,d+.008),True)
    for fish_index,(px,py,pz,length,yaw,roll) in enumerate(CATCH):
        rotation=Quaternion(Vector((0.,0.,1.)),math.radians(yaw))@Quaternion(Vector((1.,0.,0.)),math.radians(roll))
        origin=Vector((px,py,pz))
        def fish_add(geometry,role):
            add(([tuple(origin+rotation@(Vector(v)*length)) for v in geometry[0]],geometry[1]),role+"@"+str(fish_index))
        profile=((-0.42,.013,.018),(-.31,.062,.065),(-.14,.108,.094),
                 (.05,.115,.102),(.23,.089,.080),(.36,.069,.065),(.46,.027,.038),(.49,.007,.018))
        count=10
        vertices=[(xx,ry*math.cos(i*math.tau/count),rz*math.sin(i*math.tau/count)) for xx,ry,rz in profile for i in range(count)]
        faces=[tuple(reversed(range(count))),tuple(range(len(vertices)-count,len(vertices)))]
        for row in range(len(profile)-1):
            for i in range(count):
                a=row*count+i;b=row*count+(i+1)%count
                faces.append((a,b,b+count,a+count))
        fish_add((vertices,faces),"FishSide")
        # A complete forked tail and three fins have thickness and outward faces.
        def fin(points,thickness=.009,axis=2):
            vertices=[]
            for offset in (-thickness*.5,thickness*.5):
                for p in points:
                    v=list(p);v[axis]+=offset;vertices.append(tuple(v))
            n=len(points);faces=[tuple(reversed(range(n))),tuple(range(n,n*2))]
            faces.extend((i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n))
            fish_add((vertices,faces),"FishFin")
        fin(((-.39,-.034,0.),(-.57,-.155,0.),(-.535,0.,0.),(-.57,.155,0.),(-.39,.034,0.),(-.37,0.,0.)))
        fin(((-.19,0.,-.075),(-.03,0.,-.205),(.17,0.,-.090)),axis=1)
        for sign in (-1.,1.):
            fin(((.18,sign*.075,-.01),(-.055,sign*.205,.01),(-.02,sign*.075,.025)))
            # Clouded eye on each real head, with a dark rim and gill folds.
            fish_add(combat.kit.ellipsoid((.36,sign*.061,-.029),(.070,.029,.065),8,4),"FishEyeDark")
            fish_add(combat.kit.ellipsoid((.365,sign*.073,-.032),(.040,.012,.039),8,4),"FishEyeCloud")
            fish_add(combat.kit.ellipsoid((.462,0.,-.014),(.041,.058,.012),8,4),"FishDark")
            for j in range(2):
                fish_add(combat.kit.ellipsoid((.22-j*.025,sign*(.086+j*.003),-.021),(.009,.009,.102-j*.014),6,4),"FishDark")
        fish_add(combat.kit.ellipsoid((.02,0.,.070),(.56,.15,.055),10,4),"FishBelly")
    # Short rooted folds, never straps: each broad end enters torso and its
    # narrower end grows around the embedded bottom frame of the box.
    for path in FUSION_PATHS:
        points=[Vector(p) for p in path];vertices=[];count=8
        for row,p in enumerate(points):
            tangent=(points[min(row+1,len(points)-1)]-points[max(0,row-1)]).normalized()
            first=tangent.cross(Vector((0.,1.,0.))).normalized();second=tangent.cross(first).normalized()
            width=(.110,.076,.085,.064)[row];depth=(.058,.042,.058,.044)[row]
            for i in range(count):
                angle=i*math.tau/count
                irregular=1.+.13*math.sin(i*2.4+row*1.7)
                vertices.append(tuple(p+(first*math.cos(angle)*width+second*math.sin(angle)*depth)*irregular))
        faces=[tuple(reversed(range(count))),tuple(range(len(vertices)-count,len(vertices)))]
        for row in range(len(points)-1):
            for i in range(count):
                a=row*count+i;b=row*count+(i+1)%count
                faces.append((a,b,b+count,a+count))
        add((vertices,faces),"FusionSkin")
        add(combat.kit.ellipsoid(tuple(points[0]),(.210,.185,.110),10,5),"FusionSkin")
        add(combat.kit.ellipsoid(tuple(points[-1]),(.150,.130,.100),8,4),"FusionSkin")
    return parts


def crate_uv(point,normal,role):
    role=role.split("@")[0]
    axis=max(range(3),key=lambda i:abs(normal[i]))
    plane=[i for i in range(3) if i!=axis]
    uu=(point[plane[0]]+.75)*.60+.05
    vv=(point[plane[1]]+1.00)*.40
    # PNG row zero is the top; UV v zero is the bottom. Timber occupies
    # the upper 192 rows and hardware the lower 64 rows of the atlas.
    if role in ("Timber","Metal"):
        return (min(.995,max(.005,uu))*.5,.01+min(.23,max(0.,vv*.3)) if role=="Metal" else .255+min(.735,max(.005,vv)))
    regions={"FishSide":(.505,.745,.505,.995),"FishBelly":(.755,.995,.505,.995),
             "FusionSkin":(.505,.745,.005,.495),"FishEyeDark":(.755,.870,.255,.495),
             "FishFin":(.880,.995,.255,.495),"FishDark":(.755,.870,.005,.245),
             "FishEyeCloud":(.880,.995,.005,.245)}
    u0,u1,v0,v1=regions[role]
    return (u0+(u1-u0)*min(.99,max(.01,uu)),v0+(v1-v0)*min(.99,max(.01,vv)))


def crate_mesh_name(role):
    return "ZombieFusionMesh" if role=="FusionSkin" else "ZombieCatchMesh" if role.startswith("Fish") else "ZombieCrateMesh"


def crate_uv_signature(parts):
    values=[]
    for (points,faces),metal in parts:
        for face in faces:
            a,b,c=(Vector(points[i]) for i in face[:3])
            normal=(b-a).cross(c-a).normalized()
            values.append([[round(v,8) for v in crate_uv(points[i],normal,metal)] for i in face])
    return hashlib.sha256(json.dumps(values,separators=(",",":")).encode()).hexdigest()


def part_contract(parts):
    """Conserved rigid solids and fish islands; no mesh is duplicated to detach."""
    boards=[];cores=[];fish=[];wood_vertex=0
    for (points,faces),role in parts:
        if crate_mesh_name(role)!="ZombieCrateMesh":continue
        name=role.split("@")[1]
        minimum=Vector(tuple(min(p[i] for p in points) for i in range(3)))
        maximum=Vector(tuple(max(p[i] for p in points) for i in range(3)))
        pivot=(minimum+maximum)*.5
        record=dict(name=name,bone=name if name.startswith("Board") else "CrateCore",collision_frame=name,
            pivot_chest_local_blender=list(pivot),bounds_min_bone_local_blender=list(minimum-pivot),
            bounds_max_bone_local_blender=list(maximum-pivot),bounds_min_marker=name+"BoundsMin",bounds_max_marker=name+"BoundsMax",
            vertex_start=wood_vertex,vertex_count=len(points),triangle_count=sum(len(f)-2 for f in faces))
        wood_vertex+=len(points)
        if name.startswith("Board"):
            normal=Vector((0.,0.,0.))
            if pivot.z<-.39:normal.z=-1.
            elif pivot.z>.15:normal.z=1.
            elif abs(pivot.x)>.55:normal.x=1. if pivot.x>0. else -1.
            else:normal.y=1. if pivot.y>CRATE_CENTRE.y else -1.
            record.update(outward_marker=name+"Outward",outward_bone_local_blender=list(normal*.1),detachable=True)
            boards.append(record)
        else:cores.append(record)
    for index,(px,py,pz,length,yaw,roll) in enumerate(CATCH):
        name=f"Fish{index+1:02}"
        orientation=Quaternion(Vector((0.,0.,1.)),math.radians(yaw))@Quaternion(Vector((1.,0.,0.)),math.radians(roll))
        origin=Vector((px,py,pz))
        points=[orientation.inverted()@(Vector(v)-origin) for (g,role) in parts
            if role.startswith("Fish") and int(role.split("@")[1])==index for v in g[0]]
        fish.append(dict(name=name,bone=name,collision_frame=name,pivot_chest_local_blender=list(origin),
            rest_rotation_quaternion=list(orientation),bounds_min_bone_local_blender=[min(v[i] for v in points) for i in range(3)],
            bounds_max_bone_local_blender=[max(v[i] for v in points) for i in range(3)],
            bounds_min_marker=name+"BoundsMin",bounds_max_marker=name+"BoundsMax",animated_bones=[f"Catch{index+1}"+s for s in ("Body","Tail","Head")],
            vertex_count=len(points)))
    return boards,cores,fish


def crate_payload(builder, parts, texture):
    chest = builder.result.rig.data.bones["chest"].matrix_local
    vertices = [Vector(p) for (geom,_) in parts for p in geom[0]]
    world = [chest@p for p in vertices]
    def swapped(v): return [round(v[i],7) for i in (0,2,1)]
    def low(points): return Vector(tuple(min(p[i] for p in points) for i in range(3)))
    def high(points): return Vector(tuple(max(p[i] for p in points) for i in range(3)))
    digest = hashlib.sha256(json.dumps(parts,separators=(",",":")).encode()).hexdigest()
    boards,cores,fish=part_contract(parts)
    meshes=[]
    for name in ("ZombieCrateMesh","ZombieCatchMesh","ZombieFusionMesh"):
        subset=[g for g,role in parts if crate_mesh_name(role)==name]
        points=[chest@Vector(p) for g in subset for p in g[0]]
        meshes.append(dict(name=name,vertex_count=sum(len(g[0]) for g in subset),
            triangle_count=sum(sum(len(f)-2 for f in g[1]) for g in subset),
            bounds_min=swapped(low(points)),bounds_max=swapped(high(points))))
    for a,b,radius in RAMS:
        if abs(a.x)+radius>(CRATE_SIZE.x-.066)*.5 or abs(b.x)+radius>(CRATE_SIZE.x-.066)*.5 or \
            abs(a.y-b.y)>.000001 or a.y-radius<.310 or a.y+radius>.380001 or radius>.048:
            raise ValueError("Ram capsule is outside its actual thick front board")
    ram_owners=[]
    for a,b,r in RAMS:
        matches=[part["name"] for part in boards if all(part["pivot_chest_local_blender"][i]+part["bounds_min_bone_local_blender"][i]-.00001<=v[i]<=
            part["pivot_chest_local_blender"][i]+part["bounds_max_bone_local_blender"][i]+.00001 for v in (a,b) for i in range(3))]
        if len(matches)!=1:raise ValueError("Ram rail does not belong to one real board: "+str(matches))
        ram_owners.append(matches[0])
    return dict(generator="tools/build-combat-zombie-3d-model.py",version="1.5.0",test_only=True,
        units="metres",attachment_bone="chest",socket="ChestSocket",mesh="ZombieCrateMesh",
        mesh_count=3,meshes=meshes,fish_count=len(CATCH),fusion_bridge_count=len(FUSION_PATHS),
        fused_to_body=True,detachable=False,fish_state="eight conserved skinned fish islands, asynchronous flops with separate physics motion roots",
        wood_skinned=True,skinned_mesh_count=2,board_parts=boards,core_parts=cores,fish_parts=fish,
        board_count=len(boards),core_count=len(cores),skeleton_bone_count=len(boards)+33,
        physical_root_animation="BoardNN/FishNN/CrateCore have no source keys; importer retains only CatchNBody/Tail/Head curves",
        body_embedded_in_centre=True,ram_contact="two capsules inside the two actual straight front crossbars",
        ram_segments=[dict(a="RamBar"+str(i)+"A",b="RamBar"+str(i)+"B",radius_anchor="RamRadius"+str(i),
            chest_local_blender_a=list(a),chest_local_blender_b=list(b),radius_m=r,owner_board=ram_owners[i-1]) for i,(a,b,r) in enumerate(RAMS,1)],
        ram_capsules_inside_wood=True,overloaded=True,collider_chest_local_blender_center=list(CRATE_CENTRE),
        collider_chest_local_blender_size=list(CRATE_SIZE),collider_scope="whole wooden envelope; body apertures approximated solid; catch and fusion have no separate collision",
        lowest_front_board="initially unbroken across the wedged torso; conserved detachable plank",catch_animation=dict(clip=CATCH_CLIP,duration_seconds=CATCH_SECONDS,loop=True,
            rig="RIG_ZombieCatch",bone_count=32,animated_bone_count=24,source_fps=CATCH_FPS,mesh="ZombieCatchMesh",bounds_frame="CatchBounds"),
        atlas_size=[512,256],surface_roles=sorted({role.split("@")[0] for _,role in parts}),
        texture="ZombieCrateAtlas",material="ZombieCrateTimber",triangle_count=sum(sum(len(f)-2 for f in g[1]) for g,_ in parts),
        bounds_min=swapped(low(world)),bounds_max=swapped(high(world)),
        chest_local_blender_bounds_min=list(low(vertices)),chest_local_blender_bounds_max=list(high(vertices)),
        chest_rest_blender_matrix=[[round(v,9) for v in row] for row in chest],
        socket_alignment="Align imported ChestSocket world matrix to live chest; preserve imported model scale and parent with worldPositionStays true",
        collision_box="CollisionBox; BoundsMin/BoundsMax are real imported corners in its own frame",
        hands="retained rig forearms and limp hands; no carrying grip or lock markers",
        geometry_signature=digest,uv_signature=crate_uv_signature(parts),texture_sha256=hashlib.sha256(texture).hexdigest(),
        winding="all individual bevelled solids positive",determinism="two independent geometry builds match")


def build_crate(builder, parts, texture_path):
    chest = builder.result.rig.data.bones["chest"].matrix_local.copy()
    root = bpy.data.objects.new("ZombieCrate",None)
    bpy.context.collection.objects.link(root)
    socket = bpy.data.objects.new("ChestSocket",None)
    bpy.context.collection.objects.link(socket)
    socket.parent=root; socket.matrix_local=chest
    boards,cores,fish=part_contract(parts)
    armature=bpy.data.armatures.new("ZombieCatchSkeleton")
    catch_rig=bpy.data.objects.new("RIG_ZombieCatch",armature);bpy.context.collection.objects.link(catch_rig)
    catch_rig.parent=socket
    bpy.context.view_layer.objects.active=catch_rig;catch_rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    core=armature.edit_bones.new("CrateCore");core.head=(0.,0.,0.);core.tail=(0.,.1,0.)
    for part in boards:
        bone=armature.edit_bones.new(part["name"]);bone.head=part["pivot_chest_local_blender"]
        bone.tail=bone.head+Vector((0.,.1,0.));bone.parent=core
    for index,(px,py,pz,length,yaw,roll) in enumerate(CATCH):
        rotation=Quaternion(Vector((0.,0.,1.)),math.radians(yaw))@Quaternion(Vector((1.,0.,0.)),math.radians(roll))
        origin=Vector((px,py,pz))
        motion=armature.edit_bones.new(f"Fish{index+1:02}")
        motion.matrix=Matrix.Translation(origin)@rotation.to_matrix().to_4x4();motion.length=.1
        motion.parent=core
        for role,start,end in (("Body",0.,.12),("Tail",-.20,-.48),("Head",.20,.48)):
            bone=armature.edit_bones.new(f"Catch{index+1}{role}")
            bone.head=origin+rotation@Vector((start*length,0.,0.));bone.tail=origin+rotation@Vector((end*length,0.,0.))
            bone.parent=armature.edit_bones[f"Catch{index+1}Body"] if role!="Body" else motion
    bpy.ops.object.mode_set(mode="OBJECT")
    mat=bpy.data.materials.new("ZombieCrateTimber");mat.use_nodes=True
    mat.diffuse_color=(.27,.29,.24,1.)
    shader=mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Roughness"].default_value=.88
    image=bpy.data.images.load(str(texture_path),check_existing=True)
    image.pack()
    node=mat.node_tree.nodes.new("ShaderNodeTexImage");node.image=image;node.interpolation="Closest"
    mat.node_tree.links.new(node.outputs["Color"],shader.inputs["Base Color"])
    for mesh_name in ("ZombieCrateMesh","ZombieCatchMesh","ZombieFusionMesh"):
        vertices=[];faces=[];roles=[];fish_indices=[];wood_bones=[]
        for (points,polygons),role in parts:
            if crate_mesh_name(role)!=mesh_name:continue
            offset=len(vertices);vertices.extend(points)
            fish_indices.extend([int(role.split("@")[1]) if role.startswith("Fish") else -1]*len(points))
            if mesh_name=="ZombieCrateMesh":wood_bones.extend([role.split("@")[1] if "@Board" in role else "CrateCore"]*len(points))
            faces.extend(tuple(i+offset for i in f) for f in polygons)
            roles.extend([role]*len(polygons))
        mesh=bpy.data.meshes.new(mesh_name)
        mesh.from_pydata(vertices,[],faces);mesh.update(calc_edges=True)
        uv=mesh.uv_layers.new(name="UVMap")
        for polygon,role in zip(mesh.polygons,roles):
            for index in polygon.loop_indices:
                p=mesh.vertices[mesh.loops[index].vertex_index].co
                uv.data[index].uv=crate_uv(p,polygon.normal,role)
        mesh.materials.append(mat)
        obj=bpy.data.objects.new(mesh_name,mesh);bpy.context.collection.objects.link(obj);obj.parent=socket
        if mesh_name in ("ZombieCatchMesh","ZombieCrateMesh"):
            obj.parent=catch_rig
            names=(["CrateCore"]+[p["name"] for p in boards] if mesh_name=="ZombieCrateMesh" else
                [f"Catch{i+1}"+s for i in range(8) for s in ("Body","Tail","Head")])
            groups={name:obj.vertex_groups.new(name=name) for name in names}
            if mesh_name=="ZombieCrateMesh":
                for vertex,name in zip(mesh.vertices,wood_bones):groups[name].add([vertex.index],1.,"REPLACE")
                modifier=obj.modifiers.new("Conserved rigid plank skin","ARMATURE");modifier.object=catch_rig
                continue
            for vertex,index in zip(mesh.vertices,fish_indices):
                px,py,pz,length,yaw,roll=CATCH[index]
                rotation=Quaternion(Vector((0.,0.,1.)),math.radians(yaw))@Quaternion(Vector((1.,0.,0.)),math.radians(roll))
                local=rotation.inverted()@(vertex.co-Vector((px,py,pz)))/length
                tail=min(1.,max(0.,(-local.x-.08)/.27));head=min(1.,max(0.,(local.x-.12)/.25))
                for role,weight in (("Body",1.-tail-head),("Tail",tail),("Head",head)):
                    if weight>0.:groups[f"Catch{index+1}{role}"].add([vertex.index],weight,"REPLACE")
            modifier=obj.modifiers.new("Retained catch skin","ARMATURE");modifier.object=catch_rig
    bpy.context.view_layer.update()
    def bone_marker(name,bone_name,point):
        obj=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(obj)
        obj.parent=catch_rig;obj.parent_type="BONE";obj.parent_bone=bone_name
        bpy.context.view_layer.update()
        matrix=catch_rig.matrix_world@armature.bones[bone_name].matrix_local@Matrix.Translation(Vector(point))
        expected=matrix.translation
        obj.matrix_world=matrix
        bpy.context.view_layer.update()
        if (obj.matrix_world.translation-expected).length>.00001:raise ValueError("Bone marker parenting moved "+name)
        return obj
    for part in boards+fish:
        for key in ("min","max"):
            bone_marker(part["bounds_"+key+"_marker"],part["bone"],part["bounds_"+key+"_bone_local_blender"])
        if part in boards:bone_marker(part["outward_marker"],part["bone"],part["outward_bone_local_blender"])
    for part in cores:
        frame=bone_marker(part["name"],"CrateCore",part["pivot_chest_local_blender"])
        for key in ("min","max"):
            obj=bpy.data.objects.new(part["bounds_"+key+"_marker"],None);bpy.context.collection.objects.link(obj);obj.parent=frame
            obj.location=part["bounds_"+key+"_bone_local_blender"]
    owners=crate_payload(builder,parts,texture_path.read_bytes())["ram_segments"]
    for i,(a,b,radius) in enumerate(RAMS,1):
        for name,p in (("RamBar"+str(i)+"A",a),("RamBar"+str(i)+"B",b),("RamRadius"+str(i),a+Vector((0.,radius,0.)))):
            owner=owners[i-1]["owner_board"];pivot=Vector(next(v["pivot_chest_local_blender"] for v in boards if v["name"]==owner))
            bone_marker(name,owner,p-pivot)
    collision=bpy.data.objects.new("CollisionBox",None)
    bpy.context.collection.objects.link(collision);collision.parent=socket
    for name,sign in (("BoundsMin",-1.),("BoundsMax",1.)):
        anchor=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(anchor)
        anchor.parent=collision;anchor.location=CRATE_CENTRE+CRATE_SIZE*.5*sign
    bpy.context.view_layer.update()
    author_catch(root,catch_rig,next(o for o in root.children_recursive if o.name.split('.')[0]=="ZombieCatchMesh"))
    return root,socket


def author_catch(root,rig,mesh):
    """Finite, baked catch actions. No runtime oscillators or separate actors."""
    signatures=[]
    for repeat in range(2):
        if rig.animation_data and rig.animation_data.action:
            old=rig.animation_data.action;rig.animation_data.action=None;bpy.data.actions.remove(old)
        action=bpy.data.actions.new(CATCH_CLIP);action.use_fake_user=True
        action.use_cyclic=True;action.use_frame_range=True;action.frame_start=0.;action.frame_end=CATCH_SECONDS*FPS
        rig.animation_data_create().action=action
        signature=hashlib.sha256()
        for frame in range(round(CATCH_SECONDS*CATCH_FPS)+1):
            seconds=frame/CATCH_FPS
            for index,recipe in enumerate(CATCH):
                px,py,pz,length,yaw,roll=recipe
                orientation=Quaternion(Vector((0.,0.,1.)),math.radians(yaw))@Quaternion(Vector((1.,0.,0.)),math.radians(roll))
                bend=lift=wriggle=0.
                starts=[CATCH_STARTS[index]]
                if index<4:starts.append(CATCH_STARTS[index]+3.2)
                for start in starts:
                    age=seconds-start
                    if not 0.<age<.62:continue
                    pulse=score(age,((0.,0.),(.06,1.),(.15,-.45),(.22,.85),(.33,-.25),(.50,0.),(.62,0.)))
                    bend+=pulse*(1. if index%2 else -1.)
                    lift+=.035*math.sin(math.pi*min(1.,age/.35))**2 if age<.35 else 0.
                    wriggle+=math.sin(age*43.)*math.sin(math.pi*age/.62)**2
                for role,angle in (("Body",8.*bend),("Tail",23.*bend+9.*wriggle),("Head",-7.*bend)):
                    bone=rig.pose.bones[f"Catch{index+1}{role}"];bone.rotation_mode="QUATERNION"
                    axis=bone.bone.matrix_local.to_quaternion().inverted()@(orientation@Vector((0.,1.,0.)))
                    bone.rotation_quaternion=Quaternion(axis,math.radians(angle))
                    bone.location=bone.bone.matrix_local.to_3x3().inverted()@Vector((0.,0.,-lift)) if role=="Body" else Vector((0.,0.,0.))
                    bone.keyframe_insert("location",frame=seconds*FPS)
                    bone.keyframe_insert("rotation_quaternion",frame=seconds*FPS)
        for curve in common.iter_action_fcurves(action):
            for key in curve.keyframe_points:key.interpolation="LINEAR"
            signature.update(json.dumps([curve.data_path,curve.array_index,[[round(v,7) for v in key.co] for key in curve.keyframe_points]],separators=(",",":")).encode())
        signatures.append(signature.hexdigest())
    if signatures[0]!=signatures[1]:raise ValueError("Catch action generation is nondeterministic")
    minimum=Vector((100.,100.,100.));maximum=-minimum
    inverse_socket=rig.parent.matrix_world.inverted()
    maximum_motion=0.;first=None
    for frame in range(round(CATCH_SECONDS*FPS)+1):
        bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()
        evaluated=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get());skin=evaluated.to_mesh()
        points=[inverse_socket@mesh.matrix_world@v.co for v in skin.vertices]
        if first is None:first=[p.copy() for p in points]
        maximum_motion=max(maximum_motion,max((a-b).length for a,b in zip(first,points)))
        for p in points:
            for axis in range(3):minimum[axis]=min(minimum[axis],p[axis]);maximum[axis]=max(maximum[axis],p[axis])
        if frame==round(CATCH_SECONDS*FPS) and max((a-b).length for a,b in zip(first,points))>.00001:
            raise ValueError("Catch animation loop seam moved")
        evaluated.to_mesh_clear()
        if frame and frame%200==0:print(f"CATCH SKIN BOUNDS {frame}/{round(CATCH_SECONDS*FPS)}",flush=True)
    if maximum_motion<.03:raise ValueError("Catch never visibly flops")
    root["bp_catch_signature"]=signatures[-1];root["bp_catch_bounds_min"]=list(minimum);root["bp_catch_bounds_max"]=list(maximum)
    root["bp_catch_max_motion"]=maximum_motion
    bounds=bpy.data.objects.new("CatchBounds",None);bpy.context.collection.objects.link(bounds);bounds.parent=rig.parent
    for name,p in (("BBoxMin",minimum),("BBoxMax",maximum)):
        anchor=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(anchor);anchor.parent=bounds;anchor.location=p
    bpy.context.scene.frame_set(0);bpy.context.view_layer.update()


def append_catch_measurements(payload,root):
    payload["catch_animation"].update(signature=root["bp_catch_signature"],
        bounds_min_chest_local_blender=list(root["bp_catch_bounds_min"]),bounds_max_chest_local_blender=list(root["bp_catch_bounds_max"]),
        maximum_vertex_motion_m=root["bp_catch_max_motion"],loop_seam_m=0.,renderer_count=1,skin_bones=24,bounds_measurement_fps=FPS,
        bounds_min_marker="BBoxMin",bounds_max_marker="BBoxMax",motion_root_count=8)


def export_crate(root,path):
    bpy.ops.object.select_all(action="DESELECT");root.select_set(True)
    for obj in root.children_recursive:obj.select_set(True)
    bpy.context.view_layer.objects.active=root
    scene=bpy.context.scene
    scene_name=scene.name;scene_frames=(scene.frame_start,scene.frame_end)
    scene.name=CATCH_CLIP;scene.frame_start=0;scene.frame_end=round(CATCH_SECONDS*FPS)
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={"EMPTY","MESH","ARMATURE"},
        axis_forward="-Z",axis_up="Y",add_leaf_bones=False,bake_anim=True,
        bake_anim_use_all_bones=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,bake_anim_step=FPS/CATCH_FPS,bake_anim_simplify_factor=0.,
        bake_space_transform=False,mesh_smooth_type="FACE",use_custom_props=True)
    scene.name=scene_name;scene.frame_start,scene.frame_end=scene_frames


def verify_crate_fbx(builder,directory,payload,bank=None):
    """A real FBX round trip checks the socket frame, grips and world size."""
    before=set(bpy.data.objects)
    actions_before=set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=str(directory/"ZombieCrate.fbx"),use_anim=True)
    imported=set(bpy.data.objects)-before
    imported_actions=set(bpy.data.actions)-actions_before
    names=[a.name for a in imported_actions]
    takes={name.rsplit("|",1)[-1] for name in names}
    if takes!={CATCH_CLIP}:raise ValueError("Wrong exported catch take: "+str(names))
    fish_rig=next(o for o in imported if o.type=="ARMATURE")
    animation=fish_rig.animation_data.action
    duration=(animation.frame_range[1]-animation.frame_range[0])/bpy.context.scene.render.fps
    if abs(duration-CATCH_SECONDS)>.002:raise ValueError("Wrong exported catch duration: "+str(duration))
    skins=[o for o in imported if o.type=="MESH" and any(m.type=="ARMATURE" for m in o.modifiers)]
    if len(skins)!=2 or {o.name.split('.')[0] for o in skins}!={"ZombieCatchMesh","ZombieCrateMesh"}:
        raise ValueError("Crate must retain one conserved wood skin and one catch skin")
    skin_object=next(o for o in skins if o.name.startswith("ZombieCatchMesh"));sampled=[]
    sample_times=(0.,)+tuple(start+.07 for start in CATCH_STARTS)+(CATCH_SECONDS,)
    for second in sample_times:
        frame=animation.frame_range[0]+second*bpy.context.scene.render.fps
        bpy.context.scene.frame_set(math.floor(frame),subframe=frame%1);bpy.context.view_layer.update()
        evaluated=skin_object.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=evaluated.to_mesh()
        sampled.append([skin_object.matrix_world@v.co for v in mesh.vertices]);evaluated.to_mesh_clear()
    motion=max((a-b).length for sample in sampled[1:-1] for a,b in zip(sampled[0],sample))
    seam=max((a-b).length for a,b in zip(sampled[0],sampled[-1]))
    if motion<.03 or seam>.00001:raise ValueError(f"Catch FBX skin motion/seam failed {motion}/{seam}")
    bpy.context.scene.frame_set(0);bpy.context.view_layer.update()
    print("CATCH FBX TAKE "+str(takes)+f" duration={duration:.6f} oneSkin motion={motion:.6f} seam={seam:.8f}",flush=True)
    socket=next(o for o in imported if o.name.split('.')[0]=="ChestSocket")
    expected=builder.result.rig.data.bones["chest"].matrix_local
    error=max(abs(socket.matrix_world[i][j]-expected[i][j]) for i in range(4) for j in range(4))
    points=[obj.matrix_world@v.co for obj in imported if obj.type=="MESH" for v in obj.data.vertices]
    low=[min(p[i] for p in points) for i in (0,2,1)]
    high=[max(p[i] for p in points) for i in (0,2,1)]
    if error>.00001 or max(abs(a-b) for a,b in zip(low+high,payload["bounds_min"]+payload["bounds_max"]))>.0001:
        raise ValueError(f"Crate FBX socket/bounds mismatch: {error}/{low}/{high}")
    bpy.ops.import_scene.fbx(filepath=str(bank or directory/"ZombieActions.fbx"),use_anim=False)
    imported_rig=next(o for o in set(bpy.data.objects)-before-imported if o.type=="ARMATURE")
    rest=imported_rig.matrix_world@imported_rig.data.bones["chest"].matrix_local
    frame_error=max(abs(socket.matrix_world[i][j]-rest[i][j]) for i in range(4) for j in range(4))
    if frame_error>.0001:
        print("SOCKET",[list(r) for r in socket.matrix_world],"BONE",[list(r) for r in rest],"RIG",[list(r) for r in imported_rig.matrix_world],flush=True)
        raise ValueError(f"Crate socket differs from imported production bone frame: {frame_error}")
    def imported_object(name):return next(o for o in imported if o.name.split('.')[0]==name)
    for part in payload["board_parts"]+payload["core_parts"]+payload["fish_parts"]:
        name=part["name"]
        if name.startswith("Core"):
            frame=imported_object(name).matrix_world
        else:
            if name not in fish_rig.pose.bones:raise ValueError("Missing conserved rigid root "+name)
            frame=fish_rig.matrix_world@fish_rig.pose.bones[name].matrix
        for key in ("min","max"):
            marker=imported_object(part["bounds_"+key+"_marker"])
            local=frame.inverted()@marker.matrix_world.translation
            if (local-Vector(part["bounds_"+key+"_bone_local_blender"])).length>.0001:
                raise ValueError("Part FBX local bounds moved: "+name+" "+str(list(local)))
        if name.startswith("Board"):
            marker=imported_object(part["outward_marker"])
            if (frame.inverted()@marker.matrix_world.translation-Vector(part["outward_bone_local_blender"])).length>.0001:
                raise ValueError("Part outward direction moved: "+name)
    wood=next(o for o in skins if o.name.startswith("ZombieCrateMesh"))
    for vertex in wood.data.vertices:
        if len(vertex.groups)!=1 or abs(vertex.groups[0].weight-1.)>.00001:raise ValueError("Wood is not rigidly weighted")
    for index,segment in enumerate(payload["ram_segments"],1):
        for suffix in ("A","B"):
            marker=imported_object(f"RamBar{index}"+suffix)
            if marker.parent!=fish_rig or marker.parent_bone!=segment["owner_board"]:
                raise ValueError("Ram marker is not on its conserved board")
    def evaluated_points(obj):
        evaluated=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=evaluated.to_mesh()
        points=[obj.matrix_world@v.co for v in mesh.vertices];evaluated.to_mesh_clear();return points
    for obj,part in ((wood,next(p for p in payload["board_parts"] if p["name"]=="Board26")),(skin_object,payload["fish_parts"][0])):
        initial=evaluated_points(obj);bone=fish_rig.pose.bones[part["name"]];original=bone.matrix_basis.copy()
        bone.location+=Vector((.1,0.,0.));bpy.context.view_layer.update()
        moved=sum((a-b).length>.01 for a,b in zip(initial,evaluated_points(obj)))
        bone.matrix_basis=original;bpy.context.view_layer.update()
        if moved!=part["vertex_count"]:raise ValueError(f"Conserved island isolation failed {part['name']}: {moved}/{part['vertex_count']}")
    print("LOADER ROOT MOTION ISLANDS OK Board26=16 vertices Fish01=520 vertices; all other vertices retained",flush=True)
    print(f"LOADER CONSERVED PARTS OK boards={payload['board_count']} cores={payload['core_count']} fish=8 woodRigidVertices={len(wood.data.vertices)}",flush=True)
    for i,(a,b,radius) in enumerate(RAMS,1):
        for name,p in (("RamBar"+str(i)+"A",a),("RamBar"+str(i)+"B",b),("RamRadius"+str(i),a+Vector((0.,radius,0.)))):
            anchor=next(o for o in imported if o.name.split('.')[0]==name)
            if (anchor.matrix_world.translation-expected@p).length>.0001:raise ValueError("Exported ram contact moved")
    for obj in set(bpy.data.objects)-before:bpy.data.objects.remove(obj,do_unlink=True)
    for action in imported_actions:bpy.data.actions.remove(action)
    print(f"LOADER FBX SOCKET/BOUNDS/RAM CONTACTS OK {frame_error:.8f}",flush=True)


def preview(builder,crate_root,directory,source,motion=False):
    """Review the actual unchanged worker skin deformed by the new actions."""
    rig=builder.result.rig
    with bpy.data.libraries.load(str(ROOT/"ArtSource/VillageLife/StationWorker.blend"),link=False) as (data,loaded):
        loaded.objects=[name for name in data.objects if name not in ("RIG_Player","ROOT_Player")]
    meshes=[]
    for obj in loaded.objects:
        if obj is None or obj.type!="MESH":continue
        if obj.hide_render:continue
        obj.parent=rig.parent
        bpy.context.collection.objects.link(obj)
        for modifier in obj.modifiers:
            if modifier.type=="ARMATURE":modifier.object=rig
        meshes.append(obj)
    # Use the existing worker's textile atlas and this bank's pale face.
    face_path=directory/"ZombieFace1.png"
    face=bpy.data.images.load(str(face_path if face_path.is_file() else OUT/"ZombieFace1.png"),check_existing=True)
    for obj in meshes:
        for material in obj.data.materials:
            if not material or not material.use_nodes:continue
            for node in material.node_tree.nodes:
                if node.type=="TEX_IMAGE" and node.image and "Atlas" in node.image.name:node.image=face
    scene=bpy.context.scene;scene.render.engine="BLENDER_EEVEE"
    scene.render.resolution_x=560;scene.render.resolution_y=560;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format="PNG";scene.render.film_transparent=False
    scene.world.color=(.055,.060,.055)
    camera_data=bpy.data.cameras.new("LoaderReviewCamera")
    camera=bpy.data.objects.new("LoaderReviewCamera",camera_data);bpy.context.collection.objects.link(camera)
    scene.camera=camera;camera_data.lens=58
    for name,location,energy,size in (("LoaderKey",(-2.,-3.,4.),1000.,3.),("LoaderRim",(1.5,2.,3.),650.,2.)):
        data=bpy.data.lights.new(name,"AREA");data.energy=energy;data.shape="DISK";data.size=size
        obj=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(obj);obj.location=location
        obj.rotation_euler=(Vector((0.,0.,.8))-obj.location).to_track_quat("-Z","Y").to_euler()
    source.mkdir(parents=True,exist_ok=True)
    for name,clip,seconds,location in (("LoaderFront","ZombieWatch",1.3,(2.5,-3.4,1.8)),
        ("LoaderBack","ZombieIdle",1.72,(2.8,2.7,1.8)),("LoaderRam","ZombieAttackRight",.78,(4.4,-.8,1.95))):
        rig.animation_data.action=builder.result.actions[clip].action
        scene.frame_set(round(seconds*FPS));bpy.context.view_layer.update()
        camera.location=location;camera.rotation_euler=(Vector((0.,-.22,.79))-camera.location).to_track_quat("-Z","Y").to_euler()
        scene.render.filepath=str(source/(name+".png"));bpy.ops.render.render(write_still=True)
    if motion:
        frames=directory/"LoaderWalkFrames";frames.mkdir(parents=True,exist_ok=True)
        scene.render.resolution_x=384;scene.render.resolution_y=384
        rig.animation_data.action=builder.result.actions["ZombieWalk"].action
        camera.location=(2.5,-3.4,1.8);camera.rotation_euler=(Vector((0.,-.22,.79))-camera.location).to_track_quat("-Z","Y").to_euler()
        for frame in range(round(WALK_SECONDS*20)):
            scene.frame_set(round(frame/20*FPS));bpy.context.view_layer.update()
            scene.render.filepath=str(frames/f"Frame{frame:03}.png");bpy.ops.render.render(write_still=True)
        print("LOADER WALK MOTION FRAMES OK 20fps "+str(WALK_SECONDS)+"s",flush=True)


def measure(builder):
    rig = builder.result.rig
    records = []
    signature = hashlib.sha256()
    idle_endpoint=None
    for name, duration, loop in CLIPS:
        action = builder.result.actions[name].action
        for curve in common.iter_action_fcurves(action):
            if not curve.data_path.startswith('pose.bones['):
                raise ValueError("Zombie bank contains object motion")
            signature.update(json.dumps([name, curve.data_path, curve.array_index,
                [[round(v, 7) for v in key.co] for key in curve.keyframe_points]], separators=(",", ":")).encode())
        rig.animation_data.action = action
        support_error = loop_error = 0.
        first = None
        minimum_leg_gap = 100.
        max_knee = max_wrist_reach = hand_down_error = head_facing_error = cervical_arch = 0.
        head_base_error=head_speed=0.;previous_head=None
        forearm_lengths={s:[] for s in ("L","R")}
        minimum_supports = 2
        for f in range(round(duration*FPS)+1):
            second = f/FPS
            bpy.context.scene.frame_set(f)
            bpy.context.view_layer.update()
            if f == 0:
                first = {b.name: b.matrix_basis.copy() for b in rig.pose.bones}
                if name=="ZombieIdle":idle_endpoint=first
            if name in ("ZombieLoadAdjust","ZombieWatch") and f in (0,round(duration*FPS)):
                endpoint_error=max(abs(b.matrix_basis[i][j]-idle_endpoint[b.name][i][j]) for b in rig.pose.bones for i in range(4) for j in range(4))
                if endpoint_error>.00001:raise ValueError(f"Loader action no longer returns to idle: {name}/{endpoint_error}")
            if loop and f == round(duration*FPS):
                loop_error = max(abs(b.matrix_basis[i][j]-first[b.name][i][j]) for b in rig.pose.bones for i in range(4) for j in range(4))
            supports = 0
            for side in ("L", "R"):
                thigh, shin, foot = (rig.pose.bones[n+"."+side] for n in ("thigh", "shin", "foot"))
                target, stance = (foot_target(builder, side, second)[:2] if name == "ZombieWalk" else attack_foot(builder,side,name,second)[:2] if name.startswith("ZombieAttack") else (builder.support(side),True))
                if stance:
                    support_error = max(support_error, (foot.head-target).length)
                    supports += 1
                max_knee = max(max_knee, math.degrees((shin.head-thigh.head).angle(foot.head-shin.head)))
                upper, hand = rig.pose.bones["upper_arm."+side], rig.pose.bones["hand."+side]
                max_wrist_reach = max(max_wrist_reach, (hand.head-upper.head).length)
                hand_down_error=max(hand_down_error,math.degrees((hand.tail-hand.head).angle(Vector((0.,0.,-1.)))))
                fore=rig.pose.bones["forearm."+side]
                forearm_lengths[side].append((fore.tail-fore.head).length)
            head=rig.pose.bones["head"];neck=rig.pose.bones["neck"]
            face=head.matrix.to_quaternion()@head.bone.matrix_local.to_quaternion().inverted()@Vector((0.,-1.,0.))
            head_facing_error=max(head_facing_error,math.degrees(face.angle(Vector((0.,-1.,0.)))))
            if head_burst(name,second).length<.001:
                head_base_error=max(head_base_error,math.degrees(face.angle(Vector((0.,-1.,0.)))))
            current_head=head.matrix.to_quaternion()
            if previous_head is not None:
                angle=previous_head.rotation_difference(current_head).angle
                head_speed=max(head_speed,math.degrees(min(angle,math.tau-angle))*FPS)
            previous_head=current_head
            rest_relative=neck.bone.matrix_local.to_quaternion().inverted()@head.bone.matrix_local.to_quaternion()
            posed_relative=neck.matrix.to_quaternion().inverted()@head.matrix.to_quaternion()
            cervical_arch=max(cervical_arch,math.degrees(rest_relative.rotation_difference(posed_relative).angle))
            minimum_leg_gap = min(minimum_leg_gap, (rig.pose.bones["foot.L"].head-rig.pose.bones["foot.R"].head).length)
            minimum_supports = min(minimum_supports,supports)
        if hand_down_error>.1 or head_facing_error>55. or head_base_error>12. or head_speed<350.:
            raise ValueError(f"Loader limp hands/head facing failed {name}: {hand_down_error}, {head_facing_error}")
        if support_error > .001 or loop_error > .00001 or max_knee > 115. or minimum_leg_gap < .15 or minimum_supports < 1:
            raise ValueError(f"Zombie supports/anatomy failed {name}: {support_error}, {loop_error}, {max_knee}, {minimum_leg_gap}")
        records.append(dict(name=name, duration_seconds=duration, loop=loop, maximum_ground_support_error_m=support_error,
            maximum_loop_seam_error=loop_error, maximum_knee_flexion_degrees=max_knee,
            minimum_foot_separation_m=minimum_leg_gap, maximum_wrist_reach_m=max_wrist_reach,
            minimum_grounded_feet=minimum_supports,maximum_hand_axis_from_world_down_degrees=hand_down_error,
            maximum_face_from_world_forward_degrees=head_facing_error,maximum_base_face_from_world_forward_degrees=head_base_error,
            maximum_cervical_arch_degrees=cervical_arch,maximum_head_angular_speed_degrees_per_second=head_speed,
            forearm_lengths_m={s:[min(v),max(v)] for s,v in forearm_lengths.items()},
            head_bursts=[dict(start_seconds=start,snap_seconds=.04,hold_seconds=.09,return_seconds=.06,
                pitch_yaw_roll_degrees=list(rotation)) for start,rotation in HEAD_BURSTS[name]]))
    return dict(generator="tools/build-combat-zombie-3d-model.py", version="1.5.0", test_only=True,
        rig="NpcHumanV2", topology="retained_ordinary_worker", root_motion=False, animation_events=0,
        source_fps=FPS, walk_cycle_distance_m=WALK_DISTANCE, prepare_seconds=PREPARE,
        walk_duration_seconds=WALK_SECONDS,walk_double_support_fraction=.36,
        gait_root_travel=dict(interpolation="quintic_smootherstep",normalized_keys=[list(v) for v in WALK_ROOT_KEYS],
            right_swing=[.10,.42],left_swing=[.60,.92],owner_clock="advance phase by achieved authored root travel; no uniform clip speed substitute"),
        active_seconds=ACTIVE, recovery_seconds=RECOVER,attack_root_travel_m=.7,
        role="human_fused_through_fish_crate",attack_contact="two real front crossbar capsules",
        body_torso_compression_m=.115,neck_head_compression_m=.075,foot_centre_spacing_m=.65,
        arm_pose="retained asymmetric elongated forearms and limp downward hands, no crate grip",
        authoring_connections="torso/neck/head/clavicles disconnected for translation; production parents/rest matrices unchanged",
        head_pose="hyperextended cervical arch; human rest face basis points world-forward between authored jerks",
        crate_attachment="ZombieCrate/ChestSocket aligned to production chest",signature=signature.hexdigest(), clips=records)


def atlas(variant):
    # Reuse the authored worker's UV layout and textiles. The dead face and
    # skin are new pixels; no face geometry or joint surface is replaced.
    source = ROOT / ("Assets/Resources/VillageLife/StationWorkerAtlas.png" if variant == 0 else
        f"Assets/Resources/VillageLife/StationWorkerFace{variant+1:02d}Atlas.png")
    width, height, pixels = read_generated_png(source)
    canvas = PixelCanvas(width, height)
    canvas.pixels[:] = pixels
    for y in range(256):
        for x in range(384):
            i = (y*width+x)*4
            n = canvas.pixels[i]
            dirt = 16*math.exp(-((x%128-48-variant*13)/31.)**2-((y-163)/40.)**2)
            worn = max(95, round(n*.90-dirt))
            canvas.put(x, y, (worn, max(85,worn-5), max(80,worn-8), 255))
    # Face square sits in the bottom right exactly like the original atlas.
    for y in range(128):
        for x in range(128):
            i = ((y+384)*width+x+384)*4
            rgb = canvas.pixels[i:i+3]
            n = (rgb[0]*.3+rgb[1]*.5+rgb[2]*.2)
            canvas.put(x+384, y+384, (min(210,round(n*.94+22)), min(207,round(n*.99+19)), min(203,round(n*.93+19)), 255))
    for side, cx in enumerate((41+variant,87-variant)):
        canvas.ellipse(384+cx, 384+58+side, 13, 8, (71,76,72,255))
        canvas.ellipse(384+cx, 384+57+side, 8, 2, (130,136,124,255))
        canvas.line(384+cx-9,384+49+side,384+cx+8,384+48+side,(80,82,75,255),2)
        canvas.line(384+cx-9,384+66+side,384+cx+8,384+65+side,(122,129,119,255))
    canvas.line(435,484,463,485,(76,77,69,255),2)
    canvas.line(438,489,460,489,(126,129,116,255))
    # Local wounds remain muted and small, with a different side per variant.
    x = (415,470,443)[variant]
    y = (464,476,419)[variant]
    canvas.line(x,y,x+6,y+13,(105,79,72,255),2)
    canvas.line(x+1,y+1,x+7,y+12,(127,102,86,255))
    return canvas.png_bytes()


def audio(lunge):
    rate, duration = 22050, (.36 if lunge else 1.15)
    count = round(rate*duration)
    pcm = bytearray()
    for i in range(count):
        t = i/rate
        envelope = math.sin(math.pi*i/count)**2
        pitch = (79.-23.*t/duration if lunge else 60.+4.*math.sin(t*7.))
        voiced = sum(math.sin(math.tau*pitch*t*h)/h for h in (1.,2.,3.,5.,7.))*.35
        noise = (((i*1103515245+12345)&65535)/32767.5-1.)
        rasp = noise*.14*math.sin(math.tau*43.*t)**2
        value = (voiced+rasp)*envelope*(.55 if lunge else .42)
        pcm.extend(struct.pack("<h", round(min(1.,max(-1.,value))*32767)))
    output = io.BytesIO()
    with wave.open(output,"wb") as writer:
        writer.setnchannels(1);writer.setsampwidth(2);writer.setframerate(rate);writer.writeframes(pcm)
    return output.getvalue()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir",type=Path,default=OUT)
    parser.add_argument("--source-dir",type=Path,default=SOURCE)
    parser.add_argument("--validate-only",action="store_true")
    parser.add_argument("--inspect-rig",action="store_true")
    parser.add_argument("--inspect-poses",action="store_true")
    parser.add_argument("--no-preview",action="store_true")
    parser.add_argument("--inspect-attachment",action="store_true")
    parser.add_argument("--preview-only",action="store_true")
    parser.add_argument("--crate-only",action="store_true")
    parser.add_argument("--preview-dir",type=Path)
    parser.add_argument("--refresh-source",action="store_true")
    parser.add_argument("--inspect-contact",action="store_true")
    parser.add_argument("--pose-preview",action="store_true")
    parser.add_argument("--motion-preview",action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    args.output_dir=args.output_dir.resolve();args.source_dir=args.source_dir.resolve()
    builder = load_builder()
    if args.pose_preview:
        args.output_dir.mkdir(parents=True,exist_ok=True)
        texture=args.output_dir/"ZombieCrateAtlas.png";texture.write_bytes(crate_atlas())
        for name,duration,loop in CLIPS:
            times=sorted(set((0.,duration*.5,min(.78,duration),min(1.3,duration),min(1.72,duration),duration)))
            keys=[(t/duration,pose(builder,name,t)) for t in times]
            builder._create_action(name,"combat_zombie",duration,loop,round(duration*FPS),FPS,keys)
        root,socket=build_crate(builder,crate_geometry(),texture)
        constraint=socket.constraints.new("COPY_TRANSFORMS");constraint.target=builder.result.rig;constraint.subtarget="chest"
        common.save_blend(args.source_dir/"LoaderPosePreview.blend")
        preview(builder,root,args.output_dir,args.source_dir)
        return
    if args.inspect_contact:
        bpy.ops.wm.open_mainfile(filepath=str(args.source_dir/"ZombieActions.blend"))
        rig=bpy.data.objects["RIG_Player"]
        records=[]
        for name,seconds in (("ZombieIdle",0.),("ZombieAttackRight",PREPARE+ACTIVE*.5),("ZombieAttackRight",PREPARE+ACTIVE*.9)):
            rig.animation_data.action=bpy.data.actions[name]
            bpy.context.scene.frame_set(round(seconds*FPS));bpy.context.view_layer.update()
            records.append(dict(clip=name,seconds=seconds,shoulders_unity_axis_swap={side:[round(v,7) for v in
                (rig.pose.bones["upper_arm."+side].head.x,rig.pose.bones["upper_arm."+side].head.z,rig.pose.bones["upper_arm."+side].head.y)] for side in ("L","R")}))
        print("LOADER CONTACT SOURCE "+json.dumps(records),flush=True)
        mesh=bpy.data.objects.get("ZombieCrateMesh")
        if mesh is not None:
            geometry_records=[]
            for name in ("ZombieAttackRight","ZombieAttackLeft"):
                rig.animation_data.action=bpy.data.actions[name]
                maximum_box_ahead=maximum_catch_ahead=-100.
                for frame in range(round(PREPARE*FPS),round((PREPARE+ACTIVE)*FPS)+1):
                    bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()
                    side="R" if name.endswith("Right") else "L"
                    shoulder=rig.pose.bones["upper_arm."+side].head
                    box_forward=max(-(mesh.matrix_world@v.co).y for v in mesh.data.vertices)
                    catch=bpy.data.objects["ZombieCatchMesh"]
                    catch_forward=max(-(catch.matrix_world@v.co).y for v in catch.data.vertices)
                    maximum_box_ahead=max(maximum_box_ahead,box_forward+shoulder.y)
                    maximum_catch_ahead=max(maximum_catch_ahead,catch_forward+shoulder.y)
                geometry_records.append(dict(clip=name,maximum_box_ahead_of_leading_shoulder_m=maximum_box_ahead,
                    maximum_catch_ahead_of_leading_shoulder_m=maximum_catch_ahead,contact_sphere_forward_extent_m=.215))
            print("LOADER CONTACT GEOMETRY "+json.dumps(geometry_records),flush=True)
        return
    if args.crate_only:
        args.output_dir.mkdir(parents=True,exist_ok=True)
        data=crate_atlas();texture=args.output_dir/"ZombieCrateAtlas.png";texture.write_bytes(data)
        parts=crate_geometry();crate_root,_=build_crate(builder,parts,texture)
        payload=crate_payload(builder,parts,data)
        if payload!=crate_payload(builder,crate_geometry(),crate_atlas()):raise ValueError("Nondeterministic crate")
        append_catch_measurements(payload,crate_root)
        export_crate(crate_root,args.output_dir/"ZombieCrate.fbx")
        verify_crate_fbx(builder,args.output_dir,payload,OUT/"ZombieActions.fbx")
        (args.output_dir/"ZombieCrate.json").write_text(json.dumps(payload,indent=2)+"\n",encoding="utf8")
        bank_manifest_path=args.output_dir/"Zombie3D.json"
        bank_manifest=json.loads((bank_manifest_path if bank_manifest_path.is_file() else OUT/"Zombie3D.json").read_text(encoding="utf8"))
        bank_manifest["assets"]["ZombieCrateAtlas.png"]=hashlib.sha256(data).hexdigest()
        (args.output_dir/"Zombie3D.json").write_text(json.dumps(bank_manifest,indent=2)+"\n",encoding="utf8")
        return
    if args.preview_only:
        bpy.ops.wm.open_mainfile(filepath=str(args.source_dir/"ZombieActions.blend"))
        builder.result.rig=bpy.data.objects["RIG_Player"]
        builder.result.root=builder.result.rig.parent
        builder.result.actions={name:argparse.Namespace(action=bpy.data.actions[name]) for name,_,_ in CLIPS}
        previous=bpy.data.objects["ZombieCrate"]
        for obj in list(previous.children_recursive):bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.objects.remove(previous,do_unlink=True)
        before=set(bpy.data.objects)
        actions_before=set(bpy.data.actions)
        bpy.ops.import_scene.fbx(filepath=str(args.output_dir/"ZombieCrate.fbx"),use_anim=True)
        imported_fps=bpy.context.scene.render.fps/bpy.context.scene.render.fps_base
        if abs(imported_fps-FPS)>.0001:
            for action in set(bpy.data.actions)-actions_before:
                for curve in common.iter_action_fcurves(action):
                    for key in curve.keyframe_points:
                        key.co.x*=FPS/imported_fps;key.handle_left.x*=FPS/imported_fps;key.handle_right.x*=FPS/imported_fps
            bpy.context.scene.render.fps=FPS;bpy.context.scene.render.fps_base=1.
        imported=set(bpy.data.objects)-before
        crate_root=next(o for o in imported if o.name.split('.')[0]=="ZombieCrate")
        socket=next(o for o in imported if o.name.split('.')[0]=="ChestSocket")
        for obj in imported:
            if obj.type=="MESH":
                obj.data.materials.clear();obj.data.materials.append(bpy.data.materials["ZombieCrateTimber"])
        for node in bpy.data.materials["ZombieCrateTimber"].node_tree.nodes:
            if node.type=="TEX_IMAGE":
                node.image=bpy.data.images.load(str(args.output_dir/"ZombieCrateAtlas.png"),check_existing=False)
                node.image.pack()
        constraint=socket.constraints.new("COPY_TRANSFORMS");constraint.target=builder.result.rig;constraint.subtarget="chest"
        if args.refresh_source:common.save_blend(args.source_dir/"ZombieActions.blend")
        preview(builder,crate_root,args.output_dir,(args.preview_dir or args.source_dir).resolve(),args.motion_preview)
        return
    if args.inspect_attachment:
        pose(builder,"ZombieIdle",0.)
        args.output_dir.mkdir(parents=True,exist_ok=True)
        data=crate_atlas();texture=args.output_dir/"ZombieCrateAtlas.png";texture.write_bytes(data)
        parts=crate_geometry();crate_root,_=build_crate(builder,parts,texture)
        builder.result.rig.animation_data.action=None;builder._reset_pose()
        common.export_animation_fbx(args.output_dir/"ZombieActions.fbx",builder.result)
        export_crate(crate_root,args.output_dir/"ZombieCrate.fbx")
        verify_crate_fbx(builder,args.output_dir,crate_payload(builder,parts,data))
        return
    if args.inspect_rig:
        print(json.dumps({n:list(builder.result.rig.data.bones[n].head_local) for n in
            ("pelvis","spine","chest","neck","head","upper_arm.L","upper_arm.R","thigh.L","thigh.R","foot.L","foot.R")}),flush=True)
        print(json.dumps({n:{"matrix":[list(row) for row in builder.result.rig.data.bones[n].matrix_local],
            "parent":builder.result.rig.data.bones[n].parent.name if builder.result.rig.data.bones[n].parent else None,
            "length":builder.result.rig.data.bones[n].length} for n in ("chest","upper_arm.L","forearm.L","hand.L","hand.R")}),flush=True)
        return
    if args.inspect_poses:
        pose(builder,"ZombieIdle",0.)
        initial={n:b.matrix_basis.copy() for n,b in builder.result.rig.pose.bones.items()}
        pose(builder,"ZombieIdle",1.84)
        print("ARM CHANGE",json.dumps({n:max(abs(b.matrix_basis[i][j]-initial[n][i][j]) for i in range(4) for j in range(4))
            for n,b in builder.result.rig.pose.bones.items() if any(t in n for t in ("clavicle","upper_arm","forearm","hand"))}),flush=True)
        print("CLAVICLES",[(b.name,b.parent.name) for b in builder.result.rig.data.bones if "clavicle" in b.name],flush=True)
        for name,duration,_ in CLIPS:
            for t in (0.,duration*.25,duration*.5,duration*.75,duration):pose(builder,name,t)
        print("LOADER SOURCE POSES OK",flush=True)
        return
    author(builder)
    payload = measure(builder)
    author(builder)
    repeated = measure(builder)
    if payload != repeated: raise ValueError("Zombie action generation is nondeterministic")
    files = {f"ZombieFace{i+1}.png": atlas(i) for i in range(3)}
    files.update({"ZombieGroan.wav":audio(False),"ZombieLunge.wav":audio(True)})
    files["ZombieCrateAtlas.png"]=crate_atlas()
    parts=crate_geometry()
    crate_manifest=crate_payload(builder,parts,files["ZombieCrateAtlas.png"])
    if crate_manifest!=crate_payload(builder,crate_geometry(),crate_atlas()):
        raise ValueError("Loader crate generation is nondeterministic")
    payload["assets"] = {name:hashlib.sha256(data).hexdigest() for name,data in files.items()}
    manifest = args.output_dir/"Zombie3D.json"
    if args.validate_only:
        builder.result.rig.animation_data.action=None;builder._reset_pose()
        measured_root,_=build_crate(builder,parts,args.output_dir/"ZombieCrateAtlas.png")
        append_catch_measurements(crate_manifest,measured_root)
        if json.loads(manifest.read_text(encoding="utf8")) != json.loads(json.dumps(payload)):
            raise ValueError("Published zombie manifest differs from deterministic source")
        for name,data in files.items():
            if (args.output_dir/name).read_bytes()!=data: raise ValueError("Zombie palette/audio differs: "+name)
        if json.loads((args.output_dir/"ZombieCrate.json").read_text(encoding="utf8"))!=json.loads(json.dumps(crate_manifest)):
            raise ValueError("Published loader crate manifest differs")
        verify_crate_fbx(builder,args.output_dir,crate_manifest)
    else:
        args.output_dir.mkdir(parents=True,exist_ok=True)
        builder.result.rig.animation_data.action=None;builder._reset_pose()
        common.export_animation_fbx(args.output_dir/"ZombieActions.fbx",builder.result)
        for modifier,visible in builder.deformation: modifier.show_viewport=visible
        for name,data in files.items(): (args.output_dir/name).write_bytes(data)
        crate_root,socket=build_crate(builder,parts,args.output_dir/"ZombieCrateAtlas.png")
        append_catch_measurements(crate_manifest,crate_root)
        export_crate(crate_root,args.output_dir/"ZombieCrate.fbx")
        verify_crate_fbx(builder,args.output_dir,crate_manifest)
        constraint=socket.constraints.new("COPY_TRANSFORMS");constraint.target=builder.result.rig;constraint.subtarget="chest"
        common.save_blend(args.source_dir/"ZombieActions.blend")
        (args.output_dir/"ZombieCrate.json").write_text(json.dumps(crate_manifest,indent=2)+"\n",encoding="utf8")
        manifest.write_text(json.dumps(payload,indent=2)+"\n",encoding="utf8")
        if not args.no_preview:preview(builder,crate_root,args.output_dir,args.source_dir,args.motion_preview)
    print("COMBAT ZOMBIE ART CONTRACT OK "+payload["signature"],flush=True)


if __name__ == "__main__":
    main()
