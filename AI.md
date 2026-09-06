# AI project entry point

Read this file first, then use [ai/README.md](ai/README.md) as the documentation index.

## Reality check

- Unity `6000.6.0f1`, URP `17.6.0`, Input System `1.20.0` are pinned in the
  project/package settings. The current target is Windows/PC.
- Twelve build scenes contain nine gameplay roots. Gameplay is runtime-composed
  from validated plans; every root uses `Resources/Player/Player3DV2.prefab`.
- City, Mountain Road and Alpine Village occupy separate Single-loaded scenes.
  Area travel keeps its loading overlay until incremental world construction
  finishes; isolated synchronous builder entry points remain available.
- `BarPromenade.Rules` owns engine-independent calendar, activity ownership and
  input-priority rules. Runtime, Editor and test assemblies have separate roles.
- Session progress survives scene loads, not process exit. Save/load remains
  deferred. Temporary ride ownership is reset with the session.
- [Project overview](ai/project-overview.md) contains the current technical
  baseline; [current world](ai/current-world.md) holds detailed gameplay facts;
  [systems map](ai/systems-map.md) locates each owner. Read the relevant sections,
  then inspect the actual files before changing them.

## Source-of-truth order

1. Files currently present in the repository.
2. Unity project and package settings.
3. `ai/architecture-notes.md` for accepted decisions.
4. `ai/city-zones-art-bible.md` and `ai/city-story-bible.md` for what the world
   is allowed to be.
5. Planning documents for intended work.

Never report a planned system as implemented without inspecting relevant
repository evidence. This does not require running every test layer.

## Working agreement

- Use the canonical workflow matching the task in `ai/prompt-templates.md`.
- Fast targeted verification is the default. Complete suites require an
  explicit release/full-regression request. Create a player build only when it
  is the requested deliverable or release gate; add a smoke only when requested
  or when packaged startup behavior is the changed contract.
- Start from `ai/project-overview.md` and `ai/systems-map.md`.
- **Anything the player sees, hears, reads or does in the world is governed by
  two mandatory documents:** `ai/city-zones-art-bible.md` for form and
  `ai/city-story-bible.md` for meaning. Before adding a detail, find the
  `Нельзя` it would violate — none means allowed, one dated in the story
  bible's §6 registry means allowed from that level, and one that is not in the
  registry means the detail is not added. New in-fiction text must satisfy the
  story bible's §21 register, its §16 laws are hard, and every scale level must
  still pass all nine art bible §16 acceptance checks. See AGENTS.md, World
  canon.
- All future contextual player animations must follow the mandatory
  `ai/contextual-animation-standard.md`; do not add one-off teleport, root-motion
  gameplay transactions or visibility fades that conceal mismatched endpoints.
- **Every 3D object is assembled in Blender.** New geometry is authored by a
  deterministic generator under `tools/build-*-3d-model.py`, exported and
  imported as a model asset; it is not composed at runtime out of
  `RuntimePrimitiveFactory` boxes and cylinders. The existing generators are
  the pattern to copy — player, pedestrians, bus, bus driver, bartender,
  cashier, supermarket interior and product pack, cat, chess set, Last Route
  car, church, Mountain Road misc and City misc — each pairing its script with
  a measured JSON manifest and a
  determinism check. The pinned Blender build and executable discovery are
  owned by `tools/toolchain.json` and `tools/toolchain.py`; use the common
  `tools/run-blender.py` launcher documented in `tools/README.md`.
  This is a rule for what is built from now on. The structural runtime-primitive
  geometry still in the tree — terrain, roads, logical building collision and
  foundation masses, infrastructure, dynamic precinct pieces, the mountain
  road and its terminal — predates it
  and is not retroactively invalid. The City and Mountain Road misc libraries
  are explicit bounded migrations; moving anything else remains its own
  decision, taken piece by piece and never as a side effect of another task.
- **Interiors share one authoring library: `tools/interior_kit.py`.** It is
  imported by interior generators and holds what a box cannot express — wall
  runs with real openings and reveals, swept mouldings, chamfered edges,
  panelled leaves, turned legs. Its rule is that it contains no value belonging
  to any one room; if a number is specific to the bar it lives in the bar's
  generator. The bar is the first thing built on it
  (`tools/build-bar-3d-model.py`, with `tools/bar_parts.py` for Unity-space
  authoring) and is fully migrated, interior and facade. The supermarket is
  now the next complete fixed-metre user: its exterior comes from
  `tools/build-supermarket-exterior-3d-model.py`, while the passive interior
  shell, fixtures and CCTV pivots come from
  `tools/build-supermarket-interior-3d-model.py`. Its six reusable generic,
  unbranded and text-free product models come from the separate passive
  `tools/build-supermarket-products-3d-model.py` pack: five are finite shop
  stock, while the open stew can enters the world only through the Home
  refrigerator/cat flow. The apartment/stairwell remain future migrations.
- **Blender's axes reach Unity by SWAPPING the last two, not by negating one.**
  Unity `(x, y, z)` is Blender `(x, z, y)` under the project's export settings
  (`axis_forward="-Z", axis_up="Y"`) plus `bakeAxisConversion`; the
  right-to-left handedness change is what removes the sign one would expect.
  Never settle this by reasoning about it — assert an authored anchor against
  the plan position it is supposed to occupy, as `BarModelContractTests` does.
  Getting it wrong silently mirrors the model: the bar's doorway landed in the
  opposite wall and its counter 9.5 m away.
- **An imported FBX keeps its unit factor on the authoring root.** That root
  arrives scaled `100` and its meshes store vertices at a hundredth of the
  metres they were authored in; an anchor's `localPosition` is likewise a
  hundredth. Anything that separates a part from that root — a reparent with
  `worldPositionStays: false`, an `Instantiate` followed by
  `localScale = Vector3.one`, reading `anchor.localPosition` instead of
  `anchor.position` — silently makes it a hundredth of its size or puts it a
  hundredth of the way to where it belongs, while anchors, collision, counts
  and the manifest all stay right, because none of those come from the meshes.
  Reparent with `worldPositionStays: true`, take a clone's scale from the
  template's `lossyScale`, read anchors through world space, have the asset
  setup MEASURE the imported renderers against the manifest bounds, and have a
  test MEASURE the placed room — a correct prefab can still be placed wrongly.
  This cost three separate defects in the bar, and only a rendered frame found
  the last two.
- **A swapped axis pair is a reflection, so it reverses face winding.** Any
  generator that authors in Unity space and converts must re-wind every face,
  and any ring swept through XZ winds the opposite way from the same ring
  through XY. Inverted normals survive wireframes, triangle counts and every
  dimension assertion; check the signed volume of each solid at generation
  time, as `tools/bar_parts.py` does.
- **One working copy per concurrent session.** Two agents in the same checkout
  fight over one Unity project: only one instance may open it, so the other's
  runs abort with "another Unity instance is running", and a half-written file
  from one breaks the other's compilation and wastes a whole ten-minute test
  run. In one session that cost three broken compilations, several aborted
  runs and two foreign red tests in every report. Give each session its own
  checkout — `Library/`, `Temp/` and `Logs/` are already gitignored, so a
  worktree is self-sufficient:

  ```
  git worktree add ../БП-<branch> <branch>
  ```

  The first Unity launch there is slow while `Library/` is rebuilt; that is the
  whole price, and it is paid once. Branches still merge the ordinary way — the
  gain is that until they merge the sessions cannot break each other.
- **Look at what you changed.** `Assets/Tests/PlayMode/AreaCaptureFixture.cs`
  renders any world scene to `Captures/<area>/`. Run it for any scene whose
  appearance changed and open the frames. Numbers cannot see an object in the
  wrong place or a mesh at a hundredth of its size.
- Update the maps and work log when implementation changes project reality.
- Keep documentation concise and mark uncertainty directly.
