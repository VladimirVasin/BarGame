"""Documentation budget, shape and reference checker.

Every tracked Markdown document outside `ai/archive/` carries a type and a byte
budget in `tools/docs-budget.json`. This script enforces both, plus the shape
rules each type implies and the reference invariants that keep citations from
rotting silently. Standard library only; no Unity, Blender or network.

Run `python tools/check-docs.py` before committing a documentation change.
Exit code 0 means clean (warnings allowed), 1 means at least one error.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MANIFEST_PATH = ROOT / "tools" / "docs-budget.json"
ARCHIVE_PREFIX = "ai/archive/"

HEADING = re.compile(r"^(#{1,6}) +(.*?)\s*$")
DATED_HEADING = re.compile(r"^(#{2,3}) +(\d{4})-(\d{2})-(\d{2})\b")
FENCE = re.compile(r"^\s*(```|~~~)")
BACKTICKED = re.compile(r"`([^`\n]+)`")
MD_LINE_CITATION = re.compile(r"\b([\w./-]+\.md):(\d+)\b")
MD_LINK = re.compile(r"\[[^\]]*\]\(([^)\s]+)\)")
SECTION_CITATION = re.compile(r"(?<!§)§(\d+[a-z]?(?:\.\d+)?)\b")
SECTION_HEADING = re.compile(r"^## +(\d+[a-z]?(?:\.\d+)?)\. +(.*?)\s*$")
EPHEMERAL_PATH = re.compile(r"\b(TestResults|Captures)/[\w./-]+")
PASS_COUNT = re.compile(r"\b\d{1,4}\s*/\s*\d{1,4}\b")
DURATION = re.compile(r"\b\d+(?:[.,]\d+)?\s*(?:s|ms|min|сек|мин)\b")
RUN_WORD = re.compile(r"\b(test|тест|passed|passes|прош|suite|EditMode|PlayMode|assert)", re.I)
HARDLINE_HEADING = re.compile(r"^### +(Нельзя|Проверка)\s*$")
TOP_BULLET = re.compile(r"^- \*\*([A-Za-zА-Яа-я]+)")
TABLE_ROW = re.compile(r"^\s*\|(.+)\|\s*$")
NEGATION = re.compile(r"\b(Gap|Пробел|no |not |нет |без |отсутству|missing|empty|пуст)", re.I)

TYPES = ("entry", "index", "state", "canon", "ledger", "note")


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
class Document:
    path: str
    text: str
    raw: bytes
    lines: list[str] = field(default_factory=list)
    fenced: set[int] = field(default_factory=set)

    @property
    def size(self) -> int:
        return len(self.raw)


def tracked_markdown() -> list[str]:
    out = subprocess.run(
        ["git", "ls-files", "*.md"], cwd=ROOT, capture_output=True, text=True, encoding="utf-8"
    )
    if out.returncode != 0:
        sys.exit("git ls-files failed; run this from inside the repository")
    return sorted(p for p in out.stdout.splitlines() if p and not p.startswith(ARCHIVE_PREFIX))


def tracked_code(exclude: tuple[str, ...] = ()) -> list[str]:
    out = subprocess.run(
        ["git", "ls-files", "*.cs", "*.json", "*.py"],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8",
    )
    return sorted(p for p in out.stdout.splitlines() if p and p not in exclude)


def load_document(path: str) -> Document:
    # Budgets are measured on normalized content. Windows checkouts may hold CRLF even
    # where .gitattributes pins LF, and a budget that moves with the checkout is useless.
    raw = (ROOT / path).read_bytes().replace(b"\r\n", b"\n")
    text = raw.decode("utf-8")
    doc = Document(path=path, text=text, raw=raw, lines=text.splitlines())
    inside = False
    for index, line in enumerate(doc.lines, start=1):
        if FENCE.match(line):
            inside = not inside
            doc.fenced.add(index)
            continue
        if inside:
            doc.fenced.add(index)
    return doc


def entry_spans(doc: Document, level: int) -> list[tuple[int, int, str]]:
    """Return (start_line, end_line, iso_date) for each dated heading at `level`."""
    starts = [
        (index, f"{m.group(2)}-{m.group(3)}-{m.group(4)}")
        for index, line in enumerate(doc.lines, start=1)
        if (m := DATED_HEADING.match(line)) and len(m.group(1)) == level and index not in doc.fenced
    ]
    spans = []
    for position, (start, date) in enumerate(starts):
        end = starts[position + 1][0] - 1 if position + 1 < len(starts) else len(doc.lines)
        spans.append((start, end, date))
    return spans


def section_spans(doc: Document) -> dict[str, tuple[int, int, str]]:
    """Return {section id: (start, end, title)} for `## N. Title` headings."""
    marks = [
        (index, m.group(1), m.group(2))
        for index, line in enumerate(doc.lines, start=1)
        if (m := SECTION_HEADING.match(line)) and index not in doc.fenced
    ]
    spans: dict[str, tuple[int, int, str]] = {}
    for position, (start, ident, title) in enumerate(marks):
        end = marks[position + 1][0] - 1 if position + 1 < len(marks) else len(doc.lines)
        spans[ident] = (start, end, title)
    return spans


def hardline_blocks(doc: Document) -> dict[str, tuple[int, int]]:
    """Return {"<section>/<Нельзя|Проверка>": (start, end)} for binding blocks."""
    sections = section_spans(doc)
    blocks: dict[str, tuple[int, int]] = {}
    starts = [
        (index, m.group(1))
        for index, line in enumerate(doc.lines, start=1)
        if (m := HARDLINE_HEADING.match(line)) and index not in doc.fenced
    ]
    subheads = [
        index for index, line in enumerate(doc.lines, start=1)
        if line.startswith("### ") and index not in doc.fenced
    ]
    for start, kind in starts:
        following = [s for s in subheads if s > start]
        owner = next(
            (ident for ident, (a, b, _) in sections.items() if a <= start <= b), "?"
        )
        section_end = next((b for a, b, _ in sections.values() if a <= start <= b), len(doc.lines))
        end = min(following[0] - 1, section_end) if following else section_end
        key = f"{owner}/{kind}"
        suffix = 2
        while key in blocks:
            key = f"{owner}/{kind}#{suffix}"
            suffix += 1
        blocks[key] = (start, end)
    return blocks


def digest(doc: Document, start: int, end: int) -> str:
    body = "\n".join(doc.lines[start - 1 : end]).strip()
    return "sha256:" + hashlib.sha256(body.encode("utf-8")).hexdigest()[:32]


class Checker:
    def __init__(self, manifest: dict, strict: bool = False):
        self.manifest = manifest
        self.documents: dict[str, dict] = manifest.get("documents", {})
        self.sections: dict[str, dict] = manifest.get("sections", {})
        self.pending: set[str] = set(manifest.get("pending_checks", []))
        self.skip_prefixes: tuple[str, ...] = tuple(manifest.get("absent_by_design", []))
        self.scan_exclude: tuple[str, ...] = tuple(
            manifest.get("scan_exclude", ["tools/test_check_docs.py"])
        )
        self.strict = strict
        self.findings: list[Finding] = []
        self.cache: dict[str, Document] = {}

    # -- reporting ---------------------------------------------------------
    def error(self, path: str, line: int, check: str, message: str) -> None:
        # A check listed as pending states a debt the rewrite has not reached yet. It
        # reports as a warning so the rule is green the day it lands; a checker that is
        # red on arrival is one nobody can ever satisfy, which is how the previous
        # retention rule died. Commit D empties this list.
        severity = "warning" if check in self.pending else "error"
        self.findings.append(Finding(path, line, severity, check, message))

    def warn(self, path: str, line: int, check: str, message: str) -> None:
        self.findings.append(Finding(path, line, "warning", check, message))

    def doc(self, path: str) -> Document:
        if path not in self.cache:
            self.cache[path] = load_document(path)
        return self.cache[path]

    @staticmethod
    def is_table_scaffold(doc: Document, index: int) -> bool:
        """A header row (the next line is `|---|`) or the separator itself."""
        line = doc.lines[index - 1]
        if set(line) <= set("|- :\t"):
            return True
        following = doc.lines[index] if index < len(doc.lines) else ""
        return bool(TABLE_ROW.match(following)) and set(following) <= set("|- :\t")

    # -- A. inventory ------------------------------------------------------
    def check_inventory(self, present: list[str]) -> None:
        for path in present:
            if path not in self.documents:
                self.error(
                    path, 1, "inventory/unknown-document",
                    "no budget row in tools/docs-budget.json. Add one with a type from "
                    "{entry,index,state,canon,ledger,note}, or move the file under ai/archive/.",
                )
        for path in self.documents:
            if path not in present:
                self.error(
                    "tools/docs-budget.json", 0, "inventory/missing-document",
                    f"names {path}, which is not a tracked file. Remove the row or restore the file.",
                )

    def check_index_table(self) -> None:
        path = "ai/README.md"
        if path not in self.documents or not (ROOT / path).exists():
            return
        doc = self.doc(path)
        declared: dict[str, int] = {}
        column: int | None = None
        for line in doc.lines:
            row = TABLE_ROW.match(line)
            if not row:
                continue
            cells = [c.strip() for c in row.group(1).split("|")]
            # Find the Budget column by its header rather than by position, so adding a
            # column to the index cannot silently start comparing the wrong cell.
            if column is None:
                if "Budget" in cells:
                    column = cells.index("Budget")
                continue
            if len(cells) <= column:
                continue
            name = cells[0].strip("`")
            budget = cells[column]
            if name and budget.isdigit():
                declared[name] = int(budget)
        for name, budget in declared.items():
            target = name if "/" in name else f"ai/{name}"
            row = self.documents.get(target)
            if row and row["budget"] != budget:
                self.error(
                    path, 1, "inventory/index-mismatch",
                    f"the table shows budget {budget} for {name}; tools/docs-budget.json says "
                    f"{row['budget']}. Update the table.",
                )

    # -- B. size -----------------------------------------------------------
    REMEDY = {
        "ledger": "Archive whole dated entries from the bottom into ai/archive/ until the file is under the floor.",
        "state": "A change to reality is a rewrite of the affected section, not an addition. Delete what is no longer true.",
        "canon": "Delete the superseded text; git keeps it. Implementation detail belongs in ai/architecture-notes.md.",
        "index": "Move the detail to ai/architecture-notes.md; this file is an index.",
        "entry": "This file is read every session. Move the detail out and link to it.",
        "note": "Move the detail into the document that owns the subject.",
    }

    def check_size(self, path: str, row: dict) -> None:
        doc = self.doc(path)
        budget = row["budget"]
        if doc.size > budget:
            self.error(
                path, 1, "size/over-budget",
                f"{doc.size} B exceeds its {budget} B budget by {doc.size - budget} B. "
                + self.REMEDY.get(row["type"], ""),
            )
        elif doc.size >= budget * 0.98:
            # The budget is a ratchet set just above today's size, so most documents sit
            # high by construction. Only warn when an edit is about to break through.
            self.warn(
                path, 1, "size/near-budget",
                f"{doc.size} B is {100 * doc.size / budget:.0f}% of its {budget} B budget. "
                "The next addition has to be paid for with a removal.",
            )

    # -- C. ledger ---------------------------------------------------------
    def check_ledger(self, path: str, row: dict) -> None:
        doc = self.doc(path)
        level = 3 if row.get("entry_heading", "## ").strip() == "###" else 2
        spans = entry_spans(doc, level)
        floor = self.manifest.get("enforce_ledgers_from")
        cap = row.get("entry_max", 3000)

        pointer = row.get("archive_pointer")
        if pointer:
            if pointer.rsplit("/", 1)[-1] not in doc.text:
                self.error(
                    path, 1, "ledger/archive-pointer",
                    f"does not name its archive {pointer}. Keep the pointer at the top and bottom.",
                )
            elif not (ROOT / pointer).exists():
                self.error(
                    path, 1, "ledger/archive-pointer",
                    f"points at {pointer}, which does not exist. Create it or repoint.",
                )

        seen: dict[str, int] = {}
        previous: tuple[str, int] | None = None
        for start, end, date in spans:
            # Order is enforced over the whole file; the per-entry rules apply only to
            # entries written after the rule came in, so the scheme is green on arrival.
            enforced = not floor or date >= floor
            if date in seen:
                if enforced:
                    self.error(
                        path, start, "ledger/duplicate-date",
                        f"a second entry for {date} (first at line {seen[date]}). One entry per day: "
                        "fold this session's outcome into the existing entry.",
                    )
            else:
                seen[date] = start
            if previous and date > previous[0]:
                self.error(
                    path, start, "ledger/out-of-order",
                    f"{date} sits below {previous[0]} (line {previous[1]}). Entries are strictly "
                    "reverse chronological.",
                )
            previous = (date, start)

            if not enforced:
                continue
            body = "\n".join(doc.lines[start - 1 : end])
            size = len(body.encode("utf-8"))
            if size > cap:
                self.error(
                    path, start, "ledger/entry-too-large",
                    f"the {date} entry is {size} B, over the {cap} B cap. Say what changed, why it "
                    "was not obvious, and which check proved it; move the rest to "
                    "ai/architecture-notes.md or ai/current-world.md.",
                )
            for offset, line in enumerate(doc.lines[start - 1 : end], start=start):
                if offset in doc.fenced:
                    continue
                hit = EPHEMERAL_PATH.search(line)
                if hit:
                    self.error(
                        path, offset, "ledger/ephemeral-path",
                        f"cites {hit.group(0)}, which is gitignored and absent from any fresh "
                        "clone. Name the check, not its output.",
                    )
                if RUN_WORD.search(line) and (PASS_COUNT.search(line) or DURATION.search(line)):
                    self.warn(
                        path, offset, "ledger/run-metrics",
                        "records a one-off run count or duration. It is meaningless a week later; "
                        "name the check instead.",
                    )

    # -- D. shape ----------------------------------------------------------
    def check_shape(self, path: str, row: dict) -> None:
        doc = self.doc(path)
        kind = row["type"]
        limits = self.manifest["types"].get(kind, {})

        max_section = limits.get("max_section_lines")
        if max_section:
            marks = [
                index for index, line in enumerate(doc.lines, start=1)
                if line.startswith("## ") and index not in doc.fenced
            ]
            for position, start in enumerate(marks):
                end = marks[position + 1] - 1 if position + 1 < len(marks) else len(doc.lines)
                if end - start > max_section and not any(
                    doc.lines[i - 1].startswith("### ") for i in range(start, end + 1)
                ):
                    title = HEADING.match(doc.lines[start - 1]).group(2)
                    self.warn(
                        path, start, "shape/no-subheadings",
                        f'section "{title}" runs {end - start} lines with no ### subheading; '
                        '"read the relevant section" is not actionable. Split it.',
                    )

        allowed = row.get("statuses")
        if allowed:
            vocabulary = set(allowed)
            if kind == "canon":
                for index, line in enumerate(doc.lines, start=1):
                    if index in doc.fenced:
                        continue
                    m = TOP_BULLET.match(line)
                    if m and m.group(1) not in vocabulary:
                        self.error(
                            path, index, "shape/status-vocabulary",
                            f"decision bullet starts with '{m.group(1)}', which is outside "
                            f"{sorted(vocabulary)}. Use one of them.",
                        )
            else:
                for index, line in enumerate(doc.lines, start=1):
                    row_match = TABLE_ROW.match(line)
                    if not row_match or index in doc.fenced or self.is_table_scaffold(doc, index):
                        continue
                    cells = [c.strip() for c in row_match.group(1).split("|")]
                    status = cells[-1] if cells else ""
                    if status and status not in vocabulary and re.fullmatch(r"[A-Za-z]+", status):
                        self.error(
                            path, index, "shape/status-vocabulary",
                            f"status '{status}' is outside {sorted(vocabulary)}. "
                            "Use one of the four defined in ai/README.md.",
                        )
                    if status == "Partial" and not NEGATION.search("|".join(cells[:-1])):
                        self.warn(
                            path, index, "shape/partial-without-gap",
                            "a Partial row must name its gap; none of its cells state one. "
                            "This check reads loosely, so confirm by eye.",
                        )

        if kind == "index":
            # The rule in AGENTS.md is per CELL: a table row renders as tall as its widest
            # cell, so a row of three short cells is fine however long the line is.
            cell_max = limits.get("max_cell_chars", 120)
            for index, line in enumerate(doc.lines, start=1):
                row_match = TABLE_ROW.match(line)
                if not row_match or index in doc.fenced or set(line) <= set("|- :\t"):
                    continue
                cells = [c.strip() for c in row_match.group(1).split("|")]
                widest = max((len(c) for c in cells), default=0)
                if widest > cell_max:
                    self.error(
                        path, index, "index/cell-too-wide",
                        f"a cell is {widest} chars; AGENTS.md requires one or two rendered lines "
                        f"(<={cell_max}). Move the detail to ai/architecture-notes.md.",
                    )

        if kind == "canon":
            # A register entry is ONE decision. The real failure mode is a thread: a
            # later decision written inside an earlier one, so that a reader cannot tell
            # which clauses still stand. Length is not the defect — the entries here are
            # dense measured contracts, only 7% of them narration — so size is a warning
            # for outliers and the thread is the error.
            statuses = row.get("statuses")
            if statuses:
                nested = re.compile(r"^ +\*\*(" + "|".join(map(re.escape, statuses)) + r")\b")
                for index, line in enumerate(doc.lines, start=1):
                    if index in doc.fenced or not nested.match(line):
                        continue
                    self.error(
                        path, index, "canon/entry-thread",
                        "a second decision is written inside another entry, so a reader cannot "
                        "tell which of its clauses still stand. Promote it to its own `- **` "
                        "entry and delete whatever it supersedes.",
                    )

            block_max = row.get("block_max")
            if block_max:
                marks = [
                    index for index, line in enumerate(doc.lines, start=1)
                    if line.startswith("- **") and index not in doc.fenced
                ]
                first_heading = next(
                    (i for i, line in enumerate(doc.lines, start=1) if line.startswith("## ")), None
                )
                if first_heading and marks and marks[0] < first_heading:
                    self.error(
                        path, marks[0], "canon/preamble-order",
                        "a decision bullet sits above the document's own preamble and first "
                        "heading. New decisions go under the first section.",
                    )
                for position, start in enumerate(marks):
                    end = marks[position + 1] - 1 if position + 1 < len(marks) else len(doc.lines)
                    size = len("\n".join(doc.lines[start - 1 : end]).encode("utf-8"))
                    if size > block_max:
                        self.warn(
                            path, start, "canon/block-too-large",
                            f"the decision entry is {size} B, past the {block_max} B outlier mark. "
                            "Check that it is one decision and not several; a genuinely dense "
                            "measured contract may stay.",
                        )

    # -- E. references -----------------------------------------------------
    def check_references(self, path: str) -> None:
        doc = self.doc(path)
        directory = Path(path).parent
        # A ledger records what was true on its date, and a retired note records a
        # pipeline that has been withdrawn. Both are history, not live pointers.
        row = self.documents.get(path, {})
        historical = row.get("type") == "ledger" or row.get("retired", False)
        for index, line in enumerate(doc.lines, start=1):
            if index in doc.fenced:
                continue
            for target in MD_LINK.findall(line):
                if target.startswith(("http://", "https://", "#", "mailto:")):
                    continue
                resolved = (ROOT / directory / target.split("#", 1)[0]).resolve()
                if not resolved.exists():
                    self.error(
                        path, index, "ref/dead-link",
                        f"links to {target}, which does not exist. Fix or remove the link.",
                    )
            if not historical:
                for token in BACKTICKED.findall(line):
                    self.check_path_token(path, index, token)

    def check_path_token(self, path: str, index: int, token: str) -> None:
        token = token.strip()
        if "/" not in token or any(ch in token for ch in "{}*<>?|"):
            return
        # Build output and runtime-resolved roots are absent by design, not by rot.
        if any(token.startswith(prefix) for prefix in self.skip_prefixes):
            return
        if token.startswith(("http", "-", "§")) or " " in token or token.endswith("/"):
            return
        if not re.fullmatch(r"[\w./§-]+", token):
            return
        # A path has letters and a real extension. `0.44/0.56` and `2353/2371` are ratios.
        if not re.search(r"[A-Za-z]", token):
            return
        suffix = Path(token).suffix
        if not re.fullmatch(r"\.[A-Za-z][A-Za-z0-9]{0,4}", suffix):
            return
        # A note names its siblings relative to itself; a map names them from the root.
        candidates = [ROOT / token, ROOT / Path(path).parent / token]
        if token.startswith("Resources/"):
            candidates.append(ROOT / "Assets" / token)
        if any(c.exists() for c in candidates):
            return
        self.error(
            path, index, "ref/dead-path",
            f"names `{token}`, which does not exist. Update the path or drop the reference.",
        )

    def check_line_citations(self, paths: list[str]) -> None:
        for path in paths:
            # A ledger entry records what was true on its date; it is history, not a live
            # pointer, so its line citations are left alone.
            if self.documents.get(path, {}).get("type") == "ledger":
                continue
            raw = (ROOT / path).read_bytes()
            if b".md:" not in raw:
                continue
            try:
                text = raw.decode("utf-8")
            except UnicodeDecodeError:
                continue
            fenced = self.doc(path).fenced if path.endswith(".md") else set()
            for index, line in enumerate(text.splitlines(), start=1):
                if index in fenced:
                    continue
                for hit in MD_LINE_CITATION.finditer(line):
                    self.error(
                        path, index, "ref/md-line-citation",
                        f"cites {hit.group(0)} by line number. Line numbers rot silently — cite the "
                        "section (§N) or quote the sentence.",
                    )

    def check_section_citations(self, paths: list[str]) -> None:
        known: set[str] = set()
        for bible in self.sections:
            known.update(self.sections[bible].get("top", []))
            for parent, bounds in self.sections[bible].get("items", {}).items():
                known.update(f"{parent}.{n}" for n in range(bounds[0], bounds[1] + 1))
        if not known:
            return

        def resolves(ident: str) -> bool:
            # A citation is deliberately checked against the union of both bibles: the
            # corpus genuinely does not name which one it means. An item number resolves
            # through its parent section unless an explicit item range is recorded.
            if ident in known:
                return True
            parent = ident.split(".", 1)[0]
            return "." in ident and parent in known and not any(
                parent in self.sections[b].get("items", {}) for b in self.sections
            )
        for path in paths:
            raw = (ROOT / path).read_bytes()
            if b"\xc2\xa7" not in raw:
                continue
            try:
                text = raw.decode("utf-8")
            except UnicodeDecodeError:
                continue
            for index, line in enumerate(text.splitlines(), start=1):
                for hit in SECTION_CITATION.finditer(line):
                    if not resolves(hit.group(1)):
                        self.error(
                            path, index, "ref/section-citation",
                            f"cites §{hit.group(1)}, which is not a section of either bible. "
                            "Check the number against ai/city-story-bible.md or "
                            "ai/city-zones-art-bible.md.",
                        )

    def check_frozen_canon(self) -> None:
        for path, frozen in self.sections.items():
            if not (ROOT / path).exists():
                continue
            doc = self.doc(path)
            spans = section_spans(doc)
            present = list(spans)
            expected = frozen.get("top", [])
            for ident in expected:
                if ident not in spans:
                    self.error(
                        path, 1, "ref/frozen-section",
                        f"§{ident} is gone. Section numbers are cited from code and cannot be "
                        "removed or renumbered; restore it or edit tools/docs-budget.json by hand.",
                    )
            for ident in present:
                if expected and ident not in expected:
                    self.error(
                        path, spans[ident][0], "ref/frozen-section",
                        f"§{ident} is new. Run --accept-new-sections once you are sure it does not "
                        "renumber an existing one.",
                    )
            for ident, titles in frozen.get("cited_titles", {}).items():
                if ident not in spans:
                    continue
                start, end, _ = spans[ident]
                body = "\n".join(doc.lines[start - 1 : end])
                for title in titles:
                    if title not in body:
                        self.error(
                            path, start, "ref/frozen-title",
                            f"§{ident} no longer contains «{title}», which is cited by title. "
                            "Restore the heading text.",
                        )
            dates = frozen.get("registry_dates")
            if dates:
                found = set(re.findall(r"\b(2\d{3}-\d{2}-\d{2})\b", "\n".join(
                    doc.lines[spans["6"][0] - 1 : spans["6"][1]]
                ))) if "6" in spans else set()
                for date in dates:
                    if date not in found:
                        self.error(
                            path, spans.get("6", (1,))[0], "ref/frozen-registry-date",
                            f"the §6 registry no longer carries {date}, which code cites by date. "
                            "Restore the row.",
                        )
            for key, expected_hash in frozen.get("hardline_hashes", {}).items():
                blocks = hardline_blocks(doc)
                if key not in blocks:
                    self.error(
                        path, 1, "canon/hardline-changed",
                        f"the binding block {key} is gone. Nельзя/Проверка blocks are the enforceable "
                        "core; restore it or accept the change explicitly.",
                    )
                    continue
                start, end = blocks[key]
                actual = digest(doc, start, end)
                if actual != expected_hash:
                    self.error(
                        path, start, "canon/hardline-changed",
                        f"the binding block {key} changed. If that is intended, run "
                        f'--accept-canon-hash "{path}#{key}"; otherwise restore it.',
                    )

    # -- driver ------------------------------------------------------------
    def run(self) -> int:
        present = tracked_markdown()
        self.check_inventory(present)
        self.check_index_table()
        for path in present:
            row = self.documents.get(path)
            if not row:
                continue
            self.check_size(path, row)
            self.check_shape(path, row)
            self.check_references(path)
            if row["type"] == "ledger":
                self.check_ledger(path, row)
        self.check_frozen_canon()
        # The checker's own tests carry deliberately bad examples of everything below.
        scan = present + tracked_code(exclude=self.scan_exclude)
        self.check_line_citations(scan)
        self.check_section_citations(scan)
        return self.report()

    def report(self) -> int:
        errors = [f for f in self.findings if f.severity == "error"]
        deferred = [f for f in self.findings if f.check in self.pending]
        warnings = [f for f in self.findings if f.severity == "warning" and f.check not in self.pending]
        for finding in sorted(self.findings, key=lambda f: (f.path, f.line)):
            print(finding.render())
        summary = f"\n{len(errors)} error(s), {len(warnings)} warning(s)"
        if deferred:
            counts = sorted({f.check for f in deferred})
            summary += f", {len(deferred)} pending ({', '.join(counts)})"
        print(summary + f" across {len(self.documents)} documents")
        if errors:
            return 1
        return 1 if (self.strict and warnings) else 0


def seed(manifest: dict, headroom: float) -> dict:
    """Record the current tree as the baseline: budgets, sections, hashes."""
    manifest.setdefault("version", 1)
    manifest.setdefault("types", {
        "ledger": {}, "canon": {}, "state": {"max_section_lines": 400},
        "index": {"max_cell_chars": 120, "max_row_chars": 240}, "entry": {}, "note": {},
    })
    documents = manifest.setdefault("documents", {})
    for path in tracked_markdown():
        size = load_document(path).size
        row = documents.setdefault(path, {"type": "note"})
        # A ratchet needs enough slack that an ordinary edit does not trip it on day one.
        allowance = max(size * headroom, size + 4000)
        row["budget"] = max(int(allowance + 999) // 1000 * 1000, 8000)
    for path, frozen in manifest.get("sections", {}).items():
        doc = load_document(path)
        spans = section_spans(doc)
        frozen["top"] = list(spans)
        frozen["hardline_hashes"] = {
            key: digest(doc, start, end) for key, (start, end) in hardline_blocks(doc).items()
        }
        if "6" in spans and frozen.get("registry_dates") is None:
            body = "\n".join(doc.lines[spans["6"][0] - 1 : spans["6"][1]])
            frozen["registry_dates"] = sorted(set(re.findall(r"\b(2\d{3}-\d{2}-\d{2})\b", body)))
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--warnings-as-errors", action="store_true")
    parser.add_argument("--seed", action="store_true", help="record the current tree as the baseline")
    parser.add_argument("--headroom", type=float, default=1.05)
    parser.add_argument("--accept-new-sections", action="store_true")
    parser.add_argument("--accept-canon-hash", metavar="PATH#KEY")
    parser.add_argument("--ratchet", action="store_true", help="print tighter budgets; never writes")
    args = parser.parse_args()

    manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8")) if MANIFEST_PATH.exists() else {}

    if args.seed:
        manifest = seed(manifest, args.headroom)
        MANIFEST_PATH.write_text(
            json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n"
        )
        print(f"seeded {len(manifest['documents'])} documents into {MANIFEST_PATH.name}")
        return 0

    if args.accept_new_sections:
        for path, frozen in manifest.get("sections", {}).items():
            frozen["top"] = list(section_spans(load_document(path)))
        MANIFEST_PATH.write_text(
            json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n"
        )
        print("recorded the current section numbering")
        return 0

    if args.accept_canon_hash:
        path, _, key = args.accept_canon_hash.partition("#")
        frozen = manifest.get("sections", {}).get(path)
        if not frozen:
            sys.exit(f"{path} is not a frozen canon document")
        blocks = hardline_blocks(load_document(path))
        if key not in blocks:
            sys.exit(f"{key} is not a binding block of {path}")
        start, end = blocks[key]
        frozen.setdefault("hardline_hashes", {})[key] = digest(load_document(path), start, end)
        MANIFEST_PATH.write_text(
            json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n"
        )
        print(f"accepted {key} in {path}")
        return 0

    if args.ratchet:
        for path, row in sorted(manifest.get("documents", {}).items()):
            if not (ROOT / path).exists():
                continue
            size = load_document(path).size
            if size < row["budget"] * 0.75:
                suggested = max(int(size * 1.15 + 999) // 1000 * 1000, 4000)
                print(f"{path}: {size} B against {row['budget']} B — suggest {suggested} B")
        return 0

    return Checker(manifest, strict=args.warnings_as_errors).run()


if __name__ == "__main__":
    sys.exit(main())
