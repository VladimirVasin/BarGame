using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    public enum CityResidentialCourtyardVariant { PassThrough, Quiet, Service }
    public enum CityResidentialCourtyardSurfaceKind { Asphalt, Concrete, Soil }

    /// <summary>A small causal patch in the existing ground, never an extra slab.</summary>
    public sealed class CityResidentialCourtyardSurfaceDescriptor
    {
        internal CityResidentialCourtyardSurfaceDescriptor(string id,
            CityResidentialCourtyardSurfaceKind kind, Vector2[] polygon, bool sheltered = false,
            bool commonGround = false)
        {
            StableId = id; Kind = kind; IsSheltered = sheltered; IsCommonGround = commonGround;
            Polygon = new ReadOnlyCollection<Vector2>(CityRoadPolygon.CounterClockwise(polygon));
            Footprint = CityRoadPolygon.Bounds(Polygon);
        }
        public string StableId { get; }
        public CityResidentialCourtyardSurfaceKind Kind { get; }
        public bool IsSheltered { get; }
        public bool IsCommonGround { get; }
        public IReadOnlyList<Vector2> Polygon { get; }
        public Rect Footprint { get; }
    }

    public sealed class CityResidentialCourtyardPropDescriptor
    {
        private readonly float standGroundY;
        internal CityResidentialCourtyardPropDescriptor(string id, CityResidentialCourtyardPropKind kind,
            Vector3 position, Quaternion rotation, Vector3 size, Vector3 localCentre,
            IReadOnlyList<Bounds> localCollision, float standY = float.NaN, float supportError = float.NaN)
        {
            StableId = id; Kind = kind; Position = position; Rotation = rotation;
            standGroundY = float.IsNaN(standY) ? position.y : standY;
            MaximumSupportContactError = supportError;
            Bounds = Transform(new Bounds(localCentre, size));
            var collision = new List<Bounds>(localCollision.Count);
            foreach (Bounds box in localCollision) collision.Add(Transform(box));
            CollisionBounds = collision.AsReadOnly();
        }
        public string StableId { get; }
        public CityResidentialCourtyardPropKind Kind { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 Forward => Rotation * Vector3.forward;
        public Bounds Bounds { get; }
        public Rect Footprint => Rect.MinMaxRect(Bounds.min.x, Bounds.min.z, Bounds.max.x, Bounds.max.z);
        public IReadOnlyList<Bounds> CollisionBounds { get; }
        public float MaximumSupportContactError { get; }
        public Vector3 SeatPosition => World(CityResidentialCourtyardAssetProvider.BenchSeatLocalPosition);
        public Vector3 StandPosition
        {
            get { Vector3 point = World(CityResidentialCourtyardAssetProvider.BenchDockLocalPosition); point.y = standGroundY; return point; }
        }
        public Vector3 World(Vector3 local) => Position + Rotation * local;
        private Bounds Transform(Bounds local) => CityBuildingPrototypePlacement.TransformBounds(local,
            new CityBuildingPrototypePose(Position, Rotation));
    }

    public sealed class CityResidentialCourtyardDescriptor
    {
        internal CityResidentialCourtyardDescriptor(CityCourtyardBlock block,
            CityResidentialCourtyardVariant variant, IList<CityResidentialCourtyardPropDescriptor> props,
            IList<CityResidentialCourtyardSurfaceDescriptor> surfaces, IList<Vector3> carryRoute,
            CityResidentialCourtyardPropDescriptor service, Vector3 groupPosition, Vector3 groupForward,
            Rect groupBounds, Vector3 wallStart, Vector3 wallEnd, IList<Vector3> connection,
            IList<Vector3> circulation, Vector3 commonCenter, Vector2[] openCenter)
        {
            Block = block; Variant = variant;
            StableId = $"residential-court-{block.Cell.x}-{block.Cell.y}";
            Props = new ReadOnlyCollection<CityResidentialCourtyardPropDescriptor>(props);
            Surfaces = new ReadOnlyCollection<CityResidentialCourtyardSurfaceDescriptor>(surfaces);
            CarryRoute = new ReadOnlyCollection<Vector3>(carryRoute);
            ServiceProp = service;
            GroupPosition = groupPosition; GroupForward = groupForward; GroupBounds = groupBounds;
            WallStart = wallStart; WallEnd = wallEnd;
            CommonCenter = commonCenter;
            OpenCenterPolygon = new ReadOnlyCollection<Vector2>(openCenter);
            OpenCenterBounds = CityRoadPolygon.Bounds(openCenter);
            ConnectionPath = new ReadOnlyCollection<Vector3>(connection);
            CirculationSpine = new ReadOnlyCollection<Vector3>(circulation);
            var commonGround = new List<Vector2[]>();
            foreach (Vector2[] polygon in block.GroundPolygons) commonGround.Add((Vector2[])polygon.Clone());
            CommonGroundPolygons = commonGround.AsReadOnly();
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (Vector2[] polygon in commonGround)
            foreach (Vector2 point in polygon) { min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
            CommonBounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            if (service != null)
            {
                BasketRestPosition = service.World(CityResidentialCourtyardAssetProvider.ShelfBasketLocalPosition);
                BasketEndPosition = service.World(CityResidentialCourtyardAssetProvider.ShelfBasketEndLocalPosition);
            }
        }
        public string StableId { get; }
        public Vector2Int Cell => Block.Cell;
        public CityCourtyardBlock Block { get; }
        public CityResidentialCourtyardVariant Variant { get; }
        public IReadOnlyList<CityResidentialCourtyardPropDescriptor> Props { get; }
        public IReadOnlyList<CityResidentialCourtyardSurfaceDescriptor> Surfaces { get; }
        public IReadOnlyList<Vector3> CarryRoute { get; }
        public CityResidentialCourtyardPropDescriptor ServiceProp { get; }
        public Vector3 BasketRestPosition { get; }
        public Vector3 BasketEndPosition { get; }
        public Vector3 GroupPosition { get; }
        public Vector3 GroupForward { get; }
        public Rect GroupBounds { get; }
        public Rect CommonBounds { get; }
        public Vector3 CommonCenter { get; }
        public Rect OpenCenterBounds { get; }
        public IReadOnlyList<Vector2> OpenCenterPolygon { get; }
        public Vector3 WallStart { get; }
        public Vector3 WallEnd { get; }
        public IReadOnlyList<Vector2[]> CommonGroundPolygons { get; }
        public IReadOnlyList<Vector3> CirculationSpine { get; }
        public IReadOnlyList<Vector3> ConnectionPath { get; }
    }

    /// <summary>
    /// Residential court functions are fitted to actual residual polygons after
    /// the final entrances. This is separate from the four shallow facade scenes.
    /// It never asks the downstream walkable-area or ground compositor to plan.
    /// </summary>
    public sealed class CityResidentialCourtyardPlan
    {
        public const int MaximumCourtCount = 6;
        public const int MaximumPropsPerCourt = 6;
        public const int StationsPerWall = 15;
        public const float MaximumGroundSpread = .075f;
        public const float OpenCenterSize = 4f;
        public const float MaximumBenchConnectionDistance = 6f;
        public const float MaximumPublicConnectionDistance = 10f;
        public const float MaximumShelterSlopeDegrees = 5f;
        public const float MaximumShelterSupportError = .02f;
        public const int MaximumPlantedPiecesPerStrip = 32;
        public const int MaximumPlantedSurfacesPerCourt = 64;
        public const int MaximumSurfacesPerCourt = 128;
        private static readonly ConditionalWeakTable<CityLayout, CityResidentialCourtyardPlan> Plans =
            new ConditionalWeakTable<CityLayout, CityResidentialCourtyardPlan>();

        private CityResidentialCourtyardPlan(IList<CityResidentialCourtyardDescriptor> courts)
        {
            Courts = new ReadOnlyCollection<CityResidentialCourtyardDescriptor>(courts);
            var props = new List<CityResidentialCourtyardPropDescriptor>();
            var surfaces = new List<CityResidentialCourtyardSurfaceDescriptor>();
            var collision = new List<Bounds>();
            foreach (CityResidentialCourtyardDescriptor court in courts)
            {
                props.AddRange(court.Props); surfaces.AddRange(court.Surfaces);
                foreach (CityResidentialCourtyardPropDescriptor prop in court.Props)
                    collision.AddRange(prop.CollisionBounds);
            }
            Props = props.AsReadOnly(); Surfaces = surfaces.AsReadOnly(); CollisionBounds = collision.AsReadOnly();
        }
        public IReadOnlyList<CityResidentialCourtyardDescriptor> Courts { get; }
        public IReadOnlyList<CityResidentialCourtyardPropDescriptor> Props { get; }
        public IReadOnlyList<CityResidentialCourtyardSurfaceDescriptor> Surfaces { get; }
        public IReadOnlyList<Bounds> CollisionBounds { get; }
        public static CityResidentialCourtyardPlan ForLayout(CityLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            return Plans.GetValue(layout, Create);
        }

        public bool TryResolveSurface(Vector2 point, out CityResidentialCourtyardSurfaceKind kind)
        {
            // The bounded masks are ordered; the last more specific patch wins.
            for (int i = Surfaces.Count - 1; i >= 0; i--)
                if (Surfaces[i].Footprint.Contains(point) && CityRoadPolygon.Contains(Surfaces[i].Polygon, point))
                { kind = Surfaces[i].Kind; return true; }
            kind = default; return false;
        }

        private static CityResidentialCourtyardPlan Create(CityLayout layout)
        {
            TraceStage("context begin");
            var context = new GroupContext(layout);
            TraceStage("context ready");
            var blocks = new List<CityCourtyardBlock>();
            foreach (CityCourtyardBlock block in layout.CourtyardBlocks)
                if (block.Primary.IsOrdinaryBuilding && block.Primary.District == CityDistrictKind.Residential &&
                    !block.Primary.IsPlayerHome && block.Kind == CityCourtyardBlockKind.OpenCourt &&
                    !context.ProtectedCells.Contains(block.Cell)) blocks.Add(block);
            blocks.Sort((a, b) =>
            {
                int rank = Rank(layout.Seed, a.Cell).CompareTo(Rank(layout.Seed, b.Cell));
                if (rank != 0) return rank;
                rank = a.Cell.y.CompareTo(b.Cell.y); return rank != 0 ? rank : a.Cell.x.CompareTo(b.Cell.x);
            });
            var courts = new List<CityResidentialCourtyardDescriptor>();
            var selected = new HashSet<Vector2Int>();
            for (int slot = 0; slot < MaximumCourtCount; slot++)
            {
            TraceStage("slot " + slot + " begin");
            foreach (CityCourtyardBlock block in blocks)
            {
                if (selected.Contains(block.Cell)) continue;
                CityResidentialCourtyardVariant variant = slot % 3 == 0
                    ? CityResidentialCourtyardVariant.Service : slot % 3 == 1
                    ? CityResidentialCourtyardVariant.Quiet : CityResidentialCourtyardVariant.PassThrough;
                if (!TryGroup(layout, context, block, variant, out CityResidentialCourtyardDescriptor court)) continue;
                courts.Add(court); selected.Add(block.Cell);
                foreach (CityResidentialCourtyardPropDescriptor prop in court.Props)
                    context.AddSolids(prop.CollisionBounds);
                break;
            }
            TraceStage("slot " + slot + " ready");
            }
            TraceStage("complete");
            TraceFit(context, courts);
            return new CityResidentialCourtyardPlan(courts);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private static void TraceStage(string stage) => Debug.Log("RESIDENTIAL COURTYARD PLAN: " + stage);

        private enum Rejection { NoWall, ShortWall, Ground, Building, Solid, Protected, Route, Support, Apron, Connection, Carry, Masks, OpenCenter }
        private const int RejectionKindCount = 13;

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private static void TraceFit(GroupContext context, IReadOnlyList<CityResidentialCourtyardDescriptor> courts)
        {
            var report = new System.Text.StringBuilder("RESIDENTIAL COURTYARD FIT: selected=").Append(courts.Count);
            foreach (CityResidentialCourtyardDescriptor court in courts)
                report.Append(';').Append(court.Variant).Append('@').Append(court.Cell)
                    .Append(" wall=").Append(court.GroupPosition.ToString("F2"))
                    .Append(" open=").Append(court.CommonCenter.ToString("F2"));
            for (int variant = 0; variant < 3; variant++)
            {
                report.Append(" | ").Append((CityResidentialCourtyardVariant)variant).Append(" rejected:");
                for (int stage = 0; stage < RejectionKindCount; stage++)
                    if (context.Rejections[variant, stage] != 0)
                        report.Append(' ').Append((Rejection)stage).Append('=').Append(context.Rejections[variant, stage]);
            }
            Debug.Log(report.ToString());
        }

        /// <summary>Small immutable convex footprints, indexed once. Queries
        /// reuse scratch storage; only real intersections enter clipping.</summary>
        private sealed class FootprintIndex
        {
            private const float BucketSize = 8f;
            internal readonly List<Vector2[]> Polygons = new List<Vector2[]>();
            internal readonly List<Rect> Bounds = new List<Rect>();
            private readonly Dictionary<Vector2Int, List<int>> buckets = new Dictionary<Vector2Int, List<int>>();
            private readonly List<int> scratch = new List<int>();
            internal void Add(Vector2[] polygon)
            {
                Rect bounds = CityRoadPolygon.Bounds(polygon);
                int index = Polygons.Count; Polygons.Add(polygon); Bounds.Add(bounds);
                for (int x = Bucket(bounds.xMin); x <= Bucket(bounds.xMax); x++)
                for (int z = Bucket(bounds.yMin); z <= Bucket(bounds.yMax); z++)
                {
                    var key = new Vector2Int(x, z);
                    if (!buckets.TryGetValue(key, out List<int> list)) buckets.Add(key, list = new List<int>());
                    list.Add(index);
                }
            }
            internal void Add(Rect rectangle) => Add(CityRoadPolygon.Rectangle(rectangle));
            internal void Query(Rect bounds, List<int> target)
            {
                target.Clear();
                for (int x = Bucket(bounds.xMin); x <= Bucket(bounds.xMax); x++)
                for (int z = Bucket(bounds.yMin); z <= Bucket(bounds.yMax); z++)
                    if (buckets.TryGetValue(new Vector2Int(x, z), out List<int> nearby))
                        foreach (int index in nearby)
                            if (bounds.Overlaps(Bounds[index]) && !target.Contains(index)) target.Add(index);
                target.Sort();
            }
            internal bool Intersects(IReadOnlyList<Vector2> polygon)
            {
                Query(CityRoadPolygon.Bounds(polygon), scratch);
                foreach (int index in scratch) if (Overlaps(polygon, Polygons[index])) return true;
                return false;
            }
            internal bool Covers(IReadOnlyList<Vector2> polygon)
            {
                Query(CityRoadPolygon.Bounds(polygon), scratch);
                float area = 0f;
                foreach (int index in scratch)
                {
                    Vector2[] part = Polygons[index];
                    if (!Overlaps(polygon, part)) continue;
                    IReadOnlyList<Vector2> clipped = polygon;
                    for (int i = 0; i < part.Length && clipped.Count >= 3; i++)
                        clipped = CityRoadPolygon.Clip(clipped, part[i], part[(i + 1) % part.Length]);
                    area += CityRoadPolygon.Area(clipped);
                }
                return area >= CityRoadPolygon.Area(polygon) - .002f;
            }
            private static int Bucket(float value) => Mathf.FloorToInt(value / BucketSize);
        }

        private sealed class GroupContext
        {
            internal readonly CityLayout Layout;
            internal readonly CityResidentialEntrancePlan Entrances;
            internal readonly List<Vector2[]> Bodies = new List<Vector2[]>();
            internal readonly List<Vector2[]> Routes = new List<Vector2[]>();
            internal readonly List<Bounds> Solids;
            internal readonly List<Rect> ProtectedGround = new List<Rect>();
            internal readonly HashSet<Vector2Int> ProtectedCells = new HashSet<Vector2Int>();
            internal readonly FootprintIndex BodyIndex = new FootprintIndex();
            internal readonly FootprintIndex RouteIndex = new FootprintIndex();
            internal readonly FootprintIndex SolidIndex = new FootprintIndex();
            internal readonly FootprintIndex ProtectedIndex = new FootprintIndex();
            internal readonly int[,] Rejections = new int[3, RejectionKindCount];
            private readonly Dictionary<CityCourtyardBlock, FootprintIndex> courtGround =
                new Dictionary<CityCourtyardBlock, FootprintIndex>();
            internal GroupContext(CityLayout layout)
            {
                Layout = layout; Entrances = CityResidentialEntrancePlan.ForLayout(layout);
                Solids = ExistingSolids(layout);
                foreach (BuildingLot mass in layout.BuildingMasses)
                    if (mass.HasBuilding || mass.IsCafe) Bodies.AddRange(mass.CreateCollisionPolygons());
                foreach (CityRoadPath route in layout.CourtyardPaths) Routes.AddRange(route.Ribbon(1.5f));
                foreach (CityResidentialEntranceDescriptor entrance in Entrances.Entrances)
                    foreach (Rect footprint in entrance.Footprints) Routes.Add(CityRoadPolygon.Rectangle(footprint));
                foreach (Vector2Int cell in layout.PrimaryLandmarkCells.Values) ProtectedCells.Add(cell);
                foreach (BuildingLot lot in layout.BuildingLots)
                    if (lot.IsPlayerHome || lot.IsBar || lot.IsSupermarket || lot.IsDistrictPointOfInterest)
                    { ProtectedCells.Add(lot.Cell); ProtectedGround.Add(layout.GetCellWorldBounds(lot.Cell)); }
                foreach (Vector2Int cell in new[] { new Vector2Int(10, 5), new Vector2Int(11, 3),
                    new Vector2Int(11, 4), new Vector2Int(11, 5) }) ProtectedCells.Add(cell);
                foreach (CityDistrictPointOfInterestDescriptor place in layout.DistrictPointsOfInterest)
                {
                    ProtectedGround.Add(Expanded(place.PublicBounds, .3f));
                    foreach (CityDistrictPointOfInterestAccessDescriptor access in place.Accesses)
                        ProtectedGround.Add(access.ApproachBounds);
                }
                if (HomeYardSitePlanner.TryCreate(layout, out HomeYardSitePlan yard))
                    ProtectedGround.Add(Expanded(yard.GroundBounds, .5f));
                foreach (Vector2[] body in Bodies) BodyIndex.Add(body);
                foreach (Vector2[] route in Routes) RouteIndex.Add(route);
                foreach (Bounds solid in Solids) IndexSolid(solid);
                foreach (Rect ground in ProtectedGround) ProtectedIndex.Add(ground);
            }
            internal FootprintIndex Ground(CityCourtyardBlock block)
            {
                if (courtGround.TryGetValue(block, out FootprintIndex index)) return index;
                index = new FootprintIndex();
                foreach (Vector2[] polygon in block.GroundPolygons) index.Add(polygon);
                courtGround.Add(block, index); return index;
            }
            internal void AddSolids(IReadOnlyList<Bounds> solids)
            {
                foreach (Bounds solid in solids) { Solids.Add(solid); IndexSolid(solid); }
            }
            private void IndexSolid(Bounds solid) => SolidIndex.Add(Expanded(Rect.MinMaxRect(solid.min.x,
                solid.min.z, solid.max.x, solid.max.z), .08f));
            internal void Reject(CityResidentialCourtyardVariant variant, Rejection reason)
                => Rejections[(int)variant, (int)reason]++;
        }

        private readonly struct WallCandidate
        {
            internal WallCandidate(BuildingLot owner, Vector2 a, Vector2 b, Vector2 normal)
            { Owner = owner; A = a; B = b; Normal = normal; }
            internal readonly BuildingLot Owner;
            internal readonly Vector2 A, B, Normal;
        }

        private static bool TryGroup(CityLayout layout, GroupContext context, CityCourtyardBlock block,
            CityResidentialCourtyardVariant variant, out CityResidentialCourtyardDescriptor result)
        {
            result = null;
            string prefix = $"residential-court-{block.Cell.x}-{block.Cell.y}";
            float halfWidth = variant == CityResidentialCourtyardVariant.Quiet ? 2.55f
                : variant == CityResidentialCourtyardVariant.Service ? 2.8f : 1.65f;
            var walls = new List<WallCandidate>();
            AddWalls(block.Primary);
            if (block.RearBuilding != null) AddWalls(block.RearBuilding);
            if (walls.Count == 0) context.Reject(variant, Rejection.NoWall);
            walls.Sort((a, b) =>
            {
                float aDistance = ((a.A + a.B) * .5f - XZ(block.CourtCenter)).sqrMagnitude;
                float bDistance = ((b.A + b.B) * .5f - XZ(block.CourtCenter)).sqrMagnitude;
                int distance = aDistance.CompareTo(bDistance);
                if (distance != 0) return distance;
                distance = a.A.y.CompareTo(b.A.y); return distance != 0 ? distance : a.A.x.CompareTo(b.A.x);
            });
            foreach (WallCandidate wall in walls)
            {
                Vector2 along = (wall.B - wall.A).normalized;
                float length = Vector2.Distance(wall.A, wall.B);
                if (length < halfWidth * 2f + .2f)
                { context.Reject(variant, Rejection.ShortWall); continue; }
                for (int station = 0; station < StationsPerWall; station++)
                {
                    // Centre-first stations slide one COMPLETE group along its
                    // actual wall. Objects never receive independent candidates.
                    int offset = (station + 1) / 2 * (station % 2 == 0 ? -1 : 1);
                    float t = .5f + offset / (float)(StationsPerWall - 1);
                    Vector2 wallPoint = Vector2.Lerp(wall.A + along * (halfWidth + .1f),
                        wall.B - along * (halfWidth + .1f), Mathf.Clamp01(t));
                    float rootDistance = variant == CityResidentialCourtyardVariant.Service ? .825f
                        : variant == CityResidentialCourtyardVariant.Quiet ? .70f : .60f;
                    Vector2 xz = wallPoint + wall.Normal * rootDistance;
                    Quaternion rotation = Quaternion.LookRotation(new Vector3(wall.Normal.x, 0f, wall.Normal.y), Vector3.up);
                    Vector3 origin = new Vector3(xz.x, 0f, xz.y);
                    var props = new List<CityResidentialCourtyardPropDescriptor>();
                    bool fit;
                    if (variant == CityResidentialCourtyardVariant.Quiet)
                        fit = AddProp(CityResidentialCourtyardPropKind.Bench, Vector3.zero, "main") &&
                            AddProp(CityResidentialCourtyardPropKind.Planter, new Vector3(-1.7f, 0f, 0f), "plant-left") &&
                            AddProp(CityResidentialCourtyardPropKind.Planter, new Vector3(1.7f, 0f, 0f), "plant-right");
                    else if (variant == CityResidentialCourtyardVariant.Service)
                        fit = AddProp(CityResidentialCourtyardPropKind.UtilityShelter, Vector3.zero, "main") &&
                            AddProp(CityResidentialCourtyardPropKind.Planter, new Vector3(1.95f, 0f, 0f), "plant-edge");
                    else
                        fit = AddProp(CityResidentialCourtyardPropKind.Planter, new Vector3(-.8f, 0f, 0f), "plant-first") &&
                            AddProp(CityResidentialCourtyardPropKind.Planter, new Vector3(.8f, 0f, 0f), "plant-second");
                    if (!fit) continue;
                    origin.y = props[0].Position.y;
                    float x0 = variant == CityResidentialCourtyardVariant.Service ? -1.45f : -halfWidth;
                    float x1 = variant == CityResidentialCourtyardVariant.Service ? 2.8f : halfWidth;
                    float back = variant == CityResidentialCourtyardVariant.Service ? -.73f : -.44f;
                    float front = variant == CityResidentialCourtyardVariant.Service ? 1.8f : 1.55f;
                    Vector2[] apron = PolygonAt(origin, rotation, x0, back, x1, front);
                    // The shared apron is part of the court, not an island that
                    // happens to fit between obstacles. Its front must connect
                    // physically to the existing housing/street circulation.
                    if (!ClearPolygon(context, block, apron, false))
                    { context.Reject(variant, Rejection.Apron); continue; }
                    Vector3 access = variant == CityResidentialCourtyardVariant.Quiet
                        ? props[0].World(new Vector3(0f, 0f, .80f))
                        : origin + rotation * new Vector3(0f, 0f, front - .2f);
                    if (!TryConnection(context, block, access, props, out List<Vector3> connection,
                        variant == CityResidentialCourtyardVariant.Quiet ? MaximumBenchConnectionDistance : MaximumPublicConnectionDistance))
                    { context.Reject(variant, Rejection.Connection); continue; }
                    if (!TryOpenCenter(context, block, origin, rotation, access, props, variant,
                        out Vector3 commonCenter, out Vector2[] openCenter, out List<Vector3> centerConnection))
                    { context.Reject(variant, Rejection.OpenCenter); continue; }
                    var carry = new List<Vector3>();
                    CityResidentialCourtyardPropDescriptor service = null;
                    if (variant == CityResidentialCourtyardVariant.Service)
                    {
                        service = props[0];
                        foreach (Vector3 local in new[] { CityResidentialCourtyardAssetProvider.ServiceStandLocalPosition,
                            CityResidentialCourtyardAssetProvider.ServiceWaypointLocalPosition,
                            CityResidentialCourtyardAssetProvider.ServiceEndLocalPosition })
                        {
                            Vector3 point = service.World(local);
                            if (TryTop(layout, XZ(point), out float top)) point.y = top;
                            carry.Add(point);
                        }
                        if (!ClearWalk(context, block, carry[0], carry[1], props, .24f) ||
                            !ClearWalk(context, block, carry[1], carry[2], props, .24f))
                        { context.Reject(variant, Rejection.Carry); continue; }
                    }
                    var surfaces = new List<CityResidentialCourtyardSurfaceDescriptor>();
                    // The actual residual court is the common space. Ground
                    // ownership preserves the buildings and original paving;
                    // facade planting and sheltered pads are specific overlays.
                    int commonIndex = 0;
                    foreach (Vector2[] polygon in block.GroundPolygons)
                        surfaces.Add(new CityResidentialCourtyardSurfaceDescriptor(prefix + "-common-space-" + commonIndex++,
                            CityResidentialCourtyardSurfaceKind.Concrete, (Vector2[])polygon.Clone(), false, true));
                    AddPlantedEdges(context, block, surfaces, props, apron, connection, openCenter,
                        access, commonCenter, centerConnection, prefix);
                    AddSurface(block, surfaces, apron, prefix + "-shared-apron",
                        CityResidentialCourtyardSurfaceKind.Concrete);
                    if (connection.Count > 1 && Vector3.Distance(connection[0], connection[connection.Count - 1]) > .02f)
                    {
                        var line = new List<Vector2>();
                        foreach (Vector3 point in connection) line.Add(XZ(point));
                        foreach (Vector2[] ribbon in new CityRoadPath(line).Ribbon(1.4f))
                            AddSurface(block, surfaces, ribbon, prefix + "-connection", CityResidentialCourtyardSurfaceKind.Concrete);
                    }
                    if (service != null)
                    {
                        AddSurface(block, surfaces, PolygonAt(service.World(new Vector3(0f, 2.095f, 0f)),
                            service.Rotation, -1.25f, -.625f, 1.25f, .625f),
                            prefix + "-sheltered", CityResidentialCourtyardSurfaceKind.Concrete, true);
                    }
                    CityResidentialEntranceDescriptor entrance = null;
                    foreach (CityResidentialEntranceDescriptor candidate in context.Entrances.Entrances)
                        if (ReferenceEquals(candidate.Lot, wall.Owner)) { entrance = candidate; break; }
                    if (entrance != null) AddEntrance(layout, context.Solids, block, entrance, prefix, props, surfaces);
                    var circulation = new List<Vector3>();
                    foreach (Vector2 point in block.Route.Vertices)
                    {
                        if (!TryTop(layout, point, out float top)) top = block.CourtCenter.y;
                        circulation.Add(new Vector3(point.x, top, point.y));
                    }
                    if (surfaces.Count > MaximumSurfacesPerCourt)
                    { context.Reject(variant, Rejection.Masks); continue; }
                    result = new CityResidentialCourtyardDescriptor(block, variant, props, surfaces, carry, service,
                        origin, rotation * Vector3.forward, CityRoadPolygon.Bounds(apron),
                        new Vector3(wall.A.x, origin.y, wall.A.y), new Vector3(wall.B.x, origin.y, wall.B.y),
                        connection, circulation, commonCenter, openCenter);
                    return true;

                    bool AddProp(CityResidentialCourtyardPropKind kind, Vector3 local, string suffix)
                    {
                        Vector3 position = origin + rotation * local;
                        Dimensions(kind, out Vector3 size, out Vector3 centre);
                        Vector2[] footprint = PolygonAt(position, rotation, -size.x * .5f, -size.z * .5f,
                            size.x * .5f, size.z * .5f);
                        if (!ClearPolygon(context, block, footprint, true, out Rejection reason))
                        { context.Reject(variant, reason); return false; }
                        Quaternion propRotation = rotation;
                        float supportError = float.NaN;
                        if (kind == CityResidentialCourtyardPropKind.UtilityShelter)
                        {
                            if (!TryShelterGroundPose(layout, position, rotation, out position, out propRotation, out supportError))
                            { context.Reject(variant, Rejection.Support); return false; }
                        }
                        else
                        {
                            if (!Foundation(layout, CityRoadPolygon.Bounds(footprint), out float top))
                            { context.Reject(variant, Rejection.Support); return false; }
                            position.y = top;
                        }
                        Vector3 stand = position + propRotation * CityResidentialCourtyardAssetProvider.BenchDockLocalPosition;
                        if (!TryTop(layout, XZ(stand), out float standY)) return false;
                        var descriptor = new CityResidentialCourtyardPropDescriptor(prefix + "-" + suffix, kind,
                            position, propRotation, size, centre, Collision(kind), standY, supportError);
                        if (kind == CityResidentialCourtyardPropKind.UtilityShelter &&
                            !ClearPolygon(context, block, CityRoadPolygon.Rectangle(descriptor.Footprint), true, out reason))
                        { context.Reject(variant, reason); return false; }
                        props.Add(descriptor);
                        return true;
                    }
                }
            }
            return false;

            void AddWalls(BuildingLot owner)
            {
                foreach (Vector2[] body in owner.CreateCollisionPolygons())
                for (int i = 0; i < body.Length; i++)
                {
                    Vector2 a = body[i], b = body[(i + 1) % body.Length], tangent = (b - a).normalized;
                    Vector2 normal = new Vector2(tangent.y, -tangent.x);
                    // CourtCenter can be in a narrow rear gap. A side wall can
                    // still bound a much larger shared court. Judge the actual
                    // outward residual ground, not a dot toward one point.
                    bool outwardGround = false;
                    for (int probe = 0; probe < 3; probe++)
                    {
                        Vector2 point = (a + b) * .5f + normal * (.5f + probe);
                        if (context.Ground(block).Covers(CityRoadPolygon.Rectangle(
                            new Rect(point.x - .12f, point.y - .12f, .24f, .24f))))
                        { outwardGround = true; break; }
                    }
                    if (!outwardGround) continue;
                    walls.Add(new WallCandidate(owner, a, b, normal));
                }
            }
        }

        private static bool TryOpenCenter(GroupContext context, CityCourtyardBlock block, Vector3 origin,
            Quaternion rotation, Vector3 access, IReadOnlyList<CityResidentialCourtyardPropDescriptor> props,
            CityResidentialCourtyardVariant variant, out Vector3 center, out Vector2[] openPolygon,
            out List<Vector3> centerConnection)
        {
            center = default; openPolygon = null; centerConnection = null;
            const float back = .9f;
            // An area sum can describe two alleys around a building. Require
            // one genuinely open middle in front of the inhabited edge instead.
            // Existing paths are welcome inside it; static obstacles are not.
            for (int depth = 0; depth < 3; depth++)
            for (int lateral = 0; lateral < (variant == CityResidentialCourtyardVariant.Service ? 9 : 5); lateral++)
            {
                int step = (lateral + 1) / 2 * (lateral % 2 == 0 ? -1 : 1);
                float x = step * .75f, z = back + depth * .4f;
                Vector2[] polygon = PolygonAt(origin, rotation, x - OpenCenterSize * .5f, z,
                    x + OpenCenterSize * .5f, z + OpenCenterSize);
                if (!ClearPolygon(context, block, polygon, false)) continue;
                bool blocked = false;
                foreach (CityResidentialCourtyardPropDescriptor prop in props)
                foreach (Bounds solid in prop.CollisionBounds)
                    if (Overlaps(polygon, CityRoadPolygon.Rectangle(Rect.MinMaxRect(
                        solid.min.x, solid.min.z, solid.max.x, solid.max.z)))) { blocked = true; break; }
                if (blocked) continue;
                Vector3 candidate = origin + rotation * new Vector3(x, 0f, z + OpenCenterSize * .5f);
                if (!TryTop(context.Layout, XZ(candidate), out float top)) continue;
                candidate.y = top;
                if (!ClearWalk(context, block, access, candidate, props) ||
                    !TryConnection(context, block, candidate, props, out List<Vector3> publicConnection,
                        MaximumPublicConnectionDistance)) continue;
                center = candidate; openPolygon = polygon; centerConnection = publicConnection; return true;
            }
            return false;
        }

        private static bool ClearPolygon(GroupContext context, CityCourtyardBlock block,
            IReadOnlyList<Vector2> polygon, bool preserveRoutes)
            => ClearPolygon(context, block, polygon, preserveRoutes, out _);

        private static bool ClearPolygon(GroupContext context, CityCourtyardBlock block,
            IReadOnlyList<Vector2> polygon, bool preserveRoutes, out Rejection reason)
        {
            reason = Rejection.Ground;
            if (!context.Ground(block).Covers(polygon)) return false;
            reason = Rejection.Building;
            if (context.BodyIndex.Intersects(polygon)) return false;
            reason = Rejection.Solid;
            if (context.SolidIndex.Intersects(polygon)) return false;
            reason = Rejection.Protected;
            if (context.ProtectedIndex.Intersects(polygon)) return false;
            reason = Rejection.Route;
            if (preserveRoutes && context.RouteIndex.Intersects(polygon)) return false;
            return true;
        }

        private static bool TryConnection(GroupContext context, CityCourtyardBlock block, Vector3 start,
            IReadOnlyList<CityResidentialCourtyardPropDescriptor> props, out List<Vector3> connection,
            float maximumDistance = MaximumBenchConnectionDistance)
        {
            connection = null;
            float best = maximumDistance * maximumDistance;
            for (int i = 1; i < block.Route.Vertices.Count; i++)
            {
                Vector2 a = block.Route.Vertices[i - 1], b = block.Route.Vertices[i], delta = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(XZ(start) - a, delta) / delta.sqrMagnitude);
                Vector2 point = a + delta * t;
                float distance = (point - XZ(start)).sqrMagnitude;
                if (distance >= best || !TryTop(context.Layout, point, out float top)) continue;
                Vector3 end = new Vector3(point.x, top, point.y);
                if (!ClearWalk(context, block, start, end, props)) continue;
                if (!TryTop(context.Layout, XZ(start), out float startTop)) continue;
                start.y = startTop;
                connection = new List<Vector3> { start, end }; best = distance;
            }
            return connection != null;
        }

        private static bool ClearWalk(GroupContext context, CityCourtyardBlock block, Vector3 start, Vector3 end,
            IReadOnlyList<CityResidentialCourtyardPropDescriptor> props, float radius = .28f)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(XZ(start), XZ(end)) / .3f));
            float previous = 0f;
            var footPolygon = new Vector2[4];
            for (int i = 0; i <= steps; i++)
            {
                Vector3 point = Vector3.Lerp(start, end, i / (float)steps);
                // A real capsule-width approach is reserved and checked,
                // while an adjacent facade is allowed to back the furniture.
                Rect foot = new Rect(point.x - radius, point.z - radius, radius * 2f, radius * 2f);
                footPolygon[0] = new Vector2(foot.xMin, foot.yMin); footPolygon[1] = new Vector2(foot.xMax, foot.yMin);
                footPolygon[2] = new Vector2(foot.xMax, foot.yMax); footPolygon[3] = new Vector2(foot.xMin, foot.yMax);
                if (!ClearPolygon(context, block, footPolygon, false) ||
                    !TryTop(context.Layout, XZ(point), out float top) || (i > 0 && Mathf.Abs(top - previous) > .2f)) return false;
                foreach (CityResidentialCourtyardPropDescriptor prop in props)
                foreach (Bounds solid in prop.CollisionBounds)
                    if (foot.Overlaps(Rect.MinMaxRect(solid.min.x, solid.min.z, solid.max.x, solid.max.z))) return false;
                previous = top;
            }
            return true;
        }

        private static void AddPlantedEdges(GroupContext context, CityCourtyardBlock block,
            List<CityResidentialCourtyardSurfaceDescriptor> target,
            IReadOnlyList<CityResidentialCourtyardPropDescriptor> props, IReadOnlyList<Vector2> apron,
            IReadOnlyList<Vector3> connection, IReadOnlyList<Vector2> openCenter, Vector3 access,
            Vector3 commonCenter, IReadOnlyList<Vector3> centerConnection, string prefix)
        {
            int strip = 0;
            int surfaceCount = 0;
            var routeCandidates = new List<int>();
            var commonCuts = new List<IReadOnlyList<Vector2>> { openCenter };
            AddWalkingCuts(connection);
            AddWalkingCuts(new[] { access, commonCenter });
            AddWalkingCuts(centerConnection);
            foreach (BuildingLot body in new[] { block.Primary, block.RearBuilding })
            {
                if (body == null) continue;
                foreach (Vector2[] polygon in body.CreateCollisionPolygons())
                for (int i = 0; i < polygon.Length; i++)
                {
                    if (surfaceCount >= MaximumPlantedSurfacesPerCourt) return;
                    Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                    Vector2 tangent = (b - a).normalized, normal = new Vector2(tangent.y, -tangent.x);
                    if ((b - a).magnitude < 2f) continue;
                    // The accepted 1.5m building apron keeps asphalt. Planting
                    // defines its outer edge instead of repainting that apron.
                    var band = CityRoadPolygon.CounterClockwise(new[] { a + normal * 1.52f, b + normal * 1.52f,
                        b + normal * 2.22f, a + normal * 2.22f });
                    if (!context.Ground(block).Intersects(band)) continue;
                    var pieces = new List<Vector2[]> { band };
                    context.RouteIndex.Query(CityRoadPolygon.Bounds(band), routeCandidates);
                    foreach (int route in routeCandidates)
                    {
                        Subtract(context.RouteIndex.Polygons[route]);
                        if (pieces.Count == 0) break;
                    }
                    if (pieces.Count == 0) continue;
                    Subtract(apron);
                    foreach (IReadOnlyList<Vector2> cut in commonCuts) Subtract(cut);
                    foreach (CityResidentialCourtyardPropDescriptor prop in props)
                        Subtract(CityRoadPolygon.Rectangle(Expanded(prop.Footprint, .12f)));
                    // Planting is optional presentation. If its finite mask
                    // budget is exceeded, omit the WHOLE strip rather than
                    // dropping a path cut and painting over circulation.
                    var stripSurfaces = new List<CityResidentialCourtyardSurfaceDescriptor>();
                    foreach (Vector2[] part in pieces)
                    {
                        AddSurface(block, stripSurfaces, part, prefix + "-planted-edge-" + strip++,
                            CityResidentialCourtyardSurfaceKind.Soil, false, 1.4f, 2.5f);
                        if (surfaceCount + stripSurfaces.Count > MaximumPlantedSurfacesPerCourt) break;
                    }
                    if (surfaceCount + stripSurfaces.Count <= MaximumPlantedSurfacesPerCourt)
                    { target.AddRange(stripSurfaces); surfaceCount += stripSurfaces.Count; }

                    void Subtract(IReadOnlyList<Vector2> cut)
                    {
                        if (pieces.Count == 0) return;
                        Rect cutBounds = CityRoadPolygon.Bounds(cut);
                        var remaining = new List<Vector2[]>();
                        foreach (Vector2[] part in pieces)
                        {
                            // Subtract itself clips against every half-plane
                            // even for disjoint polygons. Do not enter it for
                            // city-wide routes outside this facade strip.
                            if (!CityRoadPolygon.Bounds(part).Overlaps(cutBounds) || !Overlaps(part, cut))
                                remaining.Add(part);
                            else
                                foreach (Vector2[] fragment in CityRoadPolygon.Subtract(part, cut))
                                    if (CityRoadPolygon.Area(fragment) >= .002f) remaining.Add(fragment);
                            if (remaining.Count > MaximumPlantedPiecesPerStrip)
                            { pieces.Clear(); return; }
                        }
                        pieces = remaining;
                    }
                }
            }

            void AddWalkingCuts(IReadOnlyList<Vector3> path)
            {
                if (path == null || path.Count < 2 || Vector3.Distance(path[0], path[path.Count - 1]) <= .02f) return;
                var line = new List<Vector2>(path.Count);
                foreach (Vector3 point in path) line.Add(XZ(point));
                foreach (Vector2[] ribbon in new CityRoadPath(line).Ribbon(1.4f)) commonCuts.Add(ribbon);
            }
        }

        private static void AddSurface(CityCourtyardBlock block, List<CityResidentialCourtyardSurfaceDescriptor> target,
            IReadOnlyList<Vector2> polygon, string id, CityResidentialCourtyardSurfaceKind kind, bool sheltered = false,
            float minimumArea = .001f, float minimumSpan = 0f)
        {
            int piece = 0;
            foreach (Vector2[] ground in block.GroundPolygons)
            {
                if (!CityRoadPolygon.Bounds(polygon).Overlaps(CityRoadPolygon.Bounds(ground))) continue;
                IReadOnlyList<Vector2> clipped = polygon;
                for (int i = 0; i < ground.Length && clipped.Count >= 3; i++)
                    clipped = CityRoadPolygon.Clip(clipped, ground[i], ground[(i + 1) % ground.Length]);
                if (clipped.Count < 3 || CityRoadPolygon.Area(clipped) < minimumArea) continue;
                Rect footprint = CityRoadPolygon.Bounds(clipped);
                if (Mathf.Max(footprint.width, footprint.height) < minimumSpan) continue;
                var copy = new Vector2[clipped.Count];
                for (int i = 0; i < copy.Length; i++) copy[i] = clipped[i];
                target.Add(new CityResidentialCourtyardSurfaceDescriptor(id + "-" + piece++, kind, copy, sheltered));
            }
        }

        private static Vector2[] PolygonAt(Vector3 position, Quaternion rotation, float x0, float z0, float x1, float z1)
            => CityRoadPolygon.CounterClockwise(new[] { XZ(position + rotation * new Vector3(x0, 0f, z0)),
                XZ(position + rotation * new Vector3(x1, 0f, z0)), XZ(position + rotation * new Vector3(x1, 0f, z1)),
                XZ(position + rotation * new Vector3(x0, 0f, z1)) });

        private static bool Overlaps(IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b)
        {
            if (!CityRoadPolygon.Bounds(a).Overlaps(CityRoadPolygon.Bounds(b))) return false;
            return !Separated(a, b) && !Separated(b, a);

            static bool Separated(IReadOnlyList<Vector2> axes, IReadOnlyList<Vector2> other)
            {
                for (int i = 0; i < axes.Count; i++)
                {
                    Vector2 edge = axes[(i + 1) % axes.Count] - axes[i];
                    Vector2 normal = new Vector2(-edge.y, edge.x);
                    if (normal.sqrMagnitude < .00000001f) continue;
                    float aMin = float.PositiveInfinity, aMax = float.NegativeInfinity;
                    float bMin = float.PositiveInfinity, bMax = float.NegativeInfinity;
                    for (int j = 0; j < axes.Count; j++)
                    { float p = Vector2.Dot(axes[j], normal); aMin = Mathf.Min(aMin, p); aMax = Mathf.Max(aMax, p); }
                    for (int j = 0; j < other.Count; j++)
                    { float p = Vector2.Dot(other[j], normal); bMin = Mathf.Min(bMin, p); bMax = Mathf.Max(bMax, p); }
                    float tolerance = normal.magnitude * .00001f;
                    if (aMax <= bMin + tolerance || bMax <= aMin + tolerance) return true;
                }
                return false;
            }
        }

        private static bool Covered(IReadOnlyList<Vector2[]> ground, IReadOnlyList<Vector2> polygon)
        {
            float area = 0f;
            foreach (Vector2[] part in ground)
            {
                if (!CityRoadPolygon.Bounds(polygon).Overlaps(CityRoadPolygon.Bounds(part))) continue;
                IReadOnlyList<Vector2> clipped = polygon;
                for (int i = 0; i < part.Length && clipped.Count >= 3; i++)
                    clipped = CityRoadPolygon.Clip(clipped, part[i], part[(i + 1) % part.Length]);
                area += CityRoadPolygon.Area(clipped);
            }
            return area >= CityRoadPolygon.Area(polygon) - .002f;
        }

        private static void AddEntrance(CityLayout layout, IReadOnlyList<Bounds> existingSolids,
            CityCourtyardBlock block, CityResidentialEntranceDescriptor entrance, string prefix,
            List<CityResidentialCourtyardPropDescriptor> props, List<CityResidentialCourtyardSurfaceDescriptor> surfaces)
        {
            Quaternion rotation = Quaternion.LookRotation(entrance.Outward, Vector3.up);
            Vector3 position = entrance.Threshold + entrance.Outward * .015f;
            Vector3 pipe = position + rotation * new Vector3(1.08f, 0f, .1f);
            // The only grounded canopy member is beside the reserved doorway.
            // Its roof stays above the full entrance height and is not a wall.
            Rect pipeArea = new Rect(pipe.x - .085f, pipe.z - .085f, .17f, .17f);
            if (Covered(block.GroundPolygons, pipeArea) && TryTop(layout, XZ(pipe), out float top) &&
                Mathf.Abs(top - position.y) < .18f && FixtureClear(layout, pipeArea, existingSolids, true))
            {
                Dimensions(CityResidentialCourtyardPropKind.EntranceCanopy, out Vector3 size, out Vector3 centre);
                var canopy = new CityResidentialCourtyardPropDescriptor(prefix + "-canopy",
                    CityResidentialCourtyardPropKind.EntranceCanopy, position, rotation, size, centre,
                    Collision(CityResidentialCourtyardPropKind.EntranceCanopy));
                bool clear = true;
                foreach (CityResidentialCourtyardPropDescriptor prop in props)
                    if (prop.Footprint.Overlaps(pipeArea)) { clear = false; break; }
                if (clear) props.Add(canopy);
            }
            if (props.Count >= MaximumPropsPerCourt) return;
            Vector3 drainPosition = position + rotation * new Vector3(1.1f, 0f, .75f);
            Rect drainFootprint = RectAt(drainPosition, rotation, -.225f, -.325f, .225f, .325f);
            if (!Covered(block.GroundPolygons, drainFootprint) || !Foundation(layout, drainFootprint, out float drainTop) ||
                !FixtureClear(layout, drainFootprint, existingSolids, false)) return;
            foreach (CityResidentialCourtyardPropDescriptor prop in props)
                if (prop.Kind != CityResidentialCourtyardPropKind.EntranceCanopy && prop.Footprint.Overlaps(drainFootprint)) return;
            drainPosition.y = drainTop;
            Dimensions(CityResidentialCourtyardPropKind.Drain, out Vector3 drainSize, out Vector3 drainCentre);
            var drain = new CityResidentialCourtyardPropDescriptor(prefix + "-drain", CityResidentialCourtyardPropKind.Drain,
                drainPosition, rotation, drainSize, drainCentre, Array.Empty<Bounds>());
            props.Add(drain);
            AddSurface(block, surfaces, PolygonAt(drain.Position, drain.Rotation, -.345f, -.445f, .345f, .445f),
                prefix + "-drain-set", CityResidentialCourtyardSurfaceKind.Concrete);
        }

        private static List<Bounds> ExistingSolids(CityLayout layout)
        {
            var result = new List<Bounds>();
            RoadFencePlan fence = RoadFencePlanner.CreatePlan(layout);
            CityNightFixturePlan night = CityNightFixturePlanner.CreatePlan(layout);
            foreach (RoadFenceSegmentDescriptor segment in fence.Segments)
                Add(Expanded(Rect.MinMaxRect(Mathf.Min(segment.Start.x, segment.End.x),
                    Mathf.Min(segment.Start.z, segment.End.z), Mathf.Max(segment.Start.x, segment.End.x),
                    Mathf.Max(segment.Start.z, segment.End.z)), .08f));
            foreach (CityDecorationDescriptor decoration in CityDecorationPlanner.CreatePlan(layout, fence, night).Descriptors)
                if (decoration.CollisionTier != CityDecorationCollisionTier.None)
                    CityStaticCollisionBuilder.AddDecorationProxyBounds(layout, decoration, result);
            foreach (StreetLampDescriptor lamp in night.StreetLamps)
                result.Add(CityStaticCollisionBuilder.CreateLowerPoleBounds(lamp.Position));
            foreach (TrafficSignalDescriptor signal in night.TrafficSignals)
                result.Add(CityStaticCollisionBuilder.CreateLowerPoleBounds(signal.Position));
            foreach (CityBusStopDescriptor stop in CityBusPlanner.CreateRoadRouting(layout).Stops)
                Add(CityBusStopWorldBuilder.DescribeFurnitureFootprint(stop));
            foreach (Rect footprint in CityCoastalInfrastructurePlan.ForLayout(layout).SolidFootprints) Add(footprint);
            CityLaundryPlan laundry = CityLaundryPlan.ForLayout(layout);
            if (laundry.IsEnabled) Add(laundry.ReservationBounds);
            CityDogParkPlan dogs = CityDogParkPlan.ForLayout(layout);
            if (dogs.IsEnabled) Add(dogs.ReservationBounds);
            return result;

            void Add(Rect rectangle) => result.Add(new Bounds(new Vector3(rectangle.center.x, 1f, rectangle.center.y),
                new Vector3(rectangle.width, 2f, rectangle.height)));
        }

        private static bool FixtureClear(CityLayout layout, Rect footprint,
            IReadOnlyList<Bounds> existingSolids, bool blocking)
        {
            if (CityResidentialEntrancePlan.IntersectsReservedApproach(layout, footprint)) return false;
            foreach (Bounds solid in existingSolids)
                if (footprint.Overlaps(Rect.MinMaxRect(solid.min.x, solid.min.z, solid.max.x, solid.max.z))) return false;
            foreach (BuildingLot mass in layout.BuildingMasses)
            foreach (Vector2[] body in mass.CreateCollisionPolygons())
            {
                if (!footprint.Overlaps(CityRoadPolygon.Bounds(body))) continue;
                IReadOnlyList<Vector2> overlap = CityRoadPolygon.Rectangle(footprint);
                for (int i = 0; i < body.Length && overlap.Count >= 3; i++)
                    overlap = CityRoadPolygon.Clip(overlap, body[i], body[(i + 1) % body.Length]);
                if (CityRoadPolygon.Area(overlap) > .00001f) return false;
            }
            foreach (CityDistrictPointOfInterestDescriptor place in layout.DistrictPointsOfInterest)
            {
                if (Expanded(place.PublicBounds, .3f).Overlaps(footprint)) return false;
                foreach (CityDistrictPointOfInterestAccessDescriptor access in place.Accesses)
                    if (access.ApproachBounds.Overlaps(footprint)) return false;
            }
            if (blocking)
            {
                float clearance = .75f + Mathf.Max(footprint.width, footprint.height) * .5f;
                foreach (CityRoadPath route in layout.CourtyardPaths)
                    if (route.Project(footprint.center).DistanceSquared < clearance * clearance) return false;
            }
            return true;
        }

        private static bool TryShelterGroundPose(CityLayout layout, Vector3 horizontalPosition, Quaternion yaw,
            out Vector3 position, out Quaternion rotation, out float supportError)
        {
            position = horizontalPosition; rotation = yaw; supportError = float.PositiveInfinity;
            // Four imported .19m footplates sit at x=+/-1.12, z=+/- .49.
            // Fit a rigid ground plane, then measure their actual transformed
            // corners. A natural grade is allowed; warped footing is rejected.
            const float halfWidth = 1.215f, halfDepth = .585f;
            float sumY = 0f, sumXY = 0f, sumZY = 0f, sumXX = 0f, sumZZ = 0f;
            for (int row = -1; row <= 1; row++)
            for (int column = -1; column <= 1; column++)
            {
                float x = column * halfWidth, z = row * halfDepth;
                Vector3 point = horizontalPosition + yaw * new Vector3(x, 0f, z);
                if (!TryTop(layout, XZ(point), out float top)) return false;
                sumY += top; sumXY += x * top; sumZY += z * top;
                sumXX += x * x; sumZZ += z * z;
            }
            float gradientX = sumXY / sumXX, gradientZ = sumZY / sumZZ;
            Vector3 normal = yaw * new Vector3(-gradientX, 1f, -gradientZ).normalized;
            if (Vector3.Angle(Vector3.up, normal) > MaximumShelterSlopeDegrees) return false;
            rotation = Quaternion.FromToRotation(Vector3.up, normal) * yaw;
            position.y = sumY / 9f;
            float lowest = float.PositiveInfinity, highest = float.NegativeInfinity;
            for (int foot = 0; foot < 4; foot++)
            for (int corner = 0; corner < 4; corner++)
            {
                float x = ((foot & 1) == 0 ? -1.12f : 1.12f) + ((corner & 1) == 0 ? -.095f : .095f);
                float z = ((foot & 2) == 0 ? -.49f : .49f) + ((corner & 2) == 0 ? -.095f : .095f);
                Vector3 point = position + rotation * new Vector3(x, 0f, z);
                if (!TryTop(layout, XZ(point), out float top)) return false;
                float residual = top - point.y;
                lowest = Mathf.Min(lowest, residual); highest = Mathf.Max(highest, residual);
            }
            // Centre the measured errors rather than sinking every other leg
            // to hide the highest corner. All sixteen contacts retain the same
            // explicit two-centimetre bound on the unmodified physical ground.
            position.y += (lowest + highest) * .5f;
            supportError = (highest - lowest) * .5f;
            return supportError <= MaximumShelterSupportError;
        }

        private static bool Foundation(CityLayout layout, Rect footprint, out float top)
        {
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            for (int z = 0; z < 3; z++) for (int x = 0; x < 3; x++)
            {
                Vector2 p = new Vector2(Mathf.Lerp(footprint.xMin, footprint.xMax, x * .5f),
                    Mathf.Lerp(footprint.yMin, footprint.yMax, z * .5f));
                if (!TryTop(layout, p, out float y)) { top = 0f; return false; }
                low = Mathf.Min(low, y); high = Mathf.Max(high, y);
            }
            top = low; return high - low <= MaximumGroundSpread;
        }
        private static bool TryTop(CityLayout layout, Vector2 point, out float top)
        {
            if (CityResidentialEntrancePlan.TrySampleCachedTop(layout, point, out top)) return true;
            return CityTerrainSurfacePlan.TrySampleGroundTop(layout, point, out top, out _);
        }
        private static bool Covered(IReadOnlyList<Vector2[]> ground, Rect footprint)
        {
            float area = 0f;
            foreach (Vector2[] polygon in ground)
            {
                if (!footprint.Overlaps(CityRoadPolygon.Bounds(polygon))) continue;
                IReadOnlyList<Vector2> clipped = CityRoadPolygon.Rectangle(footprint);
                for (int i = 0; i < polygon.Length && clipped.Count >= 3; i++)
                    clipped = CityRoadPolygon.Clip(clipped, polygon[i], polygon[(i + 1) % polygon.Length]);
                area += CityRoadPolygon.Area(clipped);
            }
            return area >= footprint.width * footprint.height - .001f;
        }
        private static Rect RectAt(Vector3 p, Quaternion rotation, float x0, float z0, float x1, float z1)
        {
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < 4; i++)
            {
                Vector2 point = XZ(p + rotation * new Vector3((i & 1) == 0 ? x0 : x1,
                    0f, (i & 2) == 0 ? z0 : z1));
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        private static Rect Expanded(Rect r, float amount) => Rect.MinMaxRect(r.xMin - amount,
            r.yMin - amount, r.xMax + amount, r.yMax + amount);
        private static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
        private static uint Rank(int seed, Vector2Int cell) => unchecked((uint)seed ^
            ((uint)cell.x * 73856093u) ^ ((uint)cell.y * 19349663u) ^ 0x43525459u);

        private static void Dimensions(CityResidentialCourtyardPropKind kind, out Vector3 size, out Vector3 centre)
        {
            // This method is pure source metadata; it never loads a model.
            Bounds bounds = CityResidentialCourtyardAssetProvider.GetLocalBounds(kind);
            size = bounds.size; centre = bounds.center;
        }
        private static IReadOnlyList<Bounds> Collision(CityResidentialCourtyardPropKind kind)
        {
            switch (kind)
            {
                case CityResidentialCourtyardPropKind.Bench:
                    return new[] { new Bounds(new Vector3(0f, .23f, 0f), new Vector3(1.9f, .46f, .64f)),
                        new Bounds(new Vector3(0f, .7f, -.3f), new Vector3(1.9f, .4f, .1f)) };
                case CityResidentialCourtyardPropKind.Planter:
                    return new[] { new Bounds(new Vector3(0f, .145f, 0f), new Vector3(1.3f, .29f, .65f)) };
                case CityResidentialCourtyardPropKind.UtilityShelter:
                    return new[] {
                        new Bounds(new Vector3(-1.12f, 1.025f, -.49f), new Vector3(.065f, 2.05f, .065f)),
                        new Bounds(new Vector3(-1.12f, 1.025f, .49f), new Vector3(.065f, 2.05f, .065f)),
                        new Bounds(new Vector3(1.12f, 1.025f, -.49f), new Vector3(.065f, 2.05f, .065f)),
                        new Bounds(new Vector3(1.12f, 1.025f, .49f), new Vector3(.065f, 2.05f, .065f)),
                        new Bounds(new Vector3(-1.12f, .65f, -.05f), new Vector3(.05f, 1.04f, .91f)),
                        new Bounds(new Vector3(1.12f, .65f, -.05f), new Vector3(.05f, 1.04f, .91f)),
                        new Bounds(new Vector3(0f, .835f, -.515f), new Vector3(2.2f, 1.41f, .055f)),
                        new Bounds(new Vector3(.67f, .31f, .22f), new Vector3(.58f, .62f, .52f)),
                        new Bounds(new Vector3(-.67f, .8325f, .25f), new Vector3(.83f, .035f, .64f)),
                        new Bounds(new Vector3(.67f, .8325f, .25f), new Vector3(.83f, .035f, .64f)) };
                case CityResidentialCourtyardPropKind.EntranceCanopy:
                    return new[] { new Bounds(new Vector3(1.08f, 1.35f, .1f), new Vector3(.1f, 2.7f, .1f)) };
                default: return Array.Empty<Bounds>();
            }
        }
    }
}
