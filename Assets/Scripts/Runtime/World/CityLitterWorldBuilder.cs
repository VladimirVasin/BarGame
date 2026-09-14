using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Instantiates the city-wide litter plan on the ground it was planned
    /// for: a sidewalk part rests on its own pavement box, a ground part on
    /// the continuous plan surface under it. Both are exactly what the
    /// street and terrain builders drew, so no scene mesh is read back.
    /// </summary>
    public static class CityLitterWorldBuilder
    {
        public const string RootName = "City Litter";

        internal static Transform Build(Transform parent, CityLayout layout, CityLitterPlan plan)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var instancer = new CityLitterInstancer(CityLitterCatalog.Load());
            CityStreetSurfacePlan streets = CityStreetSurfacePlanner.Create(layout);
            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            foreach (CityLitterPart part in plan.Parts)
            {
                CityLitterGroundSample sample = part.Zone == CityLitterZone.Sidewalk
                    ? SidewalkSampler(streets.SidewalkGeometry[part.Surface])
                    : GroundSampler(layout, layout.Surfaces[part.Surface]);
                instancer.Place(root, part.Id, part.Item, part.Position, part.Rotation, part.Scale, sample);
            }
            GameLog.Debug("city", "city_litter_built", GameLog.Field("parts", plan.Parts.Count),
                GameLog.Field("variants", instancer.VariantCount), GameLog.Field("triangles", plan.TriangleCount),
                GameLog.Field("solids", plan.SolidCount), GameLog.Field("sidewalk", plan.GetCount(CityLitterZone.Sidewalk)),
                GameLog.Field("lot", plan.GetCount(CityLitterZone.LotGround)), GameLog.Field("park", plan.GetCount(CityLitterZone.Park)),
                GameLog.Field("beach", plan.GetCount(CityLitterZone.Beach)));
            return root;
        }

        private static CityLitterGroundSample SidewalkSampler(RuntimeOrientedBox box)
        {
            return (Vector2 point, out float height) => box.TrySampleTop(new Vector3(point.x, 0f, point.y), out height);
        }

        private static CityLitterGroundSample GroundSampler(CityLayout layout, CitySurfaceDescriptor surface)
        {
            Rect bounds = CityLitterGeometry.Expand(surface.WorldBounds, .01f);
            return (Vector2 point, out float height) =>
            {
                if (!bounds.Contains(point)) { height = 0f; return false; }
                height = CityTerrainSurfacePlan.SampleTop(layout, surface, point);
                return true;
            };
        }
    }
}
