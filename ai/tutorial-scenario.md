# MVP tutorial scenario

Status: **Implemented with targeted startup and scene-flow coverage.**

This is the first manual acceptance path for the vertical slice.

1. Launch the build and confirm that after the black loading boundary the
   first rendered Home frame is a close clock shot showing `05:59`.
2. For five seconds confirm the whole red display flickers off very briefly
   at long intervals while the clock stays silent, no buttons are drawn and
   keyboard, mouse and gamepad cannot choose Wake Up or Quit.
3. Confirm the localized PS1-style `ПРОСНУТЬСЯ / WAKE UP` and `ВЫЙТИ / QUIT`
   buttons appear without the camera leaving the clock or the alarm starting.
   The display must still show and periodically flicker `05:59` for as long as
   no choice is made.
4. Choose Wake Up with `E`/`Enter`. Confirm that this press alone changes the
   display to solid `06:00`, hides the buttons and starts the alarm with visible
   rattle. For three seconds the camera must remain on the clock and the hero
   must remain in the sleeping loop. Confirm the alarm then stops and only
   then does the camera glide smoothly to the sleeper and ease into the main
   Home shot without a cut. The startup wake takes about six seconds—three
   times the ordinary bed wake—before normal movement, interaction and HUD
   return without another Home load.
5. Confirm the clock and nightstand remain beside the bed as silent room
   dressing.
6. Use the apartment exit, descend through the stairwell and use its street
   door. Confirm the player reaches the generated city beside their distinct
   home and near its neighboring bar.
7. Observe a large connected city with Old Town, Residential, Industrial and
   Nightlife districts surrounding a central park.
8. Open the map and confirm that district labels, green park land, sand-colored
   park paths, four widely separated bars and the labeled house icon are all
   visible.
9. Move with `WASD`/arrows along streets, enter any park gate and cross the
   lawn on foot to another side without leaving walkable space.
10. Confirm that every bar approach, the home approach and each park gate has a
   clear break in the low ochre rails.
11. Re-enter the home with `E`/`Enter` and confirm it opens normally without
    replaying the startup menu or ringing the clock; leave and return to the
    same exterior approach.
12. Approach one bar, press `E`/`Enter`, walk through its populated interior
    and use the exit.
13. Confirm the same city and bar entrance are restored. Restart the build and
    confirm both the one-shot waking opening and the seeded districts, park,
    paths, home and bar placement are reproduced.

Automated coverage includes build-scene order, complete new-session reset,
one-shot Home arrival consumption, opening phases, alarm synthesis/routing and
cleanup, waking restoration, normal direct Home loads, the same seed,
road-edge/path sequence, districts, park, bar IDs, deterministic home, nearby
fresh spawn and both return paths.

For a support capture during this path, press `F8` after reaching the City and
making the state to diagnose visible. The snapshot is flushed into `debug.log`;
`Shift+F8` opens its directory.
