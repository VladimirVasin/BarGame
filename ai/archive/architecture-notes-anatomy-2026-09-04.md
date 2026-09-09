# Architecture notes archive — the 2026-09-04 anatomy entry

A dated work-log entry that had been appended to `ai/architecture-notes.md`,
which `ai/README.md` designates a living document to correct in place rather
than append to. It is a session record, not a decision register: the same day
is already recorded in `ai/work-log.md`. Archived verbatim.
Active notes: [`ai/architecture-notes.md`](../architecture-notes.md).

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
- **Accepted and implemented 2026-09-05 — hand props are separate
  attachments, never part of a body:** the user's rule is that whatever an
  NPC holds is a thing in the hand, not skin on the body. Until now every
  held object was a skinned `ACC_*` part of its design's FBX, and the
  pool-eligible designs (mourner, babushka, weigher, watchman, chess and
  checkers players) roam the street anonymously, so a roaming mourner walked
  with her bouquet and a roaming babushka with her carpet beater. Three
  unrelated tables hid renderers by name to paper over that —
  `CityPedestrianHeldProps` for the pool, `CityBalconySmokerAccessory`
  (which also cloned the babushka's skinned cigarette onto other bodies and
  hid props by prefix) and `CityCourtyardResidentFactory` — plus per-role
  `ApplyPropVisibility`, `HideHeldBouquet` and a `SetCoffeePotVisible` that
  toggled body renderers. All of it is gone. The nine props (carpet beater,
  cigarette, funeral bouquet, chalk, fishing rod, smoking pipe, cafe
  cigarette, service towel, coffee pot) are one module-level `HAND_PROPS`
  table in `build-city-pedestrian-3d-model.py`, factored out of the six body
  builders with the same helper calls, constants and palettes, so the
  geometry is byte-identical to what the bodies carried; a
  `build_hand_prop_library` run puts each prop under an Empty
  `PROP_<Name>` at its socket bone's head, remaps the vertices through the
  reference design's own `remap_geometry_point`, and exports an EMPTY+MESH
  FBX (`33` meshes / `840` triangles, no armature, deterministic sha256
  signature proved by a second in-process build) with a manifest naming
  socket, reference design, parts, anchors and bounds. `CityPedestrianHandPropAssetSetup`
  instantiates the library and the reference body FBX at identity and
  measures the `Mount` in the bind pose: `mount = socket.worldToLocal ·
  Translate(E)`, `freeLocal_P = Translate(−E) · world_P`, both decomposed
  with a `1e-5` round-trip proof and a `socket.l2w · Mount · part == source`
  check at `1e-4`; anchors are computed from imported vertices
  (`farthest_from_socket` for the rod tip, `part_center` for the pipe ember,
  `farthest_from_part` with `LookRotation(tip − body centre, up)` for the pot
  spout) and saved as children of the Mount, palettes bound through the
  bodies' own `BuildPaletteVariant`. Each prefab under
  `Resources/Pedestrians/HandProps` is root (identity, `CityPedestrianHandPropRegistry`)
  → `Mount` → parts + anchors. At runtime `CityPedestrianHandProps.Attach(body,
  id, palette)` parents the root under the named socket (`SOCKET_Grip.R/L`,
  `SOCKET_Cigarette.R`, `SOCKET_Mouth`) and restores the Mount pose,
  `Place` sets a free-standing instance with an identity Mount, `Detach`
  destroys it; wrong sockets and missing prefabs throw. Anchors now live on
  the props: `ANCHOR_RodTip` feeds the fisherman's line, `ANCHOR_PipeEmber`
  and the ember renderer feed his pipe effect, `SOCKET_CafePotSpout` rides
  the pot and no cafe body may carry one. The laid grave bouquet is the same
  `FuneralBouquet` prefab placed by the pure `CemeteryLaidBouquet` (stems→bloom
  axis turned onto grave-local `+Z`, the rotated bounds rested on the grave's
  measured `SlabTopY`), not primitive cubes. Removing the parts dropped three
  bodies under their triangle floors, and floors are duplicated in the
  generator and in C# and compared EXACTLY, so both sides fell together:
  babushka `1650` (`1,692`), mourner `1600` (`1,712`), fisherman `800`
  (`892`). The mourner's street clips inherited `mourner_base_pose`, which
  folds both forearms around a bouquet she no longer holds, so
  `MournerStreetIdle/Walk` now build on `mourner_street_pose` — the mourner's
  base with the weigher's hanging arm rows — and the personal-space bank,
  whose mourner base is that idle's first key, was rebuilt on it. The cafe
  woman's cigarette-contact proof survives with the part off her body: the
  validator evaluates the prop's rest vertices through the posed
  `SOCKET_Cigarette.R` (`rig.matrix_world @ pose_bone.matrix @
  bone.matrix_local.inverted()`) and its recorded numbers moved by at most
  `1e-6`; an EditMode probe showed the socket-driven prop lands where
  `hand.R` skinning put the old tube to `0.0000 m` at `t=0` and `t=0.31`.
  Traps this cost: (1) Blender's `FBX_SCALE_NONE` puts the unit scale on root
  objects, so a socket's lossyScale is `≈100`, the Mount comes out at
  `0.01` and every part at `100` — scale uniformity is compared relatively
  and tests judge WORLD bounds, never local scales; a prop `100×` too big
  means `RestoreMountToSocketPose` was skipped. (2) Parts are parented with
  `matrix_parent_inverse` and their location at the socket head, and the
  Empties AND the meshes are selected for export, so every part's world
  origin equals its Empty (asserted `1e-6` in Blender, `1e-4` in Unity)
  before anything is measured. (3) `LoadAssetAtPath` on an un-imported path
  is a silent null: the prop build force-imports the FBX, the manifest and
  every reference body synchronously first, and the batch order is
  pedestrians → cafe cast → hand props → personal space, the prop
  postprocessor gated on `IsAnyPipelineBuilding`. (4) A Mount is valid only
  for the bind pose it was measured against: the registry stores
  `ReferenceSocketRestPosition/Rotation`, `ValidateOrThrow` re-measures the
  reference body within `0.0001 m / 0.02°` and queues a rebuild on drift —
  and because the pedestrian prefab turns its Model child `180°` about Y, the
  test comparing a live socket with that rest pose must work in
  `ModelRoot`'s frame. (5) The cafe presentation's `ApplyClip`/`Shutdown` and
  the registry's `Configure`/`OnEnable` ask for pot visibility before any pot
  exists, and the service presentation throws on a null spout: the registry
  remembers the last request and applies it on `AttachCoffeePot`, the
  factory attaches the props BEFORE `presentation.Initialize`, and the cafe
  asset setup's expected rig-transform count had to lose the attendant's
  `+1`. (6) The prop FBX imports READABLE after all (amendment 4 reversed):
  the cafe PlayMode contact sweeps need vertices, and sweeping a thin tilted
  cigarette as its local box read `0.058 m` of lip distance; the same test's
  contract was re-anchored on the filter tip (`11 mm` at the drag, the
  tube/filter junction is `44 mm` by construction) and its axis threshold on
  the generator's own validated `0.889` (`0.86`, not `0.94`).
- **Implemented 2026-09-05 (later the same day), the user's three asks on
  the bout — a sound, the head and neck bent harder, an expressive
  convulsion while the stream runs:** no canon moved; the §6 row already
  covers the bout. (1) THE SOUND. `Retch` is a three-beat heave (`0.62 s`,
  volume `0.55`: the breath dragged in, a harmonic voice climbing `95 →
  150 Hz` rattled at `22 Hz` with a `520 Hz` formant, a wet choke), the
  spurt `VomitGush` (`0.7 s`, `0.5`, every `0.9 s`) gained a gurgle, a body
  and bubbles, `VomitSplat` is louder (`0.36`), and a new `VomitCough`
  (`0.55 s`, `0.5`: two chest coughs, a spit, the breath back in) is cued on
  every `BurstEnd` — appended at the END of the enum and the table, the
  way the indexed table demands. The stream itself is no longer re-cued
  one-shots: `HeroVomitStreamSound` synthesises a `2 s` loop in the same
  crunch (hold `3`, `512` steps, low-pass `2.8 kHz`; pump at the flow's
  `3.2 Hz`, a `7 Hz` push, an `84 Hz` gurgling body, bubbles on an
  irregular grid; the tail folded into the head over `90 ms` so the seam
  carries neither click nor silence), and `HeroVomitStreamEffect` owns
  ONE looping `AudioSource` on the emitter (linear `1.2–13 m` like the
  pool's voices, `SfxWorld`) whose volume the controller sets from
  `Pose.Flow` every tick — the pump, the attack and the tail are heard as
  drawn, and `StopAndClear` silences it. It never plays outside play
  mode. THE EMITTER FOLLOWS THE MOUTH ONLY IN LATEUPDATE: the effect's
  `Update` (order `280`) runs after the presentation's `Update` (order
  `0`) has evaluated the graph into the raw clip pose and before its
  `LateUpdate` folds the body, and the particle step begins between the
  two — a `FollowMouth` there placed the emitter on the unbent mouth every
  frame, which the deeper fold turned into a stream leaving from where the
  head had been. One frame of lag on a held pose is the price; the mouth
  sounds take `MouthPosition` (the emitter) for the same reason. (2) THE BEND.
  `HeadDownDegrees` was raised from `14` to `30`; the user's `2026-09-06`
  adjustment lowers the held pitch to `24°`, slightly lifting the chin over
  the unchanged folded torso. The heave remains `12°` over
  `0.42 s`, plus `PumpHeadDegrees 5` with every push; the presentation
  applies the bout's chin-down beside the glance with its OWN split,
  `VomitNeckShare 0.55` (the glance's `0.38/0.62` is a nod; being sick
  folds the neck), still outside the clamp. The ragdoll drive is
  `120/11/80`, clamped at `45°`. (3) THE CONVULSION. `PlayerVomitPose` grew
  `TorsoPitchDegrees` (`22` held, `+9` at the heave, `+4` with the pump —
  positive is forward in the layer's `chestPitchDegrees`, the hiccup's
  snap-back negated), `CrouchMetres` (`5 cm`, `+4 cm` at the heave),
  `BraceWeight` (both palms braced on the thighs just above the knees — `55 %`
  down the thigh, `5 cm` ahead of the bone; the knee itself is out of an
  arm's reach at this fold and the first sheet showed arms hanging
  forward — through `PlayerArmReachPose`, scaled by `1 − locomotionBlend`
  so a man walking through a bout keeps his arms, and yielding to the
  balance model's brace hand), `WipeWeight` (the right hand to the mouth
  through the gauge's own `mouthReachWeight`, `0.4 s` up, `0.6 s` held,
  `0.6 s` down after the last burst — `TotalSeconds 10.2 → 11.0`) and
  `Pump` (the flow's half-sine scaled by the burst's strength; zero
  between bursts). All of it rides one `EvaluateHeld` curve with the
  head, so nothing lingers after the bout. Pinned by `HeroVomitTests`
  (`Pose_DoublesOverBracesOnTheKneesConvulsesWithThePumpAndWipes`, the
  score with `Cough`), `HeroVomitStreamSoundTests` (determinism, RMS band,
  pump crests over troughs, a quiet seam) and `RetroSfxLibraryTests`
  (durations and minimum volumes).

- **Accepted and implemented 2026-09-05 — the lost bout ends in vomit, and
  the vomit takes no lock either:** by the user's decision the Fail of the
  nausea gauge is no longer a stub. `IntoxicationNauseaController` only
  raises a `ConsumeFailCue`; the bout itself is `IntoxicationVomitController`,
  a second plain object ticked from `IntoxicationStatusController.Update`
  right after the gauge, with its OWN gates — not a branch inside the gauge
  controller, because the gauge cancels on `!CanRun(isFalling)` and holds on
  `BarMinigameModalLock.IsAnyLocked`, and a fall takes exactly that lock
  (`fallLock`), while the user's decision was that the bout survives the
  fall and goes on lying down and through the rise. So the vomit ignores the
  modal lock altogether; only a scene transition, a vehicle and `Shutdown`
  cancel it, and a cancel emits no further cues (a Relief already granted
  stays granted, the residue stays on the ground). It runs on SCALED
  `Time.deltaTime`, zero while paused, unlike the gauge's calendar clock:
  the stream's arc, the particles (`useUnscaledTime = false`), the ragdoll
  and the sounds all live on scaled time, and a `timeScale 0` pause has to
  freeze the head, the stream and the schedule together — the `≤ 12 %` gap
  to the gauge at the top is the one already accepted for the hand (note
  (6) of the nausea entry). The schedule is a pure deterministic
  `HeroVomitModel` (`HeroVomitRules`: onset `0.4 s` — the head goes down and
  the retch is heard before anything flows — bursts `3/1/1 s` at strengths
  `1/0.55/0.55` with `2 s` pauses, a `3.2 Hz` pulse of depth `0.45` in the
  flow, head down `14°` plus a `4°` heave in the first `0.3 s` of every
  burst, blend-in `0.35 s`, blend-out `0.8 s` after `9.4 s`, `IsActive`
  until `10.2 s`) with a FIFO cue queue, so a dropped frame delivers every
  crossed cue in order and two models advanced by the same steps give the
  same trace. THE HEAD GOES DOWN IN EVERY BODY STATE, by two different
  writers: on his feet, in a clip and through the rise it is a term in
  `Player3DCharacterPresentation.ApplyAttentionPose` — the single pass over
  `neckBone`/`headBone` — subtracted AFTER the `[−32, +10]` clamp (inside
  it the drunk droop would eat it), not under `headFree`, written with the
  same `0.38/0.62` split through the same capture/restore pair, so over the
  Rise clip's `HeadLift` the two simply add; the attention pitch is faded by
  `1 − vomitWeight` while the attention weight itself keeps running. Under
  the ragdoll PhysX owns the bones, so
  `Player3DRagdollController.SetHeadDrive(deg)` puts a slerp drive on the
  head's `ConfigurableJoint` (`spring 90`, `damper 9`, `maxForce 60` — a
  first cut at `40/6/25` moved a head lying on the floor by four degrees in
  three quarters of a second, the right way and too timidly to read;
  `targetRotation = AngleAxis(HeadDriveSign · deg, right)`), cleared in
  `Cancel`, `BeginRise` and after `FreezeBodies`. `HeadDriveSign = −1f` sits
  beside `JointFlexionSign` and was pinned the way the flexion sign was:
  `targetRotation` is the connected body's frame relative to the joint's,
  i.e. inverted, so a chin-down asks for a negative target — the capture
  fixture measures `MeasureHeadPitchDownDegrees()` before and `60` frames
  after the drive starts and names the one constant to flip if the chin
  goes up (the first run read `+3.96°` with `−1`, so the sign holds); the
  bone term on his feet is probed the same way through
  `DebugHeadPitchDownDegrees` (`+8°` and more within thirty frames). THE COLLISION IS A RAYCAST PER PARTICLE in
  `HeroVomitStreamEffect.LateUpdate` (order `280`, after the mouth has been
  lowered), never the `collision` module. The `2026-09-06` pressure adjustment
  keeps the source `2 cm` outside the actual mouth, rotates launch direction
  at most `25 degrees` toward the hero's forward and uses `2.8–3.4 m/s` for
  the full burst / `1.8–2.3 m/s` for the weak ones. The same launch feeds rods
  and chunks; gravity then bends their world-space flight. This compensates
  the combined head/torso fold without moving the emitter to the old upright
  mouth position. The mouth socket sits INSIDE the
  hero's `0.32 m` capsule and, lying down, the stream crosses his own
  ragdoll proxies, and the module cannot exclude an object; and the stair
  treads he may be standing on are FootProbe triggers, which the module
  ignores wholesale. Each live particle is swept back along
  `velocity · dt · 1.15 + 2 cm` with `QueryTriggerInteraction.Collide`, the
  nearest hit that is not a trigger (except `FootProbeSurface`), not a
  child of the hero and not a child of the residue wins, the normal is NOT
  rejected by `y` (walls and furniture take residue too), and the particle
  is killed by writing its `remainingLifetime` back through `SetParticles`
  — without the write-back the kill is lost. The stream, the chunks and the
  splashes are MESH particles (cubes stretched along velocity) on `Ps1Lit`
  — the first lit particles in the project — not on the unlit
  `CityAtmosphereParticle` material every other particle uses: an unlit
  yellow-green jet at night would glow like neon (art §15a) and would not
  separate by lightness from the dark chunks inside it in the Begotten
  print; lit and opaque it takes the street light the floor takes, and the
  residue on the ground is lit the same way. `enableGPUInstancing` is off
  (URP Lit has no procedural particle instancing; Unity bakes the mesh).
  THE RESIDUE OBEYS THE BAN ON FLAT QUADS: `HeroVomitResidueModel`
  coalesces impacts within `0.3 m` on the same normal (`dot > 0.9`) into
  one patch that grows with the volume (`radius = min(0.45, sqrt(area/π))`,
  the centre pulled toward the hit by `volume/area`), `12` patches at most
  with the oldest evicted, `48` chunks; each patch is a centre plus ten rim
  vertices at `0.62–1.0 · r` by a stable hash, in the surface's tangent
  plane, lifted `6 mm` along its normal, UV in metres over `0.18 m` tiles of
  a runtime `32×32` slurry texture (base, `30 %` darker cells, `10 %` dark
  lumps, `6 %` pale crumbs, point-filtered) — an irregular, textured, lit
  polygon, never a rectangle, with dark cubes sunk `40 %` into it for the
  pieces. THE SOILED MOUTH IS AN ATLAS FLAG, not a second face pipeline:
  the face atlas became `8×4` (`512×256`), each of the now thirteen expressions has a
  soiled twin at column `+4`, `Player3DFaceAtlasCell` gained `Soiled` (the
  three-argument constructor stays for the mother),
  `Player3DFaceAtlasBinding.TryGetTextureTransform(expression, soiled, …)`
  matches the pair exactly and falls back to the clean cell when the twin
  is missing, and `Player3DFaceAtlasPresenter.Apply(expression, soiled)` is
  the only new call the presentation makes. A runtime composite was
  rejected (a second face path around the atlas's sha256 contract) and so
  was squeezing twins into the seven free `4×4` cells (no room for nine;
  `Watchful`/`Tense` would have jumped to a soiled Neutral). The flag is
  `GameSessionState.HeroMouthSoiled`, set after the FIRST burst and cleared
  by the shower, by teeth-brushing BEFORE its daily gate (a wash always
  washes), by sleep, by `ResetDrinkingState` and by the session reset. THE
  RELIEF IS `GameSessionState.RelieveIntoxication(points, reason)`, the
  first API that lowers the level by an amount: it clamps at `0`, returns
  what was actually removed, zeroes the balance delay at `≤ 60` and does
  NOT touch `intoxicationRecoveryElapsed` — a relief is not a drink, so the
  sobering-up already under way is not restarted. The user's decisions:
  `−20` in parts, `7/7/6` at each burst's end, not once at the end; the
  soil holds until a wash or sleep, across scenes; the movement is free —
  no lock, only the interact key is claimed for the `≈ 10 s`, and the
  gauge's rest is REARMED (`boutsSuspended`) so the next bout is at least
  `20 s` away. The accepted consequence: from `100` one Fail lands on `80`,
  the last stage ends and the bouts, the hiccups and the scattering letters
  stop until the next drink; a second Fail (`60`) switches the falls off.
  My own choices, each one constant to reverse: the soil comes after burst
  one (`SoilAtBurstIndex = 0` — a face cannot be clean after three seconds
  of it; the brief's literal order is `2`), and the `0.4 s` onset before
  the first flow. Seed salt `0x564D`, the seventh drunk salt. Canon: the §6
  registry row of `2026-09-05` (level `0`; it lifts the §24.45 stub sentence
  and «§16.15 — провал ничего не отнимает» and nothing else) and story
  decision §24.46; the closing sentence of the nausea note above — "no §6
  row" — is superseded by that row.
