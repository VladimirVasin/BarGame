using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Shared residual-ground fitting for ordinary residential infill.
    /// Existing buildings, public routes and authored dressing own the site first.</summary>
    internal sealed class CityResidentialInfillPlacement
    {
        private readonly CityLayout layout;
        private readonly List<Vector2[]> ground = new List<Vector2[]>();
        private readonly List<Vector2[]> bodies = new List<Vector2[]>();
        private readonly List<Rect> blocked = new List<Rect>();
        private readonly List<Rect> connectorObstacles = new List<Rect>();
        private readonly List<Rect> paths = new List<Rect>();
        private readonly List<Vector3> accesses = new List<Vector3>();
        private readonly HashSet<Vector2Int> protectedCells = new HashSet<Vector2Int>();

        internal CityResidentialInfillPlacement(CityLayout source)
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
        internal bool Fits(Rect footprint, Rect approach, Vector3 position, Quaternion rotation, Vector3 localEntry, out Vector3 access)
        {
            access = default;
            Rect padded = Expanded(footprint,.45f);
            foreach (Rect obstacle in blocked) if (padded.Overlaps(obstacle) || approach.Overlaps(obstacle)) return false;
            foreach (Rect path in paths) if (Expanded(footprint,.45f).Overlaps(path)) return false;
            foreach (Vector2[] body in bodies)
                if (Intersects(padded,body) || Intersects(approach,body)) return false;
            if (!Covered(padded) || !Covered(approach)) return false;
            Vector3 entry = position + rotation * localEntry;
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

        internal bool Foundation(Rect footprint,out float top, float maximumSpread = .18f)
        {
            top=float.NegativeInfinity;float low=float.PositiveInfinity;
            for(int z=0;z<3;z++) for(int x=0;x<3;x++)
            {
                Vector2 point=new Vector2(Mathf.Lerp(footprint.xMin,footprint.xMax,x*.5f),
                    Mathf.Lerp(footprint.yMin,footprint.yMax,z*.5f));
                if(!RawTop(point,out float y))return false;
                top=Mathf.Max(top,y);low=Mathf.Min(low,y);
            }
            return top-low<=maximumSpread;
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

        // Later infill reads earlier reservations; earlier plans never read
        // the later owner, keeping terrain and decoration dependencies acyclic.
        internal void Reserve(Rect footprint, float padding = 0f) => AddSolid(Expanded(footprint, padding));

        internal void ReservePath(IReadOnlyList<Vector3> route, float radius)
        {
            for (int i = 1; i < route.Count; i++)
            {
                Vector3 a = route[i - 1], b = route[i];
                Reserve(Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z),
                    Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z)), radius);
            }
        }

        private static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
        private static Rect Expanded(Rect source, float amount) => Rect.MinMaxRect(
            source.xMin - amount, source.yMin - amount, source.xMax + amount, source.yMax + amount);
    }
}
