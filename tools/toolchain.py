"""Read-only checks of the checked-in art/native toolchain, without generating assets."""
from __future__ import annotations

import argparse
import importlib.metadata
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parent


def load_config() -> dict:
    return json.loads((TOOLS / "toolchain.json").read_text(encoding="utf-8"))


def require_version(name: str, actual: str, expected: str) -> None:
    if actual != expected:
        raise RuntimeError(f"{name}: installed {actual}, required {expected} (tools/toolchain.json)")


def check_python(config: dict, packages: tuple[str, ...] = ()) -> None:
    require_version("Python", platform.python_version(), config["python"])
    for package in packages:
        try:
            actual = importlib.metadata.version(package)
        except importlib.metadata.PackageNotFoundError as error:
            raise RuntimeError(f"Install {package}=={config['packages'][package]} for this toolchain") from error
        require_version(package, actual, config["packages"][package])


def blender_path(config: dict, override: str | None = None) -> Path:
    candidate = override or os.environ.get("BP_BLENDER") or config["blender"]["windows_path"]
    path = Path(candidate)
    if not path.is_file():
        found = shutil.which(candidate)
        if not found:
            raise RuntimeError("Blender is missing; set BP_BLENDER or use --blender with the pinned executable")
        path = Path(found)
    return path.resolve()


def check_blender(config: dict, executable: Path) -> None:
    result = subprocess.run([str(executable), "--version"], capture_output=True,
                            text=True, check=True, timeout=30)
    expected = config["blender"]
    lines = result.stdout.splitlines()
    if not lines or lines[0].strip() != f"Blender {expected['version']}":
        raise RuntimeError(f"Blender must be {expected['version']}; got {lines[:1]}")
    if not any(line.strip() == f"build hash: {expected['build_hash']}" for line in lines):
        raise RuntimeError(f"Blender build hash must be {expected['build_hash']}")


def find_visual_studio(config: dict) -> Path:
    if sys.platform != config["native"]["platform"]:
        raise RuntimeError("The native audio toolchain currently supports Windows x64 only")
    program_files = os.environ.get("ProgramFiles(x86)")
    if not program_files:
        raise RuntimeError("Visual Studio Installer/vswhere.exe is required")
    sdk = Path(program_files) / "Windows Kits/10/Lib" / config["native"]["windows_sdk"]
    if not (sdk / "um/x64/kernel32.lib").is_file():
        raise RuntimeError(f"Install Windows SDK {config['native']['windows_sdk']}")
    vswhere = Path(program_files) / "Microsoft Visual Studio/Installer/vswhere.exe"
    result = subprocess.run([str(vswhere), "-products", "*", "-requires",
                             "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
                             "-property", "installationPath"], capture_output=True,
                            text=True, check=True, timeout=15)
    version = config["native"]["msvc_tools"]
    for line in result.stdout.splitlines():
        installation = Path(line.strip())
        if (installation / "VC/Tools/MSVC" / version / "bin/Hostx64/x64/cl.exe").is_file():
            return installation
    raise RuntimeError(f"Install MSVC tools {version} with the Visual Studio Desktop C++ workload")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scope", choices=("python", "blender", "native", "all"), default="all")
    parser.add_argument("--blender")
    parser.add_argument("--visual-studio-path", action="store_true",
                        help="Print only the installation containing the pinned compiler")
    args = parser.parse_args()
    config = load_config()
    if args.visual_studio_path:
        print(find_visual_studio(config))
        return 0
    packages = tuple(config["packages"]) if args.scope in ("python", "all") else (
        ("numpy",) if args.scope == "native" else ())
    check_python(config, packages)
    if args.scope in ("blender", "all"):
        check_blender(config, blender_path(config, args.blender))
    if args.scope in ("native", "all"):
        find_visual_studio(config)
    print(f"Toolchain preflight passed ({args.scope}).")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, OSError, subprocess.SubprocessError) as error:
        print(f"Toolchain preflight failed: {error}", file=sys.stderr)
        raise SystemExit(1)
