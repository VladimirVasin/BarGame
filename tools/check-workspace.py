"""Workspace retention checker: what the working copy is allowed to keep.

`tools/check-docs.py` governs tracked documents. This is its sibling for the
other half of the checkout — the gitignored bytes no document can cite and no
commit can carry. Every ignored path declares a keep-policy in
`tools/workspace-budget.json`: `never`, `retain` for a window in days, `idle`
for a regenerable cache, `declared` for output a person curates, or `allowed`.
An ignored path with no row is itself an error, so a new kind of leftover
cannot appear unnoticed.

The subject is the whole checkout, not this directory: sibling worktrees are
read from `git worktree list`, because three idle `Library/` caches out there
held 5.7 GB while every in-repo measure stayed green.

Run `python tools/check-workspace.py`. Exit code 0 means clean (warnings
allowed), 1 means at least one error. `--sweep` lists what the manifest already
declared removable and `--sweep --apply` removes it. Standard library only.
"""
from __future__ import annotations

import argparse
import fnmatch
import importlib.util
import json
import os
import re
import shutil
import subprocess
import sys
import time
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MANIFEST_PATH = ROOT / "tools" / "workspace-budget.json"
DOCS_MANIFEST_PATH = ROOT / "tools" / "docs-budget.json"

# Unity Hub and the licensing client hold no file locks; only these do.
BUSY_PROCESSES = ("Unity.exe", "blender.exe", "Ableton Live")


@dataclass
class Finding:
    path: str
    line: int
    severity: str
    check: str
    message: str

    def render(self) -> str:
        where = f"{self.path}:{self.line}" if self.line else self.path
        return f"{where}: {self.severity}[{self.check}] {self.message}"


@dataclass
class Entry:
    """One ignored path, as git reports it. A directory keeps its trailing slash."""

    rel: str
    base: Path
    label: str

    @property
    def full(self) -> Path:
        return self.base / self.rel


def git(args: list[str], cwd: Path = ROOT) -> str:
    # core.quotepath=false is not optional here. The repository root is Cyrillic and
    # so are some capture names; git's default escapes them to "\320\221..." octal,
    # which then resolves to no file at all. check-docs.py gets away without it only
    # because no tracked path has a non-ASCII byte.
    out = subprocess.run(
        ["git", "-c", "core.quotepath=false", *args],
        cwd=cwd, capture_output=True, text=True, encoding="utf-8",
    )
    if out.returncode != 0:
        return ""
    return out.stdout


def worktrees() -> list[tuple[Path, str]]:
    """The main checkout first, then every sibling worktree."""
    found: list[tuple[Path, str]] = []
    for line in git(["worktree", "list", "--porcelain"]).splitlines():
        if line.startswith("worktree "):
            path = Path(line[len("worktree "):].strip())
            label = "" if path.resolve() == ROOT else path.name
            found.append((path, label))
    return found or [(ROOT, "")]


def ignored_entries() -> list[Entry]:
    entries: list[Entry] = []
    for base, label in worktrees():
        if not base.exists():
            continue
        listing = git(
            ["ls-files", "-o", "--directory", "--ignored", "--exclude-standard"], cwd=base
        )
        for rel in listing.splitlines():
            if rel:
                entries.append(Entry(rel=rel, base=base, label=label))
    return entries


def newest_mtime(path: Path, sentinels: list[str]) -> float:
    """How recently a cache was touched, without walking it.

    A directory's own mtime only tracks its immediate children, so `Library/Search`
    can read as 44 days idle while Unity is actively using it. The sentinels are
    files Unity rewrites on every launch; falling back to the directory itself is
    only for a cache shaped differently than expected.
    """
    stamps = [path.stat().st_mtime]
    for name in sentinels:
        target = path / name
        if target.exists():
            stamps.append(target.stat().st_mtime)
    return max(stamps)


def tree_bytes(path: Path) -> int:
    total = 0
    for current, _dirs, files in os.walk(path, onerror=lambda _e: None):
        for name in files:
            try:
                total += os.path.getsize(os.path.join(current, name))
            except OSError:
                continue
    return total


def cited_paths() -> set[str]:
    """Every backticked token in a live document, over-approximated on purpose.

    check-docs.py drops most of these before enforcing them, but this set guards a
    delete: over-protecting costs a leftover file, under-protecting costs a canon
    reference. The rule is imported rather than restated so it cannot drift — a
    hand-copied list goes stale the first time a bible cites a new capture.
    """
    source = ROOT / "tools" / "check-docs.py"
    spec = importlib.util.spec_from_file_location("check_docs", source)
    if spec is None or spec.loader is None or not source.exists():
        # Fail closed. An empty set here would read as "nothing is protected" at the one
        # moment that answer deletes things.
        raise RuntimeError(f"cannot read {source}; refusing to decide what is protected")
    check_docs = importlib.util.module_from_spec(spec)
    sys.modules["check_docs"] = check_docs
    spec.loader.exec_module(check_docs)

    manifest = json.loads(DOCS_MANIFEST_PATH.read_text(encoding="utf-8"))
    documents = manifest.get("documents", {})
    tokens: set[str] = set()
    for path, row in documents.items():
        # A ledger records what was true on its date and a retired note names a
        # withdrawn pipeline. check-docs.py exempts both, and so does this.
        if row.get("type") == "ledger" or row.get("retired", False):
            continue
        if not (ROOT / path).exists():
            continue
        doc = check_docs.load_document(path)
        for index, line in enumerate(doc.lines, start=1):
            if index in doc.fenced:
                continue
            for token in check_docs.BACKTICKED.findall(line):
                token = token.strip()
                if "/" in token:
                    tokens.add(token.rstrip("/"))
    return tokens


class Checker:
    def __init__(self, manifest: dict, strict: bool = False):
        self.manifest = manifest
        self.paths: dict[str, dict] = manifest.get("paths", {})
        self.pending: set[str] = set(manifest.get("pending_checks", []))
        self.worktree_apply: tuple[str, ...] = tuple(
            manifest.get("worktrees", {}).get("apply", [])
        )
        self.strict = strict
        self.findings: list[Finding] = []
        self.removable: list[tuple[Entry, str, int]] = []

    # -- reporting ---------------------------------------------------------
    def error(self, path: str, line: int, check: str, message: str) -> None:
        # Same contract as check-docs.py: a pending check states a debt and reports as
        # a warning, so the rule is green the day it lands.
        severity = "warning" if check in self.pending else "error"
        self.findings.append(Finding(path, line, severity, check, message))

    def warn(self, path: str, line: int, check: str, message: str) -> None:
        self.findings.append(Finding(path, line, "warning", check, message))

    # -- matching ----------------------------------------------------------
    def row_for(self, entry: Entry) -> tuple[str, dict] | tuple[None, None]:
        """The most specific declaration covering this entry.

        Longest pattern wins, so `Assets/InitTestScene*.unity.meta` beats
        `Assets/InitTestScene*.unity` and a literal beats a glob.
        """
        rel = entry.rel
        best: tuple[str, dict] | tuple[None, None] = (None, None)
        for pattern, row in self.paths.items():
            if self.matches(rel, pattern) and (best[0] is None or len(pattern) > len(best[0])):
                best = (pattern, row)
        return best

    @staticmethod
    def matches(rel: str, pattern: str) -> bool:
        if pattern.endswith("/"):
            stem = pattern.rstrip("/")
            if rel.rstrip("/") == stem or rel.startswith(stem + "/"):
                return True
            if stem.startswith("**/"):
                tail = stem[3:]
                return rel.rstrip("/").endswith("/" + tail) or rel.rstrip("/") == tail
            return False
        if pattern.startswith("**/"):
            return fnmatch.fnmatch(rel, pattern) or fnmatch.fnmatch(rel, pattern[3:])
        return fnmatch.fnmatch(rel, pattern)

    # -- checks ------------------------------------------------------------
    def run(self, quiet: bool = False) -> int:
        entries = ignored_entries()
        # Counted per checkout: four worktrees each keeping one solution file is right,
        # one worktree keeping four is the thing that happened.
        counts: dict[tuple[str, str], list[Entry]] = {}
        for entry in entries:
            pattern, row = self.row_for(entry)
            if row is None:
                self.error(
                    self.where(entry), 0, "inventory/undeclared",
                    "is ignored but has no row in tools/workspace-budget.json. Declare it "
                    "with a policy from {never,retain,idle,declared,allowed}, or stop writing it.",
                )
                continue
            counts.setdefault((entry.label, pattern), []).append(entry)
            self.check_entry(entry, pattern, row)
        for (label, pattern), seen in sorted(counts.items()):
            limit = self.paths.get(pattern, {}).get("max_count")
            if limit is not None and len(seen) > limit:
                self.error(
                    self.where(seen[0]), 0, "size/too-many",
                    f"{len(seen)} files match `{pattern}` in {label or 'the main checkout'} and "
                    f"the manifest allows {limit}: " + ", ".join(sorted(e.rel for e in seen)) + ".",
                )
        return self.report(len(entries), quiet=quiet)

    @staticmethod
    def where(entry: Entry) -> str:
        return f"{entry.label}/{entry.rel}" if entry.label else entry.rel

    def check_entry(self, entry: Entry, pattern: str, row: dict) -> None:
        policy = row.get("policy", "allowed")
        target = entry.full
        if not target.exists():
            return
        if policy == "never":
            size = tree_bytes(target) if target.is_dir() else target.stat().st_size
            self.error(
                self.where(entry), 0, "class/must-not-exist",
                f"matches `{pattern}`, which must not exist ({human(size)}). "
                + (row.get("regenerated_by") and f"Regenerated by {row['regenerated_by']}. " or "")
                + "Run python tools/check-workspace.py --sweep --apply.",
            )
            self.offer(entry, pattern, size)
        elif policy == "retain":
            days = row.get("days", 30)
            cutoff = time.time() - days * 86400
            stale = [f for f in walk_files(target) if f.stat().st_mtime < cutoff]
            if stale:
                size = sum(f.stat().st_size for f in stale)
                self.error(
                    self.where(entry), 0, "age/over-window",
                    f"holds {len(stale)} file(s) older than {days} days ({human(size)}). "
                    "Run python tools/check-workspace.py --sweep --apply.",
                )
                self.offer(entry, pattern, size)
        elif policy == "idle":
            days = row.get("days", 14)
            if not target.is_dir():
                return
            idle_days = (time.time() - newest_mtime(target, row.get("sentinels", []))) / 86400
            if idle_days > days:
                size = tree_bytes(target)
                self.error(
                    self.where(entry), 0, "age/idle-cache",
                    f"has been idle {int(idle_days)} days, past {days} ({human(size)}). "
                    + (row.get("regenerated_by") and f"{row['regenerated_by'].capitalize()}. " or "")
                    + "Run python tools/check-workspace.py --sweep --apply.",
                )
                self.offer(entry, pattern, size)

    def offer(self, entry: Entry, pattern: str, size: int) -> None:
        if self.paths.get(pattern, {}).get("sweep"):
            self.removable.append((entry, pattern, size))

    def report(self, scanned: int, quiet: bool = False) -> int:
        errors = [f for f in self.findings if f.severity == "error"]
        deferred = [f for f in self.findings if f.check in self.pending]
        warnings = [
            f for f in self.findings if f.severity == "warning" and f.check not in self.pending
        ]
        # A session-start probe should say nothing when there is nothing to say.
        if quiet and not errors and not warnings:
            return 0
        for finding in sorted(self.findings, key=lambda f: (f.path, f.line)):
            print(finding.render())
        summary = f"\n{len(errors)} error(s), {len(warnings)} warning(s)"
        if deferred:
            summary += f", {len(deferred)} pending ({', '.join(sorted({f.check for f in deferred}))})"
        print(summary + f" across {scanned} ignored path(s)")
        if errors:
            return 1
        return 1 if (self.strict and warnings) else 0


def walk_files(target: Path) -> list[Path]:
    if target.is_file():
        return [target]
    found: list[Path] = []
    for current, _dirs, files in os.walk(target, onerror=lambda _e: None):
        for name in files:
            candidate = Path(current) / name
            try:
                candidate.stat()
            except OSError:
                continue
            found.append(candidate)
    return found


def human(size: int) -> str:
    for unit in ("B", "KB", "MB", "GB"):
        if size < 1024 or unit == "GB":
            return f"{size:.0f} {unit}" if unit == "B" else f"{size:.1f} {unit}"
        size /= 1024.0
    return f"{size:.1f} GB"


# -- the sweep -------------------------------------------------------------


def busy() -> list[str]:
    """Every reason not to touch the checkout right now."""
    reasons: list[str] = []
    listing = subprocess.run(
        ["tasklist"], capture_output=True, text=True, errors="replace"
    ).stdout if os.name == "nt" else ""
    for name in BUSY_PROCESSES:
        if name.lower() in listing.lower():
            reasons.append(f"{name} is running")
    for base, label in worktrees():
        lock = base / "Library" / "EditorInstance.json"
        if lock.exists():
            reasons.append(f"Unity holds {label or 'the main checkout'} ({lock.name} present)")
    return reasons


def sweep(checker: Checker, apply: bool) -> int:
    if not checker.removable:
        print("nothing declared removable")
        return 0

    try:
        protected = cited_paths()
    except (RuntimeError, OSError, ValueError) as failure:
        print(f"refusing to remove anything: {failure}")
        return 1
    plan: list[tuple[Entry, int]] = []
    for entry, pattern, size in checker.removable:
        rel = entry.rel.rstrip("/")
        # 1. A live document cites the file itself, or cites something inside it. Being
        # *under* a cited directory is not protection: check-docs.py drops any token
        # with a trailing slash or no extension, so a directory citation enforces
        # nothing, and treating it as a shield would exempt every leftover beneath it.
        if any(rel == token or token.startswith(rel + "/") for token in protected):
            print(f"refused (cited by a document): {checker.where(entry)}")
            continue
        # 2. Git tracks it. Asked of git, never of a path list.
        if git(["ls-files", "--error-unmatch", "--", entry.rel], cwd=entry.base).strip():
            print(f"refused (tracked): {checker.where(entry)}")
            continue
        # 3. A Blender autosave is load-bearing while its .blend is uncommitted.
        if checker.paths.get(pattern, {}).get("guard") == "sibling-blend-clean":
            blend = entry.rel[: -len("1")]
            if git(["status", "--porcelain", "--", blend], cwd=entry.base).strip():
                print(f"refused ({blend} has uncommitted changes): {checker.where(entry)}")
                continue
        plan.append((entry, size))

    total = sum(size for _entry, size in plan)
    for entry, size in plan:
        print(f"{'removing' if apply else 'would remove'} {checker.where(entry)} ({human(size)})")
    print(f"\n{len(plan)} path(s), {human(total)}")

    if not apply:
        print("dry run; pass --apply to remove")
        return 0

    reasons = busy()
    if reasons:
        print("\nrefusing to remove anything: " + "; ".join(reasons))
        return 1

    for entry, _size in plan:
        target = entry.full
        row = checker.paths.get(checker.row_for(entry)[0] or "", {})
        if row.get("policy") == "retain":
            days = row.get("days", 30)
            cutoff = time.time() - days * 86400
            for stale in walk_files(target):
                if stale.stat().st_mtime < cutoff:
                    stale.unlink(missing_ok=True)
        elif target.is_dir():
            shutil.rmtree(target, ignore_errors=True)
        else:
            target.unlink(missing_ok=True)

    # The last invariant: prove no tracked file left with them.
    deleted = [
        line for line in git(["status", "--porcelain"]).splitlines() if re.match(r"^ ?D", line)
    ]
    if deleted:
        print("\nERROR: the sweep removed tracked files:\n" + "\n".join(deleted))
        return 1
    print(f"\nremoved {human(total)}; no tracked file changed")
    return 0


def main() -> int:
    # Worktree and capture names are Cyrillic. On a Windows console the default code
    # page turns them into question marks, which makes a reported path unusable.
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--warnings-as-errors", action="store_true")
    parser.add_argument("--sweep", action="store_true", help="list what is declared removable")
    parser.add_argument("--apply", action="store_true", help="with --sweep, actually remove it")
    parser.add_argument("--quiet", action="store_true", help="print only when something is wrong")
    args = parser.parse_args()

    if not MANIFEST_PATH.exists():
        sys.exit(f"{MANIFEST_PATH.name} is missing; the workspace has no declared policy")
    manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))

    checker = Checker(manifest, strict=args.warnings_as_errors)
    code = checker.run(quiet=args.quiet and not args.sweep)

    if args.sweep:
        return sweep(checker, apply=args.apply)
    return code


if __name__ == "__main__":
    sys.exit(main())
