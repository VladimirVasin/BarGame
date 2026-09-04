# Architecture notes

Decisions marked `Proposed` become accepted only after implementation confirms them.

## Current facts

- **Accepted:** Unity `6000.6.0f1` with URP `17.6.0` (moved from `6000.5.10f1` / URP `17.5.0` on 2026-09-04; the package set — test framework `1.8.0`, Timeline `6.6.0`, uGUI `2.6.0` — came with the editor).
- **Accepted:** New Input System is enabled.
- **Corrected — balcony smokers are a local population, not a city-load
  tableau:** a production-seed walk exposed the failure of selecting one or two
  fixed Residential buildings for the whole map: the only actor could live in
  a district the player never crossed. Every ordinary Residential building now
  publishes one deterministic candidate dock, while a per-session director
  rolls only front-facing candidates from the fog-readable lowest balcony row
  within `22 m` of the moving hero, prefers the `12-22 m` cross-street band
  ahead of the current travel direction, bounds an empty eligible area to one
  missed opportunity, keeps at most two
  active and releases them beyond `36 m` or after the hero leaves the facade
  side.
  Presentation remains passive and unchanged; Home retains a separate bounded
  deterministic composition because its exterior view is a modal reconstruction,
  not the live City runtime.
- **Accepted:** Domain reload is disabled on entering play mode
  (`m_EnterPlayModeOptions: 1`); scene reload is kept, being cheap and safe.
  This makes every mutable static field in `Assets/Scripts/Runtime` survive
  from one run to the next, so each must be cleared by a
  `RuntimeInitializeOnLoadMethod(SubsystemRegistration)` hook — the pattern in
  `BarSurfaceAppearance.ResetCachedResources` and `GameSessionState.Reset`.
  The runtime has no `static event` at all, which is what makes this safe;
  keep it that way. A static field holding a `UnityEngine.Object` is the worst
  case, because it survives as a DESTROYED object rather than as null.
- **Accepted:** Appearance is verified by rendering, not by asserting.
  `Assets/Tests/PlayMode/AreaCaptureFixture.cs` photographs a world scene
  through its own main camera, so the frames carry the real lighting and
  post-processing. The captures are `[Explicit]`: they are not tests and must
  be run one area at a time, because heavy scene-loading fixtures run together
  trip `ExitPlayModeTask`.
- **Accepted:** Interior generators share `tools/interior_kit.py` — wall runs
  with real openings, swept mouldings, chamfers, panelled leaves, turned legs.
  It holds no value belonging to any one room, which is the only test of
  whether it is general; the next interior must import it unedited.
- **Accepted:** A Blender-authored interior gets NO material assets. Every
  renderer shares `RuntimePrimitiveLit` and receives its sheet, tint,
  smoothness and metallic through a `MaterialPropertyBlock`, which is the same
  path `BarSurfaceAppearance.Apply` takes for a runtime primitive. This
  diverges from the church, which creates one material asset per slot, and it
  is deliberate: the bar's appearance varies by district, and a district tint
  is a `_BaseColor` in a property block. Baking materials into the FBX or into
  per-slot assets would need a material set per district per part.
- **Accepted:** Model UVs are baked in Blender at each sheet's measured
  metres-per-tile, so `_BaseMap_ST` stays the identity. `SurfaceAppearance`
  remains the source of those numbers but is not applied to an authored
  renderer: `Apply` derives its scale from the mesh bounding box, which is
  correct for a slab and wrong for a wall run with a doorway cut through it.
- **Accepted:** Geometry from Blender carries no collider, light, camera,
  rigidbody or animator; each is enforced by the asset setup's
  `ValidateOrThrow`, not by review. Collision and illumination stay authored
  from the layout plan, so a model can be re-cut without risking traversal.
- **Accepted — the bar is one authored pub, not a runtime furniture kit:**
  `bar_interior_v3` keeps the validated `22 x 16 x 4.8 m` plan but moves every
  permanent visible surface into one passive Blender asset (`174` semantic
  meshes / `12,832` triangles at generator `3.3.3`). Its counter return,
  backbar, booths/snug, four
  small round tables, music pocket, facade-matched `1.45 x 2.34 m` panelled
  entrance, carpet/plank and practical
  fixtures express a worn late-Victorian British pub without flag, name,
  brand, readable advertising or new lore. `bar_service_props_v1` is a second
  passive `34`-mesh / `4,136`-triangle library at `1.4.0` for five reusable
  bottle silhouettes, five vessel forms with interaction-highlight shells,
  pour stream and two-page menu. Runtime now
  instantiates only the four bottles belonging to the visible menu. Unity continues to own stable
  layout data, collision, light, liquid and interaction state; the deleted
  runtime service-mesh library has no second geometry authority.
- **Accepted by explicit user decision, 2026-09-04 — the interior entrance is
  the standard bar door:** the former `3.20 x 4.80 m` room-height opening,
  oversized trim and curtain assembly are removed. The authored interior now
  repeats the facade's ordinary `1.45 x 2.34 m` panelled door language and the
  collision plan closes the wall above it with a lintel. Door interaction and
  `DoorTransition` ownership are unchanged.
- **Accepted by explicit user decision, 2026-09-03 — bar patrons are bound to
  concrete furniture, and the hero stool has no visual affordance:** every
  regular and hero counter stool uses the Mountain Road cafe geometry
  (`0.8175 m` top, `0.48 m` seat diameter and `0.055 m` thickness) and the
  exact runtime `CafeMetalDetail` / `CafeCounterDetail` surfaces. The hero
  stool joins the regular row at local `z = 4.53`; its trigger and approach
  remain authored, but the deleted floor marker and the stool itself provide
  no special visual affordance. The bar no longer samples a general roaming
  order: its deterministic `11`-person composition is six compatible booth
  sitters on `0.48 m` cushions, two counter sitters on `0.8175 m` stools and
  three standing table patrons. The Yard Babushka and the crouched
  Chess/Checkers designs are excluded because they cannot satisfy the relevant
  furniture-contact contract. Counter patrons reuse the exact authored
  `CafeManDrink` clip through an additional input in the presentation's one
  existing `PlayableGraph` and replace the cup with a bottle. A scene-local
  overlay visibly leans torso/head back, turns the bottle horizontal and solves
  its authored neck anchor to the mouth. Table patrons apply the same sip while
  planting their left hand on the real tabletop. After `Lower`, all five
  drinkers place the bottle upright with its base on their actual counter or
  table surface, keep the right hand at a side-surface grip outside the bottle
  mesh and support the free hand on that same surface. The grip solves the
  complete `hand.R` bind-space frame: it approaches from the patron's actual
  right side and fixes wrist roll instead of positioning only the socket;
  round-table bottles dock visibly inside the tabletop rather than on its rim.
  A slight seeded,
  non-referential head drift keeps the
  resting pose alive until the next `Raise`; it targets neither the hero nor
  another patron.
  World construction evaluates seating, action and prop attachment before the
  first visible frame. This changes only the existing bar tableau and action
  presentation: it adds no dialogue, interaction, character design or story
  state.
- **Accepted — bar seats are reusable and cafe/bar share one physical menu
  substrate:** `BarCounterSeatPlanner` creates a station for every stool not
  occupied by a patron; each receives its own authored approach, entry/exit,
  camera translation and staff-service offset through
  `CounterSeat{Plan,Interaction,View}`. The rightmost position is `x = 4.00`,
  clear of the counter return, and a failed approach releases its provisional
  shop binding so it cannot poison the other stations. Both scenes consume
  `CounterMenu{Model,Input,PageView,HintView,PropMotion}` for the ordered
  open/rest/reopen/post-exit lifecycle, wrap navigation, upright page focus,
  world TMP/selection marker, the shared `0.40 s` opaque hinge fold/unfold,
  contextual hint and grip-to-dock delivery/retrieval; their adapters supply
  only rows and
  choreography. The bar's menu dock has no lateral service offset: it follows
  the selected stool axis, so the bartender places the booklet directly
  before the hero just as in the cafe. Exactly three beer taps sit in one
  `0.33 m`-spaced bank on the seat-free right overlap of the main counter and
  its return, beyond the last stool. The bar adapter moves physically to
  `0.45 m` / FOV `72` and uses a dedicated near-overhead solve: its camera
  projection stays within `0.14 m` of the larger spread's centre rather than
  inheriting the seated eye's grazing angle. The smaller cafe card retains
  `0.50 m` / FOV `40`. The bar-only page style uses fixed `0.20` bold, near-black
  type in four taller multiline blocks normalized to an inset `2 x 2` grid;
  normal word wrapping replaces per-row auto-sizing, so every localized block
  keeps that exact size,
  while two blocks per page retain their full name, price and description.
  Only the fully open bar booklet engages cinematic DOF, retargeted to its page at
  `35 mm / f/8`. Resting it, entering service or starting to stand immediately
  zeroes that volume before the stored seated or third-person camera returns.
  `BarAssetSetup`
  reapplies each manifest-declared Unity basis in prefab-root space because
  FBX Empty wrapper axes otherwise rotate the text sockets when the menu
  origin is aligned to its dock. The
  cafe keeps three price-free, effect-free entries; the bar supplies four
  localized low-grade drink names, prices and descriptions, leaves failed
  purchases open, and after a successful paid order closes the booklet on the
  counter while entering physical service. Beer then uses the central tap;
  consumption effects wait for the hero's completed drink. The former privileged-seat floor
  marker and yellow emissive counter sign are absent from play. This records
  the user's explicit `2026-09-03` shared-substrate decision and the
  `2026-09-04` multi-seat/lifecycle extension, not a second parallel menu.
- **Accepted architecture exception — 2026-09-04, explicit user correction —
  bar confirmation and close no longer share the cafe's contextual action:**
  the reported beer selection left no `session/drink_purchase_resolved` event
  because the visually dominant `E` action rested the booklet while only the
  smaller `Space` hint reached purchase. In the open bar menu,
  `BarCounterStation` now routes `E`/`Enter`/gamepad South through the same
  guarded `ConfirmSelection` transaction as `Space`/gamepad West; `Escape`
  remains the separate rest-without-order path. A failed purchase stays open.
  The cafe adapter deliberately retains its selection-only `Space` and
  close-on-`E` behavior. This supersedes only the shared input semantic, not
  the physical `CounterMenu` substrate or either venue's content contract.
- **Accepted by explicit user decision, 2026-09-04 — the visible bar menu is
  four low-grade drinks, not the former nine-offer list:** its exact order is
  beer, wine, distillate and vodka. Each receives a dry localized product name,
  fixed price and two-sentence description which signals poor quality through
  ordinary taste and packaging rather than a fictional brand or a new source
  of poisoning. Two logical blocks reuse the outer authored text sockets on
  each page; the passive nine-socket asset remains compatible and is not a
  second content authority. `BarDrinkServicePlan` centres four physical shelf
  bottles. The complete historical DrinkId/purchase lookup and unused bottle
  presentations remain readable for existing session/save compatibility and
  patron props, but the bar controller exposes only the four-item ordered
  `BarDrinkCatalog.Offers`. This supersedes only the nine-row/water-in-menu
  parts of the earlier shared-menu decision; service, payment and the fact
  that poisoned municipal water is the story's real danger remain unchanged.
- **Accepted by explicit user decision, 2026-09-03 — the active bartender is
  ordinary and two-armed:** `bar_bartender_v2` is a `1.75 m`, `39`-mesh /
  `1,136`-triangle NpcHumanV2 figure in a dark-green waistcoat, rolled sleeves
  and apron. Its registry reuses the four existing cafe-attendant clips and
  its manual service graph follows the established `BarDrinkServiceTimeline`:
  right hand to bottle, left hand to menu or vessel. `BarBartenderProvider`
  selects `BarBartenderOrdinary.prefab`; the former six-armed prefab remains a
  serialized inactive legacy reference. This is a one-for-one active-cast
  replacement and introduces no new strange body.
- **Accepted:** Alpine Village separates inhabited `TerrainBounds` from the
  larger physical `TerrainMeshBounds`; only the latter may prove the enclosing
  ridge and cable brink.
- **Corrected — the village walkable mask is ground minus obstacles, not a
  corridor:** it was a capsule chain over the lane plus one capsule per
  `AlpineVillagePathDescriptor`, on the argument that an invisible branch
  through pristine snow is not a route. Measured, that left `6.4 %` of an
  `11 703 m²` bowl standable and an invisible wall a step off the lane in every
  direction. The mask is now `TerrainBounds` grown by the sampler's own
  `RidgeStandoff` — the exact line where the `74°` rise begins, so the terrain
  holds the perimeter — minus each plot's rotated footprint (the same rectangle
  its `Physical Shell` collider stands on, so the mask prevents wall contact
  rather than surviving it) and minus the cableway cut, whose `7-28°` descent
  is the only walkable way out of the village and the one boundary the mask
  holds alone. The burial ground is deliberately not an obstacle and the adit
  gained the shell it never had. Path descriptors keep their traversal
  half-width as a pure collision envelope: every segment must clear every
  rotated plot OBB, with
  the adit using an authored outer hook around the rear-row houses rather than
  a shortcut through them; its turn is selected from the seeded expanded OBB
  of house 08. Rotated plot collision is OBB/SAT, never an
  unrotated AABB;
  explicit rear-row depth beats own the frontage layers and the seeded solver
  may only make a bounded symmetric correction around them.
- **Accepted — the adit and the burial ground are out of the village and out
  of the story:** the lead's explicit decision, taken after the adit's spoil
  heap was found to be a twelve-triangle box reading as a table above the
  street. Both bibles are amended rather than excepted, and the father's grave
  goes with the cemetery. The head of the SPRING stands where the adit did, so
  the one reason the place exists above the cableway is water — which the
  chapel over the source always was. Enum numbering keeps holes, as the deleted
  city lake's did, so nothing that ever wrote a value down reads a different
  place back; and a lookup table indexed BY those values (the village
  soundscape's) had to become a search by kind the moment a row was removed.
- **Accepted — a skin laid over sampled ground samples that ground at every
  vertex:** the village lane was laid flat at its centreline's height across
  `3.6 m` while the ground under it is the sampler's, so once the shelf blends
  ran at their intended width the terrain cut up through it at `423` of `2490`
  probes, `0.44 m` at worst — pale wedges across the street that were reported
  as snow and were not. Two vertices cannot follow a curve: subdivide across,
  sample each vertex, and ride a lift of the same order as `SeamBurial`,
  because the terrain is drawn on a `2 m` grid whose chords stand above the
  smooth height. The same rule caught the snow ribbon bridging crossing routes
  at a coarse cross pitch. A test for either must measure the CHORD that is
  drawn, not a point on it — the point-sampled version passes while the street
  is visibly cut.
- **Accepted — the motor decides WHEN a footstep happens, the surface decides
  what it is:** `IPlayerFootstepSurface` hands the step to whoever the hero is
  standing on, and a claimant owns sound and effect together so a surface and
  the default can never double. Snow-versus-trodden is what makes a route
  audible before it is visible.
- **Accepted — a dock is an interaction pose, never an arrival:** a chart
  point carries a place's `DoorDockPosition`, which stands `1.1 m` off a
  threshold facing it. Landing a player there puts him against a wall, and the
  gait is weighted by ACHIEVED speed, so a blocked hero stands in `Idle` while
  the input says walk - it reads as a broken animation, not as a wall. Village
  arrivals stand back along the route the place is reached by and look at it.
  The same rule keeps arrivals on trodden ground, whose envelope is already
  validated clear of every plot footprint.
- **Corrected — `Mathf.SmoothStep`'s third argument is a `0-1` fraction, not a
  distance:** `Mathf.SmoothStep(0f, blend, metres)` returns METRES and
  saturates at one metre of input, so `1 - that` goes negative and survives
  only because `Mathf.Lerp` clamps. The village's three shelf blends ran at
  `0.347 m` against a constant naming `3.6` — a factor of `10.4` — while the
  guards beside them believed the constant. Distance-driven easing uses
  `SmoothStep(0f, 1f, InverseLerp(start, end, value))`, which the same file
  already had as `SmoothRange`. A sweep found every other call in the project
  correct.
- **Corrected — the village ground carried vertex colours no shader reads:**
  `RuntimePrimitiveLit` is `Ps1Lit`, a verbatim URP Lit copy, and URP Lit's
  `Attributes` has no `COLOR` semantic in any pass. Ground tint belongs to the
  surface sheet and to the path ribbons that already carry it; a mesh writing
  colours into the shared default material is writing into nothing. The dead
  field was deleted rather than revived, because reviving it means hand-editing
  a clone that must stay re-copyable on a URP bump.
- **Accepted:** Small causal props in the village use
  `AlpineVillageDressingPlanner` for form-owned semantic IDs and anchors; the
  world and soundscape are readers, so rendering never depends on audio to
  decide where an object exists.
- **Accepted:** Gameplay and transition presentation are composed at runtime
  in twelve explicit build scenes; `MountainRoad`, `AreaLoading`,
  `ChurchInterior`, `AlpineVillage` and `MothersHouseInterior` are appended at
  build indices `7`, `8`, `9`, `10` and `11`.
- **Accepted:** City, Mountain Road, Alpine Village, Bar, Supermarket, Home,
  Stairwell, Church and Mother's House instantiate one
  `Resources/Player/Player3DV2` modular hero prefab through `PlayerFactory`.
  Its Generic rig, independent mesh parts, in-place Actions, prefab-derived
  visible refrigerator arm, nested full-body seated bar-drinking actions,
  dedicated 3D portrait, real mesh shadows and
  analytic contact patch are the active player presentation. A runtime-composed
  13-body companion ragdoll temporarily owns those same bones during failed
  balance falls; no alternate hero or renderer swap is used.
- **Accepted — The Kettle Hat boil is a declared per-archetype effect, not a
  clip and not a bone:** the kettle walker's lid trembles and vents and his
  spout steams in every state — idle, walk, bench, Route 01 ride, balcony
  view — because the boil belongs to the kettle, not to the gait. It is
  declared three times and must agree: `carriesBoilingKettle` on the
  `CityPedestrianArchetype`, on the editor `PedestrianDescriptor`, and
  `signature_effects: ["boiling_kettle"]` in the model manifest; the factory
  refuses a prefab whose `CityKettleHatRigAnchors` disagree with its catalog
  entry. The rig stays the exact 31-bone Hero V2 hierarchy: the prefab build
  creates an identity-frame `ANCHOR_KettleLid` under the head bone and
  re-points the one `head` entry in the lid's and knob's
  `SkinnedMeshRenderer.bones` at it (bind poses untouched, found by reference,
  exactly one hit), measures the lid centre, kettle axis and two tilt axes in
  the bind pose and stores them head-local, and drops `ANCHOR_KettleSpout` at
  the measured mouth of the spout — the fisherman's bind-pose-anchor idiom, so
  no gameplay code re-derives the FBX axis swap or the prefab's 180° flip. A
  pure `KettleBoilModel` (seeded phase, `2.2-3.1 s` LCG vent period, lift
  `14 mm` / tilt `5.5°` at the vent, `3 mm` / `1.2°` tremble between) is
  fed the presentation's own sanitised, distance-accelerated delta through
  `CityPedestrianPresentation.Advanced`, and `CityKettleHatBoilEffect`
  (attached by the factory, never authored, guarded by `IsInitialized`
  because it lands on a live object that is deactivated a moment later)
  writes the pivot as `localRotation = R`, `localPosition = c − R·c +
  InverseTransformVector(lift)` — one metre is `0.01` head-local units under
  the 100x root, so no metric constant ever touches a bone child's
  `localPosition` — and drives a code-built steam ParticleSystem on the shared
  atmosphere material with no Light and no sound. Unlike the fisherman's
  plume the steam is `AlwaysSimulate`, because a pooled walker moves while
  culled and a paused plume would be left standing in the street; it runs in
  world space and switches to local space with a lower, shorter rise while
  the walker is seated or aboard the bus, so it rides the cabin instead of
  streaming out of its roof. The walker is also the first pedestrian with a
  texture: a `256 px` grey detail atlas whose UVs are authored straight into
  its sub-rectangles, bound per renderer through the same property block as
  the palette tint and multiplied by it — so the one shared `Player3DLit`
  material and its three "every renderer shares one material" gates are
  untouched and all four palette variants survive one PNG. This inverts Hero
  V2's clothing contract (full-colour atlas on its own material, white tint)
  on purpose. The generator version stayed `4.0.0`: the new manifest keys are
  emitted only by designs that declare them, so the other thirteen city
  signatures are byte-identical.
- **Accepted — NpcHumanV2 is the common adult anatomical substrate:** all
  `27` rigged humanoid NPC model designs on disk copy the production Hero V2
  31-bone A-pose Avatar and use its `0.835 m` rest pelvis. The active humanoid
  cast does not grow: the ordinary bartender and cashier each replace a
  retained inactive predecessor one for one. Ordinary silhouettes target
  roughly `7–7.5` heads and `2.3–2.5` head-width shoulders without increasing
  polygon density. The Long-Arm figure, kettle head and hopper feet remain
  authored overlays on that substrate; six bartender arms and the Watcher's
  long neck are retained asset history, not active-world overlays.
  Amended 2026-08-31 by explicit user request: the three arch-shelter residents
  are no longer unrigged exceptions. They are staged Hero-Avatar prefabs with
  separate `256 px` detail atlases and three long, autonomous, bone-only loops
  in an isolated animation bank. The bank records deformed all-frame planar
  envelopes; the generator and Unity importer independently reject a sleeper
  that leaves the imported mattress's measured `1.89618 × 0.83633 m`
  surface. Its resident-local yaw is zero because the sleeper root inherits
  the bedding assembly's authored `-5°` world yaw. The legacy static blanket
  stays addressable for surface validation but is not rendered; the rigged,
  breathing blanket replaces it. The
  legacy static residents remain in the City-misc catalog only for
  compatibility and are never instantiated.

- **Accepted — the normal/bizarre verdict lives in C#, temporarily:** every
  character design now carries one of two marks,
  `NpcDesignAppearance.Normal` or `.Bizarre`, in
  `Assets/Scripts/Runtime/Core/NpcDesignAppearance.cs`, keyed on `design_id`
  and covering all `29` designs (the `27` rigged humanoid assets plus the raven
  and the cat; the hero's own models are out of scope). The line is this
  document's and the art bible's, not a new one: strangeness of the BODY is
  bizarre, a strange thing worn or carried is not — «его странность — само
  тело, а не надетый или несомый предмет»
  (`ai/city-zones-art-bible.md` §15). Animals are judged as ordinary
  specimens of their own species. Eight designs are bizarre: Long-Arm,
  Lampshade, Chair Carrier, Kettle Hat and Helmet Lamp Hopper among the
  authored walkers, the six-armed bartender, the retained Watcher cashier and
  the stairwell cat.
  The active `bar_bartender_v2` and `supermarket_cashier_v1` belong to the
  normal group, giving `8` bizarre and `21` normal designs without increasing
  the active cast. The
  long-eyed bus driver is NORMAL by explicit user decision — his head is
  authored as an ordinary one and his own generator refuses any part that
  would replace or conceal it, so a stylised eye sits nearer a worn thing
  than a wrong body.

  **Runtime does not read it yet, and its home is provisional.** Editor asset
  validation may assert a recorded verdict, but model selection stays explicit
  in providers. The natural place is beside `signature_anatomy` in each model
  manifest, and it belongs there the day behaviour depends on it. It is not
  there now because the generators
  have no manifest-only mode — `main()` always exports the FBX and saves the
  blend — so one JSON key would cost `28` Blender runs rewriting `28` tracked
  FBXs, blends and preview PNGs. Seventeen pedestrian manifests also carry a
  stale `generator_version`, which sits inside the build signature, so their
  signatures would move on any rebuild regardless, and
  `CityPedestrianAssetSetup.ValidateDependencyStamp` would then dirty every
  pedestrian prefab. `NpcDesignAppearanceTests` asserts the table's key set
  equals the design ids on disk in both directions, so the duplication cannot
  silently rot while it lasts.
  `NpcHumanV2AssetSetup.RunBatch` is the single asset-authoring entry point
  that rebuilds and validates all seven pipelines. This decision supersedes
  both the former static-shelter exception and older notes that kept ambient
  passengers on a `0.70 m` rig.
- **Accepted — modular humanoid visibility follows the live pose, not the
  model-FBX bind pose:** each character is split into many rigidly skinned
  renderers, while its model and clip banks are separate assets; clips and
  bounded procedural looks can still move parts outside imported per-part
  A-pose boxes. Those boxes therefore are not valid culling envelopes. The
  retained Watcher asset is the historical extreme case, but production no
  longer stretches a cashier across the hall.
  `NpcSkinnedMeshCullingGuard.EnableDynamicBounds` sets `updateWhenOffscreen`
  for every skinned descendant when each registry is configured and once when
  a runtime instance wakes; all seven humanoid asset builders serialize the
  same value. The imported meshes, bind poses and `rootBone` assignments stay
  untouched. The bounded active NPC population makes the extra offscreen
  skinning cost preferable to angle-dependent missing limbs or whole figures.
- **Accepted — The Nightlife arch reuses measured City surface families:**
  fifteen exact imported component names map shell, stairs, terrace, cladding,
  roof, barrel, fuel, bedding and sparse clutter to existing masonry,
  concrete, paving, metal, timber, cloth, paper, enamel and roof albedos.
  The visible underside of the vault belongs to `Shell_Masonry`; the raised
  surface and its vertical mass remain separate `PlatformSlab_Street` and
  `PlatformSupport_Masonry` renderers, so ceiling, top and elevation all
  receive their intended texture family rather than a flat fallback.
  `CityArchShelterSurfaceAppearance` applies metre-scale tiling and compensated
  tints through `MaterialPropertyBlock`, keeps the shared primitive material,
  and deliberately ignores the six fire/spill renderers and all resident rigs.
  The pass is idempotent and adds no material instance or new surface family.

## MVP decisions

- **Accepted — Data-first generation:** A pure `CityLayout` is validated before GameObjects are created.
- **Accepted — Stable local randomness:** Road stages and lot coordinates use stable hashes; Unity global random state is not used.
- **Accepted — Finite connected graph:** Kruskal-style spanning tree plus
  deterministic optional interior loops. For `default-coastal`, every edge
  where its road-grid footprint meets non-road-grid space is appended after
  the seeded graph and access-repair passes as mandatory Street. This preserves
  the existing interior selection while the continuous river-bank roads and
  two road bridges join the west/east perimeters into one outer circuit.
  Legacy and custom blueprints retain their prior graph policy.
- **Accepted — Stable blueprint identity over position:** An immutable
  `CityBlueprint` owns stable area IDs, category, reusable visual archetype,
  placement policy and per-cell topology. Movable urban areas may swap cell
  sets without changing roads; `CityDistrictKind` remains a presentation and
  legacy-system archetype, not canonical area identity. The session persists
  one blueprint ID and seed shared by City and Home.
- **Accepted — Anchored coastal sparse city:** The default blueprint keeps a
  connected `13 x 12` urban envelope with four urban areas and 144 land-use
  cells: an added central river column splits the former `12 x 12` grid while
  the eastern half shifts outward without losing a lot. The 16-cell central
  park becomes two `2 x 4` regions joined by its own footbridge. A full-width
  walkable northern beach and continuous non-walkable water row remain. The
  playable default also extends east
  with a `3 x 2` cemetery, a separate `4 x 2` church precinct immediately
  north of it, and a residual plain `4 x 4` north-east yard (the drained
  former lake block). Cemetery and yard retain the shared open-area
  street-access contract; the church owns one explicit west frontage without
  participating in the pre-MST access repair. The cemetery owns deterministic
  runtime-composed landmarks. Roads, ground, navigation and map
  drawing consume only active cells, so connected holes and non-rectangular
  outlines remain real voids.
- **Accepted — Church is a special precinct with split Blender payloads:**
  `default-coastal` carves `RectInt(13,2,4,2)` from the former eastern Yard
  and keeps the residual Yard rectangular at `RectInt(13,4,4,4)`. Church is
  an appended area/district/surface kind, not a building lot, and selects one
  west frontage after the ordinary graph is established; it never enters the
  pre-MST open-area repair, so adding it cannot reseed the city road network.
  The exterior's runtime `+Z` anchor is `EntranceOutward` and is placed toward
  world west, leaving the altar end east. One deterministic Blender source
  exports separate exterior/interior FBX and Resources prefabs: City loads no
  furnishings, while `ChurchInterior` loads no exterior. Imported models own
  render hierarchy and semantic anchors only; data-first Unity plans retain
  all walkability, barriers, colliders, entry/return points and lights. The
  transition uses appended `EnterChurch`/`ExitChurch` directions and the same
  accepted-request session semantics as the existing doors.
- **Accepted architecture exception — 2026-08-28, explicit user request — the
  cableway carries the player, and the village above it is a real area:** Four
  canon rules banned this outright and are lifted together by one row in the
  story bible's §6 registry. Story bible §15 («Ни дороги наверх, ни канатки, ни
  приезда»), §18 («обещание того, что наверху, до пролога»), §25 («до того, как
  это написано, деревня после пролога в игре не появляется») and art bible §10f
  («любое обещание того, что находится наверху за тросом»). Boarding at the
  mountain terminal opens, the cabin carries the hero in both directions, and
  `GameAreaId.AlpineVillage` becomes the eleventh scene and the eighth gameplay
  root. **What is lifted is the place, not the events:** the prologue still
  opens on a man already at the table and never shows the road up; the mother,
  the dinner and the news from above remain unwritten, so the village ships with
  no scene, no line about her and no way into her house — the door is built,
  lit and politely refuses. The terminal gains no view of the village, no lights
  behind the ridge and no sign naming it: the player boards without being told
  where the cabin goes. `10f` is amended from «Единственная зона за пределами
  города» to name both, and a new `§10g` states the village's form.
- **Accepted architecture exception — 2026-09-01, explicit user request — the
  mother is present, seated, and the chair rocks:** This supersedes the art
  bible's §10g clause «Кресло пусто и неподвижно», story-bible §25's «в доме
  нет персонажа» and the mother's own absence in the §6 registry's level `0`
  row; a new registry row records the lift, and the level-`0` row that stated
  the opposite is amended rather than left standing. She sits in the rocking
  chair from the first visit, at hero-level fidelity on the shared `NpcHumanV2`
  substrate, and is the first NPC to carry the hero's 4×4 facial atlas.
  **What is lifted is her presence, not the unwritten event:** she has no
  name, no line, no reaction to the hero's arrival and no gaze that follows
  him; the Cat, the dinner, the news and every piece of dialogue remain
  absent, and the hero still does not react to her. She is not interactive and
  carries no prompt. The chair's rock is even, slow and indifferent — it does
  not start, stop or change when he enters, because a chair that answered the
  door would be the reaction §25 keeps unwritten. Her age is carried by pose,
  hands and mended clothing; medicines, photographs, the bidon and readable
  text stay on the room manifest's excluded list. **The facial atlas ships
  complete but undriven:** all five expressions exist and `SetExpression` is
  public, and nothing calls it, exactly as `StairwellCatGrin` ships with no
  scheduler by the same author's decision. The room's fixed camera,
  composition, four shots and three audio sources are untouched.

- **Accepted architecture exception — 2026-09-01, explicit user request — the
  mother's house is enterable from the village:** This supersedes only the
  2026-08-28 clause that its lit door refuses and story-bible §25's matching
  prohibition. The already built threshold becomes a two-way residential
  transition to the separate `MothersHouseInterior` gameplay scene and returns
  the hero to a safe point outside the same door. The room is available from
  the first visit as an ordinary, quiet environment. **What is lifted is the
  threshold, not the unwritten event:** the mother, Cat, dinner, news and all
  dialogue remain absent; entering the room neither starts nor promises the
  prologue. Imported Blender geometry owns visuals and semantic anchors while
  pure plans continue to own walkability, collision, camera, light and the
  one-shot return position. The internal threshold is centred on the south wall
  opposite the hearth and owns the north-facing arrival pose. One passive
  authored floor-lamp fixture exposes a typed anchor; runtime lights that anchor
  instead of inventing a ceiling fill. The kettle on the table instantiates the existing
  Kettle Hat pedestrian prefab and exposes its original ten kettle renderers,
  material and atlas rather than copying or remodelling them. Every surface
  authored for the room uses the dedicated `MothersHousePositiveAtlas` instead
  of a Home or City albedo; the exact NPC kettle is the deliberate exception,
  not an environment-sheet dependency.
- **Accepted architecture exception — 2026-09-01, explicit user request — the
  mother's-house fixed camera is lowered:** This supersedes only art-bible
  §10g's acceptance wording that the previously approved camera must not move.
  The camera anchor moves from `(5.8, 3.15, -2.8)` to
  `(5.8, 2.75, -2.8)` so its upper frustum remains below the ceiling slab. Its
  southeast side, target `(-0.2, 0.8, 1.0)`, `60°` vertical FOV and required
  fireplace/windows/table/rocker/sofa composition remain unchanged. The finer
  floor planks alter surface scale only and add no world or story fact.
- **Accepted architecture exception — 2026-09-01, explicit user request — the
  mother's house gains a traversable upper storey:** This supersedes only art-
  bible §10g's one-useful-room and single-fixed-camera wording. The existing
  west sofa remains; its blank back wall opens onto a real straight stair whose
  north end is low and whose south end reaches the upper landing inside the
  summit house's existing opaque envelope. The landing joins a west corridor
  and exactly two separate, accessible, currently unfurnished rooms. The
  visible stair body closes its narrow west seam and continues its solid south
  end to the exterior wall without moving the flight; runtime mirrors that
  south closure with plan-derived collision.
  Blender owns the visible steps, slab opening, guards, walls and reveals;
  pure plans own one continuous hidden walkable ramp, split structural slabs,
  barriers, capsule clearance and height-aware fixed-camera zones. The
  established
  southeast ground-floor shot and its hearth/windows/table/rocker/sofa
  composition remain. This lifts form and circulation only: no mother, Cat,
  dinner, news, dialogue, reaction, room function, family clue, readable text
  or new lore is inferred.
- **Accepted architecture exception — 2026-08-29, explicit user request —
  Alpine Village always carries very heavy snow and very strong wind:** This
  supersedes story-bible §6 level `0` «Ясно», the §12 / art-bible §10g ban on
  «снежная буря», and the implementation's former sheltered `.34–.62` snow and
  `.08–.40` wind bands. The village now holds a `.88` snowfall floor and `.82`
  wind floor, both capped at `1`, while preserving the one deterministic city
  slot, its bearing and short gust rhythm. A village-only `Blizzard` profile
  drives dense diagonal flakes; a second terrain-sampled field carries low
  spindrift and the same normalized wind drives a continuous synthesized bed.
  The ridge remains the visual and physical closure but is not a wind shelter;
  the station canopy is a local dry core and the moving cabin is dry. **What is
  lifted is clear weather, not the village's meaning:** there is no lightning,
  thunder, danger beat, supernatural sign or whiteout; warm lighting, the one
  uphill axis, the top house and the independent warmth/dimming grade remain
  readable and unchanged.
- **Accepted architecture exception — 2026-08-30, explicit user request —
  the village's haze breathes with the gale and the bowl walls loom:** This
  SUPERSEDES the «or whiteout» and «remain readable and unchanged» clauses of
  the 2026-08-29 entry above, and is recorded as a new level-`0` row in the
  story bible's §6 registry with §12 / art-bible §10g amended to match. What
  replaces them: the village's Exp2 haze is no longer a constant but a wave
  keyed on the RAW shared gust rhythm (`GameWeatherRules.EvaluateGust`, the
  same `0.62 + 0.24 sin + 0.14 sin` term `EvaluateWind` already multiplies
  in, extracted so the wind stays bit-identical). The base density is
  `0.017`, chosen against the canon viewpoint — the station pad stands
  `StationSetback 7 m` behind the lane foot, so the mother's door is `7 + 82
  + 2 = 91 m` away and `9 %` of it survives; the peak is `0.045`, at which
  `41 m` is left at `3 %`, so at a gust crest the far half of the lane closes
  for seconds and the top house vanishes. The wave target is
  `SmoothStep(InverseLerp(0.66, 0.86, gust))` smoothed one-pole with a
  `0.5 s` attack and `1.0 s` release on `Time.deltaTime` (the game clock
  advances on the same delta, so a pause freezes rhythm and haze together);
  the trough is guaranteed by construction, and the EditMode simulation pins
  it: every `15 s` window reaches a wave `>= 0.85` AND `<= 0.12`, the lane is
  closed (`wave > 0.5`) for `20-55 %` of the time, and at the RUNNING trough
  the door keeps `>= 5 %` from the platform (about `6 %` in practice). The
  dim end of the warmth grade multiplies the breathing density by `1.55` and
  the product is clamped at the storm peak, so the prologue can never stack a
  second whiteout on the gale's. One writer: `AlpineVillageRoot.Update`
  advances the wave, runs the per-minute lighting pass, then calls
  `ApplyVisibility()` every frame — `RuntimeSceneSetup.ApplyAlpineVillageVisibility(camera,
  warmth, wave)` followed by `AlpineVillageRidgeAppearance.SetHaze(fogColor,
  fogDensity)`, so Scenes hands World what Core just wrote and Core stays
  free of World. The far plane drops `140 -> 110 m`: past the house's back
  wall from the platform (`100 m`) with margin, so the landmark is only ever
  cut by haze, never by the plane; the cableway's hidden-run bounds still
  hold (`157 m` and `233 m` against `120`). **The walls loom:** the current
  oriented bowl puts the ridge toe `15 m` outside the top house's envelope
  (`TerrainMargin 12` + `RidgeStandoff 3`), then climbs at
  `RidgeRisePerMeter 3.6` (`74°`) to `RidgeMaximumRise 60 m`; the crest stands
  `16.7 m` past the toe, closes a mean `34.1°` from mid-lane and reaches `43°`
  on the nearest bearings. The rise is the second submesh of the one ground
  mesh (one `MeshCollider`, two materials), and the cableway valley remains
  rise material rather than a bright floor-material stripe. Its village-owned
  `Shaders/CityMountainPhysical` material carries the breathing haze,
  visibility floor `0.40`, native fog `9/12 m`, tinted snow-shadow
  `(0.31, 0.35, 0.41)` and a stable opaque colour handoff over `96/108 m`.
  `_StableHazeHandoff = 1` replaces moving screen-space clip coverage only on
  this material; the shared shader defaults it to zero, so City keeps its
  existing ordered-dither, depth and depth-normal behaviour. The village
  also selects `_Ps1VertexSnap = 1`, matching the floor's `Ps1Lit` projected
  snap in every ridge pass. Floor and rise therefore share exact toe indices
  instead of duplicating and burying a broad overlap ring. Terrain, rise and
  lying snow all bake the `WindSnow` metre pitch once into world-planar UVs
  and use identity `_BaseMap_ST`; applying the renderer-size transform on top
  was the second source of distant crawl. The shader still has no
  `ShadowCaster` pass, so the `60 m` wall cannot shade the lane. Spindrift
  refuses to be born where the ridge rise exceeds `2 m`.
  **What is NOT lifted:** no silhouette layer, no panorama, no peaks in frame,
  no lightning or thunder,
  no danger beat, no supernatural sign; the uphill axis and the nearest
  houses' walls read at every point of the wave, and the house always comes
  back. This stays inside the bounded-fog decision below (`Accepted — Bounded
  local fog`): it is the per-area Exp2 haze behind the shared field that
  breathes, not a second fog of the zone's own.
- **Accepted — 2026-09-01 implementation follow-up, not an architecture
  exception — the path advises through weather and never commands:**
  `AlpineVillagePeripheralStormPlan` is a pure spatial read over the complete
  lane/path snapshot. It composes distance outside the nearest trodden route,
  a widening station-to-landmark aperture fitted around all four corners of
  the mother's house, and a rear band whose strength grows after the actual
  back wall. `AlpineVillagePeripheralStormField` consumes that sample only to
  place and fade world-space soft particle sheets; it follows the existing
  wind bearing and gust wave but never writes `RenderSettings`, camera range,
  collision, `PlayerMotor`, damage or a walkable mask. After the player steps
  into untouched snow, the field raises its overall visual pressure and also
  biases new sheets toward the player's vicinity; speed and route permission
  stay identical. The aperture keeps the whole
  house clear of this secondary layer from the station, so the accepted
  `0.017–0.045` global fog cycle remains solely responsible for hiding the
  landmark at a gust crest and revealing it again. This adds form already
  allowed by art-bible §10g and changes no story meaning, so the story bible
  and exception registry do not change.
- **Accepted architecture exception — 2026-08-31, explicit user request — the
  cemetery ravens are fauna, not a second Cat:** Story bible §10 reserves
  significant-event animal presence for the Cat, bans «второй кот», and states
  the biconditional by name: «Значимым сюжетным событием считается то, при
  котором есть кот. Если кота нет, событие не значимое, и наоборот». The two
  cemetery ravens invert every term of the Cat's grammar rather than borrow
  it: he is already sitting where the hero arrives, he speaks, and he never
  flees; they arrive only after the hero's own first act of finished work,
  they say nothing and mark nothing, they flee him at three and a half metres
  and return only when he is nearly out of sight. Their perch is habit, not
  significance — the first sealed grave is simply the grave that was there
  when they arrived — and if a scene ever needs an animal's presence to MEAN
  the moment, that is the Cat's work by the biconditional, and the ravens
  leave the frame. §16 laws 1 and 2 hold by name: law 1 («Ни один NPC не
  знает о преступлении. Никто не намекает») is carried onto the crime — the
  ravens keep to an ordinary epidemic grave, never hers; law 2 stays whole —
  no citizen and no system reacts to them, and their own reactions are bird
  habit exactly as the §6 row words them. §18 holds by name: the marked grave
  is whichever one the player happened to seal first, its plaque carries an
  epidemic dead and the player's own eight words, and the pair must read as
  a bird habit, not a memorial sign — §18's Проверка, «игрок ни разу не
  предполагает, что копает её могилу», joins the capture checklist for the
  cemetery frames. §19 is a deliberate refusal: the ravens are NOT added to
  the closed словарь следов, they carry no story, and no trace ever rides
  them. They sit on the same shelf as the village dog behind the fence —
  ordinary provincial fauna with a bounded synthesized voice — and the
  level-`2` §6 registry row lifting §10c «Хоррор» for them states that
  reading in canon, while the companion «весь документ» row regularising
  «свежие раскопанные могилы» for the hero's grave work records a
  contradiction the shipped gravedigging feature had already created.
- **Accepted architecture exception — 2026-08-31, explicit user request — the
  roost ravens multiply the species, not the sign:** sparse pairs of the same
  ordinary wintering ravens across the three outdoor areas STRENGTHEN the
  fauna reading above rather than stress it — a bird that lives at one grave
  and nowhere else is a sign by scarcity; a bird that lives all over a
  coastal town is a species. The selection rule is bird logic, never
  significance: parapet copings, a bridge rail, open gravel, a barge
  gunwale, a road shoulder, a lane-fence perch, with gravel, stone, decking
  or sky behind the bird per §10c's own «не крон» rule — and one third of
  the sites (`5` of `15`: two City dumpster kerbs, the road culvert
  shoulder, the village firewood cart and lane fence) are deliberately
  unremarkable, so «ворон ⇒ важно» cannot be derived from the world. §16
  law 1 is re-checked by name at BOTH ends of the pipe: the водоразбор yard
  AND the часовня над истоком — the place the poison was poured — are
  excluded together with their whole audible radius, so no roost stands on
  anything the crime touches; law 2 stays whole — no citizen, pedestrian,
  bus, cat or system reacts to any of them. §19 stays a refusal: the ravens
  remain outside the closed словарь следов, they carry no trace, and
  removing every raven in the game changes nothing about what the player
  concludes — a roost that failed that test would be «вещь, которая нужна
  только сюжету» and would be cut. One recorded contract hole is accepted
  with this: the AlpineVillage warmth grade quiets only the soundscape's own
  six local voices, so the village roost voices sit outside that grade's
  contract — the grade is pinned at `0` until the prologue exists, and the
  exception is recorded rather than wired. §16's sound test also holds by
  measure: the same synthesized caw now sounds in three areas, but its
  `14 m` audible radius on a `16-44 s` schedule keeps its contribution to
  district distinctness negligible. The cemetery pair keeps its own §6 row,
  its own trigger and its uniqueness: у первой закрытой могилы всегда ровно
  две птицы.
- **Accepted architecture exception — 2026-08-27, explicit user request — one
  maintained Cemetery–Church connection:** The earlier art-bible rule that the
  two precincts have no direct connection is lifted only for one internal
  `3 m` opening. It continues the cemetery's middle cross alley through the
  north fence into a south church path; the west cemetery gate remains its
  only street gate, and the mourner, watchman and grave-work routes continue to
  use it. The church door remains on the west facade and never becomes a door
  in the cemetery fence. Random breaches and any second internal opening stay
  forbidden. The accepted site upgrade keeps the exterior at `0.55` scale,
  changes its west-street setback from `16 m` to `10 m`, and gives the precinct
  a stone forecourt plus a restrained north lawn/garden with exactly two
  benches, two small trees, `6–8` clipped shrubs and modest beds. It adds no
  realtime Light, sound or lore. Data-first ownership is split
  between `CityChurchCourtyardPlan` and
  `CityChurchCemeteryPassagePlan`; the latter owns the fence opening, both
  ground heights, safe shared threshold and capsule-clear route instead of
  treating a missing fence segment as sufficient traversal.
- **Accepted — The coastal basin closes only west and south:**
  `CityMountainBoundaryDefinition` opts in only `default-coastal`; custom and
  legacy layouts receive `CityMountainBoundaryPlan.Empty` instead of acquiring
  scenery from coincidentally named cells. The planner derives flat-shaded
  physical ridges from the stable west/south perimeter Yards and samples every
  toe from `CityTerrainSurfacePlan`. Each cross-section adds an exact toe
  anchor plus a shallow rock lip buried `0.04 m` under the terrain and extended
  `0.08 m` cityward; the renderer and near-toe collider cover the same join
  instead of beginning at the former `0.35 m` outer foot inset. South remains
  one closed skyline: at the river axis the physical mass spans above a low,
  dark `10 m`-wide water mouth and closes both promenade ends against rock.
  Only the water opening itself and the separate portal interrupt the near toe. A
  diagonal south-west strip closes the otherwise empty `(-1,-1)` corner. The
  west ridge tapers toward the northern beach; north remains the sea and east
  is deliberately untouched.
  The physical ridge chunks use one shared opaque `CityMountainPhysical`
  material, MPBs and deterministic `CityMountainRockAlbedo`; only the near toe
  owns collision, while the tall rear mass casts no huge distant shadow. A
  screen-space dither uses horizontal camera distance so the camera-relative
  silhouette yields to physical coverage over `43-31 m` instead of being
  erased by an almost fully fogged opaque depth write. The shader holds a
  restrained `0.10` visibility floor only once native City Exp2 reaches it,
  so rock remains naturally stronger at `9-20 m`, and repeats the identical
  clip contract in `DepthOnly` and
  `DepthNormalsOnly`. Portal frame, physical entry and visual continuation
  remain ordinary
  `RuntimePrimitiveLit` pieces because they are close-range props, not part of
  the silhouette handoff. The throat floor reaches `0.25 m` under the terrain
  edge and sits `0.03 m` above its old plane; the wall planes move `0.04 m`
  outside the portal faces, overlap both floor and ceiling, and the ceiling
  overhangs their outer faces by another `0.04 m`. This eliminates the former
  `0.45 m` ground gap, `0.175 m` coplanar strip and `0.275 m` upper slit. The
  conforming approach terrain, narrow ruts, cross-drain, grounded
  stepped return wings, side service frame and crown floodlight belong to the
  sibling fringe-Yard plan.
  The one portal is derived from `yard-south-west-access`: an approximately
  `8 x 5.5 m` gate-free opening into twelve `6 m` floor/lining chords. The
  first `12 m` remain straight and collidered; their collinear joint meets
  exactly, while later joints overlap only where each chord turns another
  `4°` west. At `40 m` the centreline has already
  left the original opening sightline, and total `72 m` depth also places the
  uncapped end beyond the player's `11 m` navigation plus City's `48 m` far
  plane. The decision plane is intentionally earlier at `8 m`: while
  `TravelAvailable` is false, an inward crossing shows one localized thought,
  walks the normal rig back to `6.5 m` facing inward, and rearms only after
  that retreat. This automatic boundary owns no prompt action, fake `SceneId`
  or transition target; the descriptor is ready for a real handler later.
  Five ceiling fixtures follow the same sampled path. Four are emissive depth
  cues; the second reuses the existing fringe practical Spot, has a `0.22`
  daytime floor and a deterministic sparse two-dip fault. Its exact lens also
  owns the short-range mono ballast buzz and synchronized crackle. Entering the
  physical lining changes the player-following rain to a dry-core shape,
  clears exterior fog particles and hides the camera-relative ridge shell;
  all three restore with mouth hysteresis on exit.
  Because ordinary geometry at the fixed `48 m` plane disappears into City's
  `0.070` Exp2 fog, a separate presentation-only two-layer shell sits at
  `39.4-43.2 m`. `CityMountainBackdropFollower` copies camera translation but
  never rotation, so west/south remain world directions while finite-radius
  parallax cannot expose the shell. Its shader skips Unity distance fog and
  mixes `0.86` toward `RuntimeSceneSetup.CityFogColor`, leaving only a faint
  distant mass whose contrast matches the physical `0.10` floor; the shell
  has no collider, Light, shadow, probe, navigation, map or `CityWorldResult`
  bounds role. It contains only west and south sectors, keeps the southern
  silhouette closed above the low physical river mouth, leaves north and east
  open, and does not change fog, grade or far clip. The schematic map does not
  consume this presentation shell; it consumes
  `CityWorldResult.MountainBoundaryPlan`, expands display
  bounds only at the west/south minima, and renders the physical toe/outer-foot
  hatch, the visible narrow river approach into rock and an uncrossed open
  tunnel arch with only `12 m` of schematic throat. It does not expose the
  hidden cave or the tunnel's `72 m` visual tail as open map space. The layout's
  north/east maxima remain exact.
- **Accepted — Water is a surface the engine does not ship:** Unity has a
  full water system, but only in HDRP; URP 17 has no official water package
  and Unity's own URP samples author water as an ordinary Shader Graph.
  `Assets/Resources/Shaders/CityRiverWater.shader` is therefore hand-written
  HLSL like the project's seven others, and is written as *the* water shader
  rather than the river's: every quantity is derived from world position, and
  `_FlowDirection` is a parameter, so the sea adopted it
  without a second shader. Deriving from world position rather than UV is
  also what makes a segment boundary invisible — two adjacent sheets agree on
  the wave and the ripple because both are functions of where they are.
- **Accepted — The water blends itself:** The surface is queued at
  `Transparent-100` but writes opaque pixels with `ZWrite On` and `Blend Off`,
  compositing against `_CameraOpaqueTexture` and `_CameraDepthTexture` in the
  fragment. Both are already required by `PC_RPAsset`. Blending by hand costs
  one texture read over alpha blending and buys three things: the water never
  sorts against anything, it stays a correct depth occluder for the
  `CityLightHalo` particles that draw at 3000, and absorption can be a
  function of measured water thickness rather than a constant alpha. The
  refracted sample is rejected when it lands nearer than the surface,
  otherwise anything standing in front of the river bleeds into it. Banding
  is applied to the specular, foam and rain terms rather than to the whole
  colour, so the sheets still read through the PS1 composite. One consequence
  is easy to get wrong: because the shader composites its own colour rather
  than being lit, `_BaseColor` and `_DeepColor` are **rendered tones, not
  albedos**. Reusing a flat surface's authored albedo here emits at full
  value what that surface reaches the screen at a fraction of, and the water
  ends up the brightest thing in the city. The same correction is honoured by the
  sea's material, which adopted this shader.
- **Accepted — The wave shades like the mattress, not like water:** The
  displaced grid was invisible because it was honest: 5–9 cm of swell across
  44 m sheets tilts a surface a few degrees, which is nothing at 640×360
  behind a ripple map. The bed's deformable surface solved the same problem
  with `ExaggerateDentShading` (lateral normals ×3.25) and per-cell-quad
  faceting, so the water adopted both in shader form: `_SlopeGain` amplifies
  the analytic slope before the normal is built, and `_FacetStrength` blends
  in the displaced triangle's own flat normal from `ddx/ddy` — the geometry
  stays honest, the light lies louder. CPU deformation itself was rejected:
  the sea is ~22.5k vertices against the mattress's 768, its sheets are
  `UploadMeshData(true)` static, and a mesh-local write would break the
  world-XZ purity that keeps sheet seams and the river mouth invisible.
  The sea's shore fade multiplies the wave by an envelope of world Z, so
  the slope carries the product-rule term `de/dz·w`; dropping it detaches
  the normal from the surface exactly where the surf line is. The
  displacement now lives twice — HLSL and `CityWaterWaveModel` (the bed's
  model-you-can-assert pattern; also what the fisherman's float rides) —
  held identical by a contract test that reads the shader source for the
  shared literals. The basin keeps `_FacetStrength 0`: a faceted normal
  shatters the Morrowind mirror into per-triangle jumps.
- **Accepted — Water light loops must speak Forward+:** The renderer runs
  Forward+ (`PC_Renderer.asset`, `m_RenderingMode: 2`), where URP forces the
  classic `_ADDITIONAL_LIGHTS` keyword off and serves lights through the
  cluster list — a shader that reads additional lights without
  `#pragma multi_compile _ _CLUSTER_LIGHT_LOOP` and a `LIGHT_LOOP_BEGIN/END`
  loop compiles into a variant that never runs (the water's lamp glints were
  dead for exactly this). The cluster macro is textual and demands a local
  literally named `inputData` carrying `positionWS` and
  `normalizedScreenSpaceUV`. `HomeOccluderDither` and `CityRiverWater` are
  the two shaders that do it right; any future lit shader copies them.
- **Accepted — The lighthouse reflects as a virtual lamp:** The island's
  "never a real Light" rule survived contact with reflections: a real point
  light at the lantern would need a ~40 m range and would fight the city's
  fixtures for the light budget. Instead the sea material alone carries the
  lantern as data — position, colour, strength (`_LanternGlint`, zero by
  default and kept there by the river and basin), and a per-frame
  `(sin, cos)` beam azimuth plus the flash half-width cosine, pushed from
  the lantern controller's own `Apply` so the water's sweeping streak and
  the additive cones can never disagree. The shader folds the two opposed
  beams with one `abs(dot)` — `FlashFactorAt`'s `min(Δ, 180°−Δ)` in cosine
  space — and the C# rules class stays the single source of truth for the
  rotation and the 14°.
- **Accepted — The quay lanterns are pool anchors, not permanent lights:** The
  river's waterside lanterns (13 m pitch, both wall faces, lens riding the
  falling water datum) would cost ~36 permanent `Light`s done naively. Every
  wall fixture instead keeps one always-lit emissive lens and fog halo, plus
  an aimed anchor that joins `CityNightAtmosphere`'s nearest-first pool after
  the street masts. Anchors are distinguished **by index alone**
  (`quayAnchorStartIndex`): at night a slot assigned past the boundary takes a
  low wide profile (6 / 10 m / 130° / 70° — the lens hangs ~1 m over what it
  lights, not the mast's 4.7 m) and the anchor's own authored rotation, the
  fringe practicals' convention. The 12-light budget and pool of 8 stay
  untouched; daytime keeps the visible wall bulbs and halos but does not spend
  realtime slots on their secondary spill. With lamps at the water the river
  also turned `_AdditionalSpecular` on (1.2, the fountain's value). Geometry
  note: the iron brackets remain "Waterside Lantern Brackets" and the lenses
  now live in their own "Quay Wall Lamp Glow" batch, separate from the
  night-gated "Promenade Lamp Glow" batch.
- **Accepted — Puddles are the fourth water, and wetness is a material
  uniform:** The gutter puddles left the wet-surface MPB registry and
  became a `CityRiverWater` material (`CityPuddleWaterResources`, the
  fountain-basin recipe: flow zero, facets zero so the mirror holds,
  refraction zero, `_FoamDistance 0.002` pinned *below* the planner's
  3 mm standing depth because edge foam at that scale whitewashes the
  whole patch). Water carries no property blocks — that rule predates
  puddles — so drying could not ride the MPB path: the shader gained
  `_SurfaceWetness`, a whole-material uniform pushed through
  `CityWaterResources.SetSurfaceWetness` from `CityWetSurfaceRegistry`'s
  existing throttled beats, and only materials registered
  `driesWithStreets` receive it (river, sea and basin keep the default 1
  and never dry). Because the shader composites its own background from
  `_CameraOpaqueTexture`, a dry puddle is not a transparent puddle — the
  fragment lerps toward the sampled road, so wetness 0 is pixel-equal
  to no puddle at all under `Blend Off`. The rectangle is hidden by
  `_EdgeNoiseParams`: a world-XZ value noise eats the rim first (rim
  mask arrives as a UV pyramid on the builder's 3×3 patch grid), so a
  drying puddle shrinks to its middle instead of fading as a box. The
  erosion bites `sqrt(wetness)`, not the wetness (2026-08-31): at the
  city's `0.18` drizzle floor the raw form left a quarter-width sliver;
  the root keeps about half the patch, fills it in a downpour and still
  dissolves at zero. Geometry rules (2026-08-31, from a capture): the
  sheet stands `SurfaceOffset 5 mm` over its road and the wave is
  `0.8 mm`, because the trough must clear both the asphalt and the foam
  band (`WaveHeight × 1.73 < SurfaceOffset − FoamDistance`, pinned); a
  gutter patch is rejected when any other street, pavement or marking
  surface samples within 2 mm under its plane (intersection squares run
  the full right of way, so the old inset put half the puddles under the
  kerb slab); open-ground pools ask `CityTerrainSurfacePlan.SampleTop`
  for a level skin (±3 mm at centre and corners) and skip the fringe
  yards, which are terrain without a readable height model; and
  `DepthFadeDistance` is `12 mm`, so the 5 mm body reads a third to a
  half dark instead of 98% road. All
  patches share one combined mesh, one material and one
  `CityFountainReflectionController` cubemap hung over the road-network
  centre — an envmap has no parallax, so one probe serves every puddle
  exactly as it serves the fountain.
- **Accepted — Fixed lamps own their halos; pooled spots carry light
  alone:** An emissive lens is a couple of pixels the ExpSquared fog
  swallows by ~25-30 m, so every fixed lamp builds its own `CityLightHalo`.
  Ordinary street, esplanade and upper-promenade fixtures use
  `CityLightHalo.CreateNightRegistered` and remain dead by day. The low river
  wall fixtures are the deliberate exception: their directly initialized
  halos and separate emissive batch stay full around the clock. The blurred
  ball is the fixture's own; the pooled realtime spots therefore hide their
  travelling halos
  (`CityNightAtmosphere.pooledHaloVisible`, true only for the leased
  fringe practical, which has no static duplicate) so an arriving
  spot never doubles the blob. Street-mast halos stand apart from the
  lamp anchors — the night presentation test pins anchors bare.
- **Accepted — The channel has a floor:** The city deliberately emits no
  terrain under a river cell, so while the water was an opaque lid the
  channel was a hole with a lid on it. `CityRiverWorldBuilder.BuildRiverbed`
  now lays a silt floor `RiverBedDepth = 1.10 m` under the water top plus two
  submerged sides starting `SubmergedSideTop = 0.08 m` down, which laps the
  full quay wall's underside at `0.12 m` by four centimetres. The alternative
  — extending the quay wall skirt — would have re-pinned wall geometry that
  was corrected twice in the same week. Bridge piers now bottom out on that
  floor; they used to stop at the plan datum, a hand's width *above* the
  waterline, which only an opaque surface was hiding.
- **Accepted — The water sheets are not albedos:** `CityRiverWaterNormal` is
  a derivative map stored as `(-dH/du, -dH/dv, 1)` and imported **linear**
  (`sRGBTexture: 0`, the project's first such texture); `CityRiverWaterFoam`
  is a mask. Neither answers to the mean-luminance rule, the compensation
  solve or the channel-ratio bound, all of which describe a diffuse colour
  multiplied by a builder tint, so both live in a separate `waterSheets`
  block of `ArtSource/City/river-textures.json` and bypass
  `CityRiverSurfaceAppearance` entirely — they are set on the water material
  itself. They are still validated for wrap, the one contract they share.
  The fourth albedo, `CityRiverBedAlbedo`, goes through the ordinary path.
- **Accepted — River as typed infrastructure, not a district:**
  `CityRiverDefinition` belongs to the immutable blueprint and declares the
  north-south corridor plus exactly two Road bridges and one ParkPath
  footbridge. `CityRiverPlan` materializes the same contract as a `10 m`
  channel, two continuous `3 m` promenades, distinct `8 m` Works and Mouth
  road decks, a `2.8 m` timber park deck and four lower waterside landings.
  Every bridge carries two footprints: `DeckBounds` is the bank-road to
  bank-road crossing that pedestrians, the map and furniture exclusions read,
  while `SpanBounds` is the structure itself — the channel plus one
  `CityRiverPlanner.QuayEdgeOffset` seat on each quay wall. Deck planks,
  undersides, girders, piers and parapets are built on the span, so no part
  of a bridge stands on an embankment. A crossing is flat by construction —
  both bank nodes resolve to the same elevation — so the street plan's road
  or park path across it tops out at exactly `AverageY + RoadTop`. Bridge
  structure must never share that plane or those side planes:
  `CityRiverWorldBuilder.SurfaceClearance` lifts the timber deck above the path
  and widens it past the path sides, and recesses the road underside inside the
  carriageway sides and ends rather than past them, which would expose the
  parapet post bases that sit on the underside's own top plane. That surface
  is the span too: `CityStreetSurfacePlanner` insets a crossing edge to
  `SpanBounds` instead of half its own width, so the carriageway or path is
  exactly the deck and the promenade paving carries the approach — otherwise
  a crossing runs bank node to bank node and lays `8 m` of road or path over
  each embankment and into the junction pad it shares a plane with.
  Every bridge member is textured as what it is made of: `Iron` for the Works
  crossing, `Quay` for the Mouth crossing, and the park's own `Timber` sheet
  for the footbridge, which belongs to the park's family rather than the
  embankment's three.
  Each road bridge owns one bank-facing stair on both shores; the park bridge
  has no vehicle or lower-landing role. Only the two road bridges enter Route
  01, and bridge-adjacent furniture/spawn exclusions stay derived from the
  declared crossing metadata. World construction, navigation, pedestrians,
  bus routing and the City map consume the same validated plan rather than
  rediscovering the corridor from coordinates. `default-coastal` extends that
  contract south of the core: water and bed continue more than `48 m` behind
  the mountain, while both `3 m` promenades remain walkable from world
  `Z=-156` to physical rock stops at `Z=-182`. The player may walk to either
  stop but cannot enter the cave; it owns no prompt, interaction, scene or
  transition and contributes no walkable area behind the rock.
  The existing `CityMountainRiverNotchDescriptor` type name is retained for
  compatibility, but its expanded data is exposed through
  `CityMountainBoundaryPlan.HasRiverCave`/`RiverCave`. That one descriptor
  drives mountain closure, river materialization, walkable-bank footprints and
  the map; no consumer reconstructs the terminus from coordinates.
- **Accepted — Typed yards are authored edge areas, not boundary voids:** The
  former gaps behind the eastern, southern and western boundary streets are
  five `Yard` areas: one `4 x 6` pocket east of the player's home and four
  one-cell perimeter strips, each halved so it aligns to its own access datum
  on the terraced perimeter. They reuse the open-area contract wholesale —
  one declared street access as the composition anchor, sampled `OpenGround`,
  walkable `OpenLand` and guard rails on unsafe spans — and a separate default-only
  `CityFringeYardPlan` derives deterministic geometry from those same bounds,
  accesses and terrain samples. Four west/south profiles share the grammar of
  an old municipal service belt: graded maintenance trace, drainage,
  retaining work, sparse poles/cables, repair stock and restrained rockfall.
  The complete mountain-facing `OpenGround` skin is split from generic Yard
  ground and receives one quiet measured compacted-fill sheet on its sampled
  terrain mesh. Its depth is deliberately authored as road threshold `0-4 m`,
  working middle `4-14 m` and existing service/toe belt `14-22 m`. A continuous
  collider-free shoulder, three secondary service traces and three or four
  seeded meso compositions per strip make the road-to-rock sequence legible;
  the maximum longitudinal gap between those compositions and either strip end
  is `40 m`. Wherever the toe chapter uses `ServiceTrack`, it is a pair of
  narrow terrain-following traces rather than a broad collider-free surface
  box; the south-east chapter uses its drain instead.
  One macro anchor distinguishes each mountain-facing strip: stepped masonry
  around a stone culvert, a concrete repair frame with winch and pipe stock,
  the readable open-tunnel forecourt with continuous stepped concrete wings,
  a two-post service frame and a crown-mounted floodlight, and caged floodworks
  with a gauge and
  narrow silt-wash cuts. The floodworks has no broad collider-free repair or
  silt platform; every remaining surface trace is at most `0.8 m` wide. Four
  deterministic measured sheets give the quiet forefield,
  service track, board-formed concrete and old masonry independent metre-scale
  reads; rock, silt and iron reuse their existing City families. The
  south-west profile keeps a physical terrain corridor wider than `6 m`, marked
  by a narrow embedded approach trace and paired wheel ruts rather than a
  `6.9 m` visual platform. Its former loose portal blocks are now two continuous
  three-stage concrete returns: their horizontal bases are seated below the
  lowest sampled terrain corner, their iron caps rest on the concrete, the
  two-post service frame stays beside the lane, and the working lamp is fixed
  over the portal crown instead of floating on that side structure.
  The tunnel lane owns its `DriveClearBounds`: generic forefield surface marks
  are clipped out there, while its `3 m` trace segments and consecutive return
  sections meet at exact end planes instead of overlapping coplanarly.
  The other three mountain strips
  reserve straight capsule-clear `6 m` routes from their declared entrances to
  within one player radius of the ridge toe. The two western routes have broad
  gravel aprons; the south-east flood route stays on the continuous forefield
  terrain and is marked only by a narrow embedded trace reaching its drain.
  Retaining modules are split around each corridor rather than deleting a full
  `~16 m` bay. At the ring road, the four
  mountain Yards opt out of single-gate navigation: every frontage interval
  already classified step-safe becomes a real connector, while unsafe height
  changes keep their rails. Custom layouts and the eastern Yard retain the
  ordinary authored-access rule. The east profile instead stays
  low and unlit with a longitudinal service road, drain, poles, locked utility
  masses and broken spoil berm, and never creates an east ridge. Large masses
  collide; tracks, drains, cables and small traces do not.
  Four west/south anchors each expose one small emissive practical to
  `CityNightGlowRegistry`, but the fringe root creates no `Light`. When the
  player comes within `20 m`, only the nearest supported anchor can lease one
  street Spot already owned by `CityNightAtmosphere`: the production pool is
  `1` bar + `10` street + `1` fringe, still `12` total, and returns to `1+11`
  outside the activation radius. The tunnel lease is the deliberate stronger
  exception: a warm shadowless `150`-intensity, `16 m`, `72°`/`40°` Spot is
  moved from the crown to the faulty second ceiling fixture and retains a
  `0.22` day floor. The eastern edge has no practical. A separate mountain
  destination now exists, but this physical City crossing remains deliberately
  unwired; bounded tunnel navigation and automatic refusal are owned by the
  tunnel contracts rather than this fringe plan. The lot and road-grid footprint is
  still normalized to `(0,0)` because every
  per-cell random stream hashes raw cell coordinates; only the
  `OpenLand`/`Water` fringe may reach `-1`. The `(-1,-1)` blueprint cell stays
  absent from canonical surfaces, but the mountain plan now owns one precisely
  bounded city-side earthwork over its former void. Its outer wings retain the
  sampled west/south terrain edges and every diagonal toe station; inside them,
  two ordinary upward ground triangles form a continuous central slope of
  about `16.2°`. There are no stairs, benches, retaining faces or overlaid
  platforms. The closure contributes nothing to `RoadWalkableArea`; the
  ordinary horizontal and vertical map-boundary fence segments both reach the
  exact road corner and form one physical L-shaped barrier. Fence endpoints
  inside a square Street node cap fall back to that node's datum when the
  edge-distance sampler cannot see the cap corner, so the rails stay at road
  height instead of diving toward world zero. The default coastal blueprint
  also authors two `4 m` `CornerGuard` legs at the north-east urban-core road
  cap beside lot `[12,11]`. Their separate purpose is deliberate: active yard
  and waterfront ground lies outside that cap, so generic unsupported-boundary
  subtraction would otherwise remove the local L; the guard does not continue
  along either shore or change navigation. Render and collision
  share the same 20-vertex, 18-triangle soil topology behind that fence. The turning ridge interpolates its
  entire cross-section from the west normal to the south normal, so its toe,
  shoulder, crest and back weld at both endpoints instead of meeting at one
  point. Yards
  are excluded from signature stairs and from bus-stop corner
  eligibility so the canonical city's stairs, Route 01 and home stop do not
  drift.
- **Accepted — The bar-side yard is an inter-building gap, not a fringe Yard:**
  `CityOpenAreaDecorationPlanner` derives the authored composition from the
  bar directly across `PlayerHome`'s shared street frontage, then occupies the
  walkable roadless gap immediately left of that bar, between it and the
  neighbouring supermarket. The dead tree and sparse traces therefore stay
  beside the bar instead of using the large eastern `Yard` precinct; all five
  typed fringe yards remain separate authored edge areas and do not inherit
  the rider, ring or always-on light contract. The rider's circuit is deliberately
  unmarked — no drawn ring — and lives only in the yard site contract that
  the wheelchair plan, the slot clearances and the leaning utilities share. The same pure plan owns one
  stable wall-light descriptor. `CityOpenAreaWorldBuilder` mounts its static
  cold near-white Spot on the supermarket's yard-facing wall at intensity `240`, twenty times the
  ordinary `12`-intensity street practical. Range is the greater of `1.5x` the
  sampled throw and sampled throw plus `3 m`; only `6°` of total cone feather
  keeps the full wheelchair circuit inside the bright inner cone. The one
  source uses hard `0.95`-strength high-resolution shadows, a `4.8x` HDR lens
  and a larger, brighter source halo, but no volumetric beam. It stays enabled
  at constant intensity through day and night and never reads or tracks the
  runtime rider transform. The two `YardDeadLamp` geometry parts remain
  non-emissive and create no `Light`.
- **Accepted — One home-adjacent production bar:** Buildable lots get street
  frontage and bar return points are validated against it. The default coastal
  city creates exactly one Residential bar at cell `(12,6)`, directly across
  the shared frontage from the player home at `(12,5)`. Its stable ID remains
  `bar-01352777-12-06`, its activity dressing remains `SplitTheG`, and the
  three former district bar lots return to ordinary building use. Explicit
  custom multi-bar layouts remain supported; their separation still uses
  weighted street/park-path travel rather than straight-line distance.
- **Accepted — Bar-adjacent player home and fresh spawn:** With at least one
  generated bar, one non-bar building lot becomes the player home. Selection
  first prefers a residential lot across the selected bar's actual frontage,
  placing the default fresh spawn `13 m` from their shared street approach.
  If that placement is unavailable, the deterministic fallback still validates
  a maximum traversable approach distance of `48 m`. The default home mass is
  `8.8 m` tall and its City facade uses the shared third-floor
  balcony/window/door geometry. A fresh city starts on that home's frontage
  node. Bar-free custom layouts retain the central-road fallback and no home.
- **Accepted — One deterministic city supermarket:** After bars, the player
  home, district public places and primary landmarks are reserved, the default
  layout chooses exactly one remaining street-front building lot. Selection
  prefers Residential, then the shortest traversable route from the home, then
  a stable seeded rank. The supermarket cannot also be a bar, home, public
  place or primary landmark; a tiny custom layout may omit it when no ordinary
  eligible lot remains. Its branded storefront, walkable apron, interaction
  trigger, fence opening and return point derive from the canonical lot and
  frontage data.
- **Accepted — One deterministic city elevation plan:** The
  existing 2D blueprint and road topology remain the first generation stage,
  but the default coastal blueprint now creates one immutable
  `CityElevationPlan` before lots and surfaces are spatially materialized.
  Its river branch replaces the former north-profile/east-bias formula: each
  bank starts `1.8 m` above the local water and terrain rises `0.98 m` per
  node away from the channel. Default road nodes therefore span about
  `8.1 m`, peak near `10.08 m`, and retain at least `1.5 m` within every urban
  district. Sea water stays at datum `0`. The river descends
  monotonically from `2.4 m` in the south to the sea datum. Legacy/custom
  blueprints retain the exact flat fallback.
  `CityTerrainSurfacePlan` is the authoritative top sampler for
  `BuildableGround`, `ParkGround`, `OpenGround` and `Beach`. It interpolates
  the surrounding road-node datums across each cell and keeps the road-edge
  plateaus, so adjacent cells form one continuous terrain surface instead of
  stacked slabs with vertical seams; the beach has its own continuous descent
  to the waterline. `CityTerrainSurfaceWorldBuilder` materializes that contract
  as triangulated render meshes with matching mesh colliders, while declared
  special-purpose flat surfaces keep their flat contracts. Building and public
  place foundations extend downward without moving their authored tops. Park
  plazas use closed terrain-conforming meshes; each district public place owns
  an exact flat terrain pad under its slab and a `4 m` smooth blend back to the
  continuous cell, so neither feature is pierced by the surrounding slope.
  All node/cell positions, surface datums, doors, returns, stops, waiting
  slots, pedestrians and debug teleports read the elevation or terrain sampler
  rather than adding an absolute Y.
  Declared bridge decks may cross non-walkable water and river stairs may
  reach their own lower platforms. The two southern promenade extensions are
  ordinary single-level walkable footprints ending against collidered rock;
  the water cave beyond them is not traversable. Traversable tunnels and
  overlapping walkable levels at the same XZ projection remain outside this
  navigation architecture.
  Street intersections and stop pads are level. Between their `4 m` setbacks,
  oriented road/sidewalk strips may grade up to `6%` for Street/Route 01 and
  `8.3%` for pedestrian ParkPath. The bus route excludes non-bus transitions,
  samples Y and grade tangent, pitches without roll and grounds stop/ride
  anchors locally. Every urban district also owns one signature sidewalk
  stair: `6-12` visible collider-free steps, `0.15-0.17 m` rise,
  `0.30-0.34 m` tread, two `1.5 m` landings, physical rails/retaining walls
  and exactly one continuous hidden ramp collider. A grade-safe roadway stays
  beside it for Route 01. One shared `CityRoadGroundBoundaryPlan` classifies
  every road-to-ground seam from its sampled endpoint heights: a span becomes
  a radius-safe walkable connector only while the complete edge stays within
  the `0.28 m` controller step, otherwise segmented physical guards follow the
  actual terrain slope. Ground-to-ground connectors use the same sampled-edge
  predicate. Signature stair footprints are the explicit guarded-cut
  exception. The player
  contact patch raycasts to the live surface normal, and balance checks refuse
  to start above a `12°` surface angle so a stair flight cannot begin a fall
  sequence it was never designed to recover on.
- **Accepted — Data-driven indexed walkable mask:** Player motion is
  constrained to a spatially indexed union of XZ streets, park lawn,
  blueprint `OpenLand` and the complete logical `BuildableGround` regions.
  Radius-safe connector rectangles overlap only road-to-ground and
  ground-to-ground spans whose sampled physical edge is step-safe; every other
  span is physically guarded. The graded physical road, continuous terrain and
  one hidden ramp per stair flight own all climbing. `CityVerticalTraversalAudit`
  inventories every ground seam and authored road frontage, classifies it as
  authorized, unsafe or unclassified, and records deterministic reachability
  from the spawn road component in `CityVerticalTraversalPlan`. Water,
  unmapped cells and space outside the active footprint remain excluded;
  buildings and other visible obstacles are governed by their physical
  colliders instead of being carved out of the macro walkable mask.
- **Accepted — Road v2 deterministic street corridor:** The canonical default
  street footprint is `8 m`, so `CityStreetSurfacePlanner` partitions every
  ordinary street into a dark `6 m` carriageway and two `1 m` sidewalks raised
  from the local datum's `+0.08 m` road top to `+0.14 m`; ParkPath remains
  independent. The
  default grid step is therefore `26 m` for an `18 m` block. The pure plan also
  owns center dashes, an `8 x 8 m` intersection core with a clear `6 x 6 m`
  carriageway apron, intersection corner pavement, radius-query rectangles and
  four-stripe zebra approaches. One shared stable selector chooses at most six
  degree-3+ Street-only intersections that are clear of ParkPath and blocked
  public space, so paired signals and crossings cannot drift. Dashes are
  excluded from intersection and zebra bounds. City builds physical graded
  strips as combined oriented meshes with level node pads; Home consumes the
  same plan in local space without collision. Generation settings reject
  widths that leave no positive
  carriageway between the two sidewalks. Asphalt, sidewalk and white paint use
  three packaged albedos through MPBs on the shared Lit material. Road v2.1 adds
  a second stable selector for eligible Street-only perpendicular two-way
  corners and three- or four-way nodes whose four setbacks have supported,
  building-free ground. At those nodes the four `1 m` corner pads move outward,
  the complete `8 x 8 m` asphalt core is exposed and raised curbs stop `4.5 m`
  short on every real approach, where the pedestrian line continues across a
  flush shared apron. A Road v2.1 node may also own the flat zebra paint and
  paired signal fixtures. Every retained bus maneuver samples its inflated body
  against both actual signal positions with a conservative `0.30 m` fixture
  radius; overlap becomes `StaticFixtureOverlap` and rejects that maneuver.
  Pedestrian corner links follow the displaced pads and the Home reconstruction
  includes every surface whose bounds touch its retained road slice. Ordinary
  intersections retain their `6 x 6 m` clear apron.
- **Accepted — One route-driven real-scale bus on canonical Route 01:** City
  layout produces immutable `bus-route:default-coastal:route-01`, one
  deterministic right-hand, Street-only closed winding service loop. The target
  set contains every district point of interest that actually exists plus
  `PlayerHome`, and its **order is a shortest closed tour over the target
  centres**, not the district enum. The enum order was nominal — Industrial,
  Nightlife, Residential, Old Town, home — and on the default layout it ran
  west, south centre, far north-east, back west and out east again: two full
  crossings, `1166 m` of straight-line tour where `754 m` was available, and a
  `2592 m` road loop. Reordering alone brought that loop to `1798 m`, a `31%`
  cut, without touching a single clearance or right-hand proof. Five targets
  are solved exactly by fixing the first and permuting the rest; a layout with
  more than `8` falls back to nearest neighbour plus 2-opt. Equal-length tours
  are broken by the ordered target IDs, and the cycle is rotated so
  `PlayerHome` is served first with its direction fixed the same way, because
  a closed loop has no last stop to lose and home is the one stop the hero can
  name. The default is therefore Home, Residential, Old Town, Industrial and
  Nightlife. Each semantic stop is assigned to a safe straight on the
  target frontage or one connected road edge away. The river layout preserves
  that Home rule but lets a POI use the nearest cyclic Street in the same
  district, bounded to five grid edges and `120 m` from its public access. Its
  roadside cell differs from the target cell, and the physical blue `01` pole
  stays outside POI public/access bounds or the Home footprint.
  The selected target straights are connected through one deterministic
  accepted-link graph. Ordinary straights and analytic `6 m`-radius left turns
  are retained after full-body surface and signal-fixture clearance. At selected
  Road v2.1 nodes only, a two-edge safe-right macro starts at the incoming
  Street departure, uses a long symmetric S-merge toward the centerline,
  follows a `4.5 m` quarter-turn through the clear core and uses a second long
  symmetric S to the outgoing Street arrival. The macro marks both physical
  road edges occupied so pathfinding cannot use it to bypass a selected stop
  edge. Ordinary unselected `3 m` right turns remain rejected as
  `CurvatureTooTight`. Every retained path is sampled at `0.1 m` against the
  inflated envelope of the real `8.25 x 2.38 x 2.95 m` body, `4.5 m` wheelbase
  and `0.1 m` clearance margin. A physical link may recur in a connector, but
  every ordered route occurrence receives unique link/node IDs and exactly one
  successor. Runtime never chooses a random branch or steers the route toward
  the player.
  The default layout owns five target-derived stops with stable IDs,
  localization keys, lap distances and roadside poses; random roadside
  decoration does not emit bus shelters. The actor serves every stop once per
  lap, clears its service set at the loop seam and holds a fixed `10 s` total
  dwell, including the existing `0.70 s` opening and `0.70 s` closing
  transitions for both doors. Nightlife's Last Route Island has a working
  Route 01 pole nearby but outside its public ground and approaches. The
  abandoned island structures remain a distinct place rather than becoming
  the live pole.
  Runtime owns exactly one reusable actor/model slot. Obstacle-safe activation
  prefers the fixed-fog `76-86 m` band and falls back to `56-86 m` only when
  forward travel on the same loop can approach the player; no spawn is accepted
  when the route is directed away from every encounter sample. Recycling waits
  until the closest point of the complete oriented body is at least `92 m` from
  the player. The slot cap means at most one active or potentially visible
  vehicle, not a guarantee that a bus is always visible. While the hero is
  outside it yields to predicted player and pedestrian motion. That prediction
  extrapolates `0.75 s` ahead, so its input is smoothed over `0.2 s` and
  clamped to the `5.2 m/s` the motor can actually produce: an unsmoothed
  single-frame delta turned a `0.05 m` wobble on a `5 ms` frame into `10 m/s`
  and braked the bus for a phantom six metres away, and a frame that moves the
  hero past `4 m` is read as a teleport and yields no velocity at all.
  Route travel likewise carries any sub-`DistanceTolerance` remainder into the
  next frame instead of discarding it. Dropping it was a latch rather than a
  rounding loss: approach speed rides the service-braking curve exactly, so at
  `60 fps` a frame moves under `0.02 m` once the stop is within `0.31 m`, and
  because the discarded travel left the distance unchanged the speed cap never
  recovered. The bus parked a third of a metre short of the stop node,
  `BeginDwell` never ran, and the dwell-driven doors stayed shut until a long
  enough frame happened along. While the hero
  is attached as the sole passenger, that same hero is omitted from obstacle
  prediction but pedestrian yielding remains active. Camera direction, frustum
  membership and far-clip state never control spawning or recycling. The kinematic box
  body uses the dedicated `CityBus` layer: it collides with the player and
  pedestrians, ignores another bus and is excluded from camera and interaction
  queries. The passive production prefab stays collider- and `Light`-free, owns
  a modeled driver area, dashboard, twelve passenger seats, rails and two
  double-leaf doors, and matches the same real dimensions without runtime scale
  correction.
  The separate passive `CityBusDriver3D` production prefab uses the shared
  `Player3DLit` material and an exact 31-bone rig. Its normal low-poly head keeps
  the slightly bizarre identity in long horizontal eyes rather than distorted
  anatomy. Runtime procedural seated IK keeps both hands aligned to the rotating
  steering-wheel grips. The deterministic door/driver timeline moves only the
  right hand to the physical dashboard button for opening and closing, drives
  its real `12 mm` press travel and leaves the left hand on the wheel. During
  `Opening` the driver turns toward the front door, holds that look through the
  open phase, then returns during `Closing`. The four separate eye renderers
  provide deterministic pooled blinking. A hero within the `2.25-2.75 m`
  front-entry focus band on the door side overrides the static look anchor;
  the driver tracks the hero head and extends the connected neck/head segment
  by up to `0.10 m`, capped at `1.35x`, before restoring position and scale. The
  timeline preserves the fixed `10 s` dwell and `0.70 s` opening/closing
  transitions; driver bones, wheel, button, look and timeline all return to
  neutral when the actor enters its pool.
  Each doorway keeps its outer posts fixed while independently hinged leaves
  rotate in opposite directions around the vehicle vertical and fold inward.
  Presentation inserts one runtime-only `Suspension Visual` pivot above the
  imported body and keeps all four wheel assemblies outside that sprung
  hierarchy. Travel distance and speed drive cartoon road heave capped at
  `0.045 m`, acceleration contributes pitch capped at `0.8` degrees, and
  steering plus the road wave contribute roll capped at `1` degree. The wheels
  remain grounded, while the authoritative actor transform, kinematic collider
  and route pose stay unchanged. Presentation also articulates steering and
  wheel travel, synthesizes a `22050 Hz` engine loop, and scales head,
  tail/brake and cabin emission with motion and current night factor. At runtime
  only, it creates two shadowless headlight `Spot` lights and two soft
  shadowless cabin `Spot` lights under the sprung body. The shared `NightFactor`
  scales them, disables them at zero and resets them off with the rest of the
  pooled presentation. These four bus-owned lights sit outside the fixed
  12-light city-atmosphere pool, so atmosphere plus one active bus reaches a
  16-light subtotal. The single pooled helmet Spot and the fixed yard Spot can
  add one each, making the bounded worst case `18` local realtime lights; the
  scene Directional and transient lightning Directional are separate. The City
  map consumes the same immutable plan, simplifies its closed geometry, draws
  a pale neutral loop below the darker bone-toned player itinerary and adds five
  numbered localized stops in the default layout plus a compact legend. It
  deliberately has no live bus marker.
  The City-only passenger MVP uses three ordinary Default-layer trigger children
  instead of admitting the solid `CityBus` body to general interaction queries:
  one at each front/rear passenger door and one at passenger seat `07`
  (zero-based anchor index `6`), the first window seat on the lateral side
  opposite the driver.
  Both exterior triggers use the ordinary `PlayerInteractor` and
  `InteractionPromptView` E/Enter/gamepad/pointer path. A board prompt is valid
  only while the bus is dwelling with doors fully open and a deterministic
  walkable external dock exists. Each dock keeps the complete player capsule
  outside the bus obstacle corridor, so a waiting passenger does not make the
  bus yield before reaching its service pose. The controller resolves the
  closest valid door-specific plan and retains that front/rear choice for the
  later exit. Every entry and exit candidate resolves its grounded root height
  from the same deterministic `CityStreetSurfacePlan.Sidewalks` bounds used to
  construct physical City geometry: a raised sidewalk uses its `Bounds.max.y`,
  while a cut-back bus-intersection apron uses `RoadTop`, in both cases plus the
  player grounded-root offset. A road-to-sidewalk curb delta is accepted only
  when it fits the live `CharacterController.stepOffset`; the shared positioned
  approach still rejects genuinely unreachable levels.
  `CityBusRideController` acquires an owner-scoped service hold so the `10 s`
  dwell cannot close the doors mid-transfer, then uses the shared
  positioned-interaction path and `BusBoardEnter` to pass through the selected
  live doorway waypoint into that fixed opposite-driver seat. The production
  rig remains
  visible; its pelvis follows the sprung seat Transform during entry and the
  looping `BusRideLoop`. At loop handoff the gameplay root remains under its
  original parent while ordinary motor, `CharacterController` and contact
  shadow are disabled. `CityBusRideController` runs after the director and
  late-synchronizes that root to the stable actor-local seat pose; this preserves
  moving-frame alignment without making Player a child of a slot that may be
  deactivated. A seated camera follows the sprung seat position from a fixed,
  safe aisle-side offset, but derives orientation from actor forward and world
  up so suspension pitch/roll cannot tilt its horizon or couple its axes. Its
  zero-input direction is derived from the selected seat's lateral side and
  looks through the nearest window instead of inward or down. Entry and exit
  rotation blends interpolate look directions and reconstruct against world up,
  avoiding transient quaternion roll. While riding, the controller consumes
  the shared orbit sample: RMB
  mouse movement, the gamepad right stick and the arrow keys rotate bounded
  yaw and pitch in place, while the existing orbit-input flag remains a
  modal-lock gate rather than bus ownership. Entry/exit blends and exact ordinary-camera restoration
  remain fixed-pose transactions.
  The actor records one passenger owner and cannot be pooled or released while
  that owner remains. The director owns a passenger-cleanup callback for forced
  disable/shutdown. The exit prompt is unavailable until the service ordinal is
  strictly greater than its boarding value, which permits the next or any later
  stop but rejects the same dwell. Exiting reacquires the service hold, detaches
  the logical moving-frame binding, freezes the moving seat target and requests
  the independent `BusAlightExit` pose through the same selected live door
  waypoint onto a walkable grounded roadside dock. The camera blends to the
  ordinary resolved chase pose. Normal completion and every
  cancellation/lifecycle path restore motor, collider, contact shadow and
  camera exactly once without Transform hierarchy mutation, release
  service/passenger ownership and use the last safe exterior dock when an
  authored exit cannot finish. Fare/payment, destination selection, passenger
  persistence, traffic-signal simulation and live bus tracking remain deferred.
- **Accepted — A three-place cabin counting the hero, with declared seated
  riders:** `CityBusActor` owns `CabinCapacity = 3` occupants and a shared
  per-owner service hold. Both replace single-owner fields, and both had to:
  with one exclusive hold, an ambient passenger stepping through the doorway
  would silently have made the hero's own `E` prompt fail, and
  `CityBusDirector`'s passenger cleanup would have thrown on its second
  registrant. Cleanup is therefore multicast, while the release
  post-condition is unchanged — no occupant may remain when the presentation
  is pooled. Ambient passengers may take at most `CabinCapacity - 1` places, so
  seat `07` stays reserved and the hero is never locked out of his own bus;
  they fill a stable order of the other eleven anchors biased to the
  driver-side row and rear bench, which are the seats a hero in `07` actually
  sees. The cabin reserves a place logically and lets
  `CityBusRidePlan` refuse a seat index the registry cannot resolve, rather
  than duplicating that check. Recycling keys on `HasPlayerPassenger`, not
  `HasPassenger`: only the hero pins the single actor slot to the world, and
  blocking it for a rider `92 m` away behind fog would strand the bus for a
  whole lap, so ambient passengers are released with it through the same
  cleanup. `CityBusRidePlan.TryCreate` proved agent-agnostic apart from two
  hard-coded facts; parameterising seat index, agent radius and grounded-root
  offset reuses the whole validated dock ladder — including the
  `CityStreetSurfacePlan` curb/apron height resolution — for a walker whose
  root already sits on the pavement surface.
  `CityBusStopWaitPlanner` derives one wait point per stop that has usable
  pavement: the sidewalk centreline `RoadsidePoleOutsideRoadEdge +
  SidewalkWidth * 0.5 = 0.70 m` road-ward of the blue `01` pole, because the
  pole itself stands outside the walkable strip and carries a collider. Two
  slots per stop queue along the lane at `+0.30 m` and `+1.40 m` from the halt
  pose — never abreast, since a `1 m` pavement minus a `0.35 m` agent is the
  same geometry that already rules out passing, and both offsets lie in the
  clear span between the `+3.05 m` front and `-1.34 m` rear door entries. Each
  wait point also owns a single-source Dijkstra field over the pedestrian
  graph. Stops never move, so that search is solved once in the plan instead
  of being re-run every `4 m` the way player guidance must be, and routing then
  reuses the director's existing `approachTarget` and node-distance guidance
  seeded at the stop.
  A newly activated bus seats a seeded `0-2` ambient passengers before it is
  ever seen. It spawns in the same fixed-fog `76-86 m` band the population
  director already proved hides an appearing walker, so there is no visible
  pop, and a bus that has notionally been running its loop for a while does not
  always arrive empty. That preload needs no stop and no dock:
  `CityBusRidePlan.TryCreateSeatedPose` resolves the actor-local seat floor
  from the seat anchor and the cabin-floor door anchor alone, because a full
  ride plan requires a served stop and two validated roadside docks that do not
  exist while the bus is cruising. The static clearance probe is skipped for
  exactly these spawns — the capsule overlaps the bus body on purpose, which is
  what the probe exists to reject everywhere else. The draw spans `0` to
  `MaximumNpcOccupants` inclusive, so an empty bus stays a real outcome and the
  hero's place is never taken.
  `CityBusNpcPassengerController` recruits an ordinary roaming walker within
  `55 m` of a stop along the graph so the hero can watch the whole approach,
  and activates a waiter straight onto its slot only where the stop is already
  beyond the proven `76 m` fog band. Boarding takes the shared hold, runs a
  short scripted doorway walk whose budget is measured rather than assumed: a
  flat constant cannot serve an aisle leg of `1.16-2.56 m` walked at
  `0.72-1.30 m/s`, so it is derived per transfer from the real
  `pavement -> door -> seat` path and that walker's own pace and clamped to
  `3 s` up to one whole dwell. The door is chosen by the whole journey, not by
  which one the walker stands nearer: the doors are `4.39 m` apart on the same
  kerb, and choosing by the outside leg alone sends a passenger `6.60 m` down
  the aisle where `2.56 m` is available. Authored pace is kept rather than
  hurried, because each design owns its cadence and speeding the root reads as
  foot-sliding. An overrun aborts back to the pavement rather than stalling
  the fixed `10 s` dwell; alighting draws a seeded
  strictly later service ordinal, the same rule the hero's exit prompt already
  enforces. NPC transfers deliberately do not use
  `PlayerAnimatedInteractionController`: it is bound to `PlayerRuntime` and
  `IPlayerClipPresentation`, and `ai/contextual-animation-standard.md`
  explicitly does not govern NPC animation. A route-bound walker is exempt from
  the `88 m` pedestrian recycle rule, from distant simulation acceleration and
  from the bus's own pedestrian yielding, which would otherwise make the bus
  stop for its own passenger.
  Seating is one rule for the whole catalog because every design copies the
  hero's exact 31-bone rig at a `0.835 m` rest pelvis: `CityPedestrianPresentation`
  aligns that bone to the cushion anchor, the same technique
  `CityBusDriverPresentation` already uses for the driver. Sole pinning is
  switched off while seated — on a seat it would drag the model down until the
  boots touched the cabin floor — and the mixer gains a third Sit input.
  **Accepted — Declared seated rides over a blanket allowance:** a design may
  ride only by declaring `CityPedestrianArchetype.SeatedRide`, which owns its
  pelvis lift, back offset and headroom, and by owning an authored `Sit` loop
  in the shared locomotion library. The Helmet Lamp Hopper declares none: it
  has no seated posture to author on `0.46 m` hind feet, and its worn Spot is
  the one working light the pedestrian contract allows. A seated clip is
  excluded from the footwear bake, since it leaves the pavement plane on
  purpose, and proves a different contract instead — measured headroom above
  the seated pelvis inside a declared `seated_clearance_m` band, and nothing
  hanging more than the `0.41 m` cushion height below it. The four riders
  measure `0.907-0.918 m` of headroom and `0.375-0.388 m` of
  drop against a `2.05 m` cabin, so the catalog clears the roof with room to
  spare.
  The moving runtime remains City-only. A valid Home/Balcony route would
  require a real Street pass-through whose two complete-body seams both lie at
  or beyond the fog-hidden `56 m` boundary; none exists, and the default home
  facade faces a visible road terminal. Extending that road only for
  presentation would falsify the generated city, while enabling or pooling a
  bus with the Balcony camera would create a visible camera-dependent pop and
  violate the lifecycle contract. Home therefore keeps its pedestrian exterior
  runtime and reconstructs the nearby Home stop as a static collider-free pole,
  but composes no bus actor or director.
- **Accepted — a contextual effect reads its host clip's phase, never its own
  timer:** the seacoast fisherman's pipe ember, its point light and its plume
  are all functions of `SeacoastFishermanPresentation.BreathPhase`, derived from the
  leaning clip's normalized time against a constant that mirrors the authored
  key grid. A second free-running timer would be simpler and is wrong within a
  second of watching him: smoke that swells while the ribs are still filling
  reads as a particle system parented to a man rather than as smoking. The
  same rule is why the plume's lag is expressed as a fraction of a breath
  rather than as seconds. It also constrains what the clip may key: his breath
  moves the spine chain only, because both clavicles hang off the chest and a
  breath authored on them would open his two-handed grip on the rod once per
  lap.
- **Accepted — the second bench sitter duplicates his neighbour's driver
  rather than sharing one:** `ParkCheckersPlayer{Plan,Factory,Presentation}`
  is a near-copy of the chess player's quartet, and that is chosen rather than
  inherited. Every staged character in the library already owns its own
  passive quartet, `ValidatePassivePresentation` alone is duplicated ten times,
  and `CityGameRoot.ParkChessPlayer` is typed on the concrete presentation, so
  extracting a base would edit a shipped, validated character for no
  player-visible gain. The stronger reason is that the two numbers worth
  sharing must not be: `PerchPelvisLiftMeters` and `FocusHeightMeters` are
  measurements off one design's own meshes and one design's own pose, and a
  shared constant that drifted would fail no test — it would merely sink a man
  an inch into his bench. They come out equal here (`0.0651`) only because the
  geometry below the neck was authored identical on purpose, and each file
  names its own source for the value. **A third bench sitter is the point at
  which the extraction earns itself; two is not.**
- **Accepted — the draught takes the 1.75 m envelope lying down:** the
  canonical height is enforced to ten microns on every archetype, so a hat
  cannot simply be lower than the neighbour's to read as lower. The checkers
  player's silhouette therefore inverts on axis and width rather than on
  height: where the king's cross reaches the ceiling standing straight up, one
  thick draught reaches it raked back, spending the whole allowance sideways.
  Its rake is a derived quantity — given the radius and where the piece beds on
  its band there is exactly one angle whose raised edge lands on `1.750`. What
  actually sets the radius is the face, not the read: a bench sitter's head is
  below the player's eye, so a wide plate lying near-horizontal curtains the
  face from the only angle the game offers, and three review renders were spent
  discovering that the radius governs that far more than the rake does.
- **Accepted — two loops on one bench differ in rhythm, not only in phase:**
  the two men are seen together from the park approach, and a phase offset on
  one shared clip gives itself away to anyone who stands between them long
  enough. `CheckersMull` is therefore its own authored clip with a shallower
  breath and its settle at a different point in the lap. This is also forced
  rather than merely preferred: actions are handed to a design by `design_id`
  and `ACTION_BY_NAME` is keyed on the clip name alone, so a shared name would
  either leave the new archetype with nothing baked or overwrite its
  neighbour's entry. The cost is paid back — the perch validator now proves all
  288 frames against this design's own meshes instead of somebody else's.
- **Accepted — a bench sitter is proved against its seat, not against the
  pavement or a cabin:** the park chess player is the art library's first
  design whose idle is seated on world furniture, and neither existing
  grounding rule describes him. The walker rule pins the lowest sole to the
  ground plane, which would drag him down until he stood on the lawn; the bus
  rule (`seated_clearance_m`) measures headroom from the seated pelvis to a
  roof a park bench does not have. He therefore declares
  `perch_seat_height_m`, and the quantity it measures is deliberately not a
  bone position but the distance from the underside of the hip geometry — the
  seat of the coat, which is what physically rests on timber — down to the
  soles. That distance IS the height of the drawn plank (`0.540 m`), so the
  contract can be checked directly against the world instead of against a
  guessed flesh allowance, and the runtime's correction is the same number
  read the other way. Thighs are excluded from the hip measurement on purpose:
  on a high bench they slope down toward the knees, so including them would
  report the knee rather than the seat. The validator additionally reports
  which part reaches the ground, because a seated design has two candidate
  feet and a tucked foot that outreaches the planted one silently describes
  the wrong leg while still passing a height band.
  **Contrast with the removed `perch_clearance_m`:** the fisherman grew a
  seated contract and lost it when he was authored standing, on the rule that
  an unused declared contract is worse than none. This one is used by the only
  design that declares it and is read by the runtime that seats him.
- **Accepted architecture exception — the art bible's prohibitions describe a
  scale level, not an absolute:** `ai/city-story-bible.md` makes the
  supernatural explicit, which `ai/city-zones-art-bible.md` §Статус had left
  deliberately undefined and which several place sections ban outright
  («никакой мистики» at the cemetery, the mourner, the watchman, the fisherman
  and the church). Rather than weaken those bans, they are re-read as
  describing **level `0-1`** of the story bible's `0-5` scale, which is the
  city exactly as it ships today — strange pedestrians included, while the
  active bartender is now an ordinary two-armed man. The explanation the story bible
  supplies is not supernatural at all: **everything strange in the game is the
  hero's, because the game is about his alcoholism.** The citizens are
  ordinary and nobody ever reacts to any of it, which is the single rule the
  player uses to read the world. Above level 1 a ban is lifted only by name,
  dated to a level, in the story bible's §6 registry, and nowhere else; seven
  registry entries exist and the permanent list (skeletons, blood, monsters, sirens,
  cults, otherworld transitions, rust-as-aesthetic, daytime flickering
  lanterns, local fog, screamers) is never lifted at any level. The binding
  constraint is that **every level must still pass all nine of the art bible's
  own §16 acceptance checks**, so a level change alters the behaviour of
  objects already standing and almost never their form. The scale is driven by
  acts, is monotone, and is never displayed. `ai/README.md` carries the story
  bible at status `Planned`; the art bible's §Статус now points at it and
  states the level-`0` rule. This follows the precedent below: a ban is
  replaced by a positive statement of what it was protecting, in writing,
  rather than quietly ignored.
- **Accepted architecture exception — there are pieces on the boards, and
  they are in the starting position:** `ai/city-zones-art-bible.md` §10 listed
  «фигуры на досках» among the things the chess set may not have, next to a
  second player and a third sitter. The reasoning was sound and is preserved:
  a position mid-game states that somebody opposite is moving, which is the
  one thing the whole precinct is built to deny. Both sets are therefore laid
  out untouched, and no runtime path moves a man. The result sharpens the
  emptiness rather than spending it — the plank opposite each of them stops
  being a table nobody plays at and becomes a game nobody started. What the
  ban was actually protecting is now stated positively in the bible: no
  position but the opening, no captured men beside the board, no clock, no
  scoresheet, and no draughts king, because a king is evidence of a game.
- **Accepted — the drawn board's dark parity is derived from the seats, not
  chosen:** both planks face along the chess recipe's `+Forward` and
  `Tangent = (-Forward.z, 0, Forward.x)` runs to the left of anyone facing
  that way, so the near player's right-hand corner is `(file 0, rank 0)` and
  the far player's is `(7, 7)`. A board is correct only when both are light,
  and on an even board a half turn maps corner to corner, so the two share a
  parity and one rule settles both: the dark squares are the odd ones. That
  is also what puts `a1` dark and the white queen on a light `d1`. The recipe
  originally drew the even parity, which is a board that fails at a glance to
  anybody who plays — and which was invisible for exactly as long as the
  boards were empty. `CityChessBoardGeometry` now owns the rule and the five
  numbers the lattice and the men both need; two copies of `0.15` in two
  files is how a set ends up half a square off its own squares.
- **Accepted — the chess set is imported geometry, and the knight is why:**
  everything else this city draws is a box or a stack of them, and a turned
  chess piece could have been stacked cylinders. A knight cannot. Its whole
  job is to be told apart from a bishop at four metres, and it is a drawn
  profile: chest, throat, jaw, nose, the stop under the brow, forehead, poll,
  crest and mane, extruded across four slices with the outer two scaled in.
  The first attempt built it from five rotated boxes and rendered as a flag
  on a pole. Since one design already had to be authored, all seven are,
  which also buys a single triangle budget, a single height-ladder validator
  and one review render where the six silhouettes are checked side by side.
  The runtime cost is nil: `RuntimePrimitiveFactory.CreateCombinedMeshes`
  bakes `56` men into four meshes on the same world-UV contract the box
  batches use.
- **Accepted — the men are imported through a mesh provider rather than a
  prefab, and that is what forces three non-default import settings:** every
  other model in the project instantiates its imported hierarchy, and that
  hierarchy quietly carries two corrections — a `scale = 100` root and the
  Z-up-to-Y-up rotation. Fifty-six GameObjects on a park table is not a thing
  to do, so the meshes are used bare and both corrections have to be baked
  into the file instead: `apply_scale_options="FBX_SCALE_ALL"` and
  `bake_space_transform=True` on export. The third is `isReadable = true` on
  the importer, because `Mesh.CombineMeshes` reads vertices at runtime and an
  unreadable source combines into nothing — silently, in a player build, on a
  board that comes up empty. All three fail invisibly: the model preview
  looks perfect either way, which is why the editor validator asserts imported
  height, origin and readability rather than trusting the art manifest.
- **Accepted — the quarrel's shout is an authored clip, not a procedural bone
  overlay:** the project has both idioms. Every stationary NPC beat
  (`WatchmanWatch`, `MournerMourn`, `BabushkaBeat`, `WeigherCheck`) is a baked
  clip the art build validates on posture and loop closure; separately, four
  files (`BarPatronDrinkingArmPose`, `BarBartenderPresentation`,
  `SupermarketCashierPresentation`, `HomeTeethBrushingArmPose`) carry a
  recorded exception that solves bones in an explicitly ordered runtime
  presentation pass after the base pose. The shout takes the
  first. `CityPedestrianAssetRegistry` exposes only `Head`, `Pelvis` and the
  two feet, so an overlay would have had to find `neck` and `upper_arm.L` by
  name walk, and it would have left the one thing these two designs do out of
  the clip manifest, out of the perch validator and out of every review
  render. The four existing exceptions are also all *continuous* additions to
  a held pose; this is a beat with its own silhouette, which is what a clip
  is for. Cost: a `ChessJeer`/`CheckersJeer` pair, a third optional clip slot
  on the shared registry, and an `~11 MB` rebake of the shared library.
- **Accepted — both men shout with the same unmirrored pose:** the natural
  assumption is that seats facing each other need mirrored turns, and it is
  wrong here. The chess seat is `-seat-a1` at local `(-1.85, -1.10)` facing
  `+Forward`; the draughts seat is `-seat-b2` at `(+1.85, +1.10)` facing
  `-Forward`. Because `Tangent = (-Forward.z, 0, Forward.x)` points to the
  left of anybody facing `+Forward`, projecting the separation onto each
  man's own frame gives the same pair of numbers twice: the neighbour is
  `2.2 m` ahead and `3.7 m` to his **left**. The seats are a 180-degree
  rotation of each other, so each sees the other over the same shoulder.
  Both therefore turn left and both throw the left arm — the arm goes up on
  the side the head went — and `park_jeer` is one pose function used twice,
  in the same way `checkers_player_base_pose()` already returns the chess
  player's body. Two clip *names* remain necessary because `ACTION_BY_NAME`
  is keyed on the name and actions are handed to a design by `design_id`.
  **This is the fact to re-derive if either seat ever moves**, because
  authoring it the wrong way round leaves one of the two shouting into a
  hedge and no test can see it.
- **Accepted — the line appears on the clip's phase, and the panel is
  measured before it is typed:** the shout opens the bubble the frame
  `TauntPhase` crosses `0.22`, the authored full-extension key, rather than
  when the controller decided to shout. That is the fisherman's rule applied
  to a second host, and it is why `CityParkQuarrelController` declares
  `[DefaultExecutionOrder(320)]` — both presentations evaluate their graphs in
  an unordered `LateUpdate`, so a reader of this frame's phase has to be
  behind them. Once open, the typewriter runs on the view's own unscaled
  clock: that is UI time, not world time, and the rule does not reach it.
  The panel is sized once from the whole line and only the drawn substring
  grows, because `GUIStyle.CalcHeight` on a growing string steps the box a
  row taller mid-word.
- **Accepted 2026-09-03 by direct user decision — the cat's quest is dated to
  day two, and dating anything to a day is now a table rather than a
  condition:** the user asked for the first day to carry no feeding check and
  no obstacle to leaving the house, and then for the general base rather than a
  one-off. `GameDaySchedule` is that base: a pure `event -> first day` table
  looked up BY ID, with `GameSessionState.ApplyDayEvent` as the single place a
  row turns into a change in the world. The schedule stays free of a session, a
  scene and a clock, so it can be proved on its own — the split the quest and
  inventory catalogs already use — and lookup is by id rather than by enum
  ordinal because an ordinal-addressed table hands every later row its
  neighbour's data the moment somebody files an entry out of order, silently,
  which this project has already been bitten by in the sound table.
  Events fire **once per session**, tracked in `GameSessionState` rather than
  left to each event to be idempotent on its own. Quest activation happens to
  be safe to repeat; the next event somebody dates will not be, and that is not
  a thing anyone should have to remember. A debug jump backwards does not
  un-fire what already happened.
  Two facts this changes, both recorded where they were stated: the story
  bible's §2 said `FeedTheCat` was «первый квест новой игры», and §9's «Каждый
  день» now begins on the second. Worth keeping: the manual tutorial path in
  `ai/tutorial-scenario.md` has always had the hero descend the stairwell and
  use the street door on day one — which the descent blocker made literally
  impossible. The dating fixes a contradiction rather than introducing one.
- **Accepted — the cat offers no feeding at all before his quest opens:** the
  obvious reading of "no checks on day one" is to leave the descent blocker and
  the tin reservation gated (they already read `IsQuestActive`) and let the
  feeding itself stand. That is a trap. The tin is consumed the frame the cat
  puts his head in it, and `TryCompleteQuest` returns false while the quest is
  not active — so a feeding allowed on day one would eat the can, record
  nothing, and leave the hero locked in his own stairwell on day two with
  nothing to feed the cat and no way down. `StairwellCatInteraction.TryOpen`
  therefore answers with the ordinary «Кот молча смотрит» line and opens no
  menu until the quest exists. **Note what was NOT done:** `FeedTheCat` is
  still not repeatable, so it does not recur daily the way §9's «Каждый день»
  describes. That was already true before this work and is left alone.
- **Accepted 2026-09-02 by direct user decision — every spoken line types out,
  and every letter it writes ticks:** the game had two ways of saying a thing.
  The park quarrel and the mountain cafe typed into a bubble over the speaker's
  head; the watchman, the fisherman and the Ferryman answered whole and
  instantly in the prompt panel. Both are now one mechanism —
  `SpeechDelivery` owns the typing at `34` characters a second and emits one
  keystroke per newly revealed letter, and both views embed it. The user chose
  to KEEP the two channels (see the note below, which stands): an answer to the
  hero stays at the bottom of the screen, and only what he overhears hangs over
  a head. What is unified is how a character speaks, not where.
  **This lifts a prohibition, so it is a §6 registry amendment, not a
  refactor.** The story bible's two mountain-cafe rows said «Реплики не
  озвучиваются и не добавляют AudioSource» and «Муж не получает … голоса,
  AudioSource»; §17 said the same in prose; this paragraph said NPC voice audio
  was deliberately absent. All four are rewritten around one distinction: a
  blip is the sound of a letter being WRITTEN, not a voice. There are no
  phonemes, no words, no intonation and no recording anywhere in
  `NpcSpeechBlipSynthesis` — one triangle, its inharmonic partial, a little
  grit, `45 ms`, quantized to 127 steps like everything else in the village
  one-shot family. What the eight authored profiles in `NpcVoiceCatalog` carry
  is a fundamental and a timbre, which is how two men shouting at each other
  every ten seconds are told apart by ear rather than only by which head the
  panel sits over.
  The literal half of the old prohibition is also kept rather than argued away:
  **no staged prefab gains an `AudioSource`.** `CemeteryWatchmanFactory`,
  `SeacoastFishermanFactory` and `LastRouteFerrymanFactory` each throw if their
  imported model contains one, and those guards are untouched — the sources
  live on the `NpcSpeechVoice` service host and are moved to the speaker's
  position for the length of a line. They are also not `RetroAudioService`'s:
  that pool enforces a per-effect cooldown and a voice cap of one to three, and
  has no per-play pitch, so a keystroke every `90 ms` at a pitch chosen by the
  letter would have been mostly swallowed. A lease is held for a whole line, so
  a keystroke can never steal the voice out from under the line still typing.
- **Accepted — the reveal is stepped once a frame, in `Update`, and the fade is
  a property of the bubble rather than of the view:** the count used to be
  recomputed inside `OnGUI`, which fires several times a frame for layout and
  repaint — fine while nothing depended on the step, and two or three
  keystrokes per letter the moment something did. `SpeechDelivery.Step` is now
  called once from `Update` and `OnGUI` only reads what it produced.
  The opacity moved for a harder reason: `NpcSpeechBubbleView` carried ONE
  `Opacity` for everything on screen, set from outside by
  `CityParkQuarrelController` every frame. That was only ever correct because
  the two speakers it served sit at the same table — two men at different
  distances were not expressible at all. Each bubble now measures its own
  anchor against the listener through `NpcEarshotProfile`, which also owns the
  hard cull the request asked for: past the radius a line is ABSENT, not faint.
  Three presets — `Shout` `11/26/30`, `Conversation` `5/13`, `Room` `8/18` —
  and the third has a measured floor rather than a chosen value: nothing under
  the mountain cafe's own footprint diagonal (`14.0 m`, from the `9.8 x 10 m`
  in `MountainRoadTerminalPlanner.CreateCafe`) can satisfy the §6 registry's
  «внутри физического объёма кафе». **Recompute that floor if the footprint
  moves.** `CityParkQuarrelController` keeps `IsWithinEarshot` and its
  hysteresis: whether the two of them are arguing at all is behaviour, and it
  is not the same question as how solid a line is.
- **Accepted 2026-09-03 — the earshot radii are wider than the first build's,
  and the rolloff starts at the solid radius rather than at the speaker's
  elbow:** the user's report was that the sound cut off too close. Two separate
  causes, and the second was the real one.
  The radii were simply tight (`Shout` `8/22/25`, `Conversation` `3/7`,
  `Room` `4/14`); they are now `11/26/30`, `5/13` and `8/18`. Seven metres is
  four or five paces, so an answer went from full strength to gone in the time
  it takes to turn round, which read as the line being cut rather than left
  behind.
  Underneath that, `NpcSpeechVoice` set every source's `minDistance` to a flat
  `1.2 m`, so Unity's linear rolloff began a stride from the man and ran
  **against** the fade curve rather than with it. The two attenuations
  multiplied: at `Conversation`'s old faint radius the rolloff reached exactly
  zero, so the last third of the fade — the part meant to read as «over
  there» — was silent. `Blip` now raises `minDistance` to the speaker's own
  SOLID radius, so a keystroke holds full strength for exactly as long as his
  words are drawn solid and only then starts falling. **This is the fact to
  re-derive if a profile's radii ever change**, because the two curves have to
  keep their shared shape.
  `CityParkQuarrelController.AudibleRadiusMeters` and `SilenceRadiusMeters`
  were briefly aliases of the `Shout` constants; they are the quarrel's own
  `22`/`25` again. Widening how far a line can be READ must not move the moment
  two men start shouting, and the profile is now deliberately wider than the
  gate on both ends, so they fall silent before their words begin to fade.
- **Accepted — a line nobody said to the hero is drawn over the speaker, not
  in the prompt panel:** `InteractionPromptView` is the hero's own channel —
  what he can do, and what he was just told when he asked. A quarrel he is
  merely standing next to has no business in it, and putting it there would
  also make two men look like they were addressing him. `NpcSpeechBubbleView`
  is a separate IMGUI layer at `GUI.depth = -75`: above the intoxication HUD,
  below the interaction prompt, the city map and the pause menu, so it never
  covers anything the player operates. It is not uGUI/TextMeshPro — that
  would be the project's first `Canvas`, an asmdef reference and a committed
  Cyrillic font atlas, where the built-in IMGUI font already renders all
  `261` Russian entries. It is not world-space geometry either:
  `Ps1CompositeRendererFeature` averages the frame to `640x360` and quantizes
  to RGB555 *before* UI is drawn, so a panel in the world would be crushed
  while this one stays sharp.
- **Accepted — the chess lamp's wire crosses the set rather than running along
  it:** the obvious span is along the line of the two tables, one lamp over
  each board. The park's own geometry refuses it. Trees are planted on an 8x8
  grid and the decoration planner then rejects any chess-set position within
  `4.8 m` of a trunk, which seats the set in a gap between tree rows: on the
  table line the nearest trunks stand about five metres off-axis, so a wire
  between them passes beside the set and hangs its lamp over grass. Across the
  set the same field offers a pair almost on the line. So the span runs on the
  set's forward axis and its one working lamp hangs over the middle, covering
  both boards — which is also the wider circle the fixture was chosen for. The
  knot takes the trunk face nearest the set (`TrunkTieInsetMeters`), which is
  both physically how a wire is tied and worth `0.26 m` of centring. A hook
  pole is the fallback for a seed whose tree field offers nothing on one side;
  it is a fallback, and the authored city is pinned to using real trees.
- **Accepted — a head-in-hands loop breathes on the chest, never on the neck:**
  the exact inversion of the fisherman's rule and for the same underlying
  reason. His hands held a rod carried by his own fist, so his breath was free
  to move the neck and head and had to avoid the clavicles. This design's
  hands hold his head: the skull rides chest -> neck -> head and both palms
  ride chest -> clavicle -> arm, so keying the spine and chest carries all
  three as one rigid piece, while a breath on the neck or head slides his face
  out of his palms once a lap. The settle is small for a second reason the
  fisherman never had — his elbows rest on a fixed board, and every degree at
  the chest slides them about five millimetres across it.
- **Accepted — A staged wheelchair NPC is not a production archetype:**
  `pipeback_roller_v1` is a complete passive presentation asset, but it lives
  under `Assets/Pedestrians/Staged/` rather than `Resources`. Its strangeness
  belongs to the chair's asymmetrical organ-pipe back and breathing bellows,
  not to the rider's disability. The seated rider preserves the exact
  production 31-bone Generic skeleton contract, and the whole model reuses the
  `Player3DLit` material;
  the non-deforming `PIVOT_Wheel.L/R`, `PIVOT_Caster.L/R`, `PIVOT_Bellows`
  and `PIVOT_PipeBank` transforms declare future procedural anchors without
  changing the Avatar. Its current `PipebackIdle` and `PipebackRoll` loops are
  exact 31-bone, in-place skeletal clips: the raised levers frame the hand
  path, the root-bound chair stays planted, and the bellows/pipe load follows
  pelvis/chest motion. No auxiliary pivot curves or distance-driven wheel
  motion are claimed at this staged milestone. The prefab can be inspected and
  sampled through its `CityPedestrianAssetRegistry` and passive
  `CityWheelchairNpcAssetRegistry` bindings without acquiring a runtime actor,
  collider, light, audio source, interaction or persistence.
  Isolation is structural rather than conventional:
  `CityPedestrianResources.OrderedArchetypes` remains the roaming catalog (see
  the promotion note below for the second table beside it, which the Pipeback
  Roller is also absent from), no directory scan discovers staged prefabs, and
  the City/Home pool compositions remain `13` and `8`. Consequently the
  Pipeback Roller cannot
  roam either exterior, wait at a stop or occupy Route 01 merely because its
  imported asset exists.
  **Deferred — production wheelchair locomotion:** registration requires an
  accessible graph that excludes stairs and proves curb/turn clearances, a
  chair footprint instead of the ordinary `0.35 m` pedestrian capsule, and
  wheel-contact presentation that derives independent drive-wheel rotation and
  caster steering from actual motion instead of grounding a pair of shoes.
  Route 01 also needs an explicit accessible boarding and securement design;
  the ordinary pelvis-to-seat passenger transfer is not applicable to a rider
  who remains in their chair. Until those contracts exist, moving the prefab
  into `Resources`, adding it to the catalog or declaring a seated ride is an
  architecture change, not an asset toggle.
- **Accepted — Seven staged residents promoted to the street (2026-09-02):**
  the architecture change the rule above describes, made deliberately and on
  request. Four of the five roaming designs were `bizarre` — the faceless
  Lampshade Walker, the Kettle Hat runt, the Long-Arm Walker and the Helmet
  Lamp hopper — so the ordinary city read as a parade of oddities. The four
  were taken off the street and seven ordinary staged residents took their
  places: the Yard Babushka, the Weigh Attendant, the Cemetery Mourner, the
  Cemetery Watchman, the Lake Fisherman and the two Park players. With the
  Chair Carrier, who was already ordinary and already roaming, the street pool
  is eight.

- **Accepted — Two more designs taken off the street the same day
  (2026-09-02), and the courtyards recast:** the entry above left two things
  standing that the user found by walking the city. The Lake Fisherman roamed
  although he is a story figure with a permanent post on the мостки, and the
  Chair Carrier roamed although a man who carries a chair everywhere is not an
  ordinary man — the user ruled him `bizarre` against the catalog's own
  body-versus-prop line, which still holds for the two Park players. The
  street pool is SIX, and the population profile was deliberately not raised
  to compensate: a thinner street was the accepted price.

  The courtyard vignettes were the other half. `CityCourtyardResidentPlan`
  hard-coded the roaming pool OF THE DAY IT WAS WRITTEN — the Lampshade
  Walker, the Long-Arm Walker and the Chair Carrier — so when the strange
  walkers came off the street the courtyards kept them, and the one place a
  player meets a figure with no face became a residential yard a metre from
  the pavement, at every seed. That was never a story beat: the story bible's
  §6 registry has no row for any of them and none for the pockets. They are
  recast to the three ordinary designs that own a working loop of their own —
  the Cemetery Watchman, the Weigh Attendant and the Yard Babushka.

  Three things fell out of it that are worth naming:

  - **`CityPedestrianClipSource`.** Every promoted resident carries two clip
    pairs: its own working loop and a shared citizen gait. The graph only ever
    built the roaming one, so a body POSED at a dock played a pavement breath
    for ever while its six seconds of authored business sat one field away.
    A placed body now asks for `IdleClip`; everything else still gets
    `RoamingIdleClip` by default.
  - **A phase-seeding bug went with it.** `ConfigureCycle` seeded from
    `registry.IdleClip.length` while the playable had been built from
    `RoamingIdleClip` — for the babushka a `phase x 4.0 s` seek into a `2.0 s`
    clip. It wrapped rather than threw, quietly collapsing the phase spread
    the director asks for.
  - **`NpcDesignAppearanceCatalog` is finally consulted — by a test.** The
    runtime still does not read it and the rule above still holds; but
    `CityCourtyardResidentTests` now asserts that no courtyard resident is
    `bizarre`. Nothing had ever asked the table the question it was written
    for, which is why the leftover survived three weeks of green suites.

  Four consequences worth naming, because each one is a rule that used to hold
  and no longer does:

  1. **The catalog is two tables, not one.** `OrderedArchetypes` is what
     roams; `NonRoamingArchetypes` holds the four that were withdrawn.
     `TryGetArchetype` searches both and answers "can this design be
     resolved"; the new `CityPedestrianResources.Roams` answers "is it on the
     street". The four are NOT dead weight and must not be deleted:
     `CityCourtyardResidentPlan` casts the Lampshade, the Long-Arm and the
     Chair Carrier by name, and `MothersHouseKettleProp` instantiates the
     Kettle Hat walker whole in order to borrow ten of his renderers for the
     mother's teapot.
  2. **`staged` and `pool_eligible` stopped being opposites.** They were the
     same bit inverted, and both `ValidateDescriptorScope` and the manifest
     check enforced that. `staged` still means "authored by the shared art
     library and placed by hand"; `pool_eligible` now means exactly what the
     runtime catalog says, and both editor gates ask `Roams` rather than
     inferring it. The prefab of a design that roams must live under
     `Assets/Resources`; the prefab of one that does not must stay under
     `Assets/Pedestrians/Staged/`. Models and manifests stay staged either
     way.
  3. **A promoted design carries two clip pairs on one prefab.** Its walk slot
     could not simply be rewritten: the babushka's `walk_clip` is
     `BabushkaBeat`, a carpet beaten on the spot with the feet planted, and
     the drying yard plays exactly that. So `ArchetypeSpec`, the editor
     descriptor and `CityPedestrianAssetRegistry` each gained an optional
     `ambient_idle`/`ambient_walk` pair. Staged presentations go on reading
     `IdleClip`/`WalkClip` untouched; the roaming pool reads
     `RoamingIdleClip`/`RoamingWalkClip`, which fall back to the first pair
     when no street gait is declared.
  4. **The street gait is the hero's walk re-authored, not referenced.**
     `CityPedestrianModelImporter` forces `lockRootHeightY` /
     `lockRootPositionXZ` / `keepOriginal*`, baking the vertical pelvis arc
     into the pose, and the hero's importer does not; the pedestrian
     `Animator` also runs with `applyRootMotion = false`. Pointing at the
     hero's own clip asset would therefore produce a walk with a dead pelvis,
     and three editor gates require the clip to live in
     `CityPedestrianLocomotion.fbx` besides. The generator's
     `CITIZEN_WALK_CYCLE` is the hero's eight-key cycle copied key for key,
     with his `target_direction` arm aims re-expressed as X rotations of the
     same magnitude (`25.5°`, `18.1°`, `5.9°`, `12.8°`, read off his own aim
     vectors). It is a recipe merged onto each design's base pose, so seven
     designs share one gait and keep their own coats and posture.

  **Three of the eight ride Route 01, and the five refusals are
  measurements.** Seating aligns the shared rest pelvis to the cushion, so a
  design can sit only if the drop from that bone to the underside of its own
  seated body is a hip: `0.05-0.13 m` for every design that rides. The
  mourner's coat hem hangs `0.4256 m` below it and the babushka's housecoat
  `0.3347 m` — lifting either by its own contact distance floats the body,
  and not lifting it drives the garment through the cushion. The fisherman's
  shouldered rod rises `1.9047 m` above the pelvis, and the park players'
  `ChessJeer`/`CheckersJeer` shout rises `1.19 m`, both past the cabin
  ceiling; the clearance band applies to every seated clip a design owns, not
  only to the one it would ride in. Riders: the Chair Carrier, the Weigh
  Attendant and the Cemetery Watchman.

  **The story bible's concern, recorded rather than resolved.**
  `ai/city-story-bible.md` §14 builds the mortality register on "six to eight
  specific people" — the watchman, the fisherman, the old men at the boards,
  the babushkas, the weigh attendant — and warns outright that with fewer, the
  epidemic would have to be shown through anonymous passers-by. Letting those
  same people wander the whole city dilutes exactly the namedness that section
  depends on. This was raised before the change and the change was confirmed;
  if the register is ever written, the fix is a per-zone spawn weighting that
  keeps each of them common where they belong and rare elsewhere, not a
  reversal of the promotion.

  **Corrected by the bar's explicit composition decision:**
  `BarPatronWorldBuilder` still resolves ordinary city pedestrian prefabs, but
  it now owns a narrow deterministic bar list rather than inheriting the
  roaming order. This does not narrow the street catalog; it only prevents an
  incompatible body or prop from being forced into bar furniture.
- **Accepted — Local player-relative street pedestrians:** City layout and session
  seed produce one immutable, radius-safe graph over sidewalk lanes, junction
  turns and explicit three-link zebra connectors. Recursive 2-core pruning
  removes every reachable dead-end branch before runtime. Long sidewalk links
  expose deterministic spawn anchors. A `CityPedestrianPopulationProfile` owns
  the complete runtime population, so City and the Home balcony scale
  independently instead of sharing one constant: City runs `8` daytime and `3`
  night slots over a `13`-presentation pool, Home runs `5` and `2` over `8`.
  A director-local runtime random stream mixes
  plan seed, activation time and instance identity, then independently samples
  candidate rank, motion/palette values and every delay. The first event waits
  `1.25-7.5 s`. One event activates at most `2` slots, and while the population
  is below its profile target the next event follows in `0.4-2 s`; once the
  target is complete, only replacements remain and the long `3.5-12.5 s`
  cadence returns. Failed searches retry after a randomized
  `0.8-2.4 s`. A slot may activate only at a unique, obstacle-safe anchor
  in the preferred `76-86 m` band from the player. With City's fixed `0.070`
  Exp2 fog, the inner edge
  retains less than `0.2%` scene transmittance even at the widest production
  `70-degree` 16:9 frustum corner after a conservative combined `6 m` camera
  and full visual-envelope depth offset. A nearer ring was evaluated and
  rejected: that proof measures depth *along the view axis* at the frustum
  corner, only `0.574` of the radial distance, so the same bound is not met
  until roughly `72 m` radially — a `44-56 m` ring leaves `16%` transmittance,
  which is a visible silhouette rather than fog. Population size, batch fill
  and forward bias close the street instead of a shorter approach.
  Candidate search collects eligible anchors into reusable buffers and probes
  static clearance on at most `4` sampled picks rather than on every anchor,
  and one `Physics.SyncTransforms` covers a whole spawn batch. A candidate must
  also disperse: it keeps `12 m` from every active walker and at most `2`
  walkers share one sidewalk lane, which is one side of one street edge. The
  fallback ladder drops connectivity before it drops dispersion, because a
  walker farther away still reads as city life while two stacked on one lane
  does not; only the last resort relaxes both. When that band offers only
  disconnected sidewalk components whose closest point remains outside the
  `24 m` encounter radius, selection falls back to a connected obstacle-safe
  anchor at `32-86 m`; the nearer bound remains inside dense fog. A walker
  remains active
  regardless of camera direction or frustum membership and returns to the pool
  only after crossing beyond `88 m` from the player. To prevent daytime
  slots remaining occupied by invisible actors, at most `2` walkers at a time
  follow the eligible non-backtracking continuation with the shortest physical
  graph distance to the nearest player-side node in its connected component
  until they first enter a `24 m` encounter radius. This guidance
  then stays disabled for that spawn so ordinary seeded roaming resumes instead
  of turning into pursuit. Every other walker takes a seeded `50/50` initial
  direction with no player-proximity preference at all, so a busy street shows
  opposing streams rather than a crowd converging on the hero. That shared
  guidance search is one multi-source Dijkstra over the whole graph, so it uses
  a binary heap and recomputes only when the player has moved past `4 m` and
  the nearest node per component actually changed. When the player travels
  faster than `3 m/s` — a bus ride, above all — candidate selection prefers
  anchors in the smoothed forward half-plane, because anything spawned behind a
  `6 m/s` vehicle is outrun before it can be seen; a per-frame jump beyond
  `12 m` is read as a teleport and clears that heading instead of biasing it.
  Player-distance-only simulation acceleration rises
  smoothly from authored pace at `32 m` to
  `2.75x` at and beyond `76 m`; this advances both inward approaches and
  outward recycling without reintroducing a camera dependency. Strict night is
  before `06:00` and from `19:00`: it keeps authored simulation pace, activates
  exactly one walker per event, waits
  `15-35 s` for the first event and `30-70 s` for every later one including
  while still below its own population, and retries
  failed searches after `4-10 s`. Walkers already alive at dusk are not
  culled by the clock and leave only through the same distance rule. Actors
  never reverse at artificial route endpoints: they continue through graph turns,
  avoid immediate backtracking and make one seeded 50% cross/don't-cross
  choice when passing each zebra entry. An explicit stable catalog owns one
  pooled `1.75 m` presentation per design: Lampshade Walker, Chair Carrier,
  Kettle Hat Walker, Long-Arm Walker and Helmet Lamp Hopper. Each copies the production Generic
  Avatar contract while
  directly referencing its own looping in-place `Idle` and `Walk` clips from the
  animation-only pedestrian locomotion FBX. Lampshade clips preserve a hunched
  C-curve and short asymmetric step in both states; Chair Carrier clips balance
  the inverted cafe chair with an upright, precise high-knee gait; Kettle Hat
  clips keep a low stout stance with fast short steps and a constant waddle
  whose belly and kettle roll against each other; Long-Arm clips hold a narrow
  still torso over a slow shuffle while the ground-reaching forearms swing a
  quarter cycle behind the legs and keep a residual sway through idle; Helmet
  Lamp clips take no step at all and hop on both hind feet through crouch,
  launch, a tucked airborne apex and landing. Every
  clip is baked and verified against its own archetype's footwear rather than
  a shared model, and a design may declare an animated hand-to-pavement
  clearance band that footwear grounding alone cannot express.
  **Accepted — Declared pedestrian exceptions over blanket bans:** a walker
  may leave the pavement or wear one working light, but only by declaring it.
  An airborne archetype declares an apex band; its clips receive a single
  constant pelvis lift instead of a per-frame correction, must never penetrate
  and must land at least once. Its root height is still baked into the pose at
  import: the presentation runs its Animator with `applyRootMotion = false`, so
  height left unbaked is extracted as root motion and discarded, taking the hop
  with it. `CityPedestrianPresentation` then lowers the design by one declared
  `CityPedestrianArchetype.GroundTrim` instead of pinning its sole every frame,
  which would flatten the arc. That trim exists because every other walker's
  per-frame pin also silently absorbs the height the shared Generic Avatar
  introduces when it retargets a skeleton whose proportions differ from the
  hero's; an airborne design has no such pin, so the residual is declared and
  tuned by eye. It is deliberately not derived from the clip: a sole's true
  height depends on foot rotation, and the idle and hop clips answer a single
  world-space offset by different amounts, so no measured constant grounds both
  exactly. A lamp-bearing archetype declares exactly one shadowless Spot bounded
  to `7.5 m` and `3.6` intensity, parented to the animated head bone; the
  prefab validator now checks the declared count rather than forbidding lights
  outright, so an accidental extra light still fails. The pool holds one
  instance per design, which is what caps the worn lights in the world at one.
  The director
  selects among free catalog presentations from the spawn seed and applies the
  selected archetype's movement/cadence range; when both day slots are active,
  their silhouettes are therefore distinct. A `0.15 s` locomotion blend avoids
  idle/walk pose pops while the first spawned frame starts directly in Walk.
  Its `CharacterController`
  becomes physical only after a successful spawn and presentation bind, and
  is disabled before pooling. The dedicated `CityPedestrian` layer collides
  with the player but not other pedestrians; camera collision and interaction
  queries ignore it. Stable slot order still owns head-on yielding, but
  yielding is the last answer rather than the only one. A `1 m` sidewalk minus
  a `0.35 m` agent leaves `±0.15 m` of lateral room, and two walkers would need
  `0.70 m` of separation to pass, so walking around each other across the lane
  is geometrically impossible and avoidance works along it instead: a walker
  leans away from an obstruction by up to that `0.15 m` shoulder-shift and
  re-centres itself once clear; one travelling the same way drops to the pace
  of whoever is in front and queues rather than stopping and restarting; and a
  walker that wants to move but does not — pinned against a prop or nose to
  nose with another walker, which are indistinguishable from the actor's side —
  turns back after `1.5 s`. Turning back is the only resolution the pavement
  affords, and it is self-clearing: the node behind offers its other branches
  because ordinary continuation already refuses to backtrack. Walkers retain no
  prompts, persistence or gameplay reactions, and their four muted palettes
  use the shared material through property blocks. Home transforms this same
  graph and navigation mask through `PlayerHomeBalconyGeometry`, filters spawn
  anchors to a bounded `100 m` fog-hidden approach context wholly beyond the
  facade while retaining the existing `48 m` rendered street slice, and enables
  the director only for the Balcony shot as a Home scene-composition boundary.
  The director itself has no camera dependency, while vertical capsule overlap
  keeps the player four storeys above from blocking walkers on the street below.
- **Accepted — Logical terminal road boundary:** A pure planner retains rails
  only along street-union intervals whose outward side is water, unmapped or
  outside the active non-water footprint, plus full-road-width caps at true
  degree-one Street terminals. Degree counts both Street and ParkPath edges,
  so a street continuing into the park is never mistaken for a dead end.
  River promenades count as supporting travel surfaces, and declared bridge
  edges are support-only rather than generic fence sources: the river builder
  alone owns their parapets, trims them before the bank-road pads and preserves
  the four authored stair openings.
  Existing entrance/gate/public/open-area opening descriptors remain metadata
  for decoration clearance. Runtime rails own combined `MeshCollider`s while
  narrow posts remain visual-only; both stay batched in `48 m` chunks.
- **Accepted — First-class open district points of interest:** City land use,
  not the late visual-decoration pass, owns public places. After bars, the
  player home and one buildable primary-landmark cell per urban district are
  reserved, `CityLayoutGenerator` deterministically chooses at most one other
  street-connected lot in each district. It prefers more street sides, then
  greater separation from the district's primary landmark, then a stable
  seeded rank. The default layout yields four: Old Town's waterworks court,
  Residential's drying yard, Industrial's weighbridge and Nightlife's
  last-route island. The authored recipes require both `BlockWidth` and
  `BlockDepth` to be at least
  `CityLayoutGenerator.MinimumDistrictPointLotDimension` (`18 m`); a smaller
  custom layout omits all four safely, and a compact eligible district may
  still omit its place when no safe candidate exists. Each
  `CityDistrictPointOfInterestDescriptor` is the
  canonical stable ID/cell/kind/public-bounds/access contract. Its matching lot
  has `CityLandUseKind.DistrictPointOfInterest`, has no building and cannot be a
  bar, home, park cell or primary landmark. `RoadWalkableArea` includes its
  active ground and approach rectangles, while `RoadFencePlanner` treats the
  complete non-water public surface as support and emits no street-side rail;
  `CityNightFixturePlanner` excludes lamps and signals from the reserved
  ground/approaches. A dedicated world builder creates the
  physical paving, free-standing recipe and intentional solid colliders; the
  bounded Home exterior rebuilds nearby descriptors through the same
  world-to-local transform without gameplay colliders. The last-route island
  owns no emissive recipe parts: its previously unsupported departure board
  meets the paving through two visible legs and feet, while dull route plates,
  layered posters, a waste bin, bottles, a discarded timetable and a lost
  scarf replace the repeated neon-strip treatment.
- **Accepted — Data-first seeded city decoration:** `CityDecorationPlanner`
  consumes the validated layout plus fence and night-fixture plans and emits a
  stable ordered plan without using Unity global random state. Every ordinary
  building lot receives exactly one district silhouette/facade descriptor;
  supermarket and public-place lots are excluded because they own a dedicated
  facade or have no building. Every urban
  district retains one primary building landmark, and the enabled park still
  receives a fountain/statue plus bandstand.
  Optional frontage, roadside and park clusters use per-kind footprint
  clearances around entrances, gates, lamps, signals, trees and benches. The
  ordinary random roadside selector deliberately omits `RoadsideBusShelter`;
  visible Route 01 poles are built from the bus plan, so stop identity cannot
  drift away from service. The 24-family catalog retains the legacy shelter
  recipe for compatibility. Recipes orient to actual road frontage and expand
  as light-free, shadowless visuals in at most six shared-material batches per
  `48 m` chunk.
  A per-kind `None`/`Detail`/`Blocking` catalog adds one to four simple box
  proxies only for grounded structural or bulky recipes; rooftop, hanging and
  small narrative details remain non-physical. Park benches and hedges, the
  home mailbox, plus the lower sections of lamp and signal poles have focused
  static proxies. Home regenerates and filters the same visual descriptors
  after its world-to-local transform and exterior half-space clip, but its
  bounded exterior reconstruction deliberately creates no gameplay collision.
- **Superseded 2026-08-04 — Sprite physical/visual split:** The
  `CharacterController` still stays on the player root, but the former
  camera-facing nine-renderer sprite child has been replaced by the
  collider-free modular 3D prefab and `IPlayerPresentation` contract.
- **Accepted — Explicit scene allow-list:** Only `MainMenu`, `City`,
  `DoorTransition`, `BarInterior`, `SupermarketInterior`, `HomeInterior` and
  `StairwellInterior` install their matching roots. Directly opening
  `DoorTransition` installs an idle presentation root; only the transition
  service initializes and plays it.
- **Accepted — Black startup boundary and one-shot Home opening:**
  `MainMenu` is build index `0` and owns only a black launch camera. After one
  frame it resets the complete run, prepares `HomeArrivalKind.OpeningSleep`
  and Single-loads the existing `HomeInterior`. Home consumes that value once,
  starts the existing bed interaction directly in its sleeping loop, captures
  modal input and holds the first rendered Home frame on a silent `05:59`
  clock. Its complete display flickers briefly at three-second intervals.
  For five seconds no ordinary menu choice or gameplay input path exists; the
  localized PS1-style Wake Up/Quit menu then appears without changing the
  silent, flickering `05:59` display or leaving the clock shot. Wake Up alone
  switches the clock to solid `06:00`, starts the session clock and mechanical
  ring, and hides the menu.
  The camera and persistent sleep loop hold for three more unscaled seconds;
  only when the ring stops does the existing 24-frame exit begin with a `3x`
  duration multiplier (`6 s` instead of the ordinary `2 s`). The camera then
  glides to the sleeper along a `2.25 s` smootherstep quadratic path and eases
  continuously into the active Home shot. It reaches that gameplay pose
  before ordinary Home input, HUD and fixed-camera state return without a
  reload; normal
  Home arrivals never install the opening controller.
  Editor Play pins its start scene to `MainMenu`; the exact temporary
  `InitTestScene{GUID}` bootstrap used by Unity Test Framework suppresses that
  override for PlayMode tests and restores it after returning to Edit Mode.
- **Accepted — One-shot Home F9 entry to the City debug map:**
  `HomeInteriorRoot` always installs `HomeDebugCityMapShortcut`, including for
  the locked opening `ClockHold`. An accepted F9 disables the current motor,
  directly requests `City`, starts the session clock from `06:00` if it is
  still frozen, prepares `CityReturnKind.PlayerHome` and sets
  `DebugCityMapOnArrivalRequested`; a rejected or duplicate transition does
  not mutate that handoff. `CityGameRoot` waits until the transition guard is
  clear, enables `CityMapController` test teleport and then uses a
  success-driven retry window bounded to `2 s` of realtime. It accepts an
  already-open map immediately and otherwise retries `Open` only after both
  the scene transition and the previous scene's `BarMinigameModalLock` have
  released. Success clears the request exactly once; timeout also consumes the
  one-shot and records the final lock, transition and attempt state instead of
  leaking the request into a later City load. The debug branch preserves the
  fresh seed, cash, needs and starter inventory and does not alter the ordinary
  Wake/Quit or Home -> Stairwell -> City path.
- **Accepted — Persistent transition context:** Static subsystem-reset session
  state carries the seed, active bar context, explicit
  bar/home/supermarket city return kind, the next stairwell arrival side and
  the consumed Home arrival kind, one-shot debug-map arrival request and
  current game time/day index
  between Single-mode scene loads. `BeginNewGame` restores all of those values
  together with route, visits, wallet, drinking and balance state.
- **Accepted — Wake-started scaled session clock:** `GameTimeState` resets to
  frozen `05:59`. A successful startup Wake or accepted Home F9 debug skip
  atomically moves it to `06:00` and starts it; later bed interactions do not
  reset or pause it.
  `GameTimeRuntime` persists across Single-mode loads and advances through
  `Time.deltaTime` at `1.0` game minute per real second, making one full `24 h`
  day exactly `1440` real seconds (`24` minutes). Midnight increments a
  zero-based session day index; runtime/UI code exposes its one-based
  `DayNumber`. A persistent top-centre label announces the first Wake and each
  later day change, while inventory keeps `DAY N` beside `HH:MM`.
  The existing F9 window may directly select days `1–7` for testing, changing
  only the day index and preserving time of day, running state and needs; this
  debug limit does not cap ordinary midnight progression.
  Stateful City one-shot audio detects a backward absolute-time jump and
  rebases its schedule cursors and cooldown timestamps at the selected day.
  `GameTimeState.Advance` also returns the actually elapsed
  game minutes so `GameSessionState` advances clock and needs from one delta;
  any owner that sets `timeScale` to zero naturally freezes both.
- **Accepted — Bounded structured diagnostics:** Runtime support logging uses
  one fail-safe UTF-8 NDJSON stream with a versioned envelope, monotonic
  sequence, session/scene/seed context and explicit transition
  correlation IDs. Only state boundaries and results are instrumented;
  per-frame simulation and ordinary Unity log messages are excluded. Editor
  and development runs default to verbose, release players to basic, and
  batch/command-line tests to off. Files rotate at 5 MiB with three retained
  archives, while `F8` writes and flushes a manual state snapshot.
- **Accepted — Separate classic door transition:** Bar, supermarket, home and
  stairwell doors
  first run the shared source-scene `DoorUseEnter/Loop/Exit` action from an
  explicit grounded dock. Its terminal neutral completion alone may reserve
  one transition guard for the complete
  `source -> DoorTransition -> destination` chain. The intermediate scene
  runs a deterministic `3.15 s` unscaled handle/door/camera timeline in a
  black void while the destination loads asynchronously with activation held
  at the preload boundary. Activation is released only after the sequence is
  complete and fully black; a missing presentation root falls back to the
  requested destination. The door opens outward toward the fixed camera and a
  black sprite keeps the revealed doorway opaque; direction changes only the
  warm/cold lighting treatment and does not own persistent gameplay state.
- **Accepted by explicit user decision, 2026-09-04 — every exterior building
  exit leaves the door behind the hero:** Arrival orientation belongs to the
  destination door, not to `DoorTransitionDirection` and not to the source
  scene. `PlayerDoorArrivalPose` combines the destination-owned safe return
  point with the opposite of that door's validated
  `PlayerDoorActionPlan.EntryFacingDirection`. City applies it to bar, home,
  supermarket and church returns; Alpine Village applies the same contract to
  the mother's-house return. It is applied before
  `PlayerCameraFollow.Initialize`, whose initial yaw and collision-aware snap
  therefore put the camera directly behind the hero's outward-facing shoulder
  in the first visible destination frame. Interior-to-interior arrivals and
  fixed interior cameras remain owned by their scene-specific composition.
- **Accepted — Mountain Road is a separate runtime-composed area:** Build index
  `7` starts the hero `6 m` inside a `9 m` exit tunnel and builds one continuous
  `620 m` uphill road ribbon sized against the `4.83 x 1.80 m` LastRouteCar.
  Ordinary width is `4.8 m`; ten `7.5 m`-radius hairpins widen to `6.4 m`.
  The physical climb is `26.1 m`, exactly three times its former rise, and the
  pure route validator caps every sampled grade at `8%`. The climb itself ends
  at `595 m`; the final `25 m` are level, a `20 m` terrace run that carries the
  road clear of the switchback field plus the `5 m` plateau entry lead. They
  enter the unchanged irregular roughly `42 x 27 m` terminal plateau after
  about `238.5 s` (`3 min 58 s`) at `PlayerMotor`'s `2.6 m/s` walk or `148 s`
  (`2 min 28 s`) under continuous `4.2 m/s` run input.
  The terrace run exists because the pad is a raised terrace and the terrain
  sampler snaps everything inside it to the pad height: parked where the climb
  stopped, its rim reached back across the outer arc of hairpin `8` and buried
  that road under `1.5 m` of collidered snow. `MountainRoadValidator` now holds
  the pad off every stretch of climbing road by `4 m` beyond the ribbon's own
  half-width; only its own `UpperApproach` may touch the road. `MountainRoadRoutePlan` owns the ordered samples, all ten typed
  hairpin descriptors and one mandatory bridge descriptor instead of exposing
  two fixed turns. The first five hairpins form the lower forest chapter, the
  last five form the upper climb, and route-relative placement spreads forest,
  roadside misc, snow poles and ridge dressing across the full distance.
- **Accepted — The Mountain Road crosses one real high bridge:** The middle
  chapter contains a straight `50 m` road crossing whose continuous `4.8 m`
  clear roadway sits on a `5.8 m` structural deck with a `0.72 m` slab and
  `1.1 m` physical side rails. The route surface remains continuous at both
  abutments. Terrain sampling ignores the suspended route while shaping the
  approaches and applies a dedicated gorge mask with its floor at world
  `Y=-16`; both deck ends retain at least `25 m` of exposed drop. The deck,
  beams, abutments and repeated rail members are runtime-composed from the same
  validated descriptor. Each open rail leaves a deliberate `2.4 m` midpoint
  viewing gap while a continuous overlapping collider preserves the physical
  barrier. The loose bridge rail replaces the old roadside guardrail as one of
  the five causal sound owners. The map reads the bridge centre and every
  hairpin apex from that same route plan.
- **Accepted — The raised Mountain Road keeps its existing terminal chapter:**
  The road and plateau reuse the same entry vertices, terrain height and open
  lower edge, so neither a visual gap nor a transverse collider lip interrupts
  a future car. A reserved `7.5 m` centre circle keeps the arrival usable by
  that car. The left landmark
  is a same-scene, physically enterable five-sided glass cafe with a `1.6 m`
  open door, exactly two visible practicals and one shadowless technical wash
  Spot with no third visible fixture. Its tableau is a dedicated staged
  subsystem rather than part of the pedestrian pool: one lone patron, one
  couple and one attendant use four distinct Generic prefabs and an isolated
  ten-clip cafe library: one sleeping loop and one interjection for the lone
  patron, two clips for
  each member of the pair and four attendant clips. Seven stools are modeled
  with their seat tops at `0.8175 m`; three are occupied with real butt contact
  and four stay deliberately empty. The lone patron nearest the entrance sleeps
  with his head on strongly crossed forearms stacked without intersection on
  the counter and owns no cup or service
  state. A pure service clock, armed on the player's first entry into the
  cafe's `16 m` entrance radius, holds `18-32 s` Wipe loops, permits at most one
  drink/refill episode and derives two non-overlapping role-local Drink windows
  for the pair. That radius excludes every earlier hairpin; the initial
  `0.44/0.56` fills make the first visible sip cross the refill threshold and
  start a Pour within one minute. Two authored cups visibly drain at different
  rates; below the threshold only the owning cup enters the service queue and
  receives a refill. Completing the sit on
  the hero's stool gives a bounded eye-level first-person view of the counter;
  the owner hides head geometry for the seated interval and restores the exact
  prior camera mode, pose, FOV and cinematic-motion state on exit or teardown.
  The pair's conversation types out and ticks with the game-wide writing blip
  (see the accepted decision below); voice acting, phonemes and free ambience
  are deliberately absent, and the hero is never a cup/refill target or spoken
  addressee; the single physical menu exception is recorded below.
  The
  right landmark is a `230 m` cableway: eight colliderless cabins traverse one
  continuous loop over nine supports, while the far turn stands beyond the
  Mountain Road draw range and the passenger ride cuts to black at `73 m`
  without presenting an endpoint. Cable nodes derive all vertical offsets from
  the raised terminal anchor rather than retaining absolute world heights. The
  cafe remains same-scene; the cableway ride owns its established transition.
  Continuous road/plateau geometry remains the driving collider. A separate
  colliderless asphalt apron at `+0.025 m` overlaps the
  entry by `0.45 m` and exposes the complete R`7.5 m` turning pocket without a
  second physics skin, coplanar patchwork or a shoulder lip. Larger grounded
  forest and roadside misc form the close layer,
  middle ridges carry the perceived elevation, and far snowy mountains close
  the view. Five positional road sound anchors remain causal and attributable
  to visible fixtures or structures rather than free ambience; one tunnel lamp
  shares its visible deterministic flicker with its local sound behaviour. The
  cafe adds three short-range appliance voices, and the cableway adds only a
  visible reducer motor plus roller-crossing clacks. Cafe/cableway map landmarks
  and rain shelter volumes are read from the same validated terminal plan.
- **Accepted and implemented — The terminal cafe is a bounded Blender
  migration, not a rewrite of the whole terminal:** the cafe's visible
  runtime-primitive shell, interior and furniture were replaced by one
  fixed-metre deterministic Blender-authored set and measured manifest (`61`
  meshes / `5,794` triangles / `52` anchors / seven dynamic props). The terminal
  plan continues to own the five-sided
  footprint, `4.4 m` height, `2.8 m` chamfer, open `1.6 m` door, walkability,
  logical collision, shelter, map landmark, semantic/audio anchors and
  lighting; imported geometry brings no collider, Light, camera or material.
  Generator `1.2.1` retains the rear service wall through that same bounded
  asset contract: an extended cabinet and cutting-board dock, a compact stove
  with its pan dock, and a refrigerator cavity with two shelves. The sixth
  dynamic prop remains the separate hinge-ready `FridgeDoor`, with authored
  `FridgeDoorPivot` and child `Grip.FridgeDoor`. It remains closed and has no
  runtime driver, Animator, Rigidbody or attendant/player interaction; the
  existing closed-fridge and service-cabinet boxes remain two of the same
  `17` plan-owned logical colliders. The seventh prop is the colliderless,
  Rigidbody-free open `Menu.Hero`, initially hidden and owning one hand grip,
  one counter dock, one service-rail stop, three item anchors and one selection
  anchor. The napkin dispenser, sugar shaker and salt shaker move together from
  local `Z=-1.20` to `Z=-0.88`, leaving that dock clear. Runtime surface
  dispatch gives `menu_pages` uniform warm paper through the shared material
  property-block path instead of sampling the green band in `CafePropsDetail`;
  no material instance or seventh texture sheet is introduced.
  The counter has seven `0.8175 m`-high stools: the sleeping lone visitor and
  visually grouped pair occupy three with grounded seat contact, four remain
  empty, and the existing hero seat stays on one of those empty positions. The
  attendant remains the fourth silent figure behind the counter. Sitting now
  requests only the menu handoff below; it still creates no drink/food order,
  item, dialogue with the hero or gameplay transaction.
  The two visible practicals plus one shadowless technical sulphur spill proxy
  with no third visible fixture remain the complete three-Light cafe contract.
  The warm key aims at the sleeping contact pose without self-shadowing the
  folded arms across the face. The existing `Light.ColdService` now starts
  inside the visible task fixture over the stove and still holds the
  dark-clothed seated line before its range fade, while the reduced common wash
  keeps the pale attendant from owning the whole shot. The proxy reaches only
  the threshold and near apron, never the terrace or brink.

  **Accepted architecture exception — 2026-09-03, explicit user request — one
  silent physical menu handoff with a selection-only placeholder:** this
  supersedes only the cafe clauses above that forbid every menu and every form
  of hero service. After the hero completes the existing stool sit, the silent
  attendant may carry and place one open menu in front of him. It contains
  exactly three localized ordinary item names with no title, prices or brands.
  `W/S` or D-pad changes only the visible selection; `Space` or gamepad West
  marks it with an `X` and then locks that first choice. Mouse/right-stick
  look and the arrow keys remain free throughout the bounded seated view;
  `E`/`Enter`/gamepad South remains the stand action.
  No product, payment, cash mutation, inventory item, food or drink, line,
  reaction, sound or story state is created, and the pair, husband and
  two-cup refill cycles remain unchanged. This does not authorize another menu,
  other object text, a complete order flow or ownership of the hero
  presentation outside the existing seated view.

  **Accepted architecture exception — 2026-09-03, later explicit user
  request — locked page focus and one physical menu return:** this supersedes
  only the free-look and handoff-only lifecycle clauses of the menu exception
  immediately above. Once the shared service frame exposes
  `HeroMenuPlaced`, `MountainRoadCafeSeatView` remains the sole camera owner
  and blends its fixed pose over `0.45 s` along the ray from the current
  seated camera to the authored page, stopping `0.50 m` from it at FOV `40`.
  The focus looks back along that ray and derives its up axis from world-up,
  so an imported page basis can produce neither an overhead jump nor roll.
  World-space TMP uses its local `-Z` readable face toward that focus and
  aligns glyph-right with camera-right, preventing mirrored or inverted rows.
  Every arrow, right-mouse, right-stick
  and other look source is suppressed while that focus has weight; `W/S`,
  D-pad, `Space`/West and `E`/`Enter`/South remain live, so this is an
  interactive inspection rather than an unbounded cutscene.
  Confirmation preserves the first identifier and visible `X`, immediately
  releases focus back to the saved seated pose and idempotently requests
  retrieval. Standing instead ends the seated view immediately, restores the
  exact pre-seat fixed/follow camera and requests retrieval without a commit.
  The focus lock may never outlive that seated view.
  `MountainRoadCafeServiceTimeline` serializes the request with every existing
  attendant beat as `WalkToMenu -> TakeMenu` (`2.5 s`) `-> CarryMenuBack`.
  `Menu.Hero` remains visible at the counter until the animated grip takes it,
  follows the right-hand socket back and is hidden only after the prop returns
  to its service dock; it is never teleported away at confirm or stand. The
  retrieved state makes the handoff/return one-shot per scene. This later
  exception adds no second menu or text and creates no order, product, payment,
  cash/inventory mutation, food, drink, dialogue, reaction, audio or story
  state. It does not change the pair, husband or two-cup refill cycles. Asset
  and localization contracts remain generator `1.2.1`, `61` meshes / `5,794`
  triangles / `52` anchors / seven dynamic props.

  **Accepted architecture exception — 2026-09-04, explicit user request — a
  closed menu remains with the seated hero in both venues:** this supersedes
  the locked-first-choice, immediate-confirm/stand retrieval and one-shot
  clauses of the two cafe menu exceptions above. `CounterMenuState.Resting`
  separates closing the
  readable spread from taking the physical object. While the spread is open,
  the seated interaction first closes it and returns the saved seated camera;
  the thin closed booklet then remains on the same authored dock. A bounded
  viewer-ray test changes the shared prompt and action between
  reopen while looking at the booklet and stand while looking away; reopening
  restores ordinary menu navigation. Staff may
  enter `Retrieving` only after the shared interaction reports the completed
  visible exit, never on close, confirmation or the start of standing; the next
  completed sit resets the physical round trip and delivers the menu again.
  `MountainRoadCafeMenuController` implements this at the existing single hero
  stool without adding a transaction. `BarCounterStation` applies it at every
  unoccupied bar stool, using identity-safe provisional ownership and
  station-relative service poses; the old single-seat marker and emissive sign
  are not rendered. The bar's successful purchase still commits atomically and
  runs its drink service while the booklet rests closed.
  `CounterMenuPageView` owns one shared fold rig for both venues. Two opaque
  cover/page leaves derive their dimensions, material and colour from each
  venue's authored spread; the left leaf rotates over the stationary right
  leaf along the upper arc about their common physical spine for `0.40 s`.
  Its closed endpoint is `-185.5°`, with an `0.011 m` progressive stack lift
  separating both covers and page blocks instead of leaving coplanar surfaces
  to z-fight. The closed state never uses material alpha. At the fully open
  endpoint the original authored spread is authoritative again. The fold rig
  adds no collider, Rigidbody or second menu authority, so the existing
  bounded gaze target and lifecycle remain unchanged.

  **Accepted exception — role-staggered cafe drinking:** on `2026-09-01` the
  user explicitly replaced the earlier synchronized-pair beat. The pair stays
  grouped in the composition, but its two members own distinct visible fill
  levels, sip amounts and non-overlapping deterministic drink windows. Only
  either of those two cups can enter the attendant's queue after actually
  crossing the refill threshold, so service cannot silently synchronize the
  levels again. The sleeping lone patron owns no cup and never enters this
  clock. This decision updates the cafe contracts in story bible §17 and art
  bible §10f; silence, the closed household loop and the hero's exclusion from
  service remain unchanged.

  **Accepted exception — sleeping lone patron, opposite cup grips and real
  stool contact:** on `2026-09-01` the user explicitly replaced the lone
  patron's coffee loop with a seated sleep: head on strongly crossed forearms,
  one visibly stacked above the other without mesh intersection, on the
  counter, no cup and no attendant service. The other two cup handles face the
  side opposite the previous build, with their authored Grip anchors and hand
  poses refitted rather than leaving the actors to grab empty air. All seven
  stool tops move from the superseded `0.4675 m` dining-chair height to
  `0.8175 m`, while cast roots and stations stay fixed; the visual contract is
  butt-on-seat contact rather than a crouch hovering above a short stool. The
  isolated bank consequently contains nine clips: `1 + 2 + 2 + 4`. This is a
  user-approved change to the cafe tableau, not permission to add dialogue,
  sound, an order or hero service.

  **Accepted implementation — exact cafe contacts and phase-owned silent
  idles:** each member of the pair keeps the cup in the live hand until that
  hand reaches the authored dock grip; release restores the cup at the exact
  centre of its own saucer rather than hiding a bad last frame with an
  independent prop slide.
  The attendant's continuous Walk/Pour carry path must keep the right hand and
  complete coffee-pot geometry clear of the counter volume while preserving
  the already measured spout-over-cup endpoint. The couple's default loops now
  carry the two readable role gestures: the man makes three uneven contacts
  with his free left hand, and the woman raises a visible cigarette for one
  drag and exhale. `MountainRoadCafeCigaretteEffect` reads
  `DefaultClipNormalizedTime` from the same live idle Playable for both ember
  and plume envelopes; it owns no independent timer, Light or AudioSource.
  The man's contacts likewise own no impact sound. This is an implementation
  of the existing silent-cafe contract, not a new story exception.

  **Accepted correction — 2026-09-02, explicit user request — the cafe smoke
  is a mouth exhale, not ember smoke:** the existing phase-owned plume now
  follows the live `SOCKET_Mouth` and emits only after the drag, across the
  authored exhale window. The cigarette ember remains a separate
  drag-synchronized visual. Both effects still read `DefaultClipNormalizedTime`
  and add no autonomous timer, Light or AudioSource. This supersedes only the
  plume origin in the prior cafe-smoking contract.

  **Accepted architecture exception — 2026-09-02, explicit user request —
  private adult banter for the mountain-cafe pair:** this supersedes only the
  cafe's absolute silent/no-dialogue clauses. The unambiguously adult PairMan
  and PairWoman own two initial ten-line localized pools arranged as a fixed
  authored exchange: Man `01`, Woman `01`, through `10`, then repeat. A cue
  peeks rather than consumes that next entry while blocked, so a Drink clip or
  the woman's cigarette lift/drag/exhale delays the same pending speaker and
  line instead of skipping, replacing or reordering it. The service clock
  reserves a long enough Wipe window for turn-in, the four-second bubble and
  turn-out; the man's silent counter tapping is the sole allowed overlap. On
  an actual line the speaker turns the head smoothly toward the other member,
  holds through the bubble and returns afterward. The exchange stays private:
  it never names, addresses, notices or awaits the hero; never exposes plot,
  crime, water or world oddities; and never creates an order, service or
  economy. The sleeper and attendant remain silent. Dialogue adds no
  AudioSource or voice bed, so the three-appliance sound budget and the silent
  tapping/smoking contracts remain unchanged. No signage, menu, price or other
  world-surface text is added. The deliberately coarse sexual language and
  limited profanity are a one-location voice exception, not a global text
  register change; these two pools become the §21 baseline for later additions.

  **Accepted architecture exception — 2026-09-02, explicit user request —
  the sleeping patron is the ignored husband and interrupts after every third
  completed pair exchange:** this supersedes the immediately preceding
  exception only where it leaves the sleeper silent. The lone patron is
  PairWoman's strongly drunk husband; PairMan has already picked her up, while
  the husband does not understand what has happened. One exchange completes
  only after the fully displayed PairMan `NN` line and its matching PairWoman
  `NN` reply. The husband interrupts after exchanges `3/6/9...`; that counter
  continues uninterrupted when the ten-pair pool wraps from `10` to `01`.
  Strictly after the Woman bubble has closed and both pair looks have returned
  to idle, he raises his head from the crossed-arm sleep, waves his right hand
  toward the pair, owns one four-second localized bubble from a separate short
  pool, and returns to the exact sleep seam. His one-shot grows the isolated
  cafe bank from the prior nine clips to ten (`2 + 2 + 2 + 4`). A blocked or
  rolled-back pair bubble does not complete its half of an exchange. The pair's
  pending order and indices remain unchanged: Woman `03` is followed by Man
  `04`, while Woman `10` is followed by Man `01`.
  PairMan and PairWoman completely ignore the interruption: neither receives a
  look target, answer, pause pose or reaction gesture; their ordinary idles may
  continue underneath it. The husband never explains the relationship,
  infidelity or his drunkenness in text, never addresses the hero, owns no cup,
  enters no service queue and adds no voice or AudioSource. The attendant stays
  silent. This is the separate §6/§21 decision required to give a formerly
  silent NPC his first text pool; it does not authorize any other silent NPC.

  **Accepted correction — the cigarette is gripped by its filter, never its
  ember:** the user's 2026-09-02 visual correction makes the non-burning start
  of the cigarette the right-hand grip and points the burning tip away from the
  fingers and face in rest, drag and exhale. The filter still reaches the live
  mouth during the drag. The generator and shipped-asset checks distinguish
  the two ends geometrically; whole-prop/hand overlap alone is no longer proof
  of a valid grip. Ember and smoke remain phase-owned, silent and light-free.

  The user's detailed-texture implementation request accepts one recorded
  exception to the earlier one-sheet target: the set owns six `512 x 512`
  colour-neutral semantic detail sheets (exterior, interior, counter, metal,
  props and glass). They are not six new hue or base-albedo families; they
  partition wear, grime, glass matting and small edge information so unrelated
  elements do not stretch or repeat one sample. Their authored UV regions use
  deterministic offsets/quarter-turns and the generator requires zero broad
  coplanar overlaps. No sheet may contain readable text, `PHILLIES`, `5¢`, a
  logo, price, menu, copied city background or a large pre-painted facade. The
  Nighthawks reference governs only composition and value structure, not
  setting, period, story, exact camera, costumes or poses.
- **Accepted — Every 3D object is assembled in Blender:** new geometry is
  authored by a deterministic generator under `tools/build-*-3d-model.py`,
  exported, and imported as a model asset. It is not composed at runtime
  from `RuntimePrimitiveFactory` boxes and cylinders. The established
  generators set the pattern — player, pedestrians, bus, bus driver,
  bartender, cashier, cat, chess set, Last Route car, church and the two misc
  libraries — and each pairs its script with a measured JSON manifest plus a
  determinism check,
  so a rebuild that changes nothing produces a byte-identical manifest and
  a rebuild that changes something says what.
  The rule governs what is built from now on. It does not condemn what is
  already there: the generated city, every precinct in it, the mountain
  road and its terminal are runtime-composed primitives, and that is by far
  the larger part of the world's geometry. Any migration is a separate,
  deliberate decision about one piece at a time — a task may not quietly
  rebuild a neighbouring system in Blender because it happened to touch it.
  What the rule does change immediately is the answer to "how should this
  new prop exist": in Blender, with a generator, not as another handful of
  boxes.
- **Accepted — Mountain Road misc migrates as a mesh library, not as world
  prefabs:** wave one moves eight of the twelve `MountainRoadPlan.Misc` kinds
  (`102 / 159` default placements) into one deterministic Blender source and
  one FBX containing `19` passive mesh sub-assets. `MountainRoadMiscDescriptor`
  still owns stable ID, centre, rotation, size and blocking intent. The
  provider deterministically selects the three log, four stump and three dead
  tree variants by stable ID; imported parts are combined by kind/material
  role into `12` renderers instead of creating one prefab per placement.
  Colliders remain Unity box proxies and semantic roots retain their exact
  stable IDs, including the loose bridge rail and sounding snow pole. Dead
  trees alone scale uniformly by descriptor height because their descriptor
  X/Z is the trunk envelope while the established branch silhouette extends
  beyond it. Boulders, culvert, utility cable and tunnel lamp are deliberately
  outside this wave: the first already owns bespoke batched geometry and the
  other three have terrain, span or dynamic-renderer coupling.
- **Accepted — City misc is one citywide role-mesh library, not world
  prefabs:** current design `city_misc_citywide_v4` at generator `4.9.0`
  contains `82` semantic kinds, `122` assemblies and `259` role meshes
  (`46,542` triangles) under build
  signature
  `85a8abea90e03d189d069dca36ed5a6f401b1b3fbf08d313dc51ff77ee3a4e21`.
  The provider resolves kind, stable variant and semantic role; the affected
  builders then place or combine those meshes from their existing plans. The
  catalog spans the 24-family decoration layer and parks, street lamps and
  traffic housings, Route 01 shelters/poles, the eastern yard, cemetery,
  seacoast, fringe service belt and the static shells of all four district
  points of interest, plus the compatibility bar, supermarket and player-home
  shells, six shallow Residential courtyard-pocket variants, one unoccupied
  typed-fringe mason-cart scene and the
  church-yard surface/planting kit and the modified cemetery north-fence
  posts/rails. `BarBuildingShell`, `SupermarketBuildingShell` and
  `PlayerHomeBuildingShell` remain addressable but unused, preserving their v4
  compatibility while separate complete exteriors supersede them.
  Per-assembly roots and fixed metre-scale bounds are part of the manifest
  contract. The earlier wave-one and v2 subsets remain frozen
  by compatibility signatures
  `dd2e814d906fd2c7a7855c6d75ee54fe912ebb90f7cd02633c95c558d752f9f6`
  and `8ec3ffe04ffbcfba94cbf708d9c8263afbe853aeea4ffdeabfe638857a043193`.
  Unity still owns world plans, placement, terrain, collision proxies,
  dynamics, interactions, realtime lights and halos, cloth and NPCs. Tilted
  cemetery monuments deliberately stay on the legacy builder because their
  non-rigid tilt is outside the rigid assembly-placement contract.

- **Accepted architecture exception, 2026-08-31 — only the empty mason cart
  remains from the fringe work-vignette pass:** by explicit user decision, the
  hard positive contracts in art bible §10e, §15, §16 and §18.25 no longer put
  one human-scale scene in every typed Yard. `yard-west-north` alone keeps the
  Blender-authored `MasonCart`, always without an NPC. `WinchServiceSet`,
  `TunnelServiceSet`, `FloodMaintenanceSet`, `OpenHoodCar` and every fringe
  resident pose are deleted from their planners, catalogs and generated
  assets. This exception does not remove or redesign the older service-belt
  infrastructure: the west-industrial repair frame and winch, tunnel
  forecourt, floodworks and east utility edge remain their Yards' macro
  anchors. The art bible records the narrowed rule directly; no story-bible
  registry row is needed because the change adds no forbidden detail.

  The old player-home and supermarket three-role assemblies remain catalogued
  for v4 compatibility but are superseded by the complete exterior decisions
  below and are no longer instantiated.

- **Accepted — The canonical bar is one complete fixed-metre pub exterior:**
  design `bar_exterior_v2` replaces both the City misc `BarBuildingShell` and
  generic window bands for the standard City bar and every fully visible Home
  reconstruction. The existing shared bar generator exports this colliderless,
  lightless model at its authored `12.2645 x 13.5237 x 9.3435 m`
  width/depth/height envelope and unit runtime scale. Its two-storey
  late-Victorian urban form owns the masonry/render shell, pitched slate roof,
  unequal chimneys, lower service wing, bottle-green/oxblood faceted shopfront,
  individual sash windows, gutters/downpipes, a fully closed recessed
  door/canopy portal and pictorial tankard as one passive asset. The current
  export is `38` passive meshes / `4,308` triangles. Inner entrance cheeks,
  one recessed flank panel at each outer bay edge and full-depth jamb returns
  form one seam-closure contract: no oblique street view may expose the empty
  shell. It adds no
  country name, brewery text, flag or other
  in-fiction writing.

  Source local `+X` faces the street and the origin remains the gameplay door.
  `CityBarFacadeWorldBuilder` aligns the imported `exterior_door` anchor to the
  unchanged lot door and retains the `sign_pivot`/`Bar Landmark Marker`
  contract. Unity still owns a collider-free foundation skirt with metre-scaled
  box-projected `ExteriorBrick`; its visible front and side faces sit `0.08 m`
  inside the authored shell to prevent coplanar flicker. The separately
  plan-owned logical collider remains full-size. Unity also retains the
  entrance apron, trigger, transition and the single established bar
  light/halo. Home reuses the whole model without colliders
  when it is fully exterior, omits it when hidden and keeps the legacy clipped
  silhouette only for a half-space crossing; imported topology is never cut or
  non-uniformly scaled. The v4 City misc bar shell remains catalogued but is not
  instantiated.
- **Accepted — The canonical supermarket is one complete original
  neighbourhood-store exterior:** design `supermarket_exterior_v1` replaces
  the City misc `SupermarketBuildingShell`, generic apartment window bands and
  runtime-box storefront in City and every fully visible Home reconstruction.
  The fixed-metre passive model keeps the canonical
  `15.5 x 15.5 x 6.4 m` body, source/Unity `+Z` frontage and a centred
  `exterior_door` anchor on the front edge. It owns dark brick piers and
  plinth, rendered service walls, a recessed `1.9 m` double glass entrance,
  four framed bays inside the unchanged `8.4 m` storefront, the unchanged
  `9.2 m` canopy, integrated weathered cream/ochre/bottle-green/burgundy
  fascia, authored `ПРОДУКТЫ` lettering, a modest two-sided blade sign,
  rear service door/louvres/downpipes, parapet, membrane roof and low roof
  plant. The 7-Eleven photograph is a massing/storefront reference only: the
  model contains no copied logo, digit `7`, corporate wordmark, price, slogan
  or exact livery.

  UV ownership follows the thing depicted. `ExteriorWallAtlas` and
  `ExteriorFasciaAtlas` reserve distinct front/rear/side regions and clamp;
  `ExteriorBrick` and `ExteriorMetal` repeat only small physical texture, the
  roof reuses the City roof sheet, and authored glass uses the existing warm
  supermarket family. Fascia stripes are texture regions in the fascia mesh,
  not coplanar plates. Opaque joins omit hidden faces or keep at least
  `0.03 m` relief; the plan-owned visible foundation is inset `0.14 m` on all
  horizontal sides. The FBX contains no collider, Light, Camera, Rigidbody,
  Animator or interaction.

  `CitySupermarketFacadeWorldBuilder` measures the imported anchor in world
  space and aligns it to the unchanged `BuildingLot.DoorPosition`, preserving
  the FBX `100/0.01` hierarchy. Unity retains the full renderer-free logical
  collider, `4.8 m` entrance apron, `5.6 m` fence opening, trigger, stationary
  door action, scene transition and the separate fixed yard spotlight on the
  clear side-wall mount zone. The spotlight planner subtracts the authored
  `0.08 m` wall inset and accepts only a facade normal perpendicular to the
  shop frontage, matching the two declared side-wall zones. The layout owns
  the same fixed `15.5 x 15.5 x 6.4 m` lot contract as the model and omits the
  supermarket when configured buildable blocks cannot contain that footprint;
  it never scales or height-clamps the asset. Full Home placement reuses the
  same unit-scale
  collider-free prefab; Hidden omits it and Crossing alone keeps the existing
  bounds-clipped fallback, so imported topology is never sheared. The old
  CityMisc supermarket shell stays addressable only for compatibility.
- **Accepted — The canonical player home is one complete 209-1-inspired
  exterior:** design `player_home_exterior_v1` replaces the City misc
  `PlayerHomeBuildingShell`, generic window bands, runtime roof/chimney and
  duplicate City balcony geometry. Georgian Series 209-1 is a form reference,
  not a literal reconstruction: the passive model keeps a cold repaired
  two-storey rendered body, dark brick plinth, pitched slate roof, irregular
  framed openings, recessed entry and a deep supported upper gallery, without
  new signage, neighbours, clues or story text.

  `dimensions_m` describes the fixed lot/body contract
  `13 x 12 x 8.8 m`; it is not the complete renderer bounds. Source `+Y`
  imports as Unity `+Z`; the unchanged `exterior_door` anchor is at Unity local
  `(0,0,6)`, and the canonical balcony starts on that front plane and projects
  `2.3 m` outward. Visual bounds are therefore
  `[-6.5,-6,0]..[6.5,8.3,8.8]`. The layout never scales the asset: a custom
  configuration that cannot contain the oriented body or its `8.8 m` height
  omits the home designation. Unity retains an `0.08 m` inset textured
  foundation, body-sized renderer-free logical collider, walkway, mailbox,
  entrance lamp, number `7`, beacon, trigger and transition.

  Nine semantic sheets separate primary/repaired stucco, brick plinth, slate,
  painted wood/metal, window frames/glass and concrete. UVs are authored per
  element or repeat only physical microtexture; openings remain separate
  geometry and broad opaque overlays keep at least `0.03 m` relief. Exactly
  one `WindowGlass` part is emissive: the upper street pane immediately left
  of the balcony when viewed from outside; every other pane is dark. The Home
  scene keeps its existing physical deck/guards/camera/smoking contract and
  rebuilds the same materials, exact visible window positions and recessed
  entry around it, so City and Home no longer draw competing balcony shells.
  **Accepted camera-specific omission, 2026-09-02:** the bounded Home view does
  not rebuild the authored model's narrow `Front Eave Fascia`. At Home-local
  `y = 2.19 m` it crossed both the fixed balcony and smoking shots as a long
  foreground beam. The City asset retains its street-scale fascia, while Home
  retains the pitched roof slab; collision, shelter and the exterior
  silhouette contract are unchanged.
- **Accepted — Ordinary buildings use semantic fixed-metre district prototypes:**
  design `city_buildings_prototypes_v2` supersedes v1 as one deterministic,
  fixed-metre Blender source with four district grammars: Old Town's
  `FragmentedPerimeter` at `14 x 13.5 x 42 m`, Residential's
  `SetbackCourtyard` at `11.5 x 11.5 x 40 m`, Industrial's
  `LowWideProcess` at `14 x 13.5 x 36 m`, and Nightlife's `TallDense` at
  `12.5 x 12 x 48 m`. Those envelopes fit the production default's minimum
  district footprints and current district height bands without runtime
  scale. Every prototype owns exactly seven passive semantic meshes —
  `FacadePrimary`, `FacadeSecondary`, `Plinth`, `Roof`, `Metal`,
  `WindowFrame` and `WindowGlass` — for `28` meshes and `3,642` triangles in
  total, with a hard `3,500`-triangle cap per prototype. Generator `2.0.0`
  locks that catalog under build signature
  `7670234e09fcc68bdebc985d04b0e74810f3e0f4e2f8ad11e840b1c75650ef53`.
  Source `+Y` is the authored frontage and imports as Unity `+Z`; the origin
  is the footprint centre on the ground. The FBX carries no imported material,
  collider, light, camera or animation assets.

  UV0 is owned by surface meaning rather than by the whole object. The two
  facade roles use a four-column `Front`/`Rear`/`Left`/`Right` atlas with one
  non-repeating vertical span; every authored plinth face consumes the complete
  non-repeating `0..1` sheet; roof, metal and frame use physically scaled metric
  projection; each glass face remains pane-local `0..1`. The deterministic
  surface pipeline emits exactly `24` sheets — six opaque roles per district.
  Facade and plinth sheets clamp, metric micro-materials repeat, and no window,
  aperture, sign, text or lore is baked into an opaque texture.

  Unity wraps the four roots in passive Resources prefabs and binds them
  through `CityBuildingAssetProvider`. Each `CityBuildingAssetRegistry`
  preserves the fixed envelope, front anchor, roof and four facade attachment
  bounds, plus `194` explicit window slots. `CityWorldBuilder` now selects the
  matching wrapper for every ordinary lot, rotates authored `+Z` to the lot's
  frontage, aligns `FrontAnchor` to `DoorPosition + 0.08 m`, and never changes
  scale or the imported hierarchy. A shallow Unity foundation skirt closes
  terrain variation below the Blender shell. The old mass survives only as a
  renderer-free BoxCollider with the exact former lot/foundation envelope, so
  navigation, sound occlusion and special buildings keep their authority.

  The six opaque role renderers use the one packaged shared material; texture,
  tint, smoothness, metallic and identity texture transform are supplied by
  MPB, so no per-building material instances exist. The combined non-readable
  `WindowGlass` mesh decodes `(slotId + 0.5) / 256` from UV2 and indexes a
  64-entry per-building state table produced by the district window rules.
  Each facade row receives an exact deterministic lit share with at least one
  warm pane and, where the row has more than one pane, at least one dark pane;
  floor and side change the phase instead of the density. Every lit state uses
  the street lamp's `(1, 0.72, 0.42)` colour, a four-step brightness variant,
  the shared fixture factor and PS1 vertex snap. Generator `2.0.0` gives every
  `WindowGlass` face its own projected `0..1` UV0 while preserving UV2, so the
  shader selects one exact quadrant of the 2x2 curtain sheet and multiplies it
  into both albedo and emission.

  Competing exterior planes are invalid data, not a render-order workaround.
  The pure geometry audit rejects every positive-area, same-facing exterior
  coplanar overlap. It also rejects axis-aligned exterior opaque overlap of at
  least `0.05 m2` when the planes are less than `0.03 m` apart; authored plinth
  and secondary-facade relief keeps `0.035–0.065 m` of margin. Only the
  slot-identified `0.018 m` facade-to-glass and `0.012 m` frame-to-glass
  relationships remain under that threshold, while small interlocking metal
  contacts remain below the area floor. Synthetic positive controls pin the
  threshold, downward-face exclusion and those two window relationships.
  Shared internal join faces are omitted and rail, frame and trim layers are
  separated in depth. The Unity terrain skirt keeps its `0.04 m`
  vertical overlap but is inset `0.08 m` from every horizontal side. Roof decorations
  use kind-specific fixed mounts derived from the Blender generator's actual
  gables, decks and sawtooth planes. A primary landmark and its lot's ordinary
  core always use complementary surfaces: the Old Town, Residential and
  Industrial roof landmarks force facade cores, while Nightlife's facade cinema
  forces its billboard core onto the roof. Facade mounts and descriptor forward
  share the prototype's actual frontage pose, including roadless lots.
  `BuildingLot` remains the collision and planning envelope.

  `HomeExteriorViewBuilder` maps that exact City pose into Home-local space
  and classifies all eight padded prototype-bound corners against the
  apartment half-space. A wholly hidden wrapper is omitted and a wholly
  exterior wrapper is instantiated unchanged and collider-free. A crossing
  wrapper alone keeps the previous bounds-clipped primitive silhouette: the
  runtime never shears, scales or cuts the deliberately non-readable Blender
  topology. The player home follows the bounded special-shell contract above;
  the bar and supermarket follow their separate fixed-metre complete-exterior
  contracts.
- **Accepted — The summit opens exactly once, and the opening is a
  terrain mask:** the terminal plateau carries a `MountainRoadBrinkDescriptor`
  on its own descriptor rather than on the terminal plan, because
  `MountainRoadTerminalPlanner` already samples terrain to ground the
  cableway and so cannot precede the thing that changes terrain — and
  because the plateau descriptor is already threaded into every
  `SampleHeight` call in the area, which costs the change no signature
  anywhere. `MountainRoadTerrainSampler.ApplyBrinkFall` takes the ground
  down `26 m` inside a wedge from the back rim: bearing `-27` degrees in
  the plateau's own frame, `9` degrees of composed half-angle plus `3` of
  taper, from `3 m` out to `132 m`. It runs on the FINAL returned height,
  after the plateau's `12 m` exterior blend, because applied to the
  intermediate terrain that blend lifts the cut back to pad height across
  exactly the stretch the cliff is made of; the interior early return still
  answers first, so the pad, the road seam and the one drivable surface are
  untouched by construction rather than by tolerance. The bearing is
  measured: swept from the rim, the mid and far ridges stand shoulder to
  shoulder from `-60` to `-46` and again from `-8` through `+14`, and
  between those two masses there is nothing inside the area's `120 m` far
  plane. No ridge is skipped — the amphitheatre keeps its eight mid and
  twelve far-snow against floors of six and ten — and the two masses become
  the opening's jambs. `MountainRoadValidator.ValidateBrink` holds the cut
  `10 m` from every route centreline, `6 m` from every cableway node's
  GROUND (a horizontal distance on purpose: lowering ground under the line
  only makes its own clearance test greener while the supports end up on
  stilts), `3 m` from every ridge footprint and clear of every crown.
- **Accepted — What is over the brink is a fixed matte on borrowed
  shaders:** `MountainRoadVista*` stands real world geometry at `81-105 m`
  in the cut — valley bed, the switchback the hero climbed, a grain of city
  seventeen columns wide and a horizon ridge line landing within a few
  degrees of the standing eye, so the city reads BELOW the horizon rather
  than on it. Everything is measured from `y = 0`, the height of the tunnel
  he drove out of, which is what makes the drop mean something. It reuses
  `CityLighthouseIsland.shader` and `CityLighthouseBeam.shader` unchanged —
  neither has anything city-specific in its HLSL — through a mountain-owned
  resources class that only retunes distance for a `120 m` far plane. It is
  fixed rather than camera-relative because the parapet and the walls of the
  cut have to occlude it and because twenty metres of parapet make parallax
  an effect rather than an artefact. Its windows are additive vertex colour
  driven by `NightFactor` from `MountainRoadAtmosphere`'s existing
  per-minute apply, so the valley can never be lit at an hour the rock in
  front of it is not; there is no Light in it and never will be. Two things
  the contract tests deleted rather than fixed: painted shoulders framing
  the gap (occluded by the real ground at their own offset — the cut's own
  walls are better in every way) and worn paths across the yard (the plateau
  slab is already asphalt, so there is no snow to wear through).
- **Accepted — Mountain ridge dressing follows the complete route envelope:**
  The terrain bounds retain a `76 m` margin around the road, plateau and upper
  cableway reach. Ordinary mid and far-snow ridges sample the outer perimeter
  of the global route/plateau envelope instead of clustering around individual
  samples. Each oriented ridge base takes the minimum terrain height measured
  beneath its footprint and buries another `1.5 m`, so the complete silhouette
  stays grounded over uneven terrain. Plan and validator both keep every ridge
  footprint clear of the road corridor, plateau and tree crowns; the dedicated
  cableway return occluder remains separately positioned by the terminal plan.
- **Accepted — The cableway's blackout is derived from its own rock:** The
  far snow ridge that swallows the upper turn is not scenery near the top, it
  is planted ON the line: `UpperOccluderSetback 1.8 m` short of the cable end,
  `UpperOccluderDepth 10 m` thick along it, so its near face crosses the track
  `6.8 m` before the end and the last stretch of visible line — the turn
  included — is inside solid geometry deliberately. Those two numbers are the
  cableway plan's, and `MountainRoadPlanner` builds the ridge from them, so
  the ride can read them: `AlpineCablewayRideController.EvaluateFadeLeadMeters`
  returns `LineLength - LastVisibleDistance + CabinSpeed * FadeOutSeconds`
  = `11.302 m` rather than a constant. It replaces an eyeballed `5.5 m` that
  was short by four metres and let the passenger ride `3.9 m` into the mountain
  in first person; a ridge is single-sided, so from inside it he was looking at
  the world through the rock. The leading edge in that derivation is the
  cabin's ROOF LIP and not its front wall: the slab is built at `CabinSize.z *
  CabinRoofOverhang` and oversails the body by `6 cm` a side — `6 cm` quietly
  spent out of a clearance that claims to be a metre. The last tower moved
  `50 → 44` for the same reason:
  standing `1.2 m` off the rock face it left no interval in which both
  authored rules — the cut lands after the last tower, and before the rock —
  could be true at once, which is exactly why the geometry lost that argument
  silently. **One shape, three readers:** `MountainRoadRidgeGeometry` now owns
  the polygonal crest that `MountainRoadSceneryMeshFactory` draws, and both
  `MountainRoadPlanner` and `MountainRoadTerminalValidator` measure the cable
  against THAT instead of the top of the ridge's bounding box, which stands
  about `14%` of the box height — here `4 m` — above the rock that is actually
  built. The planner had to move too, not just the validator: a validator alone
  would have converted the hole into a crash, because the crest carries a
  seeded variation in eight steps and on one residue in eight the drawn snow
  stood `0.34 m` UNDER the cable it exists to hide. Both shipped seeds miss
  that residue, which is precisely how it would have shipped. Sizing from
  `CrestFactor` costs `0.41 m` of ridge on that one seed and nothing on the
  other seven, and `Occluder_CrestClearsTheCableOnEverySeedResidue` walks all
  eight.
- **Accepted — A borrowed albedo shares its bytes but not its
  compensation:** the mountain road prints six sheets of its own (asphalt,
  forest floor, wind snow, layered stone, conifer needles, bark) and borrows
  nine kinds from City, Home and Supermarket families rather than reprinting
  concrete, iron, painted metal, masonry, linoleum, timber and wall paint.
  What a borrowed kind does not inherit is the source family's albedo
  compensation. Compensation is fitted to the TINTS that multiply a sheet,
  not to the sheet: the masonry serving a city retaining wall at
  `0.335, 0.350, 0.325` cannot also serve the cafe's brick gable at
  `0.290, 0.105, 0.065` — reusing the fringe constant there would brighten
  the wall by more than the `8%` the linear rule allows.
  `tools/build-mountain-road-textures.py` therefore measures each borrowed
  PNG, re-solves the constant against the mountain's own tints, and refuses
  the build if the result would clamp a channel or miss the limit; it records
  the source sheet's SHA256 so a regeneration upstream is caught rather than
  silently shifting a mountain surface. Two kinds may name one file and still
  differ: `PaintedMetal` and `PaleEnamel` read the park's painted metal at
  opposite ends of its tint range, as do `WallPaint` and `InteriorPaint`.
- **Accepted — The mountain UVs were fixed before the sheets landed:** a
  sheet on a bad unwrap only makes the unwrap visible, so the same pass
  corrected six of them. The road's kerb continues the carriageway's unwrap
  over its edge — its U runs on past the road half-width by the slab's own
  thickness — instead of collapsing three metres of asphalt into two
  centimetres of border; the vertices stay welded, so the road and plateau
  still share one entry vertex. The plateau and the terminal apron are
  unwrapped in the ROAD's frame, measured from the entry sample and biased by
  the distance already travelled, so the texture crosses their shared seam
  unbroken rather than restarting at it. Soil and snow are two cuts of one
  vertex grid and now receive one set of normals averaged over both triangle
  sets, because letting each mesh recalculate its own lit the snow line as a
  seam that is not there. Ridges and boulders, which carried no UVs at all,
  take the same faceted box projection the combined batches use, at the stone
  recipe's pitch and off their existing normals, so nothing about the
  lighting changes. Conifer crowns unroll by arc length against world height,
  phased per tree from where it stands. The bridge deck moved from a scaled
  cube onto the single-box batch its girders and piers already use, so its
  faces tile at true metre scale; its transform stops carrying the offset and
  its collider becomes the mesh, matching the abutments. The cafe's prism
  splits its cap and side UVs onto separate vertices — sharing them gave every
  side face zero vertical UV extent — which also gives the roof slab the crisp
  arris a building edge has instead of a bevel.
- **Accepted — Cross-area map travel is a hard Single-mode scene boundary:**
  the ordinary map owns City and Mountain Road tabs, draws the hero only on the
  current area's tab and asks for confirmation before an other-area transfer.
  `AreaTravelService` first Single-loads build index `8`, `AreaLoading`, which
  owns only a black unscaled progress-bar presentation; it then Single-loads
  the destination and passes a one-shot arrival token. Thus the source world is
  destroyed before the destination composes, and City/Mountain Road are never
  resident or rendered together. `MountainRoadRoot` may regenerate the pure
  seeded City layout and mountain-boundary plan for its City tab, but must not
  invoke a City world builder or instantiate City GameObjects. Door transitions
  retain their separate authored `DoorTransition` chain. The physical City
  tunnel is an unavailable refusal boundary **on foot** and stays one; what
  crosses it is the Ferryman's car, through `AreaArrivalToken.Ferryman` — the
  first caller the token set has ever had beyond the map. The refusal needs no
  new gate for that: `CityTunnelTravelController.CanEngage` already requires
  `Motor.InputEnabled`, which a seated passenger does not have.
- **Normative — a destination root's `Awake` is INSIDE the transition, not
  after it:** `AreaTravelService` sets `allowSceneActivation`, the destination
  wakes, and only after `destinationOperation.isDone` does `Complete` clear
  the flag. Through that window `SceneTransitionService.IsTransitioning` is
  still true, and `PlayerAnimatedInteractionController.Update` force-completes
  every running interaction while it is. Anything an arrival wants to START —
  and a contextual interaction above all — must wait for it to clear rather
  than run in `Awake`. `LastRouteRideController.AwaitArrivalStart` is the
  worked example; it holds under a black screen for the frame or two involved,
  and both arrivals — the climb's and the homecoming's — go through it.
- **Accepted — The Ferryman's journey is one session stage, not two worlds,
  and that stage is a RING:** `LastRouteFerrymanRideStage`
  (`NotTaken -> InTransit -> Arrived -> Returning -> NotTaken`) lives on
  `GameSessionState`, and both areas build the car and the man from it and from
  nothing else. So he is never in two places and never in none: the City raises
  him on the island while the stage is `NotTaken`, `MountainRoadRoot` raises
  him parked on the terminal apron once it is `Arrived`, and the two moving
  values are read by nothing but an arrival. `TryAdvanceFerrymanRide` still
  refuses everything that is not the next thing that can happen; exactly one
  step is not an increment (`Returning -> NotTaken`, the car reaching the
  island again), so a car parked on the mountain can never reappear in the city
  without being driven there. `CityMapController.Open` refuses while either
  moving value is set, for the reason it refuses while the area service is
  travelling — the hero is between two places rather than standing in either,
  and a chart with a teleport on it would let him step out of a moving car.
- **Accepted — the chart bringing the hero to the mountain brings the car with
  him:** every way into `MountainRoad` that is not the ride and not the
  cableway is the map, and a map that can put the hero on a mountain six
  hundred metres above a car he never took would strand him — there is no road
  down on foot and the cableway only goes up. So `MountainRoadRoot.BuildLastRoute`
  advances the stage to `Arrived` itself on such an arrival and parks the car on
  the apron waiting. It costs the island its car, which is the invariant being
  honoured rather than broken: he is in exactly one place, and the way back is
  the ride he can now ask for.
- **Accepted — a car parked nose-in leaves by backing round, not by turning:**
  the terminal apron is a `7.5 m` pocket whose cafe corner stands `8.24 m` from
  its centre, so no U-turn of a usable radius fits (this is why the arrival
  parks nose-in in the first place). `LastRouteMountainDrivePlanner.CreateDeparture`
  therefore opens with a two-point turn — a quarter-circle backed on lock and a
  quarter-circle driven off it, both at `5 m` — and `LastRouteCarDrivePath`
  carries the reverse leg as a first-class part of the road. What it stores per
  vertex is the car's HEADING and not its direction of travel, so the cusp is
  continuous in the body and discontinuous only in the gear; the drive model
  brakes to the cusp exactly as it brakes to a terminus, pauses `0.9 s`, and
  pulls away. The city end takes the mirror answer and does NOT reverse: its bay
  can be driven into and only backed out of, because behind it stand the
  island's paving circle and its route mast, so the homecoming parks turned
  round and the canonical stance returns with the next city build.
- **Accepted — Compact separate home interior:** `HomeInterior` owns a
  validated `10 x 8 x 3.4 m` runtime-composed main room and bathroom with six
  main-room furniture footprints plus toilet, shower and sink, protected
  entry/main/bathroom-access paths and a solid ajar bathroom door. Dedicated
  blocking junk owns the otherwise reachable camera corner; the toilet keeps
  its cistern against the right wall and its bowl facing into the room. It
  reuses the common player, intoxication HUD, interaction and door-transition
  contracts while putting the single Main Camera into three authored fixed
  poses with doorway/balcony hysteresis and a main-room fallback. The camera
  poses remain unchanged. A separate cold, hard-shadow ForcePixel Spot starts
  just inside the bathroom threshold and projects through the ajar door onto
  the existing apartment exit area. It shares one deterministic unscaled
  fluorescent-failure flicker with the bathroom point pool, HDR tube and halo;
  the main practical and time-of-day window-cookie Spot remain. One separate
  shadowless warm ForcePixel Spot is co-located with the compact amber fixture
  above the entrance and aims its full-strength cone over the door and entry
  floor, for at most five atmosphere-owned local realtime lights in addition
  to the scene Directional light. The exit door's
  geometry and material remain unchanged. Exiting sends
  the player from the apartment into `StairwellInterior`; only the stairwell's
  street door sets the home return kind and restores the matching city
  approach, without altering route, visit, cash or drinking progress.
- **Accepted — Data-first interactive Home refrigerator:** The refrigerator is
  derived from the kitchen footprint as its own validated plan instead of
  remaining an indistinct cube embedded in the counter. The counter is split
  around its `1.08 x 0.76 m` footprint and the table moves `0.30 m` deeper in
  Home-local Z, leaving a player-width waypoint route and a dedicated approach
  trigger. Runtime composition builds a hollow worn cabinet, three shelves,
  lower drawer and two door bins around six cavity plus two door storage slots;
  stable slot IDs initially place one vodka bottle, chicken egg and open stew
  can. `HomeRefrigeratorItemCatalog` owns each occupant's localized name,
  description and preview transform, while `HomeRefrigeratorItemView` registers
  its original root, renderers and tight trigger collider. Stable slot IDs now
  map to session-owned world-item IDs; collected slots remain physically empty
  when Home is rebuilt after a scene round trip.
  `HomeRefrigeratorInteraction` owns a separate unscaled
  `CameraApproach -> Reach -> Unsealing -> Opening -> Inspecting -> Closing ->
  Sealing -> CameraReturn` timeline. It captures the shared modal lock, keeps
  the normal 3D hero and contact shadow through the camera approach, then
  acquires their owner-scoped visibility lease in the same presentation frame
  that a right-arm subset filtered from the production prefab appears.
  It holds the `102°` open state until explicit close input; while the ordinary
  interactor is suspended, its clickable close prompt binds directly to the
  same `RequestClose` guard used by keyboard/gamepad input. It disposes the
  subset and lease at the start of `CameraReturn`, restoring the exact world
  mesh and contact-shadow state; the active fixed-camera shot, input and HUD
  restore on completion, disable or destroy. A cold
  emissive strip plus `CityLightHalo` reveals the contents without increasing
  Home's realtime-light count; generated seal, hinge and thunk cues use the
  existing spatial audio contracts.
- **Accepted — Nested PS1 refrigerator item inspection:** Item browsing exists
  only while the outer refrigerator timeline holds `Inspecting` and reuses its
  captured modal lock. A pointer ray against registered trigger colliders
  applies a reversible `MaterialPropertyBlock` hover tint and draws the
  localized name beside the cursor; keyboard/gamepad cycling plus confirm is an
  equivalent path. Selecting the vodka, egg or open stew can advances a pure
  unscaled `Browsing -> FlyingIn -> Inspecting -> FlyingOut` timeline, reparents
  the model through a camera-relative pivot, eases it to a centered preview,
  rotates it at `18°/s` and reveals a dark camera-facing backdrop. Crisp
  post-composite UI shows the localized title, short description and
  `Take`/`Use`/`Back`. `Take` atomically adds the matching inventory item and
  stable collected-source marker, unregisters the refrigerator item and removes
  its physical model; `Use` remains unavailable. Back or an outer close request
  returns an untaken item before the door can close. Normal return, cancel, disable and
  destroy restore its exact parent, sibling index, local position/rotation/
  scale, selection-collider state and original renderer colors, then clear the
  temporary presentation state.
- **Accepted — Session hero inventory:** One pure ordered stack model and code
  catalog own the current run's apartment keys, lighter and collected Home or
  purchased supermarket food and drink items. `GameSessionState` exposes
  read-only stacks plus atomic add,
  remove and world-source collection operations; `BeginNewGame` resets starter
  possessions and every collected source, while ordinary scene transitions do
  neither. `InventoryController` is installed beside pause in all eight gameplay
  roots, opens on `I` or gamepad North only during free input, captures the
  existing fullscreen modal lock and exact time scale, and restores both on
  close or lifecycle cleanup. Its `640x360` IMGUI view keeps generated
  point-filtered icons in the bounded five-column grid, uses the dedicated
  transparent portrait rendered from the production 3D hero and
  shows the selected item through a live point-filtered 3D render in both the
  lower description and Examine views. The preview reuses the same procedural
  models as physical refrigerator contents, adds matching keys and lighter,
  rotates on unscaled time and owns a hidden camera/light/RenderTexture stage
  that is inactive outside the inventory. Status keeps the portrait and cash,
  then fits intoxication, hunger, stress and fatigue into four compact `0-100`
  bars.
  Consumables expose a contextual Eat/Drink command through pointer, `U` and
  gamepad West; unsupported Equip, Combine and Drop commands remain absent.
  Pause executes before
  inventory so Escape sees the occupied lock, then inventory closes later in
  the same frame without leaking that press into pause.
- **Accepted — Session needs and atomic inventory consumption:** Hunger, stress
  and fatigue are clamped integer `0-100` session values where higher is worse.
  All start at `0`, survive ordinary scene loads and reset to `0` with a new
  game. Once the startup Wake starts the persistent scaled clock, one pure
  double-precision progression state raises hunger by `100 / 1440` and fatigue
  by `100 / 1080` per elapsed game minute. Fractional state makes the result
  independent of frame partitioning; reaching `100` discards overflow instead
  of banking hidden growth. The clock path keeps both frozen before Wake and
  at `timeScale = 0`, while ordinary interactions, transitions and scene loads
  do not create another pause rule. The inventory exposes the clamped integer
  values, but neither need applies a gameplay debuff yet. A normally completed
  bed exit resets fatigue and its fraction through the shared interaction
  completion boundary; cancellation and lifecycle cleanup do not. A
  data-first consumable catalog gives all present food an explicit relief
  value plus a poor-food minimum hunger of `20`, so repeated cheap food can
  never fully satisfy the
  hero and food at or below its floor is not consumed. The supermarket vodka
  bottle is one atomic four-serving use. `GameSessionState` preflights every
  use, removes exactly one stack only after success is known, then commits food
  relief and clears the hunger fraction, or commits drinking, stress and
  intoxication together. Maximum intoxication,
  a stale stack or a no-effect food use mutates nothing. Refrigerator `Use`
  remains unavailable; items are taken into the hero inventory first.
- **Accepted — Separate finite-stock supermarket interior:**
  `SupermarketInterior` owns one validated `16 x 11 x 3.6 m` room with
  protected aisles, three shelf sections, a stockroom facade and a decorative
  checkout staffed by the separate normal supermarket cashier. The register does not
  process sales. Each shelf owns an authored fixed camera and one interaction
  station.
  One continuous modal browser cycles through every available physical product
  in deterministic shelf/slot order, skips empty shelves and never releases its
  captured player/camera state while changing shelves. The selected shelf keeps
  its authored camera position and field of view while the rotation targets the
  combined world renderer bounds of the selected product. Muted clickable
  previous/next arrows follow the product's projected screen bounds; pointer,
  keyboard and gamepad all use the same navigation path. The browser takes
  owner-scoped renderer-only visibility leases over both hero and cashier
  presentations: gameplay roots stay active, and ordinary exit, failed
  open, disable and destroy restore each captured renderer state exactly.
  `SupermarketProductCatalog` offers exactly one physical chicken egg, vodka
  bottle, closed stew can, instant noodles and day-old loaf. Each product has a
  stable source ID. `GameSessionState.TryPurchaseWorldItem` is the sole commit
  boundary: pure rules validate source, offer, cash and stack capacity, then
  the transaction records the source, adds one inventory item and deducts cash,
  rolling the source back if inventory insertion fails. Success immediately
  removes the shelf model and collider; scene rebuilding filters committed
  sources, so sold products stay gone until `BeginNewGame`. Failure mutates no
  cash, source or inventory state. `ClosedStewCan` is deliberately distinct
  from the refrigerator's `OpenStewCan`, so buying sealed stew cannot satisfy
  the cat-feeding requirement. Entry and exit use the shared DoorTransition and
  restore the supermarket's own City return point. The City entrance alone
  configures the common door action with a calculated `0.242 m` initial vertical
  tolerance covering the complete visible prompt reach across road grade and
  curb; its approach remains a constrained walk to the grounded dock, while
  all other doors keep the shared `0.02 m` default.
- **Accepted — Passive Blender supermarket interior:** The fixed shell,
  entrance, ceiling grid and fluorescent housings, three profiled shelf bodies,
  recessed cold cabinet, checkout, stockroom facade and four CCTV
  mount/head-pivot assemblies come from the deterministic
  `supermarket_interior_v1` Blender asset. Its measured manifest owns semantic
  parts, metre bounds, shared surface sheets, a build signature and anchors
  checked against `SupermarketInteriorLayoutPlanner`; the prefab itself owns no
  Collider, Light, Camera, Rigidbody, AudioSource or Animator. Unity continues
  to own shell/fixture collision, shelf triggers, five finite product lifetimes
  and selection colliders, practical lights, flicker, the cashier, CCTV servo
  controllers, UI and scene transitions. CCTV controllers rotate only the
  authored head pivots, and a purchased product owns its price tag so both
  leave on the same transaction. Five counter-clockwise perimeter-skirting
  sweeps bury rear and bottom faces `3 mm` into their neighbouring wall/floor
  surfaces and trim corner joins, removing all coplanar render pairs while
  preserving the visible profile.
- **Accepted — Shared passive Blender supermarket product pack:**
  `tools/build-supermarket-products-3d-model.py` owns one deterministic
  `supermarket_product_pack_v1` source under
  `ArtSource/Supermarket/Products` and exports
  `Assets/Supermarket/Products/Models/SupermarketProducts3D.{fbx,json}`.
  The pack contains six coincident bottom-centre item roots — instant noodles,
  day-old loaf, vodka bottle, closed stew can, open stew can and chicken egg —
  over `33` meshes / `2,276` triangles. They are deliberately generic:
  `authored_text` and `brands` are empty, and the source imports no Collider,
  material, Light, Camera, Rigidbody, AudioSource or animation. Its fixed build
  signature is
  `2437d765ab7b7004a05d281193ae78e26b2c6728e641e57273ecc0d9842821b7`.
  `SupermarketProductAssetSetup` extracts one passive Resources prefab per
  item; `SupermarketProductModelResources` and `InventoryItemModelFactory`
  reuse those same render models on shop shelves, in live inventory previews,
  inside the Home refrigerator and during the cat-feeding flow. Runtime keeps
  ownership of selection collision, sizing and item lifetime. Only five item
  IDs are supermarket offers: their bottom pivots bind to exact tier anchors
  in the interior asset. `OpenStewCan` has only the Home refrigerator/cat world
  source and never becomes a supermarket offer. The vodka source is exactly
  `0.46 m` tall; only its shop instance uses a `0.37 m` fit envelope on the
  unobstructed third/top tier, keeping the `1.08x` selected bounds below the
  `2.05 m` shelving-unit top. Closed stew occupies the first tier.
- **Accepted — Reusable inventory-backed target interaction:** A target-specific
  adapter supplies one validated `InventoryItemRequirement`, localized Talk,
  confirmation and missing-item keys, and an idempotent
  `IInventoryTargetInteractionHandler`. The pure model owns only
  `Closed/Choice/Confirmation/Executing` state, defaults to Talk and No, and
  cannot execute twice. The shared scene-local controller owns pointer,
  keyboard and gamepad input, captures the existing modal lock, disables the
  ordinary prompt and restores exact player/camera/HUD state. A Yes path first
  calls the handler's preparation boundary, then rechecks and atomically removes
  the required stack through `GameSessionState`, then begins presentation;
  failed preparation or a stale stack consumes nothing, and a thrown startup
  refunds the just-committed stack before cleanup. Normal completion and
  abnormal cancel/disable/destroy use separate controller exits but the same
  handler cleanup contract. Each adapter tracks the presentation resources it
  actually acquired and restores only that owned prepared/active work before
  modal input returns. This first version intentionally supports
  one item stack per definition; multi-item recipes require a later atomic
  inventory transaction rather than sequential removal.
- **Accepted and implemented 2026-08-04 — Continuous modular 3D hero:** Every
  production runtime and UI representation of the main hero now derives from
  the generated modular 3D character. `PlayerFactory` preserves the
  authoritative `PlayerMotor`/`CharacterController` root and instantiates one
  `Resources/Player/Player3D` prefab in all eight gameplay roots. Its Generic
  Animator uses no root motion; the prefab contains a 31-bone armature with six
  non-deforming sockets, while `Player3DAssetRegistry` serializes 73 mesh
  bindings, 16 required anatomical parts, metrics and 37 in-place Actions.
  `Player3DCharacterPresentation` owns locomotion, face,
  intoxication/balance and authored fall sampling, including the full-body
  side-down-to-all-fours-to-stand Rise actions; the companion ragdoll owns only
  the bounded physics interval and its `0.16 s` return bridge. Bed, smoking and
  cat feeding drive continuous full-body clips on that same rig; refrigerator
  reach exposes a visible camera-local right arm from the prefab, while bar
  drinking keeps the seated world body and drives a nested full-body
  pickup/sip/return action on it. Inventory loads the dedicated transparent 3D portrait. Real meshes cast URP
  shadows while the analytic contact patch remains grounded and fall-aware.
  Guided approach,
  independent entry/action/exit poses, neutral settle, terminal hold, atomic
  preparation and owned lifecycle cleanup remain mandatory.
- **Accepted — Grounded endpoint contract for contextual 3D animation:**
  This is the mandatory project-wide authoring and runtime contract for every
  future interaction in this class; the normative checklist lives in
  `ai/contextual-animation-standard.md`, and deviations require an explicit
  user-approved accepted exception here.
  Interactive bed sleep, balcony smoking, cat feeding and ordinary location
  doors share a visible
  `Positioning` phase before `Entering -> Looping -> Exiting`. Each adapter
  provides independent entry root/pelvis/facing, action pelvis and exit
  root/pelvis/facing data. `PlayerMotor` advances the ordinary rig with its real
  `CharacterController`, walkable constraint, gait, facing and footsteps;
  manual input cannot redirect that approach. The authored Y level must already
  be reachable from the current grounded root, so a vertical mismatch refuses
  startup rather than teleporting. A constrained move with no measurable root
  or rotation progress for `1.5 s` marks the approach stalled and restores the
  captured state; scene transition, disable and destroy boundaries cancel it
  through the same ownership cleanup.
  Exact entry alignment activates the shared presentation handoff lock, resets
  locomotion, face and additive status bones, and keeps the neutral rig visible
  for one rendered frame. The controller then samples the registered Generic
  enter/loop/exit clip before aligning its serialized pelvis anchor to the
  authored world target. Animator transitions, Animation Events and root motion
  own no gameplay transaction. The timeline guarantees that its terminal exit
  pose is presented before becoming idle; the physical root is then placed at
  the independent exit and the neutral ordinary rig stays locked until its
  final `LateUpdate` restoration frame. Normal and abnormal exits end the clip,
  reset its model-root spatial offset and restore owned presentation state.
- **Accepted — The mattress is thick cloth, not a box:** The bed's mattress
  and pillow tops are vertex grids (the project's first per-frame-written
  meshes) that dent under the sleeping hero and slowly refill after he gets
  up. `HomeBedSurfaceDepressionModel` is a pure fixed-step spring in the
  cemetery-model idiom: per-source target depth is the part's actual
  penetration under the rest plane read from live renderer bounds — not a
  guessed weight, which would contradict the rigidly lowered pose — with a
  smoothstep skirt, a pinned border ring the single-quad sides stay welded
  to, fast denting (~0.25 s) and slow recovery (~1.5 s).
  `HomeBedSurfaceDeformer` (order 400, after every pose writer) feeds it
  `Phase` + `FrameIndex` from the shared controller — no shared interaction
  code changed — and rewrites the meshes only on frames that moved.
  The load-bearing coupling: `SleepingHipHeight` descends by
  `BedSleeperSinkDepth` so the hero lies in the dent instead of hovering
  over it, and the pillow's rest top is derived one pillow-dent above the
  sunken head plane. The bedside seat deliberately takes no dent — its hip
  height is pinned by both boots on the floor, so a dent there could only
  open a gap; body weight is zero through the lie-down's seat window, and
  stays at full one for the whole wake: the sources vanish naturally as
  parts rise off the rest plane, and the slow spring refills the hollow
  visibly BEHIND the rising body — the one moment the dent is not hidden
  under the body that made it. Readability was capture-driven through the REAL pipeline (play-mode
  renders of the actual camera, bulb light and occluder-dither material):
  a smooth-normal bowl is invisible on the project's noisy albedos, so the
  top faces are independent per-cell quads (coarse 0.14 m cells — hard
  facet light-steps, not gradients), dented facet normals have their
  lateral component steepened ~3x, the displaced bedding bulges UP in a
  welt around the body (RimBulgeRatio of the local dent), and the sink is
  0.10 m — half the mattress thickness, so the sunken body reads plainly
  behind the untouched rim even from the far gameplay camera. Depth
  started at the discussed 0.045 and was raised twice at the user's
  direction after on-screen checks; the welt is 0.65 of the local dent
  and the pillow swallows 0.045 of the head.
  The mattress dent under the pillow footprint is capped to the pillow's
  embed depth (padded a cell so bilinear sampling cannot leak past the
  border), keeping a gap from opening beneath its rigid box. A loop entered
  without a lie-down (the opening's `BeginLooping`) snaps the springs to
  equilibrium; the snap trigger deliberately ignores the bed's ownership
  flag at event time, because that flag is raised only after `BeginLooping`
  returns — the weight is re-resolved in `LateUpdate` where snapping a
  foreign loop lands harmlessly on rest.
- **Accepted — The bed is built around the sleeper, not beside him:**
  A contextual clip is pinned to the world by its pelvis bone and
  `GroundOrdinaryPose` is off for as long as it owns the rig, so a single
  guessed clearance decides whether the hero rests on the bed or inside it.
  `HomeInteriorWorldBuilder.BedDressingSurfaceHeight` was one such guess and
  matched no real surface: the mattress top is `0.56`, the crooked blanket
  `0.66` and the pillow `0.73`, while the hero was placed at `0.715` — hips
  three centimetres inside the blanket, head twelve inside the pillow, and
  seated eight centimetres above the bedding with his boots off the floor.
  The bed now derives from measurement in both directions.
  `validate_bed_support_contract` in `tools/player_3d_model_common.py` samples
  the real posed meshes and reports how far the supine back, the back of the
  lifted head and the seated weight hang below the pelvis bone;
  `PlayerCharacterDimensions` mirrors those three numbers and
  `HomeBedInteractionPlan` adds them to `BedMattressSurfaceHeight`. The pillow's
  top is then built at the head the clip authors, the crooked blanket is shoved
  clear of the sleeper's corridor, and the crumpled shirt lies on the mattress.
  The generator refuses drift in either the offsets or the poses; EditMode owns
  the built geometry and PlayMode sweeps the real renderers through sleep and
  wake. Bedding compresses, so the bedside seat and the waking stir carry a
  stated soft-goods allowance while the held sleeping pose does not. The roll
  itself is deliberately unasserted: a rolling body's support moves and the
  shared pelvis transition carries one waypoint rather than a profile, so
  asserting through it would assert against the runtime instead of the pose.
  The three bed clips also became the first contextual Actions on auto-clamped
  Bezier curves with staggered keys, which is what removed the wooden-doll
  read; the other five contextual triads keep their linear timing until they
  are re-authored in turn. Waking was then re-cut at the user's direction from
  a roll into a four-beat sit-up — half-crouch on the mattress, right leg over
  the near edge, then the left, then stand — which is both what a man getting
  out on the door side actually does and what makes the whole wake checkable:
  with no roll, his weight stays on the bed until the first boot leaves it, so
  every sample of it is asserted, and the leg order is stated as its own
  measurement rather than left to survive by accident.
- **Accepted — Separate vertical home stairwell:** The exterior home door and
  apartment door connect through `StairwellInterior`, a deterministic
  `8.6 x 9.6 x 6.25 m` runtime-composed space with street, middle and apartment
  elevations. Three continuous physical ramps support 48 collider-free visible
  steps, while overlapping navigation corridors remain wider than the
  controller diameter at every floor/flight seam; this keeps the real
  `PlayerMotor` route continuous without changing the staircase silhouette.
  A full-width, full-standing-height invisible safety collider backed by
  visible furniture, mesh, planks and sacks seals the upward flight above the
  hero's floor. Side-aware arrival state chooses the correct spawn; only the
  street exit prepares the City home-return point. A separate 3D selector
  hard-cuts the shared camera between lower-flight, middle-flight and
  apartment-landing poses with vertical hold-zone hysteresis because the
  lower and upper flights overlap in XZ. Each pose keeps its exposed suspended
  HDR fluorescent tube and halo in frame; stronger co-located flickering
  practicals, post-processing, bounded dust, ventilation/electrical/pipe
  ambience and an optional scene-local `stairwell_theme` slot remain
  scene-owned.
- **Accepted — Perched interactive stairwell cat (3D Cheshire trickster):**
  The last sprite conversion. One seated near-black low-poly 3D cat owns the
  non-blocking authored perch on the top of the `Middle Landing Back Rail`
  (its origin is the rail-contact point, `MiddleElevation + 1.16`) and the
  same separate walkable interaction point. The model comes from the one-off
  Blender generator `tools/build-stairwell-cat-3d-model.py` through the
  cashier-shaped `StairwellCatAssetSetup` pipeline: passive prefab outside
  Resources, one addressable `StairwellCatProvider`, and — a first — **no
  armature at all**: articulation is pivot empties (`PIVOT_Chest`,
  `PIVOT_Head`, `PIVOT_Ear.L/R`, `PIVOT_Tail.01..03`) exported flat beside
  the meshes with every pivot-bound mesh's origin on its pivot; the actor
  adopts and articulates them (the wheelchair mechanism pattern). The
  untouched pure `StairwellCatIdleModel` timings drive breathing as chest
  scale, tail flicks, ear twitches and grooming as pivot deltas; the discrete
  look selector became the continuous hysteresis `StairwellCatHeadYawModel`
  (`65°` tracking clamp). Pose deltas apply about the model's world axes over
  rest poses cached at initialize; the geometry faces the negation of the
  model root's axes (FBX `-Z` under the prefab's inner half turn).
  Its trickster signature is `ACC_Grin`: a crescent of teeth wider than the
  head, on its own `StairwellCatGrin.shader` material that reveals by arc
  growth — the mesh bakes normalized arc length into UV x and the shader
  clips on `abs(u-0.5) > 0.5 * _GrinProgress` with a feathered glowing
  frontier, so the smile is drawn in from the center outward and un-drawn in
  reverse (appear `0.4 s`, vanish `1.2 s`, pure `StairwellCatGrinTimeline`).
  While the grin commits, the head swings over the shoulder toward the live
  camera (up to `150°` — the MiddleFlight shot sits ~`137°` behind the
  muzzle), one gesture with the reveal. **By default the grin does not
  exist**: renderer disabled at progress zero and every fragment discarded.
  There is deliberately no scheduler — `StairwellCatGrinController`
  (`BeginGrin/EndGrin/SetGrinProgress`) is a public API for a future
  trickster script, exposed as `StairwellInteriorRoot.CatGrin`.
  `StairwellCatInteraction` is untouched: the actor kept its feeding API
  verbatim, and the 16-step `6 fps` `StairwellCatFeedingTimeline` contract
  the player `CatFeed*` clips pair to now poses a head-down eating dip with
  chew alternation instead of an atlas swap.
- **Accepted — Same-scene third-floor Home balcony:** The Home right wall owns
  a real window and open glazed door connected to a walkable balcony at
  `4.7 m` street elevation. The room threshold and deck extend the same
  `RoadWalkableArea`; open-looking rails retain invisible safety colliders.
  `HomeInterior` does not additively load `City`: it regenerates the city plan
  from the preserved seed, transforms only a bounded slice of the player-home
  street into Home-local coordinates and renders nearby roads, lots, stable
  windows, lamps and signals without collision, a second City root, player,
  camera or realtime street-light pool. One shared geometry contract keeps
  the Home opening and default `8.8 m` City facade balcony aligned.
- **Accepted — Modal city-facing balcony-smoking vignette:** A data-first
  `HomeBalconySmokingPlan` derives one walkable Home-local dock around
  `(6.60, 0.04, -1.45)` from the balcony bounds. On the first `E`, modal
  ownership disables manual input but keeps the ordinary 3D rig plus its mesh
  and contact shadows visible while `PlayerMotor` walks the physical root
  through the existing
  CharacterController/walkable constraint to the explicit entry point and
  turns it along `+X`, out toward the reconstructed city. Only exact entry
  alignment begins continuous sampling of `SmokeEnter`, `SmokeLoop` and
  `SmokeExit` on that same rig. A separately authored exit root, pelvis and
  facing receive the ordinary rig before control returns; entry and exit may
  currently coincide without sharing one implicit stand anchor. The cigarette
  prop follows the serialized `SOCKET_Cigarette.R` bone. A visual-only worn
  enamel ashtray is composed permanently with the balcony at Home-local
  `(7.25, 1.12, -1.67)`: its base rests on the outer rail cap and its dish
  covers the ember at the authored exit flick. It is not owned by the
  interaction or registered with the rail dither group, so it remains visible
  before, during and after smoking. Real mesh and contact shadows remain active
  throughout because no alternate player renderer is introduced. Existing
  loop-local frames `3`, `11`, `14` and `23` produce the
  `9.5 s` rest/drag/breath/exhale cadence without duplicate art. A bounded
  deterministic ParticleSystem starts one gray-green burst at loop-local frame
  `16` from the registered mouth socket. Its emitter follows the animated mouth
  without inheriting the FBX bone scale, while world-space particles drift
  cityward and finish fading before the next loop. Exit stops new emission but
  lets the detached plume dissipate until owned cleanup. A second `E` queues
  immediately but reaches the exit sequence only at calm loop-local frames
  `0-3` or `21-23`, so no active drag or smoke plume is cut. The camera holds
  for `0.35 s` and follows a smooth quadratic push-in to `38°` FOV. Its close
  look target uses a `0.33 m` Home-local `+X` cityward offset: the resulting
  target yaw is about `13.12°`, the hero remains at about `0.37` viewport X
  at `16:9` and inside the `0.28-0.43` safety band across supported desktop
  aspects, and a point `1 m` farther toward the city stays visible to his
  screen-right. The close-camera forward direction's dot with Home-local `+X`
  stays above `0.19`, proving a material cityward component. This framing
  changes only the look target; camera position and FOV remain unchanged. A
  smoking-local
  deterministic drift is layered over that base path with
  local position amplitudes of `0.016 / 0.007 / 0.005 m` on X/Y/Z and
  pitch/yaw/roll amplitudes of `0.12° / 0.20° / 0.08°`. Each channel combines
  paired harmonics with periods from `13-23 s`, while one presentation clock
  stays continuous across Entering, Looping and Exiting so phase changes never
  restart or snap the motion. The existing camera blend is also the drift
  envelope: it brings the offset in with the push and returns it exactly to
  zero while the captured Balcony shot restores over the `2 s` exit. FOV keeps
  the original interpolation and receives no pulse. This overlay is owned by
  the smoking interaction and does not change generic `PlayerCameraFollow`
  behavior. A separate non-spatial
  `HomeSmokingMusicPlayer` loads only the optional user-supplied
  `Resources/Audio/SmokingMusic/smoking_theme`. It obeys the shared mixing
  rule rather than the timeline: it waits for the apartment theme to finish
  leaving, eases in over `MusicMix.FadeInSeconds`, leaves over
  `MusicMix.FadeOutSeconds` and treats a missing clip as a silent no-op.
- **Accepted — Diegetic Home practicals and fixed 3D shots:** The Home
  atmosphere retains exactly two shadowless practical realtime lights. A
  visible HDR emitter and depth-tested halo are physically co-located with
  each practical so the warm hanging lamp and cold bathroom tube read as
  actual sources. The bathroom point pool, tube and halo share a deterministic
  unscaled `6.4 s` cycle with a separate cold hard-shadow Spot staged just
  inside the bathroom threshold; the group remains steady for most of the
  cycle, then stutters together briefly while the Spot projects through the
  ajar door toward the apartment exit. A separate warm shadowless Spot is
  physically co-located with the compact emissive entrance lamp and illuminates
  the door plus entry floor. One shadowed cookie Spot projects through the
  window; only its color and intensity blend from the existing cold night
  shaft to warm daylight under `HomeDayNightController`, while the room
  practicals retain their existing behavior. The atmosphere owns at most five
  local realtime lights. Window/door panes reuse one shared transparent glass
  shader/material. During Home fixed-camera
  ownership, the same world-oriented modular 3D hero remains active in
  MainRoom, Bathroom and Balcony; no player billboard-plane mode is required.
- **Accepted — Explicit Home foreground cutaway:** Runtime Home builders
  register logical renderer groups for furniture and its attached dressing,
  bathroom and balcony doors, soft bathroom dressing and the visible balcony
  rail sections. Each group owns a validated kind and minimum visibility;
  renderers cannot belong to two groups. The structural room shell, glass,
  practical lights and invisible safety colliders are deliberately absent from
  the registry. `HomeOcclusionResolver` derives five camera-plane points from
  the combined live player-renderer bounds: head, left chest, right chest,
  pelvis and feet. Only the first four are protected, so low furniture may
  still hide the feet without flattening scene depth. If any registered
  renderer bounds intersects a camera segment to a protected point,
  `HomePlayerOcclusionController` fades the whole logical group toward its
  authored `0.15-0.23` floor. One shared opaque, depth-writing alpha-clip
  dither material receives `_HomeOcclusionVisibility` through one reused
  `MaterialPropertyBlock`; no per-renderer materials are created and
  existing color properties survive. Its ForwardLit variants retain the PC
  renderer's clustered additional lights, light cookies, light layers and
  reflection probes, while matching alpha-clipped shadow, depth and depth-normal
  passes keep shadows and SSAO coherent with the visible pattern. Fade-out
  takes `0.15 s`, a cleared group
  holds for `0.12 s`, and restoration takes `0.30 s`, all in unscaled time.
  Renderer presentation never disables or changes colliders, triggers or
  GameObjects. Opening, refrigerator and animated Home interactions suspend
  the cutaway and restore full opacity; disable, destruction and camera release
  also clear presentation state, with original shared materials restored when
  the controller is destroyed.
- **Accepted — Ordered session route:** The current itinerary is a unique
  ordered list of stable `BarId` values, edited only by hand on the map.
  The former bar-visited mechanic is removed entirely: no visit tracking,
  no map highlight or counter, and entering a bar changes nothing about the
  route. The route resets when the city seed changes.
- **Accepted — Road-graph route planning:** Each itinerary leg uses
  deterministic weighted Dijkstra with a binary min-heap over street and park
  path edges; player and bar endpoints are projected onto their segments
  without NavMesh.
- **Accepted — Modal schematic city map:** A runtime IMGUI overlay fits the
  complete finite city in one view, colors and labels all five districts,
  distinguishes park land and paths, exposes mouse/keyboard/gamepad editing,
  and temporarily suspends motor, interaction, camera orbit, cinematic camera
  motion and the HUD. It consumes `CityLayout.DistrictPointsOfInterest`
  directly, draws those lots as open public ground and gives the waterworks,
  drying yard, weighbridge and last-route island distinct non-route
  marker shapes plus a localized name legend. The same overlay reads the
  canonical `CityLayout.Supermarket`, draws it as a non-route grocery-shop
  landmark and resolves pointer hover across bars, home, shop and POIs by
  nearest marker, with deterministic priority ties. It also consumes the
  immutable bus plan and draws Route 01 below the darker player itinerary as a
  pale neutral closed winding loop, with five numbered localized stop
  markers in the default layout and a compact route/stop legend; it does not
  track the live pooled bus. Localized
  hover names use one high-contrast tooltip that flips and clamps inside the
  map. Shop and POI landmark markers remain context for the bone-toned player
  itinerary: POIs independently own nearby Route 01 stop targets, but the
  landmark markers do not change bar
  selection or player pathfinding.
  The City composition also passes the already validated
  `CityWorldResult.MountainBoundaryPlan` into the overlay. Map projection uses
  a separate display envelope that may grow only to the physical western and
  southern outer feet; it never extends the north/east maxima. Cross-hatched
  toe-to-outer strips describe the ridge mass, the narrow visible river
  approach ends in its dark mountain mouth without drawing the hidden cave as
  open territory, and the tunnel is an uncrossed open arch with only its first
  `12 m` drawn rather than a traversable destination.
- **Accepted — Observational two-area map-point inspection:**
  `CityMapPointDescriptor` is the common stable-ID, area, kind, localized-label
  and world-position contract for semantic points on both map tabs. The `XYZ`
  panel button, keyboard `C` and gamepad north/`Y` toggle a normal map mode;
  click resolves the same foreground-first nearest/priority target as hover,
  while Left/Right or D-pad Left/Right cycle a deterministic catalog and centre
  keyboard-selected points. Selection persists as an outline, and the side
  panel reports its localized name, area and invariant world `X/Y/Z` to one
  decimal place. City contributes exactly one descriptor for every
  `BuildingLot`, every open-area arrival, each bus stop, the current player,
  the mountain tunnel's real portal and the boat-station hut. A bar replaces
  its generic lot at `ReturnPosition`; home and supermarket replace theirs at
  `Center`; a district POI replaces its lot at the authored POI position.
  Mountain Road contributes the current player, tunnel, all authored hairpin
  apexes, bridge centre, plateau endpoint and terminal cafe/cableway landmarks.
  Road/itinerary polylines, intermediate route samples and mountain hatches are
  presentation, not catalog points. Inspection never treats a coordinate as a
  safe spawn: it is mutually exclusive with debug teleport and consumes map
  selection input without editing the bar route, requesting cross-area travel
  or confirming a teleport. Those actions return only after `XYZ` is closed.
- **Accepted — Shared-lock gameplay pause:** City, MountainRoad, BarInterior,
  SupermarketInterior, HomeInterior and StairwellInterior each attach one
  runtime `PauseMenuController` to their existing UI root. Escape or gamepad
  Start can open it only when no other
  `BarMinigameModalLock` or scene transition owns gameplay; pause therefore
  never steals Escape from maps, refrigerator inspection or modal shops, remains
  unavailable during the Home opening, and uses a Bar-specific gate rather
  than skipping the arrival reveal. Opening captures the prior time scale,
  listener-pause flag, motor/interactor/orbit/cinematic/HUD state, then sets
  scaled time to zero and pauses listener-owned gameplay audio. UI-pool sources
  alone ignore listener pause. Resume restores captured state after a one-frame
  input guard; disable, destroy, restart and quit restore it immediately.
  The localized `640x360` menu exposes only Resume, Start Over and Quit Game;
  restart and quit require explicit default-No confirmation, while save/load,
  settings and a visible main-menu destination remain unimplemented.
- **Accepted — Tank-control player heading (supersedes independent heading):**
  A/D yaw the player root directly at `150°/s` (scaled by the intoxication
  speed multiplier); the root never rotates toward velocity during input
  locomotion. W walks along the hero's own forward axis, S backs him up at a
  reduced `1.4 m/s`, and W±A/D follows an arc at the active walk/run speed.
  Holding either Shift or gamepad L3 requests Run only while forward input is
  positive; neither Shift alone nor a backward input becomes a sprint. The
  camera-relative steering basis and its camera-cut latch are gone — the chase camera orbits
  independently and never writes player yaw. Scripted interaction approaches
  (`WalkPlanarStep`) still face along their travel. The motor reports a
  `PlayerMotionSample` (planar velocity, signed forward speed, turn input) so
  the presentation can select `Walk`, `Run`, `WalkBack` or the `TurnLeft`/
  `TurnRight` in-place clips on its six-input locomotion mixer.
- **Accepted — Bounded inertial walk/run locomotion:** Character-relative
  input targets a `2.6 m/s` forward walk, `4.2 m/s` forward run or `1.4 m/s`
  backward maximum through the unchanged `6.5 m/s²`
  acceleration and `11 m/s²` braking. The motor feeds actual constrained
  displacement back into its next velocity step, so road edges and collisions
  cannot store a hidden impulse. Normal input release coasts, while modal
  ownership, scene transitions, input disable and teleport still stop planar
  motion immediately. The existing intoxication multiplier scales both
  forward targets and the turn rate; fatigue has no movement debuff. Scripted
  interaction approaches continue to call the walking step directly and never
  inherit held sprint input. `PlayerMotionSample.RunBlend` is derived from the
  signed speed that remains after collision and walkable-area constraints, not
  from the key request, so a blocked hero cannot run in place.
- **Accepted — Run is one production-only authored gait:** Hero V2 appends one
  bone-only, in-place `Run` Action (`0.75 s`, `18` frames at `24 fps`) to reach
  `38` production Actions at that decision; the later three-part seated drink
  brings the current total to `41`. Run is a heavy, weary forward-loaded cycle with
  stronger opposing arms, deeper knees and one short two-foot flight phase,
  blended against Walk by actual constrained Run weight. That weight
  progressively releases downward grounding; at full Run the correction may
  lift sole penetration but cannot drag both airborne boots onto the floor.
  At this decision point the byte-frozen Hero V1 and the independent
  pedestrian locomotion bank remained at `37` Actions; neither received this
  production clip. This is an ordinary locomotion decision, not an exception
  to the contextual-animation standard.
- **Accepted — Bounded cinematic chase camera:** Exterior/interior framing uses
  `2.6 m / 53°` and `2.2 m / 57°` profiles with `1.4 m / 1.3 m` raised focus
  points that compose the hero below frame center. RMB mouse motion, the
  gamepad right stick and the arrow keys drive independent yaw and pitch in
  ordinary City, Bar and Supermarket follow; pitch is clamped to `-20°..55°`.
  The arrows are a stick-style per-second axis (`150°/120°` per second) and
  no longer walk the hero — movement is WASD-only — and the seated park
  board game opts the keyboard out of its orbit sample because the arrows
  own its board cursor. Orbit yaw, pitch and
  target focus use deliberately weighty `0.20 s`, `0.18 s` and `0.18 s`
  damping; focus stays within `0.45 m` and snaps on jumps beyond `1.75 m`.
  Deterministic low-frequency idle drift and speed-driven bob affect only
  focus, pitch and roll; requested yaw remains stable, and FOV is stable
  below the balance threshold — above it the drunk dolly zoom (accepted
  2026-09-04, below) owns the lens and the arm together. Collision
  shortens the arm immediately, restores it with `0.32 s` damping and fades
  cinematic motion during fullscreen modal ownership. Balance checks disable
  orbit input but deliberately retain cinematic motion so intoxication lean
  and fall reactions remain visible. Fixed Home/Stairwell and contextual
  camera owners remain non-orbiting; the bus keeps its separate seated bounds.
- **Superseded 2026-08-04 — Eight-direction player presentation:** A corrected
  point-filtered `512x96` reference and a derived `512x864` layered atlas
  provide eight explicit `64x96` views at PPU 48. Each view has one body layer
  and upper/lower segments for both arms and legs. A signed player-camera
  angle selects the view in 45-degree sectors with 5-degree hysteresis; views
  are never mirrored and share one foot pivot. Jointed walking projects the
  actor's sagittal plane into the active billboard view: side views swing in
  screen space, front/back views swing in depth, diagonal views blend both,
  and contralateral limbs remain in opposite gait phases. Explicit
  atlas-derived contact points keep whichever foot is lower pinned to the
  visual ground plane; a `5 mm` base clearance is consumed by the footfall
  compression instead of adding an always-positive whole-puppet bob.
  Breathing and impact compression offset only the body and arm roots, while
  the projected joints retain grounded feet plus readable weight transfer and
  deterministic alternating left/right arm gestures while idle. A separate
  `512x480` body-expression
  atlas provides neutral, half-blink, closed-blink, watchful and tense rows;
  the stronger blink remains available during locomotion, watchful/tense
  states require sustained idle below strong intoxication and outside a
  balance/fall state, runtime swaps only the existing body renderer, and all
  rear variants remain neutral. The same joint hierarchy accepts continuous
  intoxication sway, arm spread, knee bend and balance lean. A failed balance
  check temporarily reuses its body renderer for a full-body `128x96` frame,
  disables the other eight visible layers and lazily slices one of 16
  point-filtered `10x8` fall atlases. Eight camera-relative views each own
  separately authored screen-left and screen-right variants, so the physical
  bandage/patch asymmetry never relies on mirroring. Explicit
  `Falling`/`Down`/`Rising` progress maps to `14`/`36`/`30` frames and restores
  the original nine-part puppet without changing renderer count.
- **Superseded 2026-08-04 — Camera-independent sprite shadow:** One collider-free
  nine-part `ShadowsOnly` puppet reuses the directional part sprites and a
  shared alpha-clipped URP shadow-caster material. It selects its authored view
  from the signed player-to-main-light angle, faces the directional light
  rather than the camera and remaps the live joint angles into that view, so
  City and BarInterior receive the animated gait, upper-body compression and
  whole-puppet sway without changing the nine visible puppet renderers. A
  separate shared four-vertex analytic contact quad stays on the player root
  instead of following `PoseRoot`, remains visible when realtime shadows are
  unavailable and supplies the stable ambient-occlusion cue beneath the feet.
  Practical street/bar lights remain shadowless.
- **Accepted — Runtime presentation:** City geometry, primitive colors and the
  shared interior are built at runtime. The hero, ambient City pedestrians and
  the stairwell cat all load as low-poly 3D prefabs; the runtime draws no
  world-space gameplay sprites any more (`BillboardSprite` is deleted — the
  cat was its last consumer).
- **Accepted — Shared rendering state:** Primitive colors use
  `MaterialPropertyBlock`; every ordinary runtime primitive explicitly shares
  the serialized Resources `RuntimePrimitiveLit` URP material so Player builds
  do not depend on Editor-only primitive defaults. Emissive and atmosphere
  effects reuse their cached specialized resources, with no per-instance
  materials or runtime `Shader.Find`.
- **Accepted — Ordinary roofs disappear into City fog:** A building is
  ordinary only when it has building land use and is not a bar, the player
  home or the supermarket. Those lots use a separate `36–52 m` height range;
  the old `5–13 m` range remains the bar envelope and the authored home and
  supermarket remain `8.8 m` and `6.4 m`. At the conservative `4 m`
  chase-camera height, the lowest roof is `32 m` away and native `0.070` Exp2
  fog leaves `exp(-(.070 * 32)^2) = 0.0066` of its source colour, below the
  accepted one-percent roof threshold. The `48 m` far plane therefore finishes
  the tallest masses without changing City visibility settings. Roof-anchored
  lot motifs and three rooftop district landmarks stay attached to the real
  roof and are intentionally swallowed with it; facade and street-level
  district language stays readable. The clipped Home fallback rebuilds
  primitive window rows through the clipped lot's full authored height; full
  City buildings use the Blender prototype's explicit all-height window slots.
  `CityWorldResult.Bounds` uses
  the larger of the
  ordinary and special height maxima rather than retaining the old `13 m`
  vertical cap.
- **Accepted — Geometry-locked district facade albedos remain a bounded legacy
  path:** the player-home shell, the supermarket's crossing-only fallback and a
  prototype crossing the Home
  half-space wear one of eight district wall albedos, two per buildable
  district, through `MaterialPropertyBlock`s on the same shared
  `RuntimePrimitiveLit`. Whole ordinary buildings instead use the v2 semantic
  24-sheet decision above. The legacy sheets are not tiled by metres.
  `CityFacadeAppearance` derives `_BaseMap_ST` from
  `CityFacadeGrid`, the single source of the bay and floor pitch that the
  window builders also read, so one authored cell covers exactly one pane bay
  and one `2.35 m` storey. Horizontal phase follows the pane-count parity; the
  vertical phase is independent of building height and takes one of four
  values, and it must include the `0.08 m` mass base or every window band
  slides up the wall. A stable per-lot whole-cell bay and floor rotation adds
  sixteen presentations per sheet without disturbing that alignment. Sheets are
  authored at `1024` rather than the project's `1254` so Unity's import to
  `512` is an exact 2:1 downsample; band and mullion edges are the whole point
  of the texture and a 2.449:1 resample softens them. Baked floor bands may
  continue behind the topmost geometric row as backing facade detail, but they
  are never the only visible window treatment: the clipped Home fallback now
  builds row-balanced geometric panes through the lot's full authored height.
- **Accepted — Linear-space facade compensation:** Legacy facade and v2
  ordinary-building albedos hold a mean
  linear luminance of `0.35` and the night facade tint is brightened by
  `1 / 0.62` before it reaches `_BaseColor`, which preserves the brightness the
  flat colour had and never clamps the brightest lot the generator can make, a
  bar at `0.616`. This deliberately does **not** reuse
  `StairwellSurfaceAppearance`'s `compensation = 1 / meanLinearLuminance`: that
  rule assumes the tint and texture multiply in gamma space, while URP converts
  both to linear first. Applied here it would have called for a mean of `0.64`
  and made every facade in the city almost twice as bright as before — a pale,
  chalky wall rather than a grimy one. The bound and the preserved brightness
  are both swept from the live colour ranges by
  `CityFacadeAppearanceTests` and `CityBuildingSurfaceAppearanceTests`, so
  widening a district palette or drifting a semantic sheet fails at its owner.
- **Accepted — District presentation is pure data with windows as the first
  consumer:** `CityDistrictPresentationPlanner` owns stable per-block keys for
  frontage, mass, windows, light and wear plus a one-block transition motif
  restricted to authored neighbour pairs. The current world build consumes
  only the window channel. The profile keeps a district lit ratio, but every
  actual facade row quantizes that ratio to an exact count, never zero and
  never the whole row when more than one pane exists. A stable row phase varies
  the chosen bays by block, floor and side, which distributes light over the
  full height without a repeated vertical stripe or the former Nightlife
  ground-floor bias. Every selected pane is warm like a street lamp; special
  Bar, Home and Supermarket families retain texture/material identity but not
  a different hue. The remaining channels are implemented inputs, not yet
  claims about built geometry.
- **Accepted — PC PS1 world composite:** The active PC renderer runs one native
  Unity 6 RenderGraph feature at `AfterRenderingPostProcessing` for the final
  Game camera. It footprint-averages the world to `640x360` by default, blends
  35% perceptual-space RGB555 into the original tone without a screen-space
  dither overlay, then point-upscales it, producing exact 2x/3x scaling at
  720p/1080p. Before quantization, the same pass consumes shared intoxication
  state for animated UV warp, ghost/chromatic sampling, warmth, exposure pulse
  and vignette. Lower `426x240` and `320x180` presets remain available; mobile
  renderer integration is deferred.
- **Accepted — Crisp UI after the composite:** Runtime IMGUI is intentionally
  drawn after the world composite instead of being degraded with the 3D image.
  Prompts, menus, modal inspectors, shops, journal, HUD, balance gauge, loading
  and map use one logical `640x360` canvas and the shared `RetroUiTheme`.
  Its interface-only language is soot/charcoal/dirty-bone: flat rectangular
  panels, thin nested frames, stable deterministic texture, restrained printed
  typography and value-plus-frame focus that survives grayscale. Packaged
  `Fonts/Roboto-Regular` is the deterministic RU/EN primary face; installed OS
  fonts are never requested because Unity 6.5 legacy IMGUI cannot reliably
  repaint their dynamic faces. Unity's legacy face is emergency fallback only. The
  former plum/orange cards, cut corners, bevel highlights and glow are not part
  of the language. The reference does not alter world rendering, camera,
  aspect, composite, audio, gameplay or localized copy. Menus, modal
  inspectors and the map omit persistent key-binding guides and control-hint
  footers.
  Clickable modal actions keep action-only labels; every active contextual
  prompt is also a full pointer target and invokes the exact same guarded action
  path as E, Enter or gamepad South instead of duplicating interaction logic.
- **Accepted — Shared low-poly cylinder:** Runtime cylinder requests replace
  the stock visual mesh with one cached flat-shaded 8-sided mesh while
  preserving the primitive collider contract. No per-instance mesh or
  material is created.
- **Accepted — Shared MVP exterior day/night lighting:**
  `GameTimeDayNightRules` returns night before `06:00`, a smooth dawn from
  `06:00-07:00`, day through `18:00`, a smooth dusk from `18:00-19:00`, and
  night from `19:00`. `CityDayNightController` applies its directional color,
  intensity/rotation, ambient, reflection and shadow sample, while
  `CityNightAtmosphere` scales the bounded lamps, bar lights, emissive bulbs
  and halos with the sample's night factor. `HomeDayNightController` applies
  the same sample to the apartment window shaft and reconstructed Balcony
  exterior. The neighbour-wall yard Spot is the explicit City-local exception:
  it lives with the open-area composition, outside `Night.Root`, and remains
  enabled at its authored intensity at every sample. Bar, Supermarket and
  Stairwell visual profiles remain unchanged.
  Presentation updates are change-driven: stable day/night samples perform no
  lighting work, ordinary dawn/dusk updates do not regenerate the environment
  cubemap. The street-light pool used to skip its anchor scan at a zero
  night factor; the §20 always-lit law repealed that optimisation - the
  pool leases and burns at the day floor too.
  Forced setup and Home Balcony entry/restore retain their complete refresh.
  This cycle does not own visibility: `0.070` luminous gray-green Exp2 fog,
  the matching terminal camera color, `48 m` far clip, `CityFogField` and
  dedicated `CityNoirVolumeProfile` stay unchanged at every hour. Custom fog
  stripping still retains the runtime Exp2 variant, and interiors keep their
  existing fog/range contracts.
- **Accepted architecture exception — 2026-08-29, explicit user request —
  the barrel fire under the [10;5]–[11;5] arch is one strong realtime local
  light:** this supersedes the shelter draft's temporary “emissive only” rule
  and the ordinary pooled-City-light assumption. The always-burning fire owns
  exactly one warm shadowed Point `Light` (`95` base intensity, `7.0 m`
  range), outside the night-fixture registry because it is causal at every
  hour. Its deterministic multi-frequency flicker never falls below `72%`,
  and the same factor drives the five-part flame, ground spill, fog halo and
  sparse non-lighting sparks, so the walls, ground and people receive one
  coherent moving source rather than unrelated decorative pulses. No second
  realtime light, particle light or zone-wide fog/exposure override is added.
- **Accepted architecture exception — 2026-08-29, explicit screenshot
  correction — the [10;5]–[11;5] shelter tableau stands on one visible service
  platform:** this supersedes the art-bible sentence that put the southern dry
  zone directly on the native upper datum without a separate platform, and is
  refined by the user's second screenshot correction that rejected a detached
  central plinth. One `7.30614 x 8.851 m` slab begins at the highest stair seam,
  keys directly into the inner face of the east facade support and reaches the
  raw south end of the common wall. It carries the barrel, both warmers,
  bedding and sleeper, and has a massive masonry support descending the full
  `1.562 m` to the lower datum. The stair landing is a logical clear area inside
  that same platform and shares its one physical collider; it is not a second
  coplanar slab. The west longitudinal `2.2 m` route remains the one full-depth
  ground passage. The east side is deliberately not claimed as a second route:
  sampled continuous terrain sits about `0.41-0.51 m` below the flat service
  slab at its open seams, above the player's `0.28 m` step offset. Full-width
  `1.09 m` north and south guard rails therefore close those drop edges; a
  third west guard closes the `7.251 m` segment south of the stair. The only
  opening remains the exact `1.60 m` stair band on the west edge. The physical
  east attachment begins at the slab's wall seam, so no collider gap remains
  behind the visible masonry. Loose clutter stays on the lower ground.
- **Accepted — The §20 always-lit law is implemented at the fixture
  factor:** `GameTimeDayNightRules.DayFixtureFloor = 2/3` and
  `FixtureFactor(nightFactor)` carry the story bible's law - every lighting
  fixture burns always, the day takes at most a third off it, and the fog
  halo is never taken away - while the raw `NightFactor` remains the SKY's
  and still reaches zero at noon. Consumers: street-lamp bulb emissives
  (`CityNightWorldResult`), the lit window families
  (`CityWindowAppearance` - §20 names each selected lit window a fixture), the
  ordinary Blender buildings' UV2-slot shader (it receives a separate
  `_CityWindowFixtureFactor`, never the sky's raw zero), the
  glow registry (`DeadGlowFraction` now IS the day floor; the dead tube is
  repealed), the site-light registry (authored day intensities survive only
  above the floor; no registered light is ever disabled; halos stay
  visible), the pooled street/bar/practical realtime lights
  (`CityNightAtmosphere`, including the lease scan), the vista's distant
  city windows, the bus cabin plafond (the plafond that moves; headlights
  and tail lights stay excluded as events), and the summit practicals
  (`YardLampNightBoost 0.55 → 0.5`, day exactly two thirds). The faulty
  tunnel practical keeps its flicker ON TOP of the floor - a fault is
  character, not a schedule. A pooled bus hard-offs its runtime lights and
  clears its `hasAppliedNightFactor` gate, or a respawn at a constant noon
  would skip the refresh and hold the plafond dark until dusk.
  `AlwaysLitLawTests` pins the number, both registries and the mountain
  ratios; the re-anchored fixture tests pin each consumer.
- **Accepted — Exterior windows use emission, not realtime Lights:** special
  Bar/Home/Supermarket panes reuse the packaged `Ps1Lit` emission variant and
  the same window sheet for albedo and emission; ordinary Blender prototypes
  keep emission in `CityBuildingWindowSlots`. Both paths use the same `0.48`
  emission strength and §20 fixture factor. Dark panes stay dark, every lit
  pane is warm like the street lamps, and the City realtime-light budget
  remains exactly `12`. The bar is the authored exception to sampling
  the sheet: its glass has metre-scale planar UVs and separate sash geometry,
  so its lit renderers override both maps with white instead of clamping to
  the atlas's dark border; their shared material still owns colour and power.
- **Accepted architecture exception — 2026-08-29, explicit user correction —
  window light is sparse, warm and vertically distributed:** this supersedes
  the cold Industrial window family and the Nightlife rule that concentrated
  light on the front ground floor while leaving upper/rear rows dark. It does
  not lift the art-bible ban on all-lit facades. Each declared row quantizes
  the district ratio to at least one lit pane and, for rows wider than one, at
  least one dark pane; a stable floor/side phase distributes those panes over
  the whole building. Every selected pane remains on through the §20 fixture
  floor and uses exactly `CityNightAtmosphere.StreetLampColor`; frames,
  curtains, blinds, stable brightness variants and dark panes prevent a
  uniform glowing grid. This City rule does not alter Alpine Village dimming.
- **Accepted — Bounded local fog:** One seeded, player-following
  `CityFogField` adds slowly drifting world-space fog with at most 36 particles
  and a bounded `0.120` peak alpha. It reuses the shared atmosphere material
  and has no collision, trails or particle lights. Every exterior area runs
  the SAME field — City, Home balcony, Mountain Road and Alpine Village — with
  its own seed and nothing else changed: no per-area tint, size, rate or
  gradient, because a zone's own fog is exactly what the art bible forbids.
  What stays per-area is the Exp2 haze BEHIND it, which the particle shader
  already mixes into every sheet through `MixFog`. Outside the City the fog's
  shelter rides `CityWeatherController` rather than a second controller: the
  mountain road and the village have one shelter predicate each (tunnel plus
  terminal, station canopy) and the weather owner is already polling it, so
  the fog is cleared and refilled by the same call that gives the snow its dry
  core. The City keeps `CityTunnelShelterController` and passes no fog to the
  weather owner, because there the same event must also hide the ridge shell.
- **Accepted — One camera-relative cloud ceiling for every true exterior:**
  a deterministic Blender build owns one passive `220`-triangle unit
  hemisphere and one packed linear RGB density texture; runtime owns three
  profiles through `MaterialPropertyBlock`, never cloned materials. The field
  follows camera translation but retains a canonical compass rotation. Its
  `47 / 119 / 109 m` radii sit just inside City, MountainRoad and
  AlpineVillage far planes (`48 / 120 / 110 m`), but are rendering distance,
  not a physical cloud base: translation produces no parallax, so the layer
  cannot read as clouds hanging tens of metres above roofs. City and the Home
  balcony use the same seed, profile, compass frame and phase from absolute
  session time; the road and village only change density, scale and colour,
  while all three integrate the existing `GameWeatherRules` wind schedule.
  The horizon is mixed into the haze already owned by each area; the village
  passes its live haze/storm/warmth values into that same property block. The
  cloud system creates no Light, shadow, collider, fog, grade or visibility
  rule. It exists in City, MountainRoad and AlpineVillage, is enabled in Home
  only for the active Balcony shot, and is absent from true interiors and
  technical transition scenes. The fountain reflection camera opts in with a
  marker; unrelated preview/UI cameras do not inherit the world shell.
- **Accepted — The city never dries (city-scene decree):**
  `CityEternalRainShaper` is the city's own `ICityWeatherShaper` - the seam
  the mountain and village already shape the shared schedule through. It
  floors precipitation at `DrizzleIntensity 0.18` (visible rain, clearly
  under LightRain's `0.45`; wind passes through untouched - drizzle in calm
  air is a real state of the sky) and is attached to the city's weather
  controller plus the four direct schedule readers: the city rain field's
  init, the river's build-time water hook, the bus's wiper intensity and the
  home balcony's exterior view (which shows the same sky and must agree).
  The schedule itself is untouched - determinism, kinds, lightning and the
  areas above keep their own weather, and a "Clear" slot at the mountain's
  tunnel mouth stays genuinely dry. The shared wet film consequently never
  reads drier than the drizzle in city scenes, and puddles never dissolve;
  the drying machinery stays for the slopes between heavy and drizzle.
  Amended 2026-08-31: the film's target is the shaper's own third axis,
  `ShapeSurfaceWetness(schedule)`. The controller had been handing the
  registry the raw schedule so the village blizzard could not soak the
  shared film - which also cut the city's floor out of the film path, and
  a `Clear` slot dried every puddle away while the drizzle kept falling.
  The city floors that axis at the drizzle; the snow areas pass the
  schedule through untouched.
- **Accepted — Deterministic slot weather, presentation-only:**
  `GameWeatherRules` is a pure function of the city seed and the absolute
  session minutes: `90`-game-minute slots hash into Clear (`55%`), LightRain
  (`27%`), HeavyRain (`12%`) or Thunderstorm (`6%`), and the continuous
  intensity smoothsteps between the slot targets (`0` / `0.45` / `1.0` /
  `1.0`) over the first `5` game minutes of a slot. Because the sample is
  derived, no new session state is persisted and City and the Home balcony
  can never disagree. Presentation is a seeded player-following
  `CityRainField` (at most `420` stretched streak particles over a `26 m`
  box on the shared atmosphere material, no collision) plus a deterministic
  crossfaded noise loop (`CityRainSound`, `Ambience/Beds`), driven per frame
  by `CityWeatherController` in City and by `HomeBalconyExteriorAtmosphere`
  on the balcony; balcony audio and flashes are gated to the active Balcony
  shot. While `CityBusRideController.IsPassengerAboard`, the emitter switches
  to a donut with a `10 m` rain-free core so streaks never spawn inside the
  cabin. `CityWetSurfaceRegistry` owns the one transient rain film shared by
  City and the Home balcony: it wets at `0.58/s`, dries at `0.028/s`, catches
  up from absolute game time on a scene handoff and resets with a new session.
  Ground, road, sidewalk and marking recipes multiply their authored dry tint
  and raise smoothness through MPBs on the existing shared material. A pure
  stable-rank planner chooses at most `42` road patches and one builder emits
  their upper faces as a single collider-free mesh `3 mm` above the road; dry
  puddles return to the road recipe instead of leaving raised boxes. The rain
  deliberately does not touch `GameTimeDayNightRules`, ambient/directional
  daylight, fog, grade or far clip: daylight dimming and weather grading remain
  separate future decisions.
- **Accepted — Deterministic lightning outside the pooled light budget:**
  Lightning shares the pure schedule: each `12`-game-minute window hashes
  into at most one strike (`70%`) whose start offset, azimuth and distance
  are gated to fully developed Thunderstorm slots, so every scene evaluates
  the identical storm without extra state. The flash is one transient
  shadowless directional `Light` (`CityLightningFlashLight`, peak `1.9`
  scaled down to `45%` at the far distance band) that stays disabled outside
  its `0.5`-game-minute flicker envelope and lives outside `Night.Root`.
  Because it is directional and transient, it is counted separately from the
  bounded `18` local realtime lights and does not change the pooled
  atmosphere/bus assertions. Thunder is a deterministic synthesized one-shot
  (`CityThunderSound`, `Ambience/Details`, two rotating voices on child
  objects because a low-pass filter processes its whole GameObject) played
  `0.6-3 s` after the flash with distance-scaled volume and cutoff. A frozen
  clock — pre-wake or `timeScale = 0` — suppresses the flash instead of
  holding it lit, and strike IDs deduplicate thunder across frames.
- **Accepted — Depth-tested light bloom:** Each active street/bar light and
  amber signal lens can own a two-particle `CityLightHalo`. The shared
  `Resources` shader softens depth intersections, so glow diffuses in fog
  without remaining visible through solid geometry.
- **Accepted — Data-first night fixtures:** `CityNightFixturePlanner` derives
  two lamps per road edge and consumes the shared street-intersection selector
  for at most six signalized degree-3+ intersections before GameObjects exist.
  The same ordered nodes own zebra crossings. Visual lamp fixtures and bulbs
  are combined into separate `48 m` meshes while lightweight anchors preserve
  the pooled-light contract.
- **Accepted — Bounded practical lights:** All bulbs and signal lenses reuse
  one HDR URP Unlit material; a player-relative pool of directed street spot
  lights plus bar entrance point lights keeps the city-atmosphere pool at no
  more than `12` shadowless realtime lights. The sole active bus may add `4`
  runtime-owned shadowless Spots, the single pooled helmet-lamp pedestrian may
  add `1`, and the fixed always-on yard Spot adds `1`, for a bounded worst case
  of `18` local realtime lights. The scene Directional and transient lightning
  Directional are separate from this local-light count.
- **Accepted — Safe signal rhythm:** Each selected intersection uses one
  seed-phased controller for two heads and flashes amber below 1 Hz; red and
  green lenses remain dimly visible without realtime lights.
- **Accepted — One canonical audio mixer:** `GameAudioMixer` loads one
  `Resources/Audio/Mixers/BarPromenadeAudio` asset and resolves exact
  `Music`, `Ambience/Beds`, `Ambience/Details`, `SFX/World`,
  `SFX/Gameplay` and dry `UI` groups. Scene roots select City, Bar,
  Stairwell, Home or DoorTransition snapshots; non-door profiles transition
  over `0.25 s`, while DoorTransition cuts immediately at blackout.
  The mixer keeps `-6 dB` master headroom under a compressor and owns
  dedicated Receive/Reverb and Receive/Echo returns. Music, ambience details
  and world SFX feed scene-specific send levels; Home stays short, damped and
  echo-free, while Stairwell uses the longest/strongest reverb with a dark
  high-frequency rolloff plus restrained stereo echo.
- **Accepted — The mix hierarchy is invariant across scenes:** Snapshots may
  change room response, not the order of information. Every snapshot authors
  Music `-5.5 dB`, Ambience/Beds `-4 dB`, Ambience/Details `+0.5 dB`,
  SFX/World `+2 dB`, SFX/Gameplay `+2.5 dB` and dry UI `+1.5 dB` below the
  shared `-6 dB` Master. Detail/world send trims compensate their dry-bus
  lifts, preserving the previous wet-tail energy while attacks move forward.
  Thunder is a readable world event and the wake alarm a gameplay signal;
  neither shares the continuous ambience tier.
- **Accepted — Bus audio belongs to visible mechanisms:** The pooled Route 01
  actor owns exactly four fully spatial voices. Two sit in the rear motor
  compartment: a mid-rich exterior diesel whose linear `24-48 m` tail is tied
  to `RuntimeSceneSetup.CityFarClipPlane`, stays silent throughout the
  `76-86 m` hidden spawn band and rises only inside the rendered street slice;
  and a distinct chassis loop faded in only while the hero is an attached
  passenger. The two other voices
  sit above the actual front/rear entry anchors and play low-rate pneumatic
  opening or closing clips once per real door-phase edge. NPC occupancy never
  enables the cabin mix, generic door SFX cooldowns cannot suppress either
  doorway, and pooling stops and resets every voice.
- **Accepted — Reproducible mixer authoring:** The committed mixer is created
  and updated through `AudioMixerAssetSetup`, which uses Unity's editor API,
  preserves one exact topology across repeated runs and fails when a required
  effect, send or echo parameter is unavailable. EditMode coverage validates
  the DSP graph, send targets and critical snapshot values rather than only
  checking group names.
- **Accepted — Scene-local music with guarded fades:** `CityMusicPlayer` loads only `city_theme`
  from `Resources/Audio/CityMusic`, while `BarMusicPlayer` loads only
  `bar_theme` from `Resources/Audio/BarMusic` and
  `SupermarketMusicPlayer` optionally loads only `supermarket_theme` from
  `Resources/Audio/SupermarketMusic`;
  `StairwellMusicPlayer` optionally loads only `stairwell_theme` from
  `Resources/Audio/StairwellMusic`; `HomeMusicPlayer` optionally loads only
  `home_theme` from `Resources/Audio/HomeMusic`. Each clip loads through its
  importer mode into a looping `AudioSource` and the shared `Music` group
  under its matching scene root. Ordinary scene themes remain non-spatial
  behind a `12 kHz` low-pass. Bar is the deliberate diegetic exception: its
  source sits at the visible jukebox grille, uses linear three-dimensional
  attenuation and the cabinet's `120 Hz` high-pass, `5.6 kHz` low-pass and
  light saturation. The six
  present masters are EBU-R128 measured and independently source-trimmed so
  their roughly `8 LUFS` raw spread converges near `-30.5 LUFS` after the
  Music and Master buses; a replacement master must be remeasured.
  `SceneMusicPlayer` owns a smooth unscaled gain envelope, waits for clip data
  before starting it and fails silent if loading fails. Home alone reads the
  fixed-camera Balcony shot: it fades `home_theme` to zero, pauses while
  preserving the sample position and resumes through the same envelope only
  after the shot returns indoors.
- **Accepted — Layered, physically owned bar soundscape:** Bar keeps one
  non-spatial room-pressure bed for ventilation, mains and occupied air. Two
  independently synthesized `8 s` mono crowd beds are fully spatial and sit
  on layout anchors at the real booth and table groups; they contain no words.
  A single bounded World voice moves between the counter and those two groups
  for deterministic glass, chair, bottle and short wordless crowd events every
  `4.5–8 s`. The jukebox adds one short-range Details voice for motor, record
  surface and sparse crackle at the same grille as the music. Six scene-local
  sources replace the former four-source bar budget; all use the existing
  mixer hierarchy and Bar room send, so no new bus, global reverb or echo is
  introduced. `BarJukeboxInteraction` is also the single property-block writer
  for the cabinet's amber panel and two pink tubes. Three slow phase-shifted
  pulses follow `BarMusicPlayer.NormalizedGain`, and the existing interaction
  flash is composed over the panel pulse. The effect changes only shared
  emissive material properties: it creates no material instances, realtime
  `Light` components or strobing whole-room illumination.
- **Accepted — One mixing rule for every music change:** `MusicMix` holds the
  whole rule: `FadeOutSeconds = 4`, `FadeInSeconds = 1`, and a registry of the
  sources that are still leaving. A theme starts only through
  `BeginFadeInThroughRule`, which refuses to sound while
  `MusicMix.IsFadeOutActive` and retries each frame, so themes hand over
  instead of crossfading. Because a scene unload would cut a four-second tail
  dead, `MusicMix.BeginDetachedFadeOut` reparents the departing music object
  out of its scene into `DontDestroyOnLoad` — the same `AudioSource` keeps
  playing, so a streaming clip is never re-seeked — and the player destroys
  its own carrier when the fade reaches zero. That removes the old activation
  gate: `SceneTransitionService` now only asks every `IMusicMixSource` in the
  outgoing scene to leave, and the tail finishes across the door presentation
  while the destination streams in. The registry self-prunes on destroyed,
  stopped or silent sources, and a source releases itself before fading back
  in so a theme never waits on its own tail. Before that detach, the spatial
  bar exception measures the long-range theme and short-range cabinet
  attenuation independently and preserves both levels while their carrier is
  temporarily non-spatial; otherwise either the next scene's distant listener
  would cut the tail or the close mechanism would jump louder at the door.
  `HomeSmokingMusicPlayer`
  implements the same interface: it defers `BeginFromStart` until the mix is
  clear, eases in over `FadeInSeconds` when it had to wait, and leaves through
  `BeginRuleFadeOut` at the `Exiting` phase instead of the shorter
  camera-restore ramp.
- **Accepted — Music bound to a place, not only to a scene:** `City` keeps
  `city_theme` as its default and hands the mix to a place theme whenever the
  hero stands on grounds that have one. `CityLocationMusicDirector` holds a
  table of `CityLocationMusicSlot` (`locationId`, world-XZ `Rect`, player) and
  resolves the active place each frame through the pure
  `CityLocationMusicZones.Resolve`, which keeps the active place until the
  hero is `ExitMarginMeters` (`4 m`) clear of its grounds — without that hold
  a walk along a fence would flap the mix, and every flap costs a full
  fade-out and fade-in. The handover is the shared rule at its one length —
  `MusicMix.FadeOutSeconds`, the same `4 s` as a scene change — so there is a
  single number for every music change in the game. Place themes are parked
  with
  `FadeOutAndPause(0f)` at initialization and resume from their own sample, so
  leaving and returning continues both tracks where they stopped. A slot whose
  optional clip is absent is dropped at initialization rather than accepted —
  an empty slot must never be able to silence the city. The first observation
  performs no handover: whatever the hero is standing in simply owns the mix.
  The only slot today is the cemetery, whose grounds come from
  `CityCemeteryPlan.Grounds`; a seed without a cemetery contributes no slot.
- **Accepted — Generated retro SFX:** `RetroSfx` deterministically synthesizes
  the mono `22050 Hz` UI, footstep, door latch and sustained hinge creak
  clips in memory.
  `RetroAudioService` persists across scene loads, reuses bounded UI/world/bar
  source pools, routes them to dry UI, world SFX or gameplay SFX, and applies
  per-effect cooldown and concurrent-voice limits.
- **Accepted — Diegetic opening alarm:** Home always builds one validated
  bed-relative nightstand and collider-free low-poly alarm clock. The clock
  owns one reusable four-digit, 28-segment display that keeps briefly
  flickering `05:59` before and after the five-second menu boundary, then
  switches to solid `06:00` only when Wake Up is chosen without rebuilding
  geometry, plus one generated looping mono `22050 Hz` mechanical ring on a fully
  spatial `SFX/World` source and applies a bounded visual rattle while active.
  The menu remains silent; choosing Wake Up starts the ring together with
  `06:00` and the session clock, keeps the clock shot and sleeping loop for
  exactly three unscaled seconds, then stops the ring before camera motion and
  the wake animation begin. The display follows current session hours/minutes
  thereafter and on later Home visits while remaining silent.
- **Accepted — Layered scene-local procedural ambience:** Every playable root
  owns one quiet deterministic `22050 Hz` ambience bed and tone filter routed
  to `Ambience/Beds`. Home additionally owns five spatial
  `Ambience/Details` sources and eight runtime clips: distinct closed/open
  refrigerator loops, a balcony loop, four sparse cue types and one dedicated
  bathroom-tube crackle. The crackle source is co-located with the visible
  tube and reacts in the same frame to every applied flicker-factor change.
  The two refrigerator layers are raised by `4 dB`; their sources remain
  co-located, scheduled at the same DSP time and
  mixed from door openness with clamped cosine/sine equal-power gains; closed
  and open states therefore use different deterministic eight-second mono
  timbres instead of filtering one clip. Stairwell retains three spatial
  sources and six clips: two quantized eight-second loops plus four cues. Pure
  seeded schedules bound pitch, gain and delay; a data-first anchor planner
  maps sources to refrigerator/balcony/domestic fixtures at Home and
  ventilation/electrical/pipe/debris/water/door fixtures in Stairwell.
  Reinitialization reuses owned sources and clips, enable/disable restarts or
  stops both synchronized refrigerator loops together, and Single-mode cleanup
  destroys every scene-local source and runtime clip while the persistent
  pooled SFX service remains available to the next scene.
- **Accepted — A City sound needs a physical owner:**
  `CitySoundscapeAnchorPlanner` may convert only geometry already present in
  `CityLayout` and `CityDecorationPlan`; it has no fallback coordinates and a
  missing fixture means silence. Each immutable descriptor carries a stable
  ID, district, semantic owner, exact visible bounds, cue, radius and one of
  three causal modes: loop, autonomous scheduled one-shot, or physical-action
  one-shot. Cue validation fixes that mode — a waterworks drip, wind-driven
  rope creak or broken-speaker chime may schedule itself, while carpet impact,
  weighbridge stress and the future swing creak may not exist without a real
  owner event. The default city produces ten descriptors: five loops, three
  autonomous details and the carpet/scale actions. `CitySoundscapeDirector`
  owns a hard nine-source pool (five loop, three scheduled, one action), lazy
  deterministic `22050 Hz` mono clips, a global detail-silence interval and
  coarse line-of-sight attenuation through authored building masses. The
  carpet cue subscribes to the exact authored strike frame; the scale cue
  follows the real needle's loaded crossing. The park swing remains
  silent until its motion has a first-class registry rather than a hierarchy
  search. The old City bed now contains diffuse air only and is kept at
  `0.025`; rain remains non-spatial because it surrounds the listener. Surf is
  one source at the nearest point of the finite real waterline, reuses the same
  quarter-second building-mass attenuation and avoids duplicated-loop phase
  seams, while thunder is placed at its deterministic lightning azimuth. This
  is the boundary between diegetic ambience and filler:
  every localizable sound must answer what visible system emitted it.
- **Accepted — Diegetic bar identity:** Supported bar lots keep their warm body color and
  add amber windows, a framed canopy and one collider-free pixel mug sign.
  Active signs share one generated sprite and use the existing upright
  billboard behavior, so recognition does not depend on color alone. Inside,
  the validated shared layout and all circulation remain fixed while one
  `BarDistrictIdentity` recolours shell, floor, counter, upholstery, glass,
  signs and practicals. The shipped default uses the Residential worn-surface
  and curtain profile; Old Town ledger/portraits, Industrial safety band/pipes
  and Nightlife cyan/magenta neon remain authored compatibility variants for
  explicit layouts, all over one geometry contract.
- **Accepted — Both spade acts are a choice, not a swing:** Digging and
  filling ask the hero to pick one of six squares and press `E`; there is no
  timing bar in either. There was one, with five kinds of ground behind it
  (turf, loam, clay, stone, root) each with its own bite window, and it is
  gone at the user's direction along with the whole soil system: `18` timed
  swings is the same shot demanded eighteen times, and a kind of ground the
  hero cannot see and cannot answer differently is not detail. What carries
  the act instead is the lattice rule — no segment may go deeper than its
  shallowest neighbour — which is a decision about where to work and is
  visible in the hole, because the segment under the spade is outlined down
  there by `CityCemeterySegmentFrameWorldBuilder` rather than on a panel. The
  HUD for both acts is one line naming two keys. The one timed swing left in
  the job is the three blows that set the stone, and `CemeterySwingProfile`
  now states its shape where it is used instead of in a table of soils.
- **Accepted — The gravedigging animates the tool, not the hero:** Every act
  of the gravedigger's job runs as a modal session that drives one procedural
  spade (`CityGravediggerShovelWorldBuilder` +
  `CemeteryShovelAnimator`) and never touches the player rig. The camera takes
  the hero's own eye line and `PlayerPresentationVisibility` leases his body
  out of sight for the duration, so there is no visible hero to disagree with
  the tool and no authored clip to keep in step with it. This is an explicit
  user decision and a deliberate deviation from
  `ai/contextual-animation-standard.md`, which governs interactions that take
  ownership of the hero presentation; this one takes ownership of the camera
  and hides the presentation instead. The framing is load bearing, not art
  direction: any shot that put the hero back in frame would show a man
  standing perfectly still beside a spade digging by itself. The upgrade path
  is already in the project if it is ever wanted —
  `Player3DAssetRegistry.Anchors.RightGrip` with
  `HomeTeethBrushingInteraction.EnsureProps` for the prop and
  `HomeTeethBrushingArmPose` for a procedural arm, no Blender work required.
- **Accepted — Minigames return, in the city and by a new design:** The
  gravedigging acts are the first minigames since the sprite-era cut below.
  That cut removed four *bar* minigames along with the sprite art they were
  built on and left `BarMinigameModalLock` standing "as the generic gameplay
  modal lock"; `ai/project-overview.md` records that any future activity
  starts from a new design. These are that new design, they live in the city
  rather than the bar, and they reuse the surviving modal lock rather than
  reviving `BarMinigameCatalog`/`IBarMinigame`.
- **Accepted — Sprite era ends, minigames cut:** All four bar minigames
  (cocktails, beer pong, Split the G, Tinctures in a Row), their domains,
  presentations, atlases and localization keys are removed from the project
  entirely, together with the sprite bar crowd and the sprite supermarket
  cashier. `BarActivityKind` and its pure ordinal resolver survive purely as
  interior flavour: the assigned kind still selects layout dressing (beer-pong
  table, stage) but constructs no controller and no `BarActivityStation`.
  `BarMinigameModalLock` survives as the generic gameplay modal lock used by
  maps, shops and inspectors.
- **Superseded in production 2026-09-03 — random 3D bar patrons from the city
  pool:** the first pass sampled the general pedestrian order, seated every
  `SeatedPatron` at one generic `0.46 m` height and left standing roles idle.
  That contract could place garment-incompatible designs in booths and left no
  distinct counter-seat or table-action semantics. Production now uses the
  explicit furniture-bound `11`-person composition recorded in Current facts;
  the shared pedestrian prefabs and seating substrate remain, but random cast
  inheritance and the generic seat height do not.
- **Superseded in production 2026-09-03 — the six-armed bartender pass:** on
  2026-08-29 the provider-bound `BarBartender` prefab first staffed the anchor
  as a complete `1.75 m` six-armed NpcHumanV2 figure. The asset and its legacy
  provider reference remain, but production now selects the ordinary two-arm
  replacement recorded in Current facts above. `BarBartenderWorldBuilder` and
  `BarBartenderServiceChoreography` continue to own the same anchor and
  physical service boundaries.
- **Accepted by explicit user decision, 2026-09-02 — the active cashier is
  normal and the Watcher is retained, not deleted:** the production provider
  now selects `supermarket_cashier_v1`, an ordinary-proportioned `1.75 m`
  version of the same clerk (`1.0.0`, `40` meshes / `1,244` triangles). Uniform,
  detail atlas, attentive face, planted checkout pose, blink, `28°` bounded
  eye/head tracking and talk stub remain; the neck has ordinary
  human length, never scales, and the head never leaves the body. The active
  source/model/prefab retain the canonical generic names
  `SupermarketCashier3D.{blend,png,fbx,json}` and
  `SupermarketCashier.prefab`. The former `2.05 m`, `44`-mesh / `1,588`-triangle
  `watcher_cashier_v1` is preserved as
  `SupermarketWatcherCashier3D.{blend,png,fbx,json}` plus
  `SupermarketWatcherCashier.prefab`, but no production provider references it
  and ordinary gameplay never instantiates it. Both share
  `SupermarketCashier3DDetailAtlas.png`. This is a one-for-one cast replacement,
  not a new resident: the active humanoid cast does not grow, while the on-disk
  appearance catalog now contains `29` designs (`8` bizarre, `21` normal)
  after the analogous bartender replacement retained both bartender assets.
- **Superseded for production on 2026-09-02; retained as an inactive asset —
  The Watcher Cashier:** the former supermarket checkout was staffed
  by one bespoke animation-free 3D clerk (`watcher_cashier_v1`) built by
  `tools/build-supermarket-cashier-3d-model.py` on the exact shared 31-bone
  Player Avatar. His signature long neck is five rigid segments on exported
  `PIVOT_Neck.01..05` empties: the runtime re-parents the segments under the
  pivots and folds the pivots into a chain off the neck bone (the wheelchair
  mechanism pattern), so the shared Avatar and every 31-bone validator stay
  untouched while the chain spans up to `18 m` (about `32.7x` its `0.55 m`
  rest length), bends serpentine on per-segment shares and carries the
  deliberately undersized head after its tip. The retained prefab lives outside
  Resources and is validated passive (no
  Collider/Rigidbody/AudioSource/Light/Camera). In its former production
  configuration, the provider/factory path also gave the clerk a
  `PlayerAttentionMagnet` at `2.0 m` so the hero and the clerk could catch each
  other staring.
- **Superseded with the active Watcher; retained asset history — pursuit-curve
  neck solve:** the chain is not rotated by
  per-joint shares; each frame the five pivots are laid explicitly along a
  cubic staple from the neck base to the head target — the hero's face
  plus a `0.85 m` standoff and `0.25 m` lift, clamped to the hall box.
  The reach is effectively unbounded (`18 m` cap), so the face follows
  the hero to every corner of the `16 x 11` hall. When the curve touches
  any margin-expanded (`0.22 m`) shelf or fixture AABB, both cubic
  controls lift to a shared clearance height (`tallest obstruction +
  0.5 m`) at `t = 0.2/0.8`, making the chain climb out of the counter
  fast, travel above the aisles and descend only at the hero; the
  resulting curve is re-sampled against every obstacle and the clearance
  raised until nothing clips (up to four attempts, ceiling-clamped).
  Each pivot turns its rest up-axis onto the local
  curve direction and scales its rigid segment to span the gap; the head is
  pinned to the curve tip by its authored neck-attachment point
  (`InverseTransformPoint` captured at bind), so head rotation happens
  around that joint — never around the distant canonical head bone — and
  the head cannot tear off the chain.
- **Superseded with the active Watcher; retained implementation history —
  room-wide cashier surveillance:**
  `SupermarketCashierSurveillanceState` owns the numbers — a pursuit
  weight eased by `SmoothDamp` (`0.55 s` out, `0.18 s` in) toward a
  distance target: ordinary neck inside `2 m`, full pursuit beyond `4 m`
  and a smooth band between. A caught-looking startle enters at
  `dot > cos 22°` held `0.15 s`, releases at `dot < cos 30°` held
  `0.8 s`, caps extension at `0.30` and holds the guilty retract for at
  least `4 s` from the notice. It freezes the idle scan, pinches the
  pupils and suppresses blinking `1.2 s` past release.
  `SupermarketCashierBlinkState` owns the rare `6.5 s` blink cycle that
  restarts from zero after every suppression, so the stare after being
  watched is always a full unbroken cycle. Both are Unity-free and covered
  by EditMode tests; the presentation only renders their outputs, restoring
  the imported rest pose every frame like the bus driver.
- **Accepted — Cashier talk stub:** A separate trigger object in front of
  the register carries the booth/dumpster placeholder contract
  (`interaction.talk_cashier` / `supermarket.cashier.placeholder`); the
  passive prefab itself stays collider-free.
- **Accepted — Bathroom scene skeleton and standard exceptions:** The
  full-body clip set is closed (adding clips regenerates the production
  hero FBX), so the three bathroom scenes run on one shared skeleton
  (`HomeBathroomSceneInteraction`) that keeps the runtime contract of
  `ai/contextual-animation-standard.md` — constrained visible walk-in via
  `PlayerMotor.MoveTowardsInteractionPose`, one neutral settle frame,
  modal `BarMinigameModalLock` capture, Bézier camera from the pinned
  bathroom shot with the shared smoking drift, debounced stop input,
  commit only on completion, idempotent restore + `ReapplyActiveShot` —
  while replacing authored clips with three RECORDED EXCEPTIONS:
  (a) the shower hides the standing Idle hero behind the drawn curtain
  group (scale-x animation of the folded panels); (b) the toilet is a
  privacy cut — the camera retreats to the ajar-door frame and the hero
  stays off-frame in Idle while the cistern and flush play; (c) teeth
  brushing poses a procedural additive CCD right arm atop Idle
  (`HomeTeethBrushingArmPose`, capture-solve-slerp each LateUpdate at
  order 300 — the bus-driver/cashier idiom) driving a RightGrip
  toothbrush prop oscillating at the Mouth anchor with a head
  counter-yaw.
- **Accepted — Mirror-camera brushing scene:** The close-up shoots from
  7 cm in front of the mirror plane back into the hero's face (FOV 36) —
  the PS1 "reflection" without RenderTextures; the pinned bathroom shot
  is never edited, scene poses are transient. Foam blobs ride the Mouth
  anchor; the rinse dips the camera look-at to the basin over two Pour
  beats. Stress relief is gated once per `GameDayIndex`
  (`TryCommitTeethBrushingRelief`); toilet and shower commit ungated
  (`CommitBathroomStressRelief`) — always on completion, never on
  cancel.
- **Accepted — Shower stall rebuild:** The stall keeps its footprint,
  tray collider and pinned names while gaining an L-rail, a
  four-fold animatable curtain group (gathered scale 0.55 <-> drawn 1.0)
  plus a static side run, a wall mixer with red/blue cross handles, a
  four-segment sagging hose, a tilted bell head with a dark nozzle
  plate, tray rims, a drain and a soap shelf. The water is a sixth
  owned `HomeSoundscape` source with a seamless loop-phase hiss
  (`SetShowerWaterAmount`, volume + low-pass crossfade) plus code-built
  stream/steam particles on the shared atmosphere material — no lights,
  no colliders.
- **Accepted — Clock-driven apartment mood:** `HomeDayNightController`
  now modulates the whole indoor mood, not just the window. The window
  keeps its exact day (`8.25`, warm) and night (`5.25`, blue) poles —
  test-pinned — and passes through a dusk amber
  (`1.0/0.56/0.30`, blend `0.65`) that peaks mid-transition and vanishes
  at both endpoints. The main lamp swings `2.30 (day) -> 4.10 (night)`
  and deepens its orange, the entry spot lifts `8.0 -> 9.4` (never
  below the test floor of `8`), and `RenderSettings.ambientLight` plus
  the directional fill lerp from a warm bright day to a cold dark night
  (`0.44 -> 0.22`, blue-grey). The Balcony shot is respected: the
  indoor ambient/sun mood is skipped while the balcony borrows City
  lighting and reasserts itself the moment the shot returns inside.
  Light count is unchanged — the five-light budget stands.
- **Superseded 2026-09-02 — Runtime-primitive corner CCTV heads:** The
  passive Blender-interior decision above replaced the primitive housings
  with four authored mount/head-pivot assemblies at the current plan parity:
  `0.62 m` corner inset and `0.50 m` head drop. Runtime retains only the
  controller objects: each initializes on the hero and servos its authored
  `cctv_head_XX` pivot at `240°/s` (`Quaternion.RotateTowards`). The
  fake-emissive recording LEDs still cast no shadows, and the assemblies add
  no Collider or Light.
- **Accepted — Supermarket fluorescent light budget:** The hall moves off
  the single flat directional onto an explicit six-practical budget
  (`SupermarketInteriorAtmosphere`): one shadowless cold point under each
  of the four fluorescent rows (`1.45` intensity, `8.4 m` range), one
  warm accent over the checkout — deliberately the only warmth in the
  hall, pooled on the active cashier — and one cool spill by the cold
  shelf. The directional fill remains at `0.72` intensity with `0.45`
  shadow strength and stays the scene's only shadow caster. Row three
  flickers on a deterministic
  stepped pattern (`0.11 s` steps, dips to `0.30`), dimming both its
  light and its fake-emissive tube tint through a MaterialPropertyBlock.
  The budget is test-enforced: six lights, none directional, all
  shadowless.
- **Accepted — Debug window without a launcher:** The City and BarInterior
  roots still install the F9 debug window, but it owns only deliberate test
  controls: the Left/Right arrow keys or clickable buttons change the real
  session intoxication by `-20/+20` and clamp at `0/100` while preserving
  last-drink and consumed-drink context, and City exposes the test-teleport
  toggle. Opening it still closes a conflicting city map or drink service
  before capturing the modal state.
- **Accepted — Session-only drinking persistence:** Intoxication, last
  alcoholic drink, total consumed-drink count and explicit alcohol-value
  stress relief are committed through `GameSessionState` by the physical bar
  counter service and inventory consumption; they survive scene loads and
  reset when the application subsystem restarts. Water relieves no stress.
  Aggregate debug state changes do not simulate a drink. The remaining
  balance-check delay and consumed deterministic sequence share that
  scene-persistent session lifetime.
- **Accepted — Physical first-person bar retail:** Every generated bar derives
  one validated `BarDrinkServicePlan` from its existing counter/back-bar
  layout. The world builder reserves the lower central shelf for exactly four
  stable visible-menu bottle roots and builds each with renderers, a solid collider,
  a larger selection trigger, a kinematic non-gravity Rigidbody and a mouth
  anchor. Five shared low-poly vessel meshes cover tumbler, the compatibility-
  named `Pint` slot rendered as a compact handled beer mug, wine glass, shot
  glass and snifter; transparent glass and liquid resources are shared,
  while per-drink colors and highlights use property blocks. A pure unscaled
  timeline owns camera approach, persistent browsing, pickup, vessel
  placement, pour/fill, bottle return, an exact three-second drink, empty
  vessel return and the explicit-exit camera return. The bartender owns bottle
  pickup, pour and return through deterministic kinematic poses and one
  reusable world-space liquid stream rather than a free physics/fluid
  simulation. During the hero's drink, a prefab-derived camera-local rig keeps
  only the vessel attachment trajectory alive: both arm-subset renderer groups
  stay disabled in seated gameplay. The seated world body remains authoritative
  and the seat view hides only its head. Confirmation remains the
  sole transaction boundary: cash and
  drinking state commit exactly once before service and exit is then rejected
  until the empty vessel reaches the counter. Completing service clears only
  that order and leaves the physical booklet closed on the counter. Looking at
  it reopens the seated browser; looking away offers the shared animated exit,
  whose completion alone permits bartender retrieval. Lifecycle cleanup never
  refunds but always restores the
  selected bottle, vessel, camera, player presentation, controls and HUD. The
  F9 debug window may replace only pre-commit browsing and refuses to interrupt
  committed service. The validated seated framing keeps all bottle renderer
  bounds inside a 16:10 viewport, and every reusable vessel snapshots and
  restores its authored transform so repeated orders cannot compound scale.
  The camera is placed above the counter at seated eye height with a shallow
  upward pitch. There is no floor order marker or visible emissive counter
  sign.
- **Accepted by explicit user decision, 2026-09-04 — central-tap beer service
  and embodied drinking:** this supersedes the beer-specific bottle,
  camera-local vessel-attachment and single confirmation/effect-boundary
  clauses of the physical-retail decision above; the non-beer bottle branch
  remains available. `LightBeer` uses the middle of the three existing taps.
  After the paid order the ordinary bartender walks to its authored dock,
  takes the compact beer mug, physically pulls the handle over it while the
  stream fills it, walks to the selected stool and places the full mug directly
  before the hero. `AwaitingDrink` has no timeout: the same gaze predicate owns both
  the thin yellow contour and the localized `E` prompt. Accepting it runs
  `BarDrinkPickupEnter` (`2 s`) → `BarDrinkSipLoop` (`3 s`) →
  `BarDrinkReturnExit` (`2 s`) as a nested action on the visible seated Hero V2
  rig. His right hand grips the handle, lifts, drinks and replaces the mug; it
  remains visibly empty on the counter. Cash commits at order confirmation,
  while intoxication,
  last-drink, consumed-count and stress effects commit exactly once after the
  physical action completes. This follows the shared nested-action and cleanup
  rules in `ai/contextual-animation-standard.md`; it is an accepted architecture
  decision, not an exception to that standard or either world bible.
- **Accepted by explicit user decision, 2026-09-04 — compact right-handed beer
  mug and patron-aligned sip:** this supersedes only the vessel form, grip side
  and drinking-pose clauses of the central-tap decision above. The stable
  `Pint` enum/group identity remains for compatibility, while its visible mesh
  is a smaller beer mug whose handle rests on the hero's right. The authored
  grip sits directly on that handle and binds to Hero V2's right-hand anchor;
  the vessel also exposes a drink-rim anchor and opening direction. During the
  sip, the rim stays at the mouth, the mug reaches a horizontal pose and the
  head and upper body rise with it, following the same restrained pose logic as
  the corrected patron drink. The central tap, `2/3/2 s` timeline, payment and
  deferred-effect boundaries, empty return and silent level-`0` meaning remain
  unchanged; no new lore or contextual-animation exception is introduced.
- **Accepted — Session wallet and paid bar orders:** A fresh runtime
  session starts with `$999` in integer cash and preserves that balance across
  city/bar/supermarket scene loads and city-seed changes. Every bar owns one
  counter station at each of its four unoccupied stools and one shared
  physical menu with exactly four localized offers: beer, wine, unaged
  distillate and vodka. Each offer includes its price and a short low-grade
  taste description. Pure purchase rules
  validate the offer, affordability and maximum intoxication before one
  `GameSessionState` paid-order transaction deducts cash and issues a
  single-use token. Intoxication, last-drink, consumed-count and stress effects
  commit only when physical consumption completes; failures mutate nothing,
  duplicate consumption is rejected and cash cannot become negative. Legacy
  lookup still accepts the former water and alcohol IDs so persisted state and
  patron props remain readable, but those offers are not enumerated in the
  player menu. `None` and `Moonshine` (a stable legacy ID kept for persisted
  state) are not sold. Purchased drinks are consumed at the counter instead of being added to
  the hero inventory. Supermarket purchases use the same wallet but add their
  finite physical item to inventory rather than consuming it. Earnings and
  long-term wallet/save persistence remain deferred, and a purchase never
  completes a bar visit or changes its route.
- **Accepted — Five percentage-driven intoxication ranges:** `0` is Sober.
  Positive values map through `IntoxicationStageRules` as `1–20` Light Buzz /
  «Лёгкий хмель», `21–40` Tipsy / «Навеселе», `41–60` Drunk / «Подшофе»,
  `61–80` Unsteady / «Шатает» and `81–100` Very Drunk / «В стельку».
  Parameters interpolate linearly between the 20-point boundaries instead of
  jumping only when a name changes:

  | Range and stage | Speed | 3D bone sway | Camera roll | Vignette | Ghost | Warp | Dolly |
  | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
  | `1–20` Light Buzz | `1.00` | `0.5°` | `0°` | `0.03` | `0 px` | `0` | `0` |
  | `21–40` Tipsy | `0.97` | `2°` | `0.15°` | `0.06` | `0.5 px` | `0.0005` | `0` |
  | `41–60` Drunk | `0.92` | `4°` | `0.6°` | `0.12` | `1 px` | `0.0025` | `0` |
  | `61–80` Unsteady | `0.82` | `7°` | `1.5°` | `0.20` | `2 px` | `0.009` | `0.35` |
  | `81–100` Very Drunk | `0.70` | `10°` | `2.5°` | `0.28` | `3 px` | `0.015` | `1.0` |

  Values shown are each range's upper-bound profile; the lower bound continues
  from the preceding row. Warmth rises to `0.10` and exposure pulse to `0.08`
  at 100. The 3D presentation progressively suppresses idle-only expressions,
  spreads the registered arms out to the sides in the actor's frame (world-axis
  turns through the shoulder — the imported bones' local axes are not
  anatomical axes) and adds pelvis/chest sway plus a lowered pelvis before
  signed balance lean or fall clips are considered. Runtime presentation eases
  a full-scale change over about `0.7 s`. The HUD is hidden at zero and otherwise
  shows the localized
  stage beside five separately filling 20-point segments. Percentage is the
  only source of persistent slowdown and visual intoxication; there is no
  independent expiring status.
- **Accepted — Deterministic balance challenge above 60:** `60` never starts a
  check; `61–100` does. A city-seed/sequence hash schedules checks from
  `18–28 s` near the threshold down to `7–12 s` at 100 and seeds a fixed
  `120 Hz` inertial arrow simulation. The crisp overhead gauge spans `140°`
  with a centered green sector, arrow and red failure-risk track. Arrow
  keys, A/D, D-pad and left stick provide signed acceleration. Difficulty
  continuously shortens warning from `1.0` to `0.65 s`, lengthens the active
  hold from `3.0` to `4.5 s`, narrows the safe sector from `48°` to `22°`,
  increases arrow disturbance/frequency and risk gain, and reduces player
  authority.
- **Accepted and implemented 2026-08-10 — State-preserving hybrid balance
  fall (superseded 2026-09-04: the Fall clip lead-in, the fixed down time and
  the `0.16 s` recovery blend are gone — see "the fall is fought for, the rise
  is staged" below):** A balance-specific
  modal lock leaves motor input live during warning and active play while
  stopping interaction and camera orbit; the intoxication HUD and cinematic
  camera motion remain visible. Scene transitions,
  fullscreen modals, disabled controls or ungrounded movement prevent a check;
  returning from an external block grants at least `3 s` before it can start.
  Success schedules the next normal interval. Failure stops the motor, chooses
  the arrow side and samples the matching registered `FallLeft/Right` for a
  `0.16 s` directional lead-in. `Player3DRagdollController` then suspends the
  manual PlayableGraph and transfers the current pose to 13 runtime-composed,
  initially kinematic bodies for the remainder of the `0.45 s` fall and the
  full `1.2 s` down phase. Owned colliders ignore each other and the upright
  `CharacterController`; a kinematic root joint limits pelvis displacement to
  `0.68 m` while the gameplay root stays fixed and the existing analytic
  contact shadow remains expanded. Physics is frozen and the complete bone
  hierarchy blends for `0.16 s` into the exact side-down first sample of the
  matching `RiseLeft/Right`. One existing `Rising` gameplay phase then samples
  its distinct full-body, `50`-source-frame (`1.67 s`) action through brace,
  prone roll, a two-key all-fours hold, lead-foot plant, low crouch and the exact
  `Relaxed` seam. Every landmark supplies a complete pose rather than allowing
  an omitted limb to fall back to a Generic bind/A/T-like pose, and no runtime
  mirroring is used. All-fours is not a separate gameplay state, event or root
  transaction. Completion, cancellation, transition, disable and destroy clear
  velocities, return every body to kinematic, disable owned colliders, resume
  graph ownership and restore the neutral rig. Recovery adds `6 s` to the next
  normal interval. Dropping intoxication to `60` or below safely cancels the
  challenge and clears its delay.

- **Accepted — One drive for every body of water:** The river and the sea are
  two materials of one shader, and water carries no per-renderer variation at
  all — no property blocks anywhere — so night factor and rain intensity have
  to be written on the material itself. With one body that could live in
  `CityRiverResources`; with two it cannot, because the registries that push
  those values (`CityNightGlowRegistry`, `CityWeatherController`) have no
  business knowing how many bodies exist. `CityWaterResources` owns the drive
  and nothing else: each body registers its own material and is brought up to
  the last pushed values on the spot, which is what lets a sea built halfway
  through a rain slot arrive already wet. They are not merged into one material
  because the difference is structural, not cosmetic — zero flow changes what
  the vertex stage computes, and the sea's ripple sheet is isotropic where the
  river's is deliberately smeared along its flow. (The drained lake's still
  water proved out both halves of this decision first.)
- **Accepted — A precinct's water edge is authored, or the rail stays:**
  `CityTerrainSafetyWorldBuilder` rails any drop past
  `CityRoadGroundBoundaryPlanner.MaximumSafeStep`; the skip that exists for
  `RiverWater` extends only to edges a precinct physically authors, because a
  generic rail on such an edge stands on ground that visibly continues past
  it — the invisible perimeter this project does not build. The seacoast pays
  the contract on every raised deck: mol parapets bridged only by the root
  stair, an esplanade sea wall cut exactly for the pier and the chained
  slipway, footbridge rails both sides, each under a `ValidateOrThrow`
  perimeter-continuity check to within `0.05 m`. The beach-to-sea step itself
  sits inside the safe-step budget, so the open waterline is honestly
  rail-free. (The lake's revetment proved this contract before the coast
  inherited it.)
- **Corrected — precinct ground over `Water` cells is in the walkable mask,
  and has to be:** an earlier entry recorded the opposite as an accepted
  asymmetry — that such ground was physically walkable but deliberately
  absent from the nav graph, so pedestrians would never stray onto it. That
  reasoning weighed only pedestrians. `PlayerMotor` clamps against the same
  mask, so the effect was an invisible box sealing the player out of an
  entire precinct. `CitySeacoastPlanner.AppendWalkableFootprints` contributes
  the mol, pier and footbridge decks to the mask — never the open sea — and
  the same setup feeds both the mask and the geometry so they cannot drift.
  The two promenade-to-shore junctions follow the same rule across each
  complete logical `3 m` promenade: their connector and granite threshold
  share `promenade.Bounds`. The extra roughly `1 m` structural lip between
  that route and the waterside rail stays outside the mask and is closed by a
  short visible transverse rail, rather than by an invisible motor clamp.
  Pedestrians are unaffected in practice: they consume the mask only as a
  clamp, not as a source of destinations.
- **Accepted — the park boards are played by a pure engine the presentation
  never argues with:** the two games live under `Assets/Scripts/Runtime/Games/`
  as plain C# — position, legal moves, apply, search — and the scene layer
  only ever asks for the current placement and the description of one applied
  move. Three consequences were worth the split. Chess correctness is provable
  by perft rather than by playing it, which is the only honest way to know a
  move generator is right. The presentation can be wrong for at most one move:
  every carry ends with the board re-read from the rules, so a mis-animated
  chain repairs itself instead of compounding into a corrupt game. And the
  engine's strength is one number in one struct rather than a property of the
  code, which is what let the opponent be tuned down to a plausible old man —
  a slack window at the root, a blunder roll, and an explicit exception that
  never throws away a forced mate.
- **Accepted — the hero is dark at both boards because the set already was:**
  nothing chooses sides. The free plank at each table is the one the drawn
  `CityChessSetPlan` filled with dark men, so the man opposite is White at his
  own board and therefore opens. A test asserts the live opening position
  equals the drawn placement piece for piece, which keeps the two from ever
  drifting — the moment somebody sits down, the set they were looking at is
  the position they are playing.
- **Accepted — a first-person camera hides the head by rig rule, never by
  mesh:** the hero's head is twenty-two separate meshes on this model — skull,
  neck, hair cap and fourteen tufts, ears, nose, stubble, under-eye shadows,
  and then eyes, pupils, brows and a mouth on their own `face.*` bones. The
  first seated view hid the two anatomical parts named Head and Neck and left
  the player looking at the inside of his own hair, which is the exact class of
  bug that a list of mesh names reintroduces every time the model grows a part.
  `Player3DHeadVisibility` therefore classifies by bone (`head`, `neck`,
  `face.*`), so a part added later is covered by where it is weighted rather
  than by somebody remembering to add it. Nothing below the collar is ever
  hidden: the body is what tells the player he is sitting at the board, and the
  camera is moved out of the skull instead.
- **Accepted — player graphics toggles are one static service over
  `PlayerPrefs`, the project's first persistence:** `GraphicsEffectsSettings`
  holds seven booleans (`graphics.dof`, `graphics.intoxication_fx`,
  `graphics.dither`, `graphics.scanlines`, `graphics.aspect_4_3`,
  `graphics.vertex_jitter` and `graphics.begotten`), loaded lazily and saved
  on change, with a
  `Version` counter consumers poll instead of subscribing to events — no
  lifecycle hazards. The first four default on; 4:3, vertex jitter and the
  Begotten film mode are explicit opt-ins. Effect consumers gate themselves: the PS1 composite
  zeroes its dither/scanline floats per frame, the render feature supplies
  camera-specific vertex-snap globals, the intoxication lens driver forces
  zero per `Apply`, and `DepthOfFieldSettingsBinder` flips `active` only on
  volume-profile clones or runtime-built profiles, never on the authored
  `.asset` at play time.
- **Accepted — depth of field runs in two tiers around the PS1 crush:** the
  always-on tier is Gaussian in every scene grade (City `start 8 / end 28 /
  radius 1.5` — the band must sit inside the `48 m` far clip where exp² fog
  `0.070` has not yet flattened depth). The six true interiors share radius
  `0.55` while retaining their bands: Bar `6/18`, Home `5/14`, Stairwell
  `5/16`, Supermarket `7/20`, Church `7/26`, Mother's House `4.5/13`.
  Exterior profiles, including Mountain Road `20/92/0.65`, are unchanged.
  The modal tier is one
  shared `CinematicDepthOfField` Bokeh volume at priority `10` (above scene
  grades `4-5`) whose weight blends `0.35 s` in / `0.45 s` out; modal
  controllers call `Begin`/`SetFocusDistance`/`End` around their existing
  `SetFixedPose` trios. The bar begins Bokeh only after the menu is fully open,
  uses `35 mm / f/8`, and calls `EndImmediately` when the booklet rests,
  service begins or standing starts, so neither nearby bartender work nor a
  returned seated/third-person camera inherits its tail. Because URP clamps
  the Gaussian radius at `1.5` render
  pixels, the far blur is deliberately subtle after the `640x360` downsample;
  the documented fallback if it reads as invisible is Bokeh in the city grade
  (aperture ~10), not a render-scale change, which would soften the whole PS1
  look. Transparent depth-less surfaces (river water, glass, particles) blur by
  the opaque depth behind them — accepted at this subtlety.
- **Accepted — film stays in the composite, polygon jitter stays in Lit:**
  Bayer 4x4 dithering perturbs the perceptual value by at most half an RGB555
  step before quantization, indexed by internal-pixel coordinates so the
  checker is chunky-pixel-locked; the scanline mask darkens the leading third
  of each internal row on the upscale pass (a step, not a cosine — a symmetric
  cosine cancels at exactly 2x vertical scale, the 720p case). Optional vertex
  jitter instead wraps the stock URP Lit vertex functions in the project-owned
  `Ps1Lit` shader and rounds clip XY onto the presented-pixel grid. Its strength
  is a camera-scoped global rather than material state, preserving the SRP
  Batcher; inventory-preview and reflection cameras carry an explicit
  `Ps1VertexJitterExclusion`. Intoxication additionally drives URP
  `ChromaticAberration` (`0 → 0.45`) and `LensDistortion` (`0 → -0.14`, small
  and negative so the pinched rim stays under what the point-sample crop hides)
  through `IntoxicationLensVolumeDriver` at volume priority `8`.
- **Accepted (2026-09-04) — the Begotten mode is a print held in the
  composite, not a change to the game's clock or its grade:** the option
  replaces the point upscale with three passes of `Ps1Composite.shader`
  (`BegottenSoft` half-size perceptual luma, `BegottenGlow` quarter-size
  blur, `BegottenLevels` one pixel of scene statistics, `BegottenPrint` at
  output size) and a pure seeded projector, `BegottenFilmModel`. The print
  is exposed for the scene the way a printer exposes for a negative: the
  levels pass sweeps the glow in `12x12` taps for the mean and deviation of
  the light, the mean prints at the middle of the scale and two deviations
  reach either end (`exposed = 0.5 + (light − mean) / 4σ`, σ floored at
  `0.05`), a touch of local contrast (`light + 0.5·(light − glow)`) rims a
  silhouette against a tone like its own, and the per-picture threshold
  roll around `0.42` is judged inside that range with a `±0.06` band where
  three octaves of value-noise grain decide the pixel, so the boundary
  boils instead of aliasing. The width is the readability dial: the common
  tone (fog, sky) burns to bone, a road one deviation darker prints as
  sparse grain rather than solid soot so a black figure still stands on
  it, two deviations down is soot. Narrower mappings failed twice: with
  black one deviation under the mean the common tone fell just under the
  roll and the whole ground printed as boiling black; with `1.3` either
  side the night road and the hero on it were both solid soot. Two earlier attempts are why: a fixed
  threshold pulled toward the scene mean and clamped at `0.12` turned the
  real night City - fog, ground and figures all under the clamp - into one
  unreadable field of grain (the user's first look), and pulled halfway to
  the mean a sunlit ground landed exactly on the threshold and boiled from
  edge to edge where the film burns it white (the first sheet). Lamp flicker, vignette
  and gate weave act on the light before the threshold, never on the
  printed value, so a dark corner is soot with a boiling edge and never
  grey. The film runs at `24` pictures a second of unscaled time with
  `2-3-2-3` game frames per picture and a `3 %` chance per picture of
  sticking for two to four ticks; between pictures no composite pass is
  recorded and `cameraColor` is pointed at a persistent `RTHandle` imported
  with `discardOnLastUse = false`, into which the print pass renders
  directly — no copy. The reallocation check compares every descriptor
  field including the name, so the descriptor is pinned field by field; a
  reallocated texture holds nothing and always prints. `Application.targetFrameRate`
  stays `60` because hero handling is calibrated to it. The mode forces the
  4:3 gate, mutes dither, RGB555 and scanlines (meaningless under it), keeps
  the vertex jitter and the drunk lens as the player set them, and skips
  cameras marked `Ps1VertexJitterExclusion` — the inventory preview stays in
  colour (a documented residual) and the reflection probe never holds a
  face. IMGUI (every menu, prompt, subtitle and fade) draws after the
  camera, so the print never touches it. The grain is sized in output
  pixels (`≈2.9 px` at 1080p), not on the internal grid, so it reads as
  emulsion rather than blocks over the `640x360` upscale.
- **Accepted — the opt-in 4:3 mode is a composite crop, not a camera change:**
  when `graphics.aspect_4_3` is on (default off), the feature computes the
  internal resolution for the centered 4:3 window (`480x360` from a 16:9
  output), pass 0 reads only that window (`sourceUv.x = 0.5 + (uv.x - 0.5) *
  _Ps1AspectFraction`) and pass 1 pillarboxes with pure black bars. Because
  Unity FOV is vertical, the central crop of the widescreen frame IS the exact
  image of a 4:3 camera at the same vertical FOV — no FOV compensation, no
  `Camera.rect` cleanup, and the crop/pillarbox pair cancels to an identity
  mapping over the visible region (flat-tone byte values keep the exact RGB555
  blend at unchanged screen positions, which the pillarbox PlayMode test
  exploits). On displays at or narrower than 4:3 the fraction clamps to 1. The
  IMGUI retro overlay deliberately stays full-screen above the bars.
- **Accepted — Cross-zone anchor-consumer dressing:** `CityWindDressingPlanner`
  is the first planner that consumes other plans' public descriptors instead
  of the layout alone: it reads decoration, seacoast, cemetery and fringe-yard
  descriptors (kinds, positions, rotations, sizes) and rederives each recipe's
  drawn geometry (the decoration builder's cardinal-snapped forward, lot-width
  clamps and part offsets) to compute hang points on members that exist in the
  render. The coupling is deliberate and bounded: only public descriptor data,
  null/empty anchor sets degrade to fewer props (budgets are maxima, not
  guarantees), and the physics cap and the art-restraint cap are one number in
  one validator (`64` cloths city-wide plus per-zone maxima, zero-zones by
  rule). Anchors are picked by stride across each district's whole anchor
  list — a head-of-list pick planned one market in twelve and read as an
  empty city — and roof-landmark anchors are skipped where the piece would
  hang tens of metres up (the industrial gantry, the tower billboard):
  street-level presence per urban district is pinned by an EditMode floor. Simulated cloth cannot join combined batches, so the world builder
  lives with the swing seats and fountain water after every static dresser;
  its static supports (line poles, parabolic rope chords via
  `CityRopeSpanGeometry`, pin battens) do batch, and only line poles carry the
  batch collider. Cloth at rope width (`<= 0.12 m`) keeps the factory's flat
  colour — the weave is sub-pixel — while wider panels ride the shared POI
  cloth sheet through `ApplyClothPanel`.
- **Accepted — Hero V2 changes the canonical outer garment, while V1 remains
  the production fallback:** by the user's explicit 2026-08-29 decision, the
  successor wears an unfastened faded dark olive-drab field jacket with long
  sleeves and no diagonal satchel strap or buckle, replacing the former
  burgundy overshirt/strap target in story-bible §7. Pocket construction,
  cuffs, seams, the right ochre repair patch and the left bandage are painted;
  no readable insignia or copied film marking implies a military biography.
  The same user also required Hero V1 not be removed, so its byte-frozen
  burgundy/strap prefab remains the temporary gameplay default until a later
  explicit promotion. That bounded production/canon mismatch is accepted and
  does not license either design to leak into the other.
- **Accepted — Hero V2 is a parallel explicit variant, not a mutable player
  preference:** `Player3DResources` and `PlayerFactory` default their old APIs
  to `ProductionV1`; only a caller naming `ExperimentalV2` can instantiate the
  candidate. At that decision point V2 preserved the 31-bone/37-action
  contract; the later production-only Run raised live V2 to 38 and the
  three-part seated drink subsequently raised the current total to 41.
  The variant selects five facial
  states from a merge-safe MPB atlas and binds one full-colour clothing atlas
  to a shared white-tint material. Gameplay roots and inventory remain V1, so
  there is no cross-scene toggle or half-promoted saved state. Direct resource
  instantiation reapplies the registry palette immediately because prefab MPBs
  are runtime state and cannot be serialized.
- **Accepted and implemented 2026-08-29 — Hero V2 is the production default;
  Hero V1 remains an explicit retained fallback:** by the user's correction,
  every no-variant `PlayerFactory`/`Player3DResources` route, all eight gameplay
  roots, prefab-derived first-person subsets and the inventory portrait now
  resolve to `ProductionV2` / `Player3DV2`. `ProductionV1` still resolves the
  byte-frozen former prefab and its portrait for rollback and legacy contract
  checks; those assets are not deleted. This supersedes only the temporary
  default/candidate clauses in the two decisions immediately above. The live
  `PlayerCharacterDimensions.PelvisHeight` follows V2's measured `0.835 m`
  pelvis so contextual clips remain grounded. NpcHumanV2 now gives Route 01
  ambient walkers the same rest-pelvis and Avatar contract; their per-archetype
  seated offsets remain independent so canonical silhouettes still clear the
  cabin.
- **Accepted and implemented 2026-09-04 — Hero V2 is the sole packaged player;
  the retained Hero V1 is removed:** by the user's explicit decision, the old
  rollback route is deleted rather than carried beside production. All nine
  gameplay roots, prefab-derived first-person subsets and inventory use the
  existing `Resources/Player/Player3DV2` prefab and V2 portrait through the
  no-variant `Player3DResources` / `PlayerFactory` path. The old prefab,
  portrait, model, animation bank, Blender source, editor import/setup pipeline,
  generator and V1-only test fixture are absent. The sole runnable hero
  generator remains `tools/build-player-3d-model-v2.py`; reusable rig, action,
  export and bed validation lives in the non-runnable
  `tools/player_3d_model_common.py`. The V2 pipeline contract also pins the old
  asset paths as absent. This supersedes only the retention, non-deletion and
  selectable-fallback clauses of the three `2026-08-29` decisions above; those
  entries remain the historical record of the staged promotion. Hero V2's
  visible design, its `41` Actions and the independent pedestrian bank of `37`
  Actions do not change, and this removal adds no world fact or lore.
- **Accepted and implemented 2026-09-03 — the drunk hero is a continuous
  balance model, not a scheduled check; feet are solved onto the ground:** the
  modal arrow challenge (`BalanceChallengeModel`, `BalanceCheckView`, its
  interval schedule and `BarMinigameModalLockOptions.BalanceCheck` capture at
  the check) is removed by the user's decision. `PlayerBalanceModel` is a pure,
  seeded, fixed-step (`1/120 s`) linear inverted pendulum in the hero's frame:
  seeded sways and filtered noise scaled by the level push the centre of mass,
  the centre of pressure chases the capture point with a level-dependent delay,
  A/D shifts it toward the pressed side (leaning into the fall is the
  recovery, and tank yaw is scaled down while unstable so the recovery does not
  spin him), a capture point outside the two boots' polygon plans a recovery
  step past it, a wall within reach on the tipping side becomes support once
  the hand holds it, and a kerb under the swinging boot is a forward trip.
  Sober is bit-exactly inert. The model's drift moves the CAPSULE: `PlayerMotor`
  carries it as a second `Move` through the same `IWalkableArea` constraint and
  never folds it into the player's own momentum, so the capsule, the camera and
  the body cannot disagree and a wall that stops the drift cannot fling him.
  `IntoxicationStatusController` keeps the Fall clip -> ragdoll -> Rise pipeline
  unchanged and starts it only when the model latches a fall above level `60`,
  after the session's grace, grounded and on a surface under `12°`; on a stair
  the model is pinned to the recoverable polygon and only staggers.
  `GameSessionState.BalanceCheckDelayRemaining` now means the grace before
  balance can be lost again and `BalanceCheckSequence` the episode counter, so
  the session format is unchanged. Feet: `Player3DProceduralLocomotionLayer`
  is the one late bone writer after the clip — restore, lean, per-foot heel/toe
  probes (`Physics.DefaultRaycastLayers`, triggers accepted only on the
  `FootProbe` layer), pelvis to the lower boot (run-released), `LimbTwoBoneIk`
  per leg (the seated-arm solver moved out of `SeatedArmIk` and given an
  analytic law-of-cosines pre-bend, because CCD aims but cannot shorten a
  nearly straight leg), stance-foot locks during recovery steps, and the wall
  hand. Stair treads in the stairwell and the city exterior stairs are
  render-only boxes over a hidden ramp, so each tread now also carries a
  raycast-only TRIGGER `BoxCollider` on the `FootProbe` layer (user layer 10):
  the physics matrix hides it from the hero, the ragdoll, pedestrians and the
  bus, every obstacle sweep in the project ignores triggers, and only the foot
  probes see it. City walkers adopt the legs-only layer with bones found by
  the shared names; airborne designs and seated riders keep their old paths.
- **Accepted and implemented 2026-09-04 — Stairs: the pelvis follows the
  capsule's ground, the boots follow the treads.** The rule above — pelvis to
  the LOWER BOOT, each boot's probed surface smoothed as an absolute world
  height — is correct on a floor and wrong on a slope, and the stairwell showed
  both failures at once. Absolute smoothing rate-limits the body's own descent:
  the presentation floors the Walk's plants at `0.68`, so in a walk both feet
  always count as planted, the `0.6 m/s` cap fought a controller descending the
  hidden ramp at `1.083 m/s`, and after one `16`-step flight the targets sat
  `0.7..0.8 m` above the treads, `PelvisDrop` pinned at its `+0.12 m` lift cap
  and both legs folded to `20..70°` interior to reach ankles at hip height. And
  a two-boot MINIMUM double-counts a slope: the flat-authored stride spans
  nearly five `0.24 m` treads, so the boots straddle two or three risers and the
  pelvis dives to the lower tread while the capsule root has already followed
  the ramp, `0.20..0.30 m` of it landing on the trailing knee. So the smoothed
  target is now held RELATIVE TO THE ACTOR ROOT (`Leg.SmoothedSoleAboveRoot`) —
  the body's descent passes through unfiltered and only a real change under the
  boot, a nosing or a kerb, is rate-limited, at a raised `1.2 m/s` because a
  stance boot crosses a `0.24 m` tread in `0.092 s` and each nosing moves its
  surface a whole `0.10 m` riser (`0.6 m/s` left a measured `9 cm` of boot
  inside a tread while climbing) — and the pelvis follows the walkable ground
  under the CAPSULE: `Player3DFootGroundProbe.TryProbeActorGround` casts one ray
  from the actor root that IGNORES triggers, so the render-only treads drop out
  and the hit is the surface the controller stands on, and
  `PlayerFootPlacementRules.PelvisPlaneDelta` turns it into the delta. On a
  floor that is arithmetically the old two-boot number, so nothing flat changes,
  and where no walkable ground is found — a pedestrian bound without a probe, a
  body over a gap — it falls back to `min(leftDelta, rightDelta)`. The
  drunk-only `GaitReachShortfall` generalises to `ReachShortfall`: any boot out
  of its leg's reach from the hip the pelvis has ALREADY been moved to brings
  the hips down to it, weighted by `PlayerFootPlacementRules.StanceWeight` so
  the leg carrying the weight answers for its tread while a boot still swinging
  down a flight cannot drag the body a riser ahead of its own footfall; a drunk
  gait's thrown-wide boot still counts in full, as it was tuned to, and the dip
  is off through a rise, where the Rise clip owns the pelvis. `Calibrate`
  forgets the smoothed targets so a rebind or a teleport cannot chase a target
  from another room. Alternatives not taken: a stair-stride clip, which is the
  real answer to the residual `78°` trailing knee at the deepest frame of a
  descent (`126°` median) but is an authoring change, not a runtime one; a
  slope-aware walking speed, which would shorten the stride over a flight but
  changes the motor and the gait blend for every slope in the city; and gating
  the pelvis on an absolute plant threshold, which CANNOT work here — the walk's
  plants never fall below `0.68`, so no absolute threshold can tell the stance
  foot from the swinging one, which is why the shortfall dip is weighted by
  relative stance instead.
- **Accepted and implemented 2026-09-04 — drunk dolly zoom above 60:** the
  chase camera breathes a Vertigo zoom once the level passes the balance
  threshold. `IntoxicationProfile.DollyZoomStrength` is `0` through `60`,
  `0.35` at `80` and `1` at `100` (the "Dolly" column above).
  `IntoxicationDollyZoomModel` is a pure, seeded oscillator (step clamp
  `0.1 s`, leftover carried across phase boundaries): each cycle leaves rest
  for one side — wide/pushed-in with probability `0.65`, narrow/pulled-back
  otherwise and only when the camera has `1.35×` its arm of room behind it —
  with a reach of `0.55..1×` the strength, an out leg and a back leg drawn
  separately from `3.2–6.5 s` at the threshold down to `0.8–2.6 s` at 100, a
  per-leg time warp `t^k`, `k ∈ [0.65, 1.6]`, under a smootherstep (zero slope
  at both ends, so every leg joins its holds without a kink), a hold at the
  peak of `0.12..0.4×` the out leg and a hold at rest of `0.25..0.8×` the back
  leg — the linger is a fraction of the leg that led into it, so a fast breath
  lingers briefly and a slow one longer. A cycle finishes with the reach it
  latched when the level drops; rest is bit-exactly zero. `PlayerCameraFollow`
  maps the signed exponent onto the lens and the arm together:
  `distance × tan(fov/2)` is held to the collision-resolved ordinary arm's
  value, so the hero keeps his size while the world stretches (up to `100°`,
  arm `×0.42`) or flattens (down to `34°`, arm `×1.63` exterior). The one
  collision sweep now reaches the full pull-out and also measures the room
  behind the camera; that room closes at once and reopens with the arm's own
  `0.32 s` damping, and a blocked pull-out keeps the lens on the arm it got.
  The layer has its own weight (`0.35 s`) driven by `CinematicMotionEnabled`
  and the `graphics.intoxication_fx` toggle, never the cinematic weight that
  `Snap()` zeroes, so a teleport keeps the running breath. A fixed pose
  silences it — the model rests, and `Snap`, `ResolveFollowPose` and
  `FollowFieldOfView` stay dolly-free — and on release the layer absorbs a
  returned lens only when the owner came back to within `1.5°` of the lens it
  took (the bar shop returns to the live pose it captured at entry), easing
  it out over `0.45 s`; an authored shot lens cuts to base as it always did.
  Seeded from the city seed; `ReseedDollyZoom` is the test seam, and
  `IntoxicationDollyZoomCapturePlayModeTests` writes twelve seconds of the
  city's own camera at level 100 to `Captures/DollyZoom` for the eye.
- **Accepted and implemented 2026-09-04 — the fall is fought for, the rise is
  staged:** above `60` a lost capture point still became a ragdoll on the spot,
  and the rise was a fixed `1.2 s`, a `0.16 s` lerp and the clip at its
  authored rate. Four things changed. (1) INERTIA: every channel the balance
  model writes to a bone — lean, torso, arms, crouch — goes through
  `SecondOrderFilter`, a pure sub-stepped mass-spring-damper (arms under-damped
  at `ζ 0.45`, so a reaction overshoots and swings back; exactly zero at zero,
  so sober stays bit-exact and a `dt = 0` re-apply changes nothing;
  `SettleLatePresentationPose` is the test seam that runs the springs forward
  without a frame). (2) THE FIGHT: the model is now LIP + flywheel + stepping.
  The torso-and-arms flywheel spins in the sense of the fall when the capture
  point leaves the boots (equivalent centre of pressure `I·α/(m·g)`, `0.012 m`
  per rad/s², `22 → 9 rad/s²` sober → blind, onset lagged by a reaction delay,
  release immediate), stops at `40°`, is SPENT there and unwinds regardless
  (the first version's lagged command never reached zero, so the return spring
  waited `~3 s` and a pinned stair grace kept the torso thrown), and re-arms
  below `30 %` of the stop. A step is judged against where the capture point
  WILL be when the boot lands (`e^(ω·t)` beyond the polygon's edge): if an
  ordinary step cannot get there the model enters `Toppling` at once — no
  reaction delay, no waiting for the clip to free the boot, because the point
  doubles every `0.2 s` — and throws a lunge (`1.6×` reach, `0.85×` duration,
  aim error `0.25 m × level`, A/D pulls it `3×` harder than the ankles, a soft
  landing that keeps `0.7` of the momentum blind drunk); a step already in the
  air is redirected into the lunge. In a topple the root drifts at the centre
  of mass's own velocity (the boots stay locked, the legs stretch, the trailing
  leg lets go when out of reach), the lean is measured from the boots'
  midpoint in a stance and from the boot under the pressure once they split (a
  man in a wide lunge stands over the foot with his weight — measured from the
  midpoint a split read as a `20°` lean of an upright trunk), the pelvis rides
  the pendulum's arc down by `h(1 − cos θ)`, and past `26°` both hands go out
  for the ground the controller probes ahead of each shoulder along the fall.
  A landed lunge that puts the point back between the boots with the lean under
  `20°` recovers (the brace comes down over `0.3 s`); the point of no return
  `38°`, both lunges spent, `1.4 s`, a wall contact or a blocked side loses
  it, and `BalanceFallCause` says which. `UpdatePhase` judges a landing against
  the capture point AFTER it (the first version used the pre-landing point and
  declared phantom `0.1 s` topples). Offline over 200 seeds (scratchpad
  `balancesim`, the two model files against `UnityEngine.CoreModule`): level
  80 topples every ~`90 s` and recovers `68 %` with no input (steering toward
  the lean: `2 %` fall in three minutes); level 100 topples every ~`30 s`,
  recovers `67 %`, first fall p10/p50/p90 `14/32/80 s`, every seed down inside
  three minutes; every recovery is a lunge (`1.35` per topple); a lateral
  `3 m/s` shove at 100 is always lost, a forward one is run off with two
  lunges and `4.5 m/s` is not. The old cadence (level 100 p50 `17 s`) was
  accepted as lengthening: the fights are the point. (3) THE HANDOFF:
  `PlayerRagdollHandoff` carries the topple's motion — the world velocity field
  of a rigid rotation about the boot under the pressure (`ω = v/(h cos θ)`),
  so every ragdoll body starts moving as the body was, the head fastest, the
  feet hardly; a velocity back toward upright is stripped first; the old
  scripted shove fades in only below `1.5 rad/s` (a forced fall). The ragdoll
  takes the bones as the late layer wrote them THIS frame
  (`BeginRagdollPoseFromLatePose`: re-apply at `dt = 0`, forget the base so
  nothing restores the clip, release the foot locks, raise the flag without
  the restore) and only then is the model frozen, because freezing pushes a
  neutral pose. The `Fall{Side}` and `Down{Side}` clips are no longer played
  (kept, registered, and still the path when there is no ragdoll); the shadow
  slides along the fall's own axis (`PlayerPresentationMetrics.FallAxis`).
  `Player3DCharacterPresentation.SetMotion` blends no gait under a topple's
  root drift. (4) THE RISE: `PlayerRiseModel` is pure and seeded
  (`EpisodeSeed ^ 0x51AE`) with every draw at construction — `Settling` (the
  ragdoll under `0.15 m/s` for `0.25 s`, at least `0.6 s`, at most `2.5 s`),
  `Stunned` (`0.5 → 2.0 s` by level, `±30 %`), `Stirring` (`0.6–1.0 s`: the
  frozen body blends into the clip's brace, the hands go to the floor, the head
  lifts), `PushingUp` (`0.8–1.2 s` to all fours with `0–2` slumps of `0.45 s`
  that run the clip back `0.06` and dip the pelvis `6 cm`; never two under
  level 60, never any sober), `Kneeling` (`0.6–0.9 s`: the lead boot — the
  side he lies on — steps `0.30 m` forward, the same hand goes to the knee),
  `Standing` (`0.8–1.2 s`: the ordinary leg solve fades in over the first
  `40 %`, the hands let go over `0.3 s`, a `4°` wobble in the last `30 %`
  whose last swing, halved, is the fresh balance model's first push). The
  authored Rise clip supplies the trunk and is SCRUBBED by the model
  (`ClipTime`); the late layer's `ApplyRise` draws the limbs on top with the
  wall hand's two-bone solver (`ApplyArmReach`, now both hands) and the lead
  boot to a probed floor. Root reconciliation at the first stirring frame: the
  ragdoll freezes and reports the pelvis, the chest and the shoulder heights
  (the lower shoulder picks the clip side, `6 cm` dead band, the fall side on
  a tie); `Rise{Side}(0)` goes on the bones so the authored lying frame can be
  read; the capsule is teleported under the lying pelvis and yawed to the
  lying axis through `PlayerMotor.TeleportPlanar` (walkable-constrained, the
  residual logged) with the camera told to absorb the shift
  (`AbsorbTargetShift`, no `Snap`); and the frozen pose — the pelvis captured
  in WORLD space, everything else in its parent's, so the root can move under
  the body without the body moving — is put back on top and blended into the
  clip while he stirs. Verification: `SecondOrderFilterTests`,
  `PlayerBalanceToppleTests`, `PlayerBalanceToppleRulesTests`,
  `PlayerRiseModelTests`, `PlayerRagdollHandoffTests`;
  `IntoxicationStatusPlayModeTests` (the ragdoll starts moving toward the fall
  side, the root is back under the pelvis at `Rising`, the frozen pose is let
  go by `PushingUp`), `PlayerBalancePlayModeTests` (the lean and chest-pitch
  sign probes settle the springs first; the tightrope hand-span measure skips
  brace frames); `Player3DToppleRiseCapturePlayModeTests` writes the six-tile
  `TestResults/topple-rise-sheet.png` (lunge, brace, stirring, slump,
  half-kneel, wobble).

## 2026-09-04 — Anatomy through the fall, the drunk walk, face and head, camera and keys while down

- **Anatomy is a property of the solver, not of any pose.** `LimbTwoBoneIk`
  now guards the side of the bend: after the analytic aim, and again after the
  hint's polish, a middle joint on the wrong side of the root-to-target line
  is swung to its mirror image IN THE BEND PLANE (the upper by twice its
  angular offset, the lower by what brings the tip back — the mirror keeps
  every length, so the tip is back on the target), never by a half turn about
  the line, which would roll the whole mesh. The hints moved out of world
  space: a knee is hinted along the kneecap (`kneeForwardLocal`, the actor's
  forward captured in the thigh's frame on the idle pose) and an elbow along
  the upper arm's calibrated back — read as they WILL face once the limb has
  been aimed by the least rotation (`FromToRotation(tip − root, target −
  root)` applied to the calibrated axis). The world-fixed hint was the real
  defect behind a "backward knee" on a lunging leg: with the leg swung far to
  the side the solver twisted the femur up to `180°` about the hip-to-ankle
  line to bring the knee to the actor's forward, the mesh screwed with it,
  and the thigh-frame measure then read a correct knee as `-86°`. Hinting
  along the future kneecap asks for no swivel at all. The last defect of the
  free solve is the knee's own twist: the thigh and the shin are aimed
  independently, so a foot pulled off to the side can leave the shin swung
  ROUND the thigh — the knee bent forward in the actor's frame while the
  kneecap faces the side, a joint no knee has, which the ragdoll's hinge
  then snaps back on its first step. `AlignHingeRoll` runs after every leg
  and arm solve: the upper bone is rolled about its own length until its
  bend reference (the kneecap, the elbow's back) faces the lower bone's fold
  away from straight, with the lower bone's world rotation held so the tip
  stays put — the twist becomes rotation in the hip or the shoulder, where a
  body has it. Skipped under `2 cm` of fold, where there is no plane.
- **The Rise clip was authored with three backward knees.** `foot_lift`,
  `half_kneel` (lead leg) and `crouch_leg_lift` (both) had the shin's
  `armature_direction` aimed from the ankle to the knee — `-134°`, `-30°` and
  `-99°` of hyperextension, the "grasshopper". The keys are re-authored around
  what a person does: the hips come up off the knees onto the trailing toes
  first (`foot_lift` pelvis `-0.20` instead of `-0.295`), the lead knee swings
  through with the shin folded flat and the toes down, plants heel first, and
  the trailing foot passes through toes-tucked. Every foot turn between two
  keys is a quarter turn: the clips interpolate LINEARLY (`_create_action`'s
  default), so a half turn between keys has no path the interpolation can be
  trusted with and swept the toes through the floor; and a knee swung under
  hips `45 cm` off the floor puts the foot under the floor by geometry, which
  is why the hips rise first. The V2 build is now gated frame by frame
  (`validate_fall_recovery_dense`): no visible vertex more than `2 cm` under
  the neutral floor on any baked frame of the lie or the rise (the Fall clips
  are exempt — the ragdoll has the body from the moment balance is lost and
  they are never shown), and no knee or elbow bent the wrong way by more than
  `8°` or folded past `130°`, measured against the same thigh-frame and
  upper-arm-frame references the runtime uses. The V1 validator's landmark
  contacts (hands and knees on the floor at all fours, the low crouch's
  boots) are NOT applied to V2: they were fitted to the V1 proportions, float
  `15–20 cm` on this rig, and the runtime's hand and boot IK hides it — known
  debt, recorded here rather than gated. The `Down` pose's under-body elbow
  was also `36°` backward and is now folded forward.
- **The ragdoll's hinges were inverted, and its joints too narrow for the
  pose it took over.** PhysX reads a `ConfigurableJoint`'s angular X the other
  way from `Quaternion.AngleAxis` about the same axis (the parent's frame
  measured from the child's): with the knee limits written as `-5..115` the
  anatomy check found knees folding `72°` BACKWARD. Ranges are now written as
  flexion (`BodySpec.Hinge`: hyperextension, flexion, and the way the segment
  flexes about the actor's right — backward for a shin, forward for a forearm
  or a thigh) and mapped through one pinned constant, `JointFlexionSign = -1`.
  The elbows were hinged about `Forward` (a `120°` sideways fold, `8°` on the
  real axis) and are hinges about `Right` now. The hips flexed `±25°` and the
  shoulders abducted `±55°`: a lunging leg or an arm flung out for balance
  sat outside the range, the joint snapped to its limit on the first physics
  step and whipped the shin or forearm past ITS limit (`-54°` at frame 1) —
  hips are `-30..110` with `60°` of abduction, shoulders `±110/90/90`, and the
  body's own colliders keep an arm out of the chest now, not the limit.
  Self-collision: `IgnoreOwnedCollisions` switches off only the controller
  capsule and the two halves of each joint (which the joint keeps apart);
  every other pair collides. A pair already overlapping in the idle pose by
  more than `1 cm` would explode apart on the first step, so it is switched
  off and logged (`ragdoll/resting_overlap`) and the anatomy check asserts the
  hero has none. Solver iterations went `12 → 60` (velocity `4 → 16`), because
  a leg pinned under the torso now carries its weight through the knee's
  limit. `Player3DRiseAnatomyPlayModeTests` pins all of it: hinge axes at
  initialisation, no knee hyperextension on any ragdoll frame (the knees now
  read `0..122°` through a whole fall; they used to fold `72°` backward), no
  interpenetration where he lies, and anatomical knees and elbows at five
  moments of the rise. **Residual:** on the frame a hand slaps the floor at
  `2 m/s` the elbow is pushed `13–22°` past its hard limit before the solver
  wins, gone the next frame; the test allows `25°` there. Heavier limbs,
  joint preprocessing and a depenetration cap were each tried and each made
  it worse (`30–36°`), so the authored masses and `enablePreprocessing =
  false` stay.
- **The camera stays the player's through a fall.** `BarMinigameModalLock`
  gained `DisableOrbitInput` (true for `Fullscreen`, false for
  `BalanceCheck`); the fall locks the interactor and the motor and nothing
  else. The focus is pulled off the root — which stands where he lost his
  feet while the ragdoll carries him up to a stride away — to the pelvis
  (`SetFocusOverride`, `FocusOverrideHeight 0.35`, weight one while he falls
  and lies, the fall amount while he rises, through the ordinary `0.18 s`
  damping and `0.45 m` lag clamp so it is a pan, never a cut).
- **Keys while down.** WASD and the left stick are read in one place now
  (`PlayerDirectionalInput.ReadRaw`; the motor delegates). A body on the floor
  has no forward, so the fall reads them relative to the CAMERA. While the
  physics has him a held key heaves the ragdoll that way (`Twitch`: a push at
  the hips and chest with a lift that unloads the floor for the moment the
  push acts — friction ate a plain push within the step — and a roll about
  the direction) on the edge and every `0.35 s`, and each heave shortens the
  stun to come by `0.15 s` (`NudgeStun`, floor `0.3 s`). Once he is up on all
  fours a held key holds him there: `PlayerRiseStage.Crawling`, between
  `PushingUp` and `Kneeling` (and reachable back from the first `30 %` of the
  kneel), rocks the clip between its two all-fours keys, turns him toward
  the key at `60°/s` and moves him forward only in so far as he faces it
  (`0.5 → 0.35 m/s`, in pulls) — as a direct `PlayerMotor.ApplyDownedMove`,
  because the frozen balance controller (order `-10`) zeroes the drift
  before the motor reads it. Released for `0.15 s`, the kneel goes on. Every
  draw of the rise is still taken at construction, so a seed replays the
  same rise with the same keys. **The crawl is a locomotion, not a pose.**
  The model times four contacts diagonally (`PlayerCrawlLimb`: the left hand
  and the right knee swing through the first half turn while the other two
  hold, then the other pair); the presentation plants each contact in the
  WORLD (`CrawlContact`) and holds it while the body crawls over it, and when
  its turn comes arcs it (`SmoothStep`, a `10 cm`/`6 cm` lift) from where it
  held to a spot a reach ahead of its shoulder or hip (`0.22 m`/`0.10 m`,
  followed as the body moves, so it lands where the body then is) and plants
  it there. Hands go through the arm solver; knees through a new
  `PlaceKnee` in the layer: the thigh is AIMED from the hip (a one-bone turn,
  the knee's height is what matters, so a spot nearer than the thigh's length
  is pushed out rather than overshot into the floor) and the shin laid flat
  behind it. The hips come down (`KneeHipDrop`, `HandHipDrop`: the hip a
  thigh's length from the planted knee's spot, the shoulder an arm's length
  from the planted hands' — this rig's arms do NOT reach the floor from the
  clip's all-fours shoulders, which is why a body-relative hand slid with the
  body before) at no more than `0.6 m/s` and never more than `0.25 m`. The
  same knee-to-floor drop now applies through the push-up (from `30 %`), the
  half-kneel (the trailing knee) and the first `40 %` of standing: the V2
  clip leaves the knees `15 cm` off the floor and he floated on all fours.
- **The drunk walk.** `PlayerDrunkGaitModel`, pure and seeded (`EpisodeSeed ^
  0x6A17`, reseeded with the balance model), runs on the Walk clip's own
  cycle (the left heel contacts at `0`, the right at `0.5`; each boot swings
  the half cycle centred on the other's contact). At the start of each swing
  the boot draws its landing: outward `0.03 + 0.14·t ± 0.08·t` (clamped
  `0.17`), across the midline one time in `0.15·t`, `±0.15·t` long, up to
  `0.05·t` higher, toes out to `12°·t`; the half-step's cadence `1 ± 0.25·t²`.
  The landing eases in over the swing and HOLDS through the stance — a
  constant offset in the root's frame keeps a planted boot planted. The late
  layer takes per-boot offsets, yaws and lifts, solves the disordered boot at
  full weight through its whole cycle, turns the toes about up before any
  ramp tilt, and lowers the HIPS by any reach shortfall (a wide stance is a
  squat), never the sole off the floor. The cadence multiplies the walk's
  share only, before the Run lerp, so the pinned run cadence holds. The
  model's heading weave, computed and tested since the balance slice but
  never wired, now turns the desired velocity's DIRECTION in the motor
  (`SetBalanceHeadingWeave`), never the yaw. Sober every term is exactly
  zero and the seed's sequence is untouched.
- **Face and head.** `PlayerFacialAnimationState.Advance(dt, allowIdle,
  intoxication, mood)`: the blink's shut time `0.12 → 0.30 s` and lids
  `0.055 → 0.12 s`, intervals `× 0.8` at full; the resting face `Neutral`
  under `0.35`, drowsy spells (`1.4 s` on their own table, walking or not)
  from `0.35`, `Glazed` from `0.6`, `Slack` from `0.85`; priority `Out`
  (eyes shut, no blink) > blink > `Grimace` > `Tense` > `Drowsy` > idle
  glances > the level. `PlayerFacialMoodRules.Resolve` is a pure table over
  what the presentation already holds (balance phase and brace, the ragdoll
  and its age, the rise stage and progress, a slump); the presentation is the
  only computer and the only writer of the face, the fall's clips no longer
  own it, and it is drawn under the ragdoll too. The atlas gained four cells
  (`Drowsy c2r2`, `Glazed c3r2`, `Slack c0r1`, `Grimace c1r1`; python rows are
  Unity's `3 - r`), `CanonicalFaceCells` is nine, and an atlas without them
  falls back (`PlayerFacialExpressionRules.Fallback`: Drowsy → HalfBlink,
  Grimace → Tense, the rest → Neutral) so the runtime never depends on the
  reimport having happened. The
  head: `IntoxicationHeadModel` (seeded, `0x4E0D`) sums into the attention
  turn under its limits — droop `-Lerp(2, 12, t)` fading in over the first
  fifth, wander `±3/8/4°` at `0.15/0.10/0.12 Hz`, a `15°` nod every `6–14 s`
  above `0.6` (`0.25 s` down, `0.8 s` back), and the lean through
  `SecondOrderFilter(6, 0.5)` minus the lean itself (`0.6` share) so the head
  arrives late and overshoots. The attention rules' pitch is positive UP; the
  model's is written chin-down and enters negated. `DrunkHeadRollSign` and the
  pitch sense are pinned by `Player3DDrunkFacePlayModeTests` probes against
  the actor's frame, the way `HeadLiftSign` was.
