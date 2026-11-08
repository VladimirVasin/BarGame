using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The kiosk switch owns this one circuit; the City pool retains its source and daylight profile.</summary>
    [DefaultExecutionOrder(225), DisallowMultipleComponent]
    public sealed class CityGlassReturnWorkLight : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        public bool IsPowered { get; private set; } = true;
        public float PowerMultiplier => IsPowered && isActiveAndEnabled ? 1f : 0f;
        public Light PooledLight { get; private set; }
        private Renderer lens;
        private Color litColor;
        private MaterialPropertyBlock properties;
        private CityGlassReturnWorker worker;
        private CityGlassReturnGrille passiveGrille;
        private CityNightAtmosphere lightOwner;
        private float baseIntensity, fixtureFactor = 1f, appliedFactor = float.NaN;

        public void Initialize(Renderer lens, Color litColor)
        {
            this.lens = lens ? lens : throw new ArgumentNullException(nameof(lens));
            this.litColor = litColor;
            properties = new MaterialPropertyBlock();
            fixtureFactor = GameTimeDayNightRules.FixtureFactor(CityNightGlowRegistry.NightFactor);
            SetPower(CalendarPower());
        }

        public void BindWorker(CityGlassReturnWorker owner)
        {
            if (worker) worker.WorkLightChanged -= SetPower;
            worker = owner;
            if (worker) worker.WorkLightChanged += SetPower;
            SetPower(worker ? worker.WorkLightOn : CalendarPower());
        }

        internal void BindPassiveGrille(CityGlassReturnGrille grille)
        {
            passiveGrille = grille;
            SetPower(CalendarPower());
        }

        internal void BindLight(Light light, float nominalIntensity, CityNightAtmosphere owner)
        {
            PooledLight = light;
            baseIntensity = nominalIntensity;
            lightOwner = owner;
            Apply();
        }

        internal void SetFixtureFactor(float factor)
        {
            fixtureFactor = Mathf.Clamp01(factor);
            Apply();
        }

        private static bool CalendarPower() => GameSessionState.GameTimeOfDayMinutes >= CityGlassReturnWorker.ShiftStartMinute &&
            GameSessionState.GameTimeOfDayMinutes < CityGlassReturnWorker.ShiftEndMinute;

        private void SetPower(bool powered)
        {
            IsPowered = powered;
            passiveGrille?.TrySetClosedAmount(powered ? 0f : 1f, force: true);
            Apply();
        }

        private void LateUpdate()
        {
            // The passive Home view has no NPC; it projects this circuit from the same work hours.
            if (!worker && IsPowered != CalendarPower()) SetPower(CalendarPower());
        }

        private void Apply()
        {
            float factor = IsPowered ? fixtureFactor : 0f;
            bool active = isActiveAndEnabled && gameObject.activeInHierarchy;
            if (PooledLight)
            {
                bool sourceActive = active && lightOwner && lightOwner.isActiveAndEnabled;
                PooledLight.intensity = sourceActive ? baseIntensity * factor : 0f;
                PooledLight.enabled = sourceActive && factor > .0001f;
            }
            if (factor.Equals(appliedFactor) || !lens || properties == null) return;
            appliedFactor = factor;
            lens.GetPropertyBlock(properties);
            Color color = new(litColor.r * factor, litColor.g * factor, litColor.b * factor, 1f);
            properties.SetColor(BaseColor, color); properties.SetColor(ColorId, color);
            lens.SetPropertyBlock(properties);
        }

        private void OnEnable() { appliedFactor = float.NaN; Apply(); }
        private void OnDisable() { if (PooledLight) PooledLight.enabled = false; }
        private void OnDestroy() { if (worker) worker.WorkLightChanged -= SetPower; }
    }
}
