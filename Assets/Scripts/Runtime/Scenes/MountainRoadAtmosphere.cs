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
        /// The number is on THIS AREA's EXTERIOR scale, which is not the
        /// city's. The documented city practicals run `31` to `240`; exterior
        /// mountain fixtures stay at or below `18`. The cafe's two bounded
        /// interior keys are the deliberate exception: their larger raw
        /// values cross a room onto near-black clothing without reaching the
        /// yard. Set from the city list this lamp once stood at `38`, three
        /// and a half times brighter than the rest of the summit, and blew the
        /// yard out.
        ///
        /// Raised `9.5 → 11.5` (2026-09-02, the user's "совсем как-то
        /// темно"): the yard is the one place §10f says IS lit, and one
        /// lamp `5.5 m` up delivered `9.5 / 5.5² = 0.31` under itself over
        /// a `42 x 27 m` pad. `11.5` puts the night end at `17.25`, still
        /// under the `18` the summit tests refuse above. The night could
        /// not be raised on the boost instead: see
        /// <see cref="YardLampNightBoost"/>.
        /// </summary>
        private const float YardLampDayIntensity = 11.5f;

        /// <summary>
        /// Was `0.55`, which put the day at `64.5%` of the night - two
        /// points under the §20 floor of two thirds. At `0.5` the day is
        /// the floor exactly: `11.5 / 17.25 = 2/3`.
        ///
        /// This is therefore the CEILING, not a dial: §20 forbids the day
        /// dropping below two thirds of the night, so a brighter night has
        /// to be bought with a brighter day
        /// (<see cref="YardLampDayIntensity"/>) and never with a bigger
        /// boost. `AlwaysLitLawTests` pins it.
        /// </summary>
        private const float YardLampNightBoost = 0.5f;

        /// <summary>
        /// The mercury lamp's own blurred ball in the fog. Every fixed lamp
        /// in the City carries one and not one fixture on this mountain did,
        /// which is most of why the yard read as flat dark: at Exp2 `0.026`
        /// a `0.07 m` lens has lost a third of its contrast by thirty
        /// metres, and the pad is `42 m` long. Cold, to match the lamp.
        /// </summary>
        private const float YardLampHaloInnerSize = 0.62f;

        private const float YardLampHaloOuterSize = 2.35f;

        /// <summary>
        /// The floodlight over the apron, and the one fixture on this mountain
        /// sized on the CITY's scale rather than this area's.
        ///
        /// Added 2026-09-02 on the user's instruction: light the parked car
        /// "примерно так же как в городской сцене на островке последнего
        /// рейса". So the target is the island lamp's DELIVERED light, not its
        /// wattage — it gets to stand `3.5 m` from its car and this one is
        /// held `8.9 m` back by the apron's reserved turning disc, and a spot
        /// falls off with the square of that.
        ///
        /// The island's own number was arrived at by calibration rather than
        /// arithmetic, and this follows the same chain: the drying yard's
        /// communal floodlight is `150` over `16 m` landing on things about
        /// `7 m` out, i.e. about `3.1` arriving; the island's `45` over its
        /// `3.7 m` slant delivers `3.3`; and from this post's `9.8 m` slant
        /// the same `3.1` needs `300`. That is why the number looks nothing
        /// like its neighbours and means the same thing.
        ///
        /// It therefore leaves the documented `1.65`-`16` band, deliberately.
        /// That band exists so nobody imports a city number for a WASH over
        /// the yard — `38` once did and blew it out — and it is still right
        /// for one. This is a `34°` cone on a single car, and the first night
        /// photograph of this pad is what showed the band could not do the
        /// job at all: fixtures at `13`-`17` over `4`-`5.5 m` deliver `0.5` to
        /// `0.8`, the same order as the moon and ambient they are supposed to
        /// be seen against.
        ///
        /// The `2/3` day floor is §20's and is met exactly at `200`/`300`,
        /// which is also the island's own effective ladder shape: it authors
        /// `45` night over a `15` day floor and `CityNightSiteLightRegistry`
        /// lifts that floor to `night * 2/3` before it lerps.
        /// </summary>
        private const float ApronFloodDayIntensity = 200f;

        private const float ApronFloodNightBoost = 0.5f;

        /// <summary>Warm, and the island's exact colour: this lamp and that
        /// one are the same fixture doing the same job at the two ends of the
        /// same journey.</summary>
        private static readonly Color ApronFloodColor =
            new Color(1.00f, 0.87f, 0.66f);

        /// <summary>Half the outer cone, as the island holds it.</summary>
        private const float ApronFloodInnerSpotAngle = 16f;

        /// <summary>
        /// The island's halo is `0.52` / `1.55`, sized for a lamp you stand
        /// next to. This one is read from across the pad - from the road
        /// approach it is over twenty metres off - so it is scaled up by the
        /// same argument that sized the beam: a halo is only a halo at the
        /// distance it is actually seen from.
        /// </summary>
        private const float ApronFloodHaloInnerSize = 0.66f;

        private const float ApronFloodHaloOuterSize = 1.95f;

        /// <summary>How far down the beam the halo sits, clear of the shade's
        /// own box.</summary>
        private const float ApronFloodHaloStandoff = 0.26f;
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
        private Light apronFlood;
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

        /// <summary>The floodlight aimed at where the car parks.</summary>
        public Light ApronFloodlight => apronFlood;
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
            BuildApronFloodlight();
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
            ApplyApronFloodlight();
            vista?.Apply(CurrentSample.NightFactor);

            // THE MOUNTAIN OWNS ITS OWN EMISSION WHILE IT IS THE LOADED
            // AREA, and until now nothing did. `CityNightGlowRegistry` is a
            // process-wide static written ONLY by `CityNightWorldResult`, and
            // the cafe's lit lenses register into it
            // (`MountainRoadCafeSurfaceAppearance`). So travelling up from a
            // City at noon froze every emissive thing on this pad at
            // `DeadGlowFraction` (two thirds) for the whole visit - at any
            // hour, midnight included - and travelling up at night left it at
            // full even at midday. It is safe to write from here because the
            // three exterior areas are Single-mode loads and are never
            // resident together, so exactly one of them owns the static at a
            // time; this is simply the mountain taking its turn, from the one
            // call that already holds the hour.
            CityNightGlowRegistry.SetNightFactor(CurrentSample.NightFactor);
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

            // And the ball of light the lamp is in fog. Always-burning, so
            // it is initialized directly and stays out of the night
            // registry - see CityLightHalo.CreateAlwaysBurning for why that
            // matters on a mountain the City's static does not follow.
            CityLightHalo.CreateAlwaysBurning(
                lampObject.transform,
                Vector3.zero,
                YardLampHaloInnerSize,
                YardLampHaloOuterSize,
                new Color(0.80f, 0.88f, 0.94f, 0.82f),
                new Color(0.52f, 0.64f, 0.72f, 0f));
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

        /// <summary>
        /// The floodlight over the apron, built here for the same reason the
        /// yard lamp is: every real light outside a building on this mountain
        /// has one owner, and that owner is the thing already holding the
        /// hour.
        ///
        /// It also means this fixture is not part of the six the summit
        /// lighting test counts. That is not a way round the test - the test
        /// walks what `MountainRoadWorldBuilder` builds, and says so - but it
        /// does mean this lamp's own numbers are pinned by
        /// `MountainRoadApronFloodlightTests` instead, because a light nobody
        /// asserts is a light that drifts.
        ///
        /// Shadows are OFF, as on the island. The pool has one car in it and a
        /// man on its bonnet; a hard shadow from a `44°` cone this close puts
        /// the car's own silhouette across the ground the beam exists to show.
        /// </summary>
        private void BuildApronFloodlight()
        {
            MountainRoadTerminalSitePlan site = plan.Terminal.Site;
            if (site == null)
            {
                return;
            }

            MountainRoadSitePracticalDescriptor practical =
                site.ApronFloodlight;
            var lampObject = new GameObject("Mountain Apron Floodlight");
            lampObject.transform.SetParent(transform, false);
            lampObject.transform.position = practical.Position;
            lampObject.transform.rotation = Quaternion.LookRotation(
                practical.Direction,
                Vector3.up);
            apronFlood = lampObject.AddComponent<Light>();
            apronFlood.type = LightType.Spot;
            apronFlood.color = ApronFloodColor;
            apronFlood.range = practical.Range;
            apronFlood.spotAngle = practical.SpotAngle;
            apronFlood.innerSpotAngle = ApronFloodInnerSpotAngle;
            apronFlood.shadows = LightShadows.None;
            apronFlood.renderMode = LightRenderMode.ForcePixel;
            apronFlood.lightmapBakeType = LightmapBakeType.Realtime;

            // Down the beam, not at the head's own centre. The island can put
            // its halo on the emitter because its housing is built around
            // that point; here the head is a solid shade box and a halo
            // inside it is a lamp with its own lid on. This is the aperture.
            CityLightHalo.CreateAlwaysBurning(
                lampObject.transform,
                Vector3.forward * ApronFloodHaloStandoff,
                ApronFloodHaloInnerSize,
                ApronFloodHaloOuterSize,
                new Color(
                    ApronFloodColor.r * 4.2f,
                    ApronFloodColor.g * 4.2f,
                    ApronFloodColor.b * 4.2f,
                    0.18f),
                new Color(
                    ApronFloodColor.r * 2.1f,
                    ApronFloodColor.g * 2.1f,
                    ApronFloodColor.b * 2.1f,
                    0.05f));
        }

        private void ApplyApronFloodlight()
        {
            if (apronFlood == null)
            {
                return;
            }

            apronFlood.intensity = ApronFloodDayIntensity *
                (1f + CurrentSample.NightFactor * ApronFloodNightBoost);
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
