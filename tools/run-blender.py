#!/usr/bin/env python3
"""Run one generator with checked tools, propagated failures and fresh expected outputs.

python tools/run-blender.py tools/build-city-pedestrian-3d-model.py \
  --expect Assets/Pedestrians/Models/CityPedestrian3D.fbx -- --archetype lampshade --no-preview

For a generator with output-directory arguments, --stage-output maps such an
argument to its normal repository destination, e.g. --stage-output=--model-dir=Assets/...
and --stage-output=--source-dir=ArtSource/... . Map ALL of that invocation's output
directories to keep generation isolated; the generator's other defaults are unchanged.
Expected paths under mapped directories are checked in the staging tree. Published
files preserve existing .meta sidecars. No publication happens on generator failure.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import subprocess
import sys

import toolchain
from asset_pipeline import (repository_path, output_stamp, validate_output, publish_files,
                            workspace_temporary_directory)

ROOT = toolchain.ROOT


def command(executable: Path, generator: Path, arguments: list[str]) -> list[str]:
    return [str(executable), "--background", "--factory-startup", "--python-exit-code", "1",
            "--python", str(generator), "--", *arguments]


def run(args: argparse.Namespace, generator_arguments: list[str]) -> None:
    config = toolchain.load_config()
    toolchain.check_python(config)
    executable = toolchain.blender_path(config, args.blender)
    toolchain.check_blender(config, executable)
    generator = repository_path(args.generator, ("tools",))
    if not generator.is_file() or generator.suffix != ".py":
        raise ValueError(f"Missing Python generator: {generator}")
    expected = [repository_path(value, ("Assets", "ArtSource", "Captures")) for value in args.expect]
    if not expected and not args.validate_only:
        raise ValueError("Declare at least one --expect output, or use --validate-only")
    if args.validate_only and args.stage_output:
        raise ValueError("Validation-only runs cannot publish staged outputs")
    with workspace_temporary_directory("blender-") as staging:
        mappings: list[tuple[Path, Path]] = []
        arguments = list(generator_arguments)
        flags: set[str] = set()
        for index, mapping in enumerate(args.stage_output):
            flag, separator, destination_value = mapping.partition("=")
            if not separator or not flag.startswith("--") or flag in flags:
                raise ValueError("Use unique --stage-output=--generator-directory-option=destination")
            if any(value == flag or value.startswith(flag + "=") for value in arguments):
                raise ValueError(f"Generator argument {flag} is already managed by staging")
            flags.add(flag)
            destination = repository_path(destination_value, ("Assets", "ArtSource", "Captures"))
            if any(destination.is_relative_to(other) or other.is_relative_to(destination)
                   for other, _ in mappings):
                raise ValueError("Staged destination directories must not overlap")
            staged = Path(staging) / str(index)
            staged.mkdir()
            mappings.append((destination, staged))
            arguments.extend((flag, str(staged)))
        if args.validate_only and "--validate-only" not in arguments:
            arguments.append("--validate-only")
        resolved_expected = [next((staged / path.relative_to(destination)
                                  for destination, staged in mappings if path.is_relative_to(destination)), path)
                             for path in expected]
        if mappings and any(not path.is_relative_to(Path(staging)) for path in resolved_expected):
            raise ValueError("Every expected output must belong to a staged directory when staging is used")
        before = [output_stamp(path) for path in resolved_expected]
        subprocess.run(command(executable, generator, arguments), cwd=ROOT, check=True)
        for path, previous in zip(resolved_expected, before):
            validate_output(path, previous)
        files = [(path, destination / path.relative_to(staged))
                 for destination, staged in mappings for path in staged.rglob("*")
                 if path.is_file() and path.suffix != ".meta" and not path.name.endswith(".blend1")]
        if mappings and not files:
            raise RuntimeError("The staged generator produced no files to publish")
        if files:
            publish_files(files)
        print(f"Blender generator passed; verified {len(expected)} expected outputs, published {len(files)} staged files.")


def main() -> int:
    argv = sys.argv[1:]
    separator = argv.index("--") if "--" in argv else len(argv)
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("generator")
    parser.add_argument("--blender")
    parser.add_argument("--expect", action="append", default=[])
    parser.add_argument("--stage-output", action="append", default=[])
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args(argv[:separator])
    run(args, argv[separator + 1:])
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, ValueError, OSError, subprocess.SubprocessError) as error:
        print(f"Blender generation failed: {error}", file=sys.stderr)
        raise SystemExit(1)
