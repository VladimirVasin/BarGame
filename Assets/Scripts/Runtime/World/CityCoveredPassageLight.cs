using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One physical lens and source. The shared City light pool drives its frame-coherent power.</summary>
    [DisallowMultipleComponent]
    public sealed class CityCoveredPassageLight : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock properties;
        private Color litColor;
        private int materialIndex = -1;
        private float previousFactor = float.NaN;
        public string PlanId { get; private set; }
        public CityCoveredPassageLamp Fixture { get; private set; }
        public Renderer LensRenderer { get; private set; }
        public CityLightHalo FixtureHalo { get; private set; }
        public float PowerMultiplier { get; private set; } = 1f;
        public float AppliedFactor { get; private set; } = 1f;
        public bool IsInitialized => !string.IsNullOrEmpty(PlanId);

        public void Initialize(CityCoveredPassagePlan plan, CityCoveredPassageLamp fixture)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            PlanId = plan.Id; Fixture = fixture;
            name = CityNightAtmosphere.IndustrialTaskLightPrefix + "CoveredPassage";
        }

        public void RegisterLens(Renderer renderer, Color color, int slot = -1)
        {
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            LensRenderer = renderer; litColor = color; materialIndex = slot;
            properties = new MaterialPropertyBlock();
            previousFactor = float.NaN;
            ApplyAt(CityCoveredPassageFlickerEvaluator.AbsoluteSeconds,
                GameTimeDayNightRules.FixtureFactor(CityNightGlowRegistry.NightFactor));
        }

        internal void BindHalo(CityLightHalo halo) { FixtureHalo = halo; previousFactor = float.NaN; }

        public void ApplyAt(double absoluteSeconds, float fixtureFactor)
        {
            PowerMultiplier = isActiveAndEnabled && Fixture.IsFaulty
                ? CityCoveredPassageFlickerEvaluator.Evaluate(absoluteSeconds, Fixture.Seed) : 1f;
            AppliedFactor = Mathf.Clamp01(fixtureFactor) * PowerMultiplier;
            if (previousFactor.Equals(AppliedFactor)) return;
            previousFactor = AppliedFactor;
            if (LensRenderer != null && properties != null)
            {
                Read(properties);
                Color color = new(litColor.r * AppliedFactor, litColor.g * AppliedFactor,
                    litColor.b * AppliedFactor, litColor.a);
                properties.SetColor(BaseColorId, color); properties.SetColor(ColorId, color);
                Write(properties);
            }
            if (FixtureHalo != null)
            {
                FixtureHalo.SetIntensityFactor(AppliedFactor);
                FixtureHalo.SetVisible(gameObject.activeInHierarchy && AppliedFactor > .0001f);
            }
        }

        internal void RestoreSteady(float fixtureFactor)
        {
            PowerMultiplier = 1f; AppliedFactor = fixtureFactor; previousFactor = fixtureFactor;
            if (LensRenderer != null && properties != null)
            {
                Read(properties);
                Color color = new(litColor.r * fixtureFactor, litColor.g * fixtureFactor,
                    litColor.b * fixtureFactor, litColor.a);
                properties.SetColor(BaseColorId, color); properties.SetColor(ColorId, color);
                Write(properties);
            }
            if (FixtureHalo != null)
            {
                FixtureHalo.SetIntensityFactor(fixtureFactor);
                FixtureHalo.SetVisible(gameObject.activeInHierarchy);
            }
        }

        private void OnDisable() => RestoreSteady(GameTimeDayNightRules.FixtureFactor(CityNightGlowRegistry.NightFactor));
        private void OnDestroy() => RestoreSteady(GameTimeDayNightRules.FixtureFactor(CityNightGlowRegistry.NightFactor));
        private void Read(MaterialPropertyBlock block)
        { if (materialIndex < 0) LensRenderer.GetPropertyBlock(block); else LensRenderer.GetPropertyBlock(block, materialIndex); }
        private void Write(MaterialPropertyBlock block)
        { if (materialIndex < 0) LensRenderer.SetPropertyBlock(block); else LensRenderer.SetPropertyBlock(block, materialIndex); }
    }
}
