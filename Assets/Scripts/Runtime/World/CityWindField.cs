using UnityEngine;

namespace BarPromenade
{
    internal enum CityWindGeometry { Tree = 1, Cable, Shrub }

    /// <summary>One exterior wind and history for the City and its retained Home view.</summary>
    [DefaultExecutionOrder(340), DisallowMultipleComponent]
    internal sealed class CityWindField : MonoBehaviour
    {
        internal const float MaximumTreeTravel = .75f;
        internal const float MaximumShrubTravel = .20f;
        private static readonly int WindId = Shader.PropertyToID("_CityWind");
        private static readonly int PreviousWindId = Shader.PropertyToID("_CityPreviousWind");
        private static readonly int FrameId = Shader.PropertyToID("_CityWindWorldToCity");
        private static CityWindField active;
        private CityWeatherController weather;
        private Quaternion fromCity;
        private Matrix4x4 toCity;
        private bool configured;

        internal Vector4 Wind { get; private set; }

        internal static WindSample Gust(WindSample weather, float time)
        {
            float strength = 1f - Mathf.Pow(1f - weather.Strength01, 6.75f);
            float pulse = .62f + .24f * Mathf.Sin(time * 1.5f) + .14f * Mathf.Sin(time * 3.1f);
            return new WindSample(weather.DirectionDegrees, strength * pulse);
        }

        internal static WindSample GustForHome(WindSample weather, BuildingLot home)
        {
            var gust = Gust(weather, Time.time);
            var direction = PlayerHomeBalconyGeometry.ToHomeLocalDirection(home, gust.HorizontalDirection);
            return new WindSample(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, gust.Strength01);
        }

        internal void Initialize(CityGrassSurface ground)
        {
            fromCity = ground.FromCityRotation;
            toCity = Matrix4x4.TRS(ground.FromCity(Vector3.zero), fromCity, Vector3.one).inverse;
            configured = true;
            Activate();
        }

        internal void BindWeather(CityWeatherController controller)
        {
            weather = controller;
            Activate();
        }

        internal Vector4 Pack(WindSample sample, float phase) => PackGust(Gust(sample, phase), phase);

        private Vector4 PackGust(WindSample sample, float phase)
        {
            Vector3 direction = fromCity * sample.HorizontalDirection;
            return new Vector4(direction.x, direction.z, sample.Strength01, phase);
        }

        private void OnEnable()
        {
            if (configured) Activate();
        }

        private void Activate()
        {
            if (!isActiveAndEnabled) return;
            active = this;
            Shader.SetGlobalMatrix(FrameId, toCity);
            Wind = ReadWind();
            Publish(Wind);
        }

        private Vector4 ReadWind() => weather != null
            ? PackGust(weather.CurrentGust, Time.time)
            : Pack(GameWeatherRules.EvaluateCurrentWind(), Time.time);

        private void LateUpdate()
        {
            Vector4 previous = Wind;
            if (!GameTimeScaleRuntime.IsPaused) Wind = ReadWind();
            // Grass reads its own field while both streamed districts are
            // visible. Only the active field publishes the global shader frame.
            if (active == this) Publish(previous);
        }

        private void Publish(Vector4 previous)
        {
            Shader.SetGlobalVector(WindId, Wind);
            Shader.SetGlobalVector(PreviousWindId, previous);
        }

        private void OnDisable()
        {
            if (active != this) return;
            active = null;
            Wind = Vector4.zero;
            Publish(Wind);
        }
    }
}
