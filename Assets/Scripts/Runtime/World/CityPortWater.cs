using UnityEngine;

namespace BarPromenade
{
    /// <summary>The near vessel owns a separate wake slot, never the passing fleet's lamps.</summary>
    public sealed class CityPortWater : MonoBehaviour
    {
        private CityPortController port;
        public static void Build(CityPortController controller)
        {
            var water = controller.gameObject.AddComponent<CityPortWater>();
            water.port = controller;
        }
        private void LateUpdate()
        {
            if (port == null || !port.enabled) { Clear(); return; }
            CityPortCycleSnapshot phase = port.Snapshot;
            float strength = 0f;
            if (phase.Stage == CityPortCycleStage.Approach)
                strength = Mathf.Sin(phase.StageProgress * Mathf.PI) * .8f;
            else if (phase.Stage == CityPortCycleStage.Depart)
                strength = Mathf.SmoothStep(0f, .8f, Mathf.InverseLerp(.5f, .8f, phase.StageProgress));
            CitySeaResources.SetPortWake(port.Vessel.position, port.Vessel.forward, strength);
        }
        private static void Clear() => CitySeaResources.SetPortWake(Vector3.zero, Vector3.forward, 0f);
        private void OnDisable() => Clear();
        private void OnDestroy() => Clear();
    }
}
