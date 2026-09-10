using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The village's conifers, as pure data. They are the mountain road's own
    /// trees - same descriptor, same crown generator, same three silhouettes -
    /// standing where altitude and wind leave them, not where a forest would.
    ///
    /// Three groups, and they are not interchangeable:
    ///
    /// <list type="bullet">
    /// <item>Wall trees stand ON the enclosing rise, outside the walkable mask.
    /// Nothing can reach them, so they carry no obstacle and no collider.</item>
    /// <item>Stumps stand lower on the same rise, still outside the mask. The
    /// 74 degree face holds no snow, so a cut stub reads instead of drowning in
    /// the knee-deep field the bowl floor carries.</item>
    /// <item>Copse trees stand on walkable ground behind the firewood house.
    /// These, and only these, are obstacles - and they are obstacles of the
    /// walkable MASK, never of physics. A real collider would graze the hero,
    /// and a graze zeroes his planar speed, which is exactly what the pinned
    /// art-bible check forbids: leaving the path must not change his speed.
    /// The mask slides him around a footprint instead, the way it already does
    /// around a house wall.</item>
    /// </list>
    /// </summary>
    public sealed class AlpineVillageTreePlan
    {
        internal AlpineVillageTreePlan(
            IReadOnlyList<MountainRoadForestDescriptor> wallTrees,
            IReadOnlyList<MountainRoadForestDescriptor> stumps,
            IReadOnlyList<MountainRoadForestDescriptor> copseTrees,
            float windFootY,
            float windSummitY)
        {
            WallTrees = wallTrees;
            Stumps = stumps;
            CopseTrees = copseTrees;
            WindFootY = windFootY;
            WindSummitY = windSummitY;
        }

        /// <summary>Crowned trees on the enclosing rise. Never reachable.</summary>
        public IReadOnlyList<MountainRoadForestDescriptor> WallTrees { get; }

        /// <summary>Cut stubs low on the same rise. Crownless, never reachable.</summary>
        public IReadOnlyList<MountainRoadForestDescriptor> Stumps { get; }

        /// <summary>
        /// Crowned trees on walkable ground behind the firewood house. These
        /// carry <c>BlocksMovement</c>, and the walkable mask carves them out.
        /// </summary>
        public IReadOnlyList<MountainRoadForestDescriptor> CopseTrees { get; }

        /// <summary>
        /// Lowest and highest foot in the planted stand, for the wind profile.
        ///
        /// Not the lane's own start and end: the lane climbs `6.4 m` while the
        /// wall trees stand `5-8 m` up a `74` degree face, so on the lane's
        /// scale every tree would saturate the shader's climb term and the
        /// amplitude it exists for would collapse to a constant.
        /// </summary>
        public float WindFootY { get; }

        /// <summary>The high end of that same range.</summary>
        public float WindSummitY { get; }

        /// <summary>Every tree that carries a crown, wall and copse together.</summary>
        public IEnumerable<MountainRoadForestDescriptor> CrownedTrees
        {
            get
            {
                for (int index = 0; index < WallTrees.Count; index++)
                {
                    yield return WallTrees[index];
                }

                for (int index = 0; index < CopseTrees.Count; index++)
                {
                    yield return CopseTrees[index];
                }
            }
        }
    }

    /// <summary>
    /// Places the village's conifers. Deterministic, and for the wall groups
    /// deliberately seed-INDEPENDENT: seven trees that carry a reading must not
    /// wander between seeds, exactly as the authored rock panels do not. The
    /// seed picks crown variant, yaw and a small jitter, nothing else.
    /// </summary>
    internal static class AlpineVillageTreePlanner
    {
        /// <summary>
        /// The wall band, in metres outside <c>TerrainBounds</c>.
        ///
        /// Three separate things close it. The authored rock panels stand from
        /// `5.4 m` outward, so a trunk further out would be inside authored
        /// stone. A tip must stay below `20 m` over the bowl floor, leaving
        /// `40 m` of wall above it. And a root must stand above `5 m` up the
        /// face, so that nobody at the toe can reach the tree.
        /// </summary>
        internal const float WallBandInner = 4.40f;

        /// <summary>The far edge of that band; `0.25 m` inboard of the panel foot.</summary>
        internal const float WallBandOuter = 5.15f;

        /// <summary>Stumps sit below the trees, still clear of the walkable toe.</summary>
        internal const float StumpBandInner = 3.10f;

        /// <summary>...and below the height a tree could hold.</summary>
        internal const float StumpBandOuter = 4.90f;

        /// <summary>
        /// The lee window behind a rock panel, measured along the wall from the
        /// panel's centre. The gale runs the west wall north to south, so a
        /// seedling survives immediately downwind of a buttress and nowhere
        /// else. This is the whole reason the trees are where they are.
        /// </summary>
        internal const float LeeNear = 2f;

        /// <summary>The far edge of that lee.</summary>
        internal const float LeeFar = 10f;

        /// <summary>Nothing within this of the bowl's southern edge: arrival and cut.</summary>
        internal const float SouthKeepClear = 26f;

        /// <summary>Nothing within this of the northern edge: the mother's end.</summary>
        internal const float NorthKeepClear = 30f;

        /// <summary>No tree closer than this to the lane's own axis.</summary>
        internal const float LaneKeepClear = 10f;

        /// <summary>Horizontal keep-out either side of the cableway line.</summary>
        internal const float CablewayKeepClear =
            AlpineVillageTerrainSampler.CablewayCutOuterHalfWidth + 5f;

        /// <summary>A tree may never fill more of the view than this.</summary>
        internal const float MaximumSubtendedDegrees = 12.5f;

        /// <summary>
        /// A tree must stay this far from the mother's house in bearing, from
        /// the station and from every metre of the lane. The exterior camera
        /// frames `83` degrees across, so `41.5` is exactly half of it: no tree
        /// can share a frame that has the landmark centred.
        /// </summary>
        internal const float LandmarkSeparationDegrees = 41.5f;

        /// <summary>
        /// How far from the mother's house the rule still means anything.
        ///
        /// It exists to protect one composition: the warm house as a distant
        /// target at the end of the rising axis. Standing on her threshold
        /// there is no such shot - the bearing to a landmark you are already
        /// at is noise, and enforcing a separation from it would forbid the
        /// whole bowl for no gain.
        /// </summary>
        internal const float LandmarkFramingDistance = 15f;

        /// <summary>Extra margin outside the canon aperture, in metres.</summary>
        internal const float ApertureKeepClear = 6f;

        /// <summary>Registry cap: seven on the wall.</summary>
        internal const int WallTreeCount = 7;

        /// <summary>Registry cap: no more than twenty-four cut stubs.</summary>
        internal const int StumpCount = 24;

        /// <summary>Registry cap: no more than nine in the copse.</summary>
        internal const int CopseTreeCount = 9;

        internal const float MinimumHeight = 2.9f;
        internal const float MaximumHeight = 5.4f;
        internal const float MinimumStumpHeight = 0.5f;
        internal const float MaximumStumpHeight = 0.95f;

        /// <summary>
        /// How deep a foot is pushed under the sampled ground.
        ///
        /// On a `3.6:1` face the ground falls `1.4-2.4 m` across a trunk's own
        /// footprint, so a centre-sampled foot floats on the downhill side.
        /// The answer is the rock planner's: take the LOWEST of several samples
        /// across the footprint, then bury. `2.5 m` is sized for a `49 m`
        /// authored mass; a `5 m` tree needs only enough to hide the box cap
        /// under the mesh chord and the PS1 vertex snap.
        /// </summary>
        internal const float BuriedFoot = 0.35f;

        private const int FootSamples = 5;
        private const float WallSpacing = 2f;
        private const float CopseSpacing = 2.2f;
        private const float StumpSpacing = 1.8f;

        /// <summary>Clear corridor kept behind the firewood house.</summary>
        private const float CopseStandoff = 3f;

        /// <summary>
        /// How far the copse reaches back from that corridor. The near part of
        /// the band is spent on the plot apron every tree has to clear, so the
        /// reach has to be deeper than the clump itself needs.
        /// </summary>
        private const float CopseReach = 7.5f;

        private const float WetGroundClearance = 1.5f;
        private const float StationClearance = 4f;
        private const float PlotClearance = 1.6f;
        private const float TroddenClearance = 2.6f;

        /// <summary>
        /// Authored offsets along the wall, downwind of a panel centre. Two
        /// clumps and one straggler: `4 / 2 / 1`, densest at the low southern
        /// end so the thinning runs along the composition's own uphill axis
        /// instead of competing with it. The straggler sits UPWIND, where a
        /// tree should not have survived, which is what turns two clumps into
        /// a gradient and breaks the panel rhythm.
        /// </summary>
        private static readonly float[] FirstClumpLee = { -9.16f, -6.46f, -4.06f, -2.16f };

        private static readonly float[] SecondClumpLee = { -8.36f, -3.16f };

        private const float StragglerLee = 5.74f;

        /// <summary>
        /// Outward offsets paired with those, so the clump has depth on the
        /// face rather than standing on one contour.
        /// </summary>
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
            List<float> panels = DescribeWestWallPanels(plan, rock);

            // The clear cone to the mother's house, measured by the plan that
            // already owns it rather than re-derived here.
            AlpineVillagePeripheralStormPlan storm =
                AlpineVillagePeripheralStormPlan.Create(plan);

            var wallTrees = new List<MountainRoadForestDescriptor>(WallTreeCount);
            var stumps = new List<MountainRoadForestDescriptor>(StumpCount);
            var copse = new List<MountainRoadForestDescriptor>(CopseTreeCount);

            var tally = new int[Enum.GetValues(typeof(CopseRefusal)).Length];
            AppendWallTrees(plan, paths, rock, panels, storm, wallTrees);
            AppendStumps(plan, paths, rock, panels, stumps);
            AppendCopse(plan, paths, storm, copse, tally);

            if (wallTrees.Count == 0)
            {
                throw new InvalidOperationException(
                    "The village bowl left no lee pocket standing: the wall " +
                    "carries no tree, and the registry row promises seven.");
            }

            if (copse.Count == 0)
            {
                throw new InvalidOperationException(
                    "No room behind the firewood house for the accepted copse. " +
                    "Seats refused by: " + DescribeRefusals(tally));
            }

            DescribeWindProfile(wallTrees, copse, out float footY, out float summitY);
            return new AlpineVillageTreePlan(
                wallTrees.AsReadOnly(),
                stumps.AsReadOnly(),
                copse.AsReadOnly(),
                footY,
                summitY);
        }

        /// <summary>
        /// The along-wall centres of the authored rock panels on the west wall,
        /// read back from the rock planner itself rather than recomputed. The
        /// panels ARE the anchor - a tree keyed to a number of its own would
        /// drift the day the rock kit moves.
        /// </summary>
        private static List<float> DescribeWestWallPanels(
            AlpineVillagePlan plan,
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
            var accepted = new List<Vector2>(WallTreeCount);
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
                    float height = Mathf.Lerp(
                        MinimumHeight, MaximumHeight, Unit(hash, 0x48454947u));
                    float radius = Mathf.Clamp(height * 0.20f, 0.62f, 1.30f);
                    if (!IsWallSeatFree(plan, paths, rock, point, radius) ||
                        !HasSpacing(accepted, point, WallSpacing))
                    {
                        continue;
                    }

                    float foot = GroundFoot(plan, point, radius);
                    var world = new Vector3(point.x, foot, point.y);
                    if (!ClearsTheStationAperture(storm, point, radius) ||
                        !ClearsTheLandmark(plan, world, height) ||
                        !StaysSmall(plan, world, height))
                    {
                        continue;
                    }

                    accepted.Add(point);
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
        /// Cut stubs across the base of the same wall. They share ONE threshold
        /// with the trees - <c>SAW</c> below - so the felled line is never a
        /// horizontal band: below a man's reach there are only stumps, above it
        /// only trees, and the two overlap in between.
        /// </summary>
        private static void AppendStumps(
            AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillagePathDescriptor> paths,
            IReadOnlyList<AlpineVillageRockPlacement> rock,
            IReadOnlyList<float> panels,
            List<MountainRoadForestDescriptor> target)
        {
            if (panels.Count == 0)
            {
                return;
            }

            var accepted = new List<Vector2>(StumpCount);
            float south = plan.TerrainBounds.yMin + SouthKeepClear;
            float north = plan.TerrainBounds.yMax - NorthKeepClear;
            int attempts = StumpCount * 40;
            for (int attempt = 0;
                attempt < attempts && target.Count < StumpCount;
                attempt++)
            {
                uint hash = Mix((uint)plan.Seed ^ 0x53545550u ^ (uint)attempt);
                float along = Mathf.Lerp(south, north, Unit(hash, 0x414C4F4Eu));
                float outward = Mathf.Lerp(
                    StumpBandInner, StumpBandOuter, Unit(hash, 0x4F464653u));
                var point = new Vector2(plan.TerrainBounds.xMin - outward, along);
                float rise = AlpineVillageTerrainSampler.SampleRidgeRise(plan, point);
                if (Unit(hash, 0x53415721u) > 1f - Saw(rise))
                {
                    continue;
                }

                float height = Mathf.Lerp(
                    MinimumStumpHeight, MaximumStumpHeight, Unit(hash, 0x48454947u));
                float radius = Mathf.Lerp(0.22f, 0.40f, Unit(hash, 0x52414449u));
                if (!IsWallSeatFree(plan, paths, rock, point, radius) ||
                    !HasSpacing(accepted, point, StumpSpacing))
                {
                    continue;
                }

                accepted.Add(point);
                target.Add(new MountainRoadForestDescriptor(
                    $"village-wall-stump-{target.Count:000}",
                    MountainRoadForestLayer.Mid,
                    new Vector3(point.x, GroundFoot(plan, point, radius), point.y),
                    height,
                    radius,
                    Unit(hash, 0x59415721u) * 360f,
                    (int)(Mix(hash ^ 0x50414C45u) % 3u),
                    false));
            }
        }

        /// <summary>
        /// The copse behind the firewood house. It stands on ground the hero
        /// walks, so every trunk becomes a mask obstacle - and the mask must
        /// still leave him a way round all four walls of that house, which is
        /// why the corridor behind it is kept clear before anything is planted.
        /// </summary>
        private static void AppendCopse(
            AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillagePathDescriptor> paths,
            AlpineVillagePeripheralStormPlan storm,
            List<MountainRoadForestDescriptor> target,
            int[] tally)
        {
            AlpineVillagePlotDescriptor house = default;
            bool found = false;
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                if (plan.Plots[index].StableId != AlpineVillageLifePlan.WoodHouseId)
                {
                    continue;
                }

                house = plan.Plots[index];
                found = true;
                break;
            }

            if (!found)
            {
                return;
            }

            var facing = new Vector2(house.Facing.x, house.Facing.z).normalized;
            var across = new Vector2(-facing.y, facing.x);
            var centre = new Vector2(house.GroundCenter.x, house.GroundCenter.z);
            float behind = house.FootprintSize.y * 0.5f + CopseStandoff;
            var accepted = new List<Vector2>(CopseTreeCount);
            int attempts = CopseTreeCount * 60;
            for (int attempt = 0;
                attempt < attempts && target.Count < CopseTreeCount;
                attempt++)
            {
                uint hash = Mix((uint)plan.Seed ^ 0x434F5053u ^ (uint)attempt);
                float depth = behind + Unit(hash, 0x4F464653u) * CopseReach;
                float side = Mathf.Lerp(
                    -house.FootprintSize.x * 0.75f,
                    house.FootprintSize.x * 0.75f,
                    Unit(hash, 0x53494445u));
                Vector2 point = centre - facing * depth + across * side;
                float height = Mathf.Lerp(
                    MinimumHeight, MaximumHeight, Unit(hash, 0x48454947u));
                float radius = Mathf.Clamp(height * 0.20f, 0.62f, 1.30f);
                CopseRefusal refusal = IsCopseSeatFree(plan, paths, point, radius);
                if (refusal != CopseRefusal.None)
                {
                    tally[(int)refusal]++;
                    continue;
                }

                if (!HasSpacing(accepted, point, CopseSpacing))
                {
                    tally[(int)CopseRefusal.Spacing]++;
                    continue;
                }

                float foot = Mathf.Max(
                    AlpineVillageTerrainSampler.SampleHeight(plan, point),
                    AlpineVillageTerrainSampler.SampleMeshHeight(plan, point));
                var world = new Vector3(point.x, foot, point.y);
                if (!ClearsTheStationAperture(storm, point, radius))
                {
                    tally[(int)CopseRefusal.Landmark]++;
                    continue;
                }

                if (!StaysSmall(plan, world, height))
                {
                    tally[(int)CopseRefusal.TooLarge]++;
                    continue;
                }

                accepted.Add(point);
                target.Add(new MountainRoadForestDescriptor(
                    $"village-copse-tree-{target.Count:000}",
                    MountainRoadForestLayer.Mid,
                    world,
                    height,
                    radius,
                    Unit(hash, 0x59415721u) * 360f,
                    (int)(Mix(hash ^ 0x50414C45u) % 3u),
                    true));
            }
        }

        private static string DescribeRefusals(int[] tally)
        {
            var text = new System.Text.StringBuilder();
            for (int index = 1; index < tally.Length; index++)
            {
                if (tally[index] == 0)
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text.Append(", ");
                }

                text.Append((CopseRefusal)index).Append(' ').Append(tally[index]);
            }

            return text.Length == 0 ? "nothing - no candidate was generated" : text.ToString();
        }

        private static bool IsPanelEligible(AlpineVillagePlan plan, float panelZ)
        {
            return panelZ - LeeFar >= plan.TerrainBounds.yMin + SouthKeepClear &&
                panelZ + StragglerLee <= plan.TerrainBounds.yMax - NorthKeepClear;
        }

        /// <summary>
        /// A seat on the rise: outside the walkable mask by construction, clear
        /// of the cableway cut, the station shelf, the water and the authored
        /// stone.
        /// </summary>
        private static bool IsWallSeatFree(
            AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillagePathDescriptor> paths,
            IReadOnlyList<AlpineVillageRockPlacement> rock,
            Vector2 point,
            float radius)
        {
            // The rock is cleared by the TRUNK, not by the crown. The planting
            // band is only `0.25-1.0 m` inboard of the panel foot while a crown
            // reaches `1.3 m`, so measuring the crown would reject every seat
            // on the wall - and a crown brushing stone is what a tree on a
            // cliff looks like anyway. A trunk inside the stone is the fault.
            if (IsInsideCablewayCorridor(plan, point) ||
                IsInsideRock(rock, point, Mathf.Clamp(radius * 0.16f, 0.18f, 0.46f)) ||
                TouchesWater(plan, point) ||
                AlpineVillageTerrainSampler.DistanceOutsideStation(
                    plan.Station, point) < StationClearance)
            {
                return false;
            }

            return AlpineVillagePathPlanner.MeasureDistanceOutsideTrodden(
                plan, paths, point, out _) >= LaneKeepClear;
        }

        /// <summary>
        /// Why a copse seat was refused. The planner counts these and names
        /// the dominant one when it cannot fill the clump: a bare "no room"
        /// costs a whole test run to diagnose, and the band behind one house
        /// is tight enough that this will be asked again.
        /// </summary>
        internal enum CopseRefusal
        {
            None = 0,
            Cableway,
            Water,
            OutsideBowl,
            Plot,
            Trodden,
            Lane,
            Spacing,
            Landmark,
            TooLarge
        }

        private static CopseRefusal IsCopseSeatFree(
            AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillagePathDescriptor> paths,
            Vector2 point,
            float radius)
        {
            if (IsInsideCablewayCorridor(plan, point))
            {
                return CopseRefusal.Cableway;
            }

            if (TouchesWater(plan, point))
            {
                return CopseRefusal.Water;
            }

            // The copse is the one group that stands on ground the hero uses,
            // so it answers to the mask's own rectangle rather than to the toe.
            Rect bounds = plan.TerrainBounds;
            if (point.x < bounds.xMin + radius || point.x > bounds.xMax - radius ||
                point.y < bounds.yMin + radius || point.y > bounds.yMax - radius)
            {
                return CopseRefusal.OutsideBowl;
            }

            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[index];
                if (plot.Kind == AlpineVillagePlotKind.Spring)
                {
                    continue;
                }

                if (AlpineVillageTerrainSampler.DistanceOutsidePlot(plot, point) <
                    radius + PlotClearance)
                {
                    return CopseRefusal.Plot;
                }
            }

            if (AlpineVillagePathPlanner.MeasureDistanceOutsideTrodden(
                    plan, paths, point, out _) < radius + TroddenClearance)
            {
                return CopseRefusal.Trodden;
            }

            plan.Lane.FindNearest(point, out float lateral);
            return lateral >= LaneKeepClear
                ? CopseRefusal.None
                : CopseRefusal.Lane;
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

        /// <summary>
        /// The authored stone occupies the band the trees stop just short of.
        /// A trunk grounded on the analytic face inside a panel would have its
        /// whole stem inside rock and read as a crown with no tree under it.
        /// </summary>
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
        /// mother's house that encloses the whole house.
        ///
        /// This, not a bearing cone, is what the bibles actually protect, and
        /// the peripheral storm already measures it - so the trees read its
        /// geometry rather than inventing a second one. A blanket frame-share
        /// rule measured from the station forbids the entire middle of the
        /// bowl, because the house sits on the axis at the far end of it.
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
        /// The landmark keeps its frame to itself. The exterior camera sees
        /// `83` degrees across, so a tree more than half of that away in
        /// bearing can never share a shot with the mother's house.
        ///
        /// Applied to the WALL groups only. They sit far out to one side, so
        /// it costs them nothing and it is what keeps the west wall out of the
        /// ascent frame entirely. The copse stands in the middle of the bowl
        /// by the user's decision, where this rule would leave no seat at all;
        /// there the aperture above is the binding constraint.
        /// </summary>
        private static bool ClearsTheLandmark(
            AlpineVillagePlan plan, Vector3 foot, float height)
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
        /// The size statement. A mountain-road Physical tree fills `30-50`
        /// degrees of the view from the carriageway; nothing here may fill more
        /// than `12.5`, which is what says "this is above where those stop".
        /// </summary>
        private static bool StaysSmall(
            AlpineVillagePlan plan, Vector3 foot, float height)
        {
            const float EyeHeight = 1.72f;
            int steps = Mathf.Max(1, Mathf.CeilToInt(plan.Lane.Length));
            for (int step = 0; step <= steps; step++)
            {
                AlpineVillageLaneSample sample = plan.Lane.Sample(
                    plan.Lane.Length * step / steps);
                Vector3 eye = sample.Position + Vector3.up * EyeHeight;
                if (Subtended(eye, foot, height) > MaximumSubtendedDegrees)
                {
                    return false;
                }
            }

            return true;
        }

        private static float Subtended(Vector3 eye, Vector3 foot, float height)
        {
            Vector3 toFoot = foot - eye;
            Vector3 toTip = foot + Vector3.up * height - eye;
            if (toFoot.sqrMagnitude < 0.0001f || toTip.sqrMagnitude < 0.0001f)
            {
                return 180f;
            }

            return Vector3.Angle(toFoot, toTip);
        }

        /// <summary>
        /// One threshold shared by trees and stumps: `0` inside a man's reach
        /// from the toe, `1` clear of it. Trees use it, stumps use its exact
        /// complement, and the band where both are possible is what keeps the
        /// felled line from reading as a horizontal.
        /// </summary>
        private static float Saw(float rise)
        {
            return SmoothRange(2.6f, 6.9f, rise);
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
        /// village-facing side flush and buries the uphill side, which is what
        /// a tree on such a face actually does.
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
            IReadOnlyList<MountainRoadForestDescriptor> wallTrees,
            IReadOnlyList<MountainRoadForestDescriptor> copse,
            out float footY,
            out float summitY)
        {
            footY = float.PositiveInfinity;
            summitY = float.NegativeInfinity;
            for (int index = 0; index < wallTrees.Count; index++)
            {
                footY = Mathf.Min(footY, wallTrees[index].Position.y);
                summitY = Mathf.Max(summitY, wallTrees[index].Position.y);
            }

            for (int index = 0; index < copse.Count; index++)
            {
                footY = Mathf.Min(footY, copse[index].Position.y);
                summitY = Mathf.Max(summitY, copse[index].Position.y);
            }

            // The shader normalises a tree's own base between these two, so a
            // degenerate range would divide the amplitude term by nothing.
            if (summitY - footY < 1f)
            {
                summitY = footY + 1f;
            }
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
