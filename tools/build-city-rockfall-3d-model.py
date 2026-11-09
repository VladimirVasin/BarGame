#!/usr/bin/env python3
"""Build the three passive, clustered rockfall meshes at City's physical ridge."""
import argparse
import importlib.util
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("ridge_edges", ROOT / "tools/build-city-ridge-edges-3d-model.py")
ridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ridge)
port = ridge.port
OUTPUT = ROOT / "Assets/Resources/City/Rockfall"
MANIFEST = "CityRockfall3D.json"

# X, Z, footprint radii, highest exposed bed and yaw in radians.
# Bedrock leans into +Z. Small separated flakes finish the taper at -Z.
CLUSTERS = (
    (
        (-1.0, .70, 1.40, .87, 1.68, -.36),
        (.84, .83, 1.11, .75, 1.10, -.25),
        (-.39, -.10, 1.06, .67, .72, -.18),
        (1.34, -.10, .71, .48, .52, -.52),
        (-1.57, -.52, .65, .45, .38, .60),
        (.40, -.89, .65, .46, .33, -.31),
        (-1.28, -1.33, .43, .30, .22, -.95),
        (.99, -1.40, .28, .25, .17, .45),
    ),
    (
        (-1.23, .88, 1.17, .77, 1.07, .37),
        (.64, .66, 1.43, .86, 1.73, .21),
        (-.53, .08, 1.15, .54, .99, .44),
        (1.42, -.09, .83, .46, .68, .12),
        (.31, -.65, .81, .57, .47, .39),
        (-1.76, -.36, .42, .35, .30, -.46),
        (-.68, -1.34, .42, .27, .24, -.11),
        (.78, -1.61, .31, .21, .15, .65),
    ),
    (
        (-1.42, .68, 1.12, .71, 1.43, -.39),
        (.77, .96, 1.51, .71, 1.24, -.44),
        (-.31, .23, 1.01, .68, 1.04, .12),
        (-1.69, -.19, .86, .51, .55, -.44),
        (.92, -.12, .82, .42, .49, -.27),
        (-.60, -.68, .63, .41, .43, -.59),
        (1.71, -.64, .34, .28, .22, .41),
        (-1.24, -1.27, .45, .27, .22, -.67),
        (.21, -1.37, .30, .23, .17, .08),
    ),
)


def cluster(index, blocks, material):
    root = port.empty(f"CityRockfall{index}")
    stone = ridge.kit.geometry("Rock")
    for piece, (x, z, rx, rz, crown, turn) in enumerate(blocks):
        # slab() checks each solid's winding before it joins the single role.
        ridge.slab(stone, x, z, rx, rz, -.24, crown, turn, 2310 + index * 30 + piece)
    ridge.kit.emit(stone, root.name + "Stone", root, material)
    mesh, = [o.data for o in root.children_recursive if o.type == "MESH"]
    # Preserve the authored metre-projected UVs as the position envelope is
    # normalized. Placement assigns the final dimensions from the terrain plan.
    minimum = [min(v.co[i] for v in mesh.vertices) for i in range(3)]
    maximum = [max(v.co[i] for v in mesh.vertices) for i in range(3)]
    for vertex in mesh.vertices:
        for i in range(3):
            vertex.co[i] = (vertex.co[i] - minimum[i]) / (maximum[i] - minimum[i]) - .5
    mesh.update()
    return root


def build():
    material = port.base.material("RockfallSource")
    roots = [cluster(i, blocks, material) for i, blocks in enumerate(CLUSTERS)]
    port.validate_surfaces(roots)
    models = []
    for i, root in enumerate(roots):
        model = port.describe(root)
        if model["bounds_min"] != [-.5] * 3 or model["bounds_max"] != [.5] * 3:
            raise ValueError(f"Unit placement bounds lost: {model}")
        mesh, = [o.data for o in root.children_recursive if o.type == "MESH"]
        vertices = [port.source(vertex.co) for vertex in mesh.vertices]
        front = max(y for x, y, z in vertices if z <= -.25)
        rear = max(y for x, y, z in vertices if z >= .10)
        if front > .02 or rear < .45 or model["triangles"] > 520:
            raise ValueError(f"Rockfall must rise from the foreground toward the cliff: {model}")
        model.pop("anchors")
        model.update(boulders=len(CLUSTERS[i]), front_max_y=round(front, 5), rear_max_y=round(rear, 5))
        models.append(model)
    return roots, {"design_id": "city_boundary_old_rockfall_v1", "generator": Path(__file__).name,
                   "axes": "Unity X along ridge, +Y up, +Z toward cliff; centred unit bounds",
                   "source_rock_uv_metres_per_tile": port.SURFACE_TILES["Rock"], "models": models}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path, default=OUTPUT)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    port.base.reset()
    roots, data = build()
    if args.validate_only:
        if json.loads((args.model_dir / MANIFEST).read_text()) != data:
            raise ValueError("Rockfall manifest differs from measured geometry")
    else:
        args.model_dir.mkdir(parents=True, exist_ok=True)
        for root in roots:
            port.base.export(root, args.model_dir / (root.name + ".fbx"))
        (args.model_dir / MANIFEST).write_text(json.dumps(data, indent=2) + "\n")
    port.base.reset()
    if build()[1] != data:
        raise ValueError("Deterministic rockfall rebuild differs")
    print("CITY ROCKFALL: verified outward stone solids, world-projected UVs and uphill silhouettes")


if __name__ == "__main__":
    main()
