#!/usr/bin/env python3
"""Pack and paint the shared village close-view atlas without changing geometry."""
import argparse
import hashlib
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from village_unique_surfaces import MANIFEST, ROOT, prepare, module, describe, digest, rounded, packed_uv


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--layout-only", action="store_true")
    parser.add_argument("--workroom-only", action="store_true", help="Repaint house 08 in the shipped atlas")
    parser.add_argument("--unmapped-only", action="store_true", help="Register expansion meshes with metre UVs without repainting the atlas")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    if args.unmapped_only:
        if args.workroom_only or args.layout_only:
            parser.error("--unmapped-only uses the complete source layout")
        previous = json.loads(MANIFEST.read_text())
        generator = module("expansion")
        authored = generator.create_parts()
        objects, entries = generator.build(authored)
        rows = {p["mesh"]:p for p in previous["parts"] if p["owner"] != "Expansion"}
        registered = {p["mesh"]:p for p in previous["parts"] if p["owner"] == "Expansion" and p["unique_surface"]}
        charts = {tuple(c["key"]):c for c in previous["projection"]["charts"]}
        for obj, entry in zip(objects,entries,strict=True):
            part = describe(obj,entry,"Expansion")
            row = {k:v for k,v in part.items() if k not in ("metric_uv","projection_uv","faces")}
            if part["unique_surface"]:
                row["uv0_signature"] = digest(rounded(packed_uv(part,charts)))
                if row != registered.pop(part["mesh"],None):
                    raise ValueError("Packed expansion mesh changed: " + part["mesh"])
            rows[part["mesh"]] = row
        if registered: raise ValueError("Packed expansion meshes removed: " + ", ".join(registered))
        result = dict(previous)
        result["parts"] = sorted(rows.values(),key=lambda p:p["mesh"])
        result["uv_signature"] = digest(result["parts"])
        result["geometry_signatures"] = dict(previous["geometry_signatures"],Expansion=digest(authored))
        result["source_geometry_signature"] = digest(result["geometry_signatures"])
        for name, field in (("village_unique_surfaces.py","recipe_sha256"),
                            ("village_surface_authoring.py","physical_recipe_sha256")):
            if hashlib.sha256((ROOT/"tools"/name).read_bytes()).hexdigest() != previous[field]:
                raise ValueError("The atlas paint recipe has changed: " + name)
        if args.validate_only:
            if result != previous: raise ValueError("Stale unpacked village geometry")
        else:
            MANIFEST.write_text(json.dumps(result,indent=2)+"\n")
    else:
        result = prepare(args.validate_only, args.layout_only, args.workroom_only)
    print("VILLAGE UNIQUE SURFACES OK: " + result["uv_signature"], flush=True)


if __name__ == "__main__":
    main()
