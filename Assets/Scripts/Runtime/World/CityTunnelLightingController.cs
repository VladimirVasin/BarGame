using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct CityTunnelLampFlickerSample
    {
        internal CityTunnelLampFlickerSample(
            float powerMultiplier,
            long dipEdgeId)
        {
            PowerMultiplier = powerMultiplier;
            DipEdgeId = dipEdgeId;
        }

        public float PowerMultiplier { get; }
        public long DipEdgeId { get; }
        public bool HasDipEdge => DipEdgeId >= 0;
    }

    /// <summary>
    /// Pure absolute-time contact-fault pattern. Every fault is separated by
    /// 11-17 seconds and owns at most two short dips; a skipped interval is
    /// never replayed as catch-up work.
    /// </summary>
    public static class CityTunnelLampFlickerEvaluator
    {
        public const float MinimumFaultInterval = 11f;
        public const float MaximumFaultInterval = 17f;
        public const float FaultDuration = 0.43f;

        private const double StablePhaseOffset = 4.37;

        private static readonly float[] intervals =
        {
            13.7f,
            11.6f,
            16.4f,
            12.8f,
            15.3f,
            11.2f,
            16.8f,
            14.5f
        };

        private static readonly double cycleDuration =
            SumIntervals();
        private static readonly IReadOnlyList<float> readOnlyIntervals =
            Array.AsReadOnly(intervals);

        internal static IReadOnlyList<float> FaultIntervals =>
            readOnlyIntervals;

        public static CityTunnelLampFlickerSample Evaluate(
            double absoluteSeconds)
        {
            double safeTime =
                double.IsNaN(absoluteSeconds) ||
                double.IsInfinity(absoluteSeconds) ||
                absoluteSeconds < 0d
                    ? 0d
                    : absoluteSeconds;
            double shifted = safeTime + StablePhaseOffset;
            long cycleIndex = (long)Math.Floor(
                shifted / cycleDuration);
            double localTime =
                shifted - cycleIndex * cycleDuration;
            double eventStart = 0d;
            for (int eventIndex = 0;
                 eventIndex < intervals.Length;
                 eventIndex++)
            {
                double faultTime = localTime - eventStart;
                if (faultTime >= 0d && faultTime < FaultDuration)
                {
                    return EvaluateFault(
                        (float)faultTime,
                        cycleIndex,
                        eventIndex);
                }

                eventStart += intervals[eventIndex];
            }

            return new CityTunnelLampFlickerSample(1f, -1L);
        }

        private static CityTunnelLampFlickerSample EvaluateFault(
            float faultTime,
            long cycleIndex,
            int eventIndex)
        {
            long baseDipId =
                (cycleIndex * intervals.Length + eventIndex) * 2L;
            if (faultTime < 0.075f)
            {
                return new CityTunnelLampFlickerSample(
                    Mathf.Lerp(0.46f, 0.34f, faultTime / 0.075f),
                    baseDipId);
            }

            if (faultTime < 0.18f)
            {
                return new CityTunnelLampFlickerSample(
                    Mathf.Lerp(
                        0.72f,
                        0.96f,
                        (faultTime - 0.075f) / 0.105f),
                    -1L);
            }

            if (faultTime < 0.255f)
            {
                return new CityTunnelLampFlickerSample(
                    Mathf.Lerp(
                        0.14f,
                        0.09f,
                        (faultTime - 0.18f) / 0.075f),
                    baseDipId + 1L);
            }

            return new CityTunnelLampFlickerSample(
                Mathf.Lerp(
                    0.58f,
                    1f,
                    (faultTime - 0.255f) /
                    (FaultDuration - 0.255f)),
                -1L);
        }

        private static double SumIntervals()
        {
            double result = 0d;
            for (int index = 0; index < intervals.Length; index++)
            {
                result += intervals[index];
            }

            return result;
        }
    }

    /// <summary>
    /// Five visible fixtures belonging to the authored south-tunnel path.
    /// Four are emissive-only distance cues. The faulty second fixture owns
    /// the existing pooled practical Spot and the only local electrical audio.
    /// This component never creates a Light.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityTunnelLightingController : MonoBehaviour
    {
        public const string FaultyFixtureStableId =
            "mountain-south-tunnel-faulty-lamp";
        public const int FixtureCount = 5;
        public const int FaultyFixtureIndex = 1;
        public const float AudioMaxDistance = 5.6f;

        private const float HousingClearanceBelowCrown = 0.30f;
        private const float BallastVolume = 0.13f;
        private const float CrackleVolume = 0.30f;

        private static readonly float[] fixtureDistances =
        {
            4f,
            11f,
            20f,
            30f,
            42f
        };

        private static readonly Color HousingColor =
            new Color(0.105f, 0.120f, 0.108f);
        private static readonly Color GuardColor =
            new Color(0.175f, 0.155f, 0.120f);
        private static readonly Color StableLensColor =
            new Color(1.18f, 0.78f, 0.39f, 1f);
        private static readonly Color FaultyLensColor =
            new Color(2.80f, 1.42f, 0.38f, 1f);
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");

        private readonly List<Transform> fixtures =
            new List<Transform>(FixtureCount);
        private MaterialPropertyBlock lensProperties;

        private CityNightAtmosphere nightAtmosphere;
        private Renderer faultyLens;
        private AudioSource ballastSource;
        private AudioSource crackleSource;
        private AudioClip ballastClip;
        private AudioClip[] crackleClips = Array.Empty<AudioClip>();
        private long lastDipEdgeId = long.MinValue;
        private float appliedPowerMultiplier = 1f;
        private bool isInitialized;

        public IReadOnlyList<Transform> Fixtures => fixtures;
        public Transform FaultyFixture =>
            fixtures.Count > FaultyFixtureIndex
                ? fixtures[FaultyFixtureIndex]
                : null;
        public AudioSource BallastSource => ballastSource;
        public AudioSource CrackleSource => crackleSource;
        public float AppliedPowerMultiplier => appliedPowerMultiplier;
        public bool IsInitialized => isInitialized;

        public static CityTunnelLightingController Create(
            Transform mountainRoot,
            CityMountainTunnelDescriptor tunnel,
            CityNightAtmosphere atmosphere,
            IReadOnlyList<CityFringePracticalAnchor> practicalAnchors)
        {
            if (mountainRoot == null)
            {
                throw new ArgumentNullException(nameof(mountainRoot));
            }

            var root = new GameObject("South Tunnel Lighting");
            root.transform.SetParent(mountainRoot, false);
            try
            {
                CityTunnelLightingController controller =
                    root.AddComponent<CityTunnelLightingController>();
                controller.Initialize(
                    tunnel,
                    atmosphere,
                    practicalAnchors);
                return controller;
            }
            catch
            {
                DestroyOwned(root);
                throw;
            }
        }

        public void Initialize(
            CityMountainTunnelDescriptor tunnel,
            CityNightAtmosphere atmosphere,
            IReadOnlyList<CityFringePracticalAnchor> practicalAnchors)
        {
            if (isInitialized)
            {
                throw new InvalidOperationException(
                    "The tunnel lighting controller is already initialized.");
            }

            nightAtmosphere = atmosphere != null
                ? atmosphere
                : throw new ArgumentNullException(nameof(atmosphere));
            if (practicalAnchors == null)
            {
                throw new ArgumentNullException(nameof(practicalAnchors));
            }

            if (tunnel.OpeningHeight <= 0f ||
                tunnel.VisualDepth < fixtureDistances[FixtureCount - 1] ||
                tunnel.Segments.Count == 0)
            {
                throw new ArgumentException(
                    "The tunnel must own the complete visible fixture path.",
                    nameof(tunnel));
            }

            CityFringePracticalAnchor tunnelPractical =
                FindTunnelPractical(practicalAnchors);
            DisableAuthoredPracticalLens(tunnelPractical.Anchor);
            BuildFixtures(tunnel);
            BindPooledPractical(tunnel, tunnelPractical.Anchor);
            BuildOwnedAudio();

            CityTunnelLampFlickerSample initial =
                CityTunnelLampFlickerEvaluator.Evaluate(
                    Time.unscaledTimeAsDouble);
            ApplyFlicker(initial, false);
            isInitialized = true;
            nightAtmosphere.RefreshImmediate();
            if (Application.isPlaying)
            {
                ballastSource.Play();
            }
        }

        private void Update()
        {
            if (!isInitialized)
            {
                return;
            }

            CityTunnelLampFlickerSample sample =
                CityTunnelLampFlickerEvaluator.Evaluate(
                    Time.unscaledTimeAsDouble);
            ApplyFlicker(sample, true);
        }

        private void OnDisable()
        {
            if (!isInitialized)
            {
                return;
            }

            if (nightAtmosphere != null)
            {
                nightAtmosphere.SetTunnelPracticalFlickerMultiplier(1f);
            }

            if (ballastSource != null)
            {
                ballastSource.Stop();
            }
        }

        private void OnEnable()
        {
            if (isInitialized &&
                Application.isPlaying &&
                ballastSource != null &&
                !ballastSource.isPlaying)
            {
                ballastSource.Play();
            }
        }

        private void OnDestroy()
        {
            DestroyOwned(ballastClip);
            for (int index = 0; index < crackleClips.Length; index++)
            {
                DestroyOwned(crackleClips[index]);
            }
        }

        private void BuildFixtures(CityMountainTunnelDescriptor tunnel)
        {
            for (int index = 0; index < fixtureDistances.Length; index++)
            {
                CityMountainTunnelPathSample sample =
                    tunnel.SamplePath(fixtureDistances[index]);
                Transform fixture = new GameObject(
                    $"Tunnel Ceiling Fixture {index + 1:00}").transform;
                fixture.SetParent(transform, false);
                fixture.SetPositionAndRotation(
                    sample.Position +
                    Vector3.up *
                    (tunnel.OpeningHeight -
                     HousingClearanceBelowCrown),
                    Quaternion.LookRotation(
                        sample.Forward,
                        Vector3.up));
                fixtures.Add(fixture);
                BuildFixtureGeometry(
                    fixture,
                    index == FaultyFixtureIndex);
            }
        }

        private void BuildFixtureGeometry(
            Transform fixture,
            bool faulty)
        {
            RuntimePrimitiveFactory.CreateBox(
                "Industrial Lamp Housing",
                fixture,
                Vector3.zero,
                new Vector3(0.86f, 0.26f, 1.15f),
                HousingColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Ceiling Mounting Plate",
                fixture,
                new Vector3(0f, 0.18f, 0f),
                new Vector3(0.30f, 0.12f, 0.42f),
                GuardColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Lamp Guard Left",
                fixture,
                new Vector3(-0.34f, -0.18f, 0f),
                new Vector3(0.075f, 0.10f, 0.94f),
                GuardColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Lamp Guard Right",
                fixture,
                new Vector3(0.34f, -0.18f, 0f),
                new Vector3(0.075f, 0.10f, 0.94f),
                GuardColor,
                false);
            GameObject lens = RuntimePrimitiveFactory.CreateBox(
                faulty
                    ? "Faulty Emissive Lens"
                    : "Stable Emissive Lens",
                fixture,
                new Vector3(0f, -0.19f, 0.03f),
                new Vector3(0.56f, 0.11f, 0.82f),
                faulty ? FaultyLensColor : StableLensColor,
                CityNightResources.EmissiveMaterial,
                false);
            if (!faulty)
            {
                return;
            }

            faultyLens = lens.GetComponent<Renderer>();
            RuntimePrimitiveFactory.CreateBox(
                "Loose Ballast Box",
                fixture,
                new Vector3(0.52f, -0.02f, -0.18f),
                new Vector3(0.24f, 0.25f, 0.38f),
                GuardColor,
                false);
        }

        private void BindPooledPractical(
            CityMountainTunnelDescriptor tunnel,
            Transform practicalAnchor)
        {
            CityMountainTunnelPathSample sample =
                tunnel.SamplePath(
                    fixtureDistances[FaultyFixtureIndex]);
            Vector3 lightPosition = faultyLens.transform.position;
            Vector3 lightForward =
                (Vector3.down * 0.92f +
                 sample.Forward * 0.38f).normalized;
            practicalAnchor.SetPositionAndRotation(
                lightPosition,
                Quaternion.LookRotation(
                    lightForward,
                    sample.Forward));
        }

        private void BuildOwnedAudio()
        {
            GameObject owner = faultyLens.gameObject;
            ballastSource = owner.AddComponent<AudioSource>();
            ConfigureSource(ballastSource, true, BallastVolume);
            ballastClip =
                CityTunnelLampSoundSynthesis.CreateBallastRuntimeClip();
            ballastSource.clip = ballastClip;
            GameAudioMixer.Route(
                ballastSource,
                GameAudioGroup.AmbienceDetails);

            crackleSource = owner.AddComponent<AudioSource>();
            ConfigureSource(crackleSource, false, CrackleVolume);
            GameAudioMixer.Route(
                crackleSource,
                GameAudioGroup.SfxWorld);
            crackleClips = new AudioClip[
                CityTunnelLampSoundSynthesis.CrackleVariantCount];
            for (int index = 0; index < crackleClips.Length; index++)
            {
                crackleClips[index] =
                    CityTunnelLampSoundSynthesis
                        .CreateCrackleRuntimeClip(index);
            }
        }

        private void ApplyFlicker(
            CityTunnelLampFlickerSample sample,
            bool allowCrackle)
        {
            appliedPowerMultiplier = sample.PowerMultiplier;
            ApplyFaultyLens(sample.PowerMultiplier);
            nightAtmosphere.SetTunnelPracticalFlickerMultiplier(
                sample.PowerMultiplier);
            if (ballastSource != null)
            {
                ballastSource.volume =
                    BallastVolume *
                    Mathf.Lerp(0.55f, 1f, sample.PowerMultiplier);
            }

            if (!allowCrackle)
            {
                if (sample.HasDipEdge)
                {
                    lastDipEdgeId = sample.DipEdgeId;
                }

                return;
            }

            if (!sample.HasDipEdge ||
                sample.DipEdgeId == lastDipEdgeId)
            {
                return;
            }

            lastDipEdgeId = sample.DipEdgeId;
            if (!Application.isPlaying ||
                crackleSource == null ||
                crackleClips.Length == 0)
            {
                return;
            }

            int variant = (int)(
                sample.DipEdgeId % crackleClips.Length);
            crackleSource.PlayOneShot(
                crackleClips[variant],
                1f);
        }

        private void ApplyFaultyLens(float multiplier)
        {
            if (faultyLens == null)
            {
                return;
            }

            Color color = new Color(
                FaultyLensColor.r * multiplier,
                FaultyLensColor.g * multiplier,
                FaultyLensColor.b * multiplier,
                FaultyLensColor.a);
            MaterialPropertyBlock properties = LensProperties;
            properties.Clear();
            faultyLens.GetPropertyBlock(properties);
            properties.SetColor(BaseColorId, color);
            properties.SetColor(ColorId, color);
            faultyLens.SetPropertyBlock(properties);
        }

        private MaterialPropertyBlock LensProperties
        {
            get
            {
                // MaterialPropertyBlock creates native Unity state. Keeping
                // the allocation on first use avoids MonoBehaviour constructor
                // work and also supports initialization under an inactive root,
                // where Awake has not run yet.
                if (lensProperties == null)
                {
                    lensProperties = new MaterialPropertyBlock();
                }

                return lensProperties;
            }
        }

        private static void ConfigureSource(
            AudioSource source,
            bool loop,
            float volume)
        {
            source.playOnAwake = false;
            source.loop = loop;
            source.volume = volume;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 0.65f;
            source.maxDistance = AudioMaxDistance;
            source.dopplerLevel = 0f;
            source.spread = 0f;
        }

        private static CityFringePracticalAnchor FindTunnelPractical(
            IReadOnlyList<CityFringePracticalAnchor> practicalAnchors)
        {
            int foundIndex = -1;
            for (int index = 0; index < practicalAnchors.Count; index++)
            {
                if (practicalAnchors[index].Kind !=
                    CityFringeYardKind.SouthTunnelForecourt)
                {
                    continue;
                }

                if (foundIndex >= 0)
                {
                    throw new ArgumentException(
                        "The tunnel lighting requires exactly one tunnel " +
                        "practical anchor.",
                        nameof(practicalAnchors));
                }

                foundIndex = index;
            }

            if (foundIndex < 0 ||
                practicalAnchors[foundIndex].Anchor == null)
            {
                throw new ArgumentException(
                    "The tunnel lighting requires one physical tunnel " +
                    "practical anchor.",
                    nameof(practicalAnchors));
            }

            return practicalAnchors[foundIndex];
        }

        private static void DisableAuthoredPracticalLens(
            Transform practicalAnchor)
        {
            Renderer[] renderers =
                practicalAnchor.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                if (renderers[index].gameObject.name ==
                    "Practical Emissive Lens")
                {
                    renderers[index].gameObject.SetActive(false);
                }
            }
        }

        private static void DestroyOwned(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
