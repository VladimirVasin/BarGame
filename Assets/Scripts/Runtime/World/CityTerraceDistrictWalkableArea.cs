using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Replaces the old ground only under one terraced precinct. There is one
    /// standing surface per XZ; this is not a stacked-floor navigation system.
    /// The city retains its original spatial index outside this bounded site.
    /// </summary>
    internal sealed class CityTerraceDistrictWalkableArea : IWalkableArea
    {
        private const float SweepSpacing = .10f;
        private const int MaximumSweepSteps = 256;
        private const float MaximumStep = .28f;
        private const float Epsilon = .0001f;
        private readonly CityLayout layout;
        private readonly CityTerraceDistrictPlan plan;
        private readonly RoadWalkableArea city;
        private readonly RoadWalkableArea local = new RoadWalkableArea();
        private readonly Rect bounds;
        private readonly RuntimeOrientedBox[] nearbySidewalks;

        internal CityTerraceDistrictWalkableArea(CityLayout layout, CityTerraceDistrictPlan plan, RoadWalkableArea city)
        {
            this.layout = layout; this.plan = plan; this.city = city;
            var polygons = new List<Vector2[]>();
            foreach (CityTerracePilotFloor floor in plan.Floors)
                polygons.Add(WorldRectangle(floor.Bounds));
            foreach (CityExteriorStairFlightDescriptor flight in plan.Flights)
                polygons.Add(WorldRectangle(CityTerracePilotPlan.FlightBounds(flight)));
            polygons.Add(CityRoadPolygon.Rectangle(plan.EntryBounds));
            foreach (Rect throat in plan.HouseThroats) polygons.Add(CityRoadPolygon.Rectangle(throat));

            // The existing street remains the physical surface outside the
            // apron. Overlap its socket so radius erosion cannot seal the seam.
            Rect socket = plan.EntryBounds;
            Vector3 forward = (plan.EntryEndWorld - plan.EntryStartWorld).normalized;
            if (Mathf.Abs(forward.x) > .5f)
            {
                if (forward.x > 0f) socket.xMin -= .8f; else socket.xMax += .8f;
            }
            else
            {
                if (forward.z > 0f) socket.yMin -= .8f; else socket.yMax += .8f;
            }
            polygons.Add(CityRoadPolygon.Rectangle(socket));
            local.AddPolygons(polygons);
            bounds = Rect.MinMaxRect(Mathf.Min(plan.ReservedBounds.xMin, socket.xMin) - 1f,
                Mathf.Min(plan.ReservedBounds.yMin, socket.yMin) - 1f,
                Mathf.Max(plan.ReservedBounds.xMax, socket.xMax) + 1f,
                Mathf.Max(plan.ReservedBounds.yMax, socket.yMax) + 1f);
            var sidewalks = new List<RuntimeOrientedBox>();
            foreach (RuntimeOrientedBox sidewalk in CityStreetSurfacePlanner.CreateCourtyardPaving(layout).SidewalkGeometry)
            {
                Bounds box = CityBuildingPrototypePlacement.TransformBounds(new Bounds(Vector3.zero, sidewalk.Size),
                    new CityBuildingPrototypePose(sidewalk.Center, sidewalk.Rotation));
                if (bounds.Overlaps(Rect.MinMaxRect(box.min.x, box.min.z, box.max.x, box.max.z))) sidewalks.Add(sidewalk);
            }
            nearbySidewalks = sidewalks.ToArray();
        }

        internal bool IsNear(Vector3 position, float radius) =>
            position.x >= bounds.xMin - radius && position.x <= bounds.xMax + radius &&
            position.z >= bounds.yMin - radius && position.z <= bounds.yMax + radius;

        internal bool IntersectsMove(Vector3 start, Vector3 end, float radius) =>
            Mathf.Min(start.x, end.x) <= bounds.xMax + radius && Mathf.Max(start.x, end.x) >= bounds.xMin - radius &&
            Mathf.Min(start.z, end.z) <= bounds.yMax + radius && Mathf.Max(start.z, end.z) >= bounds.yMin - radius;

        public bool Contains(Vector3 position, float radius = 0f)
        {
            if (!CityTerracePilotPlan.Finite(position) || !CityTerracePilotPlan.Finite(radius) || radius < 0f) return false;
            if (local.Contains(position, radius))
                return plan.ContainsFootprint(new Vector2(position.x, position.z)) || city.ContainsBase(position);
            if (TouchesOwnedSurface(position, radius)) return false;
            return city.ContainsBase(position, radius);
        }

        public Vector3 Constrain(Vector3 currentPosition, Vector3 desiredPosition, float radius = 0f)
        {
            if (!CityTerracePilotPlan.Finite(desiredPosition) || !Contains(currentPosition, radius) ||
                !TrySurface(currentPosition, out float currentTop) ||
                currentPosition.y < currentTop - .35f || currentPosition.y > currentTop + 2.25f)
                return currentPosition;
            Vector3 result = Sweep(currentPosition, desiredPosition, radius);
            if (PlanarSquared(result, desiredPosition) < Epsilon * Epsilon) return result;
            Vector3 x = Sweep(result, new Vector3(desiredPosition.x, desiredPosition.y, result.z), radius);
            Vector3 z = Sweep(result, new Vector3(result.x, desiredPosition.y, desiredPosition.z), radius);
            return PlanarSquared(x, desiredPosition) <= PlanarSquared(z, desiredPosition) ? x : z;
        }

        internal Vector3 ClosestPoint(Vector3 position, float radius)
        {
            Vector3 baseline = city.ClosestPointBase(position, radius);
            if (!IsNear(position, radius) && !IsNear(baseline, radius)) return baseline;
            Vector3 terrace = local.ClosestPoint(position, radius);
            bool hasBase = Contains(baseline, radius), hasTerrace = Contains(terrace, radius);
            if (hasBase && (!hasTerrace || PlanarSquared(baseline, position) <= PlanarSquared(terrace, position))) return baseline;
            if (hasTerrace) return terrace;
            Vector3 fallback = plan.LowerArrivalWorld;
            fallback.y = position.y;
            return Contains(fallback, radius) ? fallback : position;
        }

        private Vector3 Sweep(Vector3 start, Vector3 desired, float radius)
        {
            Vector3 delta = new Vector3(desired.x - start.x, 0f, desired.z - start.z);
            float distance = delta.magnitude;
            if (!CityTerracePilotPlan.Finite(distance) || !TrySurface(start, out float previousTop)) return start;
            if (distance < Epsilon) return new Vector3(start.x, desired.y, start.z);
            float length = Mathf.Min(distance, MaximumSweepSteps * SweepSpacing);
            Vector3 end = start + delta * (length / distance);
            end.y = desired.y;
            int count = Mathf.Max(1, Mathf.CeilToInt(length / SweepSpacing));
            Vector3 last = start;
            for (int i = 1; i <= count; i++)
            {
                Vector3 sample = Vector3.Lerp(start, end, i / (float)count);
                if (Allowed(sample, previousTop, radius, out float nextTop))
                { last = sample; previousTop = nextTop; continue; }
                Vector3 rejected = sample;
                for (int iteration = 0; iteration < 12; iteration++)
                {
                    Vector3 middle = (last + rejected) * .5f;
                    if (Allowed(middle, previousTop, radius, out nextTop)) { last = middle; previousTop = nextTop; }
                    else rejected = middle;
                }
                last.y = desired.y;
                return last;
            }
            return end;
        }

        private bool Allowed(Vector3 point, float previousTop, float radius, out float top)
        {
            top = 0f;
            return Contains(point, radius) && TrySurface(point, out top) && Mathf.Abs(top - previousTop) <= MaximumStep + Epsilon;
        }

        private bool TrySurface(Vector3 position, out float top)
        {
            var point = new Vector2(position.x, position.z);
            if (plan.ContainsFootprint(point)) return plan.TrySampleStandingTop(point, 0f, out top);
            for (int i = 0; i < nearbySidewalks.Length; i++)
                if (nearbySidewalks[i].TrySampleTop(position, out top)) return true;
            if (layout.ElevationPlan.TrySampleSurface(point, CitySurfaceRole.RoadTop, out top, out _)) return true;
            return CityTerrainSurfacePlan.TrySampleGroundTop(layout, point, out top, out _);
        }

        private bool TouchesOwnedSurface(Vector3 position, float radius)
        {
            for (int i = 0; i < plan.GroundCuts.Count; i++)
            {
                Rect cut = plan.GroundCuts[i];
                float dx = position.x - Mathf.Clamp(position.x, cut.xMin, cut.xMax);
                float dz = position.z - Mathf.Clamp(position.z, cut.yMin, cut.yMax);
                if (dx * dx + dz * dz <= radius * radius + Epsilon * Epsilon) return true;
            }
            return false;
        }

        private Vector2[] WorldRectangle(Rect rectangle)
        {
            Vector2[] points = CityRoadPolygon.Rectangle(rectangle);
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 point = plan.World(new Vector3(points[i].x, 0f, points[i].y));
                points[i] = new Vector2(point.x, point.z);
            }
            return points;
        }

        private static float PlanarSquared(Vector3 a, Vector3 b) =>
            (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z);
    }
}
