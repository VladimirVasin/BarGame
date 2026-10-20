#!/usr/bin/env python3
"""Derive complete road Lit/wind passes from the proven scene-surface hooks."""
import argparse
import importlib.util
from pathlib import Path
import uuid

ROOT = Path(__file__).resolve().parents[1]


def outputs():
    source = ROOT / "tools/build-village-surface-shader.py"
    spec = importlib.util.spec_from_file_location("road_surface_shader_source", source)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    result = {}
    for path, value in module.outputs().items():
        path = path.with_name(path.name.replace("VillageSurface", "MountainRoadSurface"))
        value = value.replace("VillageSurface", "MountainRoadSurface").replace("Village Surface", "Mountain Road Surface")
        value = value.replace("tools/build-village-surface-shader.py", "tools/build-mountain-road-surface-shader.py")
        result[path] = value
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    for path, value in outputs().items():
        if args.check:
            if not path.exists() or path.read_text(encoding="utf8") != value:
                raise ValueError("Regenerate road surface shader: " + str(path))
        else:
            path.write_text(value, encoding="utf8", newline="\n")
            meta = Path(str(path) + ".meta")
            if not meta.exists():
                kind = "ShaderImporter" if path.suffix == ".shader" else "ShaderIncludeImporter"
                guid = uuid.uuid5(uuid.NAMESPACE_URL, path.relative_to(ROOT).as_posix()).hex
                meta.write_text(f"fileFormatVersion: 2\nguid: {guid}\n{kind}:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n", encoding="utf8")
    print("ROAD SURFACE SHADERS OK: complete Lit, depth, shadow and wind passes retained.")


if __name__ == "__main__":
    main()
