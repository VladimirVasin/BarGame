using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A particular authored standing surface, not merely an XZ coordinate.</summary>
    public readonly struct CityTerraceSurfaceContact
    {
        internal CityTerraceSurfaceContact(int index, string id, float height)
        { SurfaceIndex = index; SurfaceId = id; Height = height; }
        public int SurfaceIndex { get; }
        public string SurfaceId { get; }
        public float Height { get; }
    }

    public sealed partial class CityTerraceDistrictPlan
    {
        private const float SurfaceSeamTolerance = .002f;
        private const float SurfaceSeamHeightTolerance = .03f;
        private Surface[] standingSurfaces = Array.Empty<Surface>();
        private bool[,] surfaceConnections;

        private sealed class Surface
        {
            internal readonly string Id;
            internal readonly Rect Bounds;
            internal readonly Vector3 Start, End;
            internal readonly RoadWalkableArea Clearance = new RoadWalkableArea();
            internal Surface(string id, Rect bounds, Vector3 start, Vector3 end)
            { Id = id; Bounds = bounds; Start = start; End = end; }
            internal float Height(Vector2 point)
            {
                Vector2 axis = new Vector2(End.x - Start.x, End.z - Start.z);
                float t = axis.sqrMagnitude > .000001f
                    ? Mathf.Clamp01(Vector2.Dot(point - new Vector2(Start.x, Start.z), axis) / axis.sqrMagnitude) : 0f;
                return Mathf.Lerp(Start.y, End.y, t);
            }
        }

        private void InitializeSurfaces()
        {
            var surfaces = new List<Surface>(Floors.Count + Flights.Count + throats.Count + 2);
            foreach (CityTerracePilotFloor floor in Floors)
            {
                Vector3 top = World(floor.Center);
                surfaces.Add(new Surface(floor.Id, WorldBounds(floor.Bounds), top, top));
            }
            foreach (CityExteriorStairFlightDescriptor flight in Flights)
                surfaces.Add(new Surface(flight.Id, WorldBounds(CityTerracePilotPlan.FlightBounds(flight)),
                    World(flight.Start), World(flight.End)));
            surfaces.Add(new Surface("street-approach", EntryBounds, EntryStartWorld, EntryEndWorld));
            surfaces.Add(new Surface("lower-pass", LowerPassBounds, LowerPassStartWorld, LowerPassEndWorld));
            for (int i = 0; i < throats.Count; i++)
                surfaces.Add(new Surface("house-throat-" + i, throats[i], houseEntries[i], houseEntries[i]));
            standingSurfaces = surfaces.ToArray();
            surfaceConnections = new bool[standingSurfaces.Length, standingSurfaces.Length];
            for (int i = 0; i < standingSurfaces.Length; i++)
            for (int j = i; j < standingSurfaces.Length; j++)
                surfaceConnections[i, j] = surfaceConnections[j, i] = i == j || ShareSupportedSeam(standingSurfaces[i], standingSurfaces[j]);
            for (int i = 0; i < standingSurfaces.Length; i++)
            {
                Surface surface = standingSurfaces[i];
                // Radius erosion uses only this surface and directly connected
                // neighbours. An overlapping deck on another level contributes
                // no support to an actor walking underneath it.
                var pieces = new List<Vector2[]>();
                for (int j = 0; j < standingSurfaces.Length; j++)
                    if (surfaceConnections[i, j]) pieces.Add(CityRoadPolygon.Rectangle(standingSurfaces[j].Bounds));
                if (surface.Id == "street-approach")
                    pieces.Add(CityRoadPolygon.Rectangle(Rect.MinMaxRect(EntryBounds.xMin - .8f, EntryBounds.yMin,
                        EntryBounds.xMin + .8f, EntryBounds.yMax)));
                else if (surface.Id == "lower-pass")
                    pieces.Add(CityRoadPolygon.Rectangle(Rect.MinMaxRect(LowerPassBounds.xMax - .8f, LowerPassBounds.yMin,
                        LowerPassBounds.xMax + .8f, LowerPassBounds.yMax)));
                surface.Clearance.AddPolygons(pieces);
            }
        }

        private static bool ShareSupportedSeam(Surface a, Surface b)
        {
            float x0 = Mathf.Max(a.Bounds.xMin, b.Bounds.xMin), x1 = Mathf.Min(a.Bounds.xMax, b.Bounds.xMax);
            float z0 = Mathf.Max(a.Bounds.yMin, b.Bounds.yMin), z1 = Mathf.Min(a.Bounds.yMax, b.Bounds.yMax);
            if (x0 > x1 + SurfaceSeamTolerance || z0 > z1 + SurfaceSeamTolerance) return false;
            // A corner alone cannot carry a capsule; a seam has finite width.
            if (x1 - x0 < .02f && z1 - z0 < .02f) return false;
            Vector2 first = new Vector2(x0, z0), last = new Vector2(x1, z1);
            return Mathf.Abs(a.Height(first) - b.Height(first)) <= SurfaceSeamHeightTolerance &&
                Mathf.Abs(a.Height(last) - b.Height(last)) <= SurfaceSeamHeightTolerance;
        }

        public bool ContainsFootprint(Vector2 point)
        {
            if (!IsEnabled || !CityTerracePilotPlan.Finite(point.x) || !CityTerracePilotPlan.Finite(point.y)) return false;
            foreach (Surface surface in standingSurfaces)
                if (CityTerracePilotPlan.Inside(surface.Bounds, point.x, point.y)) return true;
            // Tread/retaining margins own replacement ground without offering
            // an unsupported navigation strip next to their guard rail.
            foreach (Rect cut in cuts) if (CityTerracePilotPlan.Inside(cut, point.x, point.y)) return true;
            return false;
        }

        public bool TryGetSurfaceHeight(Vector3 world, out float height)
        {
            bool found = TryResolveStandingSurface(new Vector2(world.x, world.z), world.y, 0f, out CityTerraceSurfaceContact contact);
            height = contact.Height;
            return found;
        }

        /// <summary>Unhinted map lattice: deliberately chooses the highest supported level.</summary>
        public bool TrySampleStandingTop(Vector2 point, float radius, out float height)
        {
            bool found = Resolve(point, 0f, radius, true, -1, out CityTerraceSurfaceContact contact);
            height = contact.Height;
            return found;
        }

        /// <summary>Typed arrivals retain the authored level nearest their requested world Y.</summary>
        public bool TryResolveStandingSurface(Vector2 point, float preferredY, float radius, out CityTerraceSurfaceContact contact) =>
            Resolve(point, preferredY, radius, false, -1, out contact);

        internal bool TryResolveRuntimeSurface(Vector3 position, float radius, out CityTerraceSurfaceContact contact)
        {
            if (!TryResolveStandingSurface(new Vector2(position.x, position.z), position.y, radius, out contact)) return false;
            return position.y >= contact.Height - .35f && position.y <= contact.Height + 2.25f;
        }

        internal bool TryAdvanceSurface(Vector2 point, CityTerraceSurfaceContact previous, float radius,
            out CityTerraceSurfaceContact contact) => Resolve(point, previous.Height, radius, false, previous.SurfaceIndex, out contact);

        internal bool IsStreetSurface(CityTerraceSurfaceContact contact) =>
            contact.SurfaceId == "street-approach" || contact.SurfaceId == "lower-pass";

        internal bool TryClosestSurfacePoint(Vector3 position, float radius, out Vector3 closest)
        {
            closest = position;
            float best = float.PositiveInfinity;
            bool found = false;
            foreach (Surface surface in standingSurfaces)
            {
                Vector3 candidate = surface.Clearance.ClosestPoint(position, radius);
                candidate.y = position.y;
                if (!TryResolveRuntimeSurface(candidate, radius, out _)) continue;
                float squared = (candidate - position).sqrMagnitude;
                if (squared >= best) continue;
                best = squared; closest = candidate; found = true;
            }
            return found;
        }

        private bool Resolve(Vector2 point, float preferredY, float radius, bool topmost, int from,
            out CityTerraceSurfaceContact contact)
        {
            contact = default;
            if (!IsEnabled || !CityTerracePilotPlan.Finite(point.x) || !CityTerracePilotPlan.Finite(point.y) ||
                !CityTerracePilotPlan.Finite(preferredY) || !CityTerracePilotPlan.Finite(radius) || radius < 0f) return false;
            int best = -1;
            float score = float.PositiveInfinity, height = 0f;
            for (int i = 0; i < standingSurfaces.Length; i++)
            {
                Surface surface = standingSurfaces[i];
                if (from >= 0 && !surfaceConnections[from, i] ||
                    !CityTerracePilotPlan.Inside(surface.Bounds, point.x, point.y) ||
                    !surface.Clearance.Contains(new Vector3(point.x, 0f, point.y), radius)) continue;
                float top = surface.Height(point);
                float candidate = topmost ? -top : Mathf.Abs(top - preferredY);
                if (best >= 0 && candidate >= score - .000001f) continue;
                best = i; score = candidate; height = top;
            }
            if (best < 0) return false;
            contact = new CityTerraceSurfaceContact(best, standingSurfaces[best].Id, height);
            return true;
        }
    }
}
