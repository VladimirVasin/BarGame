# Project overview

## Current

- Product name: **Барный Променад** (Bar Promenade).
- Engine: Unity `6000.5.5f1`.
- Rendering: Universal Render Pipeline package `17.5.0` with a PC-targeted
  PS1 composite after URP post-processing.
- Input: Input System `1.19.0`; keyboard, mouse and gamepad are supported
  across movement, interaction and modal interfaces.
- Build scenes: `Assets/Scenes/City.unity` and
  `Assets/Scenes/BarInterior.unity`.
- Runtime assembly: `BarPromenade.Runtime`.
- Test assemblies: `BarPromenade.EditModeTests` and
  `BarPromenade.PlayModeTests`.

## Implemented MVP

A runtime-composed 3D city in which a sprite-based player walks along roads,
approaches interactive bars, enters a separate interior, and returns to the
same place.

The vertical slice contains:

- a finite, seed-reproducible connected city;
- a fixed atmospheric noir night with dense luminous gray-green fog, lifted
  geometry values, cold moonlight and a retuned City-only
  Bloom/ColorAdjustments/Vignette/FilmGrain profile;
- a default `640x360` PS1 world composite with four-tap footprint averaging,
  exact 2x/3x scaling at 720p/1080p, a 35% perceptual-space RGB555 blend
  without a screen-space dither grid, and point upscaling; lower `426x240`
  and `320x180` presets remain available;
- a crisp retro IMGUI layer after the world composite: prompts, HUD and city
  map use a logical `640x360` canvas, while the information-dense cocktail
  interface keeps responsive sizing;
- shared 8-sided cylinder geometry, hard directional shadows and disabled
  camera MSAA for a deliberate low-poly silhouette;
- one player-following `CityFogField`, capped at 36 slowly drifting particles,
  plus depth-tested soft halos around lamps, bar lights and active signals;
- deterministic collider-free street lamps with shadowless spot-light pools
  and slow out-of-phase amber traffic signals generated from the road graph;
- scene-local looping music: `city_theme` loads only from
  `Resources/Audio/CityMusic` in `City`, while `bar_theme` loads only from
  `Resources/Audio/BarMusic` in `BarInterior`; both receive a mild low-pass
  treatment and each player is destroyed by the next Single-mode scene load;
- deterministic generated mono retro SFX at `22050 Hz`, including distinct
  beer-pong throw, bounce, rim and sink cues, with bounded category pools,
  per-effect cooldowns and voice limits, plus separate scene-local procedural
  city and bar ambience;
- a spanning-tree road graph with deterministic loops;
- deterministic collider-free ochre guard rails, batched into two meshes,
  that trace only the exposed boundary of the road union, close dead ends and
  leave a `3.30 m` opening around every generated bar approach;
- 16 building lots by default, including exactly 3 reachable bars; the second
  bar in stable row-major order hosts beer pong while the others host the
  cocktail mixer;
- diegetic bar identification through warm windows, framed entrances and
  shared camera-facing pixel mug signs;
- a 13-part procedural billboard sprite with walking motion;
- camera-relative road-constrained movement;
- a perspective third-person chase camera with mouse/gamepad yaw and
  obstacle-aware distance;
- a full-screen city map with player/bar markers, persistent green completed
  visits, ordered route editing and deterministic shortest paths constrained
  to the generated road graph;
- localized interaction prompts from RU/EN JSON catalogs;
- guarded asynchronous transitions and persistent seed/bar/route/visited
  context for the current city;
- one generated shared bar-interior scene whose furniture and interaction
  station adapt to the active bar activity, plus one exit;
- one explicit `BarMinigameCatalog` whose ordered definitions and factories
  create both normal and debug minigame instances; cocktail mixing and beer
  pong are registered now, and future registrations appear in the debug list;
- an `F9` minigame debug window in both `City` and `BarInterior`; opening it
  closes a conflicting map or minigame before taking the modal lock, while
  launched debug instances neither complete bar visits nor persist
  intoxication, drinks or the `Wasted` effect;
- a same-scene modal cocktail minigame at the counter: exactly three served
  cocktails unless intoxication reaches 100, each built from one of four bases
  and 2–4 unique additions chosen from a deterministic seven-item shelf; its
  accepted final result marks the active bar as visited;
- explicit cocktail compatibility and scoring up to 100 per round/300 total,
  with a 15-point penalty for each incompatible addition;
- a real 4x4 pixel-art ingredient atlas, animated pouring/filling/serving
  feedback, three-stage progress and a final rank;
- a same-scene 2D beer-pong minigame at the second bar with six sprite cups,
  ten aimed throws, deterministic 120 Hz 2.5D ball physics, real table/rim
  bounces, clean/bank scoring and an early-clear bonus;
- a dedicated pixel-art beer-pong backdrop and 4x4 gameplay atlas for the
  ball, shadow, throwing hand, cups, hit reactions and opponent silhouettes;
- session-persistent intoxication, last-alcohol context and consumed-drink
  count; every beer-pong miss consumes a light beer and adds 8 intoxication,
  while the cocktail path retains its deferred timed `Wasted` debuff.

## Deferred

- Infinite streaming world and floating origin.
- Dynamic day/night, weather, rain, puddles and volumetric light shafts.
- Vehicle or skating physics.
- Multiple bespoke bar interiors.
- Mobile renderer parity for the PS1 composite; the current presentation
  feature targets the PC renderer.
- Minimap, in-world GPS trail, route autopilot, and manual map zoom/pan.
- Sobering mechanics, long-term save data, economy, dialogue, quests, combat,
  save slots, and online features.
- Final bespoke art and audio masters, accessibility, localization coverage,
  and platform release work.

South City Rollers/Skaters is a design reference only for procedural-world and sprite-character approaches; its code and assets are not present in this repository.
