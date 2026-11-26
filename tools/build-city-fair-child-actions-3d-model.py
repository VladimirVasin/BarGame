#!/usr/bin/env python3
"""Child-native fair actions on the independently authored 1.30 m skeleton.

No adult character or adult action bank is loaded. Pose contacts, planted
endpoints and the seated support are measured from the exported child's bones.
"""
from __future__ import annotations
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys
import bpy
from mathutils import Vector, Quaternion, Matrix

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from playground_geometry import (BEAM_END, BEAM_TOP, RUNG_END, RUNG_X, RUNG_Y, RUNGS,
                                 STEP_FIRST, STEP_LAST, STEP_TOP, SLIDE_END, SLIDE_RAIL,
                                 SLIDE_DECK, SLIDE_PROFILE, BRIDGE_LANE, BRIDGE_CROSS_TIME,
                                 SEESAW_DOCK, SEESAW_SEAT, SEESAW_PIVOT, SEESAW_GRIP, SEESAW_FOOT, BALANCE_STEP_TOP)
spec = importlib.util.spec_from_file_location("fair_child_body", ROOT / "tools/build-city-fair-child-3d-model.py")
body = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = body
spec.loader.exec_module(body)
OUT = ROOT / "Assets/Resources/City/FairChild/ChildActions.fbx"
MANIFEST = OUT.with_suffix(".json")
BLEND = ROOT / "ArtSource/City/FairChild/ChildActions.blend"
FPS = 24
CLIPS = (("Idle",4.,True),("Walk",1.25,True),("Turn",1.,False),("Stop",.5,False),
         ("GoodsEnter",1.25,False),("GoodsLoop",3.,True),("GoodsExit",1.25,False),
         ("ToyEnter",1.5,False),("ToyLoop",4.,True),("ToyExit",1.5,False),
         ("SitEnter",2.,False),("SitLoop",4.,True),("SitExit",2.,False),
         ("AdjustSleeve",2.5,False),("AdjustCap",2.5,False),("StreetWalk",1.,True),
         ("SwingEnter",1.5,False),("SwingPump",1.,False),("SwingExit",1.25,False),
         ("BarsCross",6.5,False),("BeamCross",5.5,False),("SlideCross",8.,False),
         ("SlideViaBridge",8. + BRIDGE_CROSS_TIME,False),("SeesawMount",1.5,False),
         ("BalanceStep",1.5,False))
STREET_CYCLE_METRES = .66


def smooth(t):
    t=max(0.,min(1.,t));return t*t*(3.-2.*t)
def src(p):return Vector(body.source(p))
def unity(p):return Vector((p.x,p.z,p.y))
def reset(rig):
    for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()
def rotate(rig,name,axis,degrees):
    b=rig.pose.bones[name];head=b.head.copy()
    q=Quaternion(src(axis),math.radians(degrees))
    b.matrix=Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ b.matrix
    bpy.context.view_layer.update()
def aim(rig,name,direction):
    b=rig.pose.bones[name];head=b.head.copy()
    q=(b.tail-head).rotation_difference(direction)
    b.matrix=Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ b.matrix
    bpy.context.view_layer.update()
def move_pelvis(rig,target):
    b=rig.pose.bones["pelvis"];m=b.matrix.copy();m.translation=src(target);b.matrix=m
    bpy.context.view_layer.update()
def pose_copy(rig):return {b.name:(b.location.copy(),b.rotation_quaternion.copy()) for b in rig.pose.bones}
def pose_apply(rig,pose):
    for name,(p,q) in pose.items():rig.pose.bones[name].location=p;rig.pose.bones[name].rotation_quaternion=q
    bpy.context.view_layer.update()
def blend(rig,a,b,t):
    pose_apply(rig,{name:(a[name][0].lerp(b[name][0],t),a[name][1].slerp(b[name][1],t)) for name in a})


def arm(rig,side,target,hand_direction=(0,-.08,1)):
    shoulder=rig.pose.bones["upper_arm."+side].head.copy()
    grip=rig.data.bones["SOCKET_Grip."+side];hand=rig.data.bones["hand."+side]
    offset=grip.head_local-hand.head_local
    direction=src(hand_direction).normalized()
    wrist=src(target)-direction*offset.length
    a=rig.data.bones["upper_arm."+side].length;b=rig.data.bones["forearm."+side].length
    delta=wrist-shoulder;distance=delta.length
    if distance>=a+b-.0005 or distance<=abs(a-b)+.0005:
        raise ValueError(f"Child {side} grip {tuple(target)} unreachable: wristdistance={distance:.6f}, arm={a+b:.6f}, shoulder={tuple(unity(shoulder))}")
    axis=delta.normalized();hint=src((-.4 if side=="L" else .4,-.2,-.4))
    pole=(hint-axis*hint.dot(axis)).normalized()
    along=(a*a-b*b+distance*distance)/(2*distance)
    elbow=shoulder+axis*along+pole*math.sqrt(max(0,a*a-along*along))
    aim(rig,"upper_arm."+side,elbow-shoulder)
    aim(rig,"forearm."+side,wrist-rig.pose.bones["forearm."+side].head)
    rotation=offset.rotation_difference(direction) @ hand.matrix_local.to_quaternion()
    rig.pose.bones["hand."+side].matrix=Matrix.LocRotScale(wrist,rotation,Vector((1,1,1)))
    bpy.context.view_layer.update()
    error=(rig.pose.bones["SOCKET_Grip."+side].head-src(target)).length
    if error>.0002:raise ValueError(f"Child {side} IK residual {error:.6f} at {tuple(target)}")


def street_foot(rig, side, phase):
    foot = rig.pose.bones["foot." + side]
    thigh = rig.pose.bones["thigh." + side]
    shin = rig.pose.bones["shin." + side]
    stance = .52
    swing = max(0., (phase - stance) / (1. - stance))
    height = .090 * math.sin(math.pi * swing) ** 2
    forward = STREET_CYCLE_METRES * (.5 * stance - phase + smooth(swing))
    target = src((-.079 if side == "L" else .079, .065 + height, forward))
    hip = thigh.head.copy()
    delta = target - hip
    distance = delta.length
    a, b = thigh.length, shin.length
    if not abs(a-b) < distance < a+b:
        raise ValueError(f"Street walk foot {side} cannot reach at phase {phase:.3f}")
    axis = delta.normalized()
    hint = src((0,0,1))
    pole = (hint - axis * hint.dot(axis)).normalized()
    along = (a*a - b*b + distance*distance) / (2*distance)
    knee = hip + axis*along + pole*math.sqrt(a*a - along*along)
    aim(rig, thigh.name, knee - hip)
    aim(rig, shin.name, target - shin.head)
    if (foot.head - target).length > .0002:
        raise ValueError("Street walk lost its authored foot position")
    pitch = 23 * math.sin(2 * math.pi * swing) * math.sin(math.pi * swing)
    rotation = Quaternion(src((1,0,0)), math.radians(pitch)) @ rig.data.bones[foot.name].matrix_local.to_quaternion()
    foot.matrix = Matrix.LocRotScale(foot.head.copy(), rotation, Vector((1,1,1)))
    bpy.context.view_layer.update()


def street_walk(rig, phase):
    # Each planted shoe travels backwards at exactly the route's cycle speed.
    # Catch the weight low, rise briskly over the supporting leg, then settle.
    step = (phase * 2) % 1.
    rise = math.sin(math.pi * step) ** 2
    swing = math.cos(math.tau * phase)
    move_pelvis(rig, (.016 * math.sin(math.tau * phase), .568 + .036 * rise, 0))
    rotate(rig, "chest", (0,1,0), -7 * swing)
    rotate(rig, "chest", (1,0,0), -3 + 2 * rise)
    for side, sign in (("L",1),("R",-1)):
        rotate(rig, "upper_arm." + side, (1,0,0), -26 * sign * swing)
        rotate(rig, "forearm." + side, (1,0,0), -11 - 8 * abs(swing))
        street_foot(rig, side, (phase + (0. if side == "L" else .5)) % 1.)


def held(rig,kind,t=0.):
    reset(rig);right_direction=(0,-.08,1)
    if kind=="Goods":
        rotate(rig,"spine",(1,0,0),-13)
        rotate(rig,"spine",(0,0,1),.7*math.sin(2*math.pi*t))
        rotate(rig,"head",(0,1,0),3*math.sin(2*math.pi*t))
        targets={"L":(-.17,.97,.32),"R":(.17,.97,.32)}
    elif kind=="Toy":
        rotate(rig,"spine",(1,0,0),-70)
        rotate(rig,"chest",(1,0,0),-10)
        rotate(rig,"head",(1,0,0),15)
        lift=max(0.,math.sin((t-.5)*2*math.pi)) if t>.5 else 0.
        yaw=math.radians(24*lift)
        right_direction=(math.sin(yaw),-.08,math.cos(yaw))
        targets={"L":(-.15,.565,.32),"R":(.09,.63+.065*lift,.40+.08*math.sin(2*math.pi*t))}
    elif kind=="Sit":
        move_pelvis(rig,(0,.57,-.54))
        for side in ("L","R"):
            aim(rig,"thigh."+side,src((0,-.04,.274)))
            aim(rig,"shin."+side,src((0,-.273,.02)))
            rotate(rig,"shin."+side,(1,0,0),(7 if side=="L" else -7)*math.sin(2*math.pi*t))
            foot=rig.pose.bones["foot."+side]
            foot.matrix=Matrix.LocRotScale(foot.head.copy(),rig.data.bones[foot.name].matrix_local.to_quaternion(),Vector((1,1,1)))
            bpy.context.view_layer.update()
        targets={"L":(-.13,.68,-.31),"R":(.13,.68,-.31)}
    else:raise ValueError(kind)
    for side,target in targets.items():arm(rig,side,target, right_direction if side=="R" else (0,-.08,1))
    if kind=="Sit":
        # A quiet sleeve adjustment remains seated; the pelvis never leaves its
        # support and both hands return exactly to the lap at the loop seam.
        gesture=math.sin(math.pi*t)**2
        left=Vector(targets["L"]).lerp(Vector((-.06,.80,-.30)),gesture)
        arm(rig,"L",left)
        cuff=unity(rig.pose.bones["forearm.L"].head*.2+rig.pose.bones["forearm.L"].tail*.8)
        cuff.x+=.035;cuff.z+=.035
        right=Vector(targets["R"]).lerp(cuff,gesture)
        arm(rig,"R",right)
        targets={"L":left,"R":right}
    return targets


def swing_pose(rig, pump):
    # Coordinates relative to the grounded dock, .50 m in front of the plank.
    # Both hands close on the suspension; only the legs and torso pump.
    move_pelvis(rig, (0, .74, -.50))
    rotate(rig, "spine", (1, 0, 0), 11 * pump)
    targets = {"L": (-.30, 1.06, -.50), "R": (.30, 1.06, -.50)}
    for side, target in targets.items():
        aim(rig, "thigh." + side, src((0, -.05, .27)))
        aim(rig, "shin." + side, src((0, -.27 + pump * .14, .01 + pump * .21)))
        foot = rig.pose.bones["foot." + side]
        rotation = rig.data.bones[foot.name].matrix_local.to_quaternion()
        foot.matrix = Matrix.LocRotScale(foot.head.copy(), rotation, Vector((1, 1, 1)))
        bpy.context.view_layer.update()
        arm(rig, side, target, (0, 1, .08))
    return targets


def ramp(time, start, end):
    return smooth((time - start) / (end - start))


def position_at(time, keys):
    for (begin, value), (end, following) in zip(keys, keys[1:]):
        if time <= end:
            return Vector(value).lerp(Vector(following), ramp(time, begin, end))
    return Vector(keys[-1][1])


def steps_at(time, moves):
    point = Vector((.065, 0))
    for start, end, height, z, lift in moves:
        target = Vector((.065 + height, z))
        if time < start:
            break
        if time < end:
            phase = (time - start) / (end - start)
            point = point.lerp(target, smooth(phase))
            point.x += lift * math.sin(math.pi * phase) ** 2
            break
        point = target
    return point


def place_boot(rig, side, target):
    upper = rig.pose.bones["thigh." + side]
    lower = rig.pose.bones["shin." + side]
    foot = rig.pose.bones["foot." + side]
    ankle = src(target)
    hip = upper.head.copy()
    axis = (ankle - hip).normalized()
    a, b = upper.length, lower.length
    distance = (ankle - hip).length
    if not abs(a - b) < distance < a + b:
        raise ValueError(f"Playground boot {side} cannot reach {target} from {tuple(unity(hip))}")
    hint = src((0, 0, 1))
    pole = (hint - axis * hint.dot(axis)).normalized()
    along = (a*a - b*b + distance*distance) / (2*distance)
    knee = hip + axis*along + pole*math.sqrt(max(0, a*a - along*along))
    aim(rig, upper.name, knee - hip)
    aim(rig, lower.name, ankle - lower.head)
    foot.matrix = Matrix.LocRotScale(ankle, rig.data.bones[foot.name].matrix_local.to_quaternion(), Vector((1,1,1)))
    bpy.context.view_layer.update()


def supported_hip_height(height, forward, feet):
    # Retain the authored boot contact if a straight leg would fall short.
    reach = .548
    for y, z in feet:
        limit = y + .015 + math.sqrt(max(0., reach*reach - (forward-z)**2 - .0006))
        height = min(height, limit)
    return height


def beam_cross(rig, time):
    moves = ([], [])
    for i in range(9):
        z = min((i + 1) * .34, BEAM_END)
        start = .16 + i * .58
        moves[i % 2].append((start, start + .48, BEAM_TOP if i < 7 else 0., z, .085))
    pelvis_keys = [(0., (.63, 0)), (.64, (.64, .14)), (1.22, (.81, .46))]
    pelvis_keys += [(.64 + i * .58, (.81, i * .34 + .15)) for i in range(2, 7)]
    pelvis_keys += [(4.70, (.63, 2.53)), (5.32, (.63, BEAM_END))]
    height, z = position_at(time, pelvis_keys)
    if time >= 5.32 or time <= 0.:
        move_pelvis(rig, (0, .63, BEAM_END if time >= 5.32 else 0.))
        return
    sway = math.sin(time * math.pi / .58) * math.sin(math.pi * time / 5.32)
    feet = [steps_at(time, track) for track in moves]
    move_pelvis(rig, (.014 * sway, supported_hip_height(height, z, feet), z))
    for i, (side, sign) in enumerate((("L", -1), ("R", 1))):
        boot_y, foot_z = feet[i]
        place_boot(rig, side, (sign * .066, boot_y, foot_z))
        spread = math.sin(math.pi * time / 5.32)
        rotate(rig, "upper_arm." + side, (0,0,1), sign*(37+14*sway*sign)*spread)
        rotate(rig, "forearm." + side, (1,0,0), -14*spread)
    rotate(rig, "chest", (0,0,1), 5*sway)
    rotate(rig, "head", (1,0,0), -9*math.sin(math.pi*time/5.32))


def bars_cross(rig, time):
    if time <= 0. or time >= 6.42:
        move_pelvis(rig, (0, .63, RUNG_END if time >= 6.42 else 0.))
        return 0., None
    hands = [RUNGS[0], RUNGS[0]]
    lift = [0., 0.]
    for index, rung in enumerate(RUNGS[1:]):
        side = index % 2
        start = 1.60 + index * .42
        if time < start:
            break
        phase = ramp(time, start, start + .38)
        hands[side] += (rung - hands[side]) * phase
        lift[side] = .095 * math.sin(math.pi * phase)
        if time < start + .38:
            break
    # The last reach is followed by one foot finding the exit step while
    # the other still hangs. Keep the last grips planted until that support.
    core_z = (hands[0] + hands[1]) * .5 - .006
    if time < 1.60:
        height, z = position_at(time, [(0.,(.63,0.)), (.64,(.67,.16)),
                                      (1.30,(.80,.46)), (1.60,(.79,core_z))])
    elif time <= 4.1:
        height, z = .79, core_z
    else:
        height, z = position_at(time, [(4.10,(.79,core_z)), (4.72,(.79,2.32)),
            (5.22,(.79,2.53)), (5.82,(.63,2.78)), (6.42,(.63,RUNG_END))])
    entry = (
        [(.15,.61,STEP_TOP,STEP_FIRST,.085), (1.30,1.62,.30,.54,.04)],
        [(.65,1.18,STEP_TOP,STEP_FIRST,.09), (1.30,1.62,.24,.45,.05)],
    )
    exit = (
        [(4.12,4.65,STEP_TOP,STEP_LAST,.07),(5.30,5.81,0.,RUNG_END,.06)],
        [(4.62,5.12,STEP_TOP,STEP_LAST,.095),(5.84,6.40,0.,RUNG_END,.07)],
    )
    weight = ramp(time, .63, 1.56) * (1. - ramp(time, 4.14, 4.64))
    targets = {}
    feet = []
    for i in range(2):
        if time < 1.62:
            boot_y, boot_z = steps_at(time, entry[i])
        elif time >= exit[i][0][0]:
            start, end, top, point, hop = exit[i][0]
            phase = ramp(time, start, end)
            if time <= end:
                boot_y = (.365 if i == 0 else .305) * (1-phase) + (.065+top)*phase + hop*math.sin(math.pi*phase)
                boot_z = (z + .025 + i * .045) * (1-phase) + point*phase
            else:
                boot_y, boot_z = steps_at(time, [(0.,1.,top,point,0.), exit[i][1]])
        else:
            boot_y, boot_z = (.365 if i == 0 else .305), z + .025 + i * .045
        feet.append((boot_y, boot_z))
    move_pelvis(rig, (0, supported_hip_height(height, z, feet), z))
    rotate(rig, "head", (1,0,0), 24 * weight)
    for i, (side, sign) in enumerate((("L", -1), ("R", 1))):
        boot_y, boot_z = feet[i]
        place_boot(rig, side, (sign * .079, boot_y, boot_z))
        if weight <= 0.:
            continue
        current = unity(rig.pose.bones["SOCKET_Grip."+side].head)
        target = (sign * RUNG_X, RUNG_Y + lift[i], hands[i])
        blended = current.lerp(Vector(target), weight)
        direction = Vector((0, -1, .10)).lerp(Vector((0, 1, .19)), weight).normalized()
        shoulder = unity(rig.pose.bones["upper_arm."+side].head)
        offset = (rig.data.bones["SOCKET_Grip."+side].head_local -
                  rig.data.bones["hand."+side].head_local).length
        wrist = blended - direction * offset - shoulder
        if weight < 1. or lift[i] > 0.00001:
            blended = shoulder + direction * offset + wrist.normalized() * max(.04, min(.319, wrist.length))
        arm(rig, side, blended, direction)
        targets[side] = target
    return weight if max(lift) < 1e-5 else .99, targets


def slide_height(z):
    for (start, high), (end, low) in zip(SLIDE_PROFILE, SLIDE_PROFILE[1:]):
        if z <= end:
            return high + (low - high) * max(0., (z - start) / (end - start))
    return SLIDE_PROFILE[-1][1]


def sliding_child(rig, time):
    if time <= 0. or time >= 8.:
        move_pelvis(rig, (0., .63, SLIDE_END if time >= 8. else 0.))
        return 0., None

    stairs = (
        [(.12,.59,.19,.36,.12), (1.10,1.57,.57,.90,.11),
         (2.10,2.56,SLIDE_DECK,1.42,.09), (3.04,3.50,SLIDE_DECK,1.76,.05)],
        [(.61,1.07,.38,.63,.12), (1.60,2.07,.76,1.17,.11),
         (2.59,3.02,SLIDE_DECK,1.61,.08)],
    )
    ascent = [(0.,(.63,0.)), (.60,(.69,.19)), (1.08,(.85,.44)),
              (1.59,(1.04,.70)), (2.08,(1.24,1.00)), (2.57,(1.37,1.31)),
              (3.03,(1.39,1.51)), (3.50,(1.39,1.68))]
    targets = {}
    contact = 0.
    seated = 0.
    if time <= 3.50:
        height, z = position_at(time, ascent)
        feet = [steps_at(time, track) for track in stairs]
        rail = ramp(time, .32, .78) * (1. - ramp(time, 2.08, 2.72))
    elif time < 4.58:
        seated = ramp(time, 3.50, 4.58)
        height = 1.39 + (.878 - 1.39) * seated
        z = 1.68 + (1.97 - 1.68) * seated
        feet = [Vector((.825, p)).lerp(Vector((slide_height(q) + .065, q)), ramp(time, a, b))
                for p, q, a, b in ((1.76,2.24,3.68,4.43),(1.61,2.20,3.91,4.58))]
        rail = 0.
    elif time < 6.05:
        # Gain speed on the fall, then slow along the shallow metal runout.
        progress = (time - 4.58) / (6.05 - 4.58)
        z = 1.97 + (3.62 - 1.97) * (progress - .82 * math.sin(2. * math.pi * progress) / (2. * math.pi))
        height = slide_height(z) + .118
        feet = [Vector((slide_height(min(3.76, z + length)) + .065, min(3.99, z + length)))
                for length in (.27, .23)]
        seated, rail = 1., 0.
    else:
        seated = 1. - ramp(time, 6.08, 7.10)
        rail = 0.
        height, z = position_at(time, [(6.05,(slide_height(3.62)+.118,3.62)),
            (6.55,(.40,3.84)), (7.10,(.63,3.96)), (8.0,(.63,SLIDE_END))])
        feet = []
        for length, down, forward, start, stop in ((.27,6.56,4.36,7.24,7.72),(.23,6.83,4.36,7.57,7.98)):
            boot_y = (.125 - .06 * ramp(time, 6.05, down))
            forward_step = ramp(time, start, stop)
            boot_y += .065 * math.sin(math.pi * forward_step)
            feet.append((boot_y, (3.62+length)*(1-forward_step) + forward*forward_step))

    move_pelvis(rig, (0, supported_hip_height(height, z, feet), z))
    if seated > 0.:
        rotate(rig, "spine", (1,0,0), -10 * seated)
        rotate(rig, "head", (1,0,0), 13 * seated)
    (rail_start, rail_y), (rail_end, rail_top) = SLIDE_RAIL
    contact = rail
    for index, (side, sign) in enumerate((("L", -1), ("R", 1))):
        y, foot_z = feet[index]
        place_boot(rig, side, (sign * .079, y, foot_z))
        if rail <= 0.:
            if seated > 0.:
                rotate(rig, "upper_arm."+side, (1,0,0), -28 * seated)
                rotate(rig, "forearm."+side, (1,0,0), -17 * seated)
            continue
        post = min(rail_end, max(rail_start, z + .16 + sign * .055 * math.sin(time * math.pi / .49)))
        grip = Vector((sign * .29, rail_y + (rail_top-rail_y)*(post-rail_start)/(rail_end-rail_start), post))
        original = unity(rig.pose.bones["SOCKET_Grip."+side].head)
        direction = Vector((0,-1,.18)).lerp(Vector((0,.22,1)), rail).normalized()
        offset = (rig.data.bones["SOCKET_Grip."+side].head_local -
                  rig.data.bones["hand."+side].head_local).length
        shoulder = unity(rig.pose.bones["upper_arm."+side].head)
        wrist = original.lerp(grip, rail) - direction * offset - shoulder
        if not .041 <= wrist.length <= .318:
            contact = min(contact, .99)
        target = shoulder + direction * offset + wrist.normalized() * max(.041, min(.318, wrist.length))
        arm(rig, side, target, direction)
        targets[side] = grip
    return contact, targets


def bridge_child(rig, time):
    deck_time = 3.50
    if time <= deck_time:
        return sliding_child(rig, time)
    if time >= deck_time + BRIDGE_CROSS_TIME:
        sliding_child(rig, time - BRIDGE_CROSS_TIME)
        pelvis = rig.pose.bones["pelvis"]
        pelvis.matrix = Matrix.Translation(src((BRIDGE_LANE, 0, 0))) @ pelvis.matrix
        bpy.context.view_layer.update()
        return 0., None

    t = time - deck_time
    z = 1.68
    yaw = 90 * (ramp(t, 0., .91) - ramp(t, 3.56, BRIDGE_CROSS_TIME))
    x = position_at(t, [(.0, (0., 0.)), (.50, (0., .03)), (.91, (0., .09)),
                       (1.32, (0., .24)), (1.76, (0., .50)), (2.20, (0., .77)),
                       (2.64, (0., 1.04)), (3.08, (0., 1.32)), (3.56, (0., 1.57)),
                       (4.08, (0., BRIDGE_LANE - .025)), (4.50, (0., BRIDGE_LANE))])[1]
    left = [(.50,.91,.10,z+.079), (1.35,1.76,.51,z+.079),
            (2.23,2.64,1.06,z+.079), (3.11,3.56,1.61,z+.079),
            (4.06,4.49,BRIDGE_LANE-.079,1.76)]
    right = [(.03,.49,.22,z-.079), (.93,1.32,.34,z-.079),
             (1.79,2.20,.79,z-.079), (2.67,3.08,1.34,z-.079),
             (3.59,4.05,BRIDGE_LANE+.079,1.61)]
    rotation = Quaternion(src((0,1,0)), math.radians(-yaw))
    matrix = rotation.to_matrix().to_4x4()
    origin = src((x,0,z))
    height = 1.39 - .014 * math.sin(math.pi*t/BRIDGE_CROSS_TIME)**2
    boots = []
    for side, sign, steps, initial_z in (("L", -1, left, 1.76), ("R", 1, right, 1.61)):
        foot_x, foot_z = sign * .079, initial_z
        swing = 0.
        for start, end, next_x, next_z in steps:
            p = ramp(t, start, end)
            foot_x += (next_x - foot_x) * p
            foot_z += (next_z - foot_z) * p
            if t < end:
                swing = math.sin(math.pi*p)
                break
        target = unity(rotation.inverted() @ (src((foot_x, .825 + .054 * swing, foot_z)) - origin))
        radius_squared = (target.x - sign * .082)**2 + target.z**2
        limit = target.y + .015 + math.sqrt(max(0., .548**2 - .0006 - radius_squared))
        height = min(height, limit)
        boots.append((side, target, swing))
    move_pelvis(rig, (0, height, 0))
    for side, target, swing in boots:
        place_boot(rig, side, target)
        rotate(rig, "upper_arm."+side, (1,0,0), -9 * swing)
    pelvis = rig.pose.bones["pelvis"]
    pelvis.matrix = Matrix.Translation(origin) @ matrix @ pelvis.matrix
    bpy.context.view_layer.update()
    return 0., None


def seesaw_mount(rig, phase):
    if phase <= 0.:
        return 0., None
    t = smooth(phase)
    x, y, z = SEESAW_GRIP
    foot_x, foot_y, foot_z = SEESAW_FOOT
    seat_y = SEESAW_PIVOT + .0785
    move_pelvis(rig, (0, .63 + (seat_y + .115 - .63) * t, (SEESAW_DOCK-SEESAW_SEAT) * t))
    rotate(rig, "spine", (1,0,0), -11*t)
    targets = {}
    grip = ramp(phase, .30, .94)
    for side, sign in (("L",-1),("R",1)):
        place_boot(rig, side, (sign*(.079 + (foot_x-.079)*t),
            .065 + foot_y*t + .047*math.sin(math.pi*t), (SEESAW_DOCK-foot_z)*t))
        target = Vector((sign*x,y,SEESAW_DOCK-z))
        current = unity(rig.pose.bones["SOCKET_Grip."+side].head)
        if grip > 0.:
            direction = Vector((0,.20,1)).normalized()
            offset = (rig.data.bones["SOCKET_Grip."+side].head_local -
                      rig.data.bones["hand."+side].head_local).length
            shoulder = unity(rig.pose.bones["upper_arm."+side].head)
            wrist = current.lerp(target, grip) - direction*offset - shoulder
            if grip < 1.:
                wrist = wrist.normalized() * max(.041, min(.318, wrist.length))
            arm(rig, side, shoulder + wrist + direction*offset, direction)
        targets[side] = target
    return grip, targets


def balance_step(rig, phase):
    if phase <= 0.:
        return
    height, z = position_at(phase, [(0.,(.63,0.)), (.45,(.68,.17)),
                                   (.76,(.78,.34)), (1.,(.807,.43))])
    feet = [steps_at(phase, [(.07,.42,BALANCE_STEP_TOP,.44,.072)]),
            steps_at(phase, [(.45,.89,BALANCE_STEP_TOP,.40,.072)])]
    move_pelvis(rig, (0,supported_hip_height(height,z,feet),z))
    for index, (side, sign) in enumerate((("L",-1),("R",1))):
        y, heel = feet[index]
        place_boot(rig, side, (sign*.075,y,heel))
        rotate(rig, "upper_arm."+side, (0,0,1), sign*38*smooth(phase))
        rotate(rig, "forearm."+side, (1,0,0), -12*smooth(phase))


def construct(rig,name,phase):
    reset(rig);neutral=pose_copy(rig);contact=0.;targets=None
    if name.startswith(("Goods","Toy","Sit")):
        kind=next(k for k in ("Goods","Toy","Sit") if name.startswith(k))
        if name.endswith("Loop"):
            targets=held(rig,kind,phase);contact=1.
        else:
            targets=held(rig,kind);target_pose=pose_copy(rig)
            contact=smooth(1-phase if name.endswith("Exit") else phase)
            blend(rig,neutral,target_pose,contact)
    elif name.startswith("Swing"):
        targets = swing_pose(rig, phase if name == "SwingPump" else 0.)
        contact = 1. if name == "SwingPump" else smooth(1 - phase if name == "SwingExit" else phase)
        if contact < 1.:
            blend(rig, neutral, pose_copy(rig), contact)
    elif name == "BarsCross":
        contact, targets = bars_cross(rig, phase * 6.5)
    elif name == "BeamCross":
        beam_cross(rig, phase * 5.5)
    elif name == "SlideCross":
        contact, targets = sliding_child(rig, phase * 8.)
    elif name == "SlideViaBridge":
        contact, targets = bridge_child(rig, phase * (8. + BRIDGE_CROSS_TIME))
    elif name == "SeesawMount":
        contact, targets = seesaw_mount(rig, phase)
    elif name == "BalanceStep":
        balance_step(rig,phase)
    elif name=="Idle":
        rotate(rig,"chest",(1,0,0),.45*math.sin(phase*2*math.pi))
        rotate(rig,"head",(0,1,0),1.5*math.sin(phase*2*math.pi))
    elif name=="StreetWalk":
        street_walk(rig, phase % 1.)
    elif name=="Walk":
        wave=math.sin(2*math.pi*phase)
        for side,sign in (("L",1),("R",-1)):
            swing=wave*sign
            rotate(rig,"thigh."+side,(1,0,0),19*swing)
            rotate(rig,"shin."+side,(1,0,0),-25*max(0,-swing))
            rotate(rig,"upper_arm."+side,(1,0,0),-14*swing)
            rotate(rig,"forearm."+side,(1,0,0),-7*abs(wave))
        move_pelvis(rig,(0,.63+.008*wave*wave,0))
    elif name in ("Turn","Stop"):
        wave=math.sin(math.pi*phase)**2
        rotate(rig,"chest",(0,1,0),(-6 if name=="Turn" else 0)*wave)
        rotate(rig,"thigh.L",(1,0,0),6*wave)
        rotate(rig,"shin.L",(1,0,0),-9*wave)
    elif name in ("AdjustSleeve","AdjustCap"):
        amount=math.sin(math.pi*phase)**2
        if name=="AdjustSleeve":
            rotate(rig,"spine",(1,0,0),-5)
            arm(rig,"L",(-.11,.87,.19))
            target=unity(rig.pose.bones["forearm.L"].head*.2+rig.pose.bones["forearm.L"].tail*.8)
            target.x+=.035;target.z+=.035
            arm(rig,"R",target)
        else:
            rotate(rig,"head",(1,0,0),8)
            arm(rig,"R",(.10,1.235,.115),hand_direction=(0,.8,.6))
        adjusted=pose_copy(rig);blend(rig,neutral,adjusted,amount)
    else:raise ValueError(name)
    return contact,targets


def snapshot(rig):
    def point(name):return [round(v,7) for v in unity(rig.pose.bones[name].head)]
    return dict(pelvis=point("pelvis"),left=point("SOCKET_Grip.L"),right=point("SOCKET_Grip.R"),
                left_foot=point("foot.L"),right_foot=point("foot.R"),head=point("head"))


def main():
    bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False)
    root,rig=body.create_rig();rig.animation_data_create()
    bpy.context.scene.render.fps=FPS
    tracks=[];poses={};maximum_error=0.;lower_error=0.;signature=hashlib.sha256()
    lower=("root","pelvis","thigh.L","thigh.R","shin.L","shin.R","foot.L","foot.R")
    reset(rig);neutral={b.name:b.matrix.copy() for b in rig.pose.bones}
    for name,duration,loop in CLIPS:
        action=bpy.data.actions.new("FairChild"+name);rig.animation_data.action=action
        frames=[];end=round(duration*FPS)
        for frame in range(end+1):
            contact,targets=construct(rig,name,frame/end)
            if contact==1. and targets:
                for side,target in targets.items():maximum_error=max(maximum_error,(rig.pose.bones["SOCKET_Grip."+side].head-src(target)).length)
            if name.startswith(("Goods","Toy")):
                for bone in lower:lower_error=max(lower_error,max(abs(rig.pose.bones[bone].matrix[i][j]-neutral[bone][i][j]) for i in range(4) for j in range(4)))
            payload=snapshot(rig);payload["contact_weight"]=round(contact,7);frames.append(payload)
            for bone in rig.pose.bones:
                bone.keyframe_insert("location",frame=frame,group=bone.name)
                bone.keyframe_insert("rotation_quaternion",frame=frame,group=bone.name)
                signature.update(json.dumps([name,frame,bone.name,[round(v,7) for v in bone.location],
                    [round(v,7) for v in bone.rotation_quaternion]],separators=(",",":")).encode())
            if frame in (0,end):poses[(name,frame==end)]=pose_copy(rig)
        action.use_frame_range=True;action.frame_start=0;action.frame_end=end
        tracks.append(dict(name=name,clip="FairChild"+name,duration_seconds=duration,loop=loop,frames=frames))
        print("Authored FairChild"+name,flush=True)
    endpoint_error=0.
    for kind in ("Goods","Toy","Sit"):
        for a,end,b,start in ((kind+"Enter",True,kind+"Loop",False),(kind+"Loop",True,kind+"Exit",False),
                              (kind+"Exit",True,"Idle",False),(kind+"Enter",False,"Idle",False)):
            for bone,(pos,rot) in poses[(a,end)].items():
                other=poses[(b,start)][bone]
                endpoint_error=max(endpoint_error,(pos-other[0]).length,abs(rot.rotation_difference(other[1]).angle))
    for a, end, b, start in (("SwingEnter", True, "SwingPump", False),
                             ("SwingPump", False, "SwingExit", False),
                             ("SwingExit", True, "Idle", False),
                             ("SwingEnter", False, "Idle", False)):
        for bone, (pos, rot) in poses[(a, end)].items():
            other = poses[(b, start)][bone]
            endpoint_error = max(endpoint_error, (pos-other[0]).length, abs(rot.rotation_difference(other[1]).angle))
    if maximum_error>.0002 or lower_error>.00001 or endpoint_error>.0001:
        raise ValueError(f"Child action contracts: grip={maximum_error:.8f}, planted lowerbody={lower_error:.8f}, endpoints={endpoint_error:.8f}")
    payload=dict(generator="city_fair_child_actions_v1",rig="FairChild",height=1.30,fps=FPS,
        bones=[s["name"] for s in body.bone_specs()],bone_count=len(body.bone_specs()),root_motion=False,animation_events=0,
        maximum_grip_error=maximum_error,maximum_lower_body_error=lower_error,maximum_endpoint_error=endpoint_error,
        signature=signature.hexdigest(),tracks=tracks)
    if "--validate-only" in sys.argv:
        previous=json.loads(MANIFEST.read_text(encoding="utf-8"))
        if previous!=payload:raise ValueError("Child action deterministic reconstruction differs from recorded manifest")
    else:
        OUT.parent.mkdir(parents=True,exist_ok=True);BLEND.parent.mkdir(parents=True,exist_ok=True)
        rig.animation_data.action=None;reset(rig)
        body.export_actions(OUT,root,rig)
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
        MANIFEST.write_text(json.dumps(payload,separators=(",",":"))+"\n",encoding="utf-8")
    print(json.dumps({k:v for k,v in payload.items() if k not in ("tracks","bones")}),flush=True)


if __name__=="__main__":main()
