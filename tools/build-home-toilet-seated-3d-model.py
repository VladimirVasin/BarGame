#!/usr/bin/env python3
"""Author only the seated toilet's compatible lower-body module and falling props."""
from __future__ import annotations
import argparse
import hashlib
import json
import math
import sys
from pathlib import Path
import bpy
import bmesh
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ArtSource/HomeToiletSeated"
RESOURCES = ROOT / "Assets/Resources/HomeToiletSeated"
HERO_SOURCE = ROOT / "ArtSource/PlayerV2/Blender/PlayerCharacter3DV2.blend"
PARTS = {"GEO_Pelvis": "pelvis", "GEO_Thigh.L": "thigh.L", "GEO_Thigh.R": "thigh.R",
         "GEO_Shin.L": "shin.L", "GEO_Shin.R": "shin.R"}
VERSION = "1.1.0"
OUTLET_SOURCE = (0, .033, .790)
BARE_SUPPORT_SOURCE_Z = .775170
SKIN_PATH = ROOT / "Assets/Resources/Player/PlayerBareSkinAtlas.png"

def load_hero():
    with bpy.data.libraries.load(str(HERO_SOURCE), link=False) as (data_from, data_to):
        data_to.objects = [name for name in data_from.objects if name in PARTS or "Armature" in name or name == "PlayerRig"]
    for obj in data_to.objects:
        if obj is not None:
            bpy.context.scene.collection.objects.link(obj)
    meshes = {obj.name: obj for obj in data_to.objects if obj is not None and obj.type == "MESH"}
    rig = next(mod.object for obj in meshes.values() for mod in obj.modifiers if mod.type == "ARMATURE")
    if rig.name not in bpy.context.scene.objects:
        bpy.context.scene.collection.objects.link(rig)
    rig.animation_data_clear()
    rig.data.pose_position = "REST"
    bpy.context.view_layer.update()
    return rig, meshes

def tri_mesh(mesh):
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method="BEAUTY", ngon_method="BEAUTY")
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()

def make_copy(source, name, rig):
    obj = source.copy()
    obj.data = source.data.copy()
    obj.name = name
    obj.data.name = name + "_Mesh"
    bpy.context.scene.collection.objects.link(obj)
    for modifier in list(obj.modifiers):
        if modifier.type != "ARMATURE": obj.modifiers.remove(modifier)
        else: modifier.object = rig
    tri_mesh(obj.data)
    obj.animation_data_clear()
    return obj

def lower_shape(obj, rig, bone_name):
    obj.shape_key_add(name="Basis")
    key = obj.shape_key_add(name="Lowered")
    # Shape keys compress real, UV-identical source fabric into knee folds.
    # Runtime transports these five one-bone pieces from their existing bone
    # to the actual knee; mesh vertices are never generated at runtime.
    bone_matrix = rig.data.bones[bone_name].matrix_local
    inverse_bone = bone_matrix.inverted()
    inverse_object = obj.matrix_world.inverted()
    for index, vertex in enumerate(obj.data.vertices):
        local = inverse_bone @ (obj.matrix_world @ vertex.co)
        fold = 1 + .055 * math.sin(index * 2.37)
        if bone_name == "pelvis":
            local.x *= .98
            local.y *= .28
            local.z *= .88
        else:
            local.x *= fold
            local.z *= fold
            local.y = local.y * (.12 if bone_name.startswith("thigh") else .18)
            if bone_name.startswith("shin"): local.y += .045
        key.data[index].co = inverse_object @ (bone_matrix @ local)
    obj["bp_lowered_target"] = "knee midpoint" if bone_name == "pelvis" else "shin." + bone_name[-1]

def add_cheeks(obj, rig):
    """One watertight pelvis: broad gluteal pads, cleft and thigh-root folds.

    A continuous underside replaces the former intersecting ellipsoids. Its
    outer loops sample the original garment envelope, so the hips and waist
    retain the hero's silhouette while the seated support stays unchanged.
    """
    sides = 40
    shoulder_z = .850
    inverse_object = obj.matrix_world.inverted()
    vertices = [Vector(OUTLET_SOURCE)]
    angles = [None]
    faces = []

    def source_outline(z):
        ring = []
        # Probe the source mesh before replacing it. The highest probe lies
        # just below the cap; its output is restored to the exact cap height.
        height = min(z, .972 - .00001)
        origin = Vector((0, .015, height))
        for col in range(sides):
            angle = math.tau * col / sides
            direction = Vector((math.cos(angle), math.sin(angle), 0))
            hit, location, _, _ = obj.ray_cast(inverse_object @ origin,
                inverse_object.to_3x3() @ direction)
            assert hit, ("pelvis envelope probe missed", z, col)
            point = obj.matrix_world @ location
            point.x *= .965
            point.y = obj.location.y + (point.y - obj.location.y) * .965
            point.z = z
            # Continue the central cleft up the rear plane, fading into the
            # sacral transition. It is a narrow groove, not two round caps.
            if point.y > .025:
                point.y -= .012 * math.exp(-(point.x / .018) ** 2) * \
                    math.exp(-((z - .852) / .050) ** 2)
            ring.append(point)
        return ring

    outline = source_outline(shoulder_z)
    for radius in (.10, .22, .36, .50, .63, .75, .87, 1):
        start = len(vertices)
        for col, edge in enumerate(outline):
            point = Vector(OUTLET_SOURCE).lerp(edge, radius)
            x, y = point.x, point.y
            # Broad, mildly flattened pads continue into the lateral hips.
            pad = math.exp(-((abs(x) - .056) / .043) ** 2 - ((y - .012) / .073) ** 2)
            cleft = math.exp(-(x / .013) ** 2 - ((y - .032) / .068) ** 2)
            fold_y = -.025 + .15 * (abs(x) - .060)
            fold = math.exp(-((y - fold_y) / .010) ** 2 - ((abs(x) - .068) / .052) ** 2)
            height = .795 - .023 * pad + .005 * cleft + .008 * fold
            # The same authored outlet meets the centre of the surface.
            outlet_weight = math.exp(-(x / .009) ** 2 - ((y - OUTLET_SOURCE[1]) / .032) ** 2)
            height = height * (1 - outlet_weight) + OUTLET_SOURCE[2] * outlet_weight
            edge_weight = max(0, min(1, (radius - .64) / .36))
            edge_weight = edge_weight * edge_weight * (3 - 2 * edge_weight)
            point.z = height * (1 - edge_weight) + shoulder_z * edge_weight
            vertices.append(point)
            angles.append(col / sides)
        if start == 1:
            for col in range(sides):
                faces.append((0, start + (col + 1) % sides, start + col))
        else:
            for col in range(sides):
                a, b = start - sides + col, start - sides + (col + 1) % sides
                faces.append((a, b, b + sides, a + sides))

    # Calibrate the broad seating pads to the existing authored support.
    # The outlet itself remains exact and does not acquire an offset.
    lowest = min(point.z for point in vertices)
    for point in vertices[1:]:
        point.z = BARE_SUPPORT_SOURCE_Z + (point.z - lowest) * \
            (shoulder_z - BARE_SUPPORT_SOURCE_Z) / (shoulder_z - lowest)

    for height in (.878, .912, .946, .972):
        start = len(vertices)
        vertices.extend(source_outline(height))
        angles.extend(col / sides for col in range(sides))
        for col in range(sides):
            a, b = start - sides + col, start - sides + (col + 1) % sides
            faces.append((a, b, b + sides, a + sides))
    faces.append(tuple(range(len(vertices) - sides, len(vertices))))

    uv_faces = []
    for face in faces:
        values = [angles[index] for index in face if angles[index] is not None]
        seam = len(face) <= 4 and max(values) - min(values) > .5
        values = [1 if seam and value == 0 else value for value in values]
        centre_u = sum(values) / len(values)
        uv = []
        for index in face:
            if max(face) < 1 + 8 * sides:
                # A planar underside island reuses the atlas's rear skin
                # and cleft, without stretching its front pubic pixels into
                # radial spokes around the outlet.
                u = .25 + .20 * vertices[index].x / .14
                v = .18 + .50 * (vertices[index].y + .07) / .17
            else:
                u = angles[index]
                if u is None: u = centre_u
                elif seam and u == 0: u = 1
                v = max(0, min(1, (vertices[index].z - .775) / (.972 - .775)))
            uv.append(((193 + 62 * u) / 256, (129 + 62 * v) / 256))
        uv_faces.append(uv)
    vertices = [inverse_object @ point for point in vertices]
    mesh = bpy.data.meshes.new("BarePelvis_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    uv_layer = mesh.uv_layers.new(name="UVMap")
    for face, values in zip(mesh.polygons,uv_faces):
        for index, value in zip(face.loop_indices,values): uv_layer.data[index].uv=value
    obj.data=mesh
    obj.vertex_groups.clear()
    group=obj.vertex_groups.new(name="pelvis")
    group.add(range(len(vertices)),1,"REPLACE")
    tri_mesh(mesh)

def shared_skin():
    mat=bpy.data.materials.new("MAT_SeatedBareSkin")
    mat.diffuse_color=(.68,.55,.48,1)
    mat.use_nodes=True
    texture=mat.node_tree.nodes.new("ShaderNodeTexImage")
    texture.image=bpy.data.images.load(str(SKIN_PATH),check_existing=True)
    texture.interpolation="Closest"
    mat.node_tree.links.new(texture.outputs["Color"],mat.node_tree.nodes.get("Principled BSDF").inputs["Base Color"])
    return mat

def make_module(rig, originals):
    root=bpy.data.objects.new("SeatedLowerBody_Root",None)
    bpy.context.scene.collection.objects.link(root)
    rig.parent=root
    rig.matrix_parent_inverse=Matrix.Identity(4)
    skin=shared_skin()
    models=[]
    for name,bone in PARTS.items():
        original=originals[name]
        trousers=make_copy(original,"Trousers_"+name[4:],rig)
        lower_shape(trousers,rig,bone)
        bare=make_copy(original,"Bare_"+name[4:],rig)
        if bone=="pelvis": add_cheeks(bare,rig)
        else:
            for v in bare.data.vertices:
                v.co.x*=.965
                v.co.y*=.965
        bare.data.materials.clear()
        bare.data.materials.append(skin)
        models += [trousers,bare]
    anchor=bpy.data.objects.new("ToiletOutlet",None)
    bpy.context.scene.collection.objects.link(anchor)
    anchor.parent=root
    anchor.location=OUTLET_SOURCE
    anchor.empty_display_type="PLAIN_AXES"
    anchor.empty_display_size=.015
    return root,models,anchor

def make_stool(name,length,diameter,phase):
    root=bpy.data.objects.new(name+"_Root",None)
    bpy.context.scene.collection.objects.link(root)
    vertices=[]
    rings=[(0,.13),(.08,.72),(.23,.98),(.45,1),(.68,.92),(.86,.79),(1,.18)]
    for row,(height,radius) in enumerate(rings):
        bend=.004*math.sin(height*math.pi+phase)*math.sin(height*math.pi)
        for col in range(10):
            angle=math.tau*col/10
            r=diameter*.5*radius*(1+.045*math.sin(col*2+row*1.4))
            # The toilet kit's standard Unity Y/Z swap: long axis is Unity Y.
            vertices.append((bend+r*math.cos(angle), r*math.sin(angle)*.88, length*height))
    faces=[tuple(reversed(range(10)))]
    for row in range(len(rings)-1):
        for col in range(10):
            a=row*10+col;b=row*10+(col+1)%10
            faces.append((a,b,b+10,a+10))
    faces.append(tuple(range((len(rings)-1)*10,len(rings)*10)))
    mesh=bpy.data.meshes.new(name+"_Mesh")
    mesh.from_pydata(vertices,[],faces);mesh.update();tri_mesh(mesh)
    obj=bpy.data.objects.new(name,mesh);bpy.context.scene.collection.objects.link(obj);obj.parent=root
    mat=bpy.data.materials.get("MAT_Stool") or bpy.data.materials.new("MAT_Stool")
    mat.diffuse_color=(.24,.17,.095,1);mesh.materials.append(mat)
    for anchor_name,z in (("Tip",0),("Top",length)):
        anchor=bpy.data.objects.new(anchor_name,None);bpy.context.scene.collection.objects.link(anchor)
        anchor.parent=root;anchor.location=(0,0,z)
    return root,[obj]

def signed_volume(mesh):
    mesh.calc_loop_triangles()
    return sum(mesh.vertices[t.vertices[0]].co.dot(mesh.vertices[t.vertices[1]].co.cross(
        mesh.vertices[t.vertices[2]].co)) for t in mesh.loop_triangles)/6

def validate(rig,originals,models):
    report={"trousers_source_endpoints_exact":True,"lowered_shape_count":0,"triangles":0,
            "real_bones_only":True,"closed_positive_volumes":True}
    for obj in models:
        mesh=obj.data;mesh.calc_loop_triangles()
        assert signed_volume(mesh)>1e-8,(obj.name,"inverted solid",signed_volume(mesh))
        assert all(len(v.groups)==1 for v in mesh.vertices),(obj.name,"must follow one production bone")
        for t in mesh.loop_triangles:
            a,b,c=[mesh.vertices[i].co for i in t.vertices]
            assert (b-a).cross(c-a).length>1e-10,(obj.name,"degenerate")
        report["triangles"]+=len(mesh.loop_triangles)
        if obj.name.startswith("Trousers_"):
            original=originals["GEO_"+obj.name[len("Trousers_"):]]
            assert len(mesh.vertices)==len(original.data.vertices)
            assert all((a.co-b.co).length<1e-9 for a,b in zip(mesh.vertices,original.data.vertices)),obj.name
            assert (obj.matrix_world.translation-original.matrix_world.translation).length<1e-8
            assert "Lowered" in mesh.shape_keys.key_blocks
            assert max((a.co-b.co).length for a,b in zip(mesh.vertices,mesh.shape_keys.key_blocks["Lowered"].data))>.06
            # Shape-key deformation must keep each closed fabric volume valid.
            lower_vertices=[point.co.copy() for point in mesh.shape_keys.key_blocks["Lowered"].data]
            lower_volume=sum(lower_vertices[t.vertices[0]].dot(lower_vertices[t.vertices[1]].cross(
                lower_vertices[t.vertices[2]])) for t in mesh.loop_triangles)/6
            assert lower_volume>1e-8,(obj.name,"inverted lowered cloth",lower_volume)
            def uv_vertex_set(value):
                return {(tuple(round(c,7) for c in value.vertices[value.loops[i].vertex_index].co),
                         tuple(round(c,7) for c in value.uv_layers.active.data[i].uv))
                        for p in value.polygons for i in p.loop_indices}
            assert uv_vertex_set(mesh)==uv_vertex_set(original.data),(obj.name,"source UV drift")
            report["lowered_shape_count"]+=1
    assert report["lowered_shape_count"]==5
    bare=next(o for o in models if o.name=="Bare_Pelvis")
    bm = bmesh.new()
    bm.from_mesh(bare.data)
    assert all(edge.is_manifold for edge in bm.edges), "bare pelvis must be watertight"
    remaining = set(bm.verts)
    components = 0
    while remaining:
        pending = [remaining.pop()]
        components += 1
        while pending:
            vertex = pending.pop()
            for edge in vertex.link_edges:
                neighbor = edge.other_vert(vertex)
                if neighbor in remaining:
                    remaining.remove(neighbor)
                    pending.append(neighbor)
    bm.free()
    assert components == 1, ("separate bare pelvis lobes", components)
    report["bare_pelvis_watertight_components"] = components
    support=min((bare.matrix_world@v.co).z for v in bare.data.vertices)
    assert abs(support - BARE_SUPPORT_SOURCE_Z) < 1e-6, "seated support changed"
    world_vertices = [bare.matrix_world @ vertex.co for vertex in bare.data.vertices]
    left_support = min(point.z for point in world_vertices if point.x < -.025)
    right_support = min(point.z for point in world_vertices if point.x > .025)
    assert abs(left_support - right_support) < .0003, "unbalanced gluteal support"
    assert min((point - Vector(OUTLET_SOURCE)).length for point in world_vertices) < 1e-6, "outlet surface drift"
    report["bare_pelvis_outlet_surface_exact"] = True
    report["bare_pelvis_cleft_above_support"] = round(OUTLET_SOURCE[2] - support, 6)
    report["bare_pelvis_triangles"] = len(bare.data.loop_triangles)
    report["bare_pelvis_source_min_z"]=round(support,6)
    report["seat_pelvis_world_y"]=round(.645+rig.data.bones["pelvis"].head_local.z-support,6)
    report["outlet_relative_pelvis_source"]=list(Vector(OUTLET_SOURCE)-rig.data.bones["pelvis"].head_local)
    return report

def meta(path,model=False,folder=False):
    target=Path(str(path)+".meta")
    if target.exists():return
    guid=hashlib.md5(("HomeToiletSeated/"+path.relative_to(ROOT).as_posix()).encode()).hexdigest()
    if model:
        text=(ROOT/"Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx.meta").read_text(encoding="utf-8")
        text=text.replace("guid: 17afea68fb0f9bc4795dc60cd0eef0cf","guid: "+guid)
        text=text.replace("optimizeBones: 1","optimizeBones: 0").replace("isReadable: 0","isReadable: 1")
    else:
        text="fileFormatVersion: 2\nguid: "+guid+"\n"
        if folder:text+="folderAsset: yes\nDefaultImporter:\n"
        else:text+="TextScriptImporter:\n"
        text+="  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    target.write_text(text,encoding="utf-8")

def export(path,objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,
        object_types={"EMPTY","ARMATURE","MESH"},axis_forward="-Z",axis_up="Y",
        add_leaf_bones=False,bake_anim=False,use_armature_deform_only=False,
        use_mesh_modifiers=False,mesh_smooth_type="FACE",use_custom_props=True,
        path_mode="STRIP",embed_textures=False)
    meta(path,model=True)

def preview(rig,models):
    for obj in bpy.context.scene.objects:obj.hide_render=True
    for obj in models:obj.hide_render=not obj.name.startswith("Bare_")
    scene=bpy.context.scene;scene.render.engine="BLENDER_WORKBENCH"
    scene.display.shading.light="STUDIO";scene.display.shading.color_type="TEXTURE"
    scene.display.shading.show_cavity=True
    camera_data=bpy.data.cameras.new("SeatedAuthoringCamera")
    camera=bpy.data.objects.new("SeatedAuthoringCamera",camera_data)
    scene.collection.objects.link(camera);scene.camera=camera
    rig.data.pose_position="POSE"
    for side in ("L","R"):
        for name,degrees in (("thigh.",-82),("shin.",82)):
            bone=rig.pose.bones[name+side];bone.rotation_mode="XYZ";bone.rotation_euler=(math.radians(degrees),0,0)
    camera.location=(0,.035,.44517)
    camera.rotation_euler=(Vector((0,.033,.80))-camera.location).to_track_quat("-Z","Y").to_euler()
    camera_data.type="PERSP";camera_data.lens=25;camera_data.clip_start=.005
    scene.render.resolution_x=900;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format="PNG";scene.render.filepath=str(SOURCE/"BarePelvis-Underside.png")
    bpy.ops.render.render(write_still=True)
    camera.location=(.30,.34,.66)
    camera.rotation_euler=(Vector((0,.01,.85))-camera.location).to_track_quat("-Z","Y").to_euler()
    camera_data.type="ORTHO";camera_data.ortho_scale=.43
    scene.render.filepath=str(SOURCE/"BarePelvis-RearThreeQuarter.png")
    bpy.ops.render.render(write_still=True)
    for side in ("L","R"):
        rig.pose.bones["thigh."+side].rotation_euler=(0,0,0)
        rig.pose.bones["shin."+side].rotation_euler=(0,0,0)
    rig.data.pose_position="REST"

def geometry_signature(models):
    return hashlib.sha256(json.dumps([
        {"name":obj.name,"vertices":[list(v.co) for v in obj.data.vertices],
         "faces":[list(p.vertices) for p in obj.data.polygons],
         "uv":[list(v.uv) for v in obj.data.uv_layers.active.data] if obj.data.uv_layers.active else [],
         "shapes":{key.name:[list(v.co) for v in key.data] for key in obj.data.shape_keys.key_blocks}
             if obj.data.shape_keys else {}}
        for obj in models],sort_keys=True).encode()).hexdigest()

def round_trip(expected):
    """Inspect the exported vertices, key deltas and fixed-metre anchor positions."""
    def close(actual,wanted):
        return len(actual)==len(wanted) and all(min((Vector(a)-Vector(b)).length for b in wanted)<.00002 for a in actual)
    for model,record in expected.items():
        bpy.ops.object.select_all(action="SELECT");bpy.ops.object.delete(use_global=False)
        bpy.ops.import_scene.fbx(filepath=str(RESOURCES/"Models"/(model+".fbx")),use_custom_normals=True)
        bpy.context.view_layer.update()
        for name,geometry in record["meshes"].items():
            obj=next(o for o in bpy.context.scene.objects if o.type=="MESH" and o.name==name)
            actual=sorted(tuple(round(c,5) for c in obj.matrix_world@v.co) for v in obj.data.vertices)
            assert close(actual,geometry["vertices"]),(model,name,"FBX vertex/metre mismatch")
            if geometry["lowered"] is not None:
                key=next(k for k in obj.data.shape_keys.key_blocks if k.name.endswith("Lowered"))
                actual=sorted(tuple(round(c,5) for c in obj.matrix_world@v.co) for v in key.data)
                assert close(actual,geometry["lowered"]),(model,name,"FBX Lowered mismatch")
        for name,position in record["anchors"].items():
            obj=next(o for o in bpy.context.scene.objects if o.name==name)
            assert (obj.matrix_world.translation-Vector(position)).length<1e-6,(model,name,"anchor axis mismatch")

def export_record(objects,anchors):
    return {"meshes":{obj.name:{
        "vertices":sorted(tuple(round(c,5) for c in obj.matrix_world@v.co) for v in obj.data.vertices),
        "lowered":sorted(tuple(round(c,5) for c in obj.matrix_world@v.co)
            for v in obj.data.shape_keys.key_blocks["Lowered"].data) if obj.data.shape_keys else None}
        for obj in objects},"anchors":{obj.name:list(obj.matrix_world.translation) for obj in anchors}}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--inspect", action="store_true")
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--preview", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    rig, meshes = load_hero()
    if args.inspect:
        print("SEATED_SOURCE " + json.dumps({"rig": rig.name, "matrix": [list(row) for row in rig.matrix_world],
        "parts": {name: {"location": list(obj.location), "vertices": len(obj.data.vertices),
            "bounds": [[min((obj.matrix_world @ v.co)[axis] for v in obj.data.vertices) for axis in range(3)],
                       [max((obj.matrix_world @ v.co)[axis] for v in obj.data.vertices) for axis in range(3)]],
            "bone_matrix": [list(row) for row in rig.data.bones[PARTS[name]].matrix_local]}
            for name,obj in meshes.items()}}), flush=True)
        return
    root,models,outlet=make_module(rig,meshes)
    bpy.context.view_layer.update()
    report=validate(rig,meshes,models)
    stools=[make_stool("Stool01",.08,.038,0),make_stool("Stool02",.07,.034,.5)]
    for _,objects in stools:
        assert signed_volume(objects[0].data)>1e-8
    bpy.context.view_layer.update()
    expected={"SeatedLowerBody":export_record(models,[outlet])}
    for stool_root,objects in stools:
        expected[objects[0].name]=export_record(objects,[o for o in stool_root.children if o.type=="EMPTY"])
    payload={"schema_version":1,"generator_version":VERSION,"source_hero_sha256":hashlib.sha256(HERO_SOURCE.read_bytes()).hexdigest(),
        "coordinates":"Same FBX axis/unit/import contract as production Hero V2; stool localY up",
        "report":report,"models":["SeatedLowerBody","Stool01","Stool02"],
        "outlet_source_world":OUTLET_SOURCE,"source_parts":PARTS,
        "geometry_sha256":geometry_signature(models+[o for _,items in stools for o in items]),
        "trousers_contract":"Exact production source position/UV at Basis; Lowered shape gathers at knee via actual-bone presentation proxies",
        "stools":[{"name":"Stool01","length":.08,"diameter":.038},{"name":"Stool02","length":.07,"diameter":.034}]}
    signature=hashlib.sha256(json.dumps(payload,sort_keys=True).encode()).hexdigest();payload["signature"]=signature
    manifest=RESOURCES/"HomeToiletSeated.json"
    if args.validate_only:
        assert json.loads(manifest.read_text(encoding="utf-8"))["signature"]==signature,"stale seated assets"
        round_trip(expected)
        print("HOME_TOILET_SEATED_VALIDATED "+json.dumps(report),flush=True);return
    SOURCE.mkdir(parents=True,exist_ok=True);(RESOURCES/"Models").mkdir(parents=True,exist_ok=True)
    meta(RESOURCES,folder=True);meta(RESOURCES/"Models",folder=True)
    export(RESOURCES/"Models/SeatedLowerBody.fbx",[root,rig,*models,outlet])
    for stool_root,objects in stools:export(RESOURCES/"Models"/(objects[0].name+".fbx"),[stool_root,*stool_root.children])
    if args.preview:preview(rig,models)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/"HomeToiletSeated.blend"))
    round_trip(expected)
    report["fbx_round_trip_vertices_shapes_metres_anchors"]=True
    manifest.write_text(json.dumps(payload,indent=2)+"\n",encoding="utf-8");meta(manifest)
    (SOURCE/"home-toilet-seated-3d-model.json").write_text(json.dumps(payload,indent=2)+"\n",encoding="utf-8")
    print("HOME_TOILET_SEATED_EXPORTED "+json.dumps(report),flush=True)

if __name__ == "__main__":
    main()
