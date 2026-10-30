"""Source-derived wear for Hero V2's exact native clothing UV rectangles.

Paints spatial stains, worn seams, abrasion and closed stitched tears. The
original atlas and its normal/response companions remain immutable. Geometry,
alpha, the right shoulder's ochre repair and paired forearm painting are kept.
The ordinary generator owns the UV rectangles: read its literal rather than
keeping a second table or importing Blender into this texture-only tool.
"""
from __future__ import annotations

import ast
import hashlib
import math
import re
from pathlib import Path

from atlas_kit import PixelCanvas, read_generated_png

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Assets/Player3D/V2/Textures/PlayerClothingAtlas.png"
OUTPUT = ROOT / "Assets/Resources/Player/Corruption"
VERSION = "hero_corruption_clothing_v2"


def regions() -> dict:
    source = ast.parse((ROOT / "tools/build-player-3d-model-v2.py").read_text())
    for node in source.body:
        if isinstance(node, ast.Assign) and any(
                isinstance(target, ast.Name) and target.id == "CLOTHING_REGIONS"
                for target in node.targets):
            return ast.literal_eval(node.value)
    raise RuntimeError("Hero generator lost its authored clothing UV table")


def clamp(value: float) -> float:
    return max(0., min(1., value))


def noise(x: int, y: int, seed: int) -> float:
    value = (x * 374761393 + y * 668265263 + seed * 2246822519) & 0xffffffff
    value = ((value ^ (value >> 13)) * 1274126177) & 0xffffffff
    return (value ^ (value >> 16)) / 4294967295.


def cloudy(x: float, y: float, seed: int) -> float:
    ix, iy = math.floor(x), math.floor(y)
    u, v = x - ix, y - iy
    u, v = u * u * (3. - 2. * u), v * v * (3. - 2. * v)
    a = noise(ix, iy, seed) * (1. - u) + noise(ix + 1, iy, seed) * u
    b = noise(ix, iy + 1, seed) * (1. - u) + noise(ix + 1, iy + 1, seed) * u
    return a * (1. - v) + b * v


def blot(u: float, v: float, cx: float, cy: float, rx: float, ry: float, seed: int) -> float:
    # Coherent broken boundaries create deposited grime rather than a dot print.
    radius = ((u - cx) / rx) ** 2 + ((v - cy) / ry) ** 2
    edge = cloudy(u * 19., v * 23., seed + 17)
    radius += (.53 - edge) * .95
    return clamp((1.10 - radius) * 1.7) * (.52 + .48 * cloudy(u * 11., v * 11., seed))


def mix(color, target, strength: float):
    t = clamp(strength)
    return tuple(round(a + (b - a) * t) for a, b in zip(color[:3], target)) + (color[3],)


def _strip(canvas: PixelCanvas, rect, x: int, y: int):
    _, ox, oy, width, height = rect
    # Atlas coordinates are bottom-left; PixelCanvas stores top-left rows.
    offset = ((canvas.height - 1 - (oy + y)) * canvas.width + ox + x) * 4
    return offset


def _repair(canvas, rect, points, amount, color):
    if amount <= .55:
        return
    _, ox, oy, width, height = rect
    strength = clamp((amount - .55) / .45)
    for index, (x, y) in enumerate(points):
        # A closed dark split with crossed thread; alpha never opens a false hole.
        for dy in (0, 1):
            offset = _strip(canvas, rect, x, y + dy)
            canvas.pixels[offset:offset + 4] = bytes(mix(canvas.pixels[offset:offset + 4],
                (27, 28, 22), strength * .85))
        if index % 3 == 1:
            for dx, dy in ((-1, -1), (0, 0), (1, 1), (2, 2)):
                px, py = x + dx, y + dy
                if 1 <= px < width - 1 and 1 <= py < height - 1:
                    offset = _strip(canvas, rect, px, py)
                    canvas.pixels[offset:offset + 4] = bytes(mix(
                        canvas.pixels[offset:offset + 4], color, strength * .85))


def paint(source, authored_regions, amount: float) -> PixelCanvas:
    width, height, pixels = source
    canvas = PixelCanvas(width, height)
    canvas.pixels[:] = pixels
    for name, rect in authored_regions.items():
        _, ox, oy, w, h = rect
        # Only the forearm pair shares an authored paint contract. Every other
        # garment part owns its deposits and abrasion; large knee/toe marks
        # cannot be mirrored copies masquerading as material grain.
        canonical = "JacketForearm" if name.startswith("JacketForearm") else name
        seed = int(hashlib.sha256(canonical.encode()).hexdigest()[:6], 16)
        boot = name.startswith("Boot")
        denim = name.startswith("Jeans")
        faded = (108, 107, 83) if not (boot or denim) else ((78, 83, 94) if denim else (83, 38, 42))
        dirty = (35, 36, 25) if not (boot or denim) else ((19, 23, 29) if denim else (27, 21, 20))
        scuff = (133, 130, 99) if not (boot or denim) else ((111, 119, 128) if denim else (112, 84, 65))
        thread = (159, 151, 119) if not denim else (141, 146, 145)
        for y in range(h):
            for x in range(w):
                # The repaired right shoulder remains its original ochre mark.
                if name == "JacketSleeveRight" and 20 <= x < 44 and 11 <= y < 30:
                    continue
                offset = _strip(canvas, rect, x, y)
                color = tuple(pixels[offset:offset + 4])
                u, v = x / (w - 1), y / (h - 1)
                cloud = cloudy(x / 9., y / 12., seed)
                speck = noise(x, y, seed)
                seam = 0.
                if name == "JacketBody":
                    panel_x = x % 64
                    # Rolled collar, pocket mouths/corners, lower hem and back yoke.
                    stains = max(blot(u, v, .11, .23, .12, .15, seed),
                        blot(u, v, .38, .12, .10, .13, seed + 1),
                        blot(u, v, .77, .15, .20, .15, seed + 2),
                        blot(u, v, .23, .91, .25, .11, seed + 3))
                    wear = max(blot(u, v, .08, .57, .08, .08, seed + 4),
                        blot(u, v, .42, .57, .08, .08, seed + 5),
                        blot(u, v, .75, .79, .24, .07, seed + 6))
                    seam = max(clamp(1. - abs(y - 10) / 3.),
                        clamp(1. - min(abs(panel_x - 8), abs(panel_x - 39)) / 2.) *
                        max(clamp(1. - abs(y - 37) / 16.), clamp(1. - abs(y - 80) / 14.)))
                elif name.startswith("JacketForearm"):
                    # Preserve paired sleeves without cloning conspicuous
                    # holes, stitched slashes or large spot-shaped landmarks.
                    # Cuff handling leaves only restrained continuous dirt.
                    stains = clamp((v - .76) / .24) * (.14 + .34 * cloud)
                    wear = clamp(1. - abs(v - .90) / .06) * .25
                    seam = clamp(1. - abs(y - (h - 5)) / 3.)
                elif name.startswith("JacketSleeve"):
                    stains = max(blot(u, v, .23, .21, .22, .19, seed),
                        blot(u, v, .68, .85, .30, .18, seed + 1))
                    wear = blot(u, v, .57, .81, .27, .20, seed + 2)
                    seam = clamp(1. - abs(x - 32) / 2.)
                elif name == "JeansPelvis":
                    stains = max(blot(u, v, .18, .66, .25, .24, seed),
                        blot(u, v, .80, .49, .26, .24, seed + 1))
                    wear = max(blot(u, v, .50, .19, .40, .16, seed + 2),
                        blot(u, v, .51, .88, .40, .12, seed + 3))
                    seam = clamp(1. - abs(x - 32) / 2.)
                elif name.startswith("JeansThigh"):
                    if name.endswith("Left"):
                        stains = max(blot(u, v, .23, .61, .17, .29, seed),
                            blot(u, v, .68, .40, .24, .15, seed + 1))
                        wear = max(blot(u, v, .51, .89, .28, .10, seed + 2),
                            blot(u, v, .29, .79, .12, .08, seed + 3))
                    else:
                        stains = max(blot(u, v, .73, .74, .18, .20, seed),
                            blot(u, v, .32, .29, .29, .15, seed + 1))
                        wear = max(blot(u, v, .60, .80, .18, .16, seed + 2),
                            blot(u, v, .80, .94, .11, .07, seed + 3))
                    seam = max(clamp(1. - abs(x - 2) / 2.), clamp(1. - abs(x - 32) / 2.))
                elif name.startswith("JeansShin"):
                    if name.endswith("Left"):
                        stains = max(blot(u, v, .22, .80, .19, .31, seed),
                            blot(u, v, .70, .66, .23, .13, seed + 1))
                        wear = blot(u, v, .36, .12, .24, .08, seed + 2)
                    else:
                        stains = max(blot(u, v, .58, .87, .39, .12, seed),
                            blot(u, v, .82, .53, .15, .25, seed + 1))
                        wear = max(blot(u, v, .63, .18, .26, .11, seed + 2),
                            blot(u, v, .29, .06, .12, .05, seed + 3))
                    seam = max(clamp(1. - abs(x - 2) / 2.), clamp(1. - abs(x - 32) / 2.))
                elif boot:
                    # Preserve yellow welt stitches, black laces and the eight eyelet rows.
                    if y <= 6 or (x >= 32 and y >= 31 and max(color[:3]) < 45):
                        continue
                    if name.endswith("Left"):
                        stains = max(blot(u, v, .17, .18, .23, .13, seed),
                            blot(u, v, .88, .28, .12, .21, seed + 1))
                        wear = max(blot(u, v, .20, .37, .23, .10, seed + 2),
                            blot(u, v, .77, .21, .12, .12, seed + 3))
                    else:
                        stains = max(blot(u, v, .26, .29, .17, .19, seed),
                            blot(u, v, .69, .13, .28, .08, seed + 1))
                        wear = max(blot(u, v, .76, .31, .22, .15, seed + 2),
                            blot(u, v, .16, .48, .19, .07, seed + 3))
                    seam = clamp(1. - abs(y - 12) / 2.)
                else:
                    raise RuntimeError("Unreviewed clothing UV region: " + name)
                color = mix(color, faded, amount * (.07 + .12 * cloud))
                color = mix(color, dirty, amount * stains * (.58 + .28 * cloud))
                color = mix(color, scuff, amount * wear * (.35 + .40 * speck))
                if speck < .39:
                    color = mix(color, thread if not boot else scuff,
                        clamp((amount - .08) / .92) * seam * (.28 + .56 * cloud))
                # Short parallel broken fibres read as fabric rubbed through at knees/elbows.
                if not boot and y % 3 == 0 and .18 < speck < .52:
                    color = mix(color, thread, clamp((amount - .20) / .80) * wear * .48)
                canvas.pixels[offset:offset + 4] = bytes(color)
        if name == "JeansThighLeft":
            _repair(canvas, rect, [(18 + i, 53 - i // 5) for i in range(15)], amount, thread)
        elif name == "JeansThighRight":
            _repair(canvas, rect, [(42 + i, 44 + i // 3) for i in range(11)], amount, thread)
        elif name == "JacketBody":
            _repair(canvas, rect, [(84 + i, 31 + i // 5) for i in range(22)], amount, thread)
    return canvas


def validate(canvas, source, authored_regions, amount):
    width, height, original = source
    if (width, height) != (256, 256) or (canvas.width, canvas.height) != (width, height):
        raise RuntimeError("Clothing variants must retain the original 256-square atlas")
    if canvas.pixels[3::4] != original[3::4]:
        raise RuntimeError("Clothing wear opened alpha holes")
    left, right = authored_regions["JacketForearmLeft"], authored_regions["JacketForearmRight"]
    for y in range(64):
        for x in range(64):
            a, b = _strip(canvas, left, x, y), _strip(canvas, right, x, y)
            if canvas.pixels[a:a + 4] != canvas.pixels[b:b + 4]:
                raise RuntimeError("Clothing wear broke paired forearm identity")
    patch = authored_regions["JacketSleeveRight"]
    for y in range(11, 30):
        for x in range(20, 44):
            i = _strip(canvas, patch, x, y)
            if canvas.pixels[i:i + 4] != original[i:i + 4]:
                raise RuntimeError("Clothing wear changed the hero's ochre repair")
    if amount == 1.:
        differences = [tuple(canvas.pixels[i + c] - original[i + c] for c in range(3))
            for i in range(0, len(original), 4)]
        # Both dark deposits and lifted abrasion must exist: a global tint fails.
        if sum(max(d) >= 20 for d in differences) < 300 or sum(min(d) <= -15 for d in differences) < 300:
            raise RuntimeError("Maximum clothing needs readable local abrasion AND dark grime")
        # Low-pass block means ignore textile pixels and require different
        # damage placement on the paired knees, lower legs and boot leather.
        for family in ("JeansThigh", "JeansShin", "Boot"):
            a, b = authored_regions[family + "Left"], authored_regions[family + "Right"]
            changes = []
            for by in range(4, 60, 8):
                for bx in range(4, 60, 8):
                    totals = [0., 0., 0.]
                    for y in range(by, by + 8):
                        for x in range(bx, bx + 8):
                            ia, ib = _strip(canvas, a, x, y), _strip(canvas, b, x, y)
                            for channel in range(3):
                                totals[channel] += ((canvas.pixels[ia + channel] - original[ia + channel]) -
                                    (canvas.pixels[ib + channel] - original[ib + channel])) / 64.
                    changes.extend(abs(total) for total in totals)
            if sum(changes) / len(changes) < 1.25:
                raise RuntimeError("Large garment wear must be individual, not paired copies: " + family)


def textures(output_dir: Path = OUTPUT, validate_only: bool = False) -> list[dict]:
    from player_corruption import STAGE_WEIGHTS
    source = read_generated_png(SOURCE)
    authored_regions = regions()
    source_hash = hashlib.sha256(SOURCE.read_bytes()).hexdigest()
    rows = []
    for stage, weight in enumerate(STAGE_WEIGHTS[1:], 2):
        amount = weight / 100.
        canvas = paint(source, authored_regions, amount)
        validate(canvas, source, authored_regions, amount)
        encoded = canvas.png_bytes()
        if encoded != paint(source, authored_regions, amount).png_bytes():
            raise RuntimeError("Non-deterministic clothing wear")
        destination = output_dir / f"Clothing{stage}.png"
        if validate_only:
            if destination.read_bytes() != encoded:
                raise RuntimeError("Stale corruption clothing atlas: " + str(destination))
        else:
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(encoded)
            meta = destination.with_suffix(".png.meta")
            if not meta.exists():
                template = SOURCE.with_suffix(".png.meta").read_text()
                logical_asset = f"Assets/Resources/Player/Corruption/Clothing{stage}.png"
                guid = hashlib.sha256(logical_asset.encode()).hexdigest()[:32]
                meta.write_text(re.sub(r"(?m)^guid: [0-9a-f]+$", "guid: " + guid, template))
        rows.append(dict(kind="Clothing", stage=stage,
            resource=f"Player/Corruption/Clothing{stage}", source_sha256=source_hash,
            sha256=hashlib.sha256(encoded).hexdigest(), width=source[0], height=source[1],
            authoring=VERSION, uv_layout="original_clothing_regions",
            preserve_patch_forearm_alpha=True, unique_limb_macro_damage=True,
            paired_forearm_cuff_only=True))
    return rows


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--update-manifest", action="store_true",
        help="Update only the six clothing texture rows in the existing hero manifest")
    args = parser.parse_args()
    if args.validate_only and args.update_manifest:
        parser.error("--validate-only cannot update the manifest")
    rows = textures(validate_only=args.validate_only)
    if args.update_manifest:
        import json
        path = ROOT / "Assets/Player3D/V2/Models/PlayerCharacter3DV2.json"
        payload = json.loads(path.read_text(encoding="utf-8"))
        contract = payload["hero_corruption"]
        contract["textures"] = [row for row in contract["textures"] if row["kind"] != "Clothing"] + rows
        path.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("HERO CLOTHING WEAR CONTRACT OK: unique limb damage; restrained paired cuffs, UV, patch and alpha preserved")
