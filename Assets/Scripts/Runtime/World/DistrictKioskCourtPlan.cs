using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal sealed class DistrictKioskCourtPlan
    {
        private static readonly Rect[] ResidentialFloors = { Rect.MinMaxRect(-6.65f, -1.24f, -1.46f, 2.42f) };
        private static readonly Rect[] IndustrialFloors =
        {
            Rect.MinMaxRect(-4.60f, -2.45f, -1.47f, 2.43f),
            Rect.MinMaxRect(-4.60f, -6.65f, 1.45f, -2.45f)
        };

        internal readonly CityKioskSitePlan Site;
        internal static bool IsNode(string id) => id.Contains("-kiosk:court:", System.StringComparison.Ordinal);
        internal bool Residential => Site.District == CityDistrictKind.Residential;
        internal int Capacity => Residential ? 3 : 4;
        internal string Model => Residential ? "ResidentialKioskCourt" : "IndustrialKioskCourt";
        internal IReadOnlyList<Vector2[]> Floors { get; }
        internal IReadOnlyList<Rect> FloorBounds { get; }
        internal IReadOnlyList<Rect> Obstacles { get; }
        internal Vector3 Gate => Point(-2.53f, 2.12f);
        internal Vector3 Window => Site.Approach;
        internal Vector3 Street => Point(-2.53f, CityKioskSitePlan.ApronFront - 1f + Site.ActualSidewalkWidth * .5f);
        internal Vector3 ServiceTurn => Point(-2.53f, -.85f);
        internal Vector3 ReturnTurn => Point(-2.53f, -1.51f);
        internal Vector3 StaffTurn => Point(-2.35f, -.21f);
        internal Vector3 StaffWork => Residential ? Point(-3.23f, .17f) : Point(-3.2f, -.30f);
        internal Vector3 ChairWork => Point(-3.80f, .31f);
        internal Vector3 ReturnDock => Point(-3.13f, -1.51f);

        private DistrictKioskCourtPlan(CityKioskSitePlan site)
        {
            Site = site;
            Rect[] source = Residential ? ResidentialFloors : IndustrialFloors;
            var floors = new Vector2[source.Length][];
            var bounds = new Rect[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                var rect = source[i];
                floors[i] = site.Polygon(rect.xMin, rect.yMin, rect.xMax, rect.yMax);
                bounds[i] = CityRoadPolygon.Bounds(floors[i]);
            }
            Floors = floors;
            FloorBounds = bounds;
            Obstacles = Residential
                ? new[] { Box(-3.23f, -.72f, .91f, .64f), Box(-5.36f, -.72f, 1.53f, .64f),
                    Box(-4.14f, -.18f, .58f, .47f) }
                : new[] { Box(-2.76f, -5.15f, 1.63f, .65f), Box(.12f, -5.15f, 1.27f, 1.27f),
                    Box(-3.84f, -.85f, .67f, .80f), Box(-4.19f, -4.02f, .14f, .14f) };
        }

        internal static DistrictKioskCourtPlan TryCreate(CityKioskSitePlan site,
            CityBusPlatformClearance ground, IReadOnlyList<Rect> reservations, IReadOnlyList<Rect> paths, out string rejection)
        {
            var court = new DistrictKioskCourtPlan(site);
            if (!court.LampClearance(ground))
            { rejection = "street lamp intersects a court route or fixture"; return null; }

            float foundationDepth = court.Residential ? .47f : 1.18f;
            foreach (Rect bounds in court.FloorBounds)
            {
                if (!ground.IsFixedFootprintClear(bounds))
                { rejection = "footprint obstruction " + bounds; return null; }
                foreach (Rect reserve in reservations)
                    if (reserve.Overlaps(bounds))
                    { rejection = "reserved " + reserve; return null; }
                foreach (Rect path in paths)
                    if (path.Overlaps(bounds))
                    { rejection = "walkway " + path; return null; }
                for (int x = 0; x <= 4; x++)
                for (int z = 0; z <= 4; z++)
                {
                    var point = new Vector3(Mathf.Lerp(bounds.xMin + .04f, bounds.xMax - .04f, x / 4f),
                        site.Origin.y, Mathf.Lerp(bounds.yMin + .04f, bounds.yMax - .04f, z / 4f));
                    if (!ground.TryHeight(point, out float top) || top - point.y > .45f || point.y - top > foundationDepth - .02f)
                    { rejection = $"support {point:F2}, top {top:F2}"; return null; }
                }
            }
            rejection = null;
            return court;
        }

        internal Vector3 Point(float x, float z) => Site.Origin + Site.Rotation * new Vector3(x, 0f, z);
        internal string Node(string name) => Site.StableId + ":court:" + name;
        internal string PlaceNode(int slot) => Node("place:" + slot);
        internal int Table(int slot) => Residential ? (slot == 2 ? 0 : 1) : slot / 2;
        internal bool Seated(int slot) => Residential && slot < 2;
        internal Vector3 Place(int slot) => Residential
            ? Point(slot == 0 ? -5.79f : slot == 1 ? -4.94f : -3.08f, Seated(slot) ? .68f : .16f)
            : slot switch
            {
                0 => Point(-3.18f, -4.43f),
                1 => Point(-2.32f, -4.43f),
                2 => Point(-.35f, -4.30f),
                _ => Point(.53f, -4.30f)
            };

        internal Vector2[] Entrance => Site.Polygon(-3.12f, 1.42f, -1.94f, 3.75f);
        private const float ResidentialAisle = 1.92f;
        internal Vector3 Corner => Residential ? Point(-2.53f, ResidentialAisle) : Point(-2.70f, -3.48f);
        internal Vector3 AisleAt(int slot)
        {
            Vector3 local = Quaternion.Inverse(Site.Rotation) * (Place(slot) - Site.Origin);
            return Point(local.x, Residential ? ResidentialAisle : -3.48f);
        }

        private bool LampClearance(CityBusPlatformClearance ground)
        {
            // Paving can meet the municipal pole. Check the walkable ribbons,
            // separate fixtures and staff motion that actually need clearance.
            foreach (Rect fixture in Obstacles)
                if (!ground.IsStreetLampClear(fixture)) return false;
            if (!Clear(Street, Gate) || !Clear(Gate, Window) ||
                !Clear(StaffTurn, StaffWork) || !Clear(Point(-1.47f, -.21f), StaffTurn))
                return false;

            Vector3 aisle = Corner;
            if (Residential)
            {
                if (!Clear(Gate, Corner) || !Clear(StaffWork, ChairWork) || !Clear(ChairWork, StaffTurn)) return false;
                for (int slot = Capacity - 1; slot >= 0; slot--)
                {
                    Vector3 next = AisleAt(slot);
                    if (!Clear(aisle, next) || !Clear(next, Place(slot))) return false;
                    aisle = next;
                }
                return true;
            }

            if (!Clear(Gate, ServiceTurn) || !Clear(ServiceTurn, ReturnTurn) ||
                !Clear(ReturnTurn, ReturnDock) || !Clear(ReturnTurn, Corner) ||
                !Clear(Corner, AisleAt(0)) || !Clear(AisleAt(0), Place(0)))
                return false;
            for (int slot = 1; slot < Capacity; slot++)
            {
                Vector3 next = AisleAt(slot);
                if (!Clear(aisle, next) || !Clear(next, Place(slot))) return false;
                aisle = next;
            }
            return true;

            bool Clear(Vector3 a, Vector3 b)
            {
                const float radius = CityPedestrianPlanner.AgentRadius;
                return ground.IsStreetLampClear(Rect.MinMaxRect(Mathf.Min(a.x, b.x) - radius,
                    Mathf.Min(a.z, b.z) - radius, Mathf.Max(a.x, b.x) + radius, Mathf.Max(a.z, b.z) + radius));
            }
        }

        internal bool Contains(Vector2 point)
        {
            foreach (Rect floor in FloorBounds)
                if (floor.Contains(point)) return true;
            return false;
        }

        internal bool OverlapsGround(Rect bounds)
        {
            bounds = Rect.MinMaxRect(bounds.xMin - .60f, bounds.yMin - .60f,
                bounds.xMax + .60f, bounds.yMax + .60f);
            foreach (Rect floor in FloorBounds)
                if (floor.Overlaps(bounds)) return true;
            return false;
        }

        internal void AppendFoundationCuts(Rect bounds, ICollection<CityTerrainStreetBoundaryPlan.Cut> cuts)
        {
            for (int index = 0; index < Floors.Count; index++)
            {
                if (!FloorBounds[index].Overlaps(bounds)) continue;
                Vector2[] floor = Floors[index];
                for (int edge = 0; edge < floor.Length; edge++)
                    cuts.Add(new CityTerrainStreetBoundaryPlan.Cut(floor[edge], floor[(edge + 1) % floor.Length]));
            }
        }

        internal float GradeGround(Vector2 point, float top)
        {
            foreach (Rect floor in FloorBounds)
            {
                float x = Mathf.Max(0f, Mathf.Max(floor.xMin - point.x, point.x - floor.xMax));
                float z = Mathf.Max(0f, Mathf.Max(floor.yMin - point.y, point.y - floor.yMax));
                float distance = Mathf.Sqrt(x * x + z * z);
                if (distance >= .60f) continue;
                float blend = 1f - Mathf.SmoothStep(0f, 1f, distance / .60f);
                top = Mathf.Lerp(top, Mathf.Min(top, Site.Origin.y - .045f), blend);
            }
            return top;
        }

        private Rect Box(float x, float z, float width, float depth) => CityRoadPolygon.Bounds(
            Site.Polygon(x - width * .5f, z - depth * .5f, x + width * .5f, z + depth * .5f));
    }
}
