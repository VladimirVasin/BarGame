"""Hero surface UV/data authoring; geometry, colours and animation stay owned elsewhere.

The data half is plain Python. Blender-only functions import Blender locally so
the same recipes can verify published PNGs without opening the character source.
"""
from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path

from atlas_kit import PixelCanvas


ROOT = Path(__file__).resolve().parents[1]
MODEL = ROOT / "Assets/Player3D/V2/Models/PlayerCharacter3DV2.json"
REPORT = ROOT / "ArtSource/Player/player-body-hair-surfaces.json"
SIZE = 256
VERSION = "1.0.0"
SKIN_MATERIALS = {"MAT_Skin", "MAT_SkinShadow", "MAT_SkinDark"}
EXTRA_SKIN_RECTS = {
    "SkinUpperArm": (128, 128, 64, 64),
    "SkinForearm": (128, 192, 64, 64),
    "SkinHand": (192, 192, 64, 64),
    "SkinHead": (128, 0, 64, 64),
    "SkinNeck": (192, 0, 64, 64),
}
HAIR_RECTS = {
    "HairCrown": (0, 128, 128, 128),
    "HairCurtain": (128, 128, 128, 128),
    "HairLength": (0, 0, 128, 128),
    "HairNape": (128, 0, 128, 128),
}


def digest(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def json_bytes(value: object) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def region(name: str, renderer: str, rect: tuple) -> dict:
    return dict(name=name, renderer=renderer,
                **dict(zip(("x_px", "y_px", "width_px", "height_px"), rect)))


def extra_skin_region(name: str) -> str:
    if name.startswith(("GEO_UpperArm.", "GEO_Shoulder.")):
        return "SkinUpperArm"
    if name.startswith("GEO_Forearm."):
        return "SkinForearm"
    if name.startswith(("GEO_Hand.", "GEO_Finger", "GEO_Thumb.")):
        return "SkinHand"
    if name == "GEO_Head":
        return "SkinHead"
    if name == "GEO_Neck" or name.startswith("GEO_Ear."):
        return "SkinNeck"
    raise ValueError("Unregistered hero skin UV: " + name)


def hair_region(name: str) -> str:
    if "Crown" in name or name == "GEO_HairCap":
        return "HairCrown"
    if "Curtain" in name or "Temple" in name:
        return "HairCurtain"
    if name in ("GEO_HairNape", "GEO_HairBack"):
        return "HairNape"
    return "HairLength"


def make_bindings(payload: dict) -> list[dict]:
    """All recipe membership comes from semantic manifest parts, never runtime names."""
    skin = [{k: r[k] for k in ("name", "renderer", "x_px", "y_px", "width_px", "height_px")}
            for r in payload["bare_skin_atlas"]["regions"]]
    existing = {r["renderer"] for r in skin}
    hair = []
    for part in payload["parts"]:
        name = part["name"]
        if name == "GEO_FaceSurface":
            continue
        if part["material"] in SKIN_MATERIALS and name not in existing:
            key = extra_skin_region(name)
            skin.append(region(key, name, EXTRA_SKIN_RECTS[key]))
        if part["role"] == "hair" or part["material"].startswith("MAT_Hair"):
            key = hair_region(name)
            hair.append(region(key, name, HAIR_RECTS[key]))
    result = []
    for recipe, regions, mode, scale, stem in (
        ("Clothing", payload["texture_bindings"][0]["regions"], "existing_clothing_atlas", .65, "Clothing"),
        ("Skin", skin, "existing_palette_or_bare_skin", .35, "Skin"),
        ("Hair", hair, "existing_palette", .5, "Hair"),
    ):
        binding = dict(recipe=recipe,
            normal_asset=f"Assets/Player3D/V2/Textures/Player{stem}Normal.png",
            response_asset=f"Assets/Player3D/V2/Textures/Player{stem}Response.png",
            width_px=SIZE, height_px=SIZE, uv_channel=0, uv_origin="bottom_left",
            base_color_mode=mode, normal_scale=scale, regions=regions)
        if recipe == "Clothing":
            binding["normal_sha256"] = digest((ROOT / binding["normal_asset"]).read_bytes())
            binding["response_sha256"] = digest((ROOT / binding["response_asset"]).read_bytes())
        else:
            normal, response, _ = make_maps(binding)
            binding["normal_sha256"] = digest(normal)
            binding["response_sha256"] = digest(response)
        result.append(binding)
    return result


def encoded(value: float) -> int:
    return max(0, min(255, round(value * 255)))


def make_maps(binding: dict) -> tuple[bytes, bytes, list]:
    normal, response = PixelCanvas(SIZE, SIZE), PixelCanvas(SIZE, SIZE)
    normal.rect(0, 0, SIZE, SIZE, (128, 128, 255, 255))
    response.rect(0, 0, SIZE, SIZE, (0, 0, 0, 0))
    seen, metrics = set(), []
    hair = binding["recipe"] == "Hair"
    for r in binding["regions"]:
        rect = tuple(r[k] for k in ("x_px", "y_px", "width_px", "height_px"))
        if rect in seen:
            continue
        seen.add(rect)
        x0, y0, width, height = rect
        heights = []
        alphas = []
        for y in range(height):
            row = []
            for x in range(width):
                if hair:
                    # U crosses the locks; V follows their authored root-to-tip
                    # direction. Broad ribbons, not individual photoreal fibres.
                    strand = math.sin(x * math.tau / 7 + .28 * math.sin(y * math.tau / 67))
                    broad = math.sin(x * math.tau / 23 + .15 * math.sin(y * math.tau / 53))
                    value = .070 * strand + .035 * broad
                    smoothness = .19 + .033 * broad + .012 * strand
                else:
                    # Restrained skin microrelief only: no albedo/painted hair,
                    # anatomy, scars or freckles are interpreted as displacement.
                    grain = (math.sin(x * 1.13 + math.sin(y * .73)) +
                             math.sin(y * 1.31 + math.sin(x * .61))) * .5
                    broad = math.sin(x * .19 + y * .13)
                    value = .014 * grain + .008 * broad
                    smoothness = .13 + .014 * broad + .007 * grain
                row.append(value)
                a = encoded(smoothness)
                alphas.append(a)
                response.put(x0 + x, SIZE - 1 - y0 - y, (0, 0, 0, a))
            heights.append(row)
        max_deviation = 0
        for y in range(height):
            for x in range(width):
                edge = min(x, y, width - 1 - x, height - 1 - y)
                fade = max(0., min(1., (edge - 1) / 3))
                fade = fade * fade * (3 - 2 * fade)
                # Authored cap disks live in this neutral lower-left patch.
                if x < 12 and y < 12:
                    fade = 0.
                dx = (heights[y][min(width - 1, x + 1)] - heights[y][max(0, x - 1)]) * .5
                dy = (heights[min(height - 1, y + 1)][x] - heights[max(0, y - 1)][x]) * .5
                nx, ny = -dx * 3.5 * fade, -dy * 3.5 * fade
                length = math.sqrt(nx * nx + ny * ny + 1)
                pixel = (encoded(nx / length * .5 + .5), encoded(ny / length * .5 + .5),
                         encoded(.5 / length + .5), 255)
                max_deviation = max(max_deviation, abs(pixel[0] - 128), abs(pixel[1] - 128))
                normal.put(x0 + x, SIZE - 1 - y0 - y, pixel)
        low, high = ((.14, .24) if hair else (.10, .16))
        if not encoded(low) <= min(alphas) <= max(alphas) <= encoded(high):
            raise ValueError("Surface response exceeded its restrained recipe range")
        metrics.append(dict(name=r["name"], rect_px=list(rect), smoothness_u8_min=min(alphas),
                            smoothness_u8_max=max(alphas), normal_xy_u8_max_deviation=max_deviation))
    return normal.png_bytes(), response.png_bytes(), metrics


def verify_binding_layout(binding: dict) -> None:
    seen, names = [], set()
    for r in binding["regions"]:
        if r["renderer"] in names:
            raise ValueError("Duplicate surface renderer: " + r["renderer"])
        names.add(r["renderer"])
        rect = tuple(r[k] for k in ("x_px", "y_px", "width_px", "height_px"))
        x, y, w, h = rect
        if min(x, y) < 0 or min(w, h) < 16 or max(x + w, y + h) > SIZE:
            raise ValueError("Invalid surface rectangle")
        for a, b, c, d in seen:
            if rect != (a, b, c, d) and x < a + c and a < x + w and y < b + d and b < y + h:
                raise ValueError("Partially overlapping surface atlas rectangles")
        seen.append(rect)


def publish_maps(check: bool = False) -> None:
    payload = json.loads(MODEL.read_text(encoding="utf-8"))
    expected = make_bindings(payload)
    if payload.get("surface_bindings") != expected:
        raise ValueError("Model surface bindings differ; run the Hero V2 --surfaces-only authoring pass")
    sources = {}
    for binding in (payload["face_atlas"], payload["texture_bindings"][0], payload["bare_skin_atlas"]):
        path = binding["texture_asset"]
        actual = digest((ROOT / path).read_bytes())
        if actual != binding["sha256"]:
            raise ValueError("Original colour atlas hash changed: " + path)
        sources[path] = actual
    outputs, recipes = {}, []
    for binding in expected:
        verify_binding_layout(binding)
        if binding["recipe"] == "Clothing":
            continue
        normal, response, measured = make_maps(binding)
        outputs[ROOT / binding["normal_asset"]] = normal
        outputs[ROOT / binding["response_asset"]] = response
        recipes.append(dict(recipe=binding["recipe"], normal_scale=binding["normal_scale"],
                            region_measurements=measured))
    report = dict(generator="tools/build-player-body-hair-surfaces.py", version=VERSION,
        source_model=MODEL.relative_to(ROOT).as_posix(),
        surface_bindings_sha256=digest(json_bytes(expected)), source_albedos_sha256=sources,
        uv_channel=0, uv_origin="bottom_left", normal_encoding="RGB tangent-space positive Y; A opaque",
        response_encoding="linear RGBA: R=metallic zero, G/B=reserved zero, A=absolute smoothness",
        normal_neutral_gutter_px=2, normal_fade_px=3, cap_neutral_patch_px=[12, 12],
        recipes=recipes, outputs={path.relative_to(ROOT).as_posix(): digest(data) for path, data in outputs.items()})
    outputs[REPORT] = json_bytes(report)
    for path, data in outputs.items():
        if check:
            if not path.is_file() or path.read_bytes() != data:
                raise ValueError("Stale deterministic output: " + path.relative_to(ROOT).as_posix())
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
    print("HERO BODY/HAIR SURFACES OK: " + ("deterministic maps, bindings and unchanged albedos verified" if check else
          "skin/hair normal and response maps published; original albedos unchanged"), flush=True)


def ring_uv(obj, rect: tuple, sides: int, poles: bool = False, root_to_tip: bool = False) -> None:
    """Unwrap existing ring topology without changing vertices or skin weights."""
    mesh = obj.data
    if mesh.uv_layers.get("UVMap") is not None:
        mesh.uv_layers.remove(mesh.uv_layers["UVMap"])
    uv = mesh.uv_layers.new(name="UVMap")
    body_vertices = len(mesh.vertices) - (2 if poles else 0)
    if body_vertices % sides:
        raise ValueError("Unexpected ring topology: " + obj.name)
    rings = body_vertices // sides
    x, y, width, height = rect
    for face in mesh.polygons:
        band = [i for i in face.vertices if i < body_vertices]
        seam = any(i % sides == 0 for i in band) and any(i % sides == sides - 1 for i in band)
        around = {i: (sides if seam and i % sides == 0 else i % sides) / sides for i in band}
        mean_u = sum(around.values()) / max(1, len(around))
        cap = not poles and len({i // sides for i in face.vertices}) == 1
        for li in face.loop_indices:
            vi = mesh.loops[li].vertex_index
            if cap:
                angle = vi % sides * math.tau / sides
                # A small genuine disk keeps nonzero UV area, inside the data
                # maps' neutral 12x12 corner. Existing body UV caps stay intact.
                pu, pv = 6 + math.cos(angle) * 3, 6 + math.sin(angle) * 3
            else:
                if vi >= body_vertices:
                    u, v = mean_u, float(vi > body_vertices)
                else:
                    u = around[vi]
                    v = (vi // sides + (1 if poles else 0)) / max(1, rings + (1 if poles else -1))
                if root_to_tip:
                    v = 1 - v
                pu, pv = 1 + u * (width - 2), 1 + v * (height - 2)
            uv.data[li].uv = ((x + pu) / SIZE, (y + pv) / SIZE)
    uv.active_render = True


def nape_uv(obj, rect: tuple) -> None:
    """The existing nape shell is two 25x5 grids, already ordered root to tip."""
    mesh = obj.data
    if len(mesh.vertices) != 250:
        raise ValueError("Nape surface grid changed")
    if mesh.uv_layers.get("UVMap") is not None:
        mesh.uv_layers.remove(mesh.uv_layers["UVMap"])
    uv = mesh.uv_layers.new(name="UVMap")
    x, y, width, height = rect
    for face in mesh.polygons:
        indices = [i % 125 for i in face.vertices]
        # Rim closure faces use the neutral corner instead of collapsed UVs.
        rim = len(set(indices)) < len(indices)
        for n, li in enumerate(face.loop_indices):
            i = mesh.loops[li].vertex_index % 125
            if rim:
                a = n * math.tau / len(face.loop_indices)
                u, v = 6 + math.cos(a) * 3, 6 + math.sin(a) * 3
            else:
                u, v = 1 + (i % 25) / 24 * (width - 2), 1 + (i // 25) / 4 * (height - 2)
            uv.data[li].uv = ((x + u) / SIZE, (y + v) / SIZE)
    uv.active_render = True


def author(result, bare_regions: list[dict]) -> list[str]:
    ready = {r["renderer"] for r in bare_regions}
    changed = []
    for part in result.parts:
        obj, name = part.obj, part.obj.name
        material = obj.data.materials[0].name
        if name == "GEO_FaceSurface" or name in ready:
            continue
        if material in SKIN_MATERIALS:
            key = extra_skin_region(name)
            rect = EXTRA_SKIN_RECTS[key]
            if name.startswith("GEO_Ear."):
                sides, poles = 6, True
            elif name.startswith("GEO_Shoulder."):
                sides, poles = (10, False) if obj.get("bp_shoulder_connector") else (12, True)
            elif name == "GEO_Head":
                sides, poles = 12, True
            elif name == "GEO_Neck":
                sides, poles = 12, False
            elif name.startswith("GEO_Hand."):
                sides, poles = 10, False
            elif name.startswith(("GEO_Finger", "GEO_Thumb.")):
                sides, poles = 7, False
            elif name.startswith(("GEO_UpperArm.", "GEO_Forearm.")) and obj.get("bp_joint_surface"):
                sides, poles = 10, False
            else:
                sides, poles = 8, False
            ring_uv(obj, rect, sides, poles)
        elif part.role == "hair" or material.startswith("MAT_Hair"):
            key = hair_region(name)
            rect = HAIR_RECTS[key]
            if name == "GEO_HairNape":
                nape_uv(obj, rect)
            else:
                if "Strand" in name:
                    sides, poles, reverse = 4, False, False
                elif name.startswith("GEO_HairCurtain"):
                    sides, poles, reverse = 12, False, False
                elif name == "GEO_HairBackLength":
                    sides, poles, reverse = 20, False, False
                elif name.endswith("Length"):
                    sides, poles, reverse = 26, False, False
                elif name == "GEO_HairCap":
                    sides, poles, reverse = 12, True, True
                elif name == "GEO_HairBack":
                    sides, poles, reverse = 10, True, True
                elif name.startswith("GEO_HairTemple."):
                    sides, poles, reverse = 8, True, True
                else:
                    raise ValueError("Unregistered hair topology: " + name)
                ring_uv(obj, rect, sides, poles, reverse)
        else:
            continue
        obj["bp_surface_region"] = key
        changed.append(name)
    return changed


def validate_uvs(result, bindings: list[dict]) -> None:
    records = {p.obj.name: p.obj for p in result.parts}
    for binding in bindings:
        verify_binding_layout(binding)
        for r in binding["regions"]:
            obj = records[r["renderer"]]
            uv = obj.data.uv_layers.get("UVMap")
            if uv is None or not uv.data:
                raise ValueError("Missing surface UV0: " + obj.name)
            lo = ((r["x_px"] + 1) / SIZE, (r["y_px"] + 1) / SIZE)
            hi = ((r["x_px"] + r["width_px"] - 1) / SIZE,
                  (r["y_px"] + r["height_px"] - 1) / SIZE)
            for loop in uv.data:
                if not all(math.isfinite(loop.uv[a]) and lo[a] - 1e-6 <= loop.uv[a] <= hi[a] + 1e-6 for a in (0, 1)):
                    raise ValueError("Surface UV outside its reserved cell: " + obj.name)


def refresh(module, config) -> None:
    """Publish UV-only changes from the verified source, preserving the full action bank."""
    import bpy
    original = json.loads(module.DEFAULT_MANIFEST.read_text(encoding="utf-8"))
    bpy.ops.wm.open_mainfile(filepath=str(module.DEFAULT_OUTPUT))
    rig = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
    rig.animation_data_create().action = None
    parts = [module.common.PartRecord(bpy.data.objects[p["name"]], p["role"], p["bone"], p["sprite_part"], p["side"])
             for p in original["parts"]]
    ordered = [n for item in original["wardrobe"]["items"] for n in item["renderers"]]
    ordered += [n for chain in original["hair"]["chains"] for n in chain["renderers"]]
    order = {name: i for i, name in enumerate(ordered)}
    parts.sort(key=lambda part: order.get(part.obj.name, len(order)))
    actions = {p["name"]: module.common.ActionRecord(bpy.data.actions[p["name"]], p["category"],
        p["duration_seconds"], p["loop"], p["source_frame_count"], p["source_fps"]) for p in original["actions"]}
    result = module.common.BuildResult(rig.parent, rig, {}, {}, parts=parts, actions=actions)
    result.authored_anchors = [obj for obj in bpy.data.objects if obj.name.startswith("ANCHOR_HandGrip")]
    builder = module.HeroV2Builder(config, module.DEFAULT_FACE_ATLAS, module.DEFAULT_CLOTHING_ATLAS)
    builder.result = result
    builder._reset_pose()
    hashes = (original["face_atlas"]["sha256"], original["texture_bindings"][0]["sha256"],
              original["bare_skin_atlas"]["sha256"])
    signature = lambda ignored=frozenset(): module.content_signature(config, result, *hashes, ignored_uv_names=ignored)
    if signature() != original["content_signature_sha256"]:
        raise RuntimeError("Production hero source differs from its manifest; refusing a stale UV refresh")
    old_uv = {p.obj.name: ([tuple(v.uv) for v in p.obj.data.uv_layers["UVMap"].data]
              if p.obj.data.uv_layers.get("UVMap") else []) for p in parts}
    bindings = make_bindings(original)
    changed_names = {r["renderer"] for b in bindings if b["recipe"] != "Clothing" for r in b["regions"]}
    preserved = signature(changed_names)
    changed = author(result, original["bare_skin_atlas"]["regions"])
    first = signature()
    author(result, original["bare_skin_atlas"]["regions"])
    if signature() != first or signature(changed_names) != preserved:
        raise RuntimeError("Surface UV refresh changed model geometry/rig/weights/actions or was nondeterministic")
    for part in parts:
        if part.obj.name not in changed:
            uv = part.obj.data.uv_layers.get("UVMap")
            current = [tuple(v.uv) for v in uv.data] if uv else []
            if current != old_uv[part.obj.name]:
                raise RuntimeError("Surface refresh altered an existing body/clothing/face UV")
    validate_uvs(result, bindings)
    original["surface_bindings"] = bindings
    original["surface_authoring"] = dict(generator="tools/player_body_hair_surfaces.py", version=VERSION,
        sha256=digest(Path(__file__).read_bytes()), geometry_rig_actions_preserved_sha256=preserved,
        changed_uv_renderers=sorted(changed), original_albedos_unchanged=True)
    original["content_signature_sha256"] = signature()
    result.root["bp_content_signature_sha256"] = original["content_signature_sha256"]
    bpy.context.scene["bp_content_signature_sha256"] = original["content_signature_sha256"]
    print("Hero surfaces: deterministic UVs; original geometry, rigs, weights, actions and existing UVs preserved", flush=True)
    module.common.export_fbx(config.fbx, result)
    module.common.save_blend(config.output)
    config.manifest.parent.mkdir(parents=True, exist_ok=True)
    config.manifest.write_bytes(json_bytes(original))
    print("Hero surfaces: model FBX, source and surface binding manifest exported; animations untouched", flush=True)
