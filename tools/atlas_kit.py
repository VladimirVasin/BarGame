"""Shared pixel-atlas kit for the character generators.

Why this module exists: the Hero V2 generator grew a small, dependency-free
texture pipeline - a `PixelCanvas` that paints RGBA pixels and writes them as
a PNG by hand, a reader that gets those pixels back for validation, and UV
helpers that lay a primitive's loops into one bottom-left pixel sub-rect of
an atlas.  The City pedestrian library now needs exactly the same pipeline
for its first textured design, and copying two hundred lines of PNG and UV
arithmetic into a second file would let the two drift apart.

Everything here is deliberately generic: a helper takes the atlas size and
the region rectangle it is given rather than looking a region name up in a
generator's own table, so the Hero V2 wrappers and the pedestrian generator
can each keep their own region tables and custom-property names.

What this module deliberately does NOT do: it paints nothing itself (every
picture belongs to the generator that owns the design), it imports nothing
from any generator, and it never touches Blender materials - the preview
shading and the Unity import contract stay with their generators.

The PNG and canvas functions were moved here verbatim from
`tools/build-player-3d-model-v2.py`; that generator now calls these through
thin wrappers so its atlases and manifest stay byte-identical.
"""

from __future__ import annotations

import binascii
import math
import os
import struct
import zlib
from pathlib import Path
from typing import Sequence

try:
    import bpy
except ImportError:  # pragma: no cover - the PNG half is plain Python.
    bpy = None  # type: ignore[assignment]


# --- PNG canvas, moved verbatim from the Hero V2 generator ------------------


def png_chunk(kind: bytes, payload: bytes) -> bytes:
    checksum = binascii.crc32(kind)
    checksum = binascii.crc32(payload, checksum) & 0xFFFFFFFF
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", checksum)


class PixelCanvas:
    def __init__(self, width: int, height: int):
        self.width = width
        self.height = height
        self.pixels = bytearray(width * height * 4)

    def put(self, x: int, y: int, color: tuple[int, int, int, int]) -> None:
        if not (0 <= x < self.width and 0 <= y < self.height):
            return
        offset = (y * self.width + x) * 4
        self.pixels[offset : offset + 4] = bytes(color)

    def rect(
        self,
        x0: int,
        y0: int,
        x1: int,
        y1: int,
        color: tuple[int, int, int, int],
    ) -> None:
        for y in range(y0, y1):
            for x in range(x0, x1):
                self.put(x, y, color)

    def line(
        self,
        x0: int,
        y0: int,
        x1: int,
        y1: int,
        color: tuple[int, int, int, int],
        thickness: int = 1,
    ) -> None:
        dx = abs(x1 - x0)
        sx = 1 if x0 < x1 else -1
        dy = -abs(y1 - y0)
        sy = 1 if y0 < y1 else -1
        error = dx + dy
        while True:
            radius = max(0, thickness - 1)
            self.rect(x0 - radius, y0 - radius, x0 + radius + 1, y0 + radius + 1, color)
            if x0 == x1 and y0 == y1:
                break
            doubled = 2 * error
            if doubled >= dy:
                error += dy
                x0 += sx
            if doubled <= dx:
                error += dx
                y0 += sy

    def ellipse(
        self,
        center_x: int,
        center_y: int,
        radius_x: int,
        radius_y: int,
        color: tuple[int, int, int, int],
    ) -> None:
        if radius_x <= 0 or radius_y <= 0:
            return
        for y in range(center_y - radius_y, center_y + radius_y + 1):
            normalized_y = (y - center_y) / radius_y
            span = radius_x * math.sqrt(max(0.0, 1.0 - normalized_y * normalized_y))
            self.rect(
                math.ceil(center_x - span),
                y,
                math.floor(center_x + span) + 1,
                y + 1,
                color,
            )

    def png_bytes(self) -> bytes:
        """The exact PNG payload `write_png` writes, without touching disk.

        A generator's determinism rerun re-paints its atlas into a buffer
        and compares hashes instead of overwriting the file its manifest
        has already hashed.
        """

        stride = self.width * 4
        raw = bytearray()
        for y in range(self.height):
            raw.append(0)
            start = y * stride
            raw.extend(self.pixels[start : start + stride])
        payload = bytearray(b"\x89PNG\r\n\x1a\n")
        payload.extend(
            png_chunk(
                b"IHDR",
                struct.pack(">IIBBBBB", self.width, self.height, 8, 6, 0, 0, 0),
            )
        )
        payload.extend(png_chunk(b"IDAT", zlib.compress(bytes(raw), 9)))
        payload.extend(png_chunk(b"IEND", b""))
        return bytes(payload)

    def write_png(self, path: Path) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        payload = self.png_bytes()
        temporary = path.with_suffix(path.suffix + ".tmp")
        temporary.write_bytes(payload)
        os.replace(temporary, path)


def rgba_from_hex(value: str) -> tuple[int, int, int, int]:
    value = value.lstrip("#")
    return (
        int(value[0:2], 16),
        int(value[2:4], 16),
        int(value[4:6], 16),
        255,
    )


def atlas_rect_bottom_left(
    canvas: PixelCanvas,
    x0: int,
    y0: int,
    x1: int,
    y1: int,
    color: tuple[int, int, int, int],
) -> None:
    canvas.rect(x0, canvas.height - y1, x1, canvas.height - y0, color)


def atlas_line_bottom_left(
    canvas: PixelCanvas,
    x0: int,
    y0: int,
    x1: int,
    y1: int,
    color: tuple[int, int, int, int],
    thickness: int = 1,
) -> None:
    canvas.line(
        x0,
        canvas.height - 1 - y0,
        x1,
        canvas.height - 1 - y1,
        color,
        thickness,
    )


def read_generated_png(path: Path) -> tuple[int, int, bytes]:
    """Read the filter-0 RGBA PNGs emitted by PixelCanvas."""

    payload = path.read_bytes()
    return decode_generated_png(payload, str(path))


def decode_generated_png(payload: bytes, label: str = "<buffer>") -> tuple[int, int, bytes]:
    """The body of `read_generated_png`, for a PNG that never touched disk."""

    if not payload.startswith(b"\x89PNG\r\n\x1a\n"):
        raise RuntimeError(f"Generated texture is not PNG: {label}")
    cursor = 8
    width = height = 0
    compressed = bytearray()
    while cursor < len(payload):
        length = struct.unpack(">I", payload[cursor : cursor + 4])[0]
        kind = payload[cursor + 4 : cursor + 8]
        data = payload[cursor + 8 : cursor + 8 + length]
        cursor += 12 + length
        if kind == b"IHDR":
            width, height, depth, color_type, _, _, _ = struct.unpack(">IIBBBBB", data)
            if depth != 8 or color_type != 6:
                raise RuntimeError("Generated atlas must be 8-bit RGBA")
        elif kind == b"IDAT":
            compressed.extend(data)
        elif kind == b"IEND":
            break
    raw = zlib.decompress(bytes(compressed))
    stride = width * 4
    pixels = bytearray(width * height * 4)
    for row in range(height):
        source = row * (stride + 1)
        if raw[source] != 0:
            raise RuntimeError("PixelCanvas PNG unexpectedly uses a filtered row")
        pixels[row * stride : (row + 1) * stride] = raw[source + 1 : source + 1 + stride]
    return width, height, bytes(pixels)


def png_pixel_bottom_left(
    pixels: bytes,
    width: int,
    height: int,
    x: int,
    y: int,
) -> tuple[int, int, int, int]:
    top_row = height - 1 - y
    offset = (top_row * width + x) * 4
    return tuple(pixels[offset : offset + 4])


def count_rect_color(
    pixels: bytes,
    width: int,
    height: int,
    rect_px: Sequence[int],
    colors: set[tuple[int, int, int, int]],
) -> int:
    """How many pixels of a bottom-left sub-rect carry one of `colors`."""

    x, y, region_width, region_height = rect_px
    return sum(
        png_pixel_bottom_left(pixels, width, height, px, py) in colors
        for py in range(y, y + region_height)
        for px in range(x, x + region_width)
    )


# --- UV helpers, parameterised by rectangle and atlas size -----------------


def uv_rect_normalized(
    x: int,
    y: int,
    width: int,
    height: int,
    atlas_size: int,
    inset_px: float = 1.0,
) -> tuple[float, float, float, float]:
    """A bottom-left pixel rect as (u0, v0, u1, v1) with a safe inset."""

    size = float(atlas_size)
    return (
        (x + inset_px) / size,
        (y + inset_px) / size,
        (x + width - inset_px) / size,
        (y + height - inset_px) / size,
    )


def assign_ring_strip_uv(
    obj: "bpy.types.Object",
    rect_uv: tuple[float, float, float, float],
    sides: int,
    ring_count: int,
    region_name: str,
    prop_name: str,
) -> None:
    """Seam-aware UV strip for closed frusta/ringed volumes.

    Vertex `i` sits on ring `i // sides` at station `i % sides`, which is the
    layout of every `make_frustum_between`.  Caps fold to a corner texel.
    """

    uv_layer = obj.data.uv_layers.new(name="UVMap")
    obj[prop_name] = region_name
    u0, v0, u1, v1 = rect_uv
    for polygon in obj.data.polygons:
        rings = {vertex_index // sides for vertex_index in polygon.vertices}
        is_cap = len(rings) == 1 and len(polygon.vertices) >= sides
        seam = any(vertex_index % sides == 0 for vertex_index in polygon.vertices) and any(
            vertex_index % sides == sides - 1 for vertex_index in polygon.vertices
        )
        for loop_index in polygon.loop_indices:
            vertex_index = obj.data.loops[loop_index].vertex_index
            ring_index = min(vertex_index // sides, ring_count - 1)
            around = vertex_index % sides
            if is_cap:
                local_u = 0.03 + 0.02 * (around / max(1, sides - 1))
                local_v = 0.03
            else:
                if seam and around == 0:
                    around = sides
                local_u = around / sides
                local_v = ring_index / max(1, ring_count - 1)
            uv_layer.data[loop_index].uv = (
                u0 + (u1 - u0) * local_u,
                v0 + (v1 - v0) * local_v,
            )
    uv_layer.active_render = True


def assign_box_panel_uv(
    obj: "bpy.types.Object",
    rect_px: Sequence[int],
    atlas_size: int,
    region_name: str,
    prop_name: str,
) -> None:
    """Split one region between side-panel and front/instep artwork by normal.

    The left half of the region dresses the faces seen from the side, the
    right half the faces whose normal points forward or up/down.  World
    space is Blender source space: -Y forward, +Z up.
    """

    uv_layer = obj.data.uv_layers.new(name="UVMap")
    obj[prop_name] = region_name
    x, y, width, height = rect_px
    points = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    min_x, max_x = min(p.x for p in points), max(p.x for p in points)
    min_y, max_y = min(p.y for p in points), max(p.y for p in points)
    min_z, max_z = min(p.z for p in points), max(p.z for p in points)
    size = float(atlas_size)
    for polygon in obj.data.polygons:
        normal = polygon.normal
        front_panel = abs(normal.z) > 0.50 or abs(normal.y) > 0.55
        panel_x = x + (width // 2 if front_panel else 0)
        for loop_index in polygon.loop_indices:
            point = points[obj.data.loops[loop_index].vertex_index]
            if front_panel:
                local_u = (point.x - min_x) / max(1e-6, max_x - min_x)
                local_v = (max_y - point.y) / max(1e-6, max_y - min_y)
            else:
                local_u = (max_y - point.y) / max(1e-6, max_y - min_y)
                local_v = (point.z - min_z) / max(1e-6, max_z - min_z)
            uv_layer.data[loop_index].uv = (
                (panel_x + 1 + local_u * (width // 2 - 2)) / size,
                (y + 1 + local_v * (height - 2)) / size,
            )
    uv_layer.active_render = True


def assign_ellipsoid_strip_uv(
    obj: "bpy.types.Object",
    rect_uv: tuple[float, float, float, float],
    segments: int,
    rings: int,
    region_name: str,
    prop_name: str,
) -> None:
    """Seam-aware latitude/longitude strip for a pole-capped ellipsoid.

    Expects the pedestrian `make_ellipsoid` layout: vertex 0 is the south
    pole, vertices `1 + (ring - 1) * segments + segment` form rings 1 to
    `rings - 1`, and the last vertex is the north pole.  U runs around the
    equator, V runs pole to pole.  A pole vertex has no longitude of its
    own, so each pole loop takes the mean longitude of the fan triangle it
    belongs to - the pole is folded into the strip instead of being pinned
    to one corner, which keeps every fan triangle a proper wedge of the
    painted band.
    """

    uv_layer = obj.data.uv_layers.new(name="UVMap")
    obj[prop_name] = region_name
    u0, v0, u1, v1 = rect_uv
    north_index = 1 + (rings - 1) * segments

    def ring_of(vertex_index: int) -> int:
        if vertex_index == 0:
            return 0
        if vertex_index >= north_index:
            return rings
        return 1 + (vertex_index - 1) // segments

    def around_of(vertex_index: int) -> int:
        return (vertex_index - 1) % segments

    for polygon in obj.data.polygons:
        band = [index for index in polygon.vertices if 0 < index < north_index]
        seam = any(around_of(index) == 0 for index in band) and any(
            around_of(index) == segments - 1 for index in band
        )
        band_u: list[float] = []
        for index in band:
            around = around_of(index)
            if seam and around == 0:
                around = segments
            band_u.append(around / segments)
        pole_u = sum(band_u) / max(1, len(band_u))
        for loop_index in polygon.loop_indices:
            vertex_index = obj.data.loops[loop_index].vertex_index
            if 0 < vertex_index < north_index:
                around = around_of(vertex_index)
                if seam and around == 0:
                    around = segments
                local_u = around / segments
            else:
                local_u = pole_u
            local_v = ring_of(vertex_index) / rings
            uv_layer.data[loop_index].uv = (
                u0 + (u1 - u0) * local_u,
                v0 + (v1 - v0) * local_v,
            )
    uv_layer.active_render = True
