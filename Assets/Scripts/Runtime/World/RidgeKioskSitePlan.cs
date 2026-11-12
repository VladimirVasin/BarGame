using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One small shop on the house side of Ridge Street. The window faces the road
    /// across its own concrete apron; the public pavement remains full width.</summary>
    public sealed class RidgeKioskSitePlan : CityKioskSitePlan
    {
        private static readonly ConditionalWeakTable<CityLayout, RidgeKioskSitePlan> Sites = new();
        private static readonly RidgeKioskSitePlan Missing = new(Vector3.zero);
        private RidgeKioskSitePlan(Vector3 origin) : base("ridge-kiosk", CityDistrictKind.Residential,
            origin, Quaternion.identity, DefaultNpcPopulation.RidgeKioskSeller, true) { }
        public RidgeKioskCourtPlan Court { get; private set; }

        public static bool TryCreate(CityLayout layout, out RidgeKioskSitePlan site)
        {
            site = layout != null && layout.HasSummitHome ? Sites.GetValue(layout, Survey) : Missing;
            if (site != Missing) return true;
            site = null;
            return false;
        }

        private static RidgeKioskSitePlan Survey(CityLayout layout)
        {
            var home = layout.SummitHome;
            var ridge = CityHillsideQuarterPlan.ForLayout(layout).Links[1];
            var reserved = new List<Rect>();
            foreach (var building in CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings)
            {
                reserved.Add(building.FoundationBounds);
                reserved.Add(building.ApproachBounds);
                reserved.Add(building.CourtBounds);
            }
            var gardenVerge = new List<Rect>();
            foreach (var detail in CityHillsideSiteDetails.ForLayout(layout))
                (detail.Part == CityHillsideSiteDetails.Kind.GardenVerge ? gardenVerge : reserved).Add(detail.Bounds);
            foreach (var stair in CityHillsideAccessPlan.ForLayout(layout).AllStairs)
                reserved.AddRange(stair.WalkableBounds);

            var routes = CityDistrictBusPlanner.CreateRoadRouting(layout);
            foreach (var occupied in CityBusNetworkFurniture.Reservations(routes, layout)) reserved.Add(occupied);
            CityHillsideStreetRoute west = null;
            foreach (var road in CityHillsideStreetPlan.ForLayout(layout).Routes)
                if (road.Bank == CityTerraceTransportBank.West) west = road;
            if (west == null) return Missing;
            // Start beside the west gable, working towards 03-04. Staying west of
            // the house keeps the balcony's eastward view of the truss clear.
            // The same straight sidewalk continues beyond the joint with the
            // west-bank route. The shelter, its queue and both doors are already
            // reserved. The first gap can lie past that joint beside the stop.
            float end = Mathf.Max(west.LaneMinX + 2f, home.ThresholdWorld.x - 60f);
            RidgeKioskSitePlan bareSite = Missing;
            for (float x = home.FoundationBounds.xMin - 2.0f; x >= end; x -= .25f)
            {
                float floor = StreetHeight(x);
                var site = new RidgeKioskSitePlan(new Vector3(x, floor, ridge.Bounds.yMin - ApronFront));
                // Include the eaves, visitor and outer ends of the apron, with a
                // full capsule of clearance beside each pre-existing obstacle.
                Rect extent = Rect.MinMaxRect(x - 1.60f - .35f, site.Origin.z - 1.34f - .35f,
                    x + 1.57f + .35f, site.Apron.yMax);
                if (reserved.Exists(area => extent.Overlaps(area)) || gardenVerge.Exists(area => extent.Overlaps(area))) continue;
                if (bareSite == Missing) bareSite = site;
                // One side may back onto a shelter reservation. Keep its
                // existing through walk and approach the rear court on the free side.
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var court = new RidgeKioskCourtPlan(site.Origin, StreetHeight, attempt == 2 ? -1 : 1, attempt == 0);
                    if (reserved.Exists(court.Overlaps)) continue;
                    site.Court = court;
                    return site;
                }
            }
            return bareSite;

            float StreetHeight(float x) => x >= ridge.Bounds.xMin ? ridge.PavementTopAt(x)
                : west.SampleLaneTop(CityHillsideStreetRoute.LaneCount - 1, x) + CityHillsideBuilding.PavementRise;
        }
    }
}
