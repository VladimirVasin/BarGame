using UnityEngine;

namespace BarPromenade
{
    public static class CityEastGuardWorldBuilder
    {
        public static CityEastGuardController Build(Transform parent, CityEastExitPlan exit, Transform hero, Camera camera, int seed)
        {
            if (exit == null || !exit.IsEnabled) return null;
            var host = new GameObject("Eastern Checkpoint Duty");
            host.transform.SetParent(parent, false);
            var controller = host.AddComponent<CityEastGuardController>();
            controller.Initialize(new CityEastGuardPlan(exit), hero, camera, seed);
            return controller;
        }
    }
}
