using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Turns the city on after dark and off before dawn, through one
    /// property block on one renderer.
    ///
    /// It has no Update. <see cref="MountainRoadAtmosphere"/> already
    /// gates on the game minute and already holds the evaluated sample,
    /// so the view is applied from there beside the tunnel lamp: once a
    /// minute, at no per-frame cost, and with no way for the city's
    /// windows to disagree with the sun that is lighting the rock in
    /// front of them.
    ///
    /// There are no Lights down there and never will be. A window at
    /// ninety-six metres is a bloom, not a source.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MountainRoadVistaLightsController : MonoBehaviour
    {
        /// <summary>
        /// What the additive layer reaches at full night. Chosen against
        /// the mountain grade's `0.72` bloom threshold rather than against
        /// a look: below roughly this the sodium never blooms and the
        /// windows read as flat grey chips.
        /// </summary>
        public const float NightIntensity = 1.35f;

        private static readonly int IntensityId =
            Shader.PropertyToID("_Intensity");

        private Renderer lightsRenderer;
        private MaterialPropertyBlock properties;

        public bool IsInitialized { get; private set; }
        public float AppliedIntensity { get; private set; }

        public void Initialize(Renderer lights)
        {
            lightsRenderer = lights;
            IsInitialized = lights != null;
            Apply(0f);
        }

        /// <summary>
        /// Pure enough to test: the curve from a night factor to the
        /// additive strength.
        ///
        /// It used to gate on the night entirely - "a window at
        /// ninety-six metres is a bloom, not a source" - and the §20 law
        /// repealed that: the city in this matte is the very city whose
        /// every fixture burns always, and from the brink it read DEAD
        /// from before dawn's end to past dusk's start. The day now keeps
        /// the law's two thirds; what the curve still owns is the come-up
        /// - dusk does not smear the remaining third on at ten percent
        /// for an hour, the lamps rise over a few minutes and then they
        /// are on.
        /// </summary>
        public static float EvaluateIntensity(float nightFactor)
        {
            float eased = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.18f, 0.62f, nightFactor));
            return Mathf.Lerp(
                       GameTimeDayNightRules.DayFixtureFloor,
                       1f,
                       eased) *
                   NightIntensity;
        }

        public void Apply(float nightFactor)
        {
            AppliedIntensity = EvaluateIntensity(nightFactor);
            if (lightsRenderer == null)
            {
                return;
            }

            if (properties == null)
            {
                // MaterialPropertyBlock owns native Unity state; create it
                // on first real use, never in a MonoBehaviour initializer.
                properties = new MaterialPropertyBlock();
            }

            lightsRenderer.GetPropertyBlock(properties);
            properties.SetFloat(IntensityId, AppliedIntensity);
            lightsRenderer.SetPropertyBlock(properties);
            lightsRenderer.enabled = AppliedIntensity > 0.001f;
        }
    }
}
