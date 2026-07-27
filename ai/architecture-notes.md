# Architecture notes

Decisions marked `Proposed` become accepted only after implementation confirms them.

## Current facts

- **Accepted:** Unity `6000.5.5f1` with URP `17.5.0`.
- **Accepted:** New Input System is enabled.
- **Accepted:** Gameplay is composed at runtime in two explicit build scenes.

## MVP decisions

- **Accepted — Data-first generation:** A pure `CityLayout` is validated before GameObjects are created.
- **Accepted — Stable local randomness:** Road stages and lot coordinates use stable hashes; Unity global random state is not used.
- **Accepted — Finite connected graph:** Kruskal-style spanning tree plus deterministic optional loops.
- **Accepted — Accessible bars:** Every lot gets frontage; bar return points are validated against a frontage road.
- **Accepted — Data-driven walkable mask:** Player motion is constrained to a union of XZ road and entrance-apron rectangles.
- **Accepted — Physical/visual split:** CharacterController stays on the root; 13 SpriteRenderers stay on a collider-free child.
- **Accepted — Explicit scene allow-list:** Only `City` and `BarInterior` install their matching roots.
- **Accepted — Persistent transition context:** Static subsystem-reset session state carries seed and bar ID between Single-mode scene loads.
- **Accepted — Ordered session route:** The current itinerary is a unique
  ordered list of stable `BarId` values. A separate visited-ID set survives
  scene loads for the same city. A terminal bar activity reports completion
  through `IBarMinigame`; the interior root marks that bar visited and removes
  the stop, while entering, cancelling or leaving early does not.
  Both route and visited progress reset when the city seed changes.
- **Accepted — Road-graph route planning:** Each itinerary leg uses
  deterministic weighted Dijkstra over `CityLayout.RoadEdges`; player and bar
  endpoints are projected onto their road segments without NavMesh.
- **Accepted — Modal schematic city map:** A runtime IMGUI overlay fits the
  complete finite city in one view, exposes mouse/keyboard/gamepad editing,
  and temporarily suspends motor, interaction, camera orbit and the HUD.
- **Accepted — Runtime presentation:** City geometry, primitive colors, sprite bitmaps and the shared interior are built at runtime.
- **Accepted — Shared rendering state:** Primitive colors use
  `MaterialPropertyBlock`; emissive and atmosphere effects reuse cached shared
  resources, with no per-instance materials or runtime `Shader.Find`.
- **Accepted — PC PS1 world composite:** The active PC renderer runs one native
  Unity 6 RenderGraph feature at `AfterRenderingPostProcessing` for the final
  Game camera. It footprint-averages the world to `640x360` by default, blends
  35% perceptual-space RGB555 into the original tone without a screen-space
  dither overlay, then point-upscales it, producing exact 2x/3x scaling at
  720p/1080p. Lower `426x240` and `320x180` presets remain available; mobile
  renderer integration is deferred.
- **Accepted — Crisp UI after the composite:** Runtime IMGUI is intentionally
  drawn after the world composite instead of being degraded with the 3D image.
  Prompts, HUD, map and beer pong use a logical `640x360` canvas; the denser
  cocktail view remains responsive while sharing the same palette, stepped
  frames and point-filtered accents.
- **Accepted — Shared low-poly cylinder:** Runtime cylinder requests replace
  the stock visual mesh with one cached flat-shaded 8-sided mesh while
  preserving the primitive collider contract. No per-instance mesh or
  material is created.
- **Accepted — Fixed noir exterior:** `City` applies a lifted blue-green camera,
  dense luminous gray-green exponential-squared fog, hard directional shadows,
  disabled camera MSAA, cold moon/ambient lighting and a dedicated
  Bloom/ColorAdjustments/Vignette/FilmGrain `CityNoirVolumeProfile`;
  `BarInterior` explicitly disables exterior fog and presentation objects.
- **Accepted — Bounded local fog:** One seeded, player-following
  `CityFogField` adds slowly drifting world-space fog with at most 36 particles.
  It reuses the shared atmosphere material and has no collision, trails or
  particle lights.
- **Accepted — Depth-tested light bloom:** Each active street/bar light and
  amber signal lens can own a two-particle `CityLightHalo`. The shared
  `Resources` shader softens depth intersections, so glow diffuses in fog
  without remaining visible through solid geometry.
- **Accepted — Data-first night fixtures:** `CityNightFixturePlanner` derives
  two lamps per road edge and at most six signalized degree-3+ intersections
  deterministically from the city seed and road graph before GameObjects exist.
- **Accepted — Bounded practical lights:** All bulbs and signal lenses reuse
  one HDR URP Unlit material; a player-relative pool of directed street spot
  lights plus bar entrance point lights keeps the complete exterior at no more
  than 12 shadowless realtime lights.
- **Accepted — Safe signal rhythm:** Each selected intersection uses one
  seed-phased controller for two heads and flashes amber below 1 Hz; red and
  green lenses remain dimly visible without realtime lights.
- **Accepted — Scene-local music:** `CityMusicPlayer` loads only `city_theme`
  from `Resources/Audio/CityMusic`, while `BarMusicPlayer` loads only
  `bar_theme` from `Resources/Audio/BarMusic`. Each uses a non-spatial looping
  `AudioSource` and a mild low-pass filter under its matching scene root, so a
  Single-mode scene load destroys the old player and stops its theme
  automatically.
- **Accepted — Generated retro SFX:** `RetroSfx` deterministically synthesizes
  the mono `22050 Hz` UI, footstep, door, cocktail and beer-pong clips in memory.
  `RetroAudioService` persists across scene loads, reuses bounded UI/world/bar
  source pools, and applies per-effect cooldown and concurrent-voice limits.
- **Accepted — Scene-local procedural ambience:** City and bar roots each own a
  quiet deterministic `22050 Hz` ambience loop and tone filter. Single-mode
  transitions destroy the old scene ambience, while the persistent SFX
  service remains available to the next scene.
- **Accepted — Diegetic bar identity:** Bar lots keep their warm body color and
  add amber windows, a framed canopy and one collider-free pixel mug sign.
  Active signs share one generated sprite and use the existing upright
  billboard behavior, so recognition does not depend on color alone.
- **Accepted — Activity-specific same-scene minigame:** Every bar carries a
  stable `BarActivityKind` through the transition. The second row-major bar
  selects beer pong and the others select cocktails. `BarInterior` constructs
  exactly one matching controller and `BarActivityStation`; both implement one
  completion/cancellation contract and share a state-preserving modal lock.
- **Accepted — Three served cocktails:** A complete session contains exactly
  three rounds unless intoxication reaches 100. Each round selects beer, wine,
  vodka or cognac as its base, then accepts 2–4 unique additions before serving.
- **Accepted — Pure cocktail domain:** Compatibility, scoring, deterministic
  shelves and round/session progression live under `Runtime/Cocktails` without
  Unity scene dependencies. Every shelf has seven additions: four compatible
  choices and three traps.
- **Accepted — Score compatible ingredients:** A round scores at most 100 and a
  session at most 300; every incompatible addition subtracts 15 points.
- **Accepted — Atlas-backed IMGUI presentation:** The runtime loads one real
  4x4 pixel-art atlas from `Resources/Cocktails` and draws its cells by UV.
  The same-scene view adds a large filling glass, ingredient travel/tilt,
  pouring, good/bad particles, shaking, three-stage progress and a final rank.
- **Accepted — Deterministic 2.5D beer pong:** Beer-pong state remains plain
  runtime data. A fixed `120 Hz` simulation integrates x/y/z ball motion,
  swept table and cup-mouth contacts, table and rim restitution, settlement,
  timeout and out-of-bounds results. IMGUI projects that physical state onto
  one point-filtered 640x360 table backdrop and a 4x4 gameplay sprite atlas.
- **Accepted — Beer-pong scoring and penalty:** A rack contains six cups and a
  session allows ten throws. A clean sink awards 100, a bank adds 50, and
  clearing early awards 50 for each unused throw. Every miss immediately adds
  8 intoxication and one `LightBeer`; clearing the rack, spending all throws or
  reaching 100 intoxication ends the activity.
- **Accepted — Session-only drinking persistence:** Intoxication, last alcoholic
  drink and total consumed-drink count are committed through
  `GameSessionState` after every cocktail serving and beer-pong miss, survive
  scene loads, and reset when the application subsystem restarts.
- **Accepted — Deferred Wasted presentation:** Serving any bad mixture marks a
  pending 45-second unscaled-time debuff, applied at the final result or when
  the modal closes. Reaching 100 intoxication ends the session early. The
  active effect uses `0.75` movement speed and sprite sway; camera motion
  remains stable.
