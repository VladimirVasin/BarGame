"""Checked output validation and rollback-safe publication shared by art and native tools."""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
import uuid

from toolchain import ROOT


@contextmanager
def workspace_temporary_directory(prefix: str):
    """Use inherited workspace ACLs (Windows Python's mkdtemp uses private ACLs)."""
    scratch = ROOT / "Captures/Tooling"
    scratch.mkdir(parents=True, exist_ok=True)
    directory = scratch / (prefix + uuid.uuid4().hex)
    directory.mkdir()
    try:
        yield directory
    finally:
        resolved = directory.resolve()
        if not resolved.is_relative_to(scratch.resolve()) or resolved == scratch.resolve():
            raise RuntimeError("Refusing to clean a staging directory outside Captures/Tooling")
        shutil.rmtree(resolved)


def repository_path(value: str, roots: tuple[str, ...]) -> Path:
    path = (ROOT / value).resolve()
    if not any(path.is_relative_to(ROOT / folder) for folder in roots):
        raise ValueError(f"Path must stay inside {', '.join(roots)}: {value}")
    return path


def output_stamp(path: Path) -> tuple[int, int] | None:
    if not path.is_file():
        return None
    info = path.stat()
    return info.st_mtime_ns, info.st_size


def validate_output(path: Path, before: tuple[int, int] | None) -> None:
    after = output_stamp(path)
    if after is None or after[1] == 0:
        raise RuntimeError(f"Generator did not produce a nonempty expected output: {path}")
    if before is not None and before == after:
        raise RuntimeError(f"Expected output was not refreshed by this invocation: {path}")
    if path.suffix.lower() == ".json":
        json.loads(path.read_text(encoding="utf-8"))


def publish_files(files: list[tuple[Path, Path]]) -> None:
    """Validate the complete batch first; replace each file, rolling back on failure."""
    destinations = [destination for _, destination in files]
    if len(set(destinations)) != len(destinations):
        raise ValueError("Two staged files target the same published path")
    for source, destination in files:
        repository_path(str(destination), ("Assets", "ArtSource", "Captures"))
        if not source.is_file() or source.stat().st_size == 0 or destination.suffix == ".meta":
            raise ValueError(f"Invalid staged publication: {source} -> {destination}")
    with workspace_temporary_directory("publish-") as backup_directory:
        backups: list[tuple[Path, Path | None]] = []
        try:
            for index, (source, destination) in enumerate(files):
                backup = Path(backup_directory) / str(index) if destination.exists() else None
                if backup is not None:
                    shutil.copy2(destination, backup)
                destination.parent.mkdir(parents=True, exist_ok=True)
                handle, pending_name = tempfile.mkstemp(prefix=".bp-publish-", dir=destination.parent)
                os.close(handle)
                pending = Path(pending_name)
                try:
                    shutil.copy2(source, pending)
                    os.replace(pending, destination)
                    backups.append((destination, backup))
                finally:
                    pending.unlink(missing_ok=True)
        except BaseException:
            for destination, backup in reversed(backups):
                if backup is None:
                    destination.unlink(missing_ok=True)
                else:
                    shutil.copy2(backup, destination)
            raise


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True)
    parser.add_argument("--destination", required=True)
    args = parser.parse_args()
    try:
        source = repository_path(args.source, ("Captures",))
        destination = repository_path(args.destination, ("Assets", "ArtSource"))
        publish_files([(source, destination)])
    except (RuntimeError, ValueError, OSError) as error:
        print(f"Asset publication failed: {error}", file=sys.stderr)
        raise SystemExit(1)
