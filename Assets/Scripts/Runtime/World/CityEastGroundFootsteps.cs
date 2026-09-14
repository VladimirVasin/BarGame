using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The shared surface-audio overlay follows the baked material
    /// outline, rather than exposing the former rectangular land-use seam.</summary>
    internal sealed class CityEastGroundFootsteps : FootstepGroundOverlay
    {
        private CityLayout layout;
        private CityEastGroundTransitionPlan plan;
        private FootstepGroundKind surfaceKind;

        internal void Configure(CityLayout city, CityEastGroundTransitionPlan transition, FootstepGroundKind kind)
        {
            layout = city; plan = transition; surfaceKind = kind;
            Initialize(kind, Array.Empty<RuntimeOrientedBox>());
        }

        private void OnEnable()
        {
            if (plan != null) Initialize(surfaceKind, Array.Empty<RuntimeOrientedBox>());
        }

        public override bool IsActiveAt(Vector3 feet)
        {
            var point = new Vector2(feet.x, feet.z);
            if (plan == null || !plan.Bounds.Contains(point) ||
                (plan.SoilWeight(point) >= .5f) != (surfaceKind == FootstepGroundKind.Soil)) return false;
            return CityTerrainSurfacePlan.TrySampleGroundTop(layout, point, out float top, out _) &&
                Mathf.Abs(feet.y - top) <= MaximumContactOffset;
        }
    }
}
