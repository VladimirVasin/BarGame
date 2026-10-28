using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CityStreetSupportKind
    {
        Asphalt,
        Sidewalk,
        ParkPath
    }

    /// <summary>A material region over the exact triangulated street support.</summary>
    public sealed class CityStreetSupportPatch
    {
        internal const float ContactTolerance = .001f;
        internal CityStreetSupportPatch(IReadOnlyList<Vector3> topVertices,
            CityStreetSupportKind kind, float thickness, int priority,
            RoadEdge? edge = null, Vector2Int? flatNode = null, bool isSupport = true)
        {
            if (topVertices == null || topVertices.Count < 3)
                throw new ArgumentException("A street patch needs at least three vertices.", nameof(topVertices));
            var copy = new Vector3[topVertices.Count];
            Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int index = 0; index < copy.Length; index++)
            {
                copy[index] = topVertices[index];
                Vector2 point = new Vector2(copy[index].x, copy[index].z);
                minimum = Vector2.Min(minimum, point); maximum = Vector2.Max(maximum, point);
            }
            TopVertices = Array.AsReadOnly(copy);
            Kind = kind; Thickness = thickness; Priority = priority;
            Edge = edge; FlatNode = flatNode; IsSupport = isSupport;
            Bounds = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        }

        public IReadOnlyList<Vector3> TopVertices { get; }
        public CityStreetSupportKind Kind { get; }
        public RoadEdge? Edge { get; }
        public Vector2Int? FlatNode { get; }
        // Buried closure skirts contribute side faces, never an upper floor.
        public bool IsSupport { get; }
        public FootstepGroundKind FootstepKind => Kind == CityStreetSupportKind.Sidewalk
            ? FootstepGroundKind.Stone : Kind == CityStreetSupportKind.ParkPath
                ? FootstepGroundKind.Soil : FootstepGroundKind.Concrete;
        public float Thickness { get; }
        public int Priority { get; }
        public Rect Bounds { get; }

        public bool TrySampleTop(Vector2 point, out float top)
        {
            top = float.NegativeInfinity;
            return IsSupport && TrySampleTop(TopVertices, point, out top);
        }

        // The builder uses this same vertex-zero fan. A bilinear interpolation
        // over a nonplanar quad would describe a different collision surface.
        internal static bool TrySampleTop(IReadOnlyList<Vector3> vertices, Vector2 point, out float top)
        {
            top = float.NegativeInfinity;
            bool found = false;
            Vector3 a = vertices[0];
            for (int index = 1; index < vertices.Count - 1; index++)
            {
                Vector3 b = vertices[index], c = vertices[index + 1];
                Vector2 ab = new Vector2(b.x - a.x, b.z - a.z);
                Vector2 ac = new Vector2(c.x - a.x, c.z - a.z);
                Vector2 ap = point - new Vector2(a.x, a.z);
                float determinant = Cross(ab, ac);
                if (Mathf.Abs(determinant) < .0000001f) continue;
                float u = Cross(ap, ac) / determinant;
                float v = Cross(ab, ap) / determinant;
                // Contact tolerance is measured in world metres, as for the
                // legacy oriented-box sampler. Dimensionless barycentric
                // tolerances shrink with narrow/slender triangles.
                float inverseArea = ContactTolerance / Mathf.Abs(determinant);
                float uTolerance = ac.magnitude * inverseArea;
                float vTolerance = ab.magnitude * inverseArea;
                float oppositeTolerance = (ac - ab).magnitude * inverseArea;
                if (u < -uTolerance || v < -vTolerance || u + v > 1f + oppositeTolerance) continue;
                top = Mathf.Max(top, a.y + u * (b.y - a.y) + v * (c.y - a.y));
                found = true;
            }
            return found;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }

    /// <summary>Pure, indexed upstream street support. It never samples terrain,
    /// physics, courtyard allocations or the final ground compositor.</summary>
    public sealed class CityStreetSupportPlan
    {
        private const float BucketSize = 16f;
        private readonly Dictionary<Vector2Int, List<int>> buckets = new Dictionary<Vector2Int, List<int>>();

        private CityStreetSupportPlan(IReadOnlyList<CityStreetSupportPatch> patches)
        {
            Patches = patches;
            for (int index = 0; index < patches.Count; index++)
            {
                if (!patches[index].IsSupport) continue;
                Rect bounds = patches[index].Bounds;
                float tolerance = CityStreetSupportPatch.ContactTolerance;
                for (int x = Bucket(bounds.xMin - tolerance); x <= Bucket(bounds.xMax + tolerance); x++)
                for (int z = Bucket(bounds.yMin - tolerance); z <= Bucket(bounds.yMax + tolerance); z++)
                {
                    var key = new Vector2Int(x, z);
                    if (!buckets.TryGetValue(key, out List<int> entries)) buckets.Add(key, entries = new List<int>());
                    entries.Add(index);
                }
            }
        }

        public IReadOnlyList<CityStreetSupportPatch> Patches { get; }

        // Courtyard paving omits crosswalk/cannery selection, which in turn
        // consumes terrain/courtyard routing. It is safe during terrain planning.
        public static CityStreetSupportPlan ForLayout(CityLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            return CityStreetSurfacePlanner.CreateCourtyardPaving(layout).SupportPlan;
        }

        public static CityStreetSupportPlan FromStreetPlan(CityLayout layout, CityStreetSurfacePlan plan)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (plan.SupportPlan != null) return plan.SupportPlan;
            var patches = new List<CityStreetSupportPatch>();
            AddBoxes(plan.StreetGeometry, CityStreetSupportKind.Asphalt, 20);
            AddRibbons(plan.CurvedStreetRibbons, CityStreetSupportKind.Asphalt, 20);
            AddBoxes(plan.ParkPathGeometry, CityStreetSupportKind.ParkPath, 20);
            AddBoxes(plan.SidewalkGeometry, CityStreetSupportKind.Sidewalk, 30, plan.SidewalkClosureGeometryIndices);
            AddRibbons(plan.CurvedSidewalkRibbons, CityStreetSupportKind.Sidewalk, 30);
            var result = new CityStreetSupportPlan(patches.AsReadOnly());
            plan.SupportPlan = result;
            return result;

            void AddBoxes(IReadOnlyList<RuntimeOrientedBox> boxes, CityStreetSupportKind kind, int priority,
                IReadOnlyList<int> closureGeometryIndices = null)
            {
                HashSet<int> closures = closureGeometryIndices != null && closureGeometryIndices.Count > 0
                    ? new HashSet<int>(closureGeometryIndices) : null;
                int geometryIndex = 0;
                foreach (RuntimeOrientedBox box in boxes)
                {
                    Vector3 half = box.Size * .5f;
                    var top = new[] {
                        box.Center + box.Rotation * new Vector3(-half.x, half.y, -half.z),
                        box.Center + box.Rotation * new Vector3(half.x, half.y, -half.z),
                        box.Center + box.Rotation * new Vector3(half.x, half.y, half.z),
                        box.Center + box.Rotation * new Vector3(-half.x, half.y, half.z)
                    };
                    bool isSupport = closures == null || !closures.Contains(geometryIndex);
                    geometryIndex++;
                    patches.Add(new CityStreetSupportPatch(top, kind, box.Size.y, priority, isSupport: isSupport));
                }
            }

            void AddRibbons(IReadOnlyList<CityStreetRibbonDescriptor> ribbons, CityStreetSupportKind kind, int priority)
            {
                foreach (CityStreetRibbonDescriptor ribbon in ribbons)
                for (int index = 0; index < ribbon.Polygons.Count; index++)
                    patches.Add(new CityStreetSupportPatch(
                        CityStreetSurfacePlanner.GetRibbonTop(layout, ribbon, index), kind, ribbon.Thickness, priority,
                        ribbon.Edge, ribbon.FlatNode));
            }
        }

        public void GetNearby(Rect bounds, List<CityStreetSupportPatch> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            int firstX = Bucket(bounds.xMin), lastX = Bucket(bounds.xMax);
            int firstZ = Bucket(bounds.yMin), lastZ = Bucket(bounds.yMax);
            if ((long)(lastX - firstX + 1) * (lastZ - firstZ + 1) > 4096)
            {
                foreach (CityStreetSupportPatch patch in Patches)
                    if (patch.IsSupport && Touches(patch.Bounds, bounds)) destination.Add(patch);
                return;
            }
            for (int x = firstX; x <= lastX; x++)
            for (int z = firstZ; z <= lastZ; z++)
            {
                if (!buckets.TryGetValue(new Vector2Int(x, z), out List<int> entries)) continue;
                foreach (int index in entries)
                {
                    CityStreetSupportPatch patch = Patches[index];
                    if (Touches(patch.Bounds, bounds) && !destination.Contains(patch)) destination.Add(patch);
                }
            }
        }

        public bool TrySampleTop(Vector2 point, out float top, out FootstepGroundKind kind) =>
            TrySampleTop(point, FootstepGroundKind.None, out top, out kind);

        public bool TrySampleTop(Vector2 point, FootstepGroundKind kind, out float top) =>
            TrySampleTop(point, kind, out top, out _);

        private bool TrySampleTop(Vector2 point, FootstepGroundKind requested, out float top, out FootstepGroundKind kind)
        {
            top = float.NegativeInfinity; kind = FootstepGroundKind.None;
            if (!buckets.TryGetValue(new Vector2Int(Bucket(point.x), Bucket(point.y)), out List<int> entries)) return false;
            int priority = int.MinValue;
            bool found = false;
            foreach (int index in entries)
            {
                CityStreetSupportPatch patch = Patches[index];
                if (requested != FootstepGroundKind.None && patch.FootstepKind != requested) continue;
                if (patch.Priority < priority || !patch.TrySampleTop(point, out float candidate)) continue;
                if (patch.Priority == priority && candidate <= top) continue;
                top = candidate; kind = patch.FootstepKind; priority = patch.Priority; found = true;
            }
            return found;
        }

        private static int Bucket(float coordinate) => Mathf.FloorToInt(coordinate / BucketSize);
        private static bool Touches(Rect a, Rect b) => a.xMin <= b.xMax && a.xMax >= b.xMin &&
            a.yMin <= b.yMax && a.yMax >= b.yMin;
    }
}
