# Release notes

## Unreleased

### Cinematic player presentation

- Moved the chase camera closer with separate `4.6 m / 53°` exterior and
  `3.3 m / 57°` interior profiles.
- Added bounded target damping, teleport snapping and subtle deterministic
  idle/walk camera motion while preserving immediate obstacle avoidance,
  stable yaw/FOV and camera-independent player heading.
- Camera motion now fades out and restores with the shared modal lock used by
  the map, minigames and F9 launcher.
- Added a procedural living idle: sub-pixel breathing, slow weight transfer
  and a rare asymmetric arm fidget blend with walking and are strongly
  suppressed during `Wasted`.
- Added deterministic half/closed blinks for all five visible-face directions
  through the existing body renderer. Rear views remain neutral, the puppet
  still uses exactly nine renderers and no sprite is mirrored.

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
  registered minigame; cocktail mixing and beer pong are available now.
- Normal interiors and the debug list use the same explicit catalog, so a
  future game appears after its definition and factory are registered.
- Opening the window closes a conflicting map or minigame and preserves the
  modal input/HUD state when the window or launched game closes.
- Debug runs are isolated: they do not mark a bar visited or save
  intoxication, consumed drinks or the `Wasted` effect.

### Beer-pong minigame

- The second bar on the stable city map now opens beer pong, while the first
  and third bars keep the cocktail mixer.
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
- Accepting the final minigame result now marks that bar as visited; entering
  the interior or leaving an unfinished game does not.
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
- The itinerary survives bar scene transitions and removes a stop only after
  that bar's assigned minigame is completed.
- Completed bars persist as green numbered map markers with a visited counter;
  amber corner badges keep route order readable independently.
- Added mouse, keyboard and gamepad controls plus RU/EN map text.

### Project foundation

- Unity 6 URP project created from the stock template.
- Versioned project guidance and AI memory initialized.

### Playable MVP

- Deterministic connected city with roads, 16 building lots and 3 bars.
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
