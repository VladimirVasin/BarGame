using UnityEngine;

namespace BarPromenade
{
    // The middle gallery's central opal lamp has a loose contact.
    [DisallowMultipleComponent]
    internal sealed class RidgeMotelGalleryLamp : MonoBehaviour
    {
        private static readonly int BaseColour = Shader.PropertyToID("_BaseColor");
        private static readonly int Colour = Shader.PropertyToID("_Color");
        private Light source;
        private Renderer lens;
        private MaterialPropertyBlock properties;
        private Color fullColour;
        private float strength;
        private float previous = -1f;

        internal void Initialize(Light lamp, Renderer cover, float fullIntensity, Color coverColour)
        {
            source = lamp;
            lens = cover;
            strength = fullIntensity;
            fullColour = coverColour;
            properties = new MaterialPropertyBlock();
            lens.GetPropertyBlock(properties);
            LateUpdate();
        }

        private void LateUpdate() => ApplyAt(CityCoveredPassageFlickerEvaluator.AbsoluteSeconds);

        internal void ApplyAt(double seconds)
        {
            // Offset the shared contact pattern from the tunnel circuit.
            float contact = CityTunnelLampFlickerEvaluator.Evaluate(seconds + 6.8d).PowerMultiplier;
            Apply(Mathf.Lerp(.35f, 1f, contact));
        }

        private void OnDisable() => Apply(1f);

        private void Apply(float contact)
        {
            float factor = GameTimeDayNightRules.FixtureFactor(CityNightGlowRegistry.NightFactor) * contact;
            if (factor == previous || !source || !lens) return;
            previous = factor;
            source.intensity = strength * factor;
            Color colour = new(fullColour.r * factor, fullColour.g * factor,
                fullColour.b * factor, fullColour.a);
            properties.SetColor(BaseColour, colour);
            properties.SetColor(Colour, colour);
            lens.SetPropertyBlock(properties);
        }
    }
}
