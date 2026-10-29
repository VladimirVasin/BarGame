using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One convex part of a building's ground body and its offset.
    /// The union of all offsets, less the union of all bodies, owns the band.</summary>
    internal sealed class CityBuildingPerimeterFootprint
    {
        internal CityBuildingPerimeterFootprint(string buildingId, Vector2[] body)
        {
            BuildingId = buildingId;
            Body = CityRoadPolygon.CounterClockwise((Vector2[])body.Clone());
            Outer = Offset(Body, CityBuildingPerimeterPlan.Width);
            BodyFootprint = new GroundSurfacePartitioner.Footprint(Body);
            OuterFootprint = new GroundSurfacePartitioner.Footprint(Outer);
            Bounds = OuterFootprint.Bounds;
        }

        internal string BuildingId { get; }
        internal Vector2[] Body { get; }
        internal Vector2[] Outer { get; }
        internal GroundSurfacePartitioner.Footprint BodyFootprint { get; }
        internal GroundSurfacePartitioner.Footprint OuterFootprint { get; }
        internal Rect Bounds { get; }

        private static Vector2[] Offset(IReadOnlyList<Vector2> body, float width)
        {
            var outer = new Vector2[body.Count];
            for (int index = 0; index < body.Count; index++)
            {
                Vector2 point = body[index];
                Vector2 incoming = (point - body[(index + body.Count - 1) % body.Count]).normalized;
                Vector2 outgoing = (body[(index + 1) % body.Count] - point).normalized;
                Vector2 before = new Vector2(incoming.y, -incoming.x);
                Vector2 after = new Vector2(outgoing.y, -outgoing.x);
                float denominator = 1f + Vector2.Dot(before, after);
                if (incoming.sqrMagnitude < .5f || outgoing.sqrMagnitude < .5f || denominator <= .0001f)
                    throw new InvalidOperationException("Building perimeter requires a nondegenerate convex body.");
                outer[index] = point + (before + after) * (width / denominator);
            }
            return outer;
        }
    }

    /// <summary>Immutable City-only pavement mask, indexed once per layout.
    /// It describes ground coverage without adding a slab or changing heights.</summary>
    internal sealed class CityBuildingPerimeterPlan
    {
        internal const float Width = 1.5f;
        private const float BucketSize = 16f;
        private const float BoundsTolerance = .002f;
        private static readonly ConditionalWeakTable<CityLayout, CityBuildingPerimeterPlan> Plans =
            new ConditionalWeakTable<CityLayout, CityBuildingPerimeterPlan>();
        private readonly Dictionary<Vector2Int, List<int>> buckets = new Dictionary<Vector2Int, List<int>>();
        private readonly List<GroundSurfacePartitioner.Footprint>[] bands;

        private CityBuildingPerimeterPlan(CityLayout layout, CityCemeteryPlan cemetery)
        {
            var footprints = new List<CityBuildingPerimeterFootprint>();
            Collect(layout, cemetery, footprints);
            Footprints = new ReadOnlyCollection<CityBuildingPerimeterFootprint>(footprints);
            for (int index = 0; index < footprints.Count; index++)
            {
                Rect bounds = footprints[index].Bounds;
                for (int z = Bucket(bounds.yMin); z <= Bucket(bounds.yMax); z++)
                for (int x = Bucket(bounds.xMin); x <= Bucket(bounds.xMax); x++)
                {
                    var key = new Vector2Int(x, z);
                    if (!buckets.TryGetValue(key, out List<int> entries))
                    { entries = new List<int>(); buckets.Add(key, entries); }
                    entries.Add(index);
                }
            }
            bands = new List<GroundSurfacePartitioner.Footprint>[footprints.Count];
            var candidates = new List<int>();
            for (int index = 0; index < footprints.Count; index++)
            {
                CityBuildingPerimeterFootprint footprint = footprints[index];
                Query(footprint.Bounds, candidates);
                var pieces = new List<Vector2[]> { footprint.Outer };
                foreach (int candidate in candidates)
                {
                    var remaining = new List<Vector2[]>();
                    foreach (Vector2[] piece in pieces)
                        remaining.AddRange(CityRoadPolygon.Subtract(piece, footprints[candidate].Body));
                    pieces = remaining;
                    if (pieces.Count == 0) break;
                }
                bands[index] = new List<GroundSurfacePartitioner.Footprint>(pieces.Count);
                foreach (Vector2[] piece in pieces)
                    bands[index].Add(new GroundSurfacePartitioner.Footprint(piece));
            }
        }

        internal IReadOnlyList<CityBuildingPerimeterFootprint> Footprints { get; }

        /// <summary>City supplies its already-built cemetery. A Home-first
        /// composition resolves the same deterministic lodge from its plan.</summary>
        internal static CityBuildingPerimeterPlan ForLayout(CityLayout layout, CityCemeteryPlan cemetery = null)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (Plans.TryGetValue(layout, out CityBuildingPerimeterPlan cached)) return cached;
            var created = new CityBuildingPerimeterPlan(layout, cemetery);
            Plans.Add(layout, created);
            return created;
        }

        /// <summary>Reuses the caller's scratch list; indices are unique and
        /// ascending regardless of how many spatial buckets a query crosses.</summary>
        internal void Query(Rect bounds, List<int> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            for (int z = Bucket(bounds.yMin); z <= Bucket(bounds.yMax); z++)
            for (int x = Bucket(bounds.xMin); x <= Bucket(bounds.xMax); x++)
            {
                if (!buckets.TryGetValue(new Vector2Int(x, z), out List<int> entries)) continue;
                foreach (int index in entries)
                    if (Overlaps(Footprints[index].Bounds, bounds) && !destination.Contains(index))
                        destination.Add(index);
            }
            destination.Sort();
        }

        internal bool Contains(Vector2 point)
        {
            if (!buckets.TryGetValue(new Vector2Int(Bucket(point.x), Bucket(point.y)), out List<int> entries))
                return false;
            bool outer = false;
            foreach (int index in entries)
            {
                CityBuildingPerimeterFootprint footprint = Footprints[index];
                if (!ContainsBounds(footprint.Bounds, point)) continue;
                if (CityRoadPolygon.Contains(footprint.Body, point)) return false;
                if (CityRoadPolygon.Contains(footprint.Outer, point)) outer = true;
            }
            return outer;
        }

        /// <summary>Positive-area intersection with the actual ring, including
        /// narrow stems whose roots are outside it. Does not allocate per query.</summary>
        internal bool Overlaps(Rect bounds)
        {
            if (bounds.width <= 0f || bounds.height <= 0f) return false;
            for (int z = Bucket(bounds.yMin); z <= Bucket(bounds.yMax); z++)
            for (int x = Bucket(bounds.xMin); x <= Bucket(bounds.xMax); x++)
            {
                if (!buckets.TryGetValue(new Vector2Int(x, z), out List<int> entries)) continue;
                foreach (int index in entries)
                {
                    if (!Overlaps(Footprints[index].Bounds, bounds)) continue;
                    foreach (GroundSurfacePartitioner.Footprint band in bands[index])
                        if (Intersects(band, bounds)) return true;
                }
            }
            return false;
        }

        private static bool Intersects(GroundSurfacePartitioner.Footprint band, Rect bounds)
        {
            if (!bounds.Overlaps(band.Bounds)) return false;
            // AABB overlap supplies the rectangle's two SAT axes; every band
            // edge supplies the remaining axes of these convex polygons.
            for (int index = 0; index < band.Points.Length; index++)
            {
                Vector2 point = band.Points[index];
                Vector2 edge = band.Points[(index + 1) % band.Points.Length] - point;
                Vector2 normal = new Vector2(-edge.y, edge.x);
                Vector2 far = new Vector2(normal.x >= 0f ? bounds.xMax : bounds.xMin,
                    normal.y >= 0f ? bounds.yMax : bounds.yMin);
                Vector2 near = new Vector2(normal.x >= 0f ? bounds.xMin : bounds.xMax,
                    normal.y >= 0f ? bounds.yMin : bounds.yMax);
                float minimum = 0f, maximum = 0f;
                foreach (Vector2 vertex in band.Points)
                {
                    float projection = Vector2.Dot(normal, vertex - point);
                    minimum = Mathf.Min(minimum, projection);
                    maximum = Mathf.Max(maximum, projection);
                }
                if (Vector2.Dot(normal, far - point) <= minimum + .000001f ||
                    Vector2.Dot(normal, near - point) >= maximum - .000001f) return false;
            }
            return true;
        }

        private static void Collect(CityLayout layout, CityCemeteryPlan cemetery,
            List<CityBuildingPerimeterFootprint> result)
        {
            // BuildingMasses includes courtyard rear houses; Cafe is a
            // separate land-use kind and deliberately has HasBuilding=false.
            for (int index = 0; index < layout.BuildingMasses.Count; index++)
            {
                BuildingLot lot = layout.BuildingMasses[index];
                if (!lot.HasBuilding && !lot.IsCafe) continue;
                string id = $"lot-{lot.Cell.x}-{lot.Cell.y}-{index}";
                foreach (Vector2[] polygon in lot.CreateCollisionPolygons()) Add(id, polygon, result);
            }

            CityWorldPlans world = CityWorldPlans.GetOrCreate(layout);
            if (world.Church != null) Add("church", world.Church.ModelFootprint, result);
            if (world.Laundry.IsEnabled)
                foreach (Rect body in world.Laundry.BodyFootprints) Add("laundry", body, result);
            if (world.EastExit.IsEnabled) Add("east-checkpoint", world.EastExit.BoothBounds, result);
            foreach (CityCoastalFacility facility in world.Infrastructure.Facilities)
                Add(facility.StableId, Rectangle(facility.Position, facility.Rotation, facility.Size), result);
            foreach (CityFringeYardDescriptor yard in world.FringeYard.Yards)
            foreach (CityFringeYardPartDescriptor part in yard.Parts)
                if (part.Kind == CityFringeYardPartKind.UtilityShed)
                    Add(part.StableId, Rectangle(part.Center, part.Rotation, part.Size), result);

            CityCanneryPlan cannery = CityCanneryPlan.Create(layout);
            if (cannery != null)
            {
                Bounds hall = cannery.HallBounds;
                Add("cannery", Rect.MinMaxRect(hall.min.x, hall.min.z, hall.max.x, hall.max.z), result);
            }

            if (world.Seacoast != null)
            {
                // Beach sand remains a separate dynamic contract. Keeping the
                // hut in the body register lets the compositor apply its own
                // surface eligibility without inventing a blanket exception.
                if (world.Seacoast.TryGetPart("seacoast-hut-floor", out CitySeacoastPartDescriptor hut))
                    Add("seacoast-rental-hut", Rectangle(hut.Center, hut.Rotation, hut.Size), result);
                if (world.Seacoast.Port != null)
                    Add("port-warehouse", world.Seacoast.Port.WarehouseBounds, result);
            }

            if (cemetery == null) cemetery = CityCemeteryPlanner.Create(layout, world.ChurchCemeteryPassage);
            if (cemetery != null)
                foreach (CityCemeteryPartDescriptor part in cemetery.Parts)
                    if (part.StableId == "cemetery-lodge-base")
                    { Add("cemetery-lodge", Rectangle(part.Center, part.Rotation, part.Size), result); break; }
        }

        private static void Add(string id, Rect body, List<CityBuildingPerimeterFootprint> result)
            => Add(id, CityRoadPolygon.Rectangle(body), result);

        private static void Add(string id, Vector2[] body, List<CityBuildingPerimeterFootprint> result)
        {
            if (body.Length < 3 || Mathf.Abs(CityRoadPolygon.Area(body)) <= .00001f)
                throw new InvalidOperationException("Building perimeter has no physical footprint: " + id);
            result.Add(new CityBuildingPerimeterFootprint(id, body));
        }

        private static Vector2[] Rectangle(Vector3 center, Quaternion rotation, Vector3 size)
        {
            var result = new Vector2[4];
            Vector3[] local = { new Vector3(-size.x, 0f, -size.z), new Vector3(size.x, 0f, -size.z),
                new Vector3(size.x, 0f, size.z), new Vector3(-size.x, 0f, size.z) };
            for (int index = 0; index < result.Length; index++)
            {
                Vector3 world = center + rotation * (local[index] * .5f);
                result[index] = new Vector2(world.x, world.z);
            }
            return CityRoadPolygon.CounterClockwise(result);
        }

        private static bool ContainsBounds(Rect bounds, Vector2 point) =>
            point.x >= bounds.xMin - BoundsTolerance && point.x <= bounds.xMax + BoundsTolerance &&
            point.y >= bounds.yMin - BoundsTolerance && point.y <= bounds.yMax + BoundsTolerance;
        private static bool Overlaps(Rect a, Rect b) =>
            a.xMin <= b.xMax + BoundsTolerance && a.xMax >= b.xMin - BoundsTolerance &&
            a.yMin <= b.yMax + BoundsTolerance && a.yMax >= b.yMin - BoundsTolerance;
        private static int Bucket(float value) => Mathf.FloorToInt(value / BucketSize);
    }
}
