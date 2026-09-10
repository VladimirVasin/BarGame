using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The village's conifers, as pure data. They are the mountain road's own
    /// trees - same descriptor, same crown generator, same three silhouettes.
    ///
    /// The village stands in a CLEARING. The bowl floor outside the inhabited
    /// core carries a real forest; the lane, the paths, the yards around every
    /// house and the sightline from the station to the mother's house are what
    /// is cut out of it. Two smaller groups continue it upward: a fringe on the
    /// enclosing rise, and cut stubs at its foot.
    ///
    /// The three groups are not interchangeable:
    ///
    /// <list type="bullet">
    /// <item>Forest trees stand on walkable ground. They are obstacles of the
    /// walkable MASK, never of physics. A real collider would graze the hero,
    /// and a graze is read back as achieved movement and zeroes his planar
    /// speed - exactly what the pinned art-bible check forbids. The mask turns
    /// him before he moves, the way it already does around a house wall.</item>
    /// <item>Wall trees stand on the enclosing rise, outside the mask entirely.
    /// Nothing can reach them, so they carry no obstacle at all.</item>
    /// <item>Stumps stand lower on the same rise. The 74 degree face holds no
    /// snow, so a cut stub reads there instead of drowning in the knee-deep
    /// field the bowl floor carries.</item>
    /// </list>
    /// </summary>
    /// <summary>
    /// One piece of deadfall lying between the trees: a broken limb with a
    /// fork or two, resting on the snow rather than in the ground.
    ///
    /// The plan owns where it lies and how big it is; the builder owns the
    /// boxes that draw it, the same division the trunks already use.
    /// </summary>
    public readonly struct AlpineVillageBranchDescriptor
    {
        internal AlpineVillageBranchDescriptor(
            string stableId,
            Vector3 position,
            float yawDegrees,
            float tiltDegrees,
            float length,
            float thickness,
            int forkCount)
        {
            StableId = stableId ?? string.Empty;
            Position = position;
            YawDegrees = yawDegrees;
            TiltDegrees = tiltDegrees;
            Length = length;
            Thickness = thickness;
            ForkCount = forkCount;
        }

        public string StableId { get; }

        /// <summary>Where the limb rests: on the snow surface, not the soil.</summary>
        public Vector3 Position { get; }

        public float YawDegrees { get; }

        /// <summary>A few degrees off flat, so it lies rather than floats.</summary>
        public float TiltDegrees { get; }

        public float Length { get; }
        public float Thickness { get; }

        /// <summary>One or two side limbs still attached.</summary>
        public int ForkCount { get; }
    }

    public sealed class AlpineVillageTreePlan
    {
        internal AlpineVillageTreePlan(
            IReadOnlyList<MountainRoadForestDescriptor> forestTrees,
            IReadOnlyList<MountainRoadForestDescriptor> wallTrees,
            IReadOnlyList<MountainRoadForestDescriptor> stumps,
            IReadOnlyList<AlpineVillageBranchDescriptor> branches,
            float windFootY,
            float windSummitY)
        {
            ForestTrees = forestTrees;
            WallTrees = wallTrees;
            Stumps = stumps;
            Branches = branches;
            WindFootY = windFootY;
            WindSummitY = windSummitY;
        }

        /// <summary>
        /// The forest around the village, on walkable ground. These carry
        /// <c>BlocksMovement</c>, and the walkable mask carves them out.
        /// </summary>
        public IReadOnlyList<MountainRoadForestDescriptor> ForestTrees { get; }

        /// <summary>The fringe on the enclosing rise. Never reachable.</summary>
        public IReadOnlyList<MountainRoadForestDescriptor> WallTrees { get; }

        /// <summary>Cut stubs at the foot of the rise. Crownless, never reachable.</summary>
        public IReadOnlyList<MountainRoadForestDescriptor> Stumps { get; }

        /// <summary>
        /// Deadfall between the trees. Small detail, so it stays non-physical
        /// by art §13 - the hero walks over a broken limb, he does not stop
        /// dead against one.
        /// </summary>
        public IReadOnlyList<AlpineVillageBranchDescriptor> Branches { get; }

        /// <summary>
        /// Lowest and highest foot in the planted stand, for the wind profile.
        ///
        /// Not the lane's own start and end: the lane climbs `6.4 m` while the
        /// wall fringe stands `5-8 m` up a `74` degree face, so on the lane's
        /// scale the shader's climb term would saturate and the amplitude it
        /// exists for would collapse to a constant.
        /// </summary>
        public float WindFootY { get; }

        /// <summary>The high end of that same range.</summary>
        public float WindSummitY { get; }

        /// <summary>Every tree that carries a crown, forest and fringe together.</summary>
        public IEnumerable<MountainRoadForestDescriptor> CrownedTrees
        {
            get
            {
                for (int index = 0; index < ForestTrees.Count; index++)
                {
                    yield return ForestTrees[index];
                }

                for (int index = 0; index < WallTrees.Count; index++)
                {
                    yield return WallTrees[index];
                }
            }
        }
    }

    /// <summary>
    /// Places the village's conifers. Deterministic from the plan's seed.
    /// </summary>
    internal static class AlpineVillageTreePlanner
    {
        // ---- The forest on the bowl floor -------------------------------

        /// <summary>
        /// How many trees the bowl may carry. The mountain road grows `420`
        /// over `620 m` of climb; the village bowl is smaller but is forest on
        /// every side rather than two verges, so the same order is the right
        /// one. The scatter stops when the ground is full, not at this number.
        /// </summary>
        internal const int ForestTreeCount = 420;

        /// <summary>
        /// The clearing. No tree stands closer than this to the lane or to any
        /// trodden path, so the one rising axis, the yards and every route stay
        /// open and the village reads as a clearing rather than a corridor cut
        /// through trees.
        /// </summary>
        internal const float ForestClearing = 7f;

        /// <summary>
        /// Over this much further the stand comes up to full density, so the
        /// clearing has an edge that thins rather than a wall of trunks.
        /// </summary>
        internal const float ForestThinBand = 6f;

        /// <summary>Kept clear outside every plot's own flat shelf.</summary>
        internal const float ForestPlotClearance = 2.2f;

        /// <summary>
        /// The mother's house keeps a wider clearing than the rest. It is the
        /// one mass the composition points at, and the grayscale check asks for
        /// one rising axis and one mass at its head.
        /// </summary>
        internal const float LandmarkClearing = 18f;

        /// <summary>
        /// Floor under the gap between two trunks. Two saplings may not stand
        /// closer than this even when their crowns would allow it.
        /// </summary>
        internal const float ForestSpacing = 3.4f;

        internal const float ForestMinimumHeight = 4.5f;
        internal const float ForestMaximumHeight = 8.5f;

        /// <summary>
        /// Every tree draws a uniform scale in `[1, 2]` on top of its own
        /// size, so the stand runs from a sapling to something twice the tree
        /// it would otherwise have been. Height and crown scale together - a
        /// conifer stretched in one axis stops being the road's silhouette.
        /// </summary>
        internal const float SizeScaleMinimum = 1f;

        /// <summary>The top of that scatter.</summary>
        internal const float SizeScaleMaximum = 2f;

        /// <summary>
        /// Daylight kept between two crowns. With scale in play a crown reaches
        /// `4 m`, so a fixed trunk interval would let big neighbours grow
        /// straight through one another: spacing has to be measured from the
        /// two crowns that actually meet, not from a constant.
        /// </summary>
        internal const float CrownGap = 0.3f;

        // ---- The fringe on the rise -------------------------------------

        /// <summary>
        /// The wall band, in metres outside <c>TerrainBounds</c>. The authored
        /// rock panels stand from `5.4 m` outward, a tip must stay well below
        /// the crest, and a root must stand above `5 m` up the face so nobody
        /// at the toe can reach it.
        /// </summary>
        internal const float WallBandInner = 4.40f;

        /// <summary>The far edge of that band; just inboard of the panel foot.</summary>
        internal const float WallBandOuter = 5.15f;

        /// <summary>
        /// Stumps stand on the floor at the inner edge of the forest, between
        /// the trodden ground and the first trees, which is the one place a
        /// player walking the lane actually looks at them. They used to stand
        /// on the rise; there the `74` degree face swallowed them whole.
        /// </summary>
        internal const float StumpTroddenNear = 4f;

        /// <summary>The outer edge of that band.</summary>
        internal const float StumpTroddenFar = 11f;

        /// <summary>
        /// How far a stump must stand proud of the snow lying around it.
        ///
        /// This is why a stump's height is measured from the local drift
        /// rather than drawn from a fixed range: the untouched field is
        /// `0.45 m` deep, so any constant short enough to read as a stub is
        /// also short enough to vanish under it somewhere.
        /// </summary>
        internal const float StumpExposure = 0.35f;

        /// <summary>How much more than that the tallest stub shows.</summary>
        internal const float StumpExposureSpread = 0.4f;

        /// <summary>Nothing of a stump is buried beyond this on flat ground.</summary>
        internal const float StumpBuriedFoot = 0.05f;

        /// <summary>A stump keeps this clear of any standing trunk.</summary>
        internal const float StumpTrunkClearance = 1.2f;

        // ---- Deadfall ---------------------------------------------------

        /// <summary>How many broken limbs the forest floor may carry.</summary>
        internal const int BranchCount = 90;

        /// <summary>
        /// A limb falls from a tree, so it lies within reach of one. Beyond
        /// this it would read as litter someone carried out and dropped.
        /// </summary>
        internal const float BranchReachFromTrunk = 5f;

        /// <summary>...and not right against the trunk it came off.</summary>
        internal const float BranchTrunkStandoff = 0.8f;

        /// <summary>Deadfall keeps out of the walked village like the trees do.</summary>
        internal const float BranchTroddenClearance = 5f;

        internal const float BranchMinimumLength = 0.8f;
        internal const float BranchMaximumLength = 2.4f;
        internal const float BranchMinimumThickness = 0.06f;
        internal const float BranchMaximumThickness = 0.14f;

        /// <summary>
        /// How deep a limb settles into the snow it rests on. It lies ON the
        /// drift - the same lesson the stumps taught: a small thing grounded
        /// to the soil under a `0.45 m` field is a thing nobody ever sees.
        /// </summary>
        internal const float BranchSettle = 0.04f;

        private const float BranchSpacing = 1.1f;

        /// <summary>
        /// The lee window behind a rock panel, measured along the wall. The
        /// gale runs the west wall north to south, so a seedling survives
        /// immediately downwind of a buttress and nowhere else.
        /// </summary>
        internal const float LeeNear = 2f;

        /// <summary>The far edge of that lee.</summary>
        internal const float LeeFar = 10f;

        internal const float SouthKeepClear = 26f;
        internal const float NorthKeepClear = 30f;

        /// <summary>Fringe trees keep this far from the trodden network.</summary>
        internal const float WallTroddenClearance = 10f;

        /// <summary>Horizontal keep-out either side of the cableway line.</summary>
        internal const float CablewayKeepClear =
            AlpineVillageTerrainSampler.CablewayCutOuterHalfWidth + 5f;

        /// <summary>
        /// A fringe tree stays this far from the mother's house in bearing.
        /// The exterior camera frames `83` degrees across, so half of that
        /// keeps the west wall out of any shot that has the landmark centred.
        /// Applied to the FRINGE only: the forest surrounds the village, and a
        /// bearing rule measured from the station would forbid all of it.
        /// </summary>
        internal const float LandmarkSeparationDegrees = 41.5f;

        /// <summary>
        /// How far from the mother's house that rule still means anything.
        /// Standing on her threshold there is no such shot, and the bearing to
        /// a landmark you are already at is noise.
        /// </summary>
        internal const float LandmarkFramingDistance = 15f;

        /// <summary>Extra margin outside the canon station aperture, in metres.</summary>
        internal const float ApertureKeepClear = 6f;

        internal const int StumpCount = 24;
        internal const float MinimumStumpHeight = 0.5f;
        internal const float MaximumStumpHeight = 0.95f;

        /// <summary>
        /// How deep a foot is pushed under the sampled ground on the rise.
        ///
        /// On a `3.6:1` face the ground falls `1.4-2.4 m` across a trunk's own
        /// footprint, so a centre-sampled foot floats on the downhill side.
        /// Take the LOWEST of several samples, then bury.
        /// </summary>
        internal const float BuriedFoot = 0.35f;

        /// <summary>A tree already standing, and how much room its crown takes.</summary>
        private readonly struct PlantedSeat
        {
            public PlantedSeat(Vector2 point, float crownRadius)
            {
                Point = point;
                CrownRadius = crownRadius;
            }

            public Vector2 Point { get; }
            public float CrownRadius { get; }
        }

        private const int FootSamples = 5;
        private const float WallSpacing = 2f;
        private const float StumpSpacing = 1.8f;
        private const float WetGroundClearance = 1.5f;
        private const float StationClearance = 4f;

        /// <summary>
        /// Authored offsets along the wall, downwind of a panel centre: two
        /// clumps and one straggler, densest at the low southern end. The
        /// straggler sits UPWIND, where a tree should not have survived.
        /// </summary>
        private static readonly float[] FirstClumpLee = { -9.16f, -6.46f, -4.06f, -2.16f };

        private static readonly float[] SecondClumpLee = { -8.36f, -3.16f };

        private const float StragglerLee = 5.74f;

        private static readonly float[] FirstClumpOutward = { 4.55f, 5.05f, 4.70f, 5.15f };

        private static readonly float[] SecondClumpOutward = { 4.85f, 5.10f };

        private const float StragglerOutward = 4.40f;

        public static AlpineVillageTreePlan Create(AlpineVillagePlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            IReadOnlyList<AlpineVillagePathDescriptor> paths =
                AlpineVillagePathPlanner.Create(plan);
            IReadOnlyList<AlpineVillageRockPlacement> rock =
                AlpineVillageRockPlanner.Create(plan);
            List<float> panels = DescribeWestWallPanels(rock);

            // The clear cone to the mother's house, measured by the plan that
            // already owns it rather than re-derived here.
            AlpineVillagePeripheralStormPlan storm =
                AlpineVillagePeripheralStormPlan.Create(plan);

            var forest = new List<MountainRoadForestDescriptor>(ForestTreeCount);
            var wallTrees = new List<MountainRoadForestDescriptor>(8);
            var stumps = new List<MountainRoadForestDescriptor>(StumpCount);
            var branches = new List<AlpineVillageBranchDescriptor>(BranchCount);

            AppendForest(plan, paths, storm, forest);
            AppendWallTrees(plan, paths, rock, panels, storm, wallTrees);
            AppendStumps(plan, paths, storm, forest, stumps);
            AppendBranches(plan, paths, forest, stumps, branches);

            if (forest.Count < ForestTreeCount / 4)
            {
                throw new InvalidOperationException(
                    $"The village bowl grew only {forest.Count} trees. The " +
                    "clearing rules have swallowed the forest they were meant " +
                    "to cut a hole in.");
            }

            DescribeWindProfile(forest, wallTrees, out float footY, out float summitY);
            return new AlpineVillageTreePlan(
                forest.AsReadOnly(),
                wallTrees.AsReadOnly(),
                stumps.AsReadOnly(),
                branches.AsReadOnly(),
                footY,
                summitY);
        }

        /// <summary>
        /// The forest itself: a rejection scatter over the whole bowl floor,
        /// thinned to nothing wherever people actually walk.
        ///
        /// This is the same shape of solver the mountain road's forest uses -
        /// draw a point, roll against a retention curve, reject on clearances
        /// and spacing - but the retention here is driven by distance from the
        /// trodden network rather than by altitude, because what decides where
        /// a village forest stands is where the village cut it down.
        /// </summary>
        private static void AppendForest(
            AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillagePathDescriptor> paths,
            AlpineVillagePeripheralStormPlan storm,
            List<MountainRoadForestDescriptor> target)
        {
            Rect bounds = plan.TerrainBounds;
            var accepted = new List<PlantedSeat>(ForestTreeCount);
            var landmark = new Vector2(
                plan.MothersHouse.GroundCenter.x, plan.MothersHouse.GroundCenter.z);
            int attempts = ForestTreeCount * 60;
            for (int attempt = 0;
                attempt < attempts && target.Count < ForestTreeCount;
                attempt++)
            {
                uint hash = Mix((uint)plan.Seed ^ 0x464F5253u ^ (uint)attempt);
                var point = new Vector2(
                    Mathf.Lerp(bounds.xMin, bounds.xMax, Unit(hash, 0x58585858u)),
                    Mathf.Lerp(bounds.yMin, bounds.yMax, Unit(hash, 0x5A5A5A5Au)));
                float baseHeight = Mathf.Lerp(
                    ForestMinimumHeight, ForestMaximumHeight, Unit(hash, 0x48454947u));
                float scale = Mathf.Lerp(
                    SizeScaleMinimum, SizeScaleMaximum, Unit(hash, 0x5343414Cu));
                float height = baseHeight * scale;
                float radius = Mathf.Clamp(baseHeight * 0.22f, 0.9f, 2f) * scale;

                // The clearing, and its soft edge. Nothing inside the walked
                // village; full density once the ground stops being used.
                float trodden = AlpineVillagePathPlanner.MeasureDistanceOutsideTrodden(
                    plan, paths, point, out _);
                if (trodden < ForestClearing + radius)
                {
                    continue;
                }

                float retention = SmoothRange(
                    ForestClearing, ForestClearing + ForestThinBand, trodden);
                if (Unit(hash, 0x5448494Eu) > retention)
                {
                    continue;
                }

                if ((point - landmark).magnitude < LandmarkClearing ||
                    !ClearsTheStationAperture(storm, point, radius) ||
                    IsInsideCablewayCorridor(plan, point) ||
                    TouchesWater(plan, point) ||
                    AlpineVillageTerrainSampler.DistanceOutsideStation(
                        plan.Station, point) < StationClearance ||
                    !ClearsEveryPlot(plan, point, radius) ||
                    !HasCrownRoom(accepted, point, radius, ForestSpacing))
                {
                    continue;
                }

                accepted.Add(new PlantedSeat(point, radius));
                float foot = Mathf.Max(
                    AlpineVillageTerrainSampler.SampleHeight(plan, point),
                    AlpineVillageTerrainSampler.SampleMeshHeight(plan, point));
                target.Add(new MountainRoadForestDescriptor(
                    $"village-forest-{target.Count:000}",
                    MountainRoadForestLayer.Mid,
                    new Vector3(point.x, foot, point.y),
                    height,
                    radius,
                    Unit(hash, 0x59415721u) * 360f,
                    (int)(Mix(hash ^ 0x50414C45u) % 3u),
                    true));
            }
        }

        private static bool ClearsEveryPlot(
            AlpineVillagePlan plan, Vector2 point, float radius)
        {
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[index];
                if (plot.Kind == AlpineVillagePlotKind.Spring)
                {
                    continue;
                }

                if (AlpineVillageTerrainSampler.DistanceOutsidePlot(plot, point) <
                    radius + ForestPlotClearance)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The along-wall centres of the authored rock panels on the west wall,
        /// read back from the rock planner itself rather than recomputed. The
        /// panels ARE the anchor - a fringe keyed to a number of its own would
        /// drift the day the rock kit moves.
        /// </summary>
        private static List<float> DescribeWestWallPanels(
            IReadOnlyList<AlpineVillageRockPlacement> rock)
        {
            var result = new List<float>();
            for (int index = 0; index < rock.Count; index++)
            {
                Vector3 outward = rock[index].Rotation * Vector3.forward;
                if (outward.x > -0.9f)
                {
                    continue;
                }

                result.Add(rock[index].Position.z);
            }

            result.Sort();
            return result;
        }

        private static void AppendWallTrees(
            AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillagePathDescriptor> paths,
            IReadOnlyList<AlpineVillageRockPlacement> rock,
            IReadOnlyList<float> panels,
            AlpineVillagePeripheralStormPlan storm,
            List<MountainRoadForestDescriptor> target)
        {
            var accepted = new List<PlantedSeat>(8);
            int clump = 0;
            for (int index = 0; index < panels.Count && clump < 3; index++)
            {
                float panelZ = panels[index];
                if (!IsPanelEligible(plan, panelZ))
                {
                    continue;
                }

                float[] lee;
                float[] outward;
                if (clump == 0)
                {
                    lee = FirstClumpLee;
                    outward = FirstClumpOutward;
                }
                else if (clump == 1)
                {
                    lee = SecondClumpLee;
                    outward = SecondClumpOutward;
                }
                else
                {
                    lee = new[] { StragglerLee };
                    outward = new[] { StragglerOutward };
                }

                for (int seat = 0; seat < lee.Length; seat++)
                {
                    uint hash = Mix((uint)plan.Seed ^ 0x54524545u ^
                        (uint)(index * 19349663 + seat * 83492791));
                    float along = panelZ + lee[seat] +
                        Mathf.Lerp(-0.35f, 0.35f, Unit(hash, 0x414C4F4Eu));
                    var point = new Vector2(
                        plan.TerrainBounds.xMin - outward[seat],
                        along);
                    float baseHeight = Mathf.Lerp(2.9f, 5.4f, Unit(hash, 0x48454947u));
                    float scale = Mathf.Lerp(
                        SizeScaleMinimum, SizeScaleMaximum, Unit(hash, 0x5343414Cu));
                    float height = baseHeight * scale;
                    float radius =
                        Mathf.Clamp(baseHeight * 0.20f, 0.62f, 1.30f) * scale;
                    if (!IsWallSeatFree(plan, paths, rock, point, radius) ||
                        !HasCrownRoom(accepted, point, radius, WallSpacing))
                    {
                        continue;
                    }

                    float foot = GroundFoot(plan, point, radius);
                    var world = new Vector3(point.x, foot, point.y);
                    if (!ClearsTheStationAperture(storm, point, radius) ||
                        !ClearsTheLandmark(plan, world))
                    {
                        continue;
                    }

                    accepted.Add(new PlantedSeat(point, radius));
                    target.Add(new MountainRoadForestDescriptor(
                        $"village-wall-tree-{target.Count:000}",
                        MountainRoadForestLayer.Mid,
                        world,
                        height,
                        radius,
                        Unit(hash, 0x59415721u) * 360f,
                        (int)(Mix(hash ^ 0x50414C45u) % 3u),
                        false));
                }

                clump++;
            }
        }

        /// <summary>
        /// Cut stubs at the inner edge of the forest, on the floor the village
        /// walks past.
        ///
        /// Their height is not drawn from a range - it is measured up from the
        /// snow actually lying on that spot. A stub short enough to read as a
        /// stub is shorter than the `0.45 m` untouched field, so any constant
        /// would put some of them under it; this way every one of them stands
        /// proud by construction, and the deep-field ones are simply the ones
        /// that were cut highest, which is how you cut in snow.
        /// </summary>
        private static void AppendStumps(
            AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillagePathDescriptor> paths,
            AlpineVillagePeripheralStormPlan storm,
            IReadOnlyList<MountainRoadForestDescriptor> forest,
            List<MountainRoadForestDescriptor> target)
        {
            Rect bounds = plan.TerrainBounds;
            var accepted = new List<PlantedSeat>(StumpCount);
            var landmark = new Vector2(
                plan.MothersHouse.GroundCenter.x, plan.MothersHouse.GroundCenter.z);
            int attempts = StumpCount * 200;
            for (int attempt = 0;
                attempt < attempts && target.Count < StumpCount;
                attempt++)
            {
                uint hash = Mix((uint)plan.Seed ^ 0x53545550u ^ (uint)attempt);
                var point = new Vector2(
                    Mathf.Lerp(bounds.xMin, bounds.xMax, Unit(hash, 0x58585858u)),
                    Mathf.Lerp(bounds.yMin, bounds.yMax, Unit(hash, 0x5A5A5A5Au)));
                float trodden = AlpineVillagePathPlanner.MeasureDistanceOutsideTrodden(
                    plan, paths, point, out _);
                if (trodden < StumpTroddenNear || trodden > StumpTroddenFar)
                {
                    continue;
                }

                float radius = Mathf.Lerp(0.28f, 0.5f, Unit(hash, 0x52414449u));
                if ((point - landmark).magnitude < LandmarkClearing ||
                    !ClearsTheStationAperture(storm, point, radius) ||
                    IsInsideCablewayCorridor(plan, point) ||
                    TouchesWater(plan, point) ||
                    AlpineVillageTerrainSampler.DistanceOutsideStation(
                        plan.Station, point) < StationClearance ||
                    !ClearsEveryPlot(plan, point, radius) ||
                    !HasCrownRoom(accepted, point, radius, 2f) ||
                    !ClearsEveryTrunk(forest, point, radius + StumpTrunkClearance))
                {
                    continue;
                }

                // The whole point: measured up from the drift that lies here.
                float snow = AlpineVillageSnowDrift.SampleDepth(plan, paths, point);
                float height = StumpBuriedFoot + snow + Mathf.Lerp(
                    StumpExposure,
                    StumpExposure + StumpExposureSpread,
                    Unit(hash, 0x48454947u));

                accepted.Add(new PlantedSeat(point, radius));
                float ground = Mathf.Max(
                    AlpineVillageTerrainSampler.SampleHeight(plan, point),
                    AlpineVillageTerrainSampler.SampleMeshHeight(plan, point));
                target.Add(new MountainRoadForestDescriptor(
                    $"village-stump-{target.Count:000}",
                    MountainRoadForestLayer.Mid,
                    new Vector3(point.x, ground - StumpBuriedFoot, point.y),
                    height,
                    radius,
                    Unit(hash, 0x59415721u) * 360f,
                    (int)(Mix(hash ^ 0x50414C45u) % 3u),
                    true));
            }
        }

        /// <summary>
        /// Deadfall between the trees.
        ///
        /// A limb is drawn from a TREE rather than from the bowl: pick a
        /// standing trunk, throw the limb a few metres from it, and let the
        /// clearances reject it. That way the litter belongs to the wood that
        /// dropped it instead of reading as evenly sown ground cover.
        ///
        /// It rests on the snow surface. Grounding it to the soil under a
        /// `0.45 m` field would hide every one of them, which is exactly the
        /// mistake the stumps made on the rise.
        /// </summary>
        private static void AppendBranches(
            AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillagePathDescriptor> paths,
            IReadOnlyList<MountainRoadForestDescriptor> forest,
            IReadOnlyList<MountainRoadForestDescriptor> stumps,
            List<AlpineVillageBranchDescriptor> target)
        {
            if (forest.Count == 0)
            {
                return;
            }

            var accepted = new List<Vector2>(BranchCount);
            int attempts = BranchCount * 40;
            for (int attempt = 0;
                attempt < attempts && target.Count < BranchCount;
                attempt++)
            {
                uint hash = Mix((uint)plan.Seed ^ 0x42524348u ^ (uint)attempt);
                MountainRoadForestDescriptor parent = forest[
                    (int)(Mix(hash ^ 0x50415245u) % (uint)forest.Count)];
                float bearing = Unit(hash, 0x42454152u) * Mathf.PI * 2f;
                float reach = Mathf.Lerp(
                    BranchTrunkStandoff,
                    BranchReachFromTrunk,
                    Unit(hash, 0x52454143u));
                var point = new Vector2(
                    parent.Position.x + Mathf.Cos(bearing) * reach,
                    parent.Position.z + Mathf.Sin(bearing) * reach);

                float length = Mathf.Lerp(
                    BranchMinimumLength, BranchMaximumLength, Unit(hash, 0x4C454E47u));
                float thickness = Mathf.Lerp(
                    BranchMinimumThickness,
                    BranchMaximumThickness,
                    Unit(hash, 0x5448434Bu));
                float yaw = Unit(hash, 0x59415721u) * 360f;
                if (AlpineVillagePathPlanner.MeasureDistanceOutsideTrodden(
                        plan, paths, point, out _) < BranchTroddenClearance ||
                    TouchesWater(plan, point) ||
                    !ClearsEveryPlot(plan, point, length * 0.5f) ||
                    !StaysOnWalkableGround(plan, point, length * 0.5f) ||
                    !HasSpacing(accepted, point, BranchSpacing))
                {
                    continue;
                }

                // A limb is long, so testing where it is PINNED is not enough:
                // one lying beside a trunk still runs straight through it. Walk
                // its own axis instead, against every trunk and every stub.
                if (!LimbClearsTheWood(forest, stumps, point, yaw, length, thickness))
                {
                    continue;
                }

                accepted.Add(point);
                float ground = Mathf.Max(
                    AlpineVillageTerrainSampler.SampleHeight(plan, point),
                    AlpineVillageTerrainSampler.SampleMeshHeight(plan, point));
                float snow = AlpineVillageSnowDrift.SampleDepth(plan, paths, point);
                target.Add(new AlpineVillageBranchDescriptor(
                    $"village-branch-{target.Count:000}",
                    new Vector3(point.x, ground + snow - BranchSettle, point.y),
                    yaw,
                    Mathf.Lerp(-12f, 12f, Unit(hash, 0x54494C54u)),
                    length,
                    thickness,
                    1 + (int)(Mix(hash ^ 0x464F524Bu) % 2u)));
            }
        }

        /// <summary>
        /// The limb, sampled along its own axis, clear of every standing trunk
        /// and every stub.
        /// </summary>
        private static bool LimbClearsTheWood(
            IReadOnlyList<MountainRoadForestDescriptor> forest,
            IReadOnlyList<MountainRoadForestDescriptor> stumps,
            Vector2 point,
            float yawDegrees,
            float length,
            float thickness)
        {
            float radians = yawDegrees * Mathf.Deg2Rad;
            var axis = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
            const int Samples = 5;
            for (int sample = 0; sample < Samples; sample++)
            {
                Vector2 along = point + axis *
                    Mathf.Lerp(-length * 0.5f, length * 0.5f, sample / (float)(Samples - 1));
                if (!ClearsEveryTrunk(forest, along, thickness) ||
                    !ClearsEveryTrunk(stumps, along, thickness))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Deadfall stays on ground the mask calls walkable, so a limb never
        /// ends up outside the bowl the hero can reach.
        /// </summary>
        private static bool StaysOnWalkableGround(
            AlpineVillagePlan plan, Vector2 point, float reach)
        {
            Rect bounds = plan.TerrainBounds;
            float outset = AlpineVillageWalkableArea.GroundOutset;
            return point.x >= bounds.xMin - outset + reach &&
                point.x <= bounds.xMax + outset - reach &&
                point.y >= bounds.yMin - outset + reach &&
                point.y <= bounds.yMax + outset - reach;
        }

        private static bool ClearsEveryTrunk(
            IReadOnlyList<MountainRoadForestDescriptor> trees,
            Vector2 point,
            float clearance)
        {
            for (int index = 0; index < trees.Count; index++)
            {
                MountainRoadForestDescriptor tree = trees[index];
                Vector3 position = tree.Position;
                var delta = new Vector2(point.x - position.x, point.y - position.z);
                float required = tree.TrunkRadius + clearance;
                if (delta.sqrMagnitude < required * required)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsPanelEligible(AlpineVillagePlan plan, float panelZ)
        {
            return panelZ - LeeFar >= plan.TerrainBounds.yMin + SouthKeepClear &&
                panelZ + StragglerLee <= plan.TerrainBounds.yMax - NorthKeepClear;
        }

        /// <summary>
        /// A seat on the rise: outside the walkable mask by construction, clear
        /// of the cableway cut, the station shelf, the water and the stone.
        /// </summary>
        private static bool IsWallSeatFree(
            AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillagePathDescriptor> paths,
            IReadOnlyList<AlpineVillageRockPlacement> rock,
            Vector2 point,
            float radius)
        {
            // The rock is cleared by the TRUNK, not by the crown. The band is
            // only a metre inboard of the panel foot while a crown reaches
            // further, so measuring the crown would reject every seat - and a
            // crown brushing stone is what a tree on a cliff looks like. A
            // trunk inside the stone is the fault.
            if (IsInsideCablewayCorridor(plan, point) ||
                IsInsideRock(rock, point, Mathf.Clamp(radius * 0.16f, 0.18f, 0.46f)) ||
                TouchesWater(plan, point) ||
                AlpineVillageTerrainSampler.DistanceOutsideStation(
                    plan.Station, point) < StationClearance)
            {
                return false;
            }

            return AlpineVillagePathPlanner.MeasureDistanceOutsideTrodden(
                plan, paths, point, out _) >= WallTroddenClearance;
        }

        private static bool IsInsideCablewayCorridor(
            AlpineVillagePlan plan, Vector2 point)
        {
            MountainRoadCablewayPlan cableway = plan.Station.Cableway;
            var origin = new Vector2(
                cableway.StationArea.Center.x, cableway.StationArea.Center.z);
            var right = new Vector2(
                cableway.LineRight.x, cableway.LineRight.z).normalized;
            var forward = new Vector2(
                cableway.LineForward.x, cableway.LineForward.z).normalized;
            Vector2 delta = point - origin;
            return Vector2.Dot(delta, forward) > 0f &&
                Mathf.Abs(Vector2.Dot(delta, right)) <= CablewayKeepClear;
        }

        private static bool TouchesWater(AlpineVillagePlan plan, Vector2 point)
        {
            return plan.Brook != null &&
                plan.Brook.DistanceOutsideWetGround(point) < WetGroundClearance;
        }

        private static bool IsInsideRock(
            IReadOnlyList<AlpineVillageRockPlacement> rock,
            Vector2 point,
            float radius)
        {
            for (int index = 0; index < rock.Count; index++)
            {
                AlpineVillageRockPlacement placement = rock[index];
                Vector3 forward3 = placement.Rotation * Vector3.forward;
                var forward = new Vector2(forward3.x, forward3.z).normalized;
                var right = new Vector2(forward.y, -forward.x);
                var delta = new Vector2(
                    point.x - placement.Position.x,
                    point.y - placement.Position.z);
                float depth = Vector2.Dot(delta, forward);
                if (Mathf.Abs(Vector2.Dot(delta, right)) <=
                        VillageRockAssetProvider.HalfWidth + radius &&
                    depth >= -radius &&
                    depth <= VillageRockAssetProvider.Depth + radius)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The canon aperture: the widening clear cone from the station to the
        /// mother's house that encloses the whole house. This, not a bearing
        /// cone, is what the bibles protect, and the peripheral storm already
        /// measures it - so the trees read its geometry rather than inventing
        /// a second one.
        /// </summary>
        private static bool ClearsTheStationAperture(
            AlpineVillagePeripheralStormPlan storm, Vector2 point, float radius)
        {
            Vector2 delta = point - storm.ApertureStart;
            float along = Vector2.Dot(delta, storm.ApertureDirection);
            if (along < 0f || along > storm.ApertureCoreLength + ApertureKeepClear)
            {
                return true;
            }

            var across = new Vector2(
                storm.ApertureDirection.y, -storm.ApertureDirection.x);
            float halfWidth = Mathf.Lerp(
                AlpineVillagePeripheralStormRules.ApertureNearHalfWidth,
                storm.ApertureFarHalfWidth,
                Mathf.Clamp01(along / Mathf.Max(0.01f, storm.ApertureCoreLength)));
            return Mathf.Abs(Vector2.Dot(delta, across)) >
                halfWidth + radius + ApertureKeepClear;
        }

        /// <summary>
        /// The landmark keeps its frame to itself, for the FRINGE only. The
        /// forest surrounds the village and cannot answer a bearing rule
        /// measured from the station without vanishing.
        /// </summary>
        private static bool ClearsTheLandmark(AlpineVillagePlan plan, Vector3 foot)
        {
            var tree = new Vector2(foot.x, foot.z);
            var landmark = new Vector2(
                plan.MothersHouse.GroundCenter.x, plan.MothersHouse.GroundCenter.z);
            var station = new Vector2(
                plan.Station.PadArea.Center.x, plan.Station.PadArea.Center.z);
            if (!SeparatedInBearing(station, tree, landmark))
            {
                return false;
            }

            int steps = Mathf.Max(1, Mathf.CeilToInt(plan.Lane.Length));
            for (int step = 0; step <= steps; step++)
            {
                AlpineVillageLaneSample sample = plan.Lane.Sample(
                    plan.Lane.Length * step / steps);
                var eye = new Vector2(sample.Position.x, sample.Position.z);
                if (!SeparatedInBearing(eye, tree, landmark))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SeparatedInBearing(
            Vector2 eye, Vector2 tree, Vector2 landmark)
        {
            Vector2 toTree = tree - eye;
            Vector2 toLandmark = landmark - eye;
            if (toTree.sqrMagnitude < 0.0001f ||
                toLandmark.magnitude < LandmarkFramingDistance)
            {
                return true;
            }

            return Vector2.Angle(toTree, toLandmark) >= LandmarkSeparationDegrees;
        }

        /// <summary>
        /// Never bare <c>Mathf.SmoothStep(a, b, v)</c>: that interpolates
        /// BETWEEN a and b instead of normalising v across them, and the
        /// sampler carries a comment about the day this cost real geometry.
        /// </summary>
        private static float SmoothRange(float start, float end, float value)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, value));
        }

        /// <summary>
        /// Grounds a vertical trunk on a `74` degree face. Sampling the centre
        /// alone floats the downhill side by half the fall across the trunk's
        /// own width; taking the lowest sample and burying puts the visible,
        /// village-facing side flush.
        /// </summary>
        private static float GroundFoot(
            AlpineVillagePlan plan, Vector2 point, float radius)
        {
            float trunkRadius = Mathf.Clamp(radius * 0.16f, 0.18f, 0.46f);
            float lowest = float.PositiveInfinity;
            for (int sample = 0; sample < FootSamples; sample++)
            {
                float offset = Mathf.Lerp(
                    -trunkRadius, trunkRadius, sample / (float)(FootSamples - 1));
                var foot = new Vector2(point.x + offset, point.y);
                lowest = Mathf.Min(lowest, Mathf.Max(
                    AlpineVillageTerrainSampler.SampleHeight(plan, foot),
                    AlpineVillageTerrainSampler.SampleMeshHeight(plan, foot)));
            }

            return lowest - BuriedFoot;
        }

        private static void DescribeWindProfile(
            IReadOnlyList<MountainRoadForestDescriptor> forest,
            IReadOnlyList<MountainRoadForestDescriptor> wallTrees,
            out float footY,
            out float summitY)
        {
            footY = float.PositiveInfinity;
            summitY = float.NegativeInfinity;
            for (int index = 0; index < forest.Count; index++)
            {
                footY = Mathf.Min(footY, forest[index].Position.y);
                summitY = Mathf.Max(summitY, forest[index].Position.y);
            }

            for (int index = 0; index < wallTrees.Count; index++)
            {
                footY = Mathf.Min(footY, wallTrees[index].Position.y);
                summitY = Mathf.Max(summitY, wallTrees[index].Position.y);
            }

            // The shader normalises a tree's own base between these two, so a
            // degenerate range would divide the amplitude term by nothing.
            if (summitY - footY < 1f)
            {
                summitY = footY + 1f;
            }
        }

        /// <summary>
        /// Room for one more crown. Two trees may stand no closer than the sum
        /// of the two crown radii plus a gap - so a big tree pushes its
        /// neighbours away in proportion to how big it actually grew - and
        /// never closer than the trunk floor whatever their size.
        /// </summary>
        private static bool HasCrownRoom(
            IReadOnlyList<PlantedSeat> accepted,
            Vector2 point,
            float crownRadius,
            float floor)
        {
            for (int index = 0; index < accepted.Count; index++)
            {
                PlantedSeat seat = accepted[index];
                float required = Mathf.Max(
                    seat.CrownRadius + crownRadius + CrownGap, floor);
                if ((seat.Point - point).sqrMagnitude < required * required)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasSpacing(
            IReadOnlyList<Vector2> accepted, Vector2 point, float spacing)
        {
            float square = spacing * spacing;
            for (int index = 0; index < accepted.Count; index++)
            {
                if ((accepted[index] - point).sqrMagnitude < square)
                {
                    return false;
                }
            }

            return true;
        }

        private static float Unit(uint hash, uint salt)
        {
            return (Mix(hash ^ salt) & 0xFFFFFFu) / 16777215f;
        }

        private static uint Mix(uint value)
        {
            unchecked
            {
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                return value ^ (value >> 16);
            }
        }
    }
}
