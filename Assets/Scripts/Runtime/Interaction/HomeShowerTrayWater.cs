using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public sealed partial class HomeShowerWaterEffect
    {
        public const string TrayWaterName = "Home Bathroom Shower Collected Water";
        public const float MaximumWaterDepth = .01f;
        private static Material trayWaterMaterial;
        private static readonly int AmountId = Shader.PropertyToID("_WaterAmount");
        private static readonly int FlowId = Shader.PropertyToID("_DrainFlow");
        private static readonly int ClockId = Shader.PropertyToID("_WaterClock");
        private static readonly int DrainId = Shader.PropertyToID("_DrainWorld");
        private static readonly int NozzleId = Shader.PropertyToID("_NozzleWorld");
        private MaterialPropertyBlock trayProperties;
        private Transform waterRoom;
        private Vector3 trayRestPosition;
        private float trayInflow, trayClock;

        public float TrayWaterAmount { get; private set; }
        public float DrainFlowAmount { get; private set; }
        public Renderer TrayWaterRenderer { get; private set; }
        public Vector3 DrainPosition { get; private set; }
        public Vector3 NozzlePosition => waterRoom != null
            ? waterRoom.TransformPoint(HomeShowerFraming.DripOrigin) : HomeShowerFraming.DripOrigin;

        private void InitializeTrayWater(Transform room)
        {
            waterRoom = room;
            Transform tray = room.Find(TrayWaterName);
            Transform drain = room.Find("Home Bathroom Shower Drain");
            if (tray == null || drain == null)
                throw new InvalidOperationException("The authored shower basin requires its water sheet and corner drain.");
            TrayWaterRenderer = tray.GetComponent<Renderer>();
            if (TrayWaterRenderer == null)
                throw new InvalidOperationException("The shower water sheet requires its imported renderer.");
            DrainPosition = drain.position;
            trayRestPosition = tray.localPosition;
            if (trayWaterMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/HomeShowerTrayWater");
                if (shader == null) throw new InvalidOperationException("Missing shower tray water shader.");
                trayWaterMaterial = new Material(shader)
                {
                    name = "Home Shower Tray Water Shared",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            TrayWaterRenderer.sharedMaterial = trayWaterMaterial;
            TrayWaterRenderer.shadowCastingMode = ShadowCastingMode.Off;
            TrayWaterRenderer.receiveShadows = false;
            trayProperties = new MaterialPropertyBlock();
            ClearTrayWater();
        }

        /// <summary>Inflow fills a shallow film; head-dependent outflow empties it after the valve closes.</summary>
        public void AdvanceTrayWater(float seconds)
        {
            if (TrayWaterRenderer == null || PauseMenuController.IsAnyPaused) return;
            float dt = Mathf.Clamp(seconds, 0f, .1f);
            float outflow = .30f * Mathf.Sqrt(TrayWaterAmount);
            TrayWaterAmount = Mathf.Clamp01(TrayWaterAmount + (trayInflow * .60f - outflow) * dt);
            DrainFlowAmount = Mathf.Clamp01((outflow + (TrayWaterAmount >= .999f ? trayInflow * .30f : 0f)) / .60f);
            trayClock += dt;
            TrayWaterRenderer.enabled = TrayWaterAmount > .002f;
            TrayWaterRenderer.transform.localPosition = trayRestPosition + Vector3.up * (TrayWaterAmount * MaximumWaterDepth);
            TrayWaterRenderer.GetPropertyBlock(trayProperties);
            trayProperties.SetFloat(AmountId, TrayWaterAmount);
            trayProperties.SetFloat(FlowId, DrainFlowAmount);
            trayProperties.SetFloat(ClockId, trayClock);
            trayProperties.SetVector(DrainId, DrainPosition);
            // The head now aims back toward the hero; its vertical projection
            // is outside the tray. Ripples start where the water lands.
            trayProperties.SetVector(NozzleId, waterRoom.TransformPoint(HomeShowerFraming.BasinLanding));
            TrayWaterRenderer.SetPropertyBlock(trayProperties);
        }

        private void ClearTrayWater()
        {
            TrayWaterAmount = DrainFlowAmount = trayInflow = trayClock = 0f;
            if (TrayWaterRenderer == null) return;
            TrayWaterRenderer.enabled = false;
            TrayWaterRenderer.transform.localPosition = trayRestPosition;
        }
    }
}
