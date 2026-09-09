# AI memory index

This directory is the concise, versioned memory for Bar Promenade.
Read [`AI.md`](../AI.md) first; it is the entry point and states the
source-of-truth order. Read the overview and system index next, then only the
relevant sections of the detailed catalogue and architecture notes. Avoid
copying world descriptions back into the entry documents.

Every document below has a **type** and a **byte budget**, both owned by
[`tools/docs-budget.json`](../tools/docs-budget.json) and enforced by
`python tools/check-docs.py`. The type says how you write to the document; see
Retention below. The budget column here must match the manifest — the checker
compares them, so this index cannot quietly drift out of date again.

| File | Type | Budget | Purpose |
| --- | --- | --- | --- |
| `prompt-templates.md` | entry | 10000 | Canonical FAST-default, FEATURE, RELEASE, PLAN, BUGFIX, and REFACTOR workflows |
| `project-overview.md` | entry | 12000 | Concise current baseline, technical owners, capability and scope links |
| `current-world.md` | state | 208000 | Detailed current gameplay/MVP catalogue and deferred scope |
| `village-life-plan.md` | state | 14000 | The accepted four-part village household update; all four parts are implemented |
| `system-tree.md` | state | 190000 | Hand-maintained repository path reference. Being retired: `git ls-files` and `systems-map.md` do the job |
| `systems-map.md` | index | 44000 | System index: guarantee, key files and status |
| `architecture-notes.md` | canon | 612000 | Accepted technical decisions and the exceptions the bibles allow |
| `debug-log.md` | state | 11000 | Structured diagnostics format, events and support workflow |
| `player-art-spec.md` | canon | 32000 | Locked player design for the 3D production hero; the 2D atlas contract it once held is retired |
| `contextual-animation-standard.md` | canon | 11000 | Mandatory entry/exit, hard-handoff, authoring and test contract for contextual interactions on the 3D hero rig |
| `bartender-spec.md` | canon | 16000 | The active two-armed bar worker; the six-armed design is quarantined in its appendix |
| `city-zones-art-bible.md` | canon | 473000 | Locked target visual, spatial, emotional, light and sound identity. Binding: §16 holds nine acceptance checks |
| `city-story-bible.md` | canon | 305000 | Binding story canon: the crime, the hero, the poisoning scale, the Cat, what every built place means, and the register every written line must keep. §16 is hard, §6 is the dated registry, §21 governs text |
| `work-log.md` | ledger | 563000 | Reverse-chronological implementation record, one entry per date |
| `tutorial-scenario.md` | state | 8000 | Vertical-slice walkthrough; covers three of the nine gameplay roots |
| `release-notes.md` | ledger | 211000 | Player-visible milestone notes, one entry per date |
| `archive/` | — | — | Retired logs and explicitly superseded document snapshots, retained verbatim. Not budgeted and not checked |

## Status terms

These four terms are the only statuses used anywhere under `ai/`.

- `Current`: verified in the repository.
- `Partial`: implemented, but a named part of that item's own intent is
  missing; the gap must be stated where the status appears.
- `Planned`: intended but not implemented.
- `Deferred`: explicitly outside the present milestone.

`systems-map.md` restates this table because it is the heaviest user of it.
Do not introduce a fifth term.

## Retention

The previous rule here kept "the current and the previous full month" in the two
ledgers. It fired exactly once — in the commit that introduced it — and at the
rate this project writes, a correct pass would have left `work-log.md` **larger**
than before it ran. A monthly window is the wrong instrument: it is off by a
factor of about twelve, and it covered only two of the eighteen documents. What
follows replaces it, and `python tools/check-docs.py` enforces it.

**Everything is bounded by bytes, and the type says how you write.**

- **`ledger`** — `work-log.md`, `release-notes.md`. Append-only, newest first.
  **One entry per date.** A second session on the same day extends that day's
  entry rather than adding another; a later correction rewrites the day it
  corrects. An entry states what changed, why it was not obvious, and the name
  of the check that proved it — not durations, pass counts, sample sizes, paths
  under `TestResults/` or `Captures/`, or which suites were skipped. Caps:
  `3000 B` per work-log entry, `2000 B` per release note.
  Archiving is triggered by **size, not the calendar**: once the file passes its
  budget, move whole dates — never split a date — into
  `archive/<file>-<YYYY-MM>.md`, verbatim, until the file is back under its
  floor. Keep the archive pointer at the top and bottom. Never rewrite or
  summarize an archived entry.
- **`state`** — `current-world.md`, `README.md`, `tools/README.md`,
  `debug-log.md`, `tutorial-scenario.md`, `village-life-plan.md`. These describe
  the world **as it is now**. A change to reality is a rewrite of the affected
  paragraph, never a paragraph placed beside it. The budget is what makes this
  real: to add, you must delete.
- **`canon`** — both bibles, `architecture-notes.md`, `player-art-spec.md`,
  `bartender-spec.md`, `contextual-animation-standard.md`. Grows only by
  decision. **Superseded text is deleted, not marked** — git holds the history,
  and a decision kept beside the one that replaced it is how one fact came to
  have four different values at once. Section numbers, cited `###` titles and
  the story bible's §6 registry dates are frozen: code cites them, so the
  checker refuses to let them move.
- **`index`** — `systems-map.md`. One row per system, cells to one or two
  rendered lines, only the four statuses above.
- **`entry`** — `AI.md`, `AGENTS.md`, this file, `project-overview.md`,
  `prompt-templates.md`. Read every session, so small by definition.

A document that only grows has the wrong type or the wrong budget. Fix that,
rather than raising the number.

## Capture retention

- Keep the latest complete verified capture set per subject (area, feature or
  motion sequence), with its final report, finished video and referenced audio.
  A newer partial run does not replace a complete set.
- Discard superseded attempts, duplicate copies and build/cache output. Raw
  motion frames may be removed once the finished video has been verified;
  retain any distinct final stills needed to inspect the subject.
- Reusable capture and analysis tools belong in `tools/`. Keep isolated Unity
  projects outside `Captures/` and remove those temporary copies after their
  validated assets and final captures have been published.
- Never clean an active capture series or staging directory. Check its owner
  and running processes first; update current document links when final
  captures are consolidated, leaving archived records unchanged.
