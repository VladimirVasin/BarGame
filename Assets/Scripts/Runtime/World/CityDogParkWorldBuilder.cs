using System;
using UnityEngine;

namespace BarPromenade
{
    public static class CityDogParkWorldBuilder
    {
        public static GameObject Build(Transform parent, CityLayout layout)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            CityDogParkPlan plan = CityDogParkPlan.ForLayout(layout);
            if (!plan.IsEnabled) return null;
            var host = new GameObject("Residential Dog Park");
            host.transform.SetParent(parent, false);
            host.transform.SetPositionAndRotation(plan.Position, plan.Rotation);
            GameObject model = CityDogParkAssetProvider.CreatePark(host.transform);
            host.AddComponent<CityDogParkController>().Initialize(plan, model.transform);
            return host;
        }
    }
}
