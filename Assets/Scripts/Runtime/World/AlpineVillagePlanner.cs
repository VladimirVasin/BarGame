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
        public const float LastHouseDistance = 74f;

        /// <summary>Gap between the lane edge and the nearest house wall.
        /// </summary>
        public const float HouseLaneClearance = 1.6f;

        public const float MothersHouseSetback = 2f;
        public static readonly Vector2 MothersHouseFootprint =
            new Vector2(11f, 9f);
        public const float MothersHouseHeight = 7f;

        /// <summary>How far in front of a threshold the hero stands.</summary>
        public const float DoorDockStandoff = 1.1f;

        public const float TerrainMargin = 30f;

        private const uint HouseWidthSalt = 0x5641_4C31u;
        private const uint HouseDepthSalt = 0x5641_4C32u;
        private const uint HouseHeightSalt = 0x5641_4C33u;
        private const uint HouseJitterSalt = 0x5641_4C34u;

        public static AlpineVillagePlan Create(
            int seed = MountainRoadPlanner.DefaultSeed)
        {
            Vector3 uphill = Uphill.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, uphill).normalized;

            AlpineVillageLanePlan lane = CreateLane(uphill, right);
            var plots = new List<AlpineVillagePlotDescriptor>();
            AlpineVillagePlotDescriptor mothersHouse =
                CreateMothersHouse(lane, uphill);
            plots.Add(mothersHouse);
            AppendHouses(seed, lane, plots);
            AppendSpurs(lane, right, plots);

            AlpineVillageStationPlan station = CreateStation(
                lane,
                uphill,
                right);
            Rect terrainBounds = CalculateTerrainBounds(lane, plots, station);
            List<AlpineVillageRidgeDescriptor> ridges =
                CreateRidges(terrainBounds, uphill, right);

            AlpineVillageLaneSample foot = lane.Sample(2f);
            Vector3 spawnPosition = foot.Position;
            Bounds worldBounds = CalculateWorldBounds(terrainBounds, lane);

            var plan = new AlpineVillagePlan(
                seed,
                SlopeOrigin,
                uphill,
                Grade,
                lane,
                station,
                mothersHouse,
                plots,
                ridges,
                terrainBounds,
                worldBounds,
                spawnPosition,
                uphill);
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
            Vector3 doorGround = head.Position +
                                 head.Forward * MothersHouseSetback;
            Vector3 center = doorGround +
                             head.Forward *
                             (MothersHouseFootprint.y * 0.5f);
            center.y = doorGround.y;
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
            for (int index = 0; index < HouseCount; index++)
            {
                float amount = HouseCount <= 1
                    ? 0f
                    : index / (float)(HouseCount - 1);
                // Kept to a metre either way on purpose. Houses alternate
                // sides, so same-side neighbours stand `12 m` apart; any more
                // wander than this and the widest pair below can touch.
                float jitter = (Unit(seed, index, HouseJitterSalt) - 0.5f) *
                               2f;
                float distance = Mathf.Clamp(
                    Mathf.Lerp(
                        FirstHouseDistance,
                        LastHouseDistance,
                        amount) + jitter,
                    FirstHouseDistance,
                    LastHouseDistance);
                int side = index % 2 == 0 ? -1 : 1;
                var footprint = new Vector2(
                    Mathf.Lerp(
                        6.2f,
                        8.6f,
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
                float standoff = sample.Width * 0.5f +
                                 HouseLaneClearance +
                                 footprint.y * 0.5f;
                Vector3 center = sample.Position + outward * standoff;
                center.y = sample.Position.y;
                Vector3 facing = -outward;
                Vector3 doorGround = center +
                                     facing * (footprint.y * 0.5f);
                Vector3 dock = doorGround + facing * DoorDockStandoff;
                dock.y = sample.Position.y;
                target.Add(new AlpineVillagePlotDescriptor(
                    $"village-house-{index:00}",
                    AlpineVillagePlotKind.House,
                    distance,
                    side,
                    center,
                    facing,
                    footprint,
                    doorGround,
                    dock,
                    height));
            }
        }

        /// <summary>
        /// The three things that are not on the lane. All of them sit out on a
        /// spur and none of them stands at the head of the street: the chapel
        /// over the source is a side errand, the adit is behind the houses, and
        /// the burial ground is passed rather than visited.
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
            target.Add(CreateSpur(
                lane,
                right,
                "village-adit",
                AlpineVillagePlotKind.Adit,
                67f,
                -1,
                25f,
                new Vector2(6f, 5f),
                3.4f));
            target.Add(CreateSpur(
                lane,
                right,
                "village-cemetery",
                AlpineVillagePlotKind.Cemetery,
                29f,
                -1,
                27f,
                new Vector2(18f, 14f),
                1.8f));
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
            float[] distances = { 0f, 16f, 34f, 48f, 58f };
            float[] drops = { 0f, -13f, -18.5f, -22f, -24f };
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
                var groundXZ = new Vector2(cable.x, cable.z);
                var ground = new Vector3(
                    cable.x,
                    AlpineVillageTerrainSampler.SampleMacroHeight(
                        SlopeOrigin,
                        uphill,
                        Grade,
                        groundXZ),
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

            var cabins = new List<MountainCablewayCabinDescriptor>(4);
            for (int index = 0; index < 4; index++)
            {
                cabins.Add(new MountainCablewayCabinDescriptor(
                    $"village-cableway-cabin-{index:00}",
                    index / 4f));
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
                cabins,
                "village-cableway-ridge-occluder");

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
            Rect terrainBounds,
            AlpineVillageLanePlan lane)
        {
            float floor = Mathf.Min(lane.Start.y, lane.End.y) - 12f;
            float ceiling = Mathf.Max(lane.Start.y, lane.End.y) +
                            AlpineVillageTerrainSampler.RidgeMaximumRise +
                            18f;
            var center = new Vector3(
                terrainBounds.center.x,
                (floor + ceiling) * 0.5f,
                terrainBounds.center.y);
            var size = new Vector3(
                terrainBounds.width,
                ceiling - floor,
                terrainBounds.height);
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
