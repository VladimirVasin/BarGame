using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one bridge between the shaped mountain wind and everything only
    /// the mountain has: the swaying conifer crowns, and the wind bed under
    /// them.
    ///
    /// Both mountain areas install one now - the road, and the village above
    /// it since `2026-09-10`. The globals it writes are process-wide, so only
    /// ever one at a time; each area gives its own foot and summit heights.
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
        private IMountainWindSwaySource shaper;
        private MountainRoadWindSoundPlayer sound;
        private Vector4 profile;
        private float phaseSeconds;

        public float AppliedSway { get; private set; }
        public bool IsInitialized { get; private set; }

        /// <param name="routeFootY">
        /// Low end of the range the shader normalises a crown's own base
        /// against. Give it the range the TREES actually occupy, not the
        /// route's: the village's lane climbs `6.4 m` while its trees stand
        /// `5-8 m` up a `74` degree wall, so on the lane's scale every crown
        /// saturates the climb term and the amplitude collapses to a constant.
        /// </param>
        /// <param name="windSound">
        /// Optional, and the village passes null on purpose: its storm field
        /// already drives the wind bed, and two writers on one player have no
        /// defined winner.
        /// </param>
        public void Initialize(
            CityWeatherController weatherController,
            IMountainWindSwaySource weatherShaper,
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
