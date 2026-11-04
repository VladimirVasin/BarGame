using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Bounded layered navigation for one precinct. A sweep retains the surface
    /// identity through authored seams; collision/gravity alone move the hero Y.
    /// Other City ground keeps its existing spatial index and semantics.
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
        private readonly RoadWalkableArea sockets = new RoadWalkableArea();
        private readonly Rect bounds, shortcutBounds;
        private readonly Rect[] ownedBounds;
        private readonly RuntimeOrientedBox[] nearbySidewalks;

        internal CityTerraceDistrictWalkableArea(CityLayout layout, CityTerraceDistrictPlan plan, RoadWalkableArea city)
        {
            this.layout = layout; this.plan = plan; this.city = city;
            Rect west = plan.EntryBounds; west.xMin -= .8f;
            Rect east = plan.LowerPassBounds; east.xMax += .8f;
            Rect lowerEntry = plan.LowerCourtEntryBounds; lowerEntry.xMin -= .8f;
            sockets.Add(west); sockets.Add(east); sockets.Add(lowerEntry);
            bounds = Rect.MinMaxRect(Mathf.Min(plan.ReservedBounds.xMin, west.xMin) - 1f,
                Mathf.Min(plan.ReservedBounds.yMin, west.yMin) - 1f,
                Mathf.Max(plan.ReservedBounds.xMax, east.xMax, plan.LowerCourtBounds.xMax) + 1f,
                Mathf.Max(plan.ReservedBounds.yMax, east.yMax, plan.LowerCourtBounds.yMax) + 1f);
            if (plan.HasShortcut)
            {
                shortcutBounds = plan.ShortcutBounds;
                shortcutBounds = Rect.MinMaxRect(shortcutBounds.xMin - 1f, shortcutBounds.yMin - 1f,
                    shortcutBounds.xMax + 1f, shortcutBounds.yMax + 1f);
                sockets.Add(plan.ShortcutLowSocket); sockets.Add(plan.ShortcutHighSocket);
            }
            var owned = new List<Rect>(plan.GroundCuts);
            owned.Add(plan.BridgeWorldBounds);
            ownedBounds = owned.ToArray();
            var sidewalks = new List<RuntimeOrientedBox>();
            foreach (RuntimeOrientedBox sidewalk in CityStreetSurfacePlanner.CreateCourtyardPaving(layout).SidewalkGeometry)
            {
                Bounds box = CityBuildingPrototypePlacement.TransformBounds(new Bounds(Vector3.zero, sidewalk.Size),
                    new CityBuildingPrototypePose(sidewalk.Center, sidewalk.Rotation));
                Rect footprint = Rect.MinMaxRect(box.min.x, box.min.z, box.max.x, box.max.z);
                if (bounds.Overlaps(footprint) || plan.HasShortcut && shortcutBounds.Overlaps(footprint)) sidewalks.Add(sidewalk);
            }
            nearbySidewalks = sidewalks.ToArray();
        }

        internal bool IsNear(Vector3 position, float radius) =>
            InRegion(position, position, radius, bounds) || plan.HasShortcut && InRegion(position, position, radius, shortcutBounds);

        internal bool IntersectsMove(Vector3 start, Vector3 end, float radius) =>
            InRegion(start, end, radius, bounds) || plan.HasShortcut && InRegion(start, end, radius, shortcutBounds);

        private static bool InRegion(Vector3 start, Vector3 end, float radius, Rect bounds) =>
            Mathf.Min(start.x, end.x) <= bounds.xMax + radius && Mathf.Max(start.x, end.x) >= bounds.xMin - radius &&
            Mathf.Min(start.z, end.z) <= bounds.yMax + radius && Mathf.Max(start.z, end.z) >= bounds.yMin - radius;

        public bool Contains(Vector3 position, float radius = 0f)
        {
            if (!CityTerracePilotPlan.Finite(position) || !CityTerracePilotPlan.Finite(radius) || radius < 0f) return false;
            if (plan.ContainsFootprint(new Vector2(position.x, position.z)))
                return plan.TryResolveRuntimeSurface(position, radius, out _);
            if (sockets.Contains(position, radius) && city.ContainsBase(position)) return true;
            if (TouchesOwnedSurface(position, radius)) return false;
            return city.ContainsBase(position, radius);
        }

        public Vector3 Constrain(Vector3 currentPosition, Vector3 desiredPosition, float radius = 0f)
        {
            if (!CityTerracePilotPlan.Finite(desiredPosition) || !Contains(currentPosition, radius) ||
                !TryContact(currentPosition, radius, out CityTerraceSurfaceContact current) ||
                currentPosition.y < current.Height - .35f || currentPosition.y > current.Height + 2.25f)
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
            bool hasBase = Contains(baseline, radius);
            bool hasTerrace = plan.TryClosestSurfacePoint(position, radius, out Vector3 terrace);
            if (hasBase && (!hasTerrace || PlanarSquared(baseline, position) <= PlanarSquared(terrace, position))) return baseline;
            return hasTerrace ? terrace : position;
        }

        private Vector3 Sweep(Vector3 start, Vector3 desired, float radius)
        {
            Vector3 delta = new Vector3(desired.x - start.x, 0f, desired.z - start.z);
            float distance = delta.magnitude;
            if (!CityTerracePilotPlan.Finite(distance) || !TryContact(start, radius, out CityTerraceSurfaceContact previous)) return start;
            if (distance < Epsilon) return new Vector3(start.x, SafeY(start.y, desired.y, previous), start.z);
            float length = Mathf.Min(distance, MaximumSweepSteps * SweepSpacing);
            Vector3 end = start + delta * (length / distance);
            end.y = desired.y;
            int count = Mathf.Max(1, Mathf.CeilToInt(length / SweepSpacing));
            Vector3 last = start;
            for (int i = 1; i <= count; i++)
            {
                Vector3 sample = Vector3.Lerp(start, end, i / (float)count);
                if (Allowed(sample, previous, radius, out CityTerraceSurfaceContact next))
                { last = sample; previous = next; continue; }
                Vector3 rejected = sample;
                for (int iteration = 0; iteration < 12; iteration++)
                {
                    Vector3 middle = (last + rejected) * .5f;
                    if (Allowed(middle, previous, radius, out next)) { last = middle; previous = next; }
                    else rejected = middle;
                }
                last.y = SafeY(start.y, desired.y, previous);
                return last;
            }
            end.y = SafeY(start.y, desired.y, previous);
            return end;
        }

        private static float SafeY(float currentY, float requestedY, CityTerraceSurfaceContact support) =>
            requestedY >= support.Height - .35f && requestedY <= support.Height + 2.25f ? requestedY : currentY;

        private bool Allowed(Vector3 point, CityTerraceSurfaceContact previous, float radius,
            out CityTerraceSurfaceContact contact)
        {
            contact = default;
            Vector2 xz = new Vector2(point.x, point.z);
            if (plan.ContainsFootprint(xz))
            {
                bool found = previous.SurfaceIndex >= 0
                    ? plan.TryAdvanceSurface(xz, previous, radius, out contact)
                    : plan.TryResolveStandingSurface(xz, previous.Height, radius, out contact);
                if (!found || previous.SurfaceIndex < 0 && !plan.IsStreetSurface(contact)) return false;
            }
            else
            {
                if (previous.SurfaceIndex >= 0 && !plan.IsStreetSurface(previous) ||
                    !Contains(point, radius) || !TryContact(point, radius, out contact)) return false;
            }
            return Mathf.Abs(contact.Height - previous.Height) <= MaximumStep + Epsilon;
        }

        private bool TryContact(Vector3 position, float radius, out CityTerraceSurfaceContact contact)
        {
            var point = new Vector2(position.x, position.z);
            if (plan.ContainsFootprint(point)) return plan.TryResolveRuntimeSurface(position, radius, out contact);
            for (int i = 0; i < nearbySidewalks.Length; i++)
                if (nearbySidewalks[i].TrySampleTop(position, out float sidewalkTop))
                { contact = new CityTerraceSurfaceContact(-1, "city-ground", sidewalkTop); return true; }
            bool found = layout.ElevationPlan.TrySampleSurface(point, CitySurfaceRole.RoadTop, out float top, out _) ||
                CityTerrainSurfacePlan.TrySampleGroundTop(layout, point, out top, out _);
            contact = new CityTerraceSurfaceContact(-1, "city-ground", top);
            return found;
        }

        private bool TouchesOwnedSurface(Vector3 position, float radius)
        {
            for (int i = 0; i < ownedBounds.Length; i++)
            {
                Rect cut = ownedBounds[i];
                float dx = position.x - Mathf.Clamp(position.x, cut.xMin, cut.xMax);
                float dz = position.z - Mathf.Clamp(position.z, cut.yMin, cut.yMax);
                if (dx * dx + dz * dz <= radius * radius + Epsilon * Epsilon) return true;
            }
            return false;
        }

        private static float PlanarSquared(Vector3 a, Vector3 b) =>
            (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z);
    }
}
