using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Soft, nonzero voltage dips in the valve's independent red practical.</summary>
    [DisallowMultipleComponent]
    public sealed class WarehouseValveLampFlicker : MonoBehaviour
    {
        internal const float MinimumFactor = .45f, CycleSeconds = 11.8f;
        private const float SampleInterval = .05f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private CombatTestRoot owner;
        private Renderer lens;
        private MaterialPropertyBlock originalProperties, properties;
        private Color baseColor, emission;
        private float baseIntensity, elapsed, sampleElapsed;
        private bool initialized;

        internal Light Source { get; private set; }
        internal float CurrentFactor { get; private set; } = 1f;
        internal float Elapsed => elapsed;

        internal void Initialize(GameObject fixture, Light light, CombatTestRoot warehouse)
        {
            if (initialized) throw new InvalidOperationException("Warehouse valve lamp already initialized.");
            if (fixture == null || light == null || warehouse == null) throw new ArgumentNullException();
            foreach (Renderer candidate in fixture.GetComponentsInChildren<Renderer>(true))
                if (candidate.name.EndsWith("__SignalRed", StringComparison.Ordinal)) { lens = candidate; break; }
            if (lens == null) throw new InvalidOperationException("Warehouse valve lamp lacks its authored lens.");
            owner = warehouse;
            Source = light;
            baseIntensity = light.intensity;
            originalProperties = new MaterialPropertyBlock();
            properties = new MaterialPropertyBlock();
            lens.GetPropertyBlock(originalProperties);
            Material material = lens.sharedMaterial;
            baseColor = originalProperties.HasColor(BaseColorId) ? originalProperties.GetColor(BaseColorId) : material.GetColor(BaseColorId);
            emission = originalProperties.HasColor(EmissionColorId) ? originalProperties.GetColor(EmissionColorId) : material.GetColor(EmissionColorId);
            elapsed = sampleElapsed = 0f;
            initialized = true;
        }

        private void Update() => Advance(Time.unscaledDeltaTime);

        internal void Advance(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!initialized || !isActiveAndEnabled || owner == null || !owner.isActiveAndEnabled ||
                GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused ||
                SceneTransitionService.IsTransitioning || !GameInput.MovementFocused) return;
            elapsed = Mathf.Repeat(elapsed + seconds, CycleSeconds);
            sampleElapsed += seconds;
            if (sampleElapsed < SampleInterval) return;
            sampleElapsed %= SampleInterval;
            Apply(EvaluateFactor(elapsed));
        }

        private static float EvaluateFactor(float time)
            => Mathf.Min(Dip(time, 1.65f, .60f, 1f - MinimumFactor),
                Mathf.Min(Dip(time, 2.65f, .45f, .34f), Dip(time, 7.4f, .70f, .48f)));

        private static float Dip(float time, float centre, float duration, float depth)
        {
            float distance = Mathf.Abs(time - centre) / (duration * .5f);
            return distance >= 1f ? 1f : 1f - depth * (1f - Mathf.SmoothStep(0f, 1f, distance));
        }

        private void Apply(float factor)
        {
            if (CurrentFactor == factor) return;
            CurrentFactor = factor;
            Source.intensity = baseIntensity * factor;
            lens.GetPropertyBlock(properties);
            properties.SetColor(BaseColorId, new Color(baseColor.r * factor, baseColor.g * factor, baseColor.b * factor, baseColor.a));
            properties.SetColor(EmissionColorId, emission * factor);
            lens.SetPropertyBlock(properties);
        }

        private void Restore()
        {
            if (!initialized) return;
            if (Source != null) Source.intensity = baseIntensity;
            if (lens != null) lens.SetPropertyBlock(originalProperties);
            CurrentFactor = 1f;
        }

        internal void Dispose()
        {
            Restore();
            initialized = false;
            enabled = false;
        }

        private void OnEnable()
        { if (initialized) Apply(EvaluateFactor(elapsed)); }
        private void OnDisable() => Restore();
        private void OnDestroy() => Restore();
    }
}
