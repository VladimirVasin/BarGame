# AI project entry point

Read this file first, then use [`ai/README.md`](ai/README.md) as the documentation index.

## Reality check

The Unity 6 URP vertical slice is implemented. It generates a finite connected
`12 x 12`-block city with four urban districts, a traversable central park and
four graph-separated bars, places one visually distinct player home beside a
bar street, creates an atlas-backed eight-direction jointed sprite player,
loads separate bar and home interiors, and restores the same seed and matching
exterior return point.

The build starts in `MainMenu`, resets a fresh session and opens the existing
Home interior in a one-shot sleeping presentation. Its first Home frame holds
on a silent `05:59` alarm clock whose complete display flickers briefly at
long intervals. For five seconds there is no menu input; then the localized
PS1-style `WAKE UP`/`QUIT` menu appears while the clock stays silent and keeps
showing and flickering `05:59`. Only Wake Up switches it to solid `06:00` and
starts the alarm. The clock shot and sleeping loop hold for three more
unscaled seconds; when the alarm stops, the continuous six-second camera and
wake animation begin and settle into the normal Home shot. Ordinary later bed
wakes retain their two-second timing.

Startup truth begins at `Assets/Scripts/Runtime/Scenes/MainMenuRoot.cs` and
`Assets/Scripts/Runtime/Scenes/HomeOpeningController.cs`; generated-city truth
continues from `Assets/Scripts/Runtime/Core/CityGameRoot.cs` and
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
- All future contextual player sprite/atlas interactions must follow the
  mandatory `ai/contextual-animation-standard.md`; do not add one-off teleport
  or sprite-fade handoffs.
- Update the maps and work log when implementation changes project reality.
- Keep documentation concise and mark uncertainty directly.
