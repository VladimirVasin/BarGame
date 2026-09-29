"""Shared deterministic, palette-neutral surface detail for ordinary NPCs.

The four cells describe materials, never character identity. Geometry and
existing renderer colours supply identity; the mother's expression surface
keeps its own atlas and must not be passed to ``apply_uv``.
"""
from __future__ import annotations

import hashlib
import struct
import zlib
from pathlib import Path

ATLAS_NAME = "OrdinaryCharacterDetailAtlas.png"
ASSET_PATH = "Assets/Pedestrians/Textures/" + ATLAS_NAME
SIZE = 256
CELL_SIZE = 128
# Pixel coordinates use Unity's bottom-left UV origin.
CELLS = {"skin_white": (0, 0), "hair": (128, 0),
         "cloth": (0, 128), "leather": (128, 128)}


def _grey(kind: str, x: int, y: int) -> int:
    if kind == "skin_white":
        return 255
    grain = ((x * 31 + y * 17 + x * y * 3) % 7) - 3
    if kind == "cloth":
        weave = 5 if (x + (y // 2)) % 4 == 0 else 0
        seam = 23 if x in (7, 8, 119, 120) else 0
        stitch = 12 if x in (10, 117) and y % 6 < 2 else 0
        fold = 7 if 47 <= x <= 50 or 86 <= x <= 88 else 0
        return max(0, min(255, 247 + grain - weave - seam - stitch - fold))
    if kind == "leather":
        crease = 12 if (x + y // 7) % 29 < 2 else 0
        stitch = 23 if y in (8, 119) and x % 7 < 4 else 0
        return max(0, min(255, 246 + grain - crease - stitch))
    strand = 19 if (x + y // 19) % 11 < 2 else 0
    highlight = 5 if (x + y // 19) % 11 in (4, 5) else 0
    return max(0, min(255, 244 + grain + highlight - strand))


def png_bytes() -> bytes:
    def chunk(kind: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xffffffff)
    scanlines = bytearray()
    by_cell = {position: kind for kind, position in CELLS.items()}
    for row in range(SIZE):
        y = SIZE - 1 - row
        scanlines.append(0)
        for x in range(SIZE):
            cell = (x // CELL_SIZE * CELL_SIZE, y // CELL_SIZE * CELL_SIZE)
            value = _grey(by_cell[cell], x - cell[0], y - cell[1])
            scanlines.extend((value, value, value, 255))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0)) +
            chunk(b"IDAT", zlib.compress(bytes(scanlines), 9)) + chunk(b"IEND", b""))


def publish(path: str | Path) -> Path:
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = png_bytes()
    if not path.exists() or path.read_bytes() != payload:
        path.write_bytes(payload)
    return path


def apply_uv(obj, kind: str) -> None:
    """Project each face into a safe material cell without changing its tint."""
    if kind not in CELLS:
        raise ValueError(f"Unknown ordinary NPC surface {kind!r}")
    if obj.name == "GEO_FaceSurface":
        raise ValueError("The expression surface must keep its own face UVs")
    mesh = obj.data
    uv = mesh.uv_layers.active or mesh.uv_layers.new(name="UVMap")
    minima = [min(vertex.co[axis] for vertex in mesh.vertices) for axis in range(3)]
    spans = [max(vertex.co[axis] for vertex in mesh.vertices) - minima[axis] for axis in range(3)]
    origin = CELLS[kind]
    for polygon in mesh.polygons:
        normal_axis = max(range(3), key=lambda axis: abs(polygon.normal[axis]))
        axes = [axis for axis in range(3) if axis != normal_axis]
        for index in polygon.loop_indices:
            co = mesh.vertices[mesh.loops[index].vertex_index].co
            local = [(co[axis] - minima[axis]) / max(spans[axis], 1e-8) for axis in axes]
            uv.data[index].uv = tuple((origin[axis] + 1 + local[axis] * (CELL_SIZE - 2)) / SIZE for axis in range(2))
    obj["bp_detail_kind"] = kind
    obj["bp_atlas_region"] = obj.name + ":" + kind


def texture_binding(path: str | Path, objects) -> dict:
    regions = []
    for obj in objects:
        kind = obj.get("bp_detail_kind")
        if kind is None:
            continue
        x, y = CELLS[kind]
        regions.append(dict(name=obj["bp_atlas_region"], renderer=obj.name,
                            x_px=x, y_px=y, width_px=CELL_SIZE, height_px=CELL_SIZE))
    return dict(texture_asset=ASSET_PATH, sha256=hashlib.sha256(Path(path).read_bytes()).hexdigest(),
                width_px=SIZE, height_px=SIZE, materials=[], shader_property="_BaseMap",
                color_space="sRGB", filter_mode="Point", wrap_mode="Clamp", mipmaps=False,
                compression="Uncompressed", uv_channel=0, uv_origin="bottom_left",
                uv_safe_inset_px=1, material_tint_hex="FFFFFF", tint_source="renderer_palette",
                regions=sorted(regions, key=lambda region: region["renderer"]))


def attach_preview(material, path: str | Path) -> None:
    """Use the same palette-times-atlas surface in the Blender review render."""
    import bpy
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    shader = next(node for node in nodes if node.type == "BSDF_PRINCIPLED")
    info = next((node for node in nodes if node.type == "OBJECT_INFO"), None) or nodes.new("ShaderNodeObjectInfo")
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = bpy.data.images.load(str(Path(path).resolve()), check_existing=True)
    texture.image.pack()
    texture.interpolation, texture.extension = "Closest", "EXTEND"
    mix = nodes.new("ShaderNodeMixRGB")
    mix.blend_type, mix.inputs[0].default_value = "MULTIPLY", 1.0
    for link in list(links):
        if link.to_socket == shader.inputs["Base Color"]:
            links.remove(link)
    links.new(info.outputs["Color"], mix.inputs[1])
    links.new(texture.outputs["Color"], mix.inputs[2])
    links.new(mix.outputs["Color"], shader.inputs["Base Color"])


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path(ASSET_PATH))
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    if args.validate_only:
        if not args.output.is_file() or args.output.read_bytes() != png_bytes():
            raise SystemExit("Ordinary character atlas differs from its deterministic source")
        print("Ordinary character detail atlas is deterministic (256x256, four material cells).")
    else:
        print(publish(args.output))
