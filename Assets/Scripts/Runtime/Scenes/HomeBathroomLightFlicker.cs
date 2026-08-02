using System;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class HomeBathroomLightFlicker : MonoBehaviour
    {
        public const float CycleSeconds = 6.40f;
        public const float BurstStartSeconds = 4.70f;
        public const float MinimumFactor = 0.10f;
        public const float MaximumFactor = 1.00f;

        private Light bathroomLight;
        private Light spillLight;
        private HomeBathroomLightFixture fixture;
        private float bathroomBaseIntensity;
        private float spillBaseIntensity;
        private float startedAt;

        public bool IsInitialized { get; private set; }
        public float CurrentFactor { get; private set; } = 1f;
        public Light BathroomLight => bathroomLight;
        public Light SpillLight => spillLight;
        public HomeBathroomLightFixture Fixture => fixture;

        public void Initialize(
            Light localBathroomLight,
            Light bathroomSpillLight,
            HomeBathroomLightFixture bathroomFixture = null)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The Home bathroom light flicker is already initialized.");
            }

            if (localBathroomLight == null)
            {
                throw new ArgumentNullException(
                    nameof(localBathroomLight));
            }

            if (bathroomSpillLight == null)
            {
                throw new ArgumentNullException(
                    nameof(bathroomSpillLight));
            }

            bathroomLight = localBathroomLight;
            spillLight = bathroomSpillLight;
            fixture = bathroomFixture;
            bathroomBaseIntensity = bathroomLight.intensity;
            spillBaseIntensity = spillLight.intensity;
            startedAt = Time.unscaledTime;
            IsInitialized = true;
            Apply(1f);
        }

        public static float EvaluateFactor(float elapsedSeconds)
        {
            float cycle = Mathf.Repeat(
                Mathf.Max(0f, elapsedSeconds),
                CycleSeconds);
            if (cycle < BurstStartSeconds)
            {
                return MaximumFactor;
            }

            if (cycle < 4.78f)
            {
                return 0.18f;
            }

            if (cycle < 4.88f)
            {
                return MaximumFactor;
            }

            if (cycle < 4.96f)
            {
                return 0.42f;
            }

            if (cycle < 5.04f)
            {
                return MinimumFactor;
            }

            if (cycle < 5.14f)
            {
                return 0.82f;
            }

            if (cycle < 5.22f)
            {
                return 0.28f;
            }

            return MaximumFactor;
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            Apply(EvaluateFactor(Time.unscaledTime - startedAt));
        }

        private void Apply(float factor)
        {
            CurrentFactor = Mathf.Clamp(
                factor,
                MinimumFactor,
                MaximumFactor);
            if (bathroomLight != null)
            {
                bathroomLight.intensity =
                    bathroomBaseIntensity * CurrentFactor;
            }

            if (spillLight != null)
            {
                spillLight.intensity =
                    spillBaseIntensity * CurrentFactor;
            }

            fixture?.SetFactor(CurrentFactor);
        }
    }
}
