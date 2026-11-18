"""Shared UV0 fabric, leather, skin and hair for the two catalog bodies."""

from functools import lru_cache
import hashlib
import math

from atlas_kit import PixelCanvas, uv_rect_normalized


SIZE = 512
TILE = 64
GUTTER = 6
FACE = "GEO_FaceSurface"
MATERIAL = "DefaultNpcDetail"
MAP_NAMES = (MATERIAL + "Normal.png", MATERIAL + "Response.png")
SCHEMA = "default_npc_surfaces_v1"

# A tile is (structure, peak relief in texels, smoothness, wear). Each is a
# separate finish, even where two pieces touch. The last atlas quarter
# contains the worker's existing face and blink cells.
FINISHES = {
    "skin": ("skin", .16, .24, .14),
    "shortHair": ("hair", .46, .36, .10),
    "looseHair": ("hair", .55, .36, .12),
    "boundHair": ("hair", .42, .38, .09),
    "shirt": ("plain", .32, .22, .20),
    "workShirt": ("twill", .47, .17, .24),
    "flannel": ("brushed", .48, .14, .20),
    "vest": ("canvas", .57, .18, .24),
    "workJacket": ("canvas", .65, .17, .29),
    "quilt": ("quilt", .69, .21, .23),
    "cardigan": ("knit", .78, .12, .18),
    "woolCoat": ("brushed", .59, .14, .25),
    "overshirt": ("twill", .43, .22, .22),
    "oilcloth": ("canvas", .38, .31, .26),
    "uniform": ("twill", .39, .24, .20),
    "tee": ("plain", .36, .16, .21),
    "shortJacket": ("canvas", .52, .23, .21),
    "slacks": ("twill", .39, .18, .22),
    "workTrousers": ("twill", .58, .17, .32),
    "winterTrousers": ("brushed", .51, .14, .21),
    "shoe": ("leather", .37, .34, .23),
    "workBoot": ("leather", .57, .26, .35),
    "winterBoot": ("leather", .46, .29, .28),
    "rubber": ("rubber", .48, .14, .28),
    "cap": ("twill", .41, .19, .21),
    "rib": ("rib", .61, .13, .19),
    "scarf": ("knit", .69, .12, .15),
    "glove": ("leather", .50, .21, .27),
    "apron": ("canvas", .60, .16, .32),
    "binding": ("binding", .51, .19, .24),
    "hardware": ("hardware", .19, .41, .22),
    "eyestay": ("eyelet", .42, .32, .20),
}

def tile_rect(finish):
    index = tuple(FINISHES).index(finish)
    # Leave the original neutral swatch at column 7 of the first row.
    index += index >= 7
    return index % 8 * TILE, SIZE - (1 + index // 8) * TILE


def tile_uv(finish, u, v):
    x, y = tile_rect(finish)
    lo_u, lo_v, hi_u, hi_v = uv_rect_normalized(x, y, TILE, TILE, SIZE, GUTTER + .5)
    return lo_u + u * (hi_u - lo_u), lo_v + v * (hi_v - lo_v)


def assign_chart(part, finish):
    """Project locally across the dominant face plane with a padded UV chart."""
    obj = part.obj
    mesh = obj.data
    uv = mesh.uv_layers.active or mesh.uv_layers.new(name="UVMap")
    uv.active_render = True
    obj["bp_default_npc_finish"] = finish
    x, y = tile_rect(finish)
    u0, v0, u1, v1 = uv_rect_normalized(x, y, TILE, TILE, SIZE, GUTTER + .5)
    low = [min(vertex.co[axis] for vertex in mesh.vertices) for axis in range(3)]
    high = [max(vertex.co[axis] for vertex in mesh.vertices) for axis in range(3)]
    spans = [max(.00001, maximum - minimum) for minimum, maximum in zip(low, high)]
    for polygon in mesh.polygons:
        axis = max(range(3), key=lambda index: abs(polygon.normal[index]))
        vertical = 1 if axis == 2 else 2
        across = 1 if axis == 0 else 0
        for index in polygon.loop_indices:
            point = mesh.vertices[mesh.loops[index].vertex_index].co
            u = (point[across] - low[across]) / spans[across]
            v = (point[vertical] - low[vertical]) / spans[vertical]
            uv.data[index].uv = (u0 + u * (u1 - u0), v0 + v * (v1 - v0))


def finish_for(part, item_finishes, item):
    name = part.obj.name
    if part.role == "hair":
        return item_finishes[item] if item else "shortHair"
    if not item:
        return "skin"
    if "Sole" in name or "Sock" in name:
        return "rubber" if "Sole" in name else "rib"
    if "Eyestay" in name:
        return "eyestay"
    if any(word in name for word in ("Button", "Fastening")):
        return "hardware"
    if any(word in name for word in ("Cuff", "Rib", "KnitCap")):
        return "rib" if item.startswith(("shirt.", "headwear.")) or "Cardigan" in name else "binding"
    if any(word in name for word in ("Placket", "Waistband", "Fold", "Binding", "Facing", "Strap", "Pocket", "Flap")):
        return "binding"
    return item_finishes[item]


def profile(kind, x, y):
    """Low relief shaped by the manufacture and use of each material."""
    u, v = x / (TILE - 1), y / (TILE - 1)
    ripple = math.sin(u * 15 + v * 7) * math.sin(v * 12 - u * 2)
    grain = ((x * 13 + y * 7 + x * y * 3) % 17 - 8) / 8
    thread = math.cos(x * 2.03) * math.cos(y * 2.38)
    if kind == "hair":
        locks = math.cos(x * .86 + 1.9 * math.sin(v * 4.5 + u * 3.))
        return .54 * locks + .21 * math.cos(x * 2.03 + v * 5) + .15 * ripple
    if kind == "knit" or kind == "rib":
        rib = math.cos(x * .91 + (.4 * math.sin(y * .92) if kind == "knit" else 0.))
        return .57 * rib + .29 * math.cos(y * 1.94) * rib + .14 * ripple
    if kind == "quilt":
        channel = math.cos(y * .57)
        return .65 * channel + .19 * thread + .16 * ripple
    if kind == "twill" or kind == "brushed":
        diagonal = math.sin(x * 1.57 + y * .89)
        return .38 * diagonal + .22 * thread + .27 * ripple + .13 * grain
    if kind == "leather":
        return .32 * grain + .23 * math.sin(x * 2.1 + math.cos(y * 1.7)) + .45 * ripple
    if kind == "rubber":
        return .43 * math.cos(x * 1.05) + .28 * math.cos(y * 1.18) + .29 * grain
    if kind == "binding":
        seam = math.exp(-((u - .10) / .032)**2) + math.exp(-((u - .90) / .032)**2)
        stitch = (1. if y % 7 < 4 else -.4) * seam
        return .55 * stitch + .28 * thread + .17 * ripple
    if kind == "eyelet":
        rail = math.exp(-((abs(u - .5) - .23) / .07)**2)
        gap = ((y + 4) % 12 - 6) / 4
        eyelet = rail * math.exp(-gap * gap)
        return .70 * eyelet + .23 * math.exp(-((u - .5) / .27)**2) * math.cos(y * .52) + .07 * grain
    if kind == "skin" or kind == "hardware":
        return .21 * grain + .79 * ripple
    return .42 * thread + .21 * math.cos(y * 1.83) + .27 * ripple + .10 * grain


@lru_cache(maxsize=1)
def painted_fields():
    base = PixelCanvas(SIZE, SIZE)
    normal = PixelCanvas(SIZE, SIZE)
    response = PixelCanvas(SIZE, SIZE)
    base.rect(0, 0, SIZE, SIZE, (255, 255, 255, 255))
    normal.rect(0, 0, SIZE, SIZE, (128, 128, 255, 255))
    response.rect(0, 0, SIZE, SIZE, (0, 0, 0, 54))
    for finish, (kind, relief, smooth, wear) in FINISHES.items():
        ox, bottom = tile_rect(finish)
        oy = SIZE - bottom - TILE
        heights = [[profile(kind, x, y) for x in range(TILE)] for y in range(TILE)]
        for y in range(TILE):
            sy = min(TILE - GUTTER - 1, max(GUTTER, y))
            for x in range(TILE):
                sx = min(TILE - GUTTER - 1, max(GUTTER, x))
                edge = math.exp(-((sx - 18) / 12)**2 - ((sy - 17) / 17)**2)
                fade = math.exp(-((sx - 44) / 15)**2 - ((sy - 46) / 14)**2)
                height = heights[sy][sx]
                pigment = 251 if kind == "skin" else 242
                tone = round(pigment + 6 * height + wear * (16 * edge - 28 * fade))
                tone = min(tone, 255)
                # The PNG runs downward; tangent V runs upward.
                dx = (heights[sy][sx - 1] - heights[sy][sx + 1]) * relief * .30
                dy = (heights[sy + 1][sx] - heights[sy - 1][sx]) * relief * .30
                length = math.sqrt(dx * dx + dy * dy + 1.)
                vector = tuple(round(127.5 + 127.5 * axis / length) for axis in (dx, dy, 1.))
                polish = smooth + .035 * height + wear * (.21 * edge - .12 * fade)
                base.put(ox + x, oy + y, (tone, tone, tone, 255))
                normal.put(ox + x, oy + y, (*vector, 255))
                response.put(ox + x, oy + y, (0, round(edge * wear * 255),
                                              round(fade * wear * 255), round(polish * 255)))
    return bytes(base.pixels), normal.png_bytes(), response.png_bytes()


def albedo():
    pixels, _, _ = painted_fields()
    canvas = PixelCanvas(SIZE, SIZE)
    canvas.pixels[:] = pixels
    return canvas


def publish_maps(folder, verify, publish):
    return [publish(folder / name, data, verify)
            for name, data in zip(MAP_NAMES, painted_fields()[1:])]


def manifest(result, items, item_finishes, map_hashes=None):
    parts = {part.obj.name: part.obj for part in result.parts}
    if set(item_finishes) != {item["id"] for item in items}:
        raise RuntimeError("Every default NPC wardrobe item needs an explicit surface finish")
    if set(item_finishes.values()) - FINISHES.keys():
        raise RuntimeError("Unknown finish in the wardrobe surface table")
    coverage = {name: item["id"] for item in items for name in item["renderers"]}
    finish_by_part = {name: str(obj.get("bp_default_npc_finish", "")) for name, obj in parts.items()}
    regions = []
    for name, obj in parts.items():
        if name == FACE:
            continue
        finish = finish_by_part[name]
        if finish not in FINISHES:
            raise RuntimeError("Unfinished default NPC surface: " + name)
        x, y = tile_rect(finish)
        width = height = TILE
        for loop in obj.data.uv_layers.active.data:
            u, v = (value * SIZE for value in loop.uv)
            if not (x <= u <= x + width and y <= v <= y + height):
                raise RuntimeError("Default NPC surface outside chart: " + name)
        regions.append({"name": finish, "renderer": name, "x_px": x, "y_px": y, "width_px": width, "height_px": height})
    for name, item in coverage.items():
        if not finish_by_part[name]:
            raise RuntimeError("Unfinished garment: " + item + "/" + name)
    normal, response = map_hashes or [hashlib.sha256(data).hexdigest() for data in painted_fields()[1:]]
    root = "Assets/Resources/VillageLife/"
    return {"schema": SCHEMA, "item_finishes": item_finishes, "binding": {
        "recipe": "Clothing", "normal_asset": root + MAP_NAMES[0], "response_asset": root + MAP_NAMES[1],
        "normal_sha256": normal, "response_sha256": response, "width_px": SIZE, "height_px": SIZE,
        "uv_channel": 0, "uv_origin": "bottom_left", "normal_scale": .65,
        "base_color_mode": "existing_palette", "regions": regions,
    }}
