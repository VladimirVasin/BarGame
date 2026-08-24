using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Advances only the authored idle/sit playables of the four cafe figures;
    /// it never navigates or turns them into an ambient population.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MountainRoadCafeTableauAnimator : MonoBehaviour
    {
        private IReadOnlyList<CityPedestrianPresentation> presentations =
            Array.Empty<CityPedestrianPresentation>();

        public void Initialize(
            IReadOnlyList<CityPedestrianPresentation> sourcePresentations)
        {
            presentations = sourcePresentations ??
                throw new ArgumentNullException(
                    nameof(sourcePresentations));
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            for (int index = 0; index < presentations.Count; index++)
            {
                CityPedestrianPresentation presentation =
                    presentations[index];
                if (presentation != null)
                {
                    presentation.Advance(deltaTime, false);
                }
            }
        }
    }
}
