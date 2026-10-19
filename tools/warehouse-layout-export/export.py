#!/usr/bin/env python3
"""Export authoritative warehouse placements without starting the Unity editor.

This is a placement-only projection, not a substitute for runtime validation.
The generated planner retains every position/kind/seed statement verbatim.
Native lamp rotations and navigation/validation are explicitly omitted. All generated
source and .NET build products stay under the declared Captures directory.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Assets/Scripts/Runtime/Combat/WarehouseArenaPlan.cs"
BUILD = ROOT / "Captures/Tooling/WarehouseLayout"


def digest(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def block_end(text, start):
    """Find a C# block's end while ignoring strings and comments."""
    token = re.compile(r'//[^\n]*|/\*[\s\S]*?\*/|@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|[{}]')
    depth = 0
    found = False
    for match in token.finditer(text, start):
        value = match.group()
        if value == "{":
            depth += 1
            found = True
        elif value == "}":
            depth -= 1
            if found and depth == 0:
                return match.end()
    raise RuntimeError("Unclosed source block")


def once(text, anchor):
    if text.count(anchor) != 1:
        raise RuntimeError(f"Expected one source anchor: {anchor!r}")
    return text.index(anchor)


def project_source(text):
    omissions = []

    def omit_block(anchor, expected_sha256, replacement="", initializer=False):
        nonlocal text
        start = once(text, anchor)
        end = block_end(text, start)
        if initializer:
            if text[end:end + 1] != ";":
                raise RuntimeError("Lamp initializer must end with a semicolon")
            end += 1
        omitted = text[start:end]
        actual = digest(omitted)
        if actual != expected_sha256:
            raise RuntimeError(f"Omitted source changed at {anchor!r}; review before updating its guard: {actual}")
        omissions.append({"anchor": anchor, "sha256": actual})
        text = text[:start] + replacement + text[end:]

    for original, replacement in (
        ("ModelRotation = modelRotation ?? Quaternion.FromToRotation(Vector3.down, Direction);",
         "ModelRotation = default; // Native rotation omitted from placement projection."),
        ("Quaternion.Euler(0f, yaw - 90f, 0f)", "default(Quaternion)"),
        # Shelf/approach positions are now evaluated by the planner itself.
        # Quaternion.Euler needs Unity native code; these exact yaw-only
        # replacements retain their metre offsets in the managed projection.
        ("Quaternion.Euler(0f, Yaw, 0f) *\n            new Vector3(BayOffset(bay), 0f, -1.45f)",
         "new Vector3(BayOffset(bay) * Mathf.Cos(Yaw * Mathf.Deg2Rad) - 1.45f * Mathf.Sin(Yaw * Mathf.Deg2Rad), "
         "0f, -BayOffset(bay) * Mathf.Sin(Yaw * Mathf.Deg2Rad) - 1.45f * Mathf.Cos(Yaw * Mathf.Deg2Rad))"),
        ("Quaternion.Euler(0f, Yaw, 0f) *\n            new Vector3(BayOffset(bay), ShelfHeight, -.08f)",
         "new Vector3(BayOffset(bay) * Mathf.Cos(Yaw * Mathf.Deg2Rad) - .08f * Mathf.Sin(Yaw * Mathf.Deg2Rad), "
         "ShelfHeight, -BayOffset(bay) * Mathf.Sin(Yaw * Mathf.Deg2Rad) - .08f * Mathf.Cos(Yaw * Mathf.Deg2Rad))"),
    ):
        once(text, original)
        omissions.append({"anchor": original, "sha256": digest(original)})
        text = text.replace(original, replacement)
    for call in ("BuildNavigation(plan);", "WarehouseArenaValidator.ValidateOrThrow(plan);"):
        once(text, call)
        omissions.append({"anchor": call, "sha256": digest(call)})
        text = text.replace(call, "// Omitted by placement-only exporter.")
    omit_block("private static void BuildNavigation(WarehouseArenaPlan plan)", "aa898c58a126b76bc5d907fdffc338eca5055c0c0068949c81a8d50a8e798a97")
    omit_block("public static class WarehouseArenaValidator", "852a2832b88dc8cd8f7a292600d43696ab03faaa3ab6ee8199dd4bade8b7c06b")
    return text, omissions


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity-editor", type=Path, help="Pinned Unity executable; editor is never started")
    parser.add_argument("--output", type=Path, default=ROOT / "ArtSource/CombatWarehouse/WarehouseLayout.json")
    args = parser.parse_args()

    version_text = (ROOT / "ProjectSettings/ProjectVersion.txt").read_text(encoding="utf-8")
    version = re.search(r"^m_EditorVersion: (\S+)", version_text, re.MULTILINE).group(1)
    unity = args.unity_editor or Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Unity/Hub/Editor" / version / "Editor/Unity.exe"
    managed = unity.parent / "Data/Managed/UnityEngine"
    if not (managed / "UnityEngine.CoreModule.dll").is_file():
        raise RuntimeError(f"Pinned Unity {version} managed DLL not found: {managed}")

    original = SOURCE.read_text(encoding="utf-8")
    projected, omissions = project_source(original)
    generated = BUILD / "GeneratedWarehouseArenaPlan.cs"
    metadata = BUILD / "Projection.json"
    BUILD.mkdir(parents=True, exist_ok=True)
    generated.write_text(projected, encoding="utf-8", newline="\n")
    metadata.write_text(json.dumps({
        "source": SOURCE.relative_to(ROOT).as_posix(),
        "source_sha256": hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
        "projection_sha256": digest(projected),
        "unity_version": version,
        "scope": "placement-only; native lamp rotations, navigation and runtime validation are omitted",
        "omissions": omissions,
    }, indent=2) + "\n", encoding="utf-8", newline="\n")
    command = ["dotnet", "run", "--project", str(Path(__file__).with_name("WarehouseLayoutExport.csproj")),
               "--configuration", "Release", f"-p:UnityManagedDir={managed}",
               "--", str(metadata), str(args.output.resolve())]
    subprocess.run(command, cwd=ROOT, check=True)


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, subprocess.CalledProcessError) as exception:
        print(f"Warehouse placement export failed: {exception}", file=sys.stderr)
        sys.exit(1)
