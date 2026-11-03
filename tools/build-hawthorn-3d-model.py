#!/usr/bin/env python3
"""Build the passive, measured 50 ml hawthorn bottle; run through run-blender.py."""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
spec = importlib.util.spec_from_file_location("product_geometry", ROOT / "tools/build-supermarket-products-3d-model.py")
products = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = products
spec.loader.exec_module(products)
bp, kit = products.bp, products.kit

DESIGN = "hawthorn_tincture_v1"
VERSION = "1.0.0"
NAME = "ROOT_HawthornTincture3D"
ITEM = "hawthorn_tincture"
INNER = ((.005,.014),(.009,.016),(.065,.016),(.079,.0065),(.092,.0065),(.098,.0065),(.101,.0065))
TEXT = ("БОЯРЫШНИКА", "НАСТОЙКА", "50 мл")


def add(asset, item, name, geometry, role, colour):
    # All solid authoring is checked in Unity metres before the handedness swap.
    volume = bp.signed_volume(geometry)
    if volume < 0:
        geometry = (geometry[0], [tuple(reversed(face)) for face in geometry[1]])
    if bp.signed_volume(geometry) <= 0:
        raise ValueError("Non-positive solid: " + name)
    products.add_part(asset, item, name, geometry, role,
                      "glass" if role in ("glass", "liquid", "liquid_surface") else "paper", colour)


def label_text(asset, item, text, y, width):
    curve = bpy.data.curves.new("Hawthorn inscription", "FONT")
    curve.body = text
    curve.font = bpy.data.fonts.load(str(ROOT / "Assets/Resources/Fonts/Roboto-Regular.ttf"))
    curve.align_x = "CENTER"
    curve.align_y = "CENTER"
    curve.size = .0038
    curve.extrude = .000012
    curve.resolution_u = 1
    obj = bpy.data.objects.new("Hawthorn label " + text, curve)
    asset.collection.objects.link(obj)
    obj.location = (0., -.01888, y)
    obj.rotation_euler = (math.pi / 2., 0., 0.)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.context.view_layer.update()
    if obj.dimensions.x > width:
        obj.scale *= width / obj.dimensions.x
    bpy.ops.object.convert(target="MESH")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.parent = item.root
    products.assign_uv(obj.data)
    rgba = (.105, .092, .067, 1.)
    obj.data.materials.append(products.material_for(asset, "paper", rgba))
    geom = ([tuple(v.co) for v in obj.data.vertices], [tuple(p.vertices) for p in obj.data.polygons])
    item.parts.append(products.Part(obj, ITEM, obj.name, "label_print", "paper", rgba, geom))
    obj.select_set(False)


def build():
    collection, root = products.configure_scene()
    root.name = NAME
    root["bp_design_id"] = DESIGN
    asset = products.AssetBuild(root, collection)
    item_root = bpy.data.objects.new("ITEM_hawthorn_tincture", None)
    collection.objects.link(item_root)
    item_root.parent = root
    item = products.Item(ITEM, item_root)
    asset.items[ITEM] = item
    profiles = (
        ((0.,.0155),(.004,.0185),(.055,.0185),(.055,.016),(.009,.016),(.005,.014),(.005,0.),(0.,0.)),
        ((.055,.0185),(.066,.0185),(.079,.0085),(.098,.0085),(.102,.0088),
         (.102,.0065),(.098,.0065),(.079,.0065),(.065,.016),(.055,.016)),
    )
    for band, profile in enumerate(profiles):
        for sector in range(4):
            add(asset,item,f"Hawthorn glass cell {band*4+sector:02}",
                products.bottle_cell(profile,sector),"glass",(.29,.105,.026,1.))
    add(asset,item,"Hawthorn paper label", bp.u_cylinder((0.,.050,0.),(.0375,.015,.0375),16),
        "label",(.69,.635,.48,1.))
    # Edge marks follow paper edges; they are wear, not authored words or symbols.
    for index, (x,y,w) in enumerate(((-.011,.063,.004),(.010,.036,.006),(-.008,.037,.003))):
        add(asset,item,f"Hawthorn paper wear {index}",bp.u_box((x,y,-.01883),(w,.00055,.00006)),
            "label",(.50,.437,.29,1.))
    add(asset,item,"Hawthorn amber contents",products.ring_solid(tuple((y,r,r) for y,r in INNER),16),
        "liquid",(.39,.155,.029,.86))
    add(asset,item,"Hawthorn liquid surface",bp.u_cylinder((0.,.061,0.),(.032,.00004,.032),32),
        "liquid_surface",(.52,.245,.055,.88))
    add(asset,item,"Hawthorn screw cap",bp.u_cylinder((0.,.1045,0.),(.020,.0035,.020),24),
        "cap",(.30,.285,.225,1.))
    products.movable_pivot(item.parts[-1],(0.,.1045,0.))
    products.bottle_anchor(asset,item,"HawthornGrip",(0.,.052,0.))
    products.bottle_anchor(asset,item,"HawthornMouth",(0.,.102,0.))
    bpy.ops.object.select_all(action="DESELECT")
    for text, y, width in zip(TEXT,(.059,.052,.041),(.029,.026,.012)):
        label_text(asset,item,text,y,width)
    bpy.context.view_layer.update()
    return asset, item


def payload(asset, item):
    parts = [products.part_manifest(part) for part in item.parts]
    vertices = [v for part in item.parts for v in part.geometry[0]]
    low = [min(v[i] for v in vertices) for i in range(3)]
    high = [max(v[i] for v in vertices) for i in range(3)]
    triangles = sum(kit.triangle_count(p.geometry) for p in item.parts)
    assert len([p for p in item.parts if p.role == "glass"]) == 8
    assert abs(high[2] - .108) < .000001 and abs(low[2]) < .000001
    assert triangles < 9000, triangles
    source = {"design": DESIGN, "version": VERSION, "parts": [
        {"name":p.name,"role":p.role,"colour":p.base_color,
         "vertices":[[round(c,8) for c in v] for v in p.geometry[0]],"faces":p.geometry[1]}
        for p in item.parts], "inner":INNER, "font_sha256":hashlib.sha256(
            (ROOT / "Assets/Resources/Fonts/Roboto-Regular.ttf").read_bytes()).hexdigest()}
    signature = hashlib.sha256(json.dumps(source,sort_keys=True,separators=(",",":")).encode()).hexdigest()
    return {"generator":"tools/build-hawthorn-3d-model.py","generator_version":VERSION,
            "design_id":DESIGN,"root_name":NAME,"build_signature":signature,
            "triangle_count":triangles,"mesh_count":len(parts),"parts":parts,
            "unity_bounds_min":[round(c,6) for c in products.source_to_unity(low)],
            "unity_bounds_max":[round(c,6) for c in products.source_to_unity(high)],
            "height_m":.108,"radius_m":.0185,"grip_m":[0,.052,0],"mouth_m":[0,.102,0],
            "inner_profile_m":INNER,"authored_text":TEXT,"brands":[],"passive":True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir",type=Path,default=ROOT/"Assets/Hawthorn/Models")
    parser.add_argument("--source-dir",type=Path,default=ROOT/"ArtSource/Hawthorn/Blender")
    parser.add_argument("--validate-only",action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    asset, item = build()
    first = payload(asset,item)
    # Build twice, including font tessellation, to verify the authored geometry contract.
    asset, item = build()
    second = payload(asset,item)
    assert first["build_signature"] == second["build_signature"], "Non-deterministic hawthorn geometry"
    if not args.validate_only:
        args.model_dir.mkdir(parents=True,exist_ok=True)
        (args.model_dir/"HawthornTincture3D.json").write_text(json.dumps(second,indent=2,ensure_ascii=False)+"\n",encoding="utf-8")
        products.export_fbx(asset,args.model_dir/"HawthornTincture3D.fbx")
        products.save_blend(args.source_dir/"HawthornTincture3D.blend")
    print("HAWTHORN BOTTLE VALID",second["triangle_count"],"triangles;",second["build_signature"])


if __name__ == "__main__":
    main()
