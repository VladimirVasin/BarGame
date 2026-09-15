using System;
using UnityEngine;

namespace BarPromenade
{
    public static class CityFairWorldBuilder
    {
        public static CityFairWorld Build(Transform parent, CityFairPlan plan)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.IsEnabled) return null;
            var host = new GameObject("City Fair");
            host.transform.SetParent(parent, false);
            var world = host.AddComponent<CityFairWorld>();
            world.Build(plan);
            return world;
        }
    }
}
