using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A fixed-metre building on a cut-and-fill bench, with a continuous public approach.</summary>
    public sealed class CityHillsideBuilding
    {
        private readonly CityHillsideStreetRoute street;
        private readonly int lane;
        public BuildingLot Lot { get; }
        public CityTerraceTransportBank Bank => street.Bank;
        public Rect FoundationBounds { get; }
        public Rect ApproachBounds { get; }
        public Rect CourtBounds { get; }
        public Vector3 StreetContactWorld { get; }
        public Vector3 DoorWorld { get; }
        internal const float PavementRise = CityStreetSurfacePlanner.SidewalkTop - CityStreetSurfacePlanner.RoadTop;

        internal CityHillsideBuilding(CityHillsideStreetRoute street, int lane, int parcel, float x, int variant)
        {
            this.street = street;
            this.lane = lane;
            bool housing = street.Bank == CityTerraceTransportBank.East;
            var district = housing ? CityDistrictKind.Residential : CityDistrictKind.Industrial;
            Vector3 envelope = CityBuildingAssetProvider.GetExpectedEnvelope(district, variant);
            float laneZ = street.GetLaneZ(lane);
            float top = street.SampleLaneTop(lane, x) + PavementRise;
            float front = laneZ - 8f;
            Vector3 center = new Vector3(x, top - CityFacadeGrid.MassBaseElevation, front - envelope.z * .5f);
            var size = new Vector2(envelope.x, envelope.z);
            FoundationBounds = new Rect(new Vector2(center.x, center.z) - size * .5f, size);
            StreetContactWorld = new Vector3(x, top, laneZ - 4f);
            // The residential front anchor includes the shallow balcony projection.
            // A courier reaches the exterior face of the actual entry on its own level.
            float doorZ = front - (housing ? 1.2f : 0f);
            DoorWorld = new Vector3(x, top, doorZ + .5f);
            CourtBounds = Rect.MinMaxRect(x - 3.3f, doorZ + .02f, x + 3.3f, laneZ - 6.8f);
            ApproachBounds = Rect.MinMaxRect(x - 1.2f, CourtBounds.yMax, x + 1.2f, StreetContactWorld.z);
            var cell = new Vector2Int(housing ? 21 + parcel : -21 - parcel, -2 - lane);
            Lot = new BuildingLot(cell, center, size, envelope.y,
                housing ? new Color(.46f, .49f, .48f) : new Color(.42f, .41f, .38f),
                housing ? "hillside-residential" : "hillside-workshops", district, CityLandUseKind.Building,
                false, false, false, string.Empty, BarActivityKind.None, Vector2Int.zero,
                new Vector3(x, top, doorZ), StreetContactWorld, StreetContactWorld, variant, Vector3.forward);
        }

        public bool TrySampleApproach(Vector2 point, out float top)
        {
            top = DoorWorld.y;
            if (Inside(CourtBounds, point)) return true;
            if (!Inside(ApproachBounds, point)) return false;
            float t = Mathf.InverseLerp(ApproachBounds.yMin, ApproachBounds.yMax, point.y);
            top = Mathf.Lerp(top, street.SampleLaneTop(lane, point.x) + PavementRise, t);
            return true;
        }

        internal static bool Inside(Rect r, Vector2 p) => p.x >= r.xMin - .0001f && p.x <= r.xMax + .0001f &&
            p.y >= r.yMin - .0001f && p.y <= r.yMax + .0001f;
    }

    public sealed class CityHillsideNeighborhoodPlan
    {
        private static readonly ConditionalWeakTable<CityLayout, CityHillsideNeighborhoodPlan> Plans =
            new ConditionalWeakTable<CityLayout, CityHillsideNeighborhoodPlan>();
        public IReadOnlyList<CityHillsideBuilding> Buildings { get; }

        private CityHillsideNeighborhoodPlan(CityLayout layout)
        {
            var buildings = new List<CityHillsideBuilding>();
            var access = CityHillsideAccessPlan.ForLayout(layout);
            foreach (var road in CityHillsideStreetPlan.ForLayout(layout).Routes)
            {
                bool east = road.Bank == CityTerraceTransportBank.East;
                int parcelsPerStreet = east ? 3 : 2;
                for (int lane = 0; lane < CityHillsideStreetRoute.LaneCount; lane++)
                {
                    // The first street starts at the tangent of a 90-degree entry.
                    // Near the hairpins a front or roof stays in the next fog band.
                    float min = road.LaneMinX, max = road.LaneMaxX;
                    if (lane == 0)
                    {
                        if (east) max = layout.RoadGeometry.GetNodePosition(road.MouthNode).x - 9f;
                        else min = layout.RoadGeometry.GetNodePosition(road.MouthNode).x + 9f;
                    }
                    for (int parcel = 0; parcel < parcelsPerStreet; parcel++)
                    {
                        float inset = 9f + 2.1f * ((lane + parcel) % 3);
                        float x = Mathf.Lerp(min + inset, max - inset, parcel / (float)(parcelsPerStreet - 1));
                        int variant = east ? ((lane + parcel) % 3 == 0 ? 4 : (lane + parcel) % 2)
                            : ((lane + parcel) % 3 == 0 ? 7 : parcel == 0 ? 4 : 1);
                        var building = new CityHillsideBuilding(road, lane, parcel, x, variant);
                        if (!BlocksStair(building, access)) buildings.Add(building);
                    }
                }
            }
            Buildings = buildings.AsReadOnly();
        }

        private static bool BlocksStair(CityHillsideBuilding b, CityHillsideAccessPlan access)
        {
            foreach (var stair in access.Shortcuts)
                if (b.FoundationBounds.Overlaps(stair.Bounds) || b.CourtBounds.Overlaps(stair.Bounds) ||
                    b.ApproachBounds.Overlaps(stair.Bounds)) return true;
            return false;
        }

        public static CityHillsideNeighborhoodPlan ForLayout(CityLayout layout) => Plans.GetValue(layout, l => new CityHillsideNeighborhoodPlan(l));

        public static Rect TerrainBounds(CityHillsideStreetRoute road) =>
            Rect.MinMaxRect(road.LaneMinX - 26f, road.GetLaneZ(CityHillsideStreetRoute.LaneCount - 1) - 28f,
                road.LaneMaxX + 26f, -168f);

        // Interior of the hillside between two parallel streets. Level shoulders
        // meet sloping cross-banks in the sixteen metres between pavements.
        internal static float SampleBank(CityLayout layout, CityHillsideStreetRoute road, Vector2 p)
        {
            float firstZ = road.GetLaneZ(0);
            float lane = (firstZ - p.y) / (firstZ - road.GetLaneZ(1));
            int lower = Mathf.Clamp(Mathf.FloorToInt(lane), 0, CityHillsideStreetRoute.LaneCount - 1);
            int upper = Mathf.Min(lower + 1, CityHillsideStreetRoute.LaneCount - 1);
            float low = road.SampleLaneTop(lower, p.x);
            float level = low;
            if (p.y > firstZ)
            {
                Vector2 seam = new Vector2(p.x, -167.99f);
                float north = layout.ElevationPlan.TrySampleSurface(seam, CitySurfaceRole.GroundTop, out float y, out _)
                    ? y : road.Profile.Stations[0].Height;
                level = Mathf.Lerp(north, low, Mathf.InverseLerp(-168f, firstZ + 5f, p.y));
            }
            else if (lower != upper)
                level = Mathf.Lerp(low, road.SampleLaneTop(upper, p.x),
                    Mathf.InverseLerp(road.GetLaneZ(lower) - 5f, road.GetLaneZ(upper) + 5f, p.y));
            return level - .20f;
        }
    }
}
