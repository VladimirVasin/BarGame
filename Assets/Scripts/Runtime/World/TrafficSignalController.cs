using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class TrafficSignalController : MonoBehaviour
    {
        public const float BlinkPeriod = 1.3f;
        public const float LitFraction = 0.55f;

        private static readonly Color LitColor =
            new Color(3.4f, 1.65f, 0.38f, 1f);
        private static readonly Color UnlitColor =
            new Color(0.10f, 0.045f, 0.012f, 1f);

        private Renderer[] amberLenses = Array.Empty<Renderer>();
        private CityLightHalo[] amberHalos = Array.Empty<CityLightHalo>();
        private float phaseOffset;
        private bool stateApplied;

        public bool IsLit { get; private set; }
        public float PhaseOffset => phaseOffset;
        public IReadOnlyList<Renderer> AmberLenses => amberLenses;
        public IReadOnlyList<CityLightHalo> AmberHalos => amberHalos;

        public void Initialize(
            IReadOnlyList<Renderer> lenses,
            IReadOnlyList<CityLightHalo> halos,
            float offset)
        {
            if (lenses == null)
            {
                throw new ArgumentNullException(nameof(lenses));
            }

            if (halos == null)
            {
                throw new ArgumentNullException(nameof(halos));
            }

            if (halos.Count != lenses.Count)
            {
                throw new ArgumentException(
                    "Traffic signal halos must match the lens count.",
                    nameof(halos));
            }

            amberLenses = new Renderer[lenses.Count];
            amberHalos = new CityLightHalo[halos.Count];
            for (int index = 0; index < lenses.Count; index++)
            {
                if (lenses[index] == null || halos[index] == null)
                {
                    throw new ArgumentException(
                        "Traffic signal visuals cannot contain null.",
                        nameof(lenses));
                }

                amberLenses[index] = lenses[index];
                amberHalos[index] = halos[index];
            }

            phaseOffset = Mathf.Repeat(offset, BlinkPeriod);
            stateApplied = false;
            ApplyTime(Time.unscaledTime);
        }

        public void ApplyTime(float unscaledTime)
        {
            bool shouldBeLit = EvaluateLit(unscaledTime, phaseOffset);
            if (stateApplied && IsLit == shouldBeLit)
            {
                return;
            }

            IsLit = shouldBeLit;
            stateApplied = true;
            Color color = IsLit ? LitColor : UnlitColor;
            for (int index = 0; index < amberLenses.Length; index++)
            {
                RuntimePrimitiveFactory.SetColor(amberLenses[index], color);
                amberHalos[index].SetVisible(IsLit);
            }
        }

        public static bool EvaluateLit(float unscaledTime, float offset)
        {
            float phase = Mathf.Repeat(unscaledTime + offset, BlinkPeriod);
            return phase < BlinkPeriod * LitFraction;
        }

        private void Update()
        {
            ApplyTime(Time.unscaledTime);
        }
    }
}
