# AI project entry point

Read this file first, then use [`ai/README.md`](ai/README.md) as the documentation index.

## Reality check

The Unity 6 URP vertical slice is implemented. It generates a finite connected
`12 x 12`-block city with four urban districts, a traversable central park and
four graph-separated bars, places one visually distinct player home beside a
bar street, creates an atlas-backed eight-direction jointed sprite player,
loads separate bar and home interiors, and restores the same seed and matching
exterior return point.

The source of truth starts at `Assets/Scripts/Runtime/Core/CityGameRoot.cs` and
`Assets/Scripts/Runtime/World/CityLayoutGenerator.cs`.

Runtime support diagnostics are written as bounded NDJSON through
`Assets/Scripts/Runtime/Diagnostics/`; see `ai/debug-log.md` for profiles,
paths and event boundaries.

## Source-of-truth order

1. Files currently present in the repository.
2. Unity project and package settings.
3. `ai/architecture-notes.md` for accepted decisions.
4. Planning documents for intended work.

Never report a planned system as implemented without verifying the code, scenes, and tests.

## Working agreement

- Use the canonical workflow matching the task in `ai/prompt-templates.md`.
- Start from `ai/project-overview.md` and `ai/systems-map.md`.
- Update the maps and work log when implementation changes project reality.
- Keep documentation concise and mark uncertainty directly.
