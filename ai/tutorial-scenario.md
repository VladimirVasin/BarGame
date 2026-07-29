# MVP tutorial scenario

Status: **Implemented and covered by PlayMode smoke tests.**

This is the first manual acceptance path for the vertical slice.

1. Launch the build into the generated city and confirm that the player starts
   on a street beside their distinct home and near one generated bar.
2. Observe a large connected city with Old Town, Residential, Industrial and
   Nightlife districts surrounding a central park.
3. Open the map and confirm that district labels, green park land, sand-colored
   park paths, four widely separated bars and the labeled house icon are all
   visible.
4. Move with `WASD`/arrows along streets, enter any park gate and cross the
   lawn on foot to another side without leaving walkable space.
5. Confirm that every bar approach, the home approach and each park gate has a
   clear break in the low ochre rails.
6. Enter the home with `E`/`Enter`, inspect the furnished room, then use its
   exit and confirm the hero returns to the same exterior approach.
7. Approach one bar, press `E`/`Enter` and confirm its interior loads once.
8. Walk through the populated bar interior and interact with the exit.
9. Return to the same bar entrance in a city identical to the one left.
10. Restart with the same seed and confirm the districts, park, paths, home and
    bar placement are reproduced.

The automated checks verify the same seed, road-edge/path sequence, districts,
park, bar IDs, deterministic home, nearby fresh spawn and both return paths.

For a support capture during this path, press `F8` after the state to diagnose
is visible. The snapshot is flushed into `debug.log`; `Shift+F8` opens its
directory.
