# Release notes

## Unreleased

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
- Added separate procedural ambience for the city and bar, while preserving
  the correct `city_theme`/`bar_theme` split and applying a mild low-pass tone
  to both music players.
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
- Intoxication and served cocktails now persist after every stage. A bad served
  mixture triggers «В никакашку» when the game finishes or closes, while
  reaching 100 intoxication ends the session early.

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
- The itinerary survives bar scene transitions and removes visited stops.
- Added mouse, keyboard and gamepad controls plus RU/EN map text.

### Project foundation

- Unity 6 URP project created from the stock template.
- Versioned project guidance and AI memory initialized.

### Playable MVP

- Deterministic connected city with roads, 16 building lots and 3 bars.
- Road-constrained 13-part sprite character and perspective third-person
  chase camera with obstacle avoidance.
- Localized interaction prompts and a separate generated bar interior.
- Guarded scene transitions and return to the same bar/city layout.
- EditMode, PlayMode and Windows Player verification.

### Visible bar landmarks

- Bar buildings now use warm window bands and gold-framed entrance canopies.
- Added shared procedural pixel mug signs that remain readable from changing
  third-person camera angles.
- Decorative facade pieces are collider-free and do not change city layout,
  navigation or entrance interaction.
