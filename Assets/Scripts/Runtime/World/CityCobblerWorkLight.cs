using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The worker's switch powers the desk source, lens and halo as one circuit.</summary>
    [DefaultExecutionOrder(240), DisallowMultipleComponent]
    public sealed class CityCobblerWorkLight : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly Color LitColor = new(2.65f, 1.95f, 1.13f);
        private static readonly Color UnlitColor = new(.14f, .13f, .10f, 1f);
        private const float NightIntensity = 3.1f;
        private Light source;
        private Renderer lens;
        private CityLightHalo halo;
        private CityCobblerWorker worker;
        private MaterialPropertyBlock properties;
        private float appliedFactor = float.NaN;
        public bool IsPowered { get; private set; }

        public void Initialize(Light light, Renderer bulb, CityLightHalo glow)
        {
            source = light ? light : throw new ArgumentNullException(nameof(light));
            lens = bulb ? bulb : throw new ArgumentNullException(nameof(bulb));
            halo = glow;
            properties = new MaterialPropertyBlock();
            RefreshImmediate();
        }

        public void BindWorker(CityCobblerWorker owner)
        {
            if (worker) worker.WorkLightChanged -= SetPower;
            worker = owner;
            if (worker) worker.WorkLightChanged += SetPower;
            RefreshImmediate();
        }

        public void RefreshImmediate()
        {
            double seconds = (GameSessionState.GameDayIndex * 1440d + GameSessionState.GameTimeOfDayMinutes) /
                GameTimeState.GameMinutesPerRealSecond;
            SetPower(worker ? worker.WorkLightOn : CityCobblerWorker.IsLampOnAt(seconds));
        }

        private void SetPower(bool powered)
        {
            IsPowered = powered;
            Apply();
        }

        private void LateUpdate()
        {
            if (!worker) RefreshImmediate();
            else Apply();
        }

        private void Apply()
        {
            if (!source || !lens || properties == null) return;
            float factor = isActiveAndEnabled && IsPowered
                ? GameTimeDayNightRules.FixtureFactor(CityNightSiteLightRegistry.NightFactor) : 0f;
            if (factor.Equals(appliedFactor)) return;
            appliedFactor = factor;
            source.intensity = NightIntensity * factor;
            source.enabled = factor > 0f;
            if (halo) halo.SetIntensityFactor(factor);
            lens.GetPropertyBlock(properties);
            Color color = factor > 0f ? LitColor * factor : UnlitColor;
            color.a = 1f;
            properties.SetColor(BaseColor, color);
            properties.SetColor(ColorId, color);
            lens.SetPropertyBlock(properties);
        }

        private void OnEnable() { if (source) RefreshImmediate(); }
        private void OnDisable() => Apply();
        private void OnDestroy()
        {
            if (worker) worker.WorkLightChanged -= SetPower;
        }
    }
}
