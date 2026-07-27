# AI project entry point

Read this file first, then use [`ai/README.md`](ai/README.md) as the documentation index.

## Reality check

The Unity 6 URP vertical slice is implemented. It generates a finite connected
city, creates a modular sprite player, supports reachable bar entrances, loads
the shared bar interior, and restores the same seed and bar return point.

The source of truth starts at `Assets/Scripts/Runtime/Core/CityGameRoot.cs` and
`Assets/Scripts/Runtime/World/CityLayoutGenerator.cs`.

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
