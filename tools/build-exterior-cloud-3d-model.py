#!/usr/bin/env python3
"""Build the shared camera-relative exterior cloud dome and density sheet.

Run through Blender 5, not ordinary CPython::

    blender --background --factory-startup --python-exit-code 1 --python \
        tools/build-exterior-cloud-3d-model.py

The output is deliberately small: one unit-radius upper hemisphere, one
packed 256 px linear-data texture, and one manifest binding their measured
contracts.  Runtime code scales the dome to the active area's far plane and
supplies all colour, coverage, haze and motion through a property block.

The texture channels are independent density fields rather than colour:

* R - broad overcast masses;
* G - smaller lower-cloud breakup;
* B - erosion used to keep the two fields from reading as one blur.

All noise is periodic by construction.  The PNG writer is the project's
dependency-free deterministic ``atlas_kit`` pipeline, so Blender and Unity
validate the exact same file bytes recorded in the manifest.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from dataclasses import dataclass
from pathlib import Path
import sys
from typing import Sequence

try:
    import bpy
    from mathutils import Vector
except ImportError as error:  # pragma: no cover - Blender-only entry point.
    raise SystemExit(
        "This generator must run through Blender's bundled Python."
    ) from error


sys.path.insert(0, str(Path(__file__).resolve().parent))
import atlas_kit  # noqa: E402


GENERATOR_VERSION = "1.0.0"
DESIGN_ID = "exterior_cloud_dome_v1"
DISPLAY_NAME = "Exterior Cloud Dome"
SEED = 0xC10D5

MESH_NAME = "GEO_ExteriorCloudDome"
SOURCE_COLLECTION = "SOURCE_ExteriorCloudDome3D"
PRESENTATION_COLLECTION = "PRESENTATION_ExteriorCloudDome3D"

AZIMUTH_SEGMENTS = 20
RING_COUNT = 6
EXPECTED_VERTICES = 1 + AZIMUTH_SEGMENTS * RING_COUNT
EXPECTED_TRIANGLES = (
    AZIMUTH_SEGMENTS +
    (RING_COUNT - 1) * AZIMUTH_SEGMENTS * 2
)
MIN_TRIANGLES = 180
MAX_TRIANGLES = 260
UNIT_RADIUS_METERS = 1.0

TEXTURE_NAME = "ExteriorCloudDensity.png"
TEXTURE_SIZE = 256
CHANNEL_NAMES = ("broad", "detail", "erosion")


@dataclass(frozen=True)
class DomeGeometry:
    vertices: tuple[tuple[float, float, float], ...]
    faces: tuple[tuple[int, ...], ...]
    face_uvs: tuple[tuple[tuple[float, float], ...], ...]


@dataclass(frozen=True)
class TextureReport:
    payload: bytes
    sha256: str
    minimum: tuple[int, int, int]
    maximum: tuple[int, int, int]
    mean: tuple[float, float, float]
    seam_ratio: tuple[float, float, float]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(
            "ArtSource/Environment/Clouds/Blender/"
            "ExteriorCloudDome3D.blend"
        ),
    )
    parser.add_argument(
        "--fbx",
        type=Path,
        default=Path(
            "Assets/Environment/Clouds/Models/"
            "ExteriorCloudDome3D.fbx"
        ),
    )
    parser.add_argument(
        "--manifest",
        type=Path,
        default=Path(
            "Assets/Environment/Clouds/Models/"
            "ExteriorCloudDome3D.json"
        ),
    )
    parser.add_argument(
        "--texture",
        type=Path,
        default=Path(
            "Assets/Environment/Clouds/Textures/"
            f"{TEXTURE_NAME}"
        ),
    )
    parser.add_argument(
        "--preview",
        type=Path,
        default=Path(
            "ArtSource/Environment/Clouds/Blender/"
            "ExteriorCloudDome3D.png"
        ),
    )
    parser.add_argument("--no-preview", action="store_true")
    arguments = (
        sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    )
    config = parser.parse_args(arguments)
    for field_name in (
        "output", "fbx", "manifest", "texture", "preview"
    ):
        setattr(config, field_name, getattr(config, field_name).resolve())
    return config


def stable(value: float) -> float:
    return round(value + 0.0, 6)


def build_geometry() -> DomeGeometry:
    vertices: list[tuple[float, float, float]] = [(0.0, 0.0, 1.0)]
    for ring in range(1, RING_COUNT + 1):
        elevation = math.radians(90.0 - 90.0 * ring / RING_COUNT)
        horizontal = math.cos(elevation)
        height = math.sin(elevation)
        for segment in range(AZIMUTH_SEGMENTS):
            angle = math.tau * segment / AZIMUTH_SEGMENTS
            vertices.append(
                (
                    stable(math.cos(angle) * horizontal),
                    stable(math.sin(angle) * horizontal),
                    stable(height),
                )
            )

    def vertex_index(ring: int, segment: int) -> int:
        return 1 + (ring - 1) * AZIMUTH_SEGMENTS + (
            segment % AZIMUTH_SEGMENTS
        )

    faces: list[tuple[int, ...]] = []
    for segment in range(AZIMUTH_SEGMENTS):
        faces.append(
            (
                0,
                vertex_index(1, segment),
                vertex_index(1, segment + 1),
            )
        )
    for ring in range(1, RING_COUNT):
        for segment in range(AZIMUTH_SEGMENTS):
            faces.append(
                (
                    vertex_index(ring, segment),
                    vertex_index(ring + 1, segment),
                    vertex_index(ring + 1, segment + 1),
                    vertex_index(ring, segment + 1),
                )
            )

    face_uvs = tuple(
        tuple(
            (
                stable(vertices[index][0] * 0.5 + 0.5),
                stable(vertices[index][1] * 0.5 + 0.5),
            )
            for index in face
        )
        for face in faces
    )
    result = DomeGeometry(tuple(vertices), tuple(faces), face_uvs)
    validate_geometry(result)
    return result


def triangle_count(geometry: DomeGeometry) -> int:
    return sum(len(face) - 2 for face in geometry.faces)


def geometry_signature(geometry: DomeGeometry) -> str:
    digest = hashlib.sha256()
    digest.update(GENERATOR_VERSION.encode("utf-8"))
    digest.update(DESIGN_ID.encode("utf-8"))
    for vertex in geometry.vertices:
        digest.update(
            ",".join(f"{component:.6f}" for component in vertex).encode(
                "ascii"
            )
        )
    for face, uvs in zip(geometry.faces, geometry.face_uvs):
        digest.update(",".join(str(index) for index in face).encode("ascii"))
        for uv in uvs:
            digest.update(f"{uv[0]:.6f},{uv[1]:.6f}".encode("ascii"))
    return digest.hexdigest()


def validate_geometry(geometry: DomeGeometry) -> None:
    problems: list[str] = []
    if len(geometry.vertices) != EXPECTED_VERTICES:
        problems.append(
            f"vertex count {len(geometry.vertices)}, expected "
            f"{EXPECTED_VERTICES}"
        )
    triangles = triangle_count(geometry)
    if triangles != EXPECTED_TRIANGLES:
        problems.append(
            f"triangle count {triangles}, expected {EXPECTED_TRIANGLES}"
        )
    if not MIN_TRIANGLES <= triangles <= MAX_TRIANGLES:
        problems.append(
            f"triangle count {triangles} outside "
            f"[{MIN_TRIANGLES}, {MAX_TRIANGLES}]"
        )

    for index, vertex in enumerate(geometry.vertices):
        radius = math.sqrt(sum(component * component for component in vertex))
        if abs(radius - UNIT_RADIUS_METERS) > 0.000002:
            problems.append(f"vertex {index} has radius {radius:.7f}")
            break
        if vertex[2] < -0.000001 or vertex[2] > 1.000001:
            problems.append(f"vertex {index} leaves the upper hemisphere")
            break

    rim = geometry.vertices[-AZIMUTH_SEGMENTS:]
    if any(abs(vertex[2]) > 0.000001 for vertex in rim):
        problems.append("the horizon ring is not on source z=0")

    for face_index, face in enumerate(geometry.faces):
        first = Vector(geometry.vertices[face[0]])
        second = Vector(geometry.vertices[face[1]])
        third = Vector(geometry.vertices[face[2]])
        normal = (second - first).cross(third - first)
        centre = sum(
            (Vector(geometry.vertices[index]) for index in face),
            Vector(),
        ) / len(face)
        if normal.dot(centre) <= 0.0:
            problems.append(
                f"face {face_index} is not outward-wound"
            )
            break

    if problems:
        raise RuntimeError(
            "Exterior cloud dome contract violated:\n  - " +
            "\n  - ".join(problems)
        )


def hash01(seed: int, x: int, y: int) -> float:
    value = (
        (seed & 0xFFFFFFFF) ^
        ((x * 0x9E3779B1) & 0xFFFFFFFF) ^
        ((y * 0x85EBCA77) & 0xFFFFFFFF)
    ) & 0xFFFFFFFF
    value ^= value >> 16
    value = (value * 0x7FEB352D) & 0xFFFFFFFF
    value ^= value >> 15
    value = (value * 0x846CA68B) & 0xFFFFFFFF
    value ^= value >> 16
    return (value & 0x00FFFFFF) / 16777215.0


def smooth(value: float) -> float:
    return value * value * (3.0 - 2.0 * value)


def periodic_value_noise(
    u: float,
    v: float,
    cells: int,
    seed: int,
) -> float:
    scaled_x = u * cells
    scaled_y = v * cells
    x0 = math.floor(scaled_x)
    y0 = math.floor(scaled_y)
    tx = smooth(scaled_x - x0)
    ty = smooth(scaled_y - y0)
    x1 = (x0 + 1) % cells
    y1 = (y0 + 1) % cells
    x0 %= cells
    y0 %= cells
    a = hash01(seed, x0, y0)
    b = hash01(seed, x1, y0)
    c = hash01(seed, x0, y1)
    d = hash01(seed, x1, y1)
    lower = a + (b - a) * tx
    upper = c + (d - c) * tx
    return lower + (upper - lower) * ty


def fractal_noise(
    u: float,
    v: float,
    seed: int,
    octaves: Sequence[tuple[int, float]],
) -> float:
    total = 0.0
    weight = 0.0
    for octave, (cells, amplitude) in enumerate(octaves):
        total += periodic_value_noise(
            u,
            v,
            cells,
            seed + octave * 0x1F123BB5,
        ) * amplitude
        weight += amplitude
    return total / weight


def clamp_byte(value: float) -> int:
    return max(0, min(255, int(round(value * 255.0))))


def paint_density_texture() -> atlas_kit.PixelCanvas:
    canvas = atlas_kit.PixelCanvas(TEXTURE_SIZE, TEXTURE_SIZE)
    for y in range(TEXTURE_SIZE):
        v = (y + 0.5) / TEXTURE_SIZE
        for x in range(TEXTURE_SIZE):
            u = (x + 0.5) / TEXTURE_SIZE
            broad = fractal_noise(
                u, v, SEED + 11,
                ((3, 1.0), (6, 0.52), (12, 0.24), (24, 0.10)),
            )
            detail = fractal_noise(
                u, v, SEED + 37,
                ((5, 1.0), (10, 0.56), (20, 0.30), (40, 0.13)),
            )
            micro = fractal_noise(
                u, v, SEED + 73,
                ((8, 1.0), (16, 0.62), (32, 0.34), (64, 0.16)),
            )

            # Broaden the middle without clipping either end: the runtime's
            # coverage threshold then has useful room from broken to closed.
            broad = smooth(max(0.0, min(1.0, broad)))
            detail = smooth(max(0.0, min(1.0, detail)))
            erosion = max(
                0.0,
                min(
                    1.0,
                    0.58 * abs(detail - broad) * 1.8 +
                    0.42 * micro,
                ),
            )
            canvas.put(
                x,
                y,
                (
                    clamp_byte(broad),
                    clamp_byte(detail),
                    clamp_byte(erosion),
                    255,
                ),
            )
    return canvas


def channel_statistics(
    pixels: bytes,
    channel: int,
) -> tuple[int, int, float]:
    values = pixels[channel::4]
    return min(values), max(values), sum(values) / len(values)


def seam_ratio(pixels: bytes, channel: int) -> float:
    def sample(x: int, y: int) -> int:
        return pixels[(y * TEXTURE_SIZE + x) * 4 + channel]

    seam = 0.0
    interior = 0.0
    count = 0
    for offset in range(TEXTURE_SIZE):
        seam += abs(sample(0, offset) - sample(TEXTURE_SIZE - 1, offset))
        seam += abs(sample(offset, 0) - sample(offset, TEXTURE_SIZE - 1))
        interior += abs(sample(63, offset) - sample(64, offset))
        interior += abs(sample(offset, 127) - sample(offset, 128))
        count += 2
    return (seam / count) / max(0.25, interior / count)


def texture_report(canvas: atlas_kit.PixelCanvas) -> TextureReport:
    payload = canvas.png_bytes()
    width, height, pixels = atlas_kit.decode_generated_png(
        payload,
        TEXTURE_NAME,
    )
    if width != TEXTURE_SIZE or height != TEXTURE_SIZE:
        raise RuntimeError(
            f"Density sheet is {width}x{height}, expected "
            f"{TEXTURE_SIZE}x{TEXTURE_SIZE}."
        )
    if any(pixels[index] != 255 for index in range(3, len(pixels), 4)):
        raise RuntimeError("Density sheet alpha must remain opaque.")

    stats = tuple(channel_statistics(pixels, index) for index in range(3))
    ratios = tuple(seam_ratio(pixels, index) for index in range(3))
    problems: list[str] = []
    for name, (minimum, maximum, mean), ratio in zip(
        CHANNEL_NAMES,
        stats,
        ratios,
    ):
        if maximum - minimum < 105:
            problems.append(
                f"{name} spans only {maximum - minimum} levels"
            )
        if not 70.0 <= mean <= 190.0:
            problems.append(f"{name} mean {mean:.2f} is unusable")
        if ratio > 2.5:
            problems.append(
                f"{name} wrap seam is {ratio:.2f}x an interior edge"
            )
    if problems:
        raise RuntimeError(
            "Exterior cloud texture contract violated:\n  - " +
            "\n  - ".join(problems)
        )

    return TextureReport(
        payload=payload,
        sha256=hashlib.sha256(payload).hexdigest(),
        minimum=tuple(item[0] for item in stats),
        maximum=tuple(item[1] for item in stats),
        mean=tuple(stable(item[2]) for item in stats),
        seam_ratio=tuple(stable(value) for value in ratios),
    )


def write_texture(
    canvas: atlas_kit.PixelCanvas,
    path: Path,
    report: TextureReport,
) -> None:
    canvas.write_png(path)
    measured = hashlib.sha256(path.read_bytes()).hexdigest()
    if measured != report.sha256:
        raise RuntimeError(
            f"Density texture hashes {measured} on disk but "
            f"{report.sha256} in memory."
        )


def reset_scene() -> tuple[bpy.types.Collection, bpy.types.Collection]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.render.engine = "BLENDER_EEVEE"
    source = bpy.data.collections.new(SOURCE_COLLECTION)
    presentation = bpy.data.collections.new(PRESENTATION_COLLECTION)
    scene.collection.children.link(source)
    scene.collection.children.link(presentation)
    return source, presentation


def create_dome_object(
    geometry: DomeGeometry,
    source: bpy.types.Collection,
) -> bpy.types.Object:
    mesh = bpy.data.meshes.new(MESH_NAME)
    mesh.from_pydata(geometry.vertices, [], geometry.faces)
    mesh.validate(verbose=False)
    mesh.update(calc_edges=True)
    uv_layer = mesh.uv_layers.new(name="UVMap")
    for polygon, face_uvs in zip(mesh.polygons, geometry.face_uvs):
        for loop_index, uv in zip(polygon.loop_indices, face_uvs):
            uv_layer.data[loop_index].uv = uv
        polygon.use_smooth = False
    uv_layer.active_render = True
    obj = bpy.data.objects.new(MESH_NAME, mesh)
    source.objects.link(obj)
    return obj


def export_fbx(path: Path, dome: bpy.types.Object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    dome.select_set(True)
    bpy.context.view_layer.objects.active = dome
    bpy.ops.export_scene.fbx(
        filepath=str(path),
        use_selection=True,
        object_types={"MESH"},
        axis_forward="-Z",
        axis_up="Y",
        apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=True,
        add_leaf_bones=False,
        bake_anim=False,
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
    )


def combined_signature(
    geometry: DomeGeometry,
    texture: TextureReport,
) -> str:
    digest = hashlib.sha256()
    digest.update(geometry_signature(geometry).encode("ascii"))
    digest.update(texture.sha256.encode("ascii"))
    digest.update(GENERATOR_VERSION.encode("ascii"))
    return digest.hexdigest()


def bounds(
    vertices: Sequence[Sequence[float]],
) -> tuple[list[float], list[float]]:
    minimum = [min(vertex[axis] for vertex in vertices) for axis in range(3)]
    maximum = [max(vertex[axis] for vertex in vertices) for axis in range(3)]
    return (
        [stable(value) for value in minimum],
        [stable(value) for value in maximum],
    )


def write_manifest(
    path: Path,
    geometry: DomeGeometry,
    texture: TextureReport,
) -> None:
    source_min, source_max = bounds(geometry.vertices)
    # Project export convention: Unity (x, y, z) = Blender (x, z, y).
    unity_vertices = tuple(
        (vertex[0], vertex[2], vertex[1]) for vertex in geometry.vertices
    )
    unity_min, unity_max = bounds(unity_vertices)
    payload = {
        "generator": "tools/build-exterior-cloud-3d-model.py",
        "generator_version": GENERATOR_VERSION,
        "blender_version": bpy.app.version_string,
        "design_id": DESIGN_ID,
        "display_name": DISPLAY_NAME,
        "seed": SEED,
        "mesh_name": MESH_NAME,
        "mesh_count": 1,
        "vertex_count": len(geometry.vertices),
        "triangle_count": triangle_count(geometry),
        "triangle_budget": [MIN_TRIANGLES, MAX_TRIANGLES],
        "unit_radius_m": UNIT_RADIUS_METERS,
        "azimuth_segments": AZIMUTH_SEGMENTS,
        "ring_count": RING_COUNT,
        "bounds_source_min": source_min,
        "bounds_source_max": source_max,
        "bounds_unity_min": unity_min,
        "bounds_unity_max": unity_max,
        "uv_contract": "planar_disk_source_xy",
        "source_axes": {
            "right": "+X",
            "forward": "+Y",
            "up": "+Z",
        },
        "unity_axes": {
            "right": "+X",
            "forward": "+Z",
            "up": "+Y",
            "fbx_axis_forward": "-Z",
            "fbx_axis_up": "Y",
            "bake_space_transform": True,
        },
        "texture_file": TEXTURE_NAME,
        "texture_size": TEXTURE_SIZE,
        "texture_sha256": texture.sha256,
        "texture_linear_data": True,
        "texture_channels": [
            {
                "name": name,
                "minimum": texture.minimum[index],
                "maximum": texture.maximum[index],
                "mean": texture.mean[index],
                "seam_ratio": texture.seam_ratio[index],
            }
            for index, name in enumerate(CHANNEL_NAMES)
        ],
        "colliders": False,
        "rigidbodies": False,
        "lights": False,
        "cameras": False,
        "animation_count": 0,
        "imported_materials": False,
        "build_signature": combined_signature(geometry, texture),
    }
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    os.replace(temporary, path)


def create_preview_material(texture_path: Path) -> bpy.types.Material:
    material = bpy.data.materials.new("MAT_ExteriorCloudPreview")
    material.use_nodes = True
    nodes = material.node_tree.nodes
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs["Strength"].default_value = 0.72
    image_node = nodes.new("ShaderNodeTexImage")
    image_node.image = bpy.data.images.load(str(texture_path))
    image_node.interpolation = "Linear"
    image_node.extension = "REPEAT"
    grayscale = nodes.new("ShaderNodeRGBToBW")
    material.node_tree.links.new(
        image_node.outputs["Color"],
        grayscale.inputs["Color"],
    )
    material.node_tree.links.new(
        grayscale.outputs["Val"],
        emission.inputs["Color"],
    )
    material.node_tree.links.new(
        emission.outputs["Emission"],
        output.inputs["Surface"],
    )
    return material


def render_preview(
    path: Path,
    dome: bpy.types.Object,
    presentation: bpy.types.Collection,
    texture_path: Path,
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    dome.data.materials.clear()
    dome.data.materials.append(create_preview_material(texture_path))

    scene = bpy.context.scene
    camera_data = bpy.data.cameras.new("CAM_ExteriorCloudPreview")
    camera = bpy.data.objects.new("CAM_ExteriorCloudPreview", camera_data)
    presentation.objects.link(camera)
    camera.location = (0.0, 0.0, 0.03)
    target = Vector((0.18, 0.60, 0.56))
    camera.rotation_euler = (
        target - camera.location
    ).to_track_quat("-Z", "Y").to_euler()
    camera_data.lens = 24
    camera_data.clip_start = 0.01
    camera_data.clip_end = 2.0
    scene.camera = camera

    if scene.world is None:
        scene.world = bpy.data.worlds.new("ExteriorCloudPreviewWorld")
    scene.world.color = (0.19, 0.23, 0.22)
    scene.render.resolution_x = 960
    scene.render.resolution_y = 540
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(path)
    scene.render.film_transparent = False
    scene.view_settings.look = "AgX - Medium High Contrast"
    bpy.ops.render.render(write_still=True)


def save_blend(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(path), check_existing=False)


def main() -> None:
    config = parse_args()

    geometry = build_geometry()
    rerun_geometry = build_geometry()
    if geometry_signature(rerun_geometry) != geometry_signature(geometry):
        raise RuntimeError("Non-deterministic exterior cloud geometry.")

    canvas = paint_density_texture()
    report = texture_report(canvas)
    rerun_report = texture_report(paint_density_texture())
    if rerun_report.sha256 != report.sha256:
        raise RuntimeError("Non-deterministic exterior cloud texture.")
    write_texture(canvas, config.texture, report)

    source, presentation = reset_scene()
    dome = create_dome_object(geometry, source)
    export_fbx(config.fbx, dome)
    write_manifest(config.manifest, geometry, report)
    if not config.no_preview:
        render_preview(config.preview, dome, presentation, config.texture)
    save_blend(config.output)

    print("EXTERIOR CLOUD ASSET BUILD OK")
    print(f"  Blender: {bpy.app.version_string}")
    print(f"  Design: {DESIGN_ID}")
    print(f"  Meshes: 1")
    print(f"  Vertices: {len(geometry.vertices)}")
    print(f"  Triangles: {triangle_count(geometry)}")
    print(f"  Texture: {TEXTURE_SIZE} px ({report.sha256[:12]}...)")
    print(f"  Signature: {combined_signature(geometry, report)}")
    print(f"  Blend: {config.output}")
    print(f"  FBX: {config.fbx}")
    print(f"  Manifest: {config.manifest}")
    print(f"  Density: {config.texture}")
    if not config.no_preview:
        print(f"  Preview: {config.preview}")
    print("  Determinism: repeated geometry and texture signatures match")


if __name__ == "__main__":
    main()
