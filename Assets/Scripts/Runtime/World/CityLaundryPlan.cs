using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One non-residential infill on the existing residential ground.
    /// No semantic housing lot, street or courtyard route is replaced.</summary>
    public sealed class CityLaundryPlan
    {
        public const float Width = 8.6f;
        public const float Depth = 6.6f;
        public const float MaximumGroundSpread = .18f;
        public const int MaximumCandidatesPerLot = 160;
        private const float BuildingClearance = .45f;
        private const float GroundBlend = .45f;
        private static readonly ConditionalWeakTable<CityLayout, CityLaundryPlan> Plans =
            new ConditionalWeakTable<CityLayout, CityLaundryPlan>();
        private static readonly CityLaundryPlan Absent = new CityLaundryPlan();

        private CityLaundryPlan()
        {
            SolidFootprints = Array.Empty<Rect>();
            BodyFootprints = Array.Empty<Rect>();
            WalkableFootprints = Array.Empty<Rect>();
            AccessPath = Array.Empty<Vector3>();
        }

        private CityLaundryPlan(Vector2Int cell, Vector3 position, Quaternion rotation, Vector3 access)
        {
            IsEnabled = true; Cell = cell; Position = position; Rotation = rotation;
            Footprint = RectAt(-4.3f, -3.3f, 4.3f, 3.3f);
            ApproachBounds = RectAt(-3.65f, 3.25f, -1.95f, 4.9f);
            ReservationBounds = Union(Expanded(Footprint, .18f), ApproachBounds);
            GroundBounds = Expanded(Footprint, GroundBlend);
            BodyFootprints = new ReadOnlyCollection<Rect>(new[] {
                RectAt(-4f,-3f,1.5f,3f), RectAt(1.5f,-3f,4f,0f) });
            SolidFootprints = new ReadOnlyCollection<Rect>(new[] {
                // The front leaf is closed. The east service doorway stays
                // physically open between z=.3 and 1.9; no filled-room proxy.
                RectAt(-4f,2.88f,1.5f,3.12f), RectAt(-4f,-3.12f,4f,-2.88f),
                RectAt(-4.12f,-3f,-3.88f,3f), RectAt(1.38f,0f,1.62f,.3f),
                RectAt(1.38f,1.9f,1.62f,3f), RectAt(3.88f,-3f,4.12f,0f),
                RectAt(1.5f,-.12f,4f,.12f),
                RectAt(-3.5f,-.4f,-1.8f,.8f), RectAt(-3.81f,-2.275f,-3.03f,-.625f),
                RectAt(-1.36f,-2.67f,-.24f,-1.57f), RectAt(-.06f,-2.67f,1.06f,-1.57f),
                RectAt(2.54f,.60f,3.46f,1.475f),
                RectAt(3.93f,.06f,4.17f,.30f), RectAt(3.93f,2.78f,4.17f,3.02f) });
            WalkableFootprints = new ReadOnlyCollection<Rect>(new[] {
                RectAt(-4.3f,-3.3f,4.3f,3.3f), ApproachBounds });
            AccessPath = new ReadOnlyCollection<Vector3>(new[] { ApproachWorld, access });
        }

        public bool IsEnabled { get; }
        public Vector2Int Cell { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; } = Quaternion.identity;
        public Vector3 Forward => Rotation * Vector3.forward;
        public Vector3 ApproachWorld => World(new Vector3(-2.8f, 0f, 4.3f));
        public Rect Footprint { get; }
        public Rect GroundBounds { get; }
        public Rect ApproachBounds { get; }
        public Rect ReservationBounds { get; }
        public IReadOnlyList<Rect> SolidFootprints { get; }
        public IReadOnlyList<Rect> BodyFootprints { get; }
        public IReadOnlyList<Rect> WalkableFootprints { get; }
        public IReadOnlyList<Vector3> AccessPath { get; }
        public Vector3 World(Vector3 local) => Position + Rotation * local;
        public Vector3 InverseWorld(Vector3 world) => Quaternion.Inverse(Rotation) * (world - Position);

        public static CityLaundryPlan ForLayout(CityLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            return Plans.GetValue(layout, Create);
        }

        // Terrain must never start placement: fitting and existing courtyard
        // routes themselves sample terrain. Composition primes the immutable
        // site before building the physical ground; only then does it grade it.
        internal static CityLaundryPlan CachedForSurface(CityLayout layout, CitySurfaceDescriptor surface)
        {
            return surface.Kind == CitySurfaceKind.BuildableGround &&
                Plans.TryGetValue(layout,out CityLaundryPlan plan) && plan.IsEnabled &&
                plan.GroundBounds.Overlaps(surface.WorldBounds) ? plan : null;
        }

        internal float ApplyGround(Vector2 point, float rawTop)
        {
            if (!GroundBounds.Contains(point)) return rawTop;
            float distance = Vector2.Distance(point, new Vector2(
                Mathf.Clamp(point.x, Footprint.xMin, Footprint.xMax),
                Mathf.Clamp(point.y, Footprint.yMin, Footprint.yMax)));
            return Mathf.Lerp(Position.y - .04f, rawTop,
                Mathf.SmoothStep(0f, 1f, distance / GroundBlend));
        }

        public void ValidateOrThrow(CityLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (!IsEnabled) return;
            var context = new PlacementContext(layout);
            if (!context.Fits(Footprint, ApproachBounds, Position, Rotation, out _))
                throw new InvalidOperationException("Laundry infill no longer fits the existing residential ground and clear routes.");
        }

        private static CityLaundryPlan Create(CityLayout layout)
        {
            if (!layout.SpatialPlan.IsCompactCoastal) return Absent;
            var context = new PlacementContext(layout);
            var lots = new List<BuildingLot>();
            foreach (BuildingLot lot in layout.BuildingLots)
                if (lot.IsOrdinaryBuilding && lot.District == CityDistrictKind.Residential &&
                    !context.ProtectedCell(lot.Cell)) lots.Add(lot);
            lots.Sort((a, b) => {
                int order = Rank(layout.Seed,a.Cell).CompareTo(Rank(layout.Seed,b.Cell));
                if (order != 0) return order;
                order = a.Cell.y.CompareTo(b.Cell.y);
                return order != 0 ? order : a.Cell.x.CompareTo(b.Cell.x);
            });
            foreach (BuildingLot lot in lots)
            {
                Rect cell = layout.GetCellWorldBounds(lot.Cell);
                // Six stations per axis include both shared cell seams. A
                // second orientation can use an adjoining rear court while
                // the semantic owner remains this original residential cell.
                for (int z = 0; z < 6; z++)
                for (int x = 0; x < 6; x++)
                for (int side = 0; side < 4; side++)
                {
                    Vector3 position = new Vector3(Mathf.Lerp(cell.xMin + .5f, cell.xMax - .5f, x / 5f),
                        0f, Mathf.Lerp(cell.yMin + .5f, cell.yMax - .5f, z / 5f));
                    Quaternion rotation = Quaternion.Euler(0f, side * 90f, 0f);
                    Rect footprint = RectAt(position, rotation, -4.3f,-3.3f,4.3f,3.3f);
                    Rect approach = RectAt(position, rotation, -3.65f,3.25f,-1.95f,4.9f);
                    if (!context.Fits(footprint, approach, position, rotation, out Vector3 access)) continue;
                    if (!context.Foundation(footprint, out float top)) continue;
                    position.y = top + .04f;
                    return new CityLaundryPlan(lot.Cell, position, rotation, access);
                }
            }
            if (layout.Seed != GameSessionState.DefaultCitySeed) return Absent;
            throw new InvalidOperationException("No residual residential site fits the laundry without replacing buildings or blocking existing routes.");
        }

        private Rect RectAt(float x0, float z0, float x1, float z1) => RectAt(Position, Rotation, x0,z0,x1,z1);
        private static Rect RectAt(Vector3 origin, Quaternion rotation, float x0, float z0, float x1, float z1)
        {
            Vector3 a = origin + rotation * new Vector3(x0,0,z0), b = origin + rotation * new Vector3(x1,0,z1);
            return Rect.MinMaxRect(Mathf.Min(a.x,b.x), Mathf.Min(a.z,b.z), Mathf.Max(a.x,b.x), Mathf.Max(a.z,b.z));
        }
        internal static Rect Expanded(Rect source, float amount) => Rect.MinMaxRect(
            source.xMin-amount,source.yMin-amount,source.xMax+amount,source.yMax+amount);
        private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin,b.xMin),
            Mathf.Min(a.yMin,b.yMin),Mathf.Max(a.xMax,b.xMax),Mathf.Max(a.yMax,b.yMax));
        private static uint Rank(int seed, Vector2Int cell) => unchecked((uint)seed ^
            ((uint)cell.x * 73856093u) ^ ((uint)cell.y * 19349663u) ^ 0x4C41554Eu);
        private static Vector2 XZ(Vector3 p) => new Vector2(p.x,p.z);

        private sealed class PlacementContext
        {
            private readonly CityLayout layout;
            private readonly List<Vector2[]> ground = new List<Vector2[]>();
            private readonly List<Vector2[]> bodies = new List<Vector2[]>();
            private readonly List<Rect> blocked = new List<Rect>();
            private readonly List<Rect> connectorObstacles = new List<Rect>();
            private readonly List<Rect> paths = new List<Rect>();
            private readonly List<Vector3> accesses = new List<Vector3>();
            private readonly HashSet<Vector2Int> protectedCells = new HashSet<Vector2Int>();

            internal PlacementContext(CityLayout source)
            {
                layout = source;
                foreach (Vector2Int cell in layout.PrimaryLandmarkCells.Values) protectedCells.Add(cell);
                foreach (BuildingLot lot in layout.BuildingLots)
                {
                    if (lot.IsBar || lot.IsPlayerHome || lot.IsSupermarket || lot.IsDistrictPointOfInterest)
                    { protectedCells.Add(lot.Cell); blocked.Add(Expanded(layout.GetCellWorldBounds(lot.Cell), .5f)); }
                    if (lot.HasRoadFrontage) accesses.Add(lot.SidewalkArrivalPosition);
                    if (lot.HasBuilding)
                    {
                        Vector2 a = XZ(lot.DoorPosition), b = XZ(lot.SidewalkArrivalPosition);
                        blocked.Add(Expanded(Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),
                            Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y)), .9f));
                    }
                }
                foreach (Vector2Int cell in new[] { new Vector2Int(10,5), new Vector2Int(11,3),
                    new Vector2Int(11,4), new Vector2Int(11,5) })
                { protectedCells.Add(cell); blocked.Add(Expanded(layout.GetCellWorldBounds(cell), .5f)); }
                foreach (Vector2Int cell in protectedCells) blocked.Add(layout.GetCellWorldBounds(cell));
                foreach (CitySurfaceDescriptor surface in layout.Surfaces)
                    if (surface.Kind == CitySurfaceKind.BuildableGround && layout.TryGetArea(surface.Cell, out CityAreaDefinition area) &&
                        area.Archetype == CityDistrictKind.Residential)
                        ground.AddRange(layout.RoadGeometry.GetGroundPolygons(surface.Cell));
                foreach (BuildingLot lot in layout.BuildingMasses)
                    if (lot.HasBuilding) bodies.AddRange(lot.CreateCollisionPolygons());
                foreach (CityRoadPath path in layout.CourtyardPaths)
                {
                    foreach (Vector2[] strip in path.Ribbon(1.5f)) paths.Add(CityRoadPolygon.Bounds(strip));
                    foreach (Vector2 point in path.Vertices) accesses.Add(new Vector3(point.x,0f,point.y));
                }
                CityStreetSurfacePlan paving = CityStreetSurfacePlanner.CreateCourtyardPaving(layout);
                foreach (RuntimeOrientedBox box in paving.StreetGeometry) blocked.Add(BoxBounds(box));
                foreach (RuntimeOrientedBox box in paving.SidewalkGeometry) blocked.Add(BoxBounds(box));
                foreach (CityDistrictPointOfInterestDescriptor point in layout.DistrictPointsOfInterest)
                {
                    blocked.Add(Expanded(point.PublicBounds, 1f));
                    foreach (CityDistrictPointOfInterestAccessDescriptor access in point.Accesses)
                        blocked.Add(Expanded(access.ApproachBounds, .6f));
                }
                if (HomeYardSitePlanner.TryCreate(layout,out HomeYardSitePlan yard)) blocked.Add(Expanded(yard.GroundBounds,1f));
                RoadFencePlan fence = RoadFencePlanner.CreatePlan(layout);
                foreach (RoadFenceSegmentDescriptor segment in fence.Segments)
                    AddSolid(Expanded(Rect.MinMaxRect(Mathf.Min(segment.Start.x,segment.End.x),Mathf.Min(segment.Start.z,segment.End.z),
                        Mathf.Max(segment.Start.x,segment.End.x),Mathf.Max(segment.Start.z,segment.End.z)),.5f));
                CityNightFixturePlan night = CityNightFixturePlanner.CreatePlan(layout);
                foreach (StreetLampDescriptor lamp in night.StreetLamps) AddSolid(Around(lamp.Position,.65f));
                foreach (TrafficSignalDescriptor signal in night.TrafficSignals) AddSolid(Around(signal.Position,.65f));
                foreach (CityBusStopDescriptor stop in CityBusPlanner.CreateRoadRouting(layout).Stops)
                    AddSolid(Expanded(CityBusStopWorldBuilder.DescribeFurnitureFootprint(stop), .6f));
                foreach (Rect footprint in CityCoastalInfrastructurePlan.ForLayout(layout).SolidFootprints)
                    AddSolid(Expanded(footprint,.25f));
                // Preserve the already selected pocket tableaux and other
                // solid dressing. The decoration planner never reads laundry,
                // so this baseline has no dependency back to the infill site.
                CityDecorationPlan decoration = CityDecorationPlanner.CreatePlan(layout,fence,night);
                var proxies = new List<Bounds>();
                foreach (CityDecorationDescriptor descriptor in decoration.Descriptors)
                {
                    if (descriptor.CollisionTier == CityDecorationCollisionTier.None) continue;
                    proxies.Clear(); CityStaticCollisionBuilder.AddDecorationProxyBounds(layout,descriptor,proxies);
                    foreach (Bounds bounds in proxies) AddSolid(Expanded(Rect.MinMaxRect(bounds.min.x,bounds.min.z,bounds.max.x,bounds.max.z), .35f));
                }
            }

            internal bool ProtectedCell(Vector2Int cell) => protectedCells.Contains(cell);
            internal bool Fits(Rect footprint, Rect approach, Vector3 position, Quaternion rotation, out Vector3 access)
            {
                access = default;
                Rect padded = Expanded(footprint,BuildingClearance);
                foreach (Rect obstacle in blocked) if (padded.Overlaps(obstacle) || approach.Overlaps(obstacle)) return false;
                foreach (Rect path in paths) if (Expanded(footprint,.45f).Overlaps(path)) return false;
                foreach (Vector2[] body in bodies)
                    if (Intersects(padded,body) || Intersects(approach,body)) return false;
                if (!Covered(padded) || !Covered(approach)) return false;
                Vector3 entry = position + rotation * new Vector3(-2.8f,0f,4.3f);
                float best = 14f*14f;
                foreach (Vector3 target in accesses)
                {
                    float distance = (XZ(target)-XZ(entry)).sqrMagnitude;
                    if (distance >= best || !ClearConnector(entry,target,footprint)) continue;
                    access = target; best = distance;
                }
                if (best >= 14f*14f) return false;
                if (!RawTop(XZ(entry),out float y)) return false;
                access.y = RawTop(XZ(access),out float targetY) ? targetY : y;
                return true;
            }

            private bool ClearConnector(Vector3 start, Vector3 end, Rect ownBody)
            {
                int count = Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(XZ(start),XZ(end))/.4f));
                float previous=0f;
                for (int step=0;step<=count;step++)
                {
                    Vector3 point=Vector3.Lerp(start,end,step/(float)count);
                    Rect body=Around(point,.42f);
                    if (body.Overlaps(ownBody)) return false;
                    foreach (Vector2[] mass in bodies) if (Intersects(body,mass)) return false;
                    foreach (Rect obstacle in connectorObstacles) if (body.Overlaps(obstacle)) return false;
                    // Public paving and existing walking routes are allowed
                    // connector destinations; furnishing and special grounds are not.
                    if (!RawTop(XZ(point),out float top) || (step>0 && Mathf.Abs(top-previous)>.22f)) return false;
                    previous=top;
                }
                return true;
            }

            private void AddSolid(Rect footprint)
            { blocked.Add(footprint); connectorObstacles.Add(footprint); }

            internal bool Foundation(Rect footprint,out float top)
            {
                top=float.NegativeInfinity;float low=float.PositiveInfinity;
                for(int z=0;z<3;z++) for(int x=0;x<3;x++)
                {
                    Vector2 point=new Vector2(Mathf.Lerp(footprint.xMin,footprint.xMax,x*.5f),
                        Mathf.Lerp(footprint.yMin,footprint.yMax,z*.5f));
                    if(!RawTop(point,out float y))return false;
                    top=Mathf.Max(top,y);low=Mathf.Min(low,y);
                }
                return top-low<=MaximumGroundSpread;
            }

            private bool RawTop(Vector2 point,out float top)
            {
                foreach(CitySurfaceDescriptor surface in layout.Surfaces)
                    if(!surface.IsWater && surface.WorldBounds.Contains(point))
                    {top=CityTerrainSurfacePlan.SampleTopWithoutRigidSupports(layout,surface,point);return true;}
                top=0;return false;
            }

            private bool Covered(Rect rect)
            {
                float area=0f;
                foreach(Vector2[] polygon in ground)
                {
                    if(!rect.Overlaps(CityRoadPolygon.Bounds(polygon)))continue;
                    area+=IntersectionArea(rect,polygon);
                }
                return area>=rect.width*rect.height-.01f;
            }
            private static bool Intersects(Rect rect,Vector2[] polygon) => rect.Overlaps(CityRoadPolygon.Bounds(polygon)) &&
                IntersectionArea(rect,polygon)>.0001f;
            private static float IntersectionArea(Rect rect,Vector2[] polygon)
            {
                var clipped=new List<Vector2>(CityRoadPolygon.Rectangle(rect));
                for(int side=0;side<polygon.Length && clipped.Count>=3;side++)
                    clipped=CityRoadPolygon.Clip(clipped,polygon[side],polygon[(side+1)%polygon.Length]);
                return CityRoadPolygon.Area(clipped);
            }
            private static Rect Around(Vector3 point,float radius) => new Rect(point.x-radius,point.z-radius,radius*2,radius*2);
            private static Rect BoxBounds(RuntimeOrientedBox box)
            {
                Bounds bounds=CityBuildingPrototypePlacement.TransformBounds(new Bounds(Vector3.zero,box.Size),
                    new CityBuildingPrototypePose(box.Center,box.Rotation));
                return Expanded(Rect.MinMaxRect(bounds.min.x,bounds.min.z,bounds.max.x,bounds.max.z),.2f);
            }
        }
    }
}
