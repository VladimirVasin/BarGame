"""Short-to-shoulder hair on the same three original weighted chains.

The medium Basis and its twelve bone bind matrices are immutable. Length keys
compensate the uniformly changed chain-rest translations used by PlayerHair;
the rendered target therefore changes once rather than stretching twice.
"""
from __future__ import annotations
import hashlib
import json
import math

SHORT_SHAPE = "HeroHairShort"
LONG_SHAPE = "HeroHairLong"
SHORT_SCALE = .55
LONG_SCALE = 1.8
SHORT_WEIGHTS = (100., 65., 25., 0., 0., 0., 0.)
LONG_WEIGHTS = (0., 0., 0., 20., 45., 70., 100.)
CHAINS = ("HairBack", "HairLeft", "HairRight")


def smooth(a, b, value):
    t=min(1.,max(0.,(value-a)/(b-a)))
    return t*t*(3-2*t)


def desired(point, root, length_scale, chain):
    from mathutils import Vector
    out=Vector(point)
    if chain:
        # The scalp overlap above the first joint is pinned; free rings grow
        # toward the original uneven ends rather than pulling the root open.
        freedom=smooth(.002,.036,root.z-point.z)
        out.z+=(point.z-root.z)*(length_scale-1.)*freedom
        long=max(0.,(length_scale-1.)/(LONG_SCALE-1.))
        tip=smooth(.045,.12,root.z-point.z)
        out.x+=long*tip*.010*math.sin(point.x*85.+point.y*13.)
        out.y+=long*tip*.009*math.sin(point.x*61.-point.y*17.)
    else:
        # The cap/part retain their authored contour. Side bangs are shorter
        # in the early profile; long bangs frame the face instead of covering
        # the eyes, while the nape continues into the free shoulder locks.
        root_z=1.675
        freedom=smooth(.002,.040,root_z-point.z)
        front=point.y<-.055 and abs(point.x)<.072
        factor=.30 if front and length_scale>1. else 1.
        out.z+=(point.z-root_z)*(length_scale-1.)*freedom*factor
    return out


def author(result, scale=1.):
    from mathutils import Vector
    rig=result.rig
    for part in result.parts:
        if getattr(part,"role",None)!="hair":continue
        obj=part.obj
        world,inverse=obj.matrix_world,obj.matrix_world.inverted()
        keys=obj.data.shape_keys
        basis=keys.key_blocks[0].data if keys else obj.data.vertices
        chain=obj.get("bp_hair_chain","")
        root=(rig.matrix_world@rig.data.bones[chain+".00"].head_local)/scale if chain else Vector((0.,0.,1.675))
        if keys is None:obj.shape_key_add(name="Basis",from_mix=False)
        for name,length_scale in ((SHORT_SHAPE,SHORT_SCALE),(LONG_SHAPE,LONG_SCALE)):
            key=obj.data.shape_keys.key_blocks.get(name) or obj.shape_key_add(name=name,from_mix=False)
            for i,vertex in enumerate(basis):
                point=(world@vertex.co)/scale
                destination=desired(point,root,length_scale,chain)
                skin_shift=Vector((0.,0.,0.))
                if chain:
                    for assignment in obj.data.vertices[i].groups:
                        bone_name=obj.vertex_groups[assignment.group].name
                        if bone_name.startswith(chain+"."):
                            bone_point=(rig.matrix_world@rig.data.bones[bone_name].head_local)/scale
                            skin_shift+=(bone_point-root)*(length_scale-1.)*assignment.weight
                key.data[i].co=inverse@((destination-skin_shift)*scale)
            key.value=0.
            key.slider_min=0.;key.slider_max=1.
        obj["bp_corruption_hair_lengths"]="hero_hair_length_v1"


def manifest(result):
    rows=[];digest=hashlib.sha256()
    for part in sorted(result.parts,key=lambda p:p.obj.name):
        obj=part.obj
        keys=obj.data.shape_keys
        if keys is None or keys.key_blocks.get(SHORT_SHAPE) is None:continue
        row=dict(renderer=obj.name,vertex_count=len(obj.data.vertices))
        for name in (SHORT_SHAPE,LONG_SHAPE):
            for vertex in keys.key_blocks[name].data:
                digest.update(json.dumps([round(float(c),7) for c in vertex.co],separators=(",",":")).encode())
        rows.append(row)
    return dict(contract="hero_hair_length_v1",short_shape=SHORT_SHAPE,long_shape=LONG_SHAPE,
        shortest_scale=SHORT_SCALE,longest_scale=LONG_SCALE,
        short_stage_weights=list(SHORT_WEIGHTS),long_stage_weights=list(LONG_WEIGHTS),
        rest_translation_compensated=True,root_pins_and_original_bones=True,
        surfaces=rows,geometry_sha256=digest.hexdigest())


def apply_preview_pose(result, stage):
    """The source renderer uses exactly the runtime's changed link rest lengths."""
    from mathutils import Matrix
    length_scale=1.+(SHORT_SCALE-1.)*SHORT_WEIGHTS[stage-1]/100.+(LONG_SCALE-1.)*LONG_WEIGHTS[stage-1]/100.
    for chain in CHAINS:
        for point,suffix in enumerate((".00",".01",".02",".Tip")):
            bone=result.rig.pose.bones[chain+suffix]
            if point==0:
                bone.matrix_basis=Matrix.Identity(4)
                continue
            parent=bone.parent
            local=parent.bone.matrix_local.inverted()@bone.bone.matrix_local
            local.translation*=length_scale
            bone.matrix=parent.matrix@local
