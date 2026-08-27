# AI memory index

This directory is the concise, versioned memory for Bar Promenade.
Read [`AI.md`](../AI.md) first; it is the entry point and states the
source-of-truth order.

| File | Purpose |
| --- | --- |
| `prompt-templates.md` | Canonical FAST-default, FEATURE, RELEASE, PLAN, BUGFIX, and REFACTOR workflows |
| `project-overview.md` | Product intent, current baseline, MVP scope |
| `system-tree.md` | Current and target repository/system structure |
| `systems-map.md` | System index: guarantee, key files and status |
| `architecture-notes.md` | Accepted and proposed technical decisions |
| `debug-log.md` | Structured diagnostics format, events and support workflow |
| `player-art-spec.md` | Locked player design and layered puppet-atlas contract |
| `contextual-animation-standard.md` | Mandatory entry/exit, hard-handoff, authoring and test contract for contextual player atlas animations |
| `city-zones-art-bible.md` | Current zone facts and locked target visual, spatial, emotional, light and sound identity |
| `city-story-bible.md` | Planned. Locked story canon: the crime, the hero, the poisoning scale, the Cat, what every built place means, and the register every written line must keep |
| `work-log.md` | Reverse-chronological implementation record |
| `tutorial-scenario.md` | Planned vertical-slice walkthrough and acceptance path |
| `release-notes.md` | Player-visible milestone notes |
| `archive/` | Retired work-log and release-note entries, retained verbatim |

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

`work-log.md` and `release-notes.md` grow monotonically and are the two files
most likely to crowd out useful context.

- Keep the current month and the previous full month in the active file.
- Move anything older into `archive/<file>-<YYYY-MM>.md`, verbatim, preserving
  reverse-chronological order.
- Leave a pointer to the archive at the top and bottom of the active file.
- Archive on the first working session of a month; never rewrite or summarize
  an archived entry.

Other files under `ai/` are living documents: correct them in place rather than
appending. If a document only grows, it needs a retention rule too.
