#!/usr/bin/env python3
"""Deterministic free-cloth fields for the existing Hero V2 jacket.

The renderer keeps its original topology, skin weights and animation bank.
This metadata describes a shared hem field and two cuff fields; runtime derives
per-vertex masks from bind-space positions, including vertices split by FBX UVs.

After a validated hero export, --write adds only the cloth block and its helper
hash to that JSON. --check is read-only and verifies those exact derived fields.
The regular Blender generator calls attach_metadata() on the same payload.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path


DEFAULT_MANIFEST = Path(__file__).resolve().parents[1] / "Assets/Player3D/V2/Models/PlayerCharacter3DV2.json"
HEM_RENDERERS = (
    "CLO_JacketBody", "CLO_JacketPlacket.L", "CLO_JacketPlacket.R",
    "CLO_JacketPocketHip.L", "CLO_JacketPocketHip.R",
    "CLO_JacketFlapHip.L", "CLO_JacketFlapHip.R",
)
CUFF_RENDERERS = {
    side: (f"CLO_JacketForearm.{side}", f"CLO_JacketCuff.{side}")
    for side in ("L", "R")
}
METADATA_FIELDS = ("jacket_cloth", "jacket_cloth_authoring_sha256")


def _number(value: float) -> float:
    rounded = round(value, 6)
    return 0.0 if rounded == 0.0 else rounded


def build_contract(payload: dict) -> dict:
    """Derive metre-space attachment fields from a validated modular hero."""
    if payload.get("design_version") != "HeroV2":
        raise ValueError("Jacket cloth requires the production HeroV2 manifest")
    height = float(payload.get("height_m", 0.0))
    if not math.isfinite(height) or height <= 0.0:
        raise ValueError("Jacket cloth requires a finite positive model height")
    scale = height / 1.75
    parts = {part["name"]: part for part in payload.get("parts", ())}
    moving = (*HEM_RENDERERS, *(name for names in CUFF_RENDERERS.values() for name in names))
    wardrobe = payload.get("wardrobe", {}).get("items", ())
    jackets = [item for item in wardrobe if item.get("slot") == "jacket"]
    if len(jackets) != 1:
        raise ValueError("Jacket cloth requires one independent default jacket item")
    jacket_names = set(jackets[0].get("renderers", ()))
    for name in moving:
        if name not in parts or parts[name].get("role") != "clothing" or name not in jacket_names:
            raise ValueError("Jacket cloth is missing its independently wearable mesh " + name)
    for side in CUFF_RENDERERS:
        bone = f"forearm.{side}"
        if bone not in payload.get("bones", ()):
            raise ValueError("Jacket cuff requires the original bone " + bone)
        for name in CUFF_RENDERERS[side]:
            if parts[name].get("bone") != bone:
                raise ValueError(name + " must retain its original rigid forearm binding")

    nodes = []
    for index in range(8):
        angle = -math.pi / 2 + .24 + (math.tau - .48) * index / 7
        nodes.append(dict(zip(("x", "y", "z"), (_number(value * scale) for value in
                      (.168 * math.cos(angle), .017 + .104 * math.sin(angle), .805)))))
    # Distinct front edges form an open chain. Closing 7->0 would simulate a
    # stitched jacket front, contradicting the authored open shell and placket.
    return {
        "contract": "hero_jacket_cloth_v1",
        "source_space": "blender_z_up_minus_y_forward",
        "deformation": "skinned_mesh_bind_delta",
        "mask_curve": "smoothstep",
        "wardrobe_item_id": jackets[0]["id"],
        "hem": {
            "renderers": list(HEM_RENDERERS),
            "nodes_blender": nodes,
            "node_edges": [node for index in range(7) for node in (index, index + 1)],
            "closed": False,
            "pin_z_m": _number(1.115 * scale),
            "free_z_m": _number(.805 * scale),
        },
        "cuffs": [{
            "bone": f"forearm.{side}",
            "renderers": list(names),
            "node_count": 4,
            "pin_axis_fraction": .52,
            "free_tip_offset_m": _number(.004 * scale),
        } for side, names in CUFF_RENDERERS.items()],
        "pinned_renderers": sorted(jacket_names - set(moving)),
    }


def attach_metadata(payload: dict) -> None:
    """Add derived metadata without changing the geometry/action signature."""
    contract = build_contract(payload)
    payload[METADATA_FIELDS[0]] = contract
    payload[METADATA_FIELDS[1]] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()


def validate_metadata(payload: dict) -> None:
    expected = dict(payload)
    attach_metadata(expected)
    for name in METADATA_FIELDS:
        if payload.get(name) != expected[name]:
            raise ValueError(f"Stale or missing {name}; run python tools/player_jacket_cloth.py --write")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", nargs="?", type=Path, default=DEFAULT_MANIFEST)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--write", action="store_true", help="Update only the derived cloth metadata")
    mode.add_argument("--check", action="store_true", help="Check existing metadata without writing (default)")
    args = parser.parse_args()
    payload = json.loads(args.manifest.read_text(encoding="utf-8"))
    if args.write:
        before = {key: value for key, value in payload.items() if key not in METADATA_FIELDS}
        attach_metadata(payload)
        validate_metadata(payload)
        if before != {key: value for key, value in payload.items() if key not in METADATA_FIELDS}:
            raise RuntimeError("Cloth metadata must not alter the validated hero payload")
        temporary = args.manifest.with_suffix(args.manifest.suffix + ".tmp")
        temporary.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        os.replace(temporary, args.manifest)
    else:
        validate_metadata(payload)
    print("Hero jacket cloth metadata validated: open 8-node hem, two 4-node cuffs; model/actions preserved")


if __name__ == "__main__":
    main()
