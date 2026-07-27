# Project overview

## Current

- Product name: **Барный Променад** (Bar Promenade).
- Engine: Unity `6000.5.5f1`.
- Rendering: Universal Render Pipeline package `17.5.0`.
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
- one player-following `CityFogField`, capped at 36 slowly drifting particles,
  plus depth-tested soft halos around lamps, bar lights and active signals;
- deterministic collider-free street lamps with shadowless spot-light pools
  and slow out-of-phase amber traffic signals generated from the road graph;
- scene-local looping music: `city_theme` loads only from
  `Resources/Audio/CityMusic` in `City`, while `bar_theme` loads only from
  `Resources/Audio/BarMusic` in `BarInterior`; each player is destroyed by the
  next Single-mode scene load;
- a spanning-tree road graph with deterministic loops;
- 16 building lots by default, including exactly 3 reachable bars;
- diegetic bar identification through warm windows, framed entrances and
  shared camera-facing pixel mug signs;
- a 13-part procedural billboard sprite with walking motion;
- camera-relative road-constrained movement;
- a perspective third-person chase camera with mouse/gamepad yaw and
  obstacle-aware distance;
- a full-screen city map with player/bar markers, ordered route editing and
  deterministic shortest paths constrained to the generated road graph;
- localized interaction prompts from RU/EN JSON catalogs;
- guarded asynchronous transitions and persistent seed/bar/route context;
- one generated shared bar interior with an exit;
- a same-scene modal cocktail minigame at the counter: exactly three served
  cocktails unless intoxication reaches 100, each built from one of four bases
  and 2–4 unique additions chosen from a deterministic seven-item shelf;
- explicit cocktail compatibility and scoring up to 100 per round/300 total,
  with a 15-point penalty for each incompatible addition;
- a real 4x4 pixel-art ingredient atlas, animated pouring/filling/serving
  feedback, three-stage progress and a final rank;
- session-persistent intoxication, last-alcohol context and served-cocktail
  count, plus a deferred timed `Wasted` movement/presentation debuff.

## Deferred

- Infinite streaming world and floating origin.
- Dynamic day/night, weather, rain, puddles and volumetric light shafts.
- Vehicle or skating physics.
- Multiple bespoke bar interiors.
- Minimap, in-world GPS trail, route autopilot, and manual map zoom/pan.
- Sobering mechanics, long-term save data, economy, dialogue, quests, combat,
  save slots, and online features.
- Final art, audio, accessibility, localization coverage, and platform release work.

South City Rollers/Skaters is a design reference only for procedural-world and sprite-character approaches; its code and assets are not present in this repository.
