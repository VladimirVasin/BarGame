# MVP tutorial scenario

Status: **Implemented and covered by PlayMode smoke tests.**

This is the first manual acceptance path for the vertical slice.

1. Launch the build into the generated city.
2. Observe a compact connected road network whose exposed edges and dead ends
   are marked by low ochre guard rails.
3. Move with `WASD`/arrows while remaining on walkable road space.
4. Confirm that every bar approach has a clear break in the rails, then enter
   one of those openings and see an interaction prompt.
5. Press `E`/`Enter`; the bar interior loads once.
6. Walk inside the minimal interior and interact with the exit.
7. Return to the same bar entrance in a city identical to the one left.
8. Restart with the same seed and confirm the layout is reproduced.

The automated round-trip test verifies the same seed, road-edge sequence, bar ID
and return position after step 7.
