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
- Prefer reusing or extending focused/parameterized coverage. Add a new test
  only for a distinct contract or regression that existing coverage cannot
  express.
- Follow the minimal verification policy below. Prefer one focused test that
  also compiles its dependencies; do not require compilation plus both Unity
  test layers for every change.
- Update `ai/project-overview.md`, `ai/system-tree.md`, or `ai/systems-map.md` when their facts change.
- Record meaningful implementation sessions in `ai/work-log.md`.
- Put player-visible milestones in `ai/release-notes.md`.

## Interactive fast iteration

Use **fast mode by default for every ordinary request**, including shared,
cross-system and lifecycle changes. Risk changes which focused check is most
valuable; it does not automatically authorize a broad regression run.

- Briefly state fast mode at the start of non-trivial work so the user can
  override it.
- Use release verification only when the user explicitly requests a full
  regression, complete EditMode/PlayMode run or release validation. Words such
  as "finish" or "final" by themselves do not request broad verification.
- A request to create a player build authorizes that build only. Add a smoke
  check only when the user asks for it or the task specifically changes packaged
  startup behavior. It does not imply EditMode or PlayMode suites.
- Make the requested change first, then run only the smallest relevant check.
  Do not run full suites, player builds, smoke checks or broad cross-review by
  default. Report what was intentionally not run in one short sentence.
- In either mode, treat 30 seconds without useful progress as a soft timeout:
  inspect the process immediately instead of repeating long polling cycles.
- Do not start a check already known to take several minutes unless it is the
  only practical way to validate the changed behavior or the user requested it.
- Prefer direct local work. Use parallel agents only when they materially reduce
  ambiguity or total completion time.
- If the user says they will build or test manually, stop any matching automated
  process immediately and hand off the exact changed setting or file.

## Minimal verification policy

For a normal request, use one primary check. A shared-framework change may use
one additional focused check. Stop once they provide sufficient evidence; the
default cap is one Unity invocation, or two narrowly filtered invocations for a
shared framework.

- Documentation/comments: review the diff and run `git diff --check`; no Unity
  test or build.
- Deterministic tooling, data or atlas art: run the directly affected validator.
  Do not also run general Unity suites when the validator covers the contract.
- C# runtime/editor/test code: if a suitable focused EditMode or PlayMode test
  exists, run that one selection; Unity compiles its dependencies, so skip a
  separate build. If no suitable test exists, build only the highest affected
  project because its dependencies compile transitively.
- Bug fix: rerun the concrete reproduction or its single focused regression
  test. Do not run neighboring fixtures merely because they exist.
- Scene, serialization or build-setting change: choose one proof matching the
  change—an affected scene test, a manual smoke or a player build. Do not stack
  them by default.

Never run Runtime, EditModeTests and PlayModeTests builds redundantly. Never run
complete EditMode/PlayMode suites, a Windows player build and a startup smoke in
the same ordinary task. Existing tests remain in the repository for targeted or
explicit release use; test count is not a reason to execute all of them.

If a focused check exposes an unrelated known failure, do not start a broad
rerun or repair it outside scope. Record it briefly and continue with the
requested task.
