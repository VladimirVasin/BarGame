# MVP tutorial scenario

Status: **Implemented and covered by PlayMode smoke tests.**

This is the first manual acceptance path for the vertical slice.

1. Launch the build into the generated city.
2. Observe a large connected city with Old Town, Residential, Industrial and
   Nightlife districts surrounding a central park.
3. Open the map and confirm that district labels, green park land, sand-colored
   park paths and four widely separated bars are all visible.
4. Move with `WASD`/arrows along streets, enter any park gate and cross the
   lawn on foot to another side without leaving walkable space.
5. Confirm that every bar approach and park gate has a clear break in the low
   ochre rails, then approach one bar and see an interaction prompt.
6. Press `E`/`Enter`; the bar interior loads once.
7. Walk through the populated interior and interact with the exit.
8. Return to the same bar entrance in a city identical to the one left.
9. Restart with the same seed and confirm the districts, park, paths and bar
   placement are reproduced.

The automated checks verify the same seed, road-edge/path sequence, districts,
park, bar IDs and return position after step 8.

For a support capture during this path, press `F8` after the state to diagnose
is visible. The snapshot is flushed into `debug.log`; `Shift+F8` opens its
directory.
