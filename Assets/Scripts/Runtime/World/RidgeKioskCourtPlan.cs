using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Metre-space court on the back (-Z) side of the Ridge kiosk.</summary>
    public sealed class RidgeKioskCourtPlan
    {
        public const int TableCount = CityResidentLifeRules.KioskCourtTables;
        public const int PlacesPerTable = CityResidentLifeRules.KioskCourtPlacesPerTable;
        public const int Capacity = CityResidentLifeRules.KioskCourtCapacity;
        public const float TableHeight = 1.06f, TableRadius = .65f, CanopyRadius = 1.70f;
        internal const float StandingRadius = 1.07f;
        public const float SlabBack = -10.70f, SlabFront = -1.27f, SlabHalfWidth = 5f;
        internal const float EntryZ = 2.70f, FrontAisle = -2.66f, BackAisle = -6.75f;
        private const float WaitingZ = -1.76f;
        private static readonly float[] WaitingX = { -.95f, 0f, .93f, 1.79f, 3.44f, 4.31f };
        internal const string NodePrefix = "ridge-kiosk-court:";
        private const float Blend = .75f;
        private readonly Func<float, float> streetHeight;
        private readonly Rect[] approaches;
        private readonly Rect[] reservations;
        private readonly Vector3[] tables;

        internal RidgeKioskCourtPlan(Vector3 origin, Func<float, float> streetHeight, int entrySide, bool bothSides)
        {
            Origin = origin;
            this.streetHeight = streetHeight;
            EntrySide = entrySide;
            Slab = WorldRect(-SlabHalfWidth, SlabBack, SlabHalfWidth, SlabFront);
            float slabJoin = SlabFront - CityGroundTraversalPlanner.ConnectorReach;
            var paths = new List<Rect>(4) { WorldRect(-2.55f, slabJoin, -1.45f, .75f) };
            if (entrySide < 0 || bothSides) paths.Add(WorldRect(-3.91f, slabJoin, -2.12f, CityKioskSitePlan.ApronFront));
            if (entrySide > 0 || bothSides) paths.Add(WorldRect(1.66f, slabJoin, 3.46f, CityKioskSitePlan.ApronFront));
            paths.Add(entrySide > 0 ? WorldRect(1.28f, 1.77f, 2.86f, 3.36f) : WorldRect(-3.16f, 1.77f, -1.28f, 3.36f));
            approaches = paths.ToArray();
            reservations = new Rect[approaches.Length + 1];
            reservations[0] = Slab;
            Array.Copy(approaches, 0, reservations, 1, approaches.Length);
            // The capsule mask erodes each floor. Share a strip of existing
            // pavement at its current grade without expanding the site survey.
            var walkable = (Rect[])reservations.Clone();
            for (int path = 2; path < walkable.Length - 1; path++)
                walkable[path].yMax += CityGroundTraversalPlanner.ConnectorReach;
            WalkableFloors = Array.AsReadOnly(walkable);
            tables = new[] { Point(-2.15f, -4.94f), Point(2.15f, -4.94f), Point(0f, -8.43f) };
            Phone = Point(-4.22f, -2.43f);
            PhoneBounds = WorldRect(-4.79f, -3.17f, -3.65f, -1.69f);
            PhoneAccess = WorldRect(-4.82f, -3.27f, -2.45f, -1.58f);
            var solids = new Vector2[TableCount][];
            for (int index = 0; index < TableCount; index++)
            {
                Vector3 table = tables[index];
                // Match the widest ring of the imported tabletop.
                var rim = new Vector2[16];
                for (int edge = 0; edge < rim.Length; edge++)
                {
                    float angle = 2f * Mathf.PI * edge / rim.Length;
                    rim[edge] = new Vector2(table.x + TableRadius * Mathf.Cos(angle), table.z + TableRadius * Mathf.Sin(angle));
                }
                solids[index] = rim;
            }
            Solids = Array.AsReadOnly(solids);
        }

        public Vector3 Origin { get; }
        public Rect Slab { get; }
        public Vector3 Phone { get; }
        public Rect PhoneBounds { get; }
        public Rect PhoneAccess { get; }
        public IReadOnlyList<Rect> Reservations => reservations;
        internal IReadOnlyList<Rect> WalkableFloors { get; }
        internal IReadOnlyList<Vector2[]> Solids { get; }
        internal int EntrySide { get; }
        internal float SideAisle => EntrySide > 0 ? 2.56f : -3.01f;
        internal Vector3 Entry => Point(SideAisle, EntryZ);
        internal Vector3 Window => Point(.72f, 1.93f);
        internal Vector3 SmokePoint => Point(-1.86f, -1.92f);
        internal Vector3 Table(int table) => tables[table];
        internal static string PlaceId(int slot) => NodePrefix + "place-" + slot;
        internal static string WaitingId(int slot) => NodePrefix + "wait-" + slot;
        internal Vector3 Wait(int slot) => Point(WaitingX[slot], WaitingZ);
        internal Vector3 Point(float x, float z) => Origin + new Vector3(x, 0f, z);

        internal Vector3 Place(int slot)
        {
            Vector3 table = tables[slot / PlacesPerTable];
            return table + PlaceDirection(slot) * StandingRadius;
        }

        // The two radial spurs run out from the table toward an aisle.
        // Nobody occupies the front of a table or the centre aisle.
        internal static Vector3 PlaceDirection(int slot) => slot % PlacesPerTable == 0 ? Vector3.left : Vector3.right;

        internal bool Overlaps(Rect bounds)
        {
            foreach (Rect reservation in reservations) if (reservation.Overlaps(bounds)) return true;
            return false;
        }

        internal float PathTop(Vector2 p) => Mathf.Lerp(Origin.y, streetHeight(p.x),
            Mathf.InverseLerp(1.35f, CityKioskSitePlan.ApronFront, p.y - Origin.z));

        internal float Grade(Vector2 p, float baseHeight)
        {
            foreach (Rect approach in approaches)
            {
                // Stop at the edge of the existing pavement. Its collision and
                // longitudinal profile keep ownership through the junction.
                if (p.y <= approach.yMax + .01f)
                    baseHeight = BlendHeight(approach, p, baseHeight, PathTop(p));
            }
            return BlendHeight(Slab, p, baseHeight, Origin.y - .018f);
        }

        internal bool TrySurface(Vector2 p, out float top)
        {
            if (Slab.Contains(p)) { top = Origin.y; return true; }
            foreach (Rect approach in approaches)
                if (approach.Contains(p)) { top = PathTop(p); return true; }
            top = 0f;
            return false;
        }

        internal void AddGradeBreaks(Rect bounds, SortedSet<float> xs, SortedSet<float> zs)
        {
            foreach (Rect r in reservations)
            {
                Rect expanded = Rect.MinMaxRect(r.xMin - Blend, r.yMin - Blend, r.xMax + Blend, r.yMax + Blend);
                if (!bounds.Overlaps(expanded)) continue;
                Add(xs, bounds.xMin, bounds.xMax, expanded.xMin, r.xMin, r.xMax, expanded.xMax);
                Add(zs, bounds.yMin, bounds.yMax, expanded.yMin, r.yMin, r.yMax, expanded.yMax);
            }
            static void Add(SortedSet<float> axis, float min, float max, params float[] values)
            {
                foreach (float value in values) if (value > min && value < max) axis.Add(value);
            }
        }

        private Rect WorldRect(float x, float z, float right, float front) =>
            Rect.MinMaxRect(Origin.x + x, Origin.z + z, Origin.x + right, Origin.z + front);

        private static float BlendHeight(Rect r, Vector2 p, float from, float to)
        {
            float distance = Mathf.Max(Mathf.Max(r.xMin - p.x, p.x - r.xMax), Mathf.Max(r.yMin - p.y, p.y - r.yMax));
            return distance >= Blend ? from : Mathf.Lerp(from, to, 1f - Mathf.SmoothStep(0f, 1f, distance / Blend));
        }
    }
}
