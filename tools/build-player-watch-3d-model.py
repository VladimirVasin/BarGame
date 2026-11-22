"""Build and verify the passive Hero V2 wristwatch in Unity-local metres."""

import argparse
import importlib.util
import json
import math
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets/Resources/Player/Watch"
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("watch_model_kit", ROOT / "tools/build-city-fair-3d-model.py")
kit = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = kit
spec.loader.exec_module(kit)

CASE_WIDTH = .043
LCD_WIDTH, LCD_HEIGHT = .034, .021
kit.SURFACES = {
    "Resin": (.13, .14, .13), "Steel": (.37, .37, .33),
    "Rubber": (.105, .107, .099), "LCD": (.51, .55, .45),
}


def strap():
    # X is the forearm axis. The closed band is elliptical in YZ.
    verts = []
    for x, inset in ((-.010, .0015), (-.0085, 0), (.0085, 0), (.010, .0015),
                     (.010, .0034), (-.010, .0034)):
        for i in range(16):
            angle = math.tau * i / 16
            verts.append((x, (.037 - inset) * math.cos(angle), (.034 - inset) * math.sin(angle)))
    faces = []
    for row in range(6):
        for i in range(16):
            a, b = row * 16, (row + 1) % 6 * 16
            j = (i + 1) % 16
            faces.append((a + i, a + j, b + j, b + i))
    return verts, faces


def watch():
    item = kit.Item("PlayerWatch")
    item.add("Rubber", strap())
    item.box("Resin", (0, 0, .034), (CASE_WIDTH, .045, .012), chamfer=.003)
    item.box("Steel", (0, 0, .040), (.040, .035, .003), chamfer=.001)
    item.box("Resin", (0, 0, .041), (.036, .025, .002), chamfer=.0006)
    item.box("LCD", (0, 0, .0424), (LCD_WIDTH, LCD_HEIGHT, .001), chamfer=.0002)
    item.box("Steel", (.022, .009, .034), (.004, .006, .004), chamfer=.0006)
    item.box("Steel", (-.022, -.009, .034), (.004, .006, .004), chamfer=.0006)
    item.box("Steel", (0, 0, -.034), (.025, .016, .004), chamfer=.0008)
    item.anchor("Dial", (0, 0, .043))
    return item


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    item = watch()
    signature = kit.signature([item])
    if signature != kit.signature([watch()]):
        raise ValueError("Wristwatch geometry is not deterministic")
    data = kit.manifest([item], signature)
    data.update(generator="tools/build-player-watch-3d-model.py", generator_version="1.0.0",
                lcd_uv="readable from +Z", lcd_size_m=[LCD_WIDTH, LCD_HEIGHT])
    manifest = OUT / "PlayerWatch.json"
    if not args.validate_only:
        OUT.mkdir(parents=True, exist_ok=True)
        root = kit.build_objects([item])[0]
        lcd = next(obj for obj in root.children if obj.name == "Body__LCD")
        for loop in lcd.data.loops:
            pos = lcd.data.vertices[loop.vertex_index].co
            lcd.data.uv_layers.active.data[loop.index].uv = (.5 - pos.x / LCD_WIDTH, .5 + pos.z / LCD_HEIGHT)
        kit.export(root, OUT / "PlayerWatch.fbx")
    kit.verify_fbx(OUT, data)
    if args.validate_only:
        if json.loads(manifest.read_text()) != json.loads(json.dumps(data)):
            raise ValueError("Published wristwatch differs from deterministic geometry")
    else:
        manifest.write_text(json.dumps(data, indent=2) + "\n")
    print("Player watch solid geometry, dimensions and FBX round trip OK", flush=True)


if __name__ == "__main__":
    main()
