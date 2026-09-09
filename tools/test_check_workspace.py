"""Focused regressions for the workspace checker; no Unity, Blender or network.

Run with `python -m unittest discover -s tools -p "test_check_workspace.py"` from
the repository root, or `python tools/test_check_workspace.py`.

Unlike the documentation checker's suite, this one exercises real git in a
throwaway repository. It has to: the checker's whole subject is what git calls
ignored, and the one bug that would cost real files — a Cyrillic path coming
back as `"\\320\\221..."` — is invisible to a mocked git.
"""
from __future__ import annotations

import contextlib
import importlib.util
import io
import os
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path
from unittest import mock

spec = importlib.util.spec_from_file_location(
    "check_workspace", Path(__file__).with_name("check-workspace.py")
)
check_workspace = importlib.util.module_from_spec(spec)
# @dataclass resolves its own module through sys.modules, so register before executing.
sys.modules["check_workspace"] = check_workspace
spec.loader.exec_module(check_workspace)


def entry(rel: str, base: Path, label: str = "") -> "check_workspace.Entry":
    return check_workspace.Entry(rel=rel, base=base, label=label)


class Harness(unittest.TestCase):
    """Builds a throwaway tree and runs the checker over a declared surface."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="check-workspace-")
        self.root = Path(self.temporary.name)
        self.addCleanup(self.temporary.cleanup)
        self.patch = mock.patch.object(check_workspace, "ROOT", self.root)
        self.patch.start()
        self.addCleanup(self.patch.stop)

    def touch(self, relative: str, age_days: float = 0.0, body: str = "x") -> Path:
        target = self.root / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(body, encoding="utf-8")
        if age_days:
            stamp = time.time() - age_days * 86400
            os.utime(target, (stamp, stamp))
        return target

    def run_checks(self, manifest: dict, surface: list[str]):
        checker = check_workspace.Checker(manifest)
        entries = [entry(rel, self.root) for rel in surface]
        with mock.patch.object(check_workspace, "ignored_entries", lambda: entries), \
             contextlib.redirect_stdout(io.StringIO()):
            checker.run()
        return checker

    @staticmethod
    def checks(findings, severity: str | None = None) -> list[str]:
        return [f.check for f in findings if severity is None or f.severity == severity]


class MatchingTests(unittest.TestCase):
    def test_directory_pattern_covers_the_directory_and_its_contents(self):
        self.assertTrue(check_workspace.Checker.matches("Library/", "Library/"))
        self.assertTrue(check_workspace.Checker.matches("Library/ArtifactDB", "Library/"))
        self.assertFalse(check_workspace.Checker.matches("LibraryNotes.md", "Library/"))

    def test_double_star_directory_matches_at_any_depth(self):
        for rel in ("tools/__pycache__/", "a/b/__pycache__/", "__pycache__/"):
            self.assertTrue(check_workspace.Checker.matches(rel, "**/__pycache__/"), rel)

    def test_double_star_file_matches_at_the_root_too(self):
        self.assertTrue(check_workspace.Checker.matches("a/b/c.blend1", "**/*.blend1"))
        self.assertTrue(check_workspace.Checker.matches("c.blend1", "**/*.blend1"))

    def test_longest_pattern_wins_so_the_meta_row_is_not_shadowed(self):
        checker = check_workspace.Checker({"paths": {
            "Assets/InitTestScene*.unity": {"policy": "never"},
            "Assets/InitTestScene*.unity.meta": {"policy": "declared"},
        }})
        pattern, row = checker.row_for(entry("Assets/InitTestSceneAB.unity.meta", Path(".")))
        self.assertEqual(pattern, "Assets/InitTestScene*.unity.meta")
        self.assertEqual(row["policy"], "declared")


class PolicyTests(Harness):
    def test_undeclared_ignored_path_is_an_error(self):
        checker = self.run_checks({"paths": {}}, ["something-new/"])
        self.assertEqual(self.checks(checker.findings), ["inventory/undeclared"])

    def test_never_fires_for_any_appearance(self):
        self.touch("tools/__pycache__/a.pyc")
        checker = self.run_checks(
            {"paths": {"**/__pycache__/": {"policy": "never", "sweep": True}}},
            ["tools/__pycache__/"],
        )
        self.assertEqual(self.checks(checker.findings), ["class/must-not-exist"])
        self.assertEqual([e.rel for e, _p, _s in checker.removable], ["tools/__pycache__/"])

    def test_retain_ignores_files_inside_the_window(self):
        self.touch("Logs/fresh.log", age_days=2)
        checker = self.run_checks(
            {"paths": {"Logs/": {"policy": "retain", "days": 45}}}, ["Logs/"]
        )
        self.assertEqual(checker.findings, [])

    def test_retain_fires_only_past_the_window(self):
        self.touch("Logs/fresh.log", age_days=2)
        self.touch("Logs/old.log", age_days=60)
        checker = self.run_checks(
            {"paths": {"Logs/": {"policy": "retain", "days": 45}}}, ["Logs/"]
        )
        self.assertEqual(self.checks(checker.findings), ["age/over-window"])
        self.assertIn("1 file(s) older than 45 days", checker.findings[0].message)

    def test_idle_reads_the_sentinel_not_the_directory_mtime(self):
        # The directory's own mtime only tracks its immediate children, so a deep cache
        # Unity is actively using reads as idle unless a sentinel is consulted.
        self.touch("Library/Search/deep.bin", age_days=90)
        self.touch("Library/ArtifactDB", age_days=0)
        stamp = time.time() - 90 * 86400
        os.utime(self.root / "Library", (stamp, stamp))
        manifest = {"paths": {"Library/": {
            "policy": "idle", "days": 14, "sentinels": ["ArtifactDB"],
        }}}
        self.assertEqual(self.run_checks(manifest, ["Library/"]).findings, [])

    def test_idle_fires_when_the_sentinel_is_stale_too(self):
        self.touch("Library/ArtifactDB", age_days=90)
        stamp = time.time() - 90 * 86400
        os.utime(self.root / "Library", (stamp, stamp))
        manifest = {"paths": {"Library/": {
            "policy": "idle", "days": 14, "sentinels": ["ArtifactDB"], "sweep": True,
        }}}
        checker = self.run_checks(manifest, ["Library/"])
        self.assertEqual(self.checks(checker.findings), ["age/idle-cache"])

    def test_declared_is_measured_but_never_offered_to_the_sweep(self):
        self.touch("Captures/AudioVhs/probe.wav")
        checker = self.run_checks(
            {"paths": {"Captures/": {"policy": "declared", "sweep": False}}}, ["Captures/"]
        )
        self.assertEqual(checker.findings, [])
        self.assertEqual(checker.removable, [])

    def test_max_count_is_per_checkout_not_across_worktrees(self):
        manifest = {"paths": {"*.slnx": {"policy": "allowed", "max_count": 1}}}
        checker = check_workspace.Checker(manifest)
        entries = [entry("a.slnx", self.root), entry("b.slnx", self.root, label="wt")]
        with mock.patch.object(check_workspace, "ignored_entries", lambda: entries), \
             contextlib.redirect_stdout(io.StringIO()):
            checker.run()
        self.assertEqual(checker.findings, [])

    def test_max_count_fires_within_one_checkout(self):
        manifest = {"paths": {"*.slnx": {"policy": "allowed", "max_count": 1}}}
        checker = self.run_checks(manifest, ["a.slnx", "b.slnx"])
        self.assertEqual(self.checks(checker.findings), ["size/too-many"])

    def test_pending_check_reports_as_a_warning_so_the_rule_lands_green(self):
        self.touch("tools/__pycache__/a.pyc")
        manifest = {
            "pending_checks": ["class/must-not-exist"],
            "paths": {"**/__pycache__/": {"policy": "never"}},
        }
        checker = self.run_checks(manifest, ["tools/__pycache__/"])
        self.assertEqual(self.checks(checker.findings, "warning"), ["class/must-not-exist"])
        self.assertEqual(self.checks(checker.findings, "error"), [])


class SweepGuardTests(Harness):
    def plan(self, checker, protected=frozenset()):
        with mock.patch.object(check_workspace, "cited_paths", lambda: set(protected)), \
             contextlib.redirect_stdout(io.StringIO()) as out:
            check_workspace.sweep(checker, apply=False)
        return out.getvalue()

    def make(self, rel: str, pattern: str, row: dict):
        checker = check_workspace.Checker({"paths": {pattern: row}})
        checker.removable = [(entry(rel, self.root), pattern, 1)]
        return checker

    def test_a_cited_file_is_refused(self):
        checker = self.make("Captures/x.png", "**/*.png", {"sweep": True})
        self.assertIn("refused (cited by a document)", self.plan(checker, {"Captures/x.png"}))

    def test_a_directory_holding_a_cited_file_is_refused(self):
        checker = self.make("Captures/", "Captures/", {"sweep": True})
        self.assertIn("refused (cited by a document)", self.plan(checker, {"Captures/x.png"}))

    def test_being_under_a_cited_directory_is_not_protection(self):
        # check-docs.py drops directory-shaped tokens, so they enforce nothing; treating
        # one as a shield would exempt every leftover beneath it.
        checker = self.make("tools/__pycache__/", "**/__pycache__/", {"sweep": True})
        self.assertIn("would remove", self.plan(checker, {"tools"}))

    def test_a_blend_autosave_is_refused_while_its_blend_is_dirty(self):
        checker = self.make("a/m.blend1", "**/*.blend1",
                            {"sweep": True, "guard": "sibling-blend-clean"})
        fake = lambda a, cwd=None: "" if a[0] == "ls-files" else " M a/m.blend"
        with mock.patch.object(check_workspace, "cited_paths", set), \
             mock.patch.object(check_workspace, "git", fake), \
             contextlib.redirect_stdout(io.StringIO()) as out:
            check_workspace.sweep(checker, apply=False)
        self.assertIn("has uncommitted changes", out.getvalue())

    def test_a_tracked_path_is_refused(self):
        checker = self.make("a/keep.txt", "**/*.txt", {"sweep": True})
        with mock.patch.object(check_workspace, "cited_paths", set), \
             mock.patch.object(check_workspace, "git", lambda a, cwd=None: "a/keep.txt"), \
             contextlib.redirect_stdout(io.StringIO()) as out:
            check_workspace.sweep(checker, apply=False)
        self.assertIn("refused (tracked)", out.getvalue())

    def test_the_sweep_aborts_when_the_protected_set_cannot_be_derived(self):
        # Fail closed: an unreadable check-docs.py must stop the sweep, never widen it.
        checker = self.make("a/x.pyc", "**/*.pyc", {"sweep": True})
        def boom():
            raise RuntimeError("cannot read tools/check-docs.py")
        with mock.patch.object(check_workspace, "cited_paths", boom), \
             contextlib.redirect_stdout(io.StringIO()) as out:
            code = check_workspace.sweep(checker, apply=True)
        self.assertEqual(code, 1)
        self.assertIn("refusing to remove anything", out.getvalue())


class RealGitTests(unittest.TestCase):
    """The one suite that shells out to git for real."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="check-workspace-git-")
        self.root = Path(self.temporary.name)
        self.addCleanup(self.temporary.cleanup)
        for args in (["init", "-q"], ["config", "user.email", "t@t"], ["config", "user.name", "t"]):
            subprocess.run(["git", *args], cwd=self.root, capture_output=True)
        (self.root / ".gitignore").write_text("Logs/\n*.slnx\n", encoding="utf-8")
        subprocess.run(["git", "add", ".gitignore"], cwd=self.root, capture_output=True)
        subprocess.run(["git", "commit", "-qm", "x"], cwd=self.root, capture_output=True)

    def test_a_cyrillic_ignored_name_comes_back_readable(self):
        # Without core.quotepath=false git answers "\320\221\320\260..." and every path
        # built from it resolves to nothing. The repository root is Cyrillic, so this is
        # not hypothetical.
        (self.root / "Барный Променад.slnx").write_text("x", encoding="utf-8")
        with mock.patch.object(check_workspace, "ROOT", self.root), \
             mock.patch.object(check_workspace, "worktrees", lambda: [(self.root, "")]):
            found = [e.rel for e in check_workspace.ignored_entries()]
        self.assertIn("Барный Променад.slnx", found)
        self.assertTrue((self.root / found[0]).exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
