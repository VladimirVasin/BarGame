using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The lighthouse's characteristic: a group-flashing rotating
    /// light. Two opposed beams turn at a steady rate, so a fixed
    /// observer sees a flash twice per revolution — every twenty
    /// seconds — which is the signature of a landfall light, not a
    /// harbour mark.
    ///
    /// Pure and stateless, like the old mol beacon's rules were: a
    /// function of the city seed and the absolute game minute (one
    /// game minute is one real second), so every scene sees the
    /// identical rotation and a test can pin the whole signature
    /// without a scene at all. The seed only turns the phase, so two
    /// cities do not sweep in unison.
    /// </summary>
    public static class CityLighthouseLanternRules
    {
        /// <summary>
        /// One full revolution takes 40 game minutes (40 real
        /// seconds); with two opposed beams the flash period is 20.
        /// </summary>
        public const double RotationDegreesPerMinute = 9.0;

        /// <summary>
        /// How far off the beam axis the observer still catches
        /// light, either side.
        /// </summary>
        public const float FlashHalfWidthDegrees = 14f;

        public static float BeamAzimuthDegreesAt(
            int seed,
            double absoluteGameMinutes)
        {
            if (double.IsNaN(absoluteGameMinutes) ||
                double.IsInfinity(absoluteGameMinutes))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(absoluteGameMinutes),
                    "Absolute game time must be finite.");
            }

            double phase = Hash(seed) % 65536u / 65535d * 360d;
            double azimuth =
                (phase +
                 absoluteGameMinutes * RotationDegreesPerMinute) %
                360d;
            if (azimuth < 0d)
            {
                azimuth += 360d;
            }

            return (float)azimuth;
        }

        /// <summary>
        /// How much of the lantern's light reaches an observer at
        /// the given azimuth: 1 with a beam straight in the eyes,
        /// smooth to 0 outside the flash half-width. Either of the
        /// two opposed beams counts.
        /// </summary>
        public static float FlashFactorAt(
            int seed,
            double absoluteGameMinutes,
            float observerAzimuthDegrees)
        {
            float beam = BeamAzimuthDegreesAt(
                seed,
                absoluteGameMinutes);
            float delta = Mathf.Abs(Mathf.DeltaAngle(
                observerAzimuthDegrees,
                beam));
            delta = Mathf.Min(delta, 180f - delta);
            float t = 1f - Mathf.Clamp01(
                delta / FlashHalfWidthDegrees);
            return t * t * (3f - 2f * t);
        }

        public static float CurrentBeamAzimuthDegrees()
        {
            return BeamAzimuthDegreesAt(
                GameSessionState.CitySeed,
                CurrentAbsoluteGameMinutes());
        }

        public static float CurrentFlashFactor(
            float observerAzimuthDegrees)
        {
            return FlashFactorAt(
                GameSessionState.CitySeed,
                CurrentAbsoluteGameMinutes(),
                observerAzimuthDegrees);
        }

        private static double CurrentAbsoluteGameMinutes()
        {
            return GameSessionState.GameDayIndex *
                   GameTimeDayNightRules.MinutesPerDay +
                   GameSessionState.GameTimeOfDayMinutes;
        }

        private static uint Hash(int seed)
        {
            unchecked
            {
                uint value = (uint)seed ^ 0x4C474854u; // "LGHT"
                value *= 16777619u;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                return value;
            }
        }
    }

    /// <summary>
    /// Drives the lantern assembly. Like the old mol beacon's
    /// controller it deliberately registers with neither light
    /// registry — there is no real Light here at all (a 16 m point
    /// light cannot reach the shore, and the fog would eat its halo),
    /// only additive geometry. Unlike the city's lamps it is NOT
    /// night-gated: a landfall light in permanent fog burns day and
    /// night, so the renderers stay on and the night factor only
    /// swings the intensity between a day floor and the full night
    /// blaze. The rotation is read absolutely from the pure rules
    /// every frame, so there is no drift and every scene agrees on
    /// where the beam points.
    /// </summary>
    public sealed class CityLighthouseLanternController :
        MonoBehaviour
    {
        /// <summary>
        /// The lamp's daytime fraction of its night intensity. Under
        /// the day sky the additive cones read softer against the
        /// brighter fog, which is what a lighthouse by day looks
        /// like: running, not blazing.
        /// </summary>
        private const float DayIntensity = 0.6f;

        private static readonly int IntensityId =
            Shader.PropertyToID("_Intensity");
        private static readonly int UniformId =
            Shader.PropertyToID("_Uniform");

        private Transform lanternPivot;
        private Renderer lensRenderer;
        private Renderer[] beamRenderers;
        private Vector3 lanternWorldPosition;
        private MaterialPropertyBlock propertyBlock;

        public void Initialize(
            Transform pivot,
            Renderer lens,
            Renderer[] beams,
            Vector3 worldPosition)
        {
            lanternPivot = pivot != null
                ? pivot
                : throw new ArgumentNullException(nameof(pivot));
            lensRenderer = lens != null
                ? lens
                : throw new ArgumentNullException(nameof(lens));
            beamRenderers = beams ??
                throw new ArgumentNullException(nameof(beams));
            lanternWorldPosition = worldPosition;
            propertyBlock = new MaterialPropertyBlock();
            Apply();
        }

        private void Update()
        {
            Apply();
        }

        private void Apply()
        {
            if (lanternPivot == null)
            {
                return;
            }

            lanternPivot.localRotation = Quaternion.Euler(
                0f,
                CityLighthouseLanternRules
                    .CurrentBeamAzimuthDegrees(),
                0f);

            // Day and night both: the night factor only swings the
            // intensity between the day floor and the full blaze.
            float night = CityNightSiteLightRegistry.NightFactor;
            float intensity = Mathf.Lerp(DayIntensity, 1f, night);
            for (int index = 0;
                 index < beamRenderers.Length;
                 index++)
            {
                beamRenderers[index].enabled = true;
            }

            lensRenderer.enabled = true;

            // The classic sweep-past flash: when a beam crosses the
            // camera's bearing, the lens itself surges. This is the
            // island's replacement for the fog halo the distance
            // forbids.
            float flash = 0f;
            Camera camera = Camera.main;
            if (camera != null)
            {
                Vector3 toCamera = camera.transform.position -
                                   lanternWorldPosition;
                float observerAzimuth = Mathf.Atan2(
                    toCamera.x,
                    toCamera.z) * Mathf.Rad2Deg;
                flash = CityLighthouseLanternRules.CurrentFlashFactor(
                    observerAzimuth);
            }

            propertyBlock.SetFloat(UniformId, 0f);
            propertyBlock.SetFloat(IntensityId, intensity);
            for (int index = 0;
                 index < beamRenderers.Length;
                 index++)
            {
                beamRenderers[index].SetPropertyBlock(propertyBlock);
            }

            propertyBlock.SetFloat(UniformId, 1f);
            propertyBlock.SetFloat(
                IntensityId,
                intensity * (1f + 1.5f * flash));
            lensRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}
