# Repository instructions

## Before changing the project

Read `AI.md`/relevant `ai/` files; verify reality before plans.
Separate facts from proposals. Assess every user plan for project fit under
`ai/prompt-templates.md`.
Every scene change/visual task reviews all three groups, including
post-processing: [effects standard](ai/visual-effects-standard.md).

## World canon

Read both bibles before changing anything the player sees, hears, reads or does.

- `ai/city-zones-art-bible.md` owns **form**: mass, silhouette, facade,
  material, palette, light, traces of life, sound.
- `ai/city-story-bible.md` owns **meaning**: the crime, the hero, the poisoning
  scale, the Cat, what each built place signifies, and the register every
  written line must keep.

Rules:

- Check added details against `Нельзя`. No violation → allowed. A dated story
  §6 exception permits its listed violation from that level. Otherwise
  **omit the violating detail** and discuss adding a registry row.
- The story bible's §16 «Законы сюжета» are hard constraints, not guidance.
- Any new in-fiction text — `Assets/Resources/Localization/{ru,en}.json`
  included — must satisfy the story bible's §21 «Регистр текста».
- Every level of the story bible's `0-5` scale must still pass all nine art
  bible §16 acceptance checks.
- Bible or contextual-animation deviations require an explicit user decision
  recorded as an accepted exception in `ai/architecture-notes.md`.
- Update the relevant bible when its facts change, exactly as for the documents
  listed under Quality and documentation.
- New/revised in-world drawn posters, advertising prints and similar illustrated
  signs use one primitive low-poly style with angular large forms, a limited
  muted palette and rough lettering. Photorealism, gloss and rendered detail
  are forbidden. Approved photos/maps and untouched old images retain
  their styles. [Art §13](ai/city-zones-art-bible.md#13-данные-и-детерминизм).

## Git

User `2026-11-03`: «отправить в гит» authorizes committing task changes and
pushing to `origin/main` (`https://github.com/VladimirVasin/BarGame.git`)
without reconfirmation. Host approval policy still applies.

## Host power state

- Sleep requires the user's explicit request for that run; never infer standing permission.

## Supported scope

New player text is Russian; new input targets keyboard/mouse only. Retain
existing English/gamepad support. Translations, gamepad bindings/hints and
required parity/coverage need explicit requests.

## Current baseline

- Unity `6000.6.8f1`, URP `17.6.0`, Input System `1.20.1`, AI Navigation `2.0.16`.
- Runtime composition builds near-empty scenes from validated pure plans.
- Fourteen `.unity` build scenes under `Assets/Scenes/`, in order:
  `MainMenu` (index `0`), `City`, `DoorTransition`, `BarInterior`,
  `SupermarketInterior`, `StairwellInterior`, `HomeInterior`, `MountainRoad`,
  `AreaLoading`, `ChurchInterior`, `AlpineVillage`, `MothersHouseInterior`,
  `CombatTest`, `PawnshopInterior`.
  Eleven gameplay roots exclude MainMenu, DoorTransition and AreaLoading; all use
  `Resources/Player/Player3DV2.prefab` through `PlayerFactory`.
- `Assets/Scripts/Rules/` owns engine-independent rules and contracts;
  `Assets/Scripts/Runtime/` owns gameplay, `Assets/Scripts/Editor/` owns
  authoring tools, and `Assets/Tests/{EditMode,PlayMode}/` owns verification
  over the shared `Assets/Tests/Infrastructure/` support assembly.
- Deterministic art and model generators live in `tools/` and are run through
  Blender/Python, not Unity.

## Unity rules

- Scripted cameras time expressive FOV/Dutch/horizon rolls to the action.
  Author and review lens, roll, framing and character contacts together.
- Do not edit or commit `Library`, `Temp`, `Logs`, `UserSettings`, generated IDE files, or build output.
- Preserve Unity `.meta` files with their assets. Move or rename assets through Unity when practical.
- Avoid hand-editing serialized scenes, prefabs, and ScriptableObjects unless the change is deliberate and verified in Unity.
- Keep runtime, editor, and test code separated with assembly definitions as the project grows.
- Prefer deterministic, data-first world generation and test its pure logic outside scene construction.
- Maps: built-in ImageGen/pure plans; City charcoal/Silent Hill; Village
  old red-crossed resort brochure. Each slice: geometry/RU labels/live markers/
  selection/XYZ/safe arrivals/presence/grouping. User `2026-11-04`: redraw once
  at final step if geometry/elevation changed; stale art→scheme, no SHA
  retagging. Courtyard props exempt (`2026-10-29`).
- Reuse shared materials and assets; avoid per-instance material creation.
- Keep bus queues clear of the through sidewalk. After stop, route or scenery
  changes, test every platform in PlayMode, transfers included. Walk from both
  sidewalk directions past waiting NPCs to the shelter and board with `E` at each door.
- New/changed item interactions reuse [the standard](ai/item-interaction-standard.md):
  keyed prompts, shared 3D pickup/receipt UI, one grant, input/cleanup.
  Exceptions require a recorded user decision.
- Register new default NPCs' permanent IDs/model/garment constraints in
  `DefaultNpcPopulation`. Use `DefaultNpcFactory.CreateForCharacter` and only
  `DefaultNpcCatalog` models. Keep globally unique face/hair/clothes across loads.
  Bespoke looks or missing-type pool extensions need explicit user request.
  No local randomizers. Keep placement/actions local (see `2026-09-15` in
  `ai/architecture-notes.md`).
- Schedule NPC lunch breaks only on explicit user request (`2026-11-09`).
- Speech/dialogue follow [the standard](ai/speech-presentation-standard.md).
  Every spoken line, including `E` replies, uses its speaker's shared bubble;
  bottom UI is silent. Feature-local UI/typewriter/audio/lifetime copies are
  forbidden. Deviations require explicit user decisions recorded as accepted
  architecture exceptions.
- Future contextual interactions follow `ai/contextual-animation-standard.md` using the
  continuous modular 3D hero's world rig or a camera-local first-person subset
  of that prefab. Sprite/atlas presentation is retired. Deviations require an
  explicit user decision recorded as an accepted architecture exception.

## Optimization from the start

Every feature/fix/refactor/asset considers CPU/GPU, memory/GC and loading from design.

- Bound recurring work/populations/caches/build stages. Reuse assets, materials,
  buffers and references; avoid repeated searches/loads and needless hot-loop
  allocations. NonAlloc overflow needs grow/retry or a correct fallback.
- Use events and suitable background update rates. Distance/visibility scales
  presentation while preserving authoritative time/simulation, collision and
  immediate input. Keep visual/wind bounds, rig/contact continuity, NPC identity
  and pause/disable/destroy/scene cleanup correct.
- Match techniques to workload/pipeline. Compression/LOD/culling/batching retain
  accepted quality/canon. Claim gains from comparable measurements; identify
  unmeasured expectations.
- Follow FAST below; no broad profiling or unrelated rewrite is implied.

## Workspace retention

Every ignored path needs a `tools/workspace-budget.json` policy of `never`,
`retain` days, `idle` cache days, `declared` or `allowed`.
`python tools/check-workspace.py` checks all worktrees; missing policies fail.
`--sweep --apply` refuses busy Unity/Blender checkouts, tracked files and live
document references. Delete temporary `Captures/` (`never`) outputs after review
at task end, including videos/reports/staging. Record named checks without
capture links. Fix growing writers or shorten retention; never raise it.

## Quality and documentation

- Make the smallest coherent change and preserve unrelated user work.
- Extend focused tests unless the existing coverage cannot express the regression.
- A focused Unity test compiles dependencies. Skip a separate compilation.
- Update `ai/project-overview.md` or `ai/systems-map.md` when their facts change.
  Use `git ls-files`/`rg` as the path reference; do not keep a copied file tree.
- Update both bibles under World canon.
- Record meaningful implementation sessions in `ai/work-log.md`.
- Put player-visible milestones in `ai/release-notes.md`.
- Keep `README.md`, the only human-facing document, current with what the
  player sees and controls.
- `ai/systems-map.md` is an index. Keep each cell to one or two rendered lines
  and use only the four statuses defined in `ai/README.md`; a `Partial` row
  must name its gap. Put the detail in `ai/architecture-notes.md`.
- **Every document has a type and a byte budget in `tools/docs-budget.json`, and
  the type says how you write to it.** `python tools/check-docs.py` enforces both
  and is the primary check for a documentation change.
  - `ledger` (`ai/work-log.md`, `ai/release-notes.md`): **one entry per date, not
    per slice**; extend today's entry. Caps `3000 B` / `2000 B`: changed facts,
    why non-obvious, **named proof**. Never durations/counts/sample sizes,
    `TestResults/`/`Captures/` paths or skipped-suite notes. At the byte budget,
    archive whole dates into `ai/archive/` until under the floor; not by calendar.
  - `state` (`ai/current-world.md`, `README.md`, `tools/README.md`): describes
    the world **as it is now**. A change to reality is a rewrite of the affected
    paragraph, never a paragraph beside it.
  - `canon` (both bibles, `ai/architecture-notes.md`): grows only by decision,
    and superseded text is **deleted** rather than kept with a marker — git holds
    the history. Section numbers, cited `###` titles and the story bible's §6
    registry dates are frozen; the checker refuses to let them move because code
    cites them.
  - `index` (`ai/systems-map.md`) and `entry` (`AI.md`, `AGENTS.md`,
    `ai/README.md`, `ai/project-overview.md`, `ai/prompt-templates.md`): small by
    definition, because they are read every session.

## Interactive fast iteration

Use **fast mode by default**, including shared, cross-system and lifecycle
changes. Risk selects the focused check; it does not authorize broad regression.

- State fast mode at the start of non-trivial work; the user may override it.
- Use release verification only when the user explicitly requests a full
  regression, complete EditMode/PlayMode run or release validation. Words such
  as "finish" or "final" by themselves do not request broad verification.
- A request to create a player build authorizes that build only. Add a smoke
  check only when the user asks for it or the task specifically changes packaged
  startup behavior. It does not imply EditMode or PlayMode suites.
- Make the requested change first, then run only the smallest relevant check.
  Do not run full suites, player builds, smoke checks or broad cross-review by
  default. Say what was intentionally not run in one short sentence **in the
  reply to the user**, never in `ai/work-log.md`.
- In either mode, treat 30 seconds without useful progress as a soft timeout:
  inspect the process immediately instead of repeating long polling cycles.
- Do not start a check already known to take several minutes unless it is the
  only practical way to validate the changed behavior or the user requested it.
- Prefer direct local work. Use parallel agents only when they materially reduce
  ambiguity or total completion time.
- If the user says they will build or test manually, stop any matching automated
  process immediately and hand off the exact changed setting or file.

## Minimal verification policy

Normal work uses one primary check; shared frameworks may add one focused check.
Stop when sufficient. Cap one Unity invocation, or two narrowly filtered ones
for a shared framework.

- Documentation/comments: run `python tools/check-docs.py` and review the diff;
  also run `git diff --check`. No Unity test or build. The checker enforces
  budgets, ledger shape and frozen canon references, reporting remedies.
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

Test counts do not justify a full run.

Record unrelated failures; continue without broadening checks or repairs.
