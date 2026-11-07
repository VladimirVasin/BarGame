using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The surveyed domestic loft and its accessible household yard in the last western bend.</summary>
    public sealed class CityPigeonLoftPlan
    {
        private static readonly ConditionalWeakTable<CityLayout, CityPigeonLoftPlan> Plans = new();
        public bool IsEnabled { get; }
        public Vector3 Origin { get; }
        public Quaternion Rotation => Quaternion.Euler(0f, 90f, 0f);
        public Rect Bounds { get; }
        public Vector3 Approach { get; }
        public CityPigeonCourtyardPlan Courtyard { get; }
        public IReadOnlyList<Vector2[]> SolidPolygons { get; } = Array.Empty<Vector2[]>();

        public static CityPigeonLoftPlan ForLayout(CityLayout layout) =>
            Plans.GetValue(layout, value => new CityPigeonLoftPlan(value));

        private CityPigeonLoftPlan(CityLayout layout)
        {
            if (!layout.HasSummitHome) return;
            var ground = CityHillsideGroundPlan.ForLayout(layout);
            // Keep the already built cabin at its surveyed feet after the phone moves.
            const float x = -141.5f, z = -334.75f;
            var stairFoot = new Vector2(x + 1.7f, z + 2.02f);
            if (!ground.Contains(stairFoot, .35f) || !ground.TrySampleTop(stairFoot, out float floor))
                throw new InvalidOperationException("The west Ridge pigeon loft has lost its stair footing.");

            Origin = new Vector3(x, floor + .025f, z);
            Bounds = Rect.MinMaxRect(x - 2.4f, z - 2.1f, x + 1.95f, z + 2.7f);
            foreach (var p in CityRoadPolygon.Rectangle(Bounds))
                if (!ground.TrySampleTop(p, out float top) || Mathf.Abs(top - floor) > 1.5f)
                    throw new InvalidOperationException("The west Ridge pigeon loft exceeds its footing range at " + p);

            Courtyard = new CityPigeonCourtyardPlan(ground);
            Approach = Courtyard.Arrival;
            var obstacles = new List<Vector2[]> { CityRoadPolygon.Rectangle(Bounds) };
            obstacles.AddRange(Courtyard.SolidPolygons);
            SolidPolygons = obstacles.AsReadOnly();

            var furniture = CityHillsideFurniturePlan.ForLayout(layout);
            var occupied = new List<Rect>(furniture.BusReservations);
            foreach (var house in CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings)
            {
                occupied.Add(house.FoundationBounds);
                occupied.Add(house.ApproachBounds);
                occupied.Add(house.CourtBounds);
            }
            foreach (var item in furniture.Placements) occupied.Add(item.AccessBounds);
            foreach (var stair in CityHillsideAccessPlan.ForLayout(layout).AllStairs) occupied.AddRange(stair.WalkableBounds);
            foreach (var detail in CityHillsideSiteDetails.ForLayout(layout)) occupied.Add(detail.Bounds);

            foreach (var solid in obstacles)
            {
                Rect bounds = CityRoadPolygon.Bounds(solid);
                foreach (var r in occupied)
                    if (r.Overlaps(bounds))
                        throw new InvalidOperationException("Pigeon courtyard obstacle " + bounds + " overlaps reserved ground " + r);
            }
            foreach (var street in CityHillsideStreetPlan.ForLayout(layout).Routes)
            {
                // The largest one-sided bus apron is .9 m beyond the 5 m street.
                // A full 6 m to every section leaves the physical sidewalk clear.
                foreach (var post in Courtyard.FencePosts)
                    if (street.Path.Project(new Vector2(post.x, post.z)).DistanceSquared < 36f)
                        throw new InvalidOperationException("The courtyard fence enters the street at " + post);
            }
            IsEnabled = true;
        }
    }
}
