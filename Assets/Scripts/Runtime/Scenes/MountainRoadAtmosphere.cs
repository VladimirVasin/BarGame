using System;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>
    /// Owns the cold exterior grade and every real practical outside a
    /// building on this mountain: the lamp at the tunnel exit and the
    /// one over the summit freight dock. Each light, its visible
    /// fixture and its positional ballast share one plan anchor, and
    /// both are applied on the same per-minute pass that moves the sun
    /// - which is also where the distant valley gets its night.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MountainRoadAtmosphere : MonoBehaviour
    {
        private const float TunnelLampBaseIntensity = 2.15f;
        private const float TunnelLampNightBoost = 0.42f;

        /// <summary>
        /// The one light the summit yard owns. Cold mercury on a wooden
        /// pole over the freight dock, burning at every hour: the terminal
        /// is lit because somebody still pays for it to be, not because it
        /// is dark.
        ///
        /// The number is on THIS AREA's scale, which is not the city's.
        /// The documented city practicals run `31` to `240`; every fixture
        /// on this mountain runs `1.65` to `16` — the tunnel lamp at
        /// `2.15`, the cafe counter at `10.5`. Set from the city list this
        /// lamp stood at `38`, three and a half times the brightest thing
        /// up here, and blew the yard out.
        /// </summary>
        private const float YardLampDayIntensity = 9.5f;

        private const float YardLampNightBoost = 0.55f;
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");
        private static readonly Color TunnelLensColor =
            new Color(0.88f, 0.63f, 0.32f, 1f);

        private MountainRoadPlan plan;
        private VolumeProfile runtimeProfile;
        private Renderer tunnelLampLens;
        private Light yardLamp;
        private MountainRoadVistaLightsController vista;
        private MaterialPropertyBlock tunnelLampProperties;
        private int appliedDay = int.MinValue;
        private int appliedMinute = int.MinValue;
        private float elapsedSeconds;

        public bool IsInitialized { get; private set; }
        public Volume GlobalVolume { get; private set; }
        public Light TunnelLamp { get; private set; }

        /// <summary>The yard practical over the freight dock.</summary>
        public Light YardLamp => yardLamp;
        public DayNightVisualSample CurrentSample { get; private set; }
        public float TunnelLampPower { get; private set; } = 1f;

        public void Initialize(
            Camera camera,
            MountainRoadPlan roadPlan,
            MountainRoadWorldResult world)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The mountain-road atmosphere is already initialized.");
            }

            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            plan = roadPlan ??
                throw new ArgumentNullException(nameof(roadPlan));
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            RuntimeSceneSetup.ApplyMountainRoadVisibility(camera);
            BuildGlobalVolume();
            BuildTunnelLamp(world);
            BuildYardLamp();
            ApplyCurrentTime(true);
            IsInitialized = true;
        }

        public void ApplyCurrentTime(bool force = false)
        {
            if (plan == null)
            {
                return;
            }

            int day = GameSessionState.GameDayIndex;
            int minute = GameSessionState.GameMinuteOfDay;
            if (!force && day == appliedDay && minute == appliedMinute)
            {
                return;
            }

            CurrentSample = GameTimeDayNightRules.Evaluate(
                GameSessionState.GameTimeOfDayMinutes);
            RuntimeSceneSetup.ApplyMountainRoadLighting(
                CurrentSample,
                force);
            appliedDay = day;
            appliedMinute = minute;
            ApplyTunnelLamp();
            ApplyYardLamp();
            vista?.Apply(CurrentSample.NightFactor);
        }

        /// <summary>
        /// Hands the distant valley its day/night. It lives here rather
        /// than on an Update of its own because this is where the sample
        /// already is: the city in the view can then never be lit at an
        /// hour the rock in front of it is not.
        /// </summary>
        public void AttachVista(MountainRoadVistaLightsController lights)
        {
            vista = lights;
            vista?.Apply(CurrentSample.NightFactor);
        }

        public static float EvaluateTunnelLampPower(
            float elapsed,
            int seed)
        {
            float cycle = 8.7f + Mathf.Abs(seed % 5) * 0.31f;
            float offset = Mathf.Abs(seed % 997) * 0.013f;
            float phase = Mathf.Repeat(elapsed + offset, cycle);
            float first = Dip(phase, 1.18f, 0.10f, 0.22f);
            float second = Dip(phase, 1.51f, 0.055f, 0.55f);
            float lateContact = Dip(
                phase,
                cycle - 0.44f,
                0.075f,
                0.34f);
            return Mathf.Clamp01(
                Mathf.Min(first, Mathf.Min(second, lateContact)));
        }

        private static float Dip(
            float phase,
            float center,
            float halfWidth,
            float floor)
        {
            float distance = Mathf.Abs(phase - center);
            if (distance >= halfWidth)
            {
                return 1f;
            }

            float edge = Mathf.SmoothStep(
                0f,
                1f,
                distance / halfWidth);
            return Mathf.Lerp(floor, 1f, edge);
        }

        private void BuildGlobalVolume()
        {
            runtimeProfile = RuntimeSceneSetup
                .CreateCityNoirRuntimeProfile();
            runtimeProfile.name = "Runtime Mountain Road Grade";
            if (runtimeProfile.TryGet(out ColorAdjustments color))
            {
                color.postExposure.Override(0.38f);
                color.contrast.Override(-7f);
                color.saturation.Override(-31f);
                color.colorFilter.Override(
                    new Color(0.88f, 0.97f, 0.94f, 1f));
            }

            if (runtimeProfile.TryGet(out Bloom bloom))
            {
                bloom.intensity.Override(0.43f);
                bloom.threshold.Override(0.72f);
            }

            if (runtimeProfile.TryGet(out Vignette vignette))
            {
                vignette.intensity.Override(0.13f);
            }

            RuntimeSceneSetup.AddGaussianDepthOfField(
                runtimeProfile,
                20f,
                92f,
                0.65f);

            GlobalVolume = gameObject.AddComponent<Volume>();
            GlobalVolume.isGlobal = true;
            GlobalVolume.priority = 25f;
            GlobalVolume.weight = 1f;
            GlobalVolume.sharedProfile = runtimeProfile;
            gameObject
                .AddComponent<DepthOfFieldSettingsBinder>()
                .Initialize(runtimeProfile);
        }

        private void BuildTunnelLamp(MountainRoadWorldResult world)
        {
            MountainRoadSoundAnchor anchor = default;
            bool found = false;
            for (int index = 0; index < plan.SoundAnchors.Count; index++)
            {
                if (plan.SoundAnchors[index].Kind !=
                    MountainRoadSoundAnchorKind.TunnelLampBallast)
                {
                    continue;
                }

                anchor = plan.SoundAnchors[index];
                found = true;
                break;
            }

            if (!found)
            {
                throw new InvalidOperationException(
                    "The mountain tunnel needs its physical lamp anchor.");
            }

            if (world.SemanticObjects.TryGetValue(
                    anchor.SourceObjectStableId,
                    out Transform fixture))
            {
                Renderer[] renderers = fixture.GetComponentsInChildren<
                    Renderer>(true);
                for (int index = 0; index < renderers.Length; index++)
                {
                    if (renderers[index].gameObject.name == "Warm Lamp Lens")
                    {
                        tunnelLampLens = renderers[index];
                        break;
                    }
                }
            }

            GameObject lampObject = new GameObject(
                "Mountain Tunnel Lamp Light");
            lampObject.transform.SetParent(transform, false);
            lampObject.transform.position = anchor.Position +
                                            Vector3.down * 0.05f;
            Vector3 direction = (
                Vector3.down * 0.94f +
                plan.Tunnel.OutwardAxis * 0.34f).normalized;
            lampObject.transform.rotation = Quaternion.LookRotation(
                direction,
                plan.Tunnel.OutwardAxis);
            TunnelLamp = lampObject.AddComponent<Light>();
            TunnelLamp.type = LightType.Spot;
            TunnelLamp.color = new Color(0.72f, 0.84f, 0.73f);
            TunnelLamp.range = 9.2f;
            TunnelLamp.spotAngle = 66f;
            TunnelLamp.innerSpotAngle = 38f;
            TunnelLamp.shadows = LightShadows.Hard;
            TunnelLamp.shadowStrength = 0.58f;
            TunnelLamp.shadowBias =
                RuntimeSceneSetup.PlayerMeshShadowBias;
            TunnelLamp.shadowNormalBias =
                RuntimeSceneSetup.PlayerMeshShadowNormalBias;
            TunnelLamp.shadowNearPlane =
                RuntimeSceneSetup.PlayerMeshShadowNearPlane;
            TunnelLamp.renderMode = LightRenderMode.ForcePixel;
        }

        /// <summary>
        /// Built here rather than by the site builder so that every
        /// real light on this mountain has one owner, and that owner
        /// is the thing already holding the hour.
        /// </summary>
        private void BuildYardLamp()
        {
            MountainRoadTerminalSitePlan site = plan.Terminal.Site;
            if (site == null)
            {
                return;
            }

            MountainRoadSitePracticalDescriptor practical =
                site.YardLamp;
            var lampObject = new GameObject("Mountain Yard Lamp");
            lampObject.transform.SetParent(transform, false);
            lampObject.transform.position = practical.Position;
            lampObject.transform.rotation = Quaternion.LookRotation(
                practical.Direction,
                plan.Plateau.Forward);
            yardLamp = lampObject.AddComponent<Light>();
            yardLamp.type = LightType.Spot;

            // Mercury, against the cafe's sulphur thirty metres away.
            // The two are the whole colour argument of the summit at
            // night: the yard is a working light, the window is not.
            yardLamp.color = new Color(0.74f, 0.82f, 0.86f);
            yardLamp.range = practical.Range;
            yardLamp.spotAngle = practical.SpotAngle;
            yardLamp.innerSpotAngle = practical.SpotAngle * 0.42f;
            yardLamp.shadows = LightShadows.Hard;
            yardLamp.shadowStrength = 0.54f;
            yardLamp.shadowBias =
                RuntimeSceneSetup.PlayerMeshShadowBias;
            yardLamp.shadowNormalBias =
                RuntimeSceneSetup.PlayerMeshShadowNormalBias;
            yardLamp.shadowNearPlane =
                RuntimeSceneSetup.PlayerMeshShadowNearPlane;
            yardLamp.renderMode = LightRenderMode.ForcePixel;
        }

        private void ApplyYardLamp()
        {
            if (yardLamp == null)
            {
                return;
            }

            yardLamp.intensity = YardLampDayIntensity *
                (1f + CurrentSample.NightFactor * YardLampNightBoost);
        }

        private void ApplyTunnelLamp()
        {
            if (TunnelLamp == null)
            {
                return;
            }

            TunnelLamp.intensity =
                TunnelLampBaseIntensity *
                (1f + CurrentSample.NightFactor * TunnelLampNightBoost) *
                TunnelLampPower;
            TunnelLamp.enabled = TunnelLampPower > 0.045f;
            if (tunnelLampLens == null)
            {
                return;
            }

            if (tunnelLampProperties == null)
            {
                // MaterialPropertyBlock owns native Unity state; create it
                // on first real use, never in a MonoBehaviour initializer.
                tunnelLampProperties = new MaterialPropertyBlock();
            }

            float visiblePower = Mathf.Lerp(
                0.08f,
                1f,
                TunnelLampPower);
            Color lensColor = new Color(
                TunnelLensColor.r * visiblePower,
                TunnelLensColor.g * visiblePower,
                TunnelLensColor.b * visiblePower,
                TunnelLensColor.a);
            tunnelLampLens.GetPropertyBlock(tunnelLampProperties);
            tunnelLampProperties.SetColor(BaseColorId, lensColor);
            tunnelLampProperties.SetColor(ColorId, lensColor);
            tunnelLampLens.SetPropertyBlock(tunnelLampProperties);
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            ApplyCurrentTime();
            elapsedSeconds += Time.unscaledDeltaTime;
            float nextPower = EvaluateTunnelLampPower(
                elapsedSeconds,
                plan.Seed);
            if (Mathf.Abs(nextPower - TunnelLampPower) <= 0.001f)
            {
                return;
            }

            TunnelLampPower = nextPower;
            ApplyTunnelLamp();
        }

        private void OnDestroy()
        {
            if (runtimeProfile == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(runtimeProfile);
            }
            else
            {
                DestroyImmediate(runtimeProfile);
            }

            runtimeProfile = null;
        }
    }
}
