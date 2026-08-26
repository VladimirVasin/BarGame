using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one bridge between the shaped mountain wind and everything only
    /// the mountain has: the swaying conifer crowns, and the wind bed under
    /// them.
    ///
    /// <see cref="CityWeatherController"/> already carries the wind to
    /// everything the city shares — cloth, the falling snow's drift — so
    /// this deliberately adds no second writer to any of those. It reads
    /// what the controller applied and forwards it, which is why it runs
    /// after it.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(60)]
    public sealed class MountainRoadWindDriver : MonoBehaviour
    {
        /// <summary>
        /// Metres of travel at the tip of a reference-height tree at full
        /// sway. About 3% of that height, which is the band a real conifer
        /// works in, and comfortably more than one pixel of the 640x360
        /// composite at the distance these trees stand.
        /// </summary>
        public const float TipTravelMeters = 1.15f;

        private static readonly int WindParamsId =
            Shader.PropertyToID("_MountainWindParams");
        private static readonly int WindProfileId =
            Shader.PropertyToID("_MountainWindProfile");

        private CityWeatherController weather;
        private MountainRoadWeatherShaper shaper;
        private MountainRoadWindSoundPlayer sound;
        private Vector4 profile;
        private float phaseSeconds;

        public float AppliedSway { get; private set; }
        public bool IsInitialized { get; private set; }

        public void Initialize(
            CityWeatherController weatherController,
            MountainRoadWeatherShaper weatherShaper,
            MountainRoadWindSoundPlayer windSound,
            float routeFootY,
            float routeSummitY,
            float needleMetersPerTile)
        {
            weather = weatherController != null
                ? weatherController
                : throw new ArgumentNullException(nameof(weatherController));
            shaper = weatherShaper ??
                throw new ArgumentNullException(nameof(weatherShaper));
            sound = windSound;
            profile = new Vector4(
                routeFootY,
                routeSummitY,
                TipTravelMeters,
                needleMetersPerTile);
            IsInitialized = true;
            Apply();
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            // SCALED time, unlike the precipitation field's clock. A paused
            // game whose forest keeps swaying reads as a bug, and the car it
            // is being watched from stops dead on the same pause.
            phaseSeconds += Time.deltaTime;
            Apply();
        }

        private void Apply()
        {
            AppliedSway = shaper.SwayAmplitude;
            Vector3 direction = weather.CurrentWind.HorizontalDirection;
            Shader.SetGlobalVector(
                WindParamsId,
                new Vector4(
                    direction.x,
                    AppliedSway,
                    direction.z,
                    phaseSeconds));
            Shader.SetGlobalVector(WindProfileId, profile);
            if (sound != null)
            {
                sound.SetStrength(AppliedSway);
            }
        }

        /// <summary>
        /// Puts the global back to a dead calm. It is a process-wide value
        /// and this scene is the only thing that ever writes it, so leaving
        /// it set would have the next area's asset previews bending nothing
        /// at a wind that is no longer blowing.
        /// </summary>
        private void OnDisable()
        {
            Shader.SetGlobalVector(WindParamsId, Vector4.zero);
        }
    }
}
