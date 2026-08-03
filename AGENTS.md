# Repository instructions

These instructions apply to the entire repository.

## Before changing the project

1. Read `AI.md` and the relevant files under `ai/`.
2. Inspect the actual project before trusting planned documentation.
3. Keep current facts separate from proposed architecture.

## Host power state

- Never put the user's PC into sleep mode unless the user explicitly requests
  sleep for that specific run. Do not infer permission and do not treat an
  earlier request as a standing preference.

## Current baseline

- Unity `6000.5.5f1`, Universal Render Pipeline `17.5.0`.
- The playable MVP is implemented through runtime composition.
- Build scenes are `Assets/Scenes/City.unity`,
  `Assets/Scenes/DoorTransition.unity`,
  `Assets/Scenes/BarInterior.unity`, and
  `Assets/Scenes/HomeInterior.unity`.
- `Assets/Scripts/Runtime/` owns gameplay and
  `Assets/Tests/{EditMode,PlayMode}/` owns verification.

## Unity rules

- Do not edit or commit `Library`, `Temp`, `Logs`, `UserSettings`, generated IDE files, or build output.
- Preserve Unity `.meta` files with their assets. Move or rename assets through Unity when practical.
- Avoid hand-editing serialized scenes, prefabs, and ScriptableObjects unless the change is deliberate and verified in Unity.
- Keep runtime, editor, and test code separated with assembly definitions as the project grows.
- Prefer deterministic, data-first world generation and test its pure logic outside scene construction.
- Reuse shared materials and assets; avoid per-instance material creation.
- Every future contextual interaction that replaces the ordinary player rig
  with a sprite/atlas animation must follow
  `ai/contextual-animation-standard.md`. A deviation requires an explicit user
  decision recorded as an accepted architecture exception.

## Quality and documentation

- Make the smallest coherent change and preserve unrelated user work.
- Add or update tests for behavior that can be tested deterministically.
- Verify compilation and relevant EditMode/PlayMode behavior before declaring work complete.
- Update `ai/project-overview.md`, `ai/system-tree.md`, or `ai/systems-map.md` when their facts change.
- Record meaningful implementation sessions in `ai/work-log.md`.
- Put player-visible milestones in `ai/release-notes.md`.

## Interactive fast iteration

Choose verification mode autonomously for each task, while preserving the
user's explicit choice:

- Use **fast mode** for clear, isolated, reversible iteration where one focused
  compile or targeted test gives useful confidence.
- Use **full mode** when uncertainty is material, the change crosses important
  systems, release confidence is needed, or a missed regression would be costly.
- Briefly state the selected mode at the start of non-trivial work so the user
  can override it immediately.
- An explicit request such as "быстро", "без полного прогона", or equivalent
  forces fast mode. An explicit request for "финал", "полная проверка",
  "release", or a completed build forces full mode.
- In fast mode, make the requested change first and run only the fastest relevant
  check. Do not run full suites, player builds, or broad cross-review. Report
  deferred verification honestly.
- In either mode, treat 30 seconds without useful progress as a soft timeout:
  inspect the process immediately instead of repeating long polling cycles.
- Prefer direct local work. Use parallel agents only when they materially reduce
  ambiguity or total completion time.
- If the user says they will build or test manually, stop any matching automated
  process immediately and hand off the exact changed setting or file.

## Risk-based verification

When full mode is selected, classify each coherent change by its blast radius
and use the highest applicable risk level. If the impact is unclear, raise the
classification by one level.

- **Low risk:** documentation, comments, test-only edits, or isolated visual/data
  changes with no runtime contract change. Review the diff and run only the
  directly relevant checks; documentation-only changes do not require Unity
  tests.
- **Medium risk:** behavior changes contained within one system, including its
  public methods or data. Verify compilation and run the affected system's
  EditMode and/or PlayMode tests.
- **High risk:** shared contracts or services; scenes, prefabs, ScriptableObjects,
  or serialization; save/load; assembly, package, project, or build settings;
  Unity lifecycle behavior; cross-system or cross-scene changes. Verify
  compilation, run the full EditMode and PlayMode suites, and perform a relevant
  build or manual smoke check when the changed path requires it.

Run checks after a coherent batch rather than after every edited line. Always run
targeted tests for changed behavior, reserve the full suite for high-risk changes
and release or milestone validation, and report exactly which checks ran and
which could not be completed.
