using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Dresses the terminal into the transfer yard it always was: the place
    /// where the road stops and the cable starts.
    ///
    /// Every number is authored in the plateau's own frame through
    /// <see cref="MountainRoadTerminalPlanner.LocalToWorld"/>, and every
    /// height comes off one of two datums on the plan — the yard, which is
    /// the asphalt slab the car parks on, and the terrace, which is one
    /// retaining wall above it. Nothing here samples terrain: inside the
    /// polygon the ground is that slab, flat, and the soil is a bed under
    /// it.
    ///
    /// The composition follows the ground rather than an idea about it.
    /// The mountain rises to the east — `+5.6 m` within `45 m` — so the
    /// cut face is on the east rim, behind the cableway, and the cable
    /// climbs over it. It falls to the west and the back is where the only
    /// free band is, so the terrace and its parapet close the back rim and
    /// the brink opens through them.
    /// </summary>
    public static class MountainRoadTerminalSitePlanner
    {
        /// <summary>
        /// One retaining wall. Low enough to read as a kerb from the
        /// arrival and to be climbed in three risers, high enough that the
        /// parapet on top of it finishes above a standing eye.
        /// </summary>
        public const float TerraceRise = 0.66f;

        public const float StepRise = TerraceRise / 3f;
        public const float StepRun = 0.35f;
        public const float RetainingWallForward = 12.6f;
        public const float RetainingWallThickness = 0.45f;
        public const float TerraceRimForward = 17.85f;

        /// <summary>
        /// Far enough in that its OUTER face still clears the rim by more
        /// than the mask's own clamp. Push it any closer and the clamp
        /// stops the hero first, which puts an invisible wall in front of
        /// a visible one.
        /// </summary>
        public const float ParapetForward = 17.3f;

        public const float ParapetThickness = 0.7f;
        public const float ParapetHeight = 1.02f;
        public const float TerraceLeftRight = -7f;
        public const float TerraceRightRight = 13f;

        /// <summary>
        /// The stool the hero is offered, read off the cafe's own row so
        /// the offer and the timber cannot disagree.
        ///
        /// Among the places the cafe leaves empty, this is the one whose
        /// dock fits. The seat that finishes the main row at the chamfered
        /// corner
        /// would take a plank dock `0.21 m` from that corner's glass —
        /// inside the player's own radius. So it is the middle gap,
        /// between the lone patron and the couple. Its dock stays in the
        /// open aisle, but its facing is independent: the hero approaches
        /// the loose stool from behind and settles looking at the counter,
        /// exactly like the three patrons already seated beside him.
        /// </summary>
        public static float CounterSeatCafeRight =>
            MountainRoadCafeWorldBuilder.StoolRightOffsets[
                MountainRoadCafeWorldBuilder.EmptyStoolIndex];

        private const float BenchRight = 3f;
        private const float BenchForward = 15.3f;
        private const float BenchSeatWidth = 1.9f;
        private const float BenchSeatDepth = 0.48f;
        private const float BenchSeatRise = 0.45f;

        public static MountainRoadTerminalSitePlan Create(
            MountainRoadPlateauDescriptor plateau,
            MountainRoadCafePlan cafe)
        {
            if (plateau == null)
            {
                throw new ArgumentNullException(nameof(plateau));
            }

            if (cafe == null)
            {
                throw new ArgumentNullException(nameof(cafe));
            }

            // The pad IS the walking surface: the plateau slab belongs to
            // the road mesh at the plateau centre's own height, and the
            // soil sits a bed's depth under it.
            float yardTop = plateau.Center.y;
            float terraceTop = yardTop + TerraceRise;
            var parts = new List<MountainRoadSitePartDescriptor>(128);
            var cloth = new List<MountainRoadSiteClothDescriptor>(2);
            var chains = new List<MountainRoadSiteChainDescriptor>(3);

            AppendPloughing(plateau, yardTop, parts);
            AppendRoadEnd(plateau, yardTop, parts);
            AppendCafeThreshold(plateau, cafe, yardTop, parts);
            AppendServiceYard(plateau, yardTop, parts, cloth, chains);
            AppendTerrace(plateau, yardTop, terraceTop, parts);
            AppendBrink(plateau, yardTop, terraceTop, parts, cloth, chains);
            AppendPrivy(plateau, yardTop, parts);
            AppendRockCut(plateau, yardTop, parts);
            AppendWestRail(plateau, yardTop, parts);

            return new MountainRoadTerminalSitePlan(
                parts,
                cloth,
                chains,
                yardTop,
                terraceTop,
                CreateBrinkSeat(plateau, terraceTop),
                CreateCounterSeat(plateau, cafe, yardTop),
                CreateYardLamp(plateau, yardTop));
        }

        /// <summary>
        /// The only place on the summit where snow is piled rather than
        /// fallen, which is the whole reason the rest of it is clear.
        /// </summary>
        private static void AppendPloughing(
            MountainRoadPlateauDescriptor plateau,
            float yardTop,
            ICollection<MountainRoadSitePartDescriptor> parts)
        {
            // Three bodies, and they stop where the cafe's business
            // starts. A fourth used to carry the heap on round to the
            // west rim, which put its corner inside the doorway - a
            // plough does not wrap a yard, it pushes snow into a heap and
            // leaves.
            //
            // The yaw lies each body ALONG the rim it was pushed against:
            // the south-west edge runs at `15` degrees in this frame and
            // turns to `42` at the corner. The last two used to be
            // mirrored off it, which is what put a corner outside the
            // polygon and another in the door.
            float[,] bank =
            {
                { -10.8f, -6.2f, 4.6f, 1.55f, 15f },
                { -13.0f, -4.6f, 4.4f, 1.75f, 18f },
                { -15.4f, -3.2f, 3.4f, 1.6f, 34f }
            };
            for (int index = 0; index < bank.GetLength(0); index++)
            {
                float height = bank[index, 3];
                parts.Add(Part(
                    plateau,
                    $"site-plough-bank-{index:00}",
                    MountainRoadSiteGroup.Ploughing,
                    MountainRoadSiteStyle.DirtySnow,
                    bank[index, 0],
                    yardTop + height * 0.5f,
                    bank[index, 1],
                    new Vector3(bank[index, 2], height, 2.5f),
                    bank[index, 4],
                    true));
            }

            parts.Add(Part(
                plateau,
                "site-grit-bin",
                MountainRoadSiteGroup.Ploughing,
                MountainRoadSiteStyle.PaintedSteel,
                -9.7f,
                yardTop + 0.45f,
                -4.3f,
                new Vector3(1.45f, 0.9f, 0.95f),
                15f,
                true));
            parts.Add(Part(
                plateau,
                "site-grit-bin-lid",
                MountainRoadSiteGroup.Ploughing,
                MountainRoadSiteStyle.RustedIron,
                -9.7f,
                yardTop + 0.93f,
                -4.3f,
                new Vector3(1.55f, 0.06f, 1.05f),
                15f,
                false));
            parts.Add(Part(
                plateau,
                "site-spare-snow-poles",
                MountainRoadSiteGroup.Ploughing,
                MountainRoadSiteStyle.PaleEnamel,
                -11.2f,
                yardTop + 0.18f,
                -3.4f,
                new Vector3(0.42f, 0.36f, 3.1f),
                24f,
                false));
        }

        /// <summary>
        /// The last of the road furniture, and the first thing the
        /// headlights find. Nothing here is a warning: the barrier has been
        /// open long enough to seize that way.
        /// </summary>
        private static void AppendRoadEnd(
            MountainRoadPlateauDescriptor plateau,
            float yardTop,
            ICollection<MountainRoadSitePartDescriptor> parts)
        {
            parts.Add(Part(
                plateau,
                "site-road-end-post",
                MountainRoadSiteGroup.RoadEnd,
                MountainRoadSiteStyle.RustedIron,
                11.4f,
                yardTop + 1f,
                -4.6f,
                new Vector3(0.16f, 2f, 0.16f),
                0f,
                true));
            parts.Add(Part(
                plateau,
                "site-road-end-board",
                MountainRoadSiteGroup.RoadEnd,
                MountainRoadSiteStyle.FadedSign,
                11.4f,
                yardTop + 1.72f,
                -4.66f,
                new Vector3(2.4f, 0.56f, 0.08f),
                0f,
                false));
            parts.Add(Part(
                plateau,
                "site-barrier-post",
                MountainRoadSiteGroup.RoadEnd,
                MountainRoadSiteStyle.RustedIron,
                9.2f,
                yardTop + 0.6f,
                -6.2f,
                new Vector3(0.24f, 1.2f, 0.24f),
                0f,
                true));
            parts.Add(Part(
                plateau,
                "site-barrier-arm",
                MountainRoadSiteGroup.RoadEnd,
                MountainRoadSiteStyle.PaintedSteel,
                10.6f,
                yardTop + 1.02f,
                -4.9f,
                new Vector3(0.13f, 0.13f, 3.4f),
                24f,
                false));
            parts.Add(Part(
                plateau,
                "site-gravel-pallet",
                MountainRoadSiteGroup.RoadEnd,
                MountainRoadSiteStyle.Timber,
                13.9f,
                yardTop + 0.07f,
                -2.8f,
                new Vector3(1.25f, 0.14f, 1.05f),
                -12f,
                false));
            parts.Add(Part(
                plateau,
                "site-gravel-sacks",
                MountainRoadSiteGroup.RoadEnd,
                MountainRoadSiteStyle.Concrete,
                13.9f,
                yardTop + 0.36f,
                -2.8f,
                new Vector3(1.05f, 0.44f, 0.85f),
                -8f,
                true));
            parts.Add(Part(
                plateau,
                "site-spare-guardrail",
                MountainRoadSiteGroup.RoadEnd,
                MountainRoadSiteStyle.RustedIron,
                14.6f,
                yardTop + 0.09f,
                -3f,
                new Vector3(0.2f, 0.18f, 4f),
                8f,
                false));
            parts.Add(Part(
                plateau,
                "site-last-snow-pole",
                MountainRoadSiteGroup.RoadEnd,
                MountainRoadSiteStyle.PaleEnamel,
                17.4f,
                yardTop + 1.5f,
                1.2f,
                new Vector3(0.14f, 3f, 0.14f),
                0f,
                false));
        }

        /// <summary>
        /// The strip the cafe door opens onto. Its furniture is put away
        /// for a winter that does not end.
        /// </summary>
        private static void AppendCafeThreshold(
            MountainRoadPlateauDescriptor plateau,
            MountainRoadCafePlan cafe,
            float yardTop,
            ICollection<MountainRoadSitePartDescriptor> parts)
        {
            Vector3 door = cafe.DoorCenter;
            float doorRight = Vector3.Dot(
                door - plateau.Center,
                plateau.Right);
            parts.Add(Part(
                plateau,
                "site-door-grate",
                MountainRoadSiteGroup.CafeThreshold,
                MountainRoadSiteStyle.RustedIron,
                doorRight,
                yardTop + 0.045f,
                1.72f,
                new Vector3(1.9f, 0.09f, 0.95f),
                0f,
                false));
            // Everything solid stands to one side or the other. The door
            // is `1.6 m` wide at local right `-16.2`, and the validator
            // holds a `3.2 x 3 m` box off it: what used to be here put a
            // bin `0.9 m` from one jamb, an ash post `0.5 m` from the
            // other and a snow bank across the middle, so the way in was
            // a slot rather than a door.
            parts.Add(Part(
                plateau,
                "site-stacked-tables",
                MountainRoadSiteGroup.CafeThreshold,
                MountainRoadSiteStyle.Timber,
                -12.2f,
                yardTop + 0.42f,
                1.25f,
                new Vector3(1.5f, 0.84f, 0.9f),
                8f,
                true));
            parts.Add(Part(
                plateau,
                "site-stacked-chairs",
                MountainRoadSiteGroup.CafeThreshold,
                MountainRoadSiteStyle.PaintedSteel,
                -10.6f,
                yardTop + 0.52f,
                1.3f,
                new Vector3(0.72f, 1.04f, 0.78f),
                -6f,
                true));

            // Off the threshold entirely, against the cafe's blind west
            // flank where the rubbish actually goes.
            parts.Add(Part(
                plateau,
                "site-threshold-bin",
                MountainRoadSiteGroup.CafeThreshold,
                MountainRoadSiteStyle.PaintedSteel,
                -19f,
                yardTop + 0.46f,
                3.4f,
                new Vector3(0.56f, 0.92f, 0.56f),
                0f,
                true));
            parts.Add(Part(
                plateau,
                "site-ash-post",
                MountainRoadSiteGroup.CafeThreshold,
                MountainRoadSiteStyle.RustedIron,
                -13.5f,
                yardTop + 0.57f,
                1.5f,
                new Vector3(0.18f, 1.14f, 0.18f),
                0f,
                true));
            parts.Add(Part(
                plateau,
                "site-cafe-sign-bracket",
                MountainRoadSiteGroup.CafeThreshold,
                MountainRoadSiteStyle.RustedIron,
                -14.5f,
                yardTop + 3.42f,
                2.06f,
                new Vector3(0.08f, 0.5f, 0.24f),
                0f,
                false));
            parts.Add(Part(
                plateau,
                "site-cafe-sign-board",
                MountainRoadSiteGroup.CafeThreshold,
                MountainRoadSiteStyle.FadedSign,
                -14.5f,
                yardTop + 3.42f,
                1.9f,
                new Vector3(2f, 0.54f, 0.1f),
                0f,
                false));
        }

        /// <summary>
        /// Freight, and one suitcase nobody came back for. The dock stands
        /// north of the cable station because that is the only clear ground
        /// beside it, and its kerb is the height of a cabin floor.
        /// </summary>
        private static void AppendServiceYard(
            MountainRoadPlateauDescriptor plateau,
            float yardTop,
            ICollection<MountainRoadSitePartDescriptor> parts,
            ICollection<MountainRoadSiteClothDescriptor> cloth,
            ICollection<MountainRoadSiteChainDescriptor> chains)
        {
            parts.Add(Part(
                plateau,
                "site-loading-kerb",
                MountainRoadSiteGroup.ServiceYard,
                MountainRoadSiteStyle.Concrete,
                9.5f,
                yardTop + 0.21f,
                11.3f,
                new Vector3(5f, 0.42f, 0.9f),
                0f,
                true));
            parts.Add(Part(
                plateau,
                "site-hand-cart-body",
                MountainRoadSiteGroup.ServiceYard,
                MountainRoadSiteStyle.RustedIron,
                6f,
                yardTop + 0.46f,
                11f,
                new Vector3(0.92f, 0.16f, 1.4f),
                -18f,
                true));
            parts.Add(Part(
                plateau,
                "site-hand-cart-handle",
                MountainRoadSiteGroup.ServiceYard,
                MountainRoadSiteStyle.RustedIron,
                6.28f,
                yardTop + 0.72f,
                10.35f,
                new Vector3(0.9f, 0.9f, 0.08f),
                -18f,
                false));
            parts.Add(Part(
                plateau,
                "site-crate-stack",
                MountainRoadSiteGroup.ServiceYard,
                MountainRoadSiteStyle.Timber,
                3.6f,
                yardTop + 0.55f,
                11.2f,
                new Vector3(1.35f, 1.1f, 1.05f),
                6f,
                true));
            parts.Add(Part(
                plateau,
                "site-crate-single",
                MountainRoadSiteGroup.ServiceYard,
                MountainRoadSiteStyle.Timber,
                3.35f,
                yardTop + 1.4f,
                11.05f,
                new Vector3(0.92f, 0.6f, 0.8f),
                -14f,
                false));
            parts.Add(Part(
                plateau,
                "site-abandoned-suitcase",
                MountainRoadSiteGroup.ServiceYard,
                MountainRoadSiteStyle.DeadTimber,
                7.6f,
                yardTop + 0.11f,
                10.2f,
                new Vector3(0.62f, 0.22f, 0.44f),
                24f,
                false));
            parts.Add(Part(
                plateau,
                "site-service-pole",
                MountainRoadSiteGroup.ServiceYard,
                MountainRoadSiteStyle.Timber,
                -4.4f,
                yardTop + 3.2f,
                11.4f,
                new Vector3(0.26f, 6.4f, 0.26f),
                0f,
                true));
            parts.Add(Part(
                plateau,
                "site-yard-lamp-arm",
                MountainRoadSiteGroup.ServiceYard,
                MountainRoadSiteStyle.RustedIron,
                -3.75f,
                yardTop + 5.72f,
                11.4f,
                new Vector3(1.3f, 0.09f, 0.09f),
                0f,
                false));
            parts.Add(Part(
                plateau,
                "site-yard-lamp-shade",
                MountainRoadSiteGroup.ServiceYard,
                MountainRoadSiteStyle.PaintedSteel,
                -3.15f,
                yardTop + 5.62f,
                11.4f,
                new Vector3(0.52f, 0.2f, 0.52f),
                0f,
                false));

            cloth.Add(new MountainRoadSiteClothDescriptor(
                "site-load-tarp",
                Point(plateau, 3.6f, yardTop + 1.12f, 11.2f),
                6f,
                1.6f,
                1.15f,
                true));
            chains.Add(new MountainRoadSiteChainDescriptor(
                "site-pole-guy-west",
                Point(plateau, -4.4f, yardTop + 6.1f, 11.4f),
                Point(plateau, -6.6f, yardTop + 0.1f, 10.1f),
                0.06f,
                0.05f));
            chains.Add(new MountainRoadSiteChainDescriptor(
                "site-pole-guy-east",
                Point(plateau, -4.4f, yardTop + 6.1f, 11.4f),
                Point(plateau, -2.2f, yardTop + 0.1f, 10.1f),
                0.06f,
                0.05f));
        }

        /// <summary>
        /// The retaining wall, the two flights through it and the deck on
        /// top. The risers are `0.22 m` against the player's `0.28 m` step
        /// offset, so the flights are walked rather than ramped, and the
        /// wall carries a collider precisely so the flights are the only
        /// way up.
        /// </summary>
        private static void AppendTerrace(
            MountainRoadPlateauDescriptor plateau,
            float yardTop,
            float terraceTop,
            ICollection<MountainRoadSitePartDescriptor> parts)
        {
            float wallBack = RetainingWallForward +
                             RetainingWallThickness * 0.5f;
            float[,] segments =
            {
                { TerraceLeftRight, -5.85f },
                { -4.15f, 0.15f },
                { 1.85f, TerraceRightRight }
            };
            for (int index = 0; index < segments.GetLength(0); index++)
            {
                float left = segments[index, 0];
                float rightEdge = segments[index, 1];
                parts.Add(Part(
                    plateau,
                    $"site-retaining-wall-{index:00}",
                    MountainRoadSiteGroup.Terrace,
                    MountainRoadSiteStyle.Concrete,
                    (left + rightEdge) * 0.5f,
                    yardTop + TerraceRise * 0.5f,
                    RetainingWallForward,
                    new Vector3(
                        rightEdge - left,
                        TerraceRise,
                        RetainingWallThickness),
                    0f,
                    true));
            }

            AppendFlight(plateau, yardTop, wallBack, -5f, 0, parts);
            AppendFlight(plateau, yardTop, wallBack, 1f, 1, parts);

            float deckDepth = TerraceRimForward - wallBack;
            parts.Add(Part(
                plateau,
                "site-terrace-deck",
                MountainRoadSiteGroup.Terrace,
                MountainRoadSiteStyle.Concrete,
                (TerraceLeftRight + TerraceRightRight) * 0.5f,
                yardTop + TerraceRise * 0.5f,
                wallBack + deckDepth * 0.5f,
                new Vector3(
                    TerraceRightRight - TerraceLeftRight,
                    TerraceRise,
                    deckDepth),
                0f,
                true));
        }

        private static void AppendFlight(
            MountainRoadPlateauDescriptor plateau,
            float yardTop,
            float wallBack,
            float centreRight,
            int flightIndex,
            ICollection<MountainRoadSitePartDescriptor> parts)
        {
            for (int step = 0; step < 3; step++)
            {
                float top = StepRise * (step + 1);
                float back = wallBack - StepRun * (2 - step);
                parts.Add(Part(
                    plateau,
                    $"site-terrace-step-{flightIndex}-{step}",
                    MountainRoadSiteGroup.Terrace,
                    MountainRoadSiteStyle.Concrete,
                    centreRight,
                    yardTop + top * 0.5f,
                    back - StepRun * 0.5f,
                    new Vector3(1.7f, top, StepRun),
                    0f,
                    true));
            }
        }

        /// <summary>
        /// The parapet, its gap, and the four things worth standing at the
        /// edge for. The wall stands `0.35 m` inside the rim, which is what
        /// finally makes the plateau polygon's own clamp invisible: the
        /// hero is stopped by masonry he can see instead of by a rule.
        /// </summary>
        private static void AppendBrink(
            MountainRoadPlateauDescriptor plateau,
            float yardTop,
            float terraceTop,
            ICollection<MountainRoadSitePartDescriptor> parts,
            ICollection<MountainRoadSiteClothDescriptor> cloth,
            ICollection<MountainRoadSiteChainDescriptor> chains)
        {
            float[,] runs =
            {
                { TerraceLeftRight, 8.5f },
                { 10.5f, TerraceRightRight }
            };
            for (int index = 0; index < runs.GetLength(0); index++)
            {
                float left = runs[index, 0];
                float rightEdge = runs[index, 1];
                float width = rightEdge - left;
                float centre = (left + rightEdge) * 0.5f;
                parts.Add(Part(
                    plateau,
                    $"site-parapet-wall-{index:00}",
                    MountainRoadSiteGroup.Brink,
                    MountainRoadSiteStyle.DressedStone,
                    centre,
                    terraceTop + ParapetHeight * 0.5f,
                    ParapetForward,
                    new Vector3(width, ParapetHeight, ParapetThickness),
                    0f,
                    true));
                parts.Add(Part(
                    plateau,
                    $"site-parapet-coping-{index:00}",
                    MountainRoadSiteGroup.Brink,
                    MountainRoadSiteStyle.DressedStone,
                    centre,
                    terraceTop + ParapetHeight + 0.06f,
                    ParapetForward,
                    new Vector3(
                        width,
                        0.12f,
                        ParapetThickness + 0.14f),
                    0f,
                    false));
            }

            for (int index = 0; index < 6; index++)
            {
                float postRight = Mathf.Lerp(
                    TerraceLeftRight + 0.8f,
                    7.9f,
                    index / 5f);
                parts.Add(Part(
                    plateau,
                    $"site-parapet-post-{index:00}",
                    MountainRoadSiteGroup.Brink,
                    MountainRoadSiteStyle.RustedIron,
                    postRight,
                    terraceTop + ParapetHeight + 0.28f,
                    ParapetForward,
                    new Vector3(0.11f, 0.44f, 0.11f),
                    0f,
                    false));
            }

            // The gap, and the chain that stands in for the missing run.
            for (int index = 0; index < 2; index++)
            {
                parts.Add(Part(
                    plateau,
                    $"site-parapet-gap-post-{index:00}",
                    MountainRoadSiteGroup.Brink,
                    MountainRoadSiteStyle.RustedIron,
                    index == 0 ? 8.75f : 10.25f,
                    terraceTop + 0.55f,
                    ParapetForward,
                    new Vector3(0.14f, 1.1f, 0.14f),
                    0f,
                    true));
            }

            chains.Add(new MountainRoadSiteChainDescriptor(
                "site-parapet-gap-chain",
                Point(plateau, 8.75f, terraceTop + 0.92f, ParapetForward),
                Point(plateau, 10.25f, terraceTop + 0.92f, ParapetForward),
                0.16f,
                0.045f));

            // The bench, and the one metre thirty in front of it the sit
            // interaction needs to dock in.
            for (int index = 0; index < 2; index++)
            {
                parts.Add(Part(
                    plateau,
                    $"site-bench-leg-{index:00}",
                    MountainRoadSiteGroup.Brink,
                    MountainRoadSiteStyle.Concrete,
                    BenchRight + (index == 0 ? -0.85f : 0.85f),
                    terraceTop + BenchSeatRise * 0.5f,
                    BenchForward,
                    new Vector3(0.18f, BenchSeatRise, 0.44f),
                    0f,
                    true));
            }

            for (int index = 0; index < 3; index++)
            {
                parts.Add(Part(
                    plateau,
                    $"site-bench-plank-{index:00}",
                    MountainRoadSiteGroup.Brink,
                    index == 1
                        ? MountainRoadSiteStyle.Timber
                        : MountainRoadSiteStyle.DeadTimber,
                    BenchRight,
                    terraceTop + BenchSeatRise - 0.028f,
                    BenchForward + (index - 1) * 0.16f,
                    new Vector3(BenchSeatWidth, 0.055f, 0.14f),
                    0f,
                    true));
            }

            parts.Add(Part(
                plateau,
                "site-memorial-post",
                MountainRoadSiteGroup.Brink,
                MountainRoadSiteStyle.RustedIron,
                -3.2f,
                terraceTop + 0.58f,
                16.4f,
                new Vector3(0.12f, 1.16f, 0.12f),
                6f,
                true));
            parts.Add(Part(
                plateau,
                "site-memorial-plate",
                MountainRoadSiteGroup.Brink,
                MountainRoadSiteStyle.FadedSign,
                -3.2f,
                terraceTop + 1.02f,
                16.34f,
                new Vector3(0.52f, 0.36f, 0.05f),
                6f,
                false));
            parts.Add(Part(
                plateau,
                "site-survey-pillar",
                MountainRoadSiteGroup.Brink,
                MountainRoadSiteStyle.Concrete,
                11.6f,
                terraceTop + 0.52f,
                16.2f,
                new Vector3(0.34f, 1.05f, 0.34f),
                0f,
                true));
            parts.Add(Part(
                plateau,
                "site-survey-bolt",
                MountainRoadSiteGroup.Brink,
                MountainRoadSiteStyle.RustedIron,
                11.6f,
                terraceTop + 1.07f,
                16.2f,
                new Vector3(0.08f, 0.06f, 0.08f),
                0f,
                false));
            parts.Add(Part(
                plateau,
                "site-windsock-mast",
                MountainRoadSiteGroup.Brink,
                MountainRoadSiteStyle.PaintedSteel,
                -6f,
                terraceTop + 3.6f,
                16.6f,
                new Vector3(0.18f, 7.2f, 0.18f),
                0f,
                true));
            parts.Add(Part(
                plateau,
                "site-windsock-ring",
                MountainRoadSiteGroup.Brink,
                MountainRoadSiteStyle.RustedIron,
                -5.7f,
                terraceTop + 6.9f,
                16.6f,
                new Vector3(0.62f, 0.08f, 0.62f),
                0f,
                false));

            cloth.Add(new MountainRoadSiteClothDescriptor(
                "site-windsock",
                Point(plateau, -5.7f, terraceTop + 6.86f, 16.6f),
                90f,
                0.6f,
                1.9f,
                false));
        }

        /// <summary>
        /// One plank privy, single seat, in the north-east pocket between
        /// the cable station and the cut - which is where a yard puts one:
        /// downwind of the working side, out of sight of the arrival, and
        /// nowhere near the cafe door.
        ///
        /// The pan inside it is the apartment bathroom's, and the joke is
        /// the true kind: somebody carried a proper porcelain pan up six
        /// hundred metres of switchback and bolted it into a plank bench,
        /// because that was easier than getting a new one. It is set INTO
        /// the bench rather than standing on it, which is what that
        /// arrangement actually looks like.
        ///
        /// The roof steps rather than slopes. A part carries a yaw and no
        /// pitch, and four boards falling six centimetres each read as a
        /// mono-pitch at this resolution while staying inside the batch.
        /// </summary>
        private static void AppendPrivy(
            MountainRoadPlateauDescriptor plateau,
            float yardTop,
            ICollection<MountainRoadSitePartDescriptor> parts)
        {
            const float right = 17f;
            const float forward = 13.6f;
            const float yaw = -14f;

            void Board(
                string id,
                float offsetRight,
                float worldY,
                float offsetForward,
                Vector3 size,
                MountainRoadSiteStyle style,
                float extraYaw,
                bool blocks)
            {
                float radians = yaw * Mathf.Deg2Rad;
                float localRight = right +
                                   offsetRight * Mathf.Cos(radians) +
                                   offsetForward * Mathf.Sin(radians);
                float localForward = forward -
                                     offsetRight * Mathf.Sin(radians) +
                                     offsetForward * Mathf.Cos(radians);
                parts.Add(Part(
                    plateau,
                    id,
                    MountainRoadSiteGroup.ServiceYard,
                    style,
                    localRight,
                    worldY,
                    localForward,
                    size,
                    yaw + extraYaw,
                    blocks));
            }

            for (int index = 0; index < 2; index++)
            {
                Board(
                    $"site-privy-skid-{index:00}",
                    0f,
                    yardTop + 0.07f,
                    index == 0 ? -0.6f : 0.6f,
                    new Vector3(1.62f, 0.14f, 0.18f),
                    MountainRoadSiteStyle.DeadTimber,
                    0f,
                    true);
            }

            Board(
                "site-privy-floor",
                0f,
                yardTop + 0.19f,
                0f,
                new Vector3(1.5f, 0.1f, 1.5f),
                MountainRoadSiteStyle.DeadTimber,
                0f,
                true);
            Board(
                "site-privy-wall-back",
                0f,
                yardTop + 1.31f,
                0.705f,
                new Vector3(1.5f, 2.15f, 0.09f),
                MountainRoadSiteStyle.DeadTimber,
                0f,
                true);
            Board(
                "site-privy-wall-left",
                -0.705f,
                yardTop + 1.31f,
                0f,
                new Vector3(0.09f, 2.15f, 1.5f),
                MountainRoadSiteStyle.DeadTimber,
                0f,
                true);
            Board(
                "site-privy-wall-right",
                0.705f,
                yardTop + 1.31f,
                0f,
                new Vector3(0.09f, 2.15f, 1.5f),
                MountainRoadSiteStyle.DeadTimber,
                0f,
                true);
            Board(
                "site-privy-lintel",
                0f,
                yardTop + 2.24f,
                -0.705f,
                new Vector3(1.5f, 0.28f, 0.09f),
                MountainRoadSiteStyle.DeadTimber,
                0f,
                true);

            for (int index = 0; index < 2; index++)
            {
                Board(
                    $"site-privy-jamb-{index:00}",
                    index == 0 ? -0.64f : 0.64f,
                    yardTop + 1.22f,
                    -0.705f,
                    new Vector3(0.22f, 1.86f, 0.09f),
                    MountainRoadSiteStyle.DeadTimber,
                    0f,
                    true);
            }

            // Ajar, and in two leaves with a slot between them: the
            // cut-out every one of these doors has, made of the gap
            // instead of a hole nothing here can cut.
            Board(
                "site-privy-door-lower",
                -0.24f,
                yardTop + 0.88f,
                -0.92f,
                new Vector3(0.7f, 1.28f, 0.055f),
                MountainRoadSiteStyle.Timber,
                26f,
                true);
            Board(
                "site-privy-door-upper",
                -0.24f,
                yardTop + 1.79f,
                -0.92f,
                new Vector3(0.7f, 0.3f, 0.055f),
                MountainRoadSiteStyle.Timber,
                26f,
                true);

            for (int index = 0; index < 4; index++)
            {
                Board(
                    $"site-privy-roof-{index:00}",
                    0f,
                    yardTop + 2.5f - index * 0.06f,
                    0.66f - index * 0.42f,
                    new Vector3(1.76f, 0.08f, 0.44f),
                    MountainRoadSiteStyle.Timber,
                    0f,
                    true);
            }

            Board(
                "site-privy-bench",
                0f,
                yardTop + 0.44f,
                0.34f,
                new Vector3(1.34f, 0.42f, 0.62f),
                MountainRoadSiteStyle.DeadTimber,
                0f,
                true);
            Board(
                "site-privy-pan-seat",
                0f,
                yardTop + 0.66f,
                0.3f,
                new Vector3(0.52f, 0.06f, 0.52f),
                MountainRoadSiteStyle.DeadTimber,
                0f,
                false);
        }

        /// <summary>
        /// The face the terrace was taken out of. It runs the east rim
        /// because that is the side the ground climbs, stands behind the
        /// cable station, and turns west across the north-east corner to
        /// meet the parapet. The cable crosses it eleven metres up.
        /// </summary>
        private static void AppendRockCut(
            MountainRoadPlateauDescriptor plateau,
            float yardTop,
            ICollection<MountainRoadSitePartDescriptor> parts)
        {
            float[,] faces =
            {
                { -0.6f, 3.4f },
                { 2.6f, 3.8f },
                { 5.7f, 4.3f },
                { 8.8f, 5f },
                { 11.9f, 5.7f },
                { 15f, 6.4f }
            };
            for (int index = 0; index < faces.GetLength(0); index++)
            {
                float height = faces[index, 1];
                parts.Add(Part(
                    plateau,
                    $"site-rock-face-{index:00}",
                    MountainRoadSiteGroup.RockCut,
                    MountainRoadSiteStyle.RawStone,
                    21.7f,
                    yardTop + height * 0.5f - 0.5f,
                    faces[index, 0],
                    new Vector3(3f, height, 3.2f),
                    index % 2 == 0 ? 3f : -4f,
                    true));
            }

            for (int index = 0; index < 2; index++)
            {
                parts.Add(Part(
                    plateau,
                    $"site-rock-return-{index:00}",
                    MountainRoadSiteGroup.RockCut,
                    MountainRoadSiteStyle.RawStone,
                    index == 0 ? 19.4f : 15.6f,
                    yardTop + 3f - 0.5f,
                    19.4f,
                    new Vector3(4f, 6.2f, 3f),
                    index == 0 ? -12f : 6f,
                    true));
            }

            float[,] talus =
            {
                { 19.2f, 12.4f, 1.35f },
                { 19.9f, 14.6f, 1.05f },
                { 18.4f, 15.8f, 0.9f },
                { 20.1f, 16.9f, 1.2f }
            };
            for (int index = 0; index < talus.GetLength(0); index++)
            {
                float size = talus[index, 2];
                parts.Add(Part(
                    plateau,
                    $"site-rock-talus-{index:00}",
                    MountainRoadSiteGroup.RockCut,
                    MountainRoadSiteStyle.RawStone,
                    talus[index, 0],
                    yardTop + size * 0.32f,
                    talus[index, 1],
                    new Vector3(size, size * 0.8f, size * 0.9f),
                    index * 27f,
                    true));
            }
        }

        /// <summary>
        /// The cafe's blind flank. The ground falls away west on its own,
        /// so this side needs an explanation rather than a wall.
        /// </summary>
        private static void AppendWestRail(
            MountainRoadPlateauDescriptor plateau,
            float yardTop,
            ICollection<MountainRoadSitePartDescriptor> parts)
        {
            // The west edge bows out to `-21` in its middle and comes back
            // to `-20.17` at both ends, so the run sits at `-19.7` and
            // stops short of both: a rail hung on the widest point would
            // have its ends in the air.
            for (int index = 0; index < 4; index++)
            {
                float forward = Mathf.Lerp(0f, 11.5f, index / 3f);
                parts.Add(Part(
                    plateau,
                    $"site-west-rail-post-{index:00}",
                    MountainRoadSiteGroup.Brink,
                    MountainRoadSiteStyle.RustedIron,
                    -19.7f,
                    yardTop + 0.5f,
                    forward,
                    new Vector3(0.12f, 1f, 0.12f),
                    0f,
                    true));
            }

            for (int index = 0; index < 2; index++)
            {
                parts.Add(Part(
                    plateau,
                    $"site-west-rail-{index:00}",
                    MountainRoadSiteGroup.Brink,
                    MountainRoadSiteStyle.RustedIron,
                    -19.7f,
                    yardTop + (index == 0 ? 0.5f : 0.92f),
                    5.75f,
                    new Vector3(0.09f, 0.09f, 11.5f),
                    0f,
                    true));
            }
        }

        private static MountainRoadSiteSeatDescriptor CreateBrinkSeat(
            MountainRoadPlateauDescriptor plateau,
            float terraceTop)
        {
            return new MountainRoadSiteSeatDescriptor(
                "mountain-brink-bench",
                Point(
                    plateau,
                    BenchRight,
                    terraceTop + BenchSeatRise,
                    BenchForward),
                BenchSeatWidth,
                BenchSeatDepth,
                terraceTop,
                plateau.Forward);
        }

        private static MountainRoadSiteSeatDescriptor CreateCounterSeat(
            MountainRoadPlateauDescriptor plateau,
            MountainRoadCafePlan cafe,
            float yardTop)
        {
            Vector3 seat = cafe.Center +
                           cafe.Right * CounterSeatCafeRight +
                           cafe.Forward *
                           MountainRoadCafeWorldBuilder.StoolForward;
            seat.y = cafe.FloorY +
                     MountainRoadCafeWorldBuilder.StoolSeatTopAboveFloor;
            return new MountainRoadSiteSeatDescriptor(
                "mountain-cafe-stool",
                seat,
                MountainRoadCafeWorldBuilder.StoolSeatDiameter,
                MountainRoadCafeWorldBuilder.StoolSeatDiameter,
                yardTop,
                cafe.Forward,
                -cafe.Forward);
        }

        private static MountainRoadSitePracticalDescriptor CreateYardLamp(
            MountainRoadPlateauDescriptor plateau,
            float yardTop)
        {
            return new MountainRoadSitePracticalDescriptor(
                "site-yard-lamp-shade",
                Point(plateau, -3.15f, yardTop + 5.5f, 11.4f),
                Vector3.down,
                14f,
                104f);
        }

        /// <summary>
        /// A point on the pad at an ABSOLUTE world height.
        ///
        /// <see cref="MountainRoadTerminalPlanner.LocalToWorld"/> takes an
        /// OFFSET for its `up`, and every cloth, chain, seat and practical
        /// here was handing it `yardTop + something` - a height added to a
        /// height, which hung all of them twenty-six metres in the air.
        /// The bench went up with them, and the seat test did not see it
        /// because a plank dock takes its own height from `GroundY`
        /// rather than from the seat. Nothing routes through the offset
        /// form any more.
        /// </summary>
        private static Vector3 Point(
            MountainRoadPlateauDescriptor plateau,
            float right,
            float worldY,
            float forward)
        {
            Vector3 point = MountainRoadTerminalPlanner.LocalToWorld(
                plateau,
                right,
                0f,
                forward);
            point.y = worldY;
            return point;
        }

        private static MountainRoadSitePartDescriptor Part(
            MountainRoadPlateauDescriptor plateau,
            string stableId,
            MountainRoadSiteGroup group,
            MountainRoadSiteStyle style,
            float right,
            float worldY,
            float forward,
            Vector3 size,
            float localYawDegrees,
            bool blocksMovement)
        {
            Vector3 center = MountainRoadTerminalPlanner.LocalToWorld(
                plateau,
                right,
                0f,
                forward);
            center.y = worldY;
            float plateauYaw = Mathf.Atan2(
                plateau.Forward.x,
                plateau.Forward.z) * Mathf.Rad2Deg;
            return new MountainRoadSitePartDescriptor(
                stableId,
                group,
                style,
                center,
                size,
                plateauYaw + localYawDegrees,
                blocksMovement);
        }
    }
}
