using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One surveyed domestic loft inside the last western bend. The road and
    /// existing furniture win every clearance check. Heights come from the built earth.</summary>
    public sealed class CityPigeonLoftPlan
    {
        private static readonly ConditionalWeakTable<CityLayout, CityPigeonLoftPlan> Plans = new();
        public bool IsEnabled { get; }
        public Vector3 Origin { get; }
        public Quaternion Rotation => Quaternion.Euler(0f, 90f, 0f);
        public Rect Bounds { get; }
        public Vector3 Approach { get; }
        public IReadOnlyList<Vector2[]> SolidPolygons { get; } = Array.Empty<Vector2[]>();

        public static CityPigeonLoftPlan ForLayout(CityLayout layout) =>
            Plans.GetValue(layout, value => new CityPigeonLoftPlan(value));

        private CityPigeonLoftPlan(CityLayout layout)
        {
            if (!layout.HasSummitHome) return;
            var ground = CityHillsideGroundPlan.ForLayout(layout);
            var roads = CityHillsideStreetPlan.ForLayout(layout).Routes;
            var occupied = new List<Rect>();
            foreach (var house in CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings)
            {
                occupied.Add(house.FoundationBounds);
                occupied.Add(house.ApproachBounds);
            }
            foreach (var item in CityHillsideFurniturePlan.ForLayout(layout).Placements)
                occupied.Add(item.AccessBounds);
            foreach (var stair in CityHillsideAccessPlan.ForLayout(layout).AllStairs)
                occupied.AddRange(stair.WalkableBounds);
            foreach (var detail in CityHillsideSiteDetails.ForLayout(layout)) occupied.Add(detail.Bounds);

            string rejection = string.Empty;
            // Bounded survey beside the phone, on the open side of the bend.
            for (int attempt = 0; attempt < 96; attempt++)
            {
                float x = -143.5f + (attempt % 12) * .25f;
                float z = -334.75f + (attempt / 12) * .5f;
                Rect footprint = Rect.MinMaxRect(x - 2.4f, z - 2.1f, x + 1.95f, z + 2.7f);
                bool clear = true;
                foreach (Rect obstacle in occupied)
                    if (footprint.Overlaps(obstacle)) { rejection = "occupied " + obstacle; clear = false; break; }
                if (!clear) continue;
                foreach (var road in roads)
                {
                    foreach (Vector2 corner in Corners(footprint))
                        if (road.Path.Project(corner).DistanceSquared < 8.5f * 8.5f) { rejection = "road " + corner; clear = false; }
                }
                if (!clear) continue;
                var stairFoot = new Vector2(x + 1.7f, z + 2.02f);
                if (!ground.Contains(stairFoot, .35f) || !ground.TrySampleTop(stairFoot, out float floor)) { rejection = "stair footing " + stairFoot; continue; }
                foreach (Vector2 corner in Corners(footprint))
                    if (!ground.TrySampleTop(corner, out float top) || Mathf.Abs(top - floor) > 1.5f) { rejection = "grade " + corner + " top " + top + " floor " + floor; clear = false; }
                if (!clear) continue;
                Origin = new Vector3(x, floor + .025f, z);
                Bounds = footprint;
                // The map arrives beside the phone on unchanged supported earth.
                var approach = new Vector2(x + 1.45f, z + 3.2f);
                Rect arrival = new Rect(approach - Vector2.one * .4f, Vector2.one * .8f);
                foreach (Rect obstacle in occupied)
                    if (arrival.Overlaps(obstacle)) clear = false;
                if (!clear) continue;
                if (!ground.Contains(approach, .4f) || !ground.TrySampleTop(approach, out float approachY)) { rejection = "approach " + approach; continue; }
                Approach = new Vector3(approach.x, approachY, approach.y);
                SolidPolygons = new[] { CityRoadPolygon.Rectangle(footprint) };
                IsEnabled = true;
                return;
            }
            throw new InvalidOperationException("The west Ridge pigeon loft has no surveyed clear placement: " + rejection);
        }

        private static Vector2[] Corners(Rect bounds) => new[]
        {
            bounds.min, new Vector2(bounds.xMax, bounds.yMin), bounds.max,
            new Vector2(bounds.xMin, bounds.yMax)
        };
    }
}
