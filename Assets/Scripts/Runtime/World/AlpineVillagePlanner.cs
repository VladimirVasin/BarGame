using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Lays out the village above the cableway. Pure data, deterministic from
    /// the session seed, no GameObject and no UnityEngine.Random.
    ///
    /// The composition is one idea and everything here serves it: the player
    /// steps off the cabin at the bottom of a very gentle slope, sees one
    /// crooked lane going up, and the house at the top of it is the mother's.
    /// The chapel, the adit and the burial ground sit on side spurs and are
    /// found on the way, because the head of the lane belongs to the house.
    /// </summary>
    public static class AlpineVillagePlanner
    {
        /// <summary>
        /// The village stands well clear of the mountain road's own extent so
        /// the two areas never share a coordinate on the map. Height is what
        /// matters: the weather shaper reads altitude, and this is a long way
        /// above the terminal the cabin left.
        /// </summary>
        public static readonly Vector3 SlopeOrigin =
            new Vector3(820f, 96f, 210f);

        /// <summary>Deliberately off-axis, so nothing reads as a grid.</summary>
        public static readonly Vector3 Uphill =
            new Vector3(0.34f, 0f, 0.94f);

        /// <summary>
        /// Gentle enough that the lane needs no step anywhere, steep enough
        /// that the house is visibly above the station from the moment the
        /// hero turns round. Under the pedestrian ceiling of `8.3%`.
        /// </summary>
        public const float Grade = 0.078f;

        public const float LaneLength = 82f;
        public const float LaneWidth = 3.6f;
        public const float LaneSampleStep = 1f;

        public const float StationPadTopOffset = 0.16f;

        /// <summary>
        /// How high the rope runs over the station pad. Together with the
        /// cabin's own hang this is what fixes the boarding step, which
        /// `MountainRoadCablewayPlan` derives - the same number at both
        /// terminals, solved in one place.
        /// </summary>
        public const float StationCableHeight = 4f;
        public const float StationSetback = 7f;
        public static readonly Vector2 StationPadSize = new Vector2(9f, 6.2f);

        public const int HouseCount = 12;
        public const float FirstHouseDistance = 8f;
        public const float LastHouseDistance = 75f;

        // Three authored frontage chapters, not a subdivision algorithm.
        // The two nine-metre pauses frame the cemetery and chapel reveals;
        // the last three houses make a close threshold before the head opens.
        private static readonly float[] HouseDistanceBeats =
        {
            8f, 13.5f, 19f, 25f, 34f, 40f,
            46f, 52f, 61f, 67f, 72f, 75f
        };

        // Two same-side pairs break the left/right metronome without putting
        // same-side neighbours close enough to touch.
        private static readonly int[] HouseSideBeats =
        {
            -1, 1, -1, 1, 1, -1,
            1, -1, -1, 1, -1, 1
        };

        // The mesh variants already have crooked roofs; these yaw beats stop
        // neighbouring whole buildings from returning to parallel facades.
        private static readonly float[] HouseYawBeats =
        {
            -11f, 7f, -4f, 10f, -8f, 4f,
            13f, -3f, 11f, -10f, 2f, -7f
        };

        // The two same-side reveal pairs and the last threshold house sit in
        // a deliberate rear row. This is visible hill-village depth, not an
        // emergency collision offset: keeping it authored prevents a greedy
        // solver from pushing every later house behind the previous one.
        private static readonly float[] HouseDepthBeats =
        {
            0f, 0f, 0f, 0f, 7.2f, 0f,
            0f, 0f, 7.2f, 0f, 0f, 7.5f
        };

        /// <summary>Gap between the lane edge and the nearest house wall.
        /// </summary>
        public const float HouseLaneClearance = 1.6f;

        // A local trim around an authored row may approach the lane, but it
        // keeps more than the validator's absolute physical minimum. At equal
        // displacement the search tries away from the lane first.
        private const float HouseSolveLaneClearance = 0.55f;
        private const float HouseDepthSolveStep = 0.4f;
        private const int HouseDepthSolveRings = 12;

        public const float MothersHouseSetback = 2f;
        public static readonly Vector2 MothersHouseFootprint =
            new Vector2(11f, 9f);
        public const float MothersHouseHeight = 7f;
        public const float MothersHouseDoorAcross = -0.36f;
        public const float MothersHouseEntranceTriggerRadius = 1.05f;
        public const float MothersHouseReturnStandoff = 1.55f;

        /// <summary>How far in front of a threshold the hero stands.</summary>
        public const float DoorDockStandoff = 1.1f;

        /// <summary>
        /// How far a house door may sit off the centre of its own front
        /// wall, either way.
        ///
        /// The offset used to be the world builder's, picked from the
        /// authored mesh variant, and it existed only so twelve doors did
        /// not read as one stamped row. That was fine while a door was
        /// scenery. It is not fine now that a door is a place the hero walks
        /// to: the plan owns the threshold, the dock and the trodden path
        /// that reaches it, and a leaf drawn a quarter of a metre off all
        /// three is a handle standing beside its own door. So the offset is
        /// the plan's, like every other village position, and the builder
        /// reads <see cref="AlpineVillagePlotDescriptor.DoorAcrossOffset"/>
        /// for the leaf it draws. Kept well under the door step's own half
        /// width, so every threshold path still meets the step.
        /// </summary>
        public const float HouseDoorAcross = 0.26f;

        /// <summary>
        /// Reach of a house door's interaction trigger. Smaller than the
        /// mother's, because twelve of these stand along one lane and the
        /// prompt should belong to the door the hero is actually at.
        /// </summary>
        public const float HouseDoorTriggerRadius = 0.72f;

        /// <summary>
        /// Flat ground kept around the inhabited hull before the enclosing
        /// ridge begins its standoff. `30` put the wall's toe `36 m` from the
        /// last houses and its crest at `16-20°` from the lane - a faint
        /// smear in the haze, not a bowl. At `12` the toe stands `18 m`
        /// outside the top house's envelope and the wall looms; it is still
        /// well past the widest shelf blend (`PlotApron 1.1 +
        /// ShelfBlendDistance 3.6`), so no plot's flat ever meets the rise.
        /// Every `TerrainBounds` consumer - validator, map patch, spawn -
        /// re-derives from the rect it contains.
        /// </summary>
        public const float TerrainMargin = 12f;

        private const uint HouseWidthSalt = 0x5641_4C31u;
        private const uint HouseDepthSalt = 0x5641_4C32u;
        private const uint HouseHeightSalt = 0x5641_4C33u;
        private const uint HouseJitterSalt = 0x5641_4C34u;
        private const uint HouseYawSalt = 0x5641_4C35u;
        private const uint HouseSetbackSalt = 0x5641_4C36u;
        private const uint HouseDoorAcrossSalt = 0x5641_4C37u;

        public static AlpineVillagePlan Create(
            int seed = MountainRoadPlanner.DefaultSeed)
        {
            Vector3 uphill = Uphill.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, uphill).normalized;

            AlpineVillageLanePlan lane = CreateLane(uphill, right);
            var plots = new List<AlpineVillagePlotDescriptor>();
            AlpineVillagePlotDescriptor mothersHouse =
                CreateMothersHouse(lane, uphill);
            Vector3 mothersHouseReturn =
                mothersHouse.DoorGroundPosition +
                mothersHouse.Facing * MothersHouseReturnStandoff;
            mothersHouseReturn.y = mothersHouse.GroundCenter.y;
            plots.Add(mothersHouse);
            AppendHouses(seed, lane, plots);
            AppendSpurs(lane, right, plots);

            AlpineVillageStationPlan station = CreateStation(
                lane,
                uphill,
                right);
            Rect terrainBounds = CalculateTerrainBounds(lane, plots, station);
            Rect terrainMeshBounds = CalculateTerrainMeshBounds(
                terrainBounds,
                station);
            List<AlpineVillageRidgeDescriptor> ridges =
                CreateRidges(terrainBounds, uphill, right);

            AlpineVillageLaneSample foot = lane.Sample(2f);
            Vector3 spawnPosition = foot.Position;
            Bounds worldBounds = CalculateWorldBounds(
                terrainMeshBounds,
                lane,
                station);

            var plan = new AlpineVillagePlan(
                seed,
                SlopeOrigin,
                uphill,
                Grade,
                lane,
                station,
                mothersHouse,
                mothersHouseReturn,
                plots,
                ridges,
                terrainBounds,
                terrainMeshBounds,
                worldBounds,
                spawnPosition,
                uphill);

            // The water is traced LAST and on the finished ground, because
            // the fall line it follows is made by the lane's bed, the plot
            // shelves and the cableway cut as much as by the macro plane -
            // and because the sampler dishes a swale under it, which must not
            // exist while it is deciding where to run.
            plan.AttachBrook(AlpineVillageBrookPlanner.Create(plan));
            plan.ValidateOrThrow();
            return plan;
        }

        /// <summary>
        /// The lane. Four control points give it a slow double bend, so it
        /// reads as a village street rather than a ramp, and the house at the
        /// top comes into view a little at a time.
        /// </summary>
        private static AlpineVillageLanePlan CreateLane(
            Vector3 uphill,
            Vector3 right)
        {
            var controls = new List<Vector3>();
            float[] along = { 0f, 21f, 44f, 63f, LaneLength };
            float[] across = { 0f, 2.4f, -1.9f, 1.3f, 0f };
            for (int index = 0; index < along.Length; index++)
            {
                Vector2 pointXZ = new Vector2(
                    SlopeOrigin.x +
                    uphill.x * along[index] +
                    right.x * across[index],
                    SlopeOrigin.z +
                    uphill.z * along[index] +
                    right.z * across[index]);
                float height = AlpineVillageTerrainSampler.SampleMacroHeight(
                    SlopeOrigin,
                    uphill,
                    Grade,
                    pointXZ);
                controls.Add(new Vector3(pointXZ.x, height, pointXZ.y));
            }

            var samples = new List<AlpineVillageLaneSample>();
            float travelled = 0f;
            for (int index = 0; index < controls.Count - 1; index++)
            {
                Vector3 first = controls[index];
                Vector3 second = controls[index + 1];
                Vector3 delta = second - first;
                Vector3 forward = new Vector3(delta.x, 0f, delta.z);
                float span = forward.magnitude;
                forward = forward.normalized;
                int steps = Mathf.Max(
                    1,
                    Mathf.RoundToInt(span / LaneSampleStep));
                for (int step = 0; step < steps; step++)
                {
                    float amount = step / (float)steps;
                    samples.Add(new AlpineVillageLaneSample(
                        travelled + span * amount,
                        Vector3.Lerp(first, second, amount),
                        forward,
                        LaneWidth));
                }

                travelled += span;
            }

            Vector3 lastForward = samples[samples.Count - 1].Forward;
            samples.Add(new AlpineVillageLaneSample(
                travelled,
                controls[controls.Count - 1],
                lastForward,
                LaneWidth));
            return new AlpineVillageLanePlan(samples, travelled);
        }

        private static AlpineVillagePlotDescriptor CreateMothersHouse(
            AlpineVillageLanePlan lane,
            Vector3 uphill)
        {
            AlpineVillageLaneSample head = lane.Sample(lane.Length);
            Vector3 facing = -head.Forward;
            Vector3 frontCenter = head.Position +
                                  head.Forward * MothersHouseSetback;
            Vector3 center = frontCenter +
                              head.Forward *
                              (MothersHouseFootprint.y * 0.5f);
            center.y = frontCenter.y;
            Vector3 buildingRight = Vector3.Cross(
                Vector3.up,
                facing).normalized;
            Vector3 doorGround = frontCenter +
                                 buildingRight * MothersHouseDoorAcross;
            Vector3 dock = doorGround + facing * DoorDockStandoff;
            dock.y = head.Position.y;
            return new AlpineVillagePlotDescriptor(
                "village-mothers-house",
                AlpineVillagePlotKind.MothersHouse,
                lane.Length,
                0,
                center,
                facing,
                MothersHouseFootprint,
                doorGround,
                dock,
                MothersHouseHeight);
        }

        private static void AppendHouses(
            int seed,
            AlpineVillageLanePlan lane,
            ICollection<AlpineVillagePlotDescriptor> target)
        {
            if (HouseDistanceBeats.Length != HouseCount ||
                HouseSideBeats.Length != HouseCount ||
                HouseYawBeats.Length != HouseCount ||
                HouseDepthBeats.Length != HouseCount)
            {
                throw new InvalidOperationException(
                    "The village house rhythm does not match HouseCount.");
            }

            for (int index = 0; index < HouseCount; index++)
            {
                // A quarter-metre either way keeps seeds alive without moving
                // the authored pauses or the side-landmark sight lines.
                float jitter = (Unit(seed, index, HouseJitterSalt) - 0.5f) *
                               0.5f;
                float distance = Mathf.Clamp(
                    HouseDistanceBeats[index] + jitter,
                    FirstHouseDistance,
                    LastHouseDistance);
                int side = HouseSideBeats[index];
                var footprint = new Vector2(
                    Mathf.Lerp(
                        6.2f,
                        8.3f,
                        Unit(seed, index, HouseWidthSalt)),
                    Mathf.Lerp(
                        5.5f,
                        8f,
                        Unit(seed, index, HouseDepthSalt)));
                float height = Mathf.Lerp(
                    4.2f,
                    6.4f,
                    Unit(seed, index, HouseHeightSalt));

                AlpineVillageLaneSample sample = lane.Sample(distance);
                Vector3 outward = sample.Right * side;
                float setback = Mathf.Lerp(
                    -0.35f,
                    0.95f,
                    Unit(seed, index, HouseSetbackSalt));
                float yaw = HouseYawBeats[index] +
                            (Unit(seed, index, HouseYawSalt) - 0.5f) * 3f;
                Vector3 facing = Quaternion.AngleAxis(
                    yaw,
                    Vector3.up) * -outward;
                Vector3 buildingRight = Vector3.Cross(
                    Vector3.up,
                    facing).normalized;
                float doorAcross = Mathf.Lerp(
                    -HouseDoorAcross,
                    HouseDoorAcross,
                    Unit(seed, index, HouseDoorAcrossSalt));
                float outwardRadius =
                    Mathf.Abs(Vector3.Dot(outward, buildingRight)) *
                    footprint.x * 0.5f +
                    Mathf.Abs(Vector3.Dot(outward, facing)) *
                    footprint.y * 0.5f;
                float standoff = sample.Width * 0.5f +
                                 HouseLaneClearance +
                                 outwardRadius +
                                 setback +
                                 HouseDepthBeats[index];
                AlpineVillagePlotDescriptor accepted = null;

                // Keep the authored front/rear rhythm and make only the
                // smallest local correction needed by a wide or strongly
                // yawed seed. Alternating around the beat avoids the old
                // cascade where each later house had to move beyond an
                // already deep neighbour.
                for (int attempt = 0;
                     attempt <= HouseDepthSolveRings * 2;
                     attempt++)
                {
                    float depthOffset = GetHouseDepthSolveOffset(attempt);
                    Vector3 center = sample.Position +
                                     outward *
                                     (standoff + depthOffset);
                    center.y = sample.Position.y;
                    Vector3 doorGround = center +
                                         facing *
                                         (footprint.y * 0.5f) +
                                         buildingRight * doorAcross;
                    Vector3 dock = doorGround +
                                   facing * DoorDockStandoff;
                    dock.y = sample.Position.y;
                    var candidate = new AlpineVillagePlotDescriptor(
                        $"village-house-{index:00}",
                        AlpineVillagePlotKind.House,
                        distance,
                        side,
                        center,
                        facing,
                        footprint,
                        doorGround,
                        dock,
                        height);
                    if (OverlapsExistingPlot(candidate, target) ||
                        AlpineVillageValidator.MeasureLaneClearance(
                            lane,
                            candidate) <
                        HouseSolveLaneClearance)
                    {
                        continue;
                    }

                    accepted = candidate;
                    break;
                }

                if (accepted == null)
                {
                    throw new InvalidOperationException(
                        $"House {index:00} cannot clear the authored " +
                        "village rhythm.");
                }

                target.Add(accepted);
            }
        }

        private static float GetHouseDepthSolveOffset(int attempt)
        {
            if (attempt <= 0)
            {
                return 0f;
            }

            int ring = (attempt + 1) / 2;
            float direction = attempt % 2 == 1 ? 1f : -1f;
            return ring * HouseDepthSolveStep * direction;
        }

        private static bool OverlapsExistingPlot(
            AlpineVillagePlotDescriptor candidate,
            IEnumerable<AlpineVillagePlotDescriptor> existing)
        {
            foreach (AlpineVillagePlotDescriptor plot in existing)
            {
                if (AlpineVillageValidator.FootprintsOverlap(
                        candidate,
                        plot))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The two things that are not on the lane, and neither stands at the
        /// head of the street: the chapel over the source is a side errand,
        /// and the spring's head is behind the houses.
        ///
        /// The adit and the burial ground used to be here and are gone - out
        /// of the village and out of the story, by the lead's decision. The
        /// spring took the adit's place because the water is what the village
        /// is actually about.
        /// </summary>
        private static void AppendSpurs(
            AlpineVillageLanePlan lane,
            Vector3 right,
            ICollection<AlpineVillagePlotDescriptor> target)
        {
            target.Add(CreateSpur(
                lane,
                right,
                "village-chapel",
                AlpineVillagePlotKind.Chapel,
                58f,
                1,
                23f,
                new Vector2(5f, 6.5f),
                4.2f));
            // The spring's head, where the adit used to be. Low and wide
            // rather than tall: nothing here stands up out of the hill, the
            // water simply arrives at the surface.
            target.Add(CreateSpur(
                lane,
                right,
                "village-spring",
                AlpineVillagePlotKind.Spring,
                67f,
                -1,
                29f,
                new Vector2(6f, 5f),
                0.9f));
        }

        private static AlpineVillagePlotDescriptor CreateSpur(
            AlpineVillageLanePlan lane,
            Vector3 right,
            string stableId,
            AlpineVillagePlotKind kind,
            float laneDistance,
            int side,
            float lateral,
            Vector2 footprint,
            float height)
        {
            AlpineVillageLaneSample sample = lane.Sample(laneDistance);
            Vector3 outward = sample.Right * side;
            Vector2 centerXZ = new Vector2(
                sample.Position.x + outward.x * lateral,
                sample.Position.z + outward.z * lateral);
            float ground = AlpineVillageTerrainSampler.SampleMacroHeight(
                SlopeOrigin,
                Uphill.normalized,
                Grade,
                centerXZ);
            var center = new Vector3(centerXZ.x, ground, centerXZ.y);
            Vector3 facing = -outward;
            Vector3 doorGround = center + facing * (footprint.y * 0.5f);
            Vector3 dock = doorGround + facing * DoorDockStandoff;
            return new AlpineVillagePlotDescriptor(
                stableId,
                kind,
                laneDistance,
                side,
                center,
                facing,
                footprint,
                doorGround,
                dock,
                height);
        }

        /// <summary>
        /// The upper terminal, downhill of the lane foot, and the line that
        /// leaves it.
        ///
        /// The cableway plan's first node is the end this scene builds a
        /// station at and its last node is the end hidden behind the ridge -
        /// so here the node heights DESCEND, which is the mirror of the
        /// mountain terminal and is why the type talks about a near and a far
        /// end rather than a top and a bottom.
        /// </summary>
        private static AlpineVillageStationPlan CreateStation(
            AlpineVillageLanePlan lane,
            Vector3 uphill,
            Vector3 right)
        {
            AlpineVillageLaneSample foot = lane.Sample(0f);
            Vector3 padCenter = foot.Position - uphill * StationSetback;
            padCenter.y = foot.Position.y;

            // The line leaves the village downhill and a little across, so it
            // does not run straight back down the lane the hero is about to
            // walk up.
            Vector3 lineForward = (-uphill * 0.94f + right * 0.34f)
                .normalized;
            Vector3 lineRight = Vector3.Cross(Vector3.up, lineForward)
                .normalized;

            // THE PAD IS SQUARE TO THE LINE, not to the hill, and getting that
            // wrong was an invisible wall you could walk into while standing
            // on visible concrete.
            //
            // `MountainCablewayWorldBuilder` poses the whole station with
            // `LookRotation(plan.LineForward)` and lays every solid box on
            // `LineRight`/`LineForward`, but this rectangle used to be built
            // on `right`/`uphill` - and at the village those two frames are
            // `19.9°` apart. `AlpineVillageWalkableArea` takes its pad rect
            // from here, so the mask sat skewed across the concrete: it
            // refused `3.71 m²` of real pad at all four corners, up to
            // `1.35 m` deep, and granted `7.59 m²` of thin air off the sides.
            // At the summit `MountainRoadTerminalPlanner` builds its rect from
            // the line axes, which is why the two ends never disagreed in a
            // test.
            var padArea = new MountainRoadTerminalRect(
                padCenter,
                lineRight,
                lineForward,
                StationPadSize);

            Vector3 nearCable = padCenter +
                                lineForward * 1.9f +
                                Vector3.up * StationCableHeight;
            // A LINE LEAVES A STATION NEARLY LEVEL AND STEEPENS. The drops
            // used to be `{0, -13, -18.5, -22, -24}`, which is `39` degrees
            // out of the first span and flattening downhill - backwards, and
            // unbuildable: the cabin's floor hangs `3.13 m` under the rope,
            // so at that grade its underside was below the boarding platform
            // one metre off the pad and stayed under the ground the terrain
            // can honestly cut. The total fall is unchanged at `24 m`; it is
            // spread the way a mountain line actually falls.
            // And past the brink the rope goes on down the mountainside at
            // about the slope's own fall, to a turn nearly two far planes
            // beyond the cut: from the platform and from the seat the line
            // dissolves into the haze and never shows an end.
            float[] distances =
            {
                0f, 16f, 34f, 48f, 62f, 84f, 110f, 138f, 168f, 200f, 230f
            };
            float[] drops =
            {
                0f, -2f, -11f, -18.5f, -23.5f, -25.2f, -27.2f, -29.2f,
                -31.4f, -33.6f, -35.7f
            };
            var nodes = new List<MountainCablewayNodeDescriptor>(
                distances.Length);
            for (int index = 0; index < distances.Length; index++)
            {
                Vector3 cable = nearCable + lineForward * distances[index];
                cable.y = nearCable.y + drops[index];
                MountainCablewayNodeKind kind = index == 0
                    ? MountainCablewayNodeKind.LowerStation
                    : index == distances.Length - 1
                        ? MountainCablewayNodeKind.UpperTurn
                        : MountainCablewayNodeKind.Support;
                // The line leaves the station over a real brink, not through
                // the macro slope. Every visible support owns the same
                // clearance that the terrain sampler cuts under it, so its
                // legs meet the physical mesh and its rollers meet the rope.
                float groundY = index == 0
                    ? padCenter.y
                    : cable.y - AlpineVillageTerrainSampler
                        .CablewaySupportClearance;
                var ground = new Vector3(
                    cable.x,
                    groundY,
                    cable.z);
                nodes.Add(new MountainCablewayNodeDescriptor(
                    index == 0
                        ? "village-cableway-station"
                        : index == distances.Length - 1
                            ? "village-cableway-far-turn"
                            : $"village-cableway-support-{index:00}",
                    kind,
                    distances[index],
                    cable,
                    ground));
            }

            int cabinCount = MountainRoadTerminalPlanner.CablewayCabinCount;
            var cabins = new List<MountainCablewayCabinDescriptor>(cabinCount);
            for (int index = 0; index < cabinCount; index++)
            {
                cabins.Add(new MountainCablewayCabinDescriptor(
                    $"village-cableway-cabin-{index:00}",
                    index / (float)cabinCount));
            }

            var cableway = new MountainRoadCablewayPlan(
                "village-cableway",
                padArea,
                lineForward,
                lineRight,
                MountainRoadTerminalPlanner.CablewayTrackSeparation,
                MountainRoadTerminalPlanner.CablewayLineLength,
                MountainRoadTerminalPlanner.CablewayCabinSpeed,
                new Vector3(1.75f, 2.05f, 1.55f),
                nodes,
                cabins);

            return new AlpineVillageStationPlan(
                padArea,
                padCenter.y + StationPadTopOffset,
                cableway);
        }

        private static Rect CalculateTerrainBounds(
            AlpineVillageLanePlan lane,
            IReadOnlyList<AlpineVillagePlotDescriptor> plots,
            AlpineVillageStationPlan station)
        {
            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity;
            float maxZ = float.NegativeInfinity;

            for (int index = 0; index < lane.Samples.Count; index++)
            {
                Vector3 point = lane.Samples[index].Position;
                Include(point.x, point.z, ref minX, ref maxX, ref minZ,
                    ref maxZ);
            }

            for (int index = 0; index < plots.Count; index++)
            {
                Rect bounds = plots[index].BoundsXZ;
                Include(bounds.xMin, bounds.yMin, ref minX, ref maxX,
                    ref minZ, ref maxZ);
                Include(bounds.xMax, bounds.yMax, ref minX, ref maxX,
                    ref minZ, ref maxZ);
            }

            Vector3 padCenter = station.PadArea.Center;
            float padReach = station.PadArea.Size.magnitude * 0.5f;
            Include(padCenter.x - padReach, padCenter.z - padReach,
                ref minX, ref maxX, ref minZ, ref maxZ);
            Include(padCenter.x + padReach, padCenter.z + padReach,
                ref minX, ref maxX, ref minZ, ref maxZ);

            return Rect.MinMaxRect(
                minX - TerrainMargin,
                minZ - TerrainMargin,
                maxX + TerrainMargin,
                maxZ + TerrainMargin);
        }

        /// <summary>
        /// The inhabited bounds above are deliberately not the mesh bounds.
        /// The ridge begins only after them, and needs enough ground to reach
        /// its full height and continue behind the crest. The cableway also
        /// leaves the inhabited rectangle: its complete local line belongs to
        /// this scene even though its far turn is hidden in the mountain.
        /// </summary>
        private static Rect CalculateTerrainMeshBounds(
            Rect terrainBounds,
            AlpineVillageStationPlan station)
        {
            float outset = AlpineVillageTerrainSampler.RidgeMeshOutset;
            float minX = terrainBounds.xMin - outset;
            float maxX = terrainBounds.xMax + outset;
            float minZ = terrainBounds.yMin - outset;
            float maxZ = terrainBounds.yMax + outset;

            MountainRoadCablewayPlan cableway = station.Cableway;
            float cableHalfWidth =
                AlpineVillageTerrainSampler.CablewayCutOuterHalfWidth +
                AlpineVillageTerrainSampler.TerrainCell;
            for (int index = 0; index < cableway.Nodes.Count; index++)
            {
                Vector3 node = cableway.Nodes[index].GroundPosition;
                Vector3 across = cableway.LineRight * cableHalfWidth;
                Include(node.x - across.x, node.z - across.z,
                    ref minX, ref maxX, ref minZ, ref maxZ);
                Include(node.x + across.x, node.z + across.z,
                    ref minX, ref maxX, ref minZ, ref maxZ);
            }

            // Continue a little beyond the far turn, so the last towers
            // stand on ground rather than on the mesh's last row. The turn
            // itself is past the draw range and is never seen.
            Vector3 beyondTurn = cableway.UpperCableCenter +
                                 cableway.LineForward *
                                 AlpineVillageTerrainSampler.RidgeCrestDepth;
            Include(
                beyondTurn.x,
                beyondTurn.z,
                ref minX,
                ref maxX,
                ref minZ,
                ref maxZ);

            return Rect.MinMaxRect(minX, minZ, maxX, maxZ);
        }

        private static void Include(
            float x,
            float z,
            ref float minX,
            ref float maxX,
            ref float minZ,
            ref float maxZ)
        {
            minX = Mathf.Min(minX, x);
            maxX = Mathf.Max(maxX, x);
            minZ = Mathf.Min(minZ, z);
            maxZ = Mathf.Max(maxZ, z);
        }

        private static List<AlpineVillageRidgeDescriptor> CreateRidges(
            Rect terrainBounds,
            Vector3 uphill,
            Vector3 right)
        {
            var center = new Vector3(
                terrainBounds.center.x,
                SlopeOrigin.y,
                terrainBounds.center.y);
            float halfAlong = terrainBounds.height * 0.5f;
            float halfAcross = terrainBounds.width * 0.5f;
            return new List<AlpineVillageRidgeDescriptor>
            {
                new AlpineVillageRidgeDescriptor(
                    "village-ridge-head",
                    center + uphill * halfAlong,
                    right,
                    terrainBounds.width,
                    28f,
                    18f),
                new AlpineVillageRidgeDescriptor(
                    "village-ridge-left",
                    center - right * halfAcross,
                    uphill,
                    terrainBounds.height,
                    24f,
                    16f),
                new AlpineVillageRidgeDescriptor(
                    "village-ridge-right",
                    center + right * halfAcross,
                    uphill,
                    terrainBounds.height,
                    24f,
                    16f)
            };
        }

        private static Bounds CalculateWorldBounds(
            Rect terrainMeshBounds,
            AlpineVillageLanePlan lane,
            AlpineVillageStationPlan station)
        {
            float floor = Mathf.Min(lane.Start.y, lane.End.y) - 12f;
            for (int index = 0;
                 index < station.Cableway.Nodes.Count;
                 index++)
            {
                floor = Mathf.Min(
                    floor,
                    station.Cableway.Nodes[index].GroundPosition.y - 12f);
            }

            float ceiling = Mathf.Max(lane.Start.y, lane.End.y) +
                            AlpineVillageTerrainSampler.RidgeMaximumRise +
                            18f;
            var center = new Vector3(
                terrainMeshBounds.center.x,
                (floor + ceiling) * 0.5f,
                terrainMeshBounds.center.y);
            var size = new Vector3(
                terrainMeshBounds.width,
                ceiling - floor,
                terrainMeshBounds.height);
            return new Bounds(center, size);
        }

        private static float Unit(int seed, int index, uint salt)
        {
            uint hash = CitySoundStableHash.Combine(
                unchecked((uint)seed),
                unchecked((uint)index));
            return CitySoundStableHash.ToUnitFloat(
                CitySoundStableHash.Combine(hash, salt));
        }
    }
}
