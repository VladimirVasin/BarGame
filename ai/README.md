# AI memory index

Start with [`AI.md`](../AI.md), then the overview, system index and relevant
details. AI.md owns source order; entry documents do not catalogue the world.

Document **types** and **byte budgets** come from
[`tools/docs-budget.json`](../tools/docs-budget.json).
`python tools/check-docs.py` enforces them and checks this table; see Retention.

| File | Type | Budget | Purpose |
| --- | --- | --- | --- |
| `prompt-templates.md` | entry | 6000 | Canonical FAST-default, FEATURE, RELEASE, PLAN, BUGFIX, and REFACTOR workflows |
| `project-overview.md` | entry | 9000 | Concise current baseline, technical owners, capability and scope links |
| `current-world.md` | state | 208000 | Detailed current gameplay/MVP catalogue and deferred scope |
| `project-audit-2026-10-28.md` | note | 24000 | Audit findings and focused evidence |
| `audit-remediation-plan.md` | note | 16000 | Twelve accepted repair packages and measured outcomes |
| `world-location-update-plan.md` | note | 30000 | Ordered audit/update checklist for every scene and location; effects and focused acceptance |
| `village-life-plan.md` | state | 12000 | The accepted four-part village household update; all four parts are implemented |
| `village-narrative-plan.md` | note | 40000 | Place-based inspections, families, reusable object camera/text; acceptance contract |
| `city-lighting-plan.md` | note | 8000 | Nine City lighting stages and acceptance |
| `city-texture-plan.md` | note | 8000 | Nine City material stages and acceptance |
| `bar-dialogue-plan.md` | note | 64000 | Bar speech contract, paired conversations and Russian phrase bank |
| `karaoke-plan.md` | note | 6000 | Free live voice, hall attention, camera shots, drunken pitch drift and focused acceptance |
| `systems-map.md` | index | 44000 | System index: guarantee, key files and status |
| `architecture-notes.md` | canon | 570000 | Accepted technical decisions and the exceptions the bibles allow |
| `debug-log.md` | state | 8000 | Structured diagnostics format, events and support workflow |
| `player-art-spec.md` | canon | 32000 | Locked player design for the 3D production hero; the 2D atlas contract it once held is retired |
| `contextual-animation-standard.md` | canon | 8000 | Mandatory entry/exit, hard-handoff, authoring and test contract for contextual interactions on the 3D hero rig |
| `speech-presentation-standard.md` | canon | 4000 | Mandatory shared bubbles for all speech, including E replies; ownership, ordering, pause and cleanup |
| `item-interaction-standard.md` | canon | 6000 | Mandatory keyed prompts, shared 3D pickup/receipt screen, single credit, input and cleanup |
| `visual-effects-standard.md` | canon | 6000 | Every scene/visual change: three groups incl. post-processing |
| `bartender-spec.md` | canon | 13000 | Default NPC bartender, shared service rig and contacts |
| `city-zones-art-bible.md` | canon | 473000 | Binding form/material/light/sound; §16 holds nine acceptance checks |
| `city-story-bible.md` | canon | 305000 | Binding meaning; §6 exception registry, §16 laws, §21 text register |
| `work-log.md` | ledger | 563000 | Reverse-chronological implementation record, one entry per date |
| `tutorial-scenario.md` | state | 5000 | Vertical-slice walkthrough; covers three of the nine gameplay roots |
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

Retention follows byte budgets rather than a calendar window;
`python tools/check-docs.py` enforces it.

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
  **One entry is one decision.** Writing a later decision inside an earlier
  entry is an error: it produces a thread in which a reader cannot tell which
  clauses still stand — one entry had grown to eleven decisions and 16 KB that
  way. Promote it to its own entry and delete whatever it supersedes.
  Entry *length* is only a warning, and deliberately so: these entries are
  dense measured contracts other code depends on, and a measurement found just
  7% of the long ones to be narration. There is nothing to squeeze, so the file
  budget is what bounds the register, not a per-entry cap.
- **`index`** — `systems-map.md`. One row per system, cells to one or two
  rendered lines, only the four statuses above.
- **`entry`** — `AI.md`, `AGENTS.md`, this file, `project-overview.md`,
  `prompt-templates.md`. Read every session, so small by definition.

A document that only grows has the wrong type or the wrong budget. Fix that,
rather than raising the number.

## Workspace retention

Everything gitignored is declared in
[`tools/workspace-budget.json`](../tools/workspace-budget.json) and enforced by
`python tools/check-workspace.py`. A path is `never`, a `retain` window in days,
an `idle` window for a regenerable cache, `declared`, or `allowed`, and an
ignored path with no row is an error. What used to be advice here — do not clean
an active series, check the running processes first — is now the sweep's own
precondition, and it refuses on it.

`Captures/` is temporary (`never`): generate what the current task needs, review
it, then delete all frames, videos, reports and staging at task completion.
Recapture when needed; do not keep evidence sets or cite them as durable
references. Reusable tools belong in `tools/`; record named checks in the log.

`TestResults/` and `Art/` remain `declared`; document references protect them
from the sweep. Art contains authored, non-reproducible assets.
