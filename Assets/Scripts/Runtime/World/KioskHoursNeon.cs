using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>A slow chasing circuit on the kiosk's large fascia-mounted 24/7 sign.</summary>
    [DisallowMultipleComponent]
    public sealed class KioskHoursNeon : MonoBehaviour
    {
        public const int SectorCount = 13;
        public const float CycleSeconds = 3.6f, MinimumPower = .35f;
        private const string SectorPrefix = "RidgeKiosk_KioskNeon";
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly Color Pink = new(2.4f, .075f, .75f, 1f);
        private readonly Renderer[] sectors = new Renderer[SectorCount];
        private readonly float[] powers = new float[SectorCount];
        private MaterialPropertyBlock properties;
        private Camera observer;
        private double nextSample, nextCameraCheck;
        public bool AutoAdvance { get; set; } = true;
        public float PhaseSeconds { get; private set; }
        public int LeadingSector { get; private set; }
        public IReadOnlyList<Renderer> Sectors => sectors;

        internal void Initialize(Transform model)
        {
            properties = new MaterialPropertyBlock();
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.name.StartsWith(SectorPrefix, StringComparison.Ordinal)) continue;
                if (!int.TryParse(renderer.name.Substring(SectorPrefix.Length), out int index) ||
                    index < 0 || index >= SectorCount || sectors[index])
                    throw new InvalidOperationException("Invalid kiosk neon sector " + renderer.name);
                sectors[index] = renderer;
                renderer.sharedMaterial = CityNightResources.EmissiveMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            for (int i = 0; i < sectors.Length; i++)
                if (!sectors[i]) throw new InvalidOperationException("Missing kiosk neon sector " + i);
            ApplyAt(Clock());
        }

        public float GetSectorPower(int index) => powers[index];

        public void ApplyAt(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0d) throw new ArgumentOutOfRangeException(nameof(seconds));
            PhaseSeconds = (float)(seconds % CycleSeconds);
            float head = PhaseSeconds / CycleSeconds * SectorCount;
            LeadingSector = Mathf.FloorToInt(head) % SectorCount;
            float daylight = GameTimeDayNightRules.FixtureFactor(CityNightGlowRegistry.NightFactor);
            for (int i = 0; i < sectors.Length; i++)
            {
                // The next sector rises gently before the head passes it;
                // three trailing sectors fade, and the rest retain a dim tube.
                float behind = Mathf.Repeat(head - i + .5f, SectorCount) - .5f;
                float wave = behind < 0f ? Mathf.SmoothStep(0f, 1f, (behind + .5f) * 2f) :
                    1f - Mathf.SmoothStep(0f, 1f, behind / 3f);
                powers[i] = Mathf.Lerp(MinimumPower, 1f, wave);
                Paint(i, isActiveAndEnabled ? powers[i] * daylight : 0f);
            }
        }

        public void AdvanceAt(double seconds)
        {
            if (isActiveAndEnabled && !GameTimeScaleRuntime.IsPaused) ApplyAt(seconds);
        }

        private static double Clock() => (GameSessionState.GameDayIndex * 1440d +
            GameSessionState.GameTimeOfDayMinutes) / GameTimeState.GameMinutesPerRealSecond;

        private void LateUpdate()
        {
            if (!AutoAdvance || properties == null || GameTimeScaleRuntime.IsPaused) return;
            double now = Time.unscaledTimeAsDouble;
            if (now < nextSample) return;
            if (!observer && now >= nextCameraCheck) { observer = Camera.main; nextCameraCheck = now + 1d; }
            float distanceSq = observer ? (observer.transform.position - transform.position).sqrMagnitude : 0f;
            nextSample = now + (distanceSq < 15f * 15f ? 1d / 15d : distanceSq < 40f * 40f ? .2d : 1d);
            ApplyAt(Clock());
        }

        private void Paint(int index, float strength)
        {
            if (!sectors[index] || properties == null) return;
            Color color = Pink * strength; color.a = 1f;
            properties.SetColor(BaseColor, color); properties.SetColor(ColorId, color);
            sectors[index].SetPropertyBlock(properties);
        }

        private void OnDisable()
        {
            for (int i = 0; i < sectors.Length; i++) Paint(i, 0f);
        }

        private void OnEnable()
        {
            nextSample = 0d;
            if (properties != null) ApplyAt(Clock());
        }
    }
}
