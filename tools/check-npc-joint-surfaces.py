#!/usr/bin/env python3
"""Read-only Blender check of the legacy NPC joint migration and art budgets.

Run with tools/run-blender.py --validate-only. No model or animation is exported.
"""
from __future__ import annotations

import importlib.util
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.dont_write_bytecode = True

import npc_detail_atlas
import character_joint_surfaces as joints


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / "tools" / filename)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def check_npcs():
    # Source builders attach this existing image only for preview. A check
    # must not republish it as a side effect of reconstructing source geometry.
    npc_detail_atlas.publish = lambda path: Path(path)
    base = load("npc_joint_check_base", "build-city-pedestrian-3d-model.py")
    reports = []
    failures = []
    for spec in base.ARCHETYPES.values():
        try:
            atlas = base.paint_detail_atlas(spec, ROOT / "Assets/Pedestrians/Textures" / spec.texture_atlas) if spec.texture_atlas else None
            result = base.PedestrianBuilder(spec).build()
            report = base.validate_result(result, spec, atlas)
            reports.append((spec.key, report.triangle_count, len(joints.manifest(result)["seams"])))
            print("NPC joints:", reports[-1], flush=True)
        except (RuntimeError, ValueError) as error:
            failures.append((spec.key, str(error)))
            print("NPC joint failure:", spec.key, error, flush=True)
    standalone = (
        ("driver", "build-city-bus-driver-3d-model.py", "DriverBuilder", "validate_driver_result"),
        ("bartender", "build-ordinary-bartender-3d-model.py", "OrdinaryBartenderBuilder", "validate_result"),
    )
    for name, filename, builder_name, validator_name in standalone:
        try:
            module = load("npc_joint_check_" + name, filename)
            result = getattr(module, builder_name)().build()
            report = getattr(module, validator_name)(result)
            reports.append((name, report.triangle_count, len(joints.manifest(result)["seams"])))
            print("NPC joints:", reports[-1], flush=True)
        except (RuntimeError, ValueError) as error:
            failures.append((name, str(error)))
            print("NPC joint failure:", name, error, flush=True)
    cashier = load("npc_joint_check_cashier", "build-supermarket-cashier-3d-model.py")
    for variant in (cashier.NORMAL_VARIANT, cashier.WATCHER_VARIANT):
        try:
            cashier.activate_variant(variant)
            atlas = cashier.DetailAtlasReport(ROOT / "Assets/Pedestrians/Textures" / cashier.DETAIL_ATLAS_NAME,
                                             __import__("hashlib").sha256(cashier.paint_cashier_detail_atlas().png_bytes()).hexdigest(),
                                             cashier.DETAIL_ATLAS_SIZE, cashier.DETAIL_ATLAS_SIZE)
            result = cashier.CashierBuilder(variant).build()
            report = cashier.validate_cashier_result(result, atlas, variant)
            reports.append(("cashier." + variant.key, report.triangle_count, len(joints.manifest(result)["seams"])))
            print("NPC joints:", reports[-1], flush=True)
        except (RuntimeError, ValueError) as error:
            failures.append(("cashier." + variant.key, str(error)))
            print("NPC joint failure:", variant.key, error, flush=True)
    residents = load("npc_joint_check_residents", "build-village-residents-3d-model.py")
    for name, builder in (("WoodWoman", lambda: residents.ResidentBuilder(True)),
                          ("Village male substrate", lambda: residents.ResidentBuilder(False)),
                          *((role, lambda role=role: residents.LifeResidentBuilder(role)) for role in residents.LIFE_ROLES)):
        try:
            result = builder().build()
            errors = []
            joints.validate(result, errors)
            if errors:
                raise RuntimeError("; ".join(errors))
            reports.append((name, residents.measured(result)["triangle_count"], len(joints.manifest(result)["seams"])))
            print("NPC joints:", reports[-1], flush=True)
        except (RuntimeError, ValueError) as error:
            failures.append((name, str(error)))
            print("NPC joint failure:", name, error, flush=True)
    for name, filename, builder_name in (("StationWorker", "build-default-npc-3d-model.py", "WorkerBuilder"),
                                         ("CanneryReceiver", "build-cannery-receiver-3d-model.py", "ReceiverBuilder")):
        try:
            module = load("npc_joint_check_" + name, filename)
            builder = getattr(module, builder_name)()
            result = builder.build()
            if name == "StationWorker":
                module.validate(builder, module.wardrobe(builder))
                module.hand_grip_manifest(builder)
            else:
                module.manifest(result, "source-check", "source-check")
            reports.append((name, module.resident.measured(result)["triangle_count"], len(joints.manifest(result)["seams"])))
            print("NPC joints:", reports[-1], flush=True)
        except (RuntimeError, ValueError) as error:
            failures.append((name, str(error)))
            print("NPC joint failure:", name, error, flush=True)
    if failures:
        raise RuntimeError("NPC joint source check failed:\n" + "\n".join(name + ": " + error for name, error in failures))
    print("NPC JOINT SURFACE SOURCE CHECK OK", flush=True)
    return reports


if __name__ == "__main__":
    check_npcs()
