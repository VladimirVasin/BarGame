using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Small mono sounds owned by visible cableway machinery. The motor loop
    /// belongs to the reducer; every clack is triggered by a real cabin/roller
    /// crossing rather than an ambience timer.
    /// </summary>
    public static class MountainCablewaySoundSynthesis
    {
        public const int SampleRate = 22050;
        public const float MotorDuration = 2f;
        public const float ClackDuration = 0.34f;

        private const int MotorCrossfadeSamples = 1024;
        private const float QuantizationSteps = 127f;

        public static float[] GenerateMotorSamples()
        {
            int sampleCount = Mathf.RoundToInt(SampleRate * MotorDuration);
            var extended = new float[sampleCount + MotorCrossfadeSamples];
            uint noiseState = 0x4341424Cu;
            float lowNoise = 0f;
            for (int index = 0; index < extended.Length; index++)
            {
                float time = index / (float)SampleRate;
                float white = NextNoise(ref noiseState);
                lowNoise += (white - lowNoise) * 0.028f;
                float gearbox =
                    Mathf.Sin(Mathf.PI * 2f * 47f * time) * 0.155f +
                    Mathf.Sin(Mathf.PI * 2f * 94f * time + 0.21f) * 0.072f +
                    Mathf.Sin(Mathf.PI * 2f * 188f * time + 1.14f) * 0.028f;
                float teeth = Mathf.Sin(
                                  Mathf.PI * 2f * 423f * time +
                                  Mathf.Sin(Mathf.PI * 2f * 7f * time) *
                                  0.38f) *
                              0.021f;
                float strainedPulse = 0.86f +
                    Mathf.Sin(Mathf.PI * 2f * 3f * time) * 0.10f;
                extended[index] = Quantize(
                    gearbox * strainedPulse + teeth + lowNoise * 0.018f);
            }

            var samples = new float[sampleCount];
            Array.Copy(extended, samples, sampleCount);
            for (int index = 0; index < MotorCrossfadeSamples; index++)
            {
                float blend = index / (float)MotorCrossfadeSamples;
                samples[index] = Quantize(Mathf.Lerp(
                    extended[sampleCount + index],
                    samples[index],
                    blend));
            }

            samples[sampleCount - 1] = samples[0];
            return samples;
        }

        public static float[] GenerateClackSamples()
        {
            int sampleCount = Mathf.RoundToInt(SampleRate * ClackDuration);
            var samples = new float[sampleCount];
            uint noiseState = 0x524F4C4Cu;
            for (int index = 0; index < samples.Length; index++)
            {
                float time = index / (float)SampleRate;
                float attack = Mathf.Min(1f, time * 850f);
                float decay = Mathf.Exp(-time * 23f);
                float metal =
                    Mathf.Sin(Mathf.PI * 2f * 318f * time) * 0.34f +
                    Mathf.Sin(Mathf.PI * 2f * 637f * time + 0.37f) * 0.17f +
                    Mathf.Sin(Mathf.PI * 2f * 951f * time + 1.2f) * 0.07f;
                float impact = NextNoise(ref noiseState) *
                    Mathf.Exp(-time * 61f) * 0.19f;
                samples[index] = Quantize(
                    (metal * attack * decay) + impact);
            }

            return samples;
        }

        internal static AudioClip CreateMotorRuntimeClip()
        {
            return CreateClip(
                "Cableway Visible Reducer Motor",
                GenerateMotorSamples());
        }

        internal static AudioClip CreateClackRuntimeClip()
        {
            return CreateClip(
                "Cableway Cabin Roller Crossing",
                GenerateClackSamples());
        }

        private static AudioClip CreateClip(string name, float[] samples)
        {
            AudioClip clip = AudioClip.Create(
                name,
                samples.Length,
                1,
                SampleRate,
                false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        private static float NextNoise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            float normalized =
                (state & 0x00FFFFFFu) / 8388607.5f - 1f;
            if (state == 0u)
            {
                state = 0xA341316Cu;
            }

            return normalized;
        }

        private static float Quantize(float sample)
        {
            return Mathf.Round(
                       Mathf.Clamp(sample, -0.82f, 0.82f) *
                       QuantizationSteps) /
                   QuantizationSteps;
        }
    }

    [DisallowMultipleComponent]
    public sealed class MountainCablewayController : MonoBehaviour
    {
        private readonly List<Transform> cabins = new List<Transform>();
        private readonly List<float> phases = new List<float>();
        private readonly List<EventMarker> eventMarkers =
            new List<EventMarker>();
        private readonly List<AudioSource> audioSources =
            new List<AudioSource>();

        private MountainRoadCablewayPlan plan;
        private Transform bullwheel;
        private Quaternion bullwheelBaseRotation;
        private AudioClip motorClip;
        private AudioClip clackClip;
        private float elapsedSeconds;
        private bool initialized;

        public bool IsInitialized => initialized;
        public float ElapsedSeconds => elapsedSeconds;
        public IReadOnlyList<Transform> Cabins => cabins;
        public IReadOnlyList<AudioSource> AudioSources => audioSources;

        internal void Initialize(
            MountainRoadCablewayPlan sourcePlan,
            IReadOnlyList<Transform> cabinRoots,
            Transform visibleBullwheel,
            Transform visibleReducer,
            IReadOnlyList<Transform> supportRollerAnchors)
        {
            if (initialized)
            {
                throw new InvalidOperationException(
                    "The cableway controller is already initialized.");
            }

            plan = sourcePlan ??
                throw new ArgumentNullException(nameof(sourcePlan));
            if (cabinRoots == null ||
                cabinRoots.Count != plan.Cabins.Count ||
                visibleBullwheel == null ||
                visibleReducer == null ||
                supportRollerAnchors == null)
            {
                throw new ArgumentException(
                    "Cableway presentation does not match its plan.");
            }

            int supportCount = 0;
            for (int index = 0; index < plan.Nodes.Count; index++)
            {
                if (plan.Nodes[index].Kind ==
                    MountainCablewayNodeKind.Support)
                {
                    supportCount++;
                }
            }

            if (supportRollerAnchors.Count != supportCount)
            {
                throw new ArgumentException(
                    "Every authored support needs one visible roller anchor.",
                    nameof(supportRollerAnchors));
            }

            for (int index = 0; index < cabinRoots.Count; index++)
            {
                if (cabinRoots[index] == null)
                {
                    throw new ArgumentException(
                        "A cable cabin has no transform.",
                        nameof(cabinRoots));
                }

                cabins.Add(cabinRoots[index]);
                phases.Add(plan.Cabins[index].Phase);
            }

            bullwheel = visibleBullwheel;
            bullwheelBaseRotation = bullwheel.localRotation;
            motorClip = MountainCablewaySoundSynthesis
                .CreateMotorRuntimeClip();
            clackClip = MountainCablewaySoundSynthesis
                .CreateClackRuntimeClip();
            AudioSource motor = CreateMotorSource(visibleReducer);
            AudioSource lowerClack = CreateClackSource(
                visibleBullwheel,
                20f,
                0.27f);
            float turnLength = Mathf.PI * plan.TurnRadius;
            float descendingEnd =
                plan.LineLength * 2f + turnLength;
            eventMarkers.Add(new EventMarker(0f, lowerClack));
            eventMarkers.Add(new EventMarker(descendingEnd, lowerClack));

            int supportAnchorIndex = 0;
            float descendingStart = plan.LineLength + turnLength;
            for (int index = 0; index < plan.Nodes.Count; index++)
            {
                MountainCablewayNodeDescriptor node = plan.Nodes[index];
                if (node.Kind != MountainCablewayNodeKind.Support)
                {
                    continue;
                }

                AudioSource supportSource = CreateClackSource(
                    supportRollerAnchors[supportAnchorIndex++],
                    18f,
                    0.22f);
                eventMarkers.Add(new EventMarker(
                    node.Distance,
                    supportSource));
                eventMarkers.Add(new EventMarker(
                    descendingStart + plan.LineLength - node.Distance,
                    supportSource));
            }

            elapsedSeconds = 0f;
            initialized = true;
            ApplyPresentation(0f, 0f, false);
            if (Application.isPlaying)
            {
                motor.Play();
            }
        }

        public void Advance(float deltaTime)
        {
            if (!initialized)
            {
                return;
            }

            if (float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime) ||
                deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            float previousTime = elapsedSeconds;
            elapsedSeconds += deltaTime;
            ApplyPresentation(
                previousTime,
                elapsedSeconds,
                Application.isPlaying && deltaTime > 0f);
        }

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        private void ApplyPresentation(
            float previousTime,
            float currentTime,
            bool allowAudio)
        {
            for (int index = 0; index < cabins.Count; index++)
            {
                float phaseDistance = phases[index] * plan.LoopLength;
                float previousDistance = phaseDistance +
                    previousTime * plan.CabinSpeed;
                float currentDistance = phaseDistance +
                    currentTime * plan.CabinSpeed;
                MountainCablewayMotionSample sample =
                    MountainCablewayMotion.Sample(plan, currentDistance);
                ApplyCabinPose(
                    cabins[index],
                    sample,
                    currentDistance,
                    phases[index]);
                if (allowAudio)
                {
                    EmitCrossingClacks(previousDistance, currentDistance);
                }
            }

            float circumference = Mathf.Max(
                0.1f,
                Mathf.PI * 2f * plan.TurnRadius);
            float wheelDegrees =
                currentTime * plan.CabinSpeed / circumference * 360f;
            bullwheel.localRotation = bullwheelBaseRotation *
                Quaternion.AngleAxis(wheelDegrees, Vector3.up);
        }

        private void ApplyCabinPose(
            Transform cabin,
            MountainCablewayMotionSample sample,
            float unwrappedDistance,
            float phase)
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(
                sample.Tangent,
                Vector3.up);
            if (horizontal.sqrMagnitude < 0.0001f)
            {
                horizontal = plan.LineForward;
            }

            horizontal.Normalize();
            Quaternion upright = Quaternion.LookRotation(
                horizontal,
                Vector3.up);
            float sway = Mathf.Sin(
                unwrappedDistance * 0.31f + phase * Mathf.PI * 2f) * 1.1f;
            Quaternion swayRotation = Quaternion.AngleAxis(
                sway,
                horizontal);
            cabin.SetPositionAndRotation(
                sample.Position,
                swayRotation * upright);
        }

        private void EmitCrossingClacks(
            float previousDistance,
            float currentDistance)
        {
            for (int index = 0; index < eventMarkers.Count; index++)
            {
                EventMarker marker = eventMarkers[index];
                if (MountainCablewayMotion.CountForwardCrossings(
                        previousDistance,
                        currentDistance,
                        marker.LoopDistance,
                        plan.LoopLength) <= 0)
                {
                    continue;
                }

                marker.Source.PlayOneShot(clackClip, 1f);
            }
        }

        private AudioSource CreateMotorSource(Transform reducer)
        {
            AudioSource source = reducer.gameObject.AddComponent<AudioSource>();
            ConfigureSpatialSource(source, 1.3f, 23f, 154);
            source.loop = true;
            source.clip = motorClip;
            source.volume = 0.17f;
            source.pitch = 0.94f;
            reducer.gameObject.AddComponent<AudioLowPassFilter>()
                .cutoffFrequency = 3700f;
            audioSources.Add(source);
            return source;
        }

        private AudioSource CreateClackSource(
            Transform visibleOwner,
            float radius,
            float volume)
        {
            AudioSource source =
                visibleOwner.gameObject.AddComponent<AudioSource>();
            ConfigureSpatialSource(source, 0.8f, radius, 148);
            source.loop = false;
            source.volume = volume;
            source.pitch = 1f;
            audioSources.Add(source);
            return source;
        }

        private static void ConfigureSpatialSource(
            AudioSource source,
            float minimumDistance,
            float maximumDistance,
            int priority)
        {
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = minimumDistance;
            source.maxDistance = maximumDistance;
            source.dopplerLevel = 0f;
            source.spread = 0f;
            source.priority = priority;
            GameAudioMixer.Route(source, GameAudioGroup.SfxWorld);
        }

        private void OnDestroy()
        {
            DestroyGeneratedClip(motorClip);
            DestroyGeneratedClip(clackClip);
            motorClip = null;
            clackClip = null;
            cabins.Clear();
            phases.Clear();
            eventMarkers.Clear();
            audioSources.Clear();
            initialized = false;
        }

        private static void DestroyGeneratedClip(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(clip);
            }
            else
            {
                DestroyImmediate(clip);
            }
        }

        private readonly struct EventMarker
        {
            public EventMarker(float loopDistance, AudioSource source)
            {
                LoopDistance = loopDistance;
                Source = source;
            }

            public float LoopDistance { get; }
            public AudioSource Source { get; }
        }
    }
}
