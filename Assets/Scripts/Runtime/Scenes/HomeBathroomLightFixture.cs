using System;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class HomeBathroomLightFixture : MonoBehaviour
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");

        private MaterialPropertyBlock properties;
        private Renderer tubeRenderer;
        private CityLightHalo halo;
        private Color baseEmitterColor;

        public bool IsInitialized { get; private set; }
        public float CurrentFactor { get; private set; } = 1f;
        public Renderer TubeRenderer => tubeRenderer;
        public CityLightHalo Halo => halo;

        public void Initialize(
            Renderer emitterRenderer,
            CityLightHalo emitterHalo,
            Color emitterColor)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The Home bathroom light fixture is already initialized.");
            }

            if (emitterRenderer == null)
            {
                throw new ArgumentNullException(nameof(emitterRenderer));
            }

            if (emitterHalo == null)
            {
                throw new ArgumentNullException(nameof(emitterHalo));
            }

            tubeRenderer = emitterRenderer;
            halo = emitterHalo;
            baseEmitterColor = emitterColor;
            properties = new MaterialPropertyBlock();
            IsInitialized = true;
            SetFactor(1f);
        }

        public void SetFactor(float factor)
        {
            CurrentFactor = Mathf.Clamp(factor, 0f, 1.05f);
            if (!IsInitialized || tubeRenderer == null)
            {
                return;
            }

            Color displayed = baseEmitterColor * CurrentFactor;
            displayed.a = baseEmitterColor.a;
            tubeRenderer.GetPropertyBlock(properties);
            properties.SetColor(BaseColorId, displayed);
            properties.SetColor(ColorId, displayed);
            tubeRenderer.SetPropertyBlock(properties);
            halo?.SetVisible(CurrentFactor > 0.16f);
        }
    }
}
