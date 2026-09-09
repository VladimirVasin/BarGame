"""Focused regressions for the documentation checker; no Unity, Blender or network.

Run with `python -m unittest discover -s tools -p "test_check_docs.py"` from the
repository root, or `python tools/test_check_docs.py`.
"""
from __future__ import annotations

import contextlib
import importlib.util
import io
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

spec = importlib.util.spec_from_file_location(
    "check_docs", Path(__file__).with_name("check-docs.py")
)
check_docs = importlib.util.module_from_spec(spec)
# @dataclass resolves its own module through sys.modules, so register before executing.
sys.modules["check_docs"] = check_docs
spec.loader.exec_module(check_docs)


class CheckerHarness(unittest.TestCase):
    """Builds a throwaway document tree and runs the checker over it."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="check-docs-")
        self.root = Path(self.temporary.name)
        self.addCleanup(self.temporary.cleanup)
        self.patch = mock.patch.object(check_docs, "ROOT", self.root)
        self.patch.start()
        self.addCleanup(self.patch.stop)

    def write(self, relative: str, text: str) -> None:
        target = self.root / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding="utf-8", newline="\n")

    def run_checks(self, manifest: dict, markdown: list[str], code: list[str] | None = None):
        checker = check_docs.Checker(manifest)
        with mock.patch.object(check_docs, "tracked_markdown", lambda: markdown), \
             mock.patch.object(check_docs, "tracked_code", lambda exclude=(): code or []), \
             contextlib.redirect_stdout(io.StringIO()):
            checker.run()
        return checker.findings

    @staticmethod
    def checks(findings, severity: str | None = None) -> list[str]:
        return [f.check for f in findings if severity is None or f.severity == severity]


class LedgerTests(CheckerHarness):
    MANIFEST = {
        "enforce_ledgers_from": "2026-09-01",
        "types": {"ledger": {}},
        "documents": {
            "log.md": {
                "type": "ledger", "budget": 100000, "entry_heading": "## ", "entry_max": 200,
            }
        },
    }

    def test_a_second_entry_for_one_date_is_refused_once_the_rule_applies(self):
        self.write("log.md", "# Log\n\n## 2026-09-05 — first\n\n- one\n\n## 2026-09-05 — second\n\n- two\n")
        findings = self.run_checks(self.MANIFEST, ["log.md"])
        self.assertIn("ledger/duplicate-date", self.checks(findings))

    def test_entries_written_before_the_rule_are_grandfathered(self):
        self.write("log.md", "# Log\n\n## 2026-08-05 — first\n\n- one\n\n## 2026-08-05 — second\n\n- two\n")
        findings = self.run_checks(self.MANIFEST, ["log.md"])
        self.assertNotIn("ledger/duplicate-date", self.checks(findings))
        self.assertNotIn("ledger/entry-too-large", self.checks(findings))

    def test_order_is_enforced_over_the_whole_file_including_old_entries(self):
        self.write("log.md", "# Log\n\n## 2026-08-01 — older\n\n- one\n\n## 2026-08-09 — newer\n\n- two\n")
        findings = self.run_checks(self.MANIFEST, ["log.md"])
        self.assertIn("ledger/out-of-order", self.checks(findings))

    def test_an_oversized_entry_is_refused(self):
        self.write("log.md", "# Log\n\n## 2026-09-05 — big\n\n" + "- filler line\n" * 40)
        findings = self.run_checks(self.MANIFEST, ["log.md"])
        self.assertIn("ledger/entry-too-large", self.checks(findings))

    def test_gitignored_artifact_paths_are_refused_but_do_not_trip_the_path_check(self):
        self.write("log.md", "# Log\n\n## 2026-09-05 — run\n\n- see `Captures/Village/report.json`\n")
        findings = self.run_checks(self.MANIFEST, ["log.md"])
        self.assertIn("ledger/ephemeral-path", self.checks(findings))
        self.assertNotIn("ref/dead-path", self.checks(findings))


class SizeAndShapeTests(CheckerHarness):
    def test_crlf_does_not_inflate_the_measured_size(self):
        (self.root / "doc.md").write_bytes(b"# T\r\n\r\nbody\r\n")
        manifest = {"types": {"state": {}}, "documents": {"doc.md": {"type": "state", "budget": 13}}}
        findings = self.run_checks(manifest, ["doc.md"])
        self.assertNotIn("size/over-budget", self.checks(findings))

    def test_over_budget_is_reported_with_the_remedy_for_its_type(self):
        self.write("doc.md", "# T\n\n" + "x" * 500)
        manifest = {"types": {"state": {}}, "documents": {"doc.md": {"type": "state", "budget": 100}}}
        findings = self.run_checks(manifest, ["doc.md"])
        self.assertIn("size/over-budget", self.checks(findings))
        self.assertIn("rewrite", findings[0].message)

    def test_a_table_header_is_not_mistaken_for_a_status(self):
        self.write("map.md", "# Map\n\n| System | Guarantee | Status |\n| --- | --- | --- |\n| A | short | Current |\n")
        manifest = {
            "types": {"index": {"max_cell_chars": 120, "max_row_chars": 240}},
            "documents": {"map.md": {
                "type": "index", "budget": 10000,
                "statuses": ["Current", "Partial", "Planned", "Deferred"],
            }},
        }
        findings = self.run_checks(manifest, ["map.md"])
        self.assertEqual([], self.checks(findings, "error"))

    def test_a_status_outside_the_four_is_refused(self):
        self.write("map.md", "# Map\n\n| System | Guarantee | Status |\n| --- | --- | --- |\n| A | short | Done |\n")
        manifest = {
            "types": {"index": {"max_cell_chars": 120, "max_row_chars": 240}},
            "documents": {"map.md": {
                "type": "index", "budget": 10000,
                "statuses": ["Current", "Partial", "Planned", "Deferred"],
            }},
        }
        findings = self.run_checks(manifest, ["map.md"])
        self.assertIn("shape/status-vocabulary", self.checks(findings))

    def test_a_pending_check_reports_as_a_warning_so_the_rule_lands_green(self):
        self.write("map.md", "# Map\n\n| System | Guarantee | Status |\n| --- | --- | --- |\n| A | short | Done |\n")
        manifest = {
            "pending_checks": ["shape/status-vocabulary"],
            "types": {"index": {"max_cell_chars": 120, "max_row_chars": 240}},
            "documents": {"map.md": {
                "type": "index", "budget": 10000,
                "statuses": ["Current", "Partial", "Planned", "Deferred"],
            }},
        }
        findings = self.run_checks(manifest, ["map.md"])
        self.assertEqual([], self.checks(findings, "error"))
        self.assertIn("shape/status-vocabulary", self.checks(findings, "warning"))


class ReferenceTests(CheckerHarness):
    MANIFEST = {
        "absent_by_design": ["Build/"],
        "types": {"state": {}},
        "documents": {"doc.md": {"type": "state", "budget": 100000}},
    }

    def test_a_line_number_citation_into_markdown_is_refused_anywhere(self):
        self.write("doc.md", "# T\n\nSee `ai/city-zones-art-bible.md:2712`.\n")
        findings = self.run_checks(self.MANIFEST, ["doc.md"])
        self.assertIn("ref/md-line-citation", self.checks(findings))

    def test_a_sibling_path_resolves_relative_to_the_document(self):
        self.write("notes/doc.md", "# T\n\nSee `Blender/model.blend`.\n")
        self.write("notes/Blender/model.blend", "x")
        manifest = {"types": {"note": {}}, "documents": {"notes/doc.md": {"type": "note", "budget": 10000}}}
        findings = self.run_checks(manifest, ["notes/doc.md"])
        self.assertNotIn("ref/dead-path", self.checks(findings))

    def test_a_numeric_ratio_is_not_mistaken_for_a_path(self):
        self.write("doc.md", "# T\n\nThe split is `0.44/0.56` and the run was `2353/2371`.\n")
        findings = self.run_checks(self.MANIFEST, ["doc.md"])
        self.assertNotIn("ref/dead-path", self.checks(findings))

    def test_build_output_is_absent_by_design(self):
        self.write("doc.md", "# T\n\nRun `Build/Windows/Game.exe`.\n")
        findings = self.run_checks(self.MANIFEST, ["doc.md"])
        self.assertNotIn("ref/dead-path", self.checks(findings))

    def test_a_genuinely_missing_path_is_reported(self):
        self.write("doc.md", "# T\n\nSee `Assets/Resources/Gone.png`.\n")
        findings = self.run_checks(self.MANIFEST, ["doc.md"])
        self.assertIn("ref/dead-path", self.checks(findings))

    def test_a_fenced_block_is_not_scanned(self):
        self.write("doc.md", "# T\n\n```\nSee `Assets/Resources/Gone.png` and doc.md:12\n```\n")
        findings = self.run_checks(self.MANIFEST, ["doc.md"])
        self.assertEqual([], self.checks(findings, "error"))


class CanonTests(CheckerHarness):
    BIBLE = (
        "# Bible\n\n"
        "## 1. First\n\n### Нельзя\n\n- never do this\n\n### Проверка\n\n- look at it\n\n"
        "## 2. Second\n\n### Квартира героя\n\n- a room\n"
    )

    def manifest(self, extra: dict | None = None) -> dict:
        base = {
            "types": {"canon": {}},
            "documents": {"bible.md": {"type": "canon", "budget": 100000}},
            "sections": {"bible.md": {"top": ["1", "2"], **(extra or {})}},
        }
        return base

    def test_a_changed_binding_block_is_refused(self):
        self.write("bible.md", self.BIBLE)
        doc = check_docs.load_document("bible.md")
        blocks = check_docs.hardline_blocks(doc)
        hashes = {key: check_docs.digest(doc, *span) for key, span in blocks.items()}
        self.write("bible.md", self.BIBLE.replace("- never do this", "- actually, do it"))
        findings = self.run_checks(self.manifest({"hardline_hashes": hashes}), ["bible.md"])
        self.assertIn("canon/hardline-changed", self.checks(findings))

    def test_an_unchanged_bible_passes(self):
        self.write("bible.md", self.BIBLE)
        doc = check_docs.load_document("bible.md")
        hashes = {k: check_docs.digest(doc, *s) for k, s in check_docs.hardline_blocks(doc).items()}
        findings = self.run_checks(self.manifest({"hardline_hashes": hashes}), ["bible.md"])
        self.assertEqual([], self.checks(findings, "error"))

    REGISTER = (
        "# Notes\n\n## Current facts\n\n"
        "- **Accepted — the first decision:** it stands.\n"
        "{nested}"
        "- **Accepted — the second decision:** it also stands.\n"
    )

    def register_manifest(self) -> dict:
        return {
            "types": {"canon": {}},
            "documents": {"notes.md": {
                "type": "canon", "budget": 100000, "block_max": 8000,
                "statuses": ["Accepted", "Proposed", "Superseded", "Corrected", "Current"],
            }},
        }

    def test_a_decision_written_inside_another_entry_is_refused(self):
        self.write("notes.md", self.REGISTER.format(
            nested="\n  **Accepted — a later decision:** this supersedes part of the above.\n\n"))
        findings = self.run_checks(self.register_manifest(), ["notes.md"])
        self.assertIn("canon/entry-thread", self.checks(findings, "error"))

    def test_separate_entries_are_accepted(self):
        self.write("notes.md", self.REGISTER.format(nested=""))
        findings = self.run_checks(self.register_manifest(), ["notes.md"])
        self.assertEqual([], self.checks(findings, "error"))

    def test_a_long_entry_is_only_an_outlier_warning(self):
        # Length is not the defect: these entries are dense measured contracts.
        self.write("notes.md", "# Notes\n\n## Current facts\n\n- **Accepted — big:** "
                   + "x" * 9000 + "\n")
        findings = self.run_checks(self.register_manifest(), ["notes.md"])
        self.assertEqual([], self.checks(findings, "error"))
        self.assertIn("canon/block-too-large", self.checks(findings, "warning"))

    def test_a_removed_section_is_refused(self):
        self.write("bible.md", "# Bible\n\n## 1. First\n\n- text\n")
        findings = self.run_checks(self.manifest(), ["bible.md"])
        self.assertIn("ref/frozen-section", self.checks(findings))

    def test_a_renamed_cited_subheading_is_refused(self):
        self.write("bible.md", self.BIBLE.replace("### Квартира героя", "### Жильё героя"))
        manifest = self.manifest({"cited_titles": {"2": ["Квартира героя"]}})
        findings = self.run_checks(manifest, ["bible.md"])
        self.assertIn("ref/frozen-title", self.checks(findings))

    def test_a_removed_registry_date_is_refused(self):
        self.write(
            "bible.md",
            "# Bible\n\n## 6. Registry\n\n| level | date |\n| --- | --- |\n| 0 | 2026-09-05 |\n",
        )
        manifest = {
            "types": {"canon": {}},
            "documents": {"bible.md": {"type": "canon", "budget": 100000}},
            "sections": {"bible.md": {"top": ["6"], "registry_dates": ["2026-09-05", "2026-09-06"]}},
        }
        findings = self.run_checks(manifest, ["bible.md"])
        self.assertIn("ref/frozen-registry-date", self.checks(findings))

    def test_a_citation_to_an_unknown_section_is_refused(self):
        self.write("bible.md", self.BIBLE)
        self.write("code.cs", "// governed by §77\n")
        findings = self.run_checks(self.manifest(), ["bible.md"], ["code.cs"])
        self.assertIn("ref/section-citation", self.checks(findings))

    def test_an_item_citation_resolves_through_its_parent_section(self):
        self.write("bible.md", self.BIBLE)
        self.write("code.cs", "// governed by §2.7\n")
        findings = self.run_checks(self.manifest(), ["bible.md"], ["code.cs"])
        self.assertNotIn("ref/section-citation", self.checks(findings))


class InventoryTests(CheckerHarness):
    def test_a_document_without_a_budget_row_is_refused(self):
        self.write("stray.md", "# Stray\n")
        findings = self.run_checks({"types": {}, "documents": {}}, ["stray.md"])
        self.assertIn("inventory/unknown-document", self.checks(findings))

    def test_a_budget_row_without_a_document_is_refused(self):
        manifest = {"types": {"state": {}}, "documents": {"gone.md": {"type": "state", "budget": 100}}}
        findings = self.run_checks(manifest, [])
        self.assertIn("inventory/missing-document", self.checks(findings))

    INDEX = (
        "# Index\n\n"
        "| File | Type | Budget | Purpose |\n"
        "| --- | --- | --- | --- |\n"
        "| `notes.md` | state | {budget} | Holds §16 and nine other things |\n"
    )

    def index_manifest(self, budget: int) -> dict:
        return {
            "types": {"entry": {}, "state": {}},
            "documents": {
                "ai/README.md": {"type": "entry", "budget": 100000},
                "ai/notes.md": {"type": "state", "budget": budget},
            },
        }

    def test_the_index_table_must_agree_with_the_manifest(self):
        self.write("ai/README.md", self.INDEX.format(budget=1234))
        self.write("ai/notes.md", "# Notes\n")
        findings = self.run_checks(self.index_manifest(9999), ["ai/README.md", "ai/notes.md"])
        self.assertIn("inventory/index-mismatch", self.checks(findings))

    def test_the_budget_column_is_found_by_its_header_not_by_position(self):
        # Prose in a later column carries digits (`§16`, `nine`); reading the wrong cell
        # is how this check first went wrong.
        self.write("ai/README.md", self.INDEX.format(budget=9999))
        self.write("ai/notes.md", "# Notes\n")
        findings = self.run_checks(self.index_manifest(9999), ["ai/README.md", "ai/notes.md"])
        self.assertNotIn("inventory/index-mismatch", self.checks(findings))


if __name__ == "__main__":
    unittest.main(verbosity=2)
