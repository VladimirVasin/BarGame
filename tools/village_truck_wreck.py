"""A passive village wreck made from the cannery truck's authored solids.

The donor's truck() runs unchanged in an isolated module. Its mesh emission is
captured before semantic batching, so the passenger door can be removed even
though the exported donor batches it with the cab. No donor FBX, material,
default, scene object or geometry signature is changed by this adapter.
"""
from __future__ import annotations

import importlib.util
import math
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.dont_write_bytecode = True
sys.path.insert(0, str(ROOT / "tools"))

import bar_parts as bp
import interior_kit as kit
from mathutils import Vector
from mathutils.bvhtree import BVHTree

MAX_TRIANGLES = 5500
_DONOR = None
_CAPTURE = None


def _donor_parts():
    global _DONOR, _CAPTURE
    if _CAPTURE is not None:
        return _DONOR, _CAPTURE
    spec = importlib.util.spec_from_file_location(
        "village_wreck_cannery_donor", ROOT / "tools/build-city-cannery-3d-model.py")
    donor = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(donor)
    original_geometry = donor.Geometry

    class CapturedGeometry(original_geometry):
        def __init__(self):
            super().__init__()
            self.components = []

        def add(self, vertices, faces, color, uvs=None, solid=True):
            first_vertex, first_face = len(self.vertices), len(self.faces)
            super().add(vertices, faces, color, uvs, solid)
            self.components.append((first_vertex, len(self.vertices), first_face,
                                    len(self.faces), color, self.role, solid))

    captured = {}

    def capture(geometry, name, parent, material):
        captured[name] = geometry

    # These calls only emit the donor hierarchy/animated tail lift. All retained
    # geometry is authored in its root space; no mesh requires a moving pivot.
    donor.Geometry = CapturedGeometry
    donor.empty = lambda name, parent=None, position=(0, 0, 0): name
    donor.anchor = lambda *args: None
    donor.collision_box = lambda *args: None
    donor.truck_tail_lift_mechanism = lambda *args: None
    donor.obj = capture
    donor.truck(None)
    required = {"TruckVisible", "TruckFrontPanel", "DriverDoorVisible", "CabinGlass",
                "RearDoorVisibleLeft", "RearDoorVisibleRight", "CabinDoorGlass"}
    required.update("WheelVisible" + axle + side for axle in ("F", "R") for side in ("L", "R"))
    assert required <= captured.keys(), "Cannery donor hierarchy changed; inspect wreck extraction"
    _DONOR, _CAPTURE = donor, captured
    return donor, captured


def _components(geometry):
    for first, last, face_first, face_last, color, role, solid in geometry.components:
        # The donor stores Blender coordinates, while the expansion pack owns
        # Unity metre-space geometry. The swap reverses the face winding.
        vertices = [(x, z, y) for x, y, z in geometry.vertices[first:last]]
        faces = [tuple(index - first for index in reversed(face))
                 for face in geometry.faces[face_first:face_last]]
        assert solid and bp.signed_volume((vertices, faces)) > 1e-9
        yield (vertices, faces), color, role


def _bounds(geometry):
    low, high = kit.bounds(geometry)
    center = tuple((a + b) * .5 for a, b in zip(low, high))
    size = tuple(b - a for a, b in zip(low, high))
    return low, high, center, size


def _map(geometry, function):
    result = ([function(*point) for point in geometry[0]], geometry[1])
    assert bp.signed_volume(result) > 1e-9, "Wreck deformation inverted an authored solid"
    return result


def _snow(center, width, depth, height):
    # A closed irregular cap, not a white plane hiding the open cab or chassis.
    vertices, faces = bp.to_source(kit.lathe([(.64, 0), (.76, .018), (.56, .065), (.12, .083)], 9))
    return ([(center[0] + x * width * (1 + .07 * math.sin(i * 2.3)),
              center[1] + y * height / .083, center[2] + z * depth)
             for i, (x, y, z) in enumerate(vertices)], faces)


def rusted_truck(add):
    """Append measured passive RustedTruck parts to the expansion pack."""
    donor, emitted = _donor_parts()
    groups = {"Chassis": [], "CabShell": [], "BentNose": [], "BentArches": [],
              "CargoFrame": [], "FloorRemnants": [], "SeatRemnants": [], "Snow": []}
    removed_passenger_door = 0
    archived_arches = 0
    reused = 0
    for geometry, color, role in _components(emitted["TruckVisible"]):
        low, high, center, size = _bounds(geometry)
        x, y, z = center
        if abs(x - 1.06) < .002 and abs(y - 1.48) < .002 and abs(z - 3.20) < .002:
            assert .11 < size[0] < .13 and 1.71 < size[2] < 1.73
            removed_passenger_door += 1
            continue
        # Insulated walls/roof and the refrigeration head are gone. What is
        # left is the original lorry frame, not another closed goods vehicle.
        if role == "Insulation" or (low[1] > 2.55 and high[2] < 2.60 and size[0] > .7):
            continue
        if role == "Deck":
            # Two cropped pieces of the donor deck expose the middle chassis.
            for start, length in ((-1.89, 1.13), (1.43, .62)):
                groups["FloorRemnants"].append(_map(geometry, lambda xx, yy, zz, a=start, n=length:
                    (xx, yy + .028 * math.sin(xx * 3.1 + zz), a + (zz - low[2]) / size[2] * n)))
            reused += 1
            continue
        # The remaining refrigerator solids are above the cargo-front wall;
        # this region deliberately keeps no fan/service-line silhouette.
        if low[1] > 2.60 and high[2] < 2.55:
            continue
        if abs(x) > 1.08 and low[1] > 1.9 and high[1] < 2.43 and z > 3.65:
            continue  # Both mirror stalks/housings are stripped.
        if x > 1.08 and 1.65 < y < 1.8 and 2.65 < z < 2.8:
            continue  # Passenger door handle, not a cab-shell component.
        if color == donor.DARK and low[1] > 1.8 and z > 4.0:
            continue  # Wiper blades.
        if role == "Rubber":
            continue  # Hanging flexible mud flaps, not the metal arches.
        if color == donor.PAINT and abs(x) > .8 and low[1] >= .449 and high[1] < 1.02:
            archived_arches += 1
            if archived_arches % 5 == 0:
                continue
            groups["BentArches"].append(_map(geometry, lambda xx, yy, zz:
                (xx + .035 * math.sin(zz * 7), yy - .025 * math.cos(zz * 5), zz)))
        elif high[1] < .84 or (high[1] < 1.08 and z < 2.20):
            groups["Chassis"].append(geometry)
        elif high[2] < 2.25 and low[1] >= 1.19:
            # Original box rails/posts/ribs: selected strips stay in place,
            # with the top sagged and the right side visibly bent inward.
            groups["CargoFrame"].append(_map(geometry, lambda xx, yy, zz:
                (xx - (.12 if xx > 0 else .035) * max(0, yy - 1.21),
                 1.21 + (yy - 1.21) * .76 - .035 * math.sin(zz * 1.4), zz)))
        elif color == donor.DARK and 2.6 < z < 3.6 and 1.2 < y < 2.2:
            groups["SeatRemnants"].append(_map(geometry, lambda xx, yy, zz:
                (xx, 1.34 + (yy - 1.34) * .62, zz)))
        elif low[2] > 2.20:
            groups["CabShell"].append(_map(geometry, lambda xx, yy, zz:
                (xx + .025 * max(0, yy - 2.3), yy - .035 * max(0, xx) * max(0, yy - 2), zz)))
        else:
            continue
        reused += 1
    assert removed_passenger_door == 1, "Passenger door no longer matches the original donor solid"
    assert archived_arches == 32, "Expected the donor's four eight-segment metal arches"
    for geometry, _, _ in _components(emitted["TruckFrontPanel"]):
        groups["BentNose"].append(_map(geometry, lambda x, y, z:
            (x, y - .07 * max(0, x), z - .10 * max(0, x))))
        reused += 1
    assert reused >= 65, "Wreck lost the donor's recognizable structural geometry"
    # Small isolated corrosion remnants remain on the cargo skeleton: broad
    # enough to show old paint, too incomplete to read as intact box walls.
    for center, size, angle in (((-1.10, 1.43, -.87), (.035, .42, 1.28), (2, 0, -8)),
                                ((1.07, 1.49, 1.44), (.035, .52, .62), (-5, 0, 12)),
                                ((-.56, 2.48, 2.15), (.91, .36, .035), (0, -8, 7))):
        local = bp.u_box((0, 0, 0), size, .009)
        groups["FloorRemnants"].append(kit.translated(bp.u_rotated(local, angle), center))
    for center, width, depth, height in (((-.25, 2.961, 3.19), 1.01, .91, .070),
                                         ((.45, 1.155, 3.70), .66, .35, .085),
                                         ((-.20, 1.242, -1.23), .95, .51, .060),
                                         ((-.73, .813, .72), .14, .75, .045),
                                         ((-.40, 1.245, 1.78), .64, .22, .050)):
        groups["Snow"].append(_snow(center, width, depth, height))

    structural = [g for name, chunks in groups.items() if name != "Snow" for g in chunks]
    trees = [BVHTree.FromPolygons(*g, all_triangles=False) for g in structural]

    def aperture(start, end, label):
        direction = Vector(end) - Vector(start)
        assert not any(tree.ray_cast(Vector(start), direction.normalized(), direction.length)[0]
                       is not None for tree in trees), "Wreck blocked " + label

    for sign in (-1, 1):
        aperture((sign * 1.6, 1.67, 3.68), (sign * .80, 1.67, 3.68), "cabin doorway")
        aperture((sign * 1.6, 2.33, 3.20), (sign * .80, 2.33, 3.20), "side window")
    aperture((.48, 2.34, 4.8), (.48, 2.34, 3.95), "windscreen")
    aperture((.48, 1.80, -2.65), (.48, 1.80, -1.45), "rear cargo doorway")
    # Only these original root-space bodies are ever consumed: four wheel
    # groups, both cargo doors, driver door and every glass group are excluded.
    assert set(groups) == {"Chassis", "CabShell", "BentNose", "BentArches", "CargoFrame",
                           "FloorRemnants", "SeatRemnants", "Snow"}
    assert all(groups.values()), "An expected readable wreck part is absent"
    rotated = {name: [bp.u_rotated(_map(g, lambda x, y, z: (x, y - .65, z - 1.24)),
                                   (1.25, 0, -2.35)) for g in chunks]
               for name, chunks in groups.items()}
    ground = min(v[1] for chunks in rotated.values() for g in chunks for v in g[0])
    final = {name: kit.merge_all(kit.translated(g, (0, -ground, 0)) for g in chunks)
             for name, chunks in rotated.items()}
    all_geometry = kit.merge_all(final.values())
    low, high, _, size = _bounds(all_geometry)
    assert abs(low[1]) < 1e-8 and 6.0 < size[2] < 6.8 and 2.25 < size[0] < 2.70
    assert 2.2 < size[1] < 2.95, "Wheel-less truck must sit low on its original chassis"
    assert kit.triangle_count(all_geometry) <= MAX_TRIANGLES, "Wreck exceeded its bounded mesh budget"
    surfaces = {"Chassis": ("RustedIron", (.22, .195, .17, 1)),
                "CabShell": ("WreckPaint", (1, 1, 1, 1)),
                "BentNose": ("WreckPaint", (.96, .96, .96, 1)),
                "BentArches": ("WreckRust", (.98, .98, .98, 1)),
                "CargoFrame": ("WreckRust", (.90, .90, .90, 1)),
                "FloorRemnants": ("WreckRust", (.95, .95, .95, 1)),
                "SeatRemnants": ("Canvas", (.24, .24, .205, 1)),
                "Snow": ("WindSnow", (.83, .85, .84, 1))}
    for name, geometry in final.items():
        surface, tint = surfaces[name]
        add("RustedTruck", name, geometry, surface, name != "Snow", tint)


if __name__ == "__main__":
    import json
    rows = []
    rusted_truck(lambda *row: rows.append(row))
    repeated = []
    rusted_truck(lambda *row: repeated.append(row))
    assert rows == repeated, "Non-deterministic wreck geometry"
    print(json.dumps({"parts": [{"name": row[1], "triangles": kit.triangle_count(row[2]),
                                 "bounds": kit.bounds(row[2])} for row in rows],
                      "triangles": sum(kit.triangle_count(row[2]) for row in rows)}))
    print("VILLAGE TRUCK WRECK OK: donor solids, removed doors/wheels/glass, open apertures, dimensions, determinism")
