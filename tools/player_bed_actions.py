"""Scoped supported-sofa action publication on the existing production Hero V2."""
from __future__ import annotations

import json
import math
import hashlib
import copy
from pathlib import Path
import bpy
import numpy as np
from mathutils import Vector, Matrix

NAMES = ("BedEnter", "BedSleepLoop", "BedExit")
ARMS = tuple(f"{bone}.{side}" for side in ("L", "R") for bone in ("upper_arm", "forearm", "hand"))
MATTRESS_HALF_DEPTH = 1.57 * 1.15 / 1.75 / 2
BACK_FRONT = (.380+.375-.08)*1.15/1.75
# The loaded torso raises a local rim under the reclining palm. Unity's
# measured surface reaches .5754 m there; retain one planted wrist while
# clearing that rim instead of lifting every hand or weakening collision proof.
RECLINE_WRIST_HEIGHT = .606
PRESSURE_PARTS = ("GEO_Pelvis", "GEO_Torso", "GEO_Thigh.L", "GEO_Thigh.R", "GEO_Foot.L", "GEO_Foot.R")


class MattressRimEnvelope:
    """Local upper bound of HomeBedSurfaceDepressionModel's causal pressure skirt.

    The runtime spring is a convex interpolation of past targets. Taking the
    current raised target or the preceding bound's slow .5s recovery covers its
    history without retaining an old bulge after the body has moved away.
    Actual body footprints, the pinned perimeter and the 0.18 m skirt keep this
    local: unpressured cloth remains at .56 rather than raising all arms.
    """
    def __init__(self,result,common):
        self.columns,self.rows=17,11
        self.size_x,self.size_y=2.39,MATTRESS_HALF_DEPTH*2
        x=np.linspace(-self.size_x/2,self.size_x/2,self.columns+1)
        y=np.linspace(-self.size_y/2,self.size_y/2,self.rows+1)
        xx,yy=np.meshgrid(x,y);self.xx,self.yy=xx,yy
        border=np.minimum(self.size_x/2-np.abs(xx),self.size_y/2-np.abs(yy))
        edge=np.clip(border/.15,0,1);edge=edge*edge*(3-2*edge)
        targets={name:np.zeros((round(result.actions[name].action.frame_end)+1,*xx.shape)) for name in NAMES}
        parts={part.obj.name:part.obj for part in result.parts}
        morphs=[obj.data.shape_keys.key_blocks["HeroCorruption"] for obj in parts.values()
                if obj.data.shape_keys is not None and "HeroCorruption" in obj.data.shape_keys.key_blocks]
        values=[key.value for key in morphs]
        try:
            # The same frame-local bound also covers intermediate body forms.
            for form in (0.,.5,1.):
                for key in morphs: key.value=form
                for name in NAMES:
                    steps=round(result.actions[name].action.frame_end)
                    for index in range(steps+1):
                        t=index/steps;sample(result,name,t)
                        offset=support_position(common,name,t)-result.rig.pose.bones["pelvis"].head
                        deps=bpy.context.evaluated_depsgraph_get()
                        deepest=np.zeros_like(xx);bulge=np.zeros_like(xx)
                        for part_name in PRESSURE_PARTS:
                            obj=parts[part_name];evaluated=obj.evaluated_get(deps);mesh=evaluated.to_mesh()
                            try:
                                vertices=np.array([tuple(evaluated.matrix_world @ vertex.co+offset) for vertex in mesh.vertices])
                            finally: evaluated.to_mesh_clear()
                            low,high=vertices.min(axis=0),vertices.max(axis=0)
                            depth=min(.10,max(0.,.56-low[2]))
                            if depth<=0: continue
                            centre=(low+high)/2;half=(high-low)/2
                            # Match the currently placed owner's world AABB
                            # extents after the sofa's quarter-turn.
                            excess_x=np.maximum(0,np.abs(xx-centre[0])-half[1])/.18
                            excess_y=np.maximum(0,np.abs(yy-centre[1])-half[0])/.18
                            skirt=np.sqrt(excess_x*excess_x+excess_y*excess_y)
                            dent=np.clip(skirt/.55,0,1);dent=depth*(1-dent*dent*(3-2*dent))
                            rim=np.clip((skirt-.55)/.45,0,1);rim=rim*rim*(3-2*rim)
                            deepest=np.maximum(deepest,dent)
                            bulge=np.maximum(bulge,depth*.65*4*rim*(1-rim))
                        raised=np.where(deepest>.0005,0,bulge)*edge
                        if name=="BedEnter":
                            # Match the existing seated/lying weight ramp;
                            # use the next discrete interaction frame so the
                            # bound also covers sampling between those frames.
                            raised*=smooth((math.ceil(t*60)/60-.30)/.70)
                        targets[name][index]=np.maximum(targets[name][index],raised)
                print(f"Bed local mattress rim envelope: body form {form:g}",flush=True)
        finally:
            for key,value in zip(morphs,values): key.value=value
            bpy.context.view_layer.update()
        self.fields={}
        previous=np.zeros_like(xx)
        entering=[]
        for raised in targets["BedEnter"]:
            previous=np.maximum(raised,previous*math.exp(-(1/24)/.5))
            entering.append(previous.copy())
        loop=np.maximum(np.max(targets["BedSleepLoop"],axis=0),previous)
        # All three exact resting handoffs share the same bounded field.
        for index in range(len(entering)):
            weight=smooth((index/(len(entering)-1)-.90)/.10)
            entering[index]=np.maximum(entering[index],loop*weight)
        self.fields["BedEnter"]=.56+np.array(entering)
        self.fields["BedSleepLoop"]=.56+np.array([loop]*len(targets["BedSleepLoop"]))
        exiting=[];previous=loop
        count=len(targets["BedExit"])-1
        for index,raised in enumerate(targets["BedExit"]):
            dt=(.35/.30 if index/count<=.30 else 4.2/.70)/count
            if index>0: previous=np.maximum(raised,previous*math.exp(-dt/.5))
            exiting.append(previous.copy())
        self.fields["BedExit"]=.56+np.array(exiting)
        self.heights=.56+loop
        print("BED_RIM_ENVELOPE "+json.dumps({"maximum_rise_m":round(float(max(field.max() for field in self.fields.values())-.56),6),
            "observed_84_percent_bound_m":round(self.height(-.061,-.318,self.grid("BedEnter",.842)),6),
            "observed_975_percent_bound_m":round(self.height(-.204,-.409,self.grid("BedEnter",.975)),6)}),flush=True)

    def grid(self,name,t):
        fields=self.fields[name];frame=t*(len(fields)-1)
        # Both surrounding samples bound interpolation/import error without
        # carrying a future phase's much larger, unrelated pressure footprint.
        return np.maximum(fields[math.floor(frame)],fields[math.ceil(frame)])

    def height(self,x,y,field=None,padding=True):
        field=self.heights if field is None else field
        gx=min(self.columns,max(0.,(x+self.size_x/2)*self.columns/self.size_x))
        gy=min(self.rows,max(0.,(y+self.size_y/2)*self.rows/self.size_y))
        ix,iy=min(int(gx),self.columns-1),min(int(gy),self.rows-1)
        tx,ty=gx-ix,gy-iy
        a=field[iy,ix]*(1-tx)+field[iy,ix+1]*tx
        b=field[iy+1,ix]*(1-tx)+field[iy+1,ix+1]*tx
        height=float(a*(1-ty)+b*ty)
        # Imported AABBs, discrete body-weight frames and the live spring
        # differ by a few millimetres from source samples. Reserve that only
        # on a raised skirt; flat, unpressured support is unchanged.
        return height+(.004*smooth((height-.56)/.005) if padding else 0.)


def smooth(t):
    t = min(1.,max(0.,t))
    return t*t*(3-2*t)


def support_position(common,name,t):
    height,cross=common.bed_pelvis_at(name,t) if name != "BedSleepLoop" else (.5903,-.375)
    return Vector((.135,cross+.375,height))


def arm_vertices(result,side,bones=("hand","forearm")):
    deps=bpy.context.evaluated_depsgraph_get()
    for part in result.parts:
        if part.bone not in tuple(f"{bone}.{side}" for bone in bones): continue
        evaluated=part.obj.evaluated_get(deps); mesh=evaluated.to_mesh()
        try:
            for vertex in mesh.vertices: yield evaluated.matrix_world @ vertex.co
        finally: evaluated.to_mesh_clear()


def bake_contacts(builder,common):
    """Bake ordinary limb rotations against the measured sofa, retaining every torso/leg key."""
    from player_counter_seat_actions import solve_chain
    result=builder.result; rig=result.rig
    rim=MattressRimEnvelope(result,common)
    result.bed_rim_envelope=rim
    # Select one fixed wrist height from the palm's own posed shape and the
    # loaded interval. Forearm deficits belong to the elbow pole, never this
    # target. A neighbouring grid hump outside the hand cannot raise it.
    sample(result,"BedEnter",.74)
    planted_hand=rig.pose.bones["hand.R"]
    rest=planted_hand.bone.matrix_local.to_quaternion()
    flat=(planted_hand.bone.tail_local-planted_hand.bone.head_local).normalized().rotation_difference(Vector((0,-1,0))) @ rest
    inverse=planted_hand.matrix.to_quaternion().inverted()
    hand_offsets=[flat @ (inverse @ (point-planted_hand.head)) for point in arm_vertices(result,"R",("hand",))]
    # The jacket cuff surrounds the fixed wrist: retain a small local garment
    # allowance here rather than moving a loaded wrist during the action.
    recline_height=max(RECLINE_WRIST_HEIGHT,max(rim.height(-.04+p.x,-.24+p.y,rim.grid("BedEnter",t))+.010-p.z
        for t in np.linspace(.70,.84,29) for p in hand_offsets))
    print(f"Bed fixed reclining wrist height: {recline_height:.6f} m",flush=True)
    for name in NAMES:
        record=result.actions[name]; raw=record.action
        target=raw.copy(); target.name=name+"_supported"; target.use_fake_user=True
        for curve in list(common.iter_action_fcurves(target)):
            if any(curve.data_path.startswith(f'pose.bones["{bone}"]') for bone in ARMS):
                # Blender 5 actions store channels in a layered channel bag.
                for layer in target.layers:
                    for strip in layer.strips:
                        for bag in strip.channelbags:
                            if curve in bag.fcurves.values(): bag.fcurves.remove(curve)
        steps=round(raw.frame_end*2)
        previous={}
        for index in range(steps+1):
            t=index/steps; sample(result,name,t)
            field=rim.grid(name,t)
            # Wake follows a live, still-recovering spring and imports the
            # fast first third of the clip. Add 5 mm to its low-arm reserve;
            # ease from the exact sleeping seam, preserving both other takes.
            clearance=.006+(.005*smooth(t/.05) if name=="BedExit" else 0.)
            pelvis=rig.pose.bones["pelvis"].head.copy()
            support=support_position(common,name,t)
            for side in ("L","R"):
                upper=rig.pose.bones[f"upper_arm.{side}"]; lower=rig.pose.bones[f"forearm.{side}"]; hand=rig.pose.bones[f"hand.{side}"]
                wrist=hand.head.copy(); rotation=hand.matrix.to_quaternion().copy()
                hint=lower.head.copy()
                weight=(smooth((t-.64)/.06)*(1-smooth((t-.84)/.08))) if name=="BedEnter" and side=="R" else 0.
                if weight>0:
                    planted=pelvis-support+Vector((-.04,-.24,recline_height))
                    wrist=wrist.lerp(planted,weight)
                    rest=hand.bone.matrix_local.to_quaternion()
                    flat=(hand.bone.tail_local-hand.bone.head_local).normalized().rotation_difference(Vector((0,-1,0))) @ rest
                    rotation=rotation.slerp(flat,weight)
                    hint+=Vector((-.12,-.05,.25))*weight
                for attempt in range(24):
                    solve_chain(upper,lower,hand,wrist,rotation,hint)
                    hand_points=[p-pelvis+support for p in arm_vertices(result,side,("hand",))]
                    forearm_points=[p-pelvis+support for p in arm_vertices(result,side,("forearm",))]
                    points=hand_points+forearm_points
                    lift=max((rim.height(p.x,p.y,field)+clearance-p.z for p in hand_points
                        if abs(p.x)<1.195 and abs(p.y)<MATTRESS_HALF_DEPTH),default=0.)
                    elbow_lift=max((rim.height(p.x,p.y,field)+clearance-p.z for p in forearm_points
                        if abs(p.x)<1.195 and abs(p.y)<MATTRESS_HALF_DEPTH),default=0.)
                    rear=max((p.y for p in points if abs(p.x)<1.275 and .5<p.z<1.085),default=BACK_FRONT-.008)
                    lift=max(0.,lift)
                    elbow_lift=max(0.,elbow_lift)
                    inward=max(0.,rear-(BACK_FRONT-.008))
                    if max(lift,elbow_lift,inward)<.0005: break
                    # A raised welt beside the elbow must not drag a planted
                    # palm along with it. Move the pole first, keeping the
                    # wrist target; only the hand's own clearance can lift it.
                    if attempt>=8 and weight<.999:
                        lift=max(lift,elbow_lift)
                    wrist+=Vector((0,-inward*1.15,lift*1.15))
                    hint+=Vector((0,-inward*.5,lift+elbow_lift*3.))
            rig.animation_data.action=target
            for bone_name in ARMS:
                bone=rig.pose.bones[bone_name]
                if bone_name in previous: bone.rotation_quaternion.make_compatible(previous[bone_name])
                previous[bone_name]=bone.rotation_quaternion.copy()
                for channel in ("rotation_quaternion","location","scale"):
                    bone.keyframe_insert(data_path=channel,frame=t*raw.frame_end,group=bone_name)
            rig.animation_data.action=raw
        for curve in common.iter_action_fcurves(target):
            if any(curve.data_path.startswith(f'pose.bones["{bone}"]') for bone in ARMS):
                for key in curve.keyframe_points:
                    key.interpolation="BEZIER";key.handle_left_type=key.handle_right_type="AUTO_CLAMPED"
        rig.animation_data.action=None
        bpy.data.actions.remove(raw);target.name=name
        result.actions[name]=common.ActionRecord(target,record.category,record.duration_seconds,record.loop,record.source_frame_count,record.source_fps)
        print("Bed supported arm bake: "+name,flush=True)


def sample(result, name, time):
    action = result.actions[name].action
    result.rig.animation_data.action = action
    frame = action.frame_end * time
    bpy.context.scene.frame_set(math.floor(frame), subframe=frame-math.floor(frame))
    bpy.context.view_layer.update()


def arm_report(result, common):
    rows = []
    for name in NAMES:
        for t in (.3,.375,.545,.65,.7,.74,.78,.82,.86,.90,.94,1.):
            sample(result,name,t)
            pelvis = result.rig.matrix_world @ result.rig.pose.bones["pelvis"].head
            height, cross = common.bed_pelvis_at(name,t) if name != "BedSleepLoop" else (.5903,-.375)
            deps = bpy.context.evaluated_depsgraph_get()
            row = {"clip":name,"time":t}
            for side in ("L","R"):
                low = math.inf
                for part in result.parts:
                    if part.bone not in (f"hand.{side}",f"forearm.{side}"): continue
                    evaluated=part.obj.evaluated_get(deps); mesh=evaluated.to_mesh()
                    try:
                        for v in mesh.vertices:
                            p=evaluated.matrix_world @ v.co-pelvis+Vector((.135,cross+.375,height))
                            if abs(p.x)<1.195 and abs(p.y)<1.0317143/2:
                                low=min(low,p.z)
                    finally: evaluated.to_mesh_clear()
                row[side]=round(low,4) if math.isfinite(low) else None
                wrist=result.rig.pose.bones[f"hand.{side}"].head-pelvis+Vector((.135,cross+.375,height))
                row[side+"_wrist"]=[round(x,3) for x in wrist]
            rows.append(row)
    print("BED_ARM_SOURCE_SCAN "+json.dumps(rows),flush=True)


def validate_contacts(result, common):
    """Actual arm meshes clear the full local pressure-rim envelope and solid back."""
    rim=getattr(result,"bed_rim_envelope",None)
    if rim is None:
        rim=MattressRimEnvelope(result,common)
        result.bed_rim_envelope=rim
    minimum=math.inf; physical_minimum=math.inf; back_clearance=math.inf; planted=[]; worst=None
    for name in NAMES:
        steps=round(result.actions[name].action.frame_end*2)
        for index in range(steps+1):
            t=index/steps;sample(result,name,t)
            field=rim.grid(name,t)
            pelvis=result.rig.pose.bones["pelvis"].head.copy()
            offset=support_position(common,name,t)-pelvis
            for side in ("L","R"):
                for point in arm_vertices(result,side):
                    p=point+offset
                    if abs(p.x)<1.195 and abs(p.y)<MATTRESS_HALF_DEPTH:
                        surface=rim.height(p.x,p.y,field)
                        physical_minimum=min(physical_minimum,p.z-rim.height(p.x,p.y,field,padding=False))
                        if p.z-surface<minimum:
                            minimum=p.z-surface;worst=(name,t,side,tuple(p),surface)
                    if abs(p.x)<1.275 and .52<p.z<1.08:
                        back_clearance=min(back_clearance,BACK_FRONT-p.y)
                        if p.y>BACK_FRONT+.002:
                            raise RuntimeError(f"{name} at {t:.5f}: {side} arm penetrates solid sofa back by {p.y-BACK_FRONT:.5f} m")
                if name=="BedEnter" and side=="R" and .70<=t<=.84:
                    planted.append(result.rig.pose.bones["hand.R"].head+offset)
    drift=max((point-planted[0]).length for point in planted)
    if physical_minimum<.002:
        raise RuntimeError(f"Bed arm lacks 2mm physical rim clearance: {physical_minimum:.6f}m")
    # The four-millimetre import reserve is separate from physical cloth.
    # Sub-millimetre quaternion interpolation may consume at most one mm of
    # that reserve; the actual rig must retain positive physical clearance.
    if minimum<-.001:
        raise RuntimeError(f"Bed arm consumes too much import reserve: {-minimum:.6f}m; worst={worst}")
    if drift>.015: raise RuntimeError(f"BedEnter loaded recline palm drifts {drift:.6f}m")
    # All world-rig handoffs remain exact even though the narrower sofa
    # requires a corrected resting forearm, shared by all three takes.
    seams=(("Relaxed",0.,"BedEnter",0.),("BedEnter",1.,"BedSleepLoop",0.),
           ("BedSleepLoop",0.,"BedSleepLoop",1.),("BedSleepLoop",0.,"BedExit",0.),
           ("BedExit",1.,"Relaxed",0.))
    for first,a,last,b in seams:
        sample(result,first,a); before={bone.name:bone.matrix.copy() for bone in result.rig.pose.bones}
        sample(result,last,b)
        for bone in result.rig.pose.bones:
            error=max(abs(before[bone.name][row][col]-bone.matrix[row][col]) for row in range(4) for col in range(4))
            if error>.0001: raise RuntimeError(f"Bed seam {first}->{last} changes {bone.name} by {error}")
    proof={"minimum_arm_mattress_clearance_m":round(physical_minimum,6),
           "minimum_padded_margin_m":round(minimum,6),
           "minimum_arm_back_clearance_m":round(back_clearance,6),
           "recline_palm_drift_m":round(drift,6),"sofa_depth_m":1.15,
           "surface":"bounded actual pressure skirts; all bed phases and body forms",
           "local_import_sampling_margin_m":.004,
           "recline_palm_hold":[.70,.84],"exact_full_rig_seams":True}
    print("HOME_BED_CONTACTS_OK "+json.dumps(proof),flush=True)
    return proof


def refresh(module,config):
    from player_counter_seat_actions import load
    import player_hand_gestures
    original,builder=load(module,config)
    result=builder.result
    hashes=(original["face_atlas"]["sha256"],original["texture_bindings"][0]["sha256"],original["bare_skin_atlas"]["sha256"])
    gestures=original["hand_grip"]["gestures"]
    def frozen_gestures(source,scale,grip):
        parts={part.obj.name:part.obj for part in source.parts}
        content={name:{shape:[[round(float(c),7) for c in point.co]
            for point in parts[name].data.shape_keys.key_blocks[shape].data]
            for shape in sorted(player_hand_gestures.shape_names(name))} for name in player_hand_gestures.NAMES}
        if hashlib.sha256(json.dumps(content,sort_keys=True).encode()).hexdigest()!=gestures["shape_signature"]:
            raise RuntimeError("Bed refresh changed a finite hand profile")
        return copy.deepcopy(gestures["validation"])
    def signature(exclude_bed=False):
        result.rig.animation_data.action=None;builder._reset_pose()
        old=player_hand_gestures.validate;complete=result.actions
        try:
            player_hand_gestures.validate=frozen_gestures
            if exclude_bed: result.actions={name:row for name,row in complete.items() if name not in NAMES}
            return module.content_signature(config,result,*hashes)
        finally:
            result.actions=complete;player_hand_gestures.validate=old
    def shape_signature():
        return hashlib.sha256(repr([(part.obj.name,[(key.name,key.value,key.mute,key.slider_min,key.slider_max,
            key.interpolation,key.relative_key.name if key.relative_key else None,[tuple(point.co) for point in key.data])
            for key in part.obj.data.shape_keys.key_blocks]) for part in sorted(result.parts,key=lambda p:p.obj.name)
            if part.obj.data.shape_keys is not None]).encode()).hexdigest()
    if signature()!=original["content_signature_sha256"]:
        raise RuntimeError("Bed refresh source differs from its published manifest")
    preserved=signature(True);shapes=shape_signature()
    baseline_errors=[]
    module.common.validate_bed_support_contract(result,baseline_errors)
    module.common.validate_bed_sleep_pose(result,baseline_errors)
    def rebuild():
        result.rig.animation_data.action=None
        for name in NAMES:
            old=result.actions.pop(name);bpy.data.actions.remove(old.action)
        builder.build_bed_actions()
        for name in NAMES:
            action=result.actions[name].action
            action["bp_generator_version"]=module.V2_GENERATOR_VERSION
            action["bp_torso_skin"]="pelvis_spine_chest_v1"
            action["bp_event_count"]=0
    rebuild()
    proof=validate_contacts(result,module.common)
    errors=[]
    module.common.validate_bed_support_contract(result,errors)
    module.common.validate_bed_sleep_pose(result,errors)
    introduced=sorted(set(errors)-set(baseline_errors))
    if introduced: raise RuntimeError("Bed contact validation regressed:\n"+"\n".join(introduced))
    if errors:
        print("HOME_BED_EXISTING_GARMENT_BASELINE: unchanged existing jacket support failures; no new bed contract failures",flush=True)
    first=signature()
    rebuild()
    if signature()!=first: raise RuntimeError("Bed supported actions are nondeterministic")
    if signature(True)!=preserved or shape_signature()!=shapes:
        raise RuntimeError("Bed refresh changed unrelated actions/model/rig/UV/weights/morphs")
    print("HOME_BED_PRESERVED_CONTENT_OK: deterministic bed actions; all other actions, model, rig and shapes unchanged",flush=True)
    contract=original["bed_contract"]
    for key,name in (("enter_pelvis_path","BedEnter"),("exit_pelvis_path","BedExit")):
        contract[key]=[{"progress":t,"position":[-6.285-(cross+.375),height,-.04]}
            for t,height,cross in module.common.BED_PELVIS_PROFILES[name]]
    contract["supported_sofa"]=proof
    original["bed_authoring_sha256"]=hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    original["content_signature_sha256"]=first
    result.root["bp_content_signature_sha256"]=first;bpy.context.scene["bp_content_signature_sha256"]=first
    module.common.export_animation_fbx(config.animation_fbx,result)
    result.rig.animation_data.action=None;builder._reset_pose()
    module.common.save_blend(config.output)
    config.manifest.parent.mkdir(parents=True,exist_ok=True)
    config.manifest.write_text(json.dumps(original,indent=2,ensure_ascii=False)+"\n",encoding="utf-8")
    source_path,manifest_path=module.DEFAULT_OUTPUT,module.DEFAULT_MANIFEST
    try:
        module.DEFAULT_OUTPUT,module.DEFAULT_MANIFEST=config.output,config.manifest
        _,builder=load(module,config)
        result=builder.result
        if signature()!=first:
            raise RuntimeError("Saved bed source differs from its published animation contract")
    finally:
        module.DEFAULT_OUTPUT,module.DEFAULT_MANIFEST=source_path,manifest_path
    print("HOME_BED_SAVED_SOURCE_OK: all bed actions survive reopen with the verified source signature",flush=True)
    print("HOME_BED_PUBLICATION_OK: source, animation FBX and bed manifest",flush=True)
