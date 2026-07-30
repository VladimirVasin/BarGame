# Release notes

## Unreleased

### 2026-07-30 — Window and walkable third-floor balcony

- Replaced the black right side of the Home interior with a real window and an
  open glazed door leading onto a balcony in the same scene.
- The balcony is fully walkable at third-floor height and overlooks the same
  seeded street as the exterior home. Its open rails keep their light
  silhouette while an invisible safety boundary prevents accidental falls.
- Nearby roads, buildings, lit windows, lamps and signals now continue beyond
  the room instead of ending in darkness; the City version of the home has the
  matching balcony facade.
- A cold, shadowed shaft of night light enters through the window, while the
  existing warm room lamp and cold bathroom tube keep their previous lighting.
  The window and door use shared transparent glass.
- Added a dedicated fixed camera shot that takes over when the hero steps
  through the door and onto the balcony.
- Sealed unintended ceiling, side-wall and front-entry gaps, removed the stray
  orange exit marker from the camera edge, and kept exterior scenery strictly
  outside the facade without changing the window, open door or walkable
  balcony.

### 2026-07-30 — Animated sleep at home

- The bed in the Home interior is now interactive from its open side. Press
  `E` once to lie down and fall asleep; the hero remains in a breathing sleep
  loop for as long as desired, and a second `E` wakes them up.
- Added a bespoke 64-frame sequence with a full lie-down, persistent sleeping
  loop and separate wake-up animation. The normal walking puppet and both of
  its shadows return only after the wake-up finishes.
- Slowed the sleeping loop to one five-second breath: the chest rises at
  `4 fps`, pauses briefly at full inhale, then settles into a longer rest
  after exhale.
- The sleeping hero now follows the bed's perspective, keeps their head on the
  pillow side and sits evenly within the mattress instead of appearing
  mirrored or screen-horizontal. The full sleeping silhouette now clears the
  bedding instead of visually sinking into the mattress and blanket.
- Movement remains locked for the complete sleep interaction, while the wake
  prompt becomes available during sleep in both Russian and English.

### 2026-07-30 — Approved Home framing and practical lights

- Moved the main-room fixed camera into the approved bed-side corner at
  `(-4.48, 3.00, -3.25)`, Euler `(28°, 55°, 0°)`, with a `64°` FOV. The
  bathroom now uses `(1.82, 2.20, 0.86)`, Euler `(30°, 38°, 0°)`, with a
  `92°` FOV.
- The warm hanging bulb and cold bathroom tube are now visible HDR emitters
  with halos physically aligned to the light they cast, so illumination has a
  readable source in both shots.
- Blocking junk closes the camera corner without obstructing the authored
  walking routes, and the bathroom toilet now faces naturally into the room
  with its cistern at the right wall.
- The hero now aligns to the complete fixed-camera plane instead of only its
  horizontal direction. This preserves the original `64 x 96` sprite
  proportions in steep views and automatically returns to normal billboard
  behavior after leaving the fixed-camera controller.

### Player home

- Every generated city now contains one recognizable player home beside a bar
  street. Its teal facade, cool windows, porch light and mailbox distinguish
  it in the world, while the city map gives it a separate labeled house icon.
- The interior is now a dim, neglected old alcoholic's bachelor flat: stained
  walls, a boarded dead window, six main-room furniture groups, worn bedding,
  dirty dishes, bottles, cans, an ashtray, old papers, a radio and sparse
  personal remnants sell long-term poverty and drinking without blocking the
  walking routes.
- Added a complete separate bathroom with tiled surfaces, an ajar doorway,
  toilet, shower and curtain, sink, cracked mirror, rusty exposed pipes, leak
  damage and a floor drain.
- A visible dirty-yellow hanging lamp and cold bathroom tube sit over a
  subdued home-only color grade, sparse dust and a dedicated refrigerator,
  mains, pipe and drip ambience.
- The single Main Camera now hard-cuts between fixed main-room and bathroom
  corner shots. Wider hold areas add hysteresis at the doorway, so hovering at
  the threshold cannot flicker the view; orbit input does not move either
  fixed pose. Home temporarily aligns the player's billboard to the complete
  camera plane, preventing both edge-on and vertically compressed sprites.
- Entering and leaving still use the same door transition as bars and return
  the hero to the matching exterior approach without losing route, visit,
  wallet or intoxication progress.

### Bar-adjacent city start

- A fresh run now places the hero on a safe street node beside their home and
  its neighboring generated bar instead of at the distant city center.
- Returning from a bar remains unchanged and still restores that specific
  bar's entrance position.

### Bar drinks and session wallet

- Every bar now has a separate counter point where the player can buy one of
  nine ordinary drinks without starting or completing the bar's minigame.
- A fresh session starts with `$999`. The shop shows each price, the current
  balance and the resulting intoxication before confirmation; successful
  purchases deduct cash and consume the drink immediately.
- Water remains available at maximum intoxication, costs `$2` and does not
  sober the player. Unavailable or unaffordable purchases leave both cash and
  drinking progress unchanged.

### Opaque player hands

- Restored the missing skin and bandage pixels in both lower arms across all
  eight player directions, so the character's hands no longer show the world
  through transparent gaps.
- Rebuilt the jointed puppet atlas without changing the character design,
  facial artwork, directional silhouettes or animation hierarchy.

### Lower third-person framing

- Raised the exterior and interior camera aim points so the hero now occupies
  the lower part of the screen and leaves more view ahead while walking.
- Kept the existing camera distance, field of view, orbit and obstacle
  behavior unchanged.

### Physically raised city surfaces

- Streets and park paths now use their rendered height as a real walkable
  surface. The player steps onto them instead of sinking through to the city
  ground beneath.
- The park lawn and central plaza also have matching surface colliders. Small
  height changes use the existing character step behavior, leaving room for
  authored stairs in later city geometry.

### Support diagnostics

- Added a bounded structured `debug.log` for reproducible support reports. It
  records build/scene/seed context, generated city, bar and home summaries,
  route and visit changes, correlated transitions, minigame results,
  drinking/balance outcomes and Unity warnings or exceptions without
  per-frame telemetry.
- Press `F8` in the city or bar to capture the current player/session/world
  state immediately. `Shift+F8` opens the directory containing the active log.
- Logs rotate automatically at 5 MiB and retain three archives; release builds
  use a quieter profile while development builds include phase timings.

### District-scale city and central park

- Expanded the default city from `4 x 4` to `12 x 12` blocks, roughly
  `288 x 288 m`, with cross-city arterials and a deterministic connected road
  graph.
- Added Old Town, Residential, Industrial and Nightlife districts with
  different building proportions, heights, palettes and street details.
- Added a central `4 x 4`-block park with a walkable lawn, crossing paths,
  plaza, trees, benches, hedges and four open gates connected to surrounding
  streets.
- Moved the four bars into different urban districts and enforced at least
  `120 m` of traversable graph distance between every pair.
- Updated the full-screen map with district colors and localized labels plus
  distinct park land and paths.
- Spatially indexed walkability, changed route finding to a binary min-heap
  and batched roads, fences and lamp geometry into `48 m` chunks so the larger
  city remains practical at runtime.

### Cinematic expanded bar interior

- Expanded the bar into a denser `22 x 16 m` venue with a long counter and
  mirrored backbar, bottle shelves, three booths, four social tables, a
  curtained performance stage and dedicated activity space.
- Added entrance dressing, posters, beams, wainscot, a ceiling fan, practical
  lamps, service details and atmospheric dust so the room reads as a lived-in
  venue from every camera angle.
- Added 12 animated patrons: a working bartender, performer, seated booth
  groups, standing guests and a roaming visitor. Their silhouettes layer
  correctly around the player and furniture.
- Warm cinematic grading, bloom, vignette and film grain now combine with six
  shadowless practical lights. A skippable opening camera move establishes the
  bar before returning cleanly to normal follow control.
- Added a subtle spatial crowd bed and occasional glass/chair sounds while
  retaining the bar theme and ambience.
- Beer Pong, Split G and Tincture remain available in their respective bar
  variants, with clear paths between the entrance, counter, activity and exit.

### Five-stage intoxication and balance

- Replaced the former temporary intoxication status with one persistent
  percentage-driven system. The HUD stays hidden at `0`; positive values fill
  five 20-point segments named Light Buzz / «Лёгкий хмель», Tipsy /
  «Навеселе», Drunk / «Подшофе», Unsteady / «Шатает» and Very Drunk /
  «В стельку».
- Higher values continuously strengthen puppet sway, arm spread, bent knees,
  camera roll, movement slowdown and world-image vignette, ghost/chromatic
  doubling, warp, warmth and exposure pulse. The strongest level lowers
  movement speed to `0.70x`; all presentation eases into a changed value.
- Above `60`, periodic balance checks draw a crisp semicircular gauge over the
  hero. Hold arrows or A/D, D-pad or left stick to keep its moving arrow in
  the shrinking green center before the red risk meter fills.
- Checks become longer and more frequent as intoxication rises, with stronger
  disturbances and less player authority. Failing drops the visual puppet to
  the arrow side, holds it down briefly and raises it again while the
  physical player root remains safely stationary.
- Balance checks pause around maps, minigames, F9 and scene transitions. They
  resume only after a safety delay; reaching `60` or below cancels them.

### Classic fixed-camera door transitions

- Entering or leaving a bar now passes through a dedicated black-void scene
  instead of cutting directly between locations.
- A close fixed camera watches the handle turn and the low-poly door open,
  eases toward the threshold, then fades fully to black over `3.15 s`.
- The door swings outward toward the player, while the revealed doorway stays
  completely black instead of exposing a flat destination-colored panel.
  Warm/cold door lighting, a short latch and two hinge-creak beats reinforce
  the movement.
- The destination preloads behind the animation and cannot activate until
  both loading and the final blackout are complete.

### Restricted fog visibility

- Thickened the city's luminous gray-green distance fog and capped its camera
  range at `48 m`, so the next blocks dissolve into haze instead of remaining
  clearly readable across the map.
- Replaced the separate dark camera backdrop with the terminal fog color, so
  gaps between distant buildings no longer expose a black edge of the world.
- Made the existing local drifting fog more visible without increasing its
  36-particle budget.
- Bar interiors remain fog-free and retain their `220 m` camera range.

### Opaque diagonal head silhouettes

- Restored 51 turntable-authored head, cheek, ear, hair and neck pixels that
  the original chroma-key pass had left transparent across `FrontRight`,
  `BackRight`, `BackLeft` and `FrontLeft`.
- Regenerated the reference, jointed-parts and all five body-expression atlas
  rows. Rear diagonal expressions remain neutral; only their missing alpha
  coverage changed.

### Grounded player foot contact

- Lowered the visual foot baseline from `4 cm` to `5 mm`; the previous
  always-positive walk bob could place both soles as much as `7.5 cm` above
  the road.
- Added atlas-derived left/right foot contacts. The lower stance foot now
  remains pinned through the gait cycle while the opposite foot swings, and a
  short `12 mm` upper-body compression plus `5 mm` sole compression marks
  each footfall.
- Breathing and impact motion now affect the body and arms without lifting
  both legs during idle or walking.
- Added a small procedural contact shadow fixed to the grounded actor root.
  It stays beneath the feet independently of puppet bob, camera orbit,
  directional-light state and the existing realtime silhouette shadow.

### Heavy inertial locomotion

- The hero's maximum movement speed is now `2.6 m/s`, half of its previous
  value. Existing acceleration and braking remain intact, so movement still
  ramps and settles instead of changing speed in one frame.
- Reversing direction first bleeds the old momentum. Road boundaries and
  physical collisions discard blocked velocity, so they never release a
  stored push later.
- Modal interfaces, scene transitions, input disable and teleport retain an
  immediate safe stop.
- Walking cadence now follows actual distance travelled rather than playing
  at one fixed rate. Joint settling is softer and body rock is slightly
  stronger, keeping the gait alive through braking before it returns to idle.

### Dynamic player shadow

- The hero now casts a realtime alpha-clipped silhouette in the city, bar
  interior and home interior.
- The hidden shadow puppet faces the main directional light and chooses one of
  the existing eight authored views from the player/light angle, so orbiting
  the camera no longer rotates or flattens the shadow.
- All nine shadow-only body and limb parts now mirror the live joint angles.
  The projected silhouette visibly walks, compresses at footfall and sways
  instead of sliding as one frozen full-body card.
- Street and bar practical lights remain shadowless to preserve the existing
  realtime-light budget.

### Tinctures in a Row minigame

- Added a fourth stable city bar with a `7x7` match-three board, five
  symbol-coded infusion flavors, exactly one starting `XXX` moonshine shot and
  15 accepted moves.
- Invalid swaps return without spending a move. Accepted swaps resolve unique
  matches, gravity, seeded refills and deterministic cascades with a multiplier
  capped at `x5`; boards with no normal move reshuffle automatically.
- Runs of four or more and intersecting matches can create `XXX`, but the board
  never contains more than one. Swapping it with a flavor clears every shot of
  that flavor.
- Normal matches are customer orders and do not increase intoxication. Only
  activating `XXX` immediately saves one `Moonshine`, one consumed drink and
  +24 intoxication; cancelling cannot refund it, while F9 runs remain isolated.
- Added mouse click/drag, keyboard and gamepad controls, RU/EN UI, an
  activity-specific tray/shot/`XXX` interior display, a point-filtered
  `640x360` backdrop, transparent 4x4 sprite atlas and generated swap, match
  and moonshine-burst sounds. Swaps, gravity and refills animate between
  immutable board snapshots with synchronized cascade effects.
- Closing during the terminal cascade still completes the visit. Reaching
  100 intoxication finishes after the cascade and leaves the player at the
  permanent highest percentage-driven stage.

### Split the G minigame

- The third stable city bar hosts Split the G; together with Tinctures in a
  Row, the default four bars now have one distinct activity each.
- Hold Space, LMB or gamepad South for one irreversible virtual sip. The exact
  liquid boundary disappears behind the tilted pint, hand and foam until the
  `1.4 s` settling phase reveals the result.
- Remaining level is derived from total unscaled hold time, so the same sip
  scores identically at different frame rates. Perfect/Excellent/Good/Close/
  Miss use 1/3/6/10-percent error bands.
- A session allows up to three fresh dark-beer glasses and keeps its best
  result. Continue can finish early; the third result finishes automatically.
- Every non-empty sip immediately saves its actual consumed fraction as dark
  beer progress, while F9 debug launches remain fully isolated.
- Added a dedicated `640x360` pixel-art bar backdrop, transparent 4x4
  pint/hand/foam/effect atlas, localized RU/EN interface and generated gulp
  sound.

### Cinematic player presentation

- Moved the centered chase camera much closer with separate `2.6 m / 53°`
  exterior and `2.2 m / 57°` interior profiles while retaining a complete
  full-body composition.
- Increased orbit, focus, obstacle-recovery and cinematic blend inertia for a
  heavier, smoother response. Focus lag remains bounded, teleport snapping
  and immediate inward obstacle avoidance are preserved, and the arm now
  eases back out instead of popping.
- Camera motion now fades out and restores with the shared modal lock used by
  the map, minigames and F9 launcher.
- Strengthened the procedural living idle with readable breathing, weight
  transfer and a short gesture that alternates between the left and right
  arms; all motion still blends with walking and yields progressively to
  strong intoxication, balance and fall poses.
- Expanded facial animation to five deterministic states: stronger
  half/closed blinks plus watchful and tense idle expressions in all five
  visible-face directions. Rear views remain neutral, locomotion cancels the
  idle-only expressions, the visible puppet still uses exactly nine renderers
  and no sprite is mirrored.

### Eight-direction player prototype

- Added eight unique front/side/back views without replacing the character's
  modular animation principle: the current rig uses one body layer and
  separate upper/lower segments for both arms and legs.
- Camera orbit no longer turns the hero. Movement stays camera-relative, while
  the hero keeps the last actual movement heading when stopping.
- Added 5-degree directional hysteresis, a shared foot pivot and explicit
  non-mirrored views to prevent boundary flicker, size jumps and asymmetric
  detail errors.
- Restored 259 face pixels accidentally removed by chroma-key processing; all
  visible facial pixels are opaque while the existing silhouette, clothing and
  palette remain unchanged.
- Walking now rotates shoulders, elbows, hips and knees in every direction,
  alongside lightweight bob/rock and the existing whole-puppet intoxication
  sway. Full multi-frame idle/walk animation remains a future art pass.
- Corrected front/back walking so limbs swing in depth instead of fanning
  sideways. Left/right limbs now alternate explicitly, arms oppose the
  same-side legs, diagonals blend screen/depth motion and far limbs pass behind
  the torso.

### Visible road-edge fences

- Added low ochre two-rail barriers along every exposed road edge and across
  dead ends, making the road-only movement boundary visible in the city.
- Intersections and connected road mouths stay open because fences follow the
  exact perimeter of the combined road surface rather than individual edges.
- Every generated bar automatically receives a `3.30 m` fence opening around
  its entrance walkway; future bar lots use the same data-driven rule.
- The barriers are visual-only, so the existing road/apron movement mask
  remains authoritative and the chase camera is unaffected by the new posts.
  All rails and posts are combined into two render meshes.

### F9 minigame debug window

- Press `F9` in the city or bar interior to open a direct launcher for every
  registered minigame; cocktail mixing, beer pong, Split the G and Tinctures
  in a Row are available now.
- Normal interiors and the debug list use the same explicit catalog, so a
  future game appears after its definition and factory are registered.
- Opening the window closes a conflicting map or minigame and preserves the
  modal input/HUD state when the window or launched game closes.
- The Left/Right arrow keys or clickable `-20/+20` controls change the real
  session intoxication in clamped 20-point steps for rapid stage and balance
  testing without changing its last-drink or consumed-drink context.
- Debug minigame runs remain isolated: they do not mark a bar visited or save
  their own intoxication and consumed-drink changes.

### Beer-pong minigame

- The second bar on the stable city map opens beer pong; the first keeps the
  cocktail mixer, the third hosts Split the G and the fourth hosts Tinctures
  in a Row.
- Aim with mouse, keyboard or gamepad, charge a throw and watch the ball use
  deterministic 2.5D physics with real table and cup-rim bounces.
- Clear six cups in ten throws. Clean sinks score 100, bank shots add 50, and
  unused throws add an early-clear bonus.
- Every miss consumes a light beer, adds 8 intoxication and immediately saves
  that drinking state. The activity ends on a clear, the throw limit or
  maximum intoxication.
- Added a point-filtered 640x360 pixel-art bar/table background, a 4x4
  ball/hand/cup/effect atlas, compact aiming feedback and distinct retro throw,
  bounce, rim and sink sounds.
- Completing the activity marks that bar visited and removes it from the
  itinerary; cancelling leaves both the visit and route untouched.

### PS1-inspired presentation and audio

- Added a PC renderer feature that composites the post-processed world at
  `640x360` by default, applies four-tap footprint averaging and RGB555
  quantization as a 35% perceptual-space blend without a visible screen-space
  dither grid, then point-upscales at exact 2x/3x scale on 720p/1080p outputs;
  lower `426x240` and `320x180` modes remain available.
- Restyled prompts, intoxication HUD, city map and cocktail interface with a
  compact burgundy/amber PS1-era UI theme. General overlays use a logical
  `640x360` canvas, while the cocktail screen remains responsive.
- Replaced smooth cylinder visuals with one shared flat-shaded 8-sided mesh,
  switched the main directional light to hard shadows and disabled camera
  MSAA for sharper low-poly silhouettes.
- Added deterministic `22050 Hz` retro UI, movement, door and cocktail SFX with
  pooled playback, cooldowns and voice limits.
- Added separate procedural ambience for the city, bar and home. The Home loop
  adds refrigerator, mains, pipe and drip layers while preserving the correct
  `city_theme`/`bar_theme` split and mild low-pass treatment on both music
  players.
- Kept runtime IMGUI intentionally crisp after the pixelated world composite.
  The current renderer integration targets PC; mobile parity is deferred.
- Fixed city-map road/route/player-heading lines being displaced by nested
  GUI transforms; the player now uses a clear chevron heading indicator.

### Cocktail mixing minigame

- Replaced the five-pick drink selection with a hands-on three-cocktail game
  at the edge of the bar counter.
- Choose beer, wine, vodka or cognac, then mix in 2–4 unique ingredients from
  a seven-item shelf containing four good matches and three traps.
- Compatible recipes score up to 100 points per cocktail and 300 total; every
  bad addition costs 15 points.
- New pixel-art bottles, fruit, ice and glass animations show each pour,
  rising liquid, good sparks, bad bubbles and the final shake.
- The glass fill now follows the actual inner cavity with a tapered pixel
  surface instead of appearing as a glowing rectangular progress bar.
- Added three-stage progress, a final rank and complete mouse, keyboard and
  gamepad controls.
- Accepting the final minigame result now marks that bar as visited; entering
  the interior or leaving an unfinished game does not.
- Intoxication and served cocktails persist after every stage. A bad served
  mixture keeps its score and intoxication penalties but creates no separate
  timed status; reaching 100 ends the session with the explicit
  maximum-intoxication result.

### Fog-forward city atmosphere

- Raised the night scene's baseline visibility and replaced the dark blue haze
  with a denser luminous gray-green fog.
- Added a slow local fog layer that follows the player without changing
  navigation or collision.
- Street lamps now cast directed pools of light, while lamps, bar entrances and
  flashing amber signals bloom into soft depth-aware halos.
- Retuned City-only bloom, grading, vignette and film grain; the warm bar
  interior remains clear of exterior atmosphere effects.

### Scene music slots

- Added separate resource folders for the looping `city_theme` and `bar_theme`.
- Each theme now plays only in its matching scene and stops automatically on a
  Single-mode transition.

### Noir city night

- Converted the generated city to a fixed nocturnal presentation with
  atmospheric fog, cold moonlight and City-only color grading.
- Added glowing street lamps with warm pools of light and a strict realtime
  light budget.
- Added slow seed-phased blinking amber signals to major intersections.
- Ordinary windows now form a deterministic mix of dark, cool and rare warm
  panes, while bars remain constant bright landmarks.
- Bar interiors remain warm and fog-free.

### City map and bar itinerary

- Added a full-screen map showing the road network, player and every bar.
- Bars can be added, removed and reordered into a numbered visit itinerary.
- Each leg follows a deterministic shortest route over the generated roads.
- The itinerary survives bar scene transitions and removes a stop only after
  that bar's assigned minigame is completed.
- Completed bars persist as green numbered map markers with a visited counter;
  amber corner badges keep route order readable independently.
- Added mouse, keyboard and gamepad controls plus RU/EN map text.

### Project foundation

- Unity 6 URP project created from the stock template.
- Versioned project guidance and AI memory initialized.

### Playable MVP

- The initial vertical slice used a deterministic connected city with roads,
  16 building lots and 3 bars; the current default has since expanded to 4.
- Road-constrained eight-direction sprite character and free-orbit perspective
  third-person chase camera with obstacle avoidance.
- Localized interaction prompts and a separate generated bar interior.
- Guarded scene transitions and return to the same bar/city layout.
- EditMode, PlayMode and Windows Player verification.

### Visible bar landmarks

- Bar buildings now use warm window bands and gold-framed entrance canopies.
- Added shared procedural pixel mug signs that remain readable from changing
  third-person camera angles.
- Decorative facade pieces are collider-free and do not change city layout,
  navigation or entrance interaction.
