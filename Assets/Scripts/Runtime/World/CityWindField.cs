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
        private const float HeightGain = .82f;
        private const float Foot = CityAltitudeVisibility.LowlandTop;
        private const float Climb = CityHillsideStreetRoute.SummitTop - Foot;
        private static readonly int WindId = Shader.PropertyToID("_CityWind");
        private static readonly int PreviousWindId = Shader.PropertyToID("_CityPreviousWind");
        private static readonly int ProfileId = Shader.PropertyToID("_CityWindProfile");
        private static readonly int FrameId = Shader.PropertyToID("_CityWindWorldToCity");
        private static CityWindField active;
        private CityWeatherController weather;
        private Quaternion fromCity;
        private Matrix4x4 toCity;
        private bool configured;

        internal Vector4 Wind { get; private set; }

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

        internal Vector4 Pack(WindSample sample, float phase)
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
            Shader.SetGlobalVector(ProfileId, new Vector4(Foot, 1f / Climb, HeightGain, 0f));
            Shader.SetGlobalMatrix(FrameId, toCity);
            Wind = ReadWind();
            Publish(Wind);
        }

        private Vector4 ReadWind() => Pack(weather != null ? weather.CurrentWind :
            GameWeatherRules.EvaluateCurrentWind(), Time.time);

        private void LateUpdate()
        {
            if (active != this) return;
            Vector4 previous = Wind;
            if (!GameTimeScaleRuntime.IsPaused) Wind = ReadWind();
            Publish(previous);
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
