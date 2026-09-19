using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct CityFairStall
    {
        internal CityFairStall(string id, Vector3 position, Quaternion rotation,
            Vector3 vendorPosition, Vector3 facing, int goodsIndex)
        {
            Id = id;
            Position = position;
            Facing = facing;
            Rotation = rotation;
            VendorPosition = vendorPosition;
            GoodsIndex = goodsIndex;
            Footprint = CityFairPlanner.Footprint(position, 2.8f, 2.2f);
        }

        public string Id { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 VendorPosition { get; }
        public Vector3 Facing { get; }
        public int GoodsIndex { get; }
        public Rect Footprint { get; }
    }

    public readonly struct CityFairGarland
    {
        internal CityFairGarland(Vector3 start, Vector3 end)
        {
            Start = start;
            End = end;
        }

        public Vector3 Start { get; }
        public Vector3 End { get; }
    }

    /// <summary>
    /// The fair follows the existing continuous City terrain. Cell elevations
    /// place building lots; they are not the walking surface between facades.
    /// All positions are world metres and all prop origins are at their feet.
    /// </summary>
    public sealed class CityFairPlan
    {
        public static CityFairPlan Absent { get; } = new CityFairPlan();
        private readonly CityLayout layout;

        private CityFairPlan() { }

        internal CityFairPlan(CityLayout layout, UnityEngine.Bounds south,
            UnityEngine.Bounds north, float boundaryZ)
        {
            this.layout = layout;
            IsEnabled = true;
            SouthBuildingBounds = south;
            NorthBuildingBounds = north;
            BoundaryZ = boundaryZ;
            Bounds = Rect.MinMaxRect(Mathf.Max(south.min.x, north.min.x),
                south.max.z, Mathf.Min(south.max.x, north.max.x), north.min.z);
            float centerX = Bounds.center.x;
            // The extra wall setback also accommodates the canopy's small
            // horizontal displacement when its whole stall follows the slope.
            float southRowZ = Bounds.yMin + 1.50f;
            float northRowZ = Bounds.yMax - 1.50f;
            SouthY = SampleGroundY(new Vector3(centerX, 0f, southRowZ));
            NorthY = SampleGroundY(new Vector3(centerX, 0f, northRowZ));
            Stalls = new[]
            {
                CreateStall("fair-south-west", centerX - 3.15f, southRowZ, Vector3.forward, 0),
                CreateStall("fair-south-east", centerX + 3.15f, southRowZ, Vector3.forward, 1),
                CreateStall("fair-north-west", centerX - 3.15f, northRowZ, Vector3.back, 2),
                CreateStall("fair-north-east", centerX + 3.15f, northRowZ, Vector3.back, 3)
            };

            ResolveGroundPose(new Vector3(centerX - 3.15f, 0f, boundaryZ - 1.05f),
                Vector3.back, 1.10f, 0.80f, out Vector3 organPosition, out Quaternion organRotation);
            OrganPosition = organPosition;
            OrganRotation = organRotation;
            ResolveGroundPose(new Vector3(centerX + 3.15f, 0f, boundaryZ - 1.05f),
                Vector3.back, 0.82f, 0.66f, out Vector3 bellPosition, out Quaternion bellRotation);
            BellPosition = bellPosition;
            BellRotation = bellRotation;
            BenchPositions = new Vector3[2];
            BenchRotations = new Quaternion[2];
            Benches = new CityBenchSeat[2];
            for (int i = 0; i < Benches.Length; i++)
            {
                ResolveGroundPose(new Vector3(centerX + (i == 0 ? -3.15f : 3.15f), 0f,
                    boundaryZ + 0.80f), Vector3.forward, 1.80f, 0.70f,
                    out Vector3 position, out Quaternion rotation);
                BenchPositions[i] = position;
                BenchRotations[i] = rotation;
                Vector3 seatTop = position + rotation * (Vector3.up * CityFairPlanner.BenchSeatHeight);
                Vector3 facing = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up).normalized;
                Vector3 dock = seatTop + facing * (0.25f + CityBenchSitPlan.EntryEdgeDistance);
                Benches[i] = new CityBenchSeat(i == 0 ? "fair-bench-west" : "fair-bench-east",
                    seatTop, 1.80f, 0.50f, SampleGroundY(dock), facing, frontApproachOnly: true);
            }
            ResolveGroundPose(new Vector3(centerX, 0f, Bounds.yMin + 0.75f),
                Vector3.forward, 1.70f, 0.90f, out Vector3 clutterPosition, out Quaternion clutterRotation);
            ClutterPositions = new[] { clutterPosition };
            ClutterRotations = new[] { clutterRotation };
            ResolveGroundPose(new Vector3(Bounds.xMin + .95f, 0f, boundaryZ - .10f),
                Vector3.forward, .80f, .55f, out Vector3 tablePosition, out Quaternion tableRotation);
            ChildTablePosition = tablePosition;
            ChildTableRotation = tableRotation;
            Garlands = new CityFairGarland[3];
            for (int i = 0; i < Garlands.Length; i++)
            {
                float x = centerX + (i - 1) * 3.70f;
                Vector3 start = GroundPoint(x, Bounds.yMin - 0.04f) + Vector3.up * 4.20f;
                Vector3 end = GroundPoint(x, Bounds.yMax + 0.04f) + Vector3.up * 4.20f;
                Garlands[i] = new CityFairGarland(
                    start, end);
            }

            float southLaneZ = southRowZ + 1.35f;
            float northLaneZ = northRowZ - 1.20f - CityFairPlanner.ClearWidth;
            ClearPaths = new[]
            {
                new Rect(Bounds.xMin, southLaneZ, Bounds.width, CityFairPlanner.ClearWidth),
                new Rect(Bounds.xMin, northLaneZ, Bounds.width, CityFairPlanner.ClearWidth),
                Rect.MinMaxRect(centerX - CityFairPlanner.ClearWidth * .5f, southLaneZ,
                    centerX + CityFairPlanner.ClearWidth * .5f, northLaneZ + CityFairPlanner.ClearWidth)
            };
            CenterPathSouth = GroundPoint(centerX, southLaneZ + CityFairPlanner.ClearWidth * .5f);
            CenterPathNorth = GroundPoint(centerX, northLaneZ + CityFairPlanner.ClearWidth * .5f);
            var obstacles = new List<Rect>();
            foreach (CityFairStall stall in Stalls) obstacles.Add(stall.Footprint);
            obstacles.Add(CityFairPlanner.Footprint(OrganPosition, 1.10f, 1.28f));
            obstacles.Add(CityFairPlanner.Footprint(BellPosition, 0.82f, 0.66f));
            foreach (Vector3 position in BenchPositions)
                obstacles.Add(CityFairPlanner.Footprint(position, 1.80f, 0.70f));
            foreach (Vector3 position in ClutterPositions)
                obstacles.Add(CityFairPlanner.Footprint(position, 1.70f, 0.90f));
            obstacles.Add(CityFairPlanner.Footprint(ChildTablePosition, .80f, .55f));
            Obstacles = obstacles.ToArray();
            // Visitors browse from the inner sides of the first three stalls.
            // The front lanes and the children's toy/table/bench routes stay clear.
            AdultPositions = new Vector3[DefaultNpcPopulation.FairVisitorCount];
            AdultFacings = new Vector3[AdultPositions.Length];
            for (int i = 0; i < AdultPositions.Length; i++)
            {
                CityFairStall stall = Stalls[i];
                float side = stall.Position.x < centerX ? 1f : -1f;
                AdultPositions[i] = GroundPoint(stall.Position.x + side * 1.95f,
                    stall.Position.z + stall.Facing.z * .45f);
                AdultFacings[i] = new Vector3(-side, 0f, 0f);
            }
        }

        public bool IsEnabled { get; }
        public Rect Bounds { get; }
        public float SouthY { get; }
        public float NorthY { get; }
        public float BoundaryZ { get; }
        public UnityEngine.Bounds SouthBuildingBounds { get; }
        public UnityEngine.Bounds NorthBuildingBounds { get; }
        public Vector3 CenterPathSouth { get; }
        public Vector3 CenterPathNorth { get; }
        public CityFairStall[] Stalls { get; } = Array.Empty<CityFairStall>();
        public Vector3 OrganPosition { get; }
        public Quaternion OrganRotation { get; }
        public Vector3 BellPosition { get; }
        public Quaternion BellRotation { get; }
        public CityBenchSeat[] Benches { get; } = Array.Empty<CityBenchSeat>();
        public Vector3[] BenchPositions { get; } = Array.Empty<Vector3>();
        public Quaternion[] BenchRotations { get; } = Array.Empty<Quaternion>();
        public CityFairGarland[] Garlands { get; } = Array.Empty<CityFairGarland>();
        public Vector3[] ClutterPositions { get; } = Array.Empty<Vector3>();
        public Quaternion[] ClutterRotations { get; } = Array.Empty<Quaternion>();
        public Vector3 ChildTablePosition { get; }
        public Quaternion ChildTableRotation { get; }
        public Rect[] Obstacles { get; } = Array.Empty<Rect>();
        public Rect[] ClearPaths { get; } = Array.Empty<Rect>();
        public Vector3[] AdultPositions { get; } = Array.Empty<Vector3>();
        public Vector3[] AdultFacings { get; } = Array.Empty<Vector3>();

        public bool Contains(Vector3 point) => IsEnabled &&
            Bounds.Contains(new Vector2(point.x, point.z));

        public bool Overlaps(Rect footprint) => Suppresses(footprint);

        public bool Suppresses(Rect footprint, float margin = 0f)
        {
            if (!IsEnabled) return false;
            Rect expanded = Bounds;
            expanded.xMin -= margin;
            expanded.xMax += margin;
            expanded.yMin -= margin;
            expanded.yMax += margin;
            return CityFairPlanner.Overlaps(expanded, footprint);
        }

        public float SampleGroundY(Vector3 position)
        {
            if (layout == null || !CityTerrainSurfacePlan.TrySampleGroundTop(layout,
                new Vector2(position.x, position.z), out float y, out _))
                throw new InvalidOperationException("The fair requires actual continuous ground below every placement.");
            return y;
        }

        public Vector3 SampleGroundNormal(Vector3 position)
        {
            const float offset = .10f;
            float xSlope = (SampleGroundY(position + Vector3.right * offset) -
                SampleGroundY(position - Vector3.right * offset)) / (2f * offset);
            float zSlope = (SampleGroundY(position + Vector3.forward * offset) -
                SampleGroundY(position - Vector3.forward * offset)) / (2f * offset);
            return new Vector3(-xSlope, 1f, -zSlope).normalized;
        }

        private Vector3 GroundPoint(float x, float z)
        {
            var point = new Vector3(x, 0f, z);
            point.y = SampleGroundY(point);
            return point;
        }

        private CityFairStall CreateStall(string id, float x, float z, Vector3 facing, int goods)
        {
            ResolveGroundPose(new Vector3(x, 0f, z), facing, 2.8f, 2.2f,
                out Vector3 position, out Quaternion rotation);
            Vector3 vendor = position + rotation * new Vector3(0f, 0f, -.60f);
            vendor.y = SampleGroundY(vendor);
            return new CityFairStall(id, position, rotation, vendor, facing, goods);
        }

        private void ResolveGroundPose(Vector3 anchor, Vector3 facing, float width, float depth,
            out Vector3 position, out Quaternion rotation)
        {
            position = GroundPoint(anchor.x, anchor.z);
            rotation = Quaternion.FromToRotation(Vector3.up, SampleGroundNormal(position)) *
                Quaternion.LookRotation(facing, Vector3.up);
            // The terrain is piecewise bilinear, so its centre tangent can be
            // millimetres below a corner. Lift by that measured residual only.
            float lift = 0f;
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 support = position + rotation * new Vector3(x * width * .5f, 0f, z * depth * .5f);
                    lift = Mathf.Max(lift, SampleGroundY(support) - support.y);
                }
            position.y += lift;
        }
    }

    public static class CityFairPlanner
    {
        public static readonly Vector2Int SouthCell = new Vector2Int(11, 3);
        public static readonly Vector2Int NorthCell = new Vector2Int(11, 4);
        public const float ClearWidth = 2.20f;
        public const float MaximumGroundSlopeDegrees = 30f;
        public const float BenchSeatHeight = 0.50f;
        private static readonly ConditionalWeakTable<CityLayout, CityFairPlan> Plans =
            new ConditionalWeakTable<CityLayout, CityFairPlan>();

        public static CityFairPlan Create(CityLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            return Plans.GetValue(layout, CreateUncached);
        }

        private static CityFairPlan CreateUncached(CityLayout layout)
        {
            if (layout.BlueprintId != CityBlueprintCatalog.DefaultBlueprintId ||
                layout.Seed != GameSessionState.DefaultCitySeed)
                return CityFairPlan.Absent;
            BuildingLot south = FindLot(layout, SouthCell);
            BuildingLot north = FindLot(layout, NorthCell);
            if (south == null || north == null || !south.IsOrdinaryBuilding ||
                !north.IsOrdinaryBuilding || south.District != CityDistrictKind.Nightlife ||
                north.District != CityDistrictKind.Nightlife ||
                south.FrontageDirection != Vector2Int.down ||
                north.FrontageDirection != Vector2Int.left ||
                layout.HasRoad(RoadEdge.ForCellFrontage(SouthCell, Vector2Int.up)))
                return CityFairPlan.Absent;

            UnityEngine.Bounds southBounds = CityArchShelterPlacementResolver
                .ResolveExpectedBuildingBounds(south);
            UnityEngine.Bounds northBounds = CityArchShelterPlacementResolver
                .ResolveExpectedBuildingBounds(north);
            float boundaryZ = layout.WorldOrigin.z + NorthCell.y * layout.NodeSpacing.y;
            var plan = new CityFairPlan(layout, southBounds, northBounds, boundaryZ);
            ValidateOrThrow(layout, plan);
            return plan;
        }

        public static void ValidateOrThrow(CityLayout layout, CityFairPlan plan)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.IsEnabled) return;
            Require(plan.Bounds.width >= 10.5f && plan.Bounds.height >= 13.5f,
                "The fair requires the measured two-facade gap.");
            Require(plan.Stalls.Length == 4 && plan.Benches.Length == 2 &&
                plan.Garlands.Length == 3, "The fair must retain its bounded MVP population.");
            Require(plan.AdultPositions.Length == DefaultNpcPopulation.FairVisitorCount &&
                plan.AdultFacings.Length == plan.AdultPositions.Length,
                "The fair must place its three registered adult visitors.");
            foreach (Vector3 position in plan.AdultPositions)
            {
                Rect body = Footprint(position, CityFairAdults.BodyRadius * 2f, CityFairAdults.BodyRadius * 2f);
                Require(Contains(plan.Bounds, body), "An adult visitor leaves the fair.");
                Require(Mathf.Abs(position.y - plan.SampleGroundY(position)) < .001f,
                    "An adult visitor must stand on the actual ground.");
                foreach (Rect path in plan.ClearPaths)
                    Require(!Overlaps(body, path), "An adult visitor blocks a clear path.");
                foreach (Rect obstacle in plan.Obstacles)
                    Require(!Overlaps(body, obstacle), "An adult visitor intersects a fair prop.");
            }
            foreach (Rect obstacle in plan.Obstacles)
            {
                Require(Contains(plan.Bounds, obstacle), "A fair obstacle leaves its site.");
                foreach (Rect path in plan.ClearPaths)
                    Require(!Overlaps(obstacle, path), "A fair obstacle blocks a clear path.");
            }
            for (int i = 0; i < plan.Obstacles.Length; i++)
                for (int j = i + 1; j < plan.Obstacles.Length; j++)
                    Require(!Overlaps(plan.Obstacles[i], plan.Obstacles[j]),
                        "Two fair obstacles intersect.");
            Require(plan.ClearPaths[2].width >= ClearWidth &&
                MaximumGroundSlopeDegrees < PlayerFactory.SlopeLimitDegrees,
                "The fair must retain the shared player's width and slope contract.");
            foreach (Rect path in plan.ClearPaths)
            {
                int xSteps = Mathf.CeilToInt(path.width / .5f);
                int zSteps = Mathf.CeilToInt(path.height / .5f);
                for (int x = 0; x <= xSteps; x++)
                    for (int z = 0; z <= zSteps; z++)
                    {
                        var point = new Vector3(Mathf.Lerp(path.xMin, path.xMax, (float)x / xSteps),
                            0f, Mathf.Lerp(path.yMin, path.yMax, (float)z / zSteps));
                        Require(Vector3.Angle(Vector3.up, plan.SampleGroundNormal(point)) <= MaximumGroundSlopeDegrees,
                            "The fair's existing ground exceeds the walking slope contract.");
                    }
            }
            foreach (CityFairStall stall in plan.Stalls)
            {
                ValidateSupports(plan, stall.Position, stall.Rotation, 2.64f, 2f);
                Require(Mathf.Abs(stall.VendorPosition.y - plan.SampleGroundY(stall.VendorPosition)) <= .001f,
                    "A fair vendor must stand on the actual ground.");
                for (int x = -1; x <= 1; x += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = stall.Position + stall.Rotation * new Vector3(x * 1.49f, 2.55f, z * 1.20f);
                        Require(corner.z > plan.SouthBuildingBounds.max.z && corner.z < plan.NorthBuildingBounds.min.z,
                            "A terrain-aligned fair canopy intersects a facade.");
                    }
            }
            ValidateSupports(plan, plan.OrganPosition, plan.OrganRotation, 1.10f, .80f);
            ValidateSupports(plan, plan.BellPosition, plan.BellRotation, .82f, .66f);
            ValidateSupports(plan, plan.ChildTablePosition, plan.ChildTableRotation, .80f, .55f);
            for (int i = 0; i < plan.Benches.Length; i++)
                ValidateSupports(plan, plan.BenchPositions[i], plan.BenchRotations[i], 1.32f, .50f);
            for (int i = 0; i < plan.ClutterPositions.Length; i++)
                ValidateSupports(plan, plan.ClutterPositions[i], plan.ClutterRotations[i], 1.70f, .90f);
            BuildingLot north = FindLot(layout, NorthCell);
            Require(north != null && !plan.Suppresses(
                CityCourtyardPocketPlanner.CreateDoorClearance(north)),
                "The fair must leave the north building's west door approach open.");
        }

        internal static Rect Footprint(Vector3 position, float width, float depth) =>
            new Rect(position.x - width * 0.5f, position.z - depth * 0.5f, width, depth);

        private static void ValidateSupports(CityFairPlan plan, Vector3 position,
            Quaternion rotation, float width, float depth)
        {
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 foot = position + rotation * new Vector3(x * width * .5f, 0f, z * depth * .5f);
                    Require(Mathf.Abs(foot.y - plan.SampleGroundY(foot)) <= .035f,
                        "A fair prop must contact the actual continuous terrain at every support.");
                }
        }

        internal static bool Overlaps(Rect a, Rect b) =>
            a.xMin < b.xMax - 0.001f && a.xMax > b.xMin + 0.001f &&
            a.yMin < b.yMax - 0.001f && a.yMax > b.yMin + 0.001f;

        private static bool Contains(Rect outer, Rect inner) =>
            inner.xMin >= outer.xMin - 0.001f && inner.xMax <= outer.xMax + 0.001f &&
            inner.yMin >= outer.yMin - 0.001f && inner.yMax <= outer.yMax + 0.001f;

        private static BuildingLot FindLot(CityLayout layout, Vector2Int cell)
        {
            foreach (BuildingLot lot in layout.BuildingLots)
                if (lot.Cell == cell) return lot;
            return null;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
