using System;
using UnityEngine;

namespace BarPromenade
{
    public enum CityBusDoorAudioCue
    {
        None = 0,
        Opening,
        Closing
    }

    /// <summary>
    /// Canonical bus gain staging and distance law. The exterior tail starts
    /// at the City's visible boundary and remains silent while the pooled bus
    /// is travelling through its fog-hidden spawn band.
    /// </summary>
    public static class CityBusAudioMix
    {
        public const float ExteriorMinimumDistance = 24f;
        public const float ExteriorMaximumDistance =
            RuntimeSceneSetup.CityFarClipPlane;
        public const float InteriorMinimumDistance = 4.5f;
        public const float InteriorMaximumDistance = 24f;
        public const float DoorMinimumDistance = 3.5f;
        public const float DoorMaximumDistance = 30f;
        public const float InteriorIdleVolume = 0.24f;
        public const float InteriorMaximumVolume = 0.42f;
        public const float ExteriorCabinMultiplier = 0.72f;
        public const float CabinBlendSeconds = 0.35f;
        public const float ExteriorDoorVolume = 0.66f;
        public const float InteriorDoorVolume = 0.82f;
        public const int ExteriorPriority = 80;
        public const int InteriorPriority = 64;
        public const int DoorPriority = 56;

        public static float EvaluateExteriorSourceGain(
            float distance,
            float normalizedSpeed)
        {
            float volume = Mathf.Lerp(
                CityBusActor.EngineIdleVolume,
                CityBusActor.EngineMaximumVolume,
                Mathf.Clamp01(normalizedSpeed));
            return volume * EvaluateLinearAttenuation(
                distance,
                ExteriorMinimumDistance,
                ExteriorMaximumDistance);
        }

        public static float EvaluateLinearAttenuation(
            float distance,
            float minimumDistance,
            float maximumDistance)
        {
            if (!IsFinite(distance) ||
                !IsFinite(minimumDistance) ||
                !IsFinite(maximumDistance) ||
                maximumDistance <= minimumDistance)
            {
                return 0f;
            }

            if (distance <= minimumDistance)
            {
                return 1f;
            }

            if (distance >= maximumDistance)
            {
                return 0f;
            }

            return 1f - ((distance - minimumDistance) /
                         (maximumDistance - minimumDistance));
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Bounded, source-first bus audio. Every voice belongs to a visible
    /// mechanism: both engine layers live in the rear motor compartment and
    /// each pneumatic door voice lives above its own passenger doorway.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityBusAudio : MonoBehaviour
    {
        private const int DoorSampleRate = 11025;
        private const float DoorClipDuration = 0.72f;
        private const string ExteriorEngineClipName =
            "City Bus Exterior Diesel Loop";
        private const string InteriorEngineClipName =
            "City Bus Cabin Structure Loop";
        private const string DoorOpeningClipName =
            "City Bus Pneumatic Doors Opening";
        private const string DoorClosingClipName =
            "City Bus Pneumatic Doors Closing";

        private static AudioClip exteriorEngineClip;
        private static AudioClip interiorEngineClip;
        private static AudioClip doorOpeningClip;
        private static AudioClip doorClosingClip;

        private Transform engineAnchor;
        private AudioSource exteriorEngineSource;
        private AudioSource interiorEngineSource;
        private AudioSource frontDoorSource;
        private AudioSource rearDoorSource;
        private Transform frontDoorEntryAnchor;
        private Transform rearDoorEntryAnchor;
        private CityBusDoorPhase previousDoorPhase;
        private float cabinBlend;
        private bool isInitialized;
        private bool isRunning;

        public Transform EngineAnchor => engineAnchor;
        public AudioSource ExteriorEngineSource => exteriorEngineSource;
        public AudioSource InteriorEngineSource => interiorEngineSource;
        public AudioSource FrontDoorSource => frontDoorSource;
        public AudioSource RearDoorSource => rearDoorSource;
        public float CabinBlend => cabinBlend;
        public CityBusDoorAudioCue LastDoorCue { get; private set; }
        public int DoorOpeningCueCount { get; private set; }
        public int DoorClosingCueCount { get; private set; }

        public void Initialize(CityBusDimensions dimensions)
        {
            if (isInitialized)
            {
                throw new InvalidOperationException(
                    "The city bus audio presentation is already initialized.");
            }

            engineAnchor = CreateAnchor(
                "Bus Rear Engine Audio",
                new Vector3(
                    0f,
                    Mathf.Clamp(dimensions.Height * 0.31f, 0.72f, 1.05f),
                    -(dimensions.Length * 0.5f) + 0.72f));
            exteriorEngineSource = engineAnchor.gameObject.AddComponent<
                AudioSource>();
            interiorEngineSource = engineAnchor.gameObject.AddComponent<
                AudioSource>();

            frontDoorSource = CreateAnchor(
                    "Bus Front Door Audio",
                    Vector3.zero)
                .gameObject.AddComponent<AudioSource>();
            rearDoorSource = CreateAnchor(
                    "Bus Rear Door Audio",
                    Vector3.zero)
                .gameObject.AddComponent<AudioSource>();

            ConfigureEngineSource(
                exteriorEngineSource,
                GetOrCreateExteriorEngineClip(),
                CityBusAudioMix.ExteriorMinimumDistance,
                CityBusAudioMix.ExteriorMaximumDistance,
                CityBusAudioMix.ExteriorPriority,
                0.20f);
            ConfigureEngineSource(
                interiorEngineSource,
                GetOrCreateInteriorEngineClip(),
                CityBusAudioMix.InteriorMinimumDistance,
                CityBusAudioMix.InteriorMaximumDistance,
                CityBusAudioMix.InteriorPriority,
                0f);
            ConfigureDoorSource(frontDoorSource, 0.985f);
            ConfigureDoorSource(rearDoorSource, 1.035f);
            ResetDoorState();
            isInitialized = true;
        }

        public void BindPresentation(CityBusPresentation presentation)
        {
            if (!isInitialized || presentation == null ||
                presentation.Registry == null)
            {
                throw new InvalidOperationException(
                    "Initialize bus audio and bind a valid presentation.");
            }

            CityBusAssetRegistry registry = presentation.Registry;
            frontDoorEntryAnchor = registry.FrontDoorEntryAnchor;
            rearDoorEntryAnchor = registry.RearDoorEntryAnchor;
            SyncPhysicalAnchors();
            ResetDoorState();
        }

        public void SyncPhysicalAnchors()
        {
            PositionDoorSource(frontDoorSource, frontDoorEntryAnchor);
            PositionDoorSource(rearDoorSource, rearDoorEntryAnchor);
        }

        public void BeginPlayback(
            float normalizedSpeed,
            bool playerInside)
        {
            if (!isInitialized)
            {
                return;
            }

            isRunning = true;
            cabinBlend = playerInside ? 1f : 0f;
            UpdateEngine(normalizedSpeed, playerInside, 0f);
            PlayLoop(exteriorEngineSource);
            PlayLoop(interiorEngineSource);
        }

        public void UpdateEngine(
            float normalizedSpeed,
            bool playerInside,
            float deltaTime)
        {
            if (!isInitialized)
            {
                return;
            }

            float speed01 = Mathf.Clamp01(normalizedSpeed);
            float blendStep = CityBusAudioMix.CabinBlendSeconds > 0f
                ? Mathf.Max(0f, deltaTime) /
                  CityBusAudioMix.CabinBlendSeconds
                : 1f;
            cabinBlend = Mathf.MoveTowards(
                cabinBlend,
                playerInside ? 1f : 0f,
                blendStep);

            exteriorEngineSource.pitch = Mathf.Lerp(
                CityBusActor.EngineIdlePitch,
                CityBusActor.EngineMaximumPitch,
                speed01);
            float exteriorVolume = Mathf.Lerp(
                CityBusActor.EngineIdleVolume,
                CityBusActor.EngineMaximumVolume,
                speed01);
            exteriorEngineSource.volume = exteriorVolume * Mathf.Lerp(
                1f,
                CityBusAudioMix.ExteriorCabinMultiplier,
                cabinBlend);

            interiorEngineSource.pitch = Mathf.Lerp(0.70f, 1.10f, speed01);
            interiorEngineSource.volume = Mathf.Lerp(
                    CityBusAudioMix.InteriorIdleVolume,
                    CityBusAudioMix.InteriorMaximumVolume,
                    speed01) *
                cabinBlend;
        }

        public void ApplyDoorSample(
            CityBusDriverDoorSample sample,
            bool playerInside)
        {
            if (!isInitialized)
            {
                return;
            }

            SyncPhysicalAnchors();
            CityBusDoorAudioCue cue = ResolveDoorCue(sample);
            previousDoorPhase = sample.DoorPhase;
            if (cue == CityBusDoorAudioCue.None)
            {
                return;
            }

            AudioClip clip = cue == CityBusDoorAudioCue.Opening
                ? GetOrCreateDoorOpeningClip()
                : GetOrCreateDoorClosingClip();
            float volume = playerInside
                ? CityBusAudioMix.InteriorDoorVolume
                : CityBusAudioMix.ExteriorDoorVolume;
            PlayDoorCue(frontDoorSource, clip, volume);
            PlayDoorCue(rearDoorSource, clip, volume * 0.94f);
            LastDoorCue = cue;
            if (cue == CityBusDoorAudioCue.Opening)
            {
                DoorOpeningCueCount++;
            }
            else
            {
                DoorClosingCueCount++;
            }
        }

        public void Stop()
        {
            isRunning = false;
            StopSource(exteriorEngineSource, false);
            StopSource(interiorEngineSource, false);
            StopSource(frontDoorSource, true);
            StopSource(rearDoorSource, true);
            cabinBlend = 0f;
            ResetDoorState();
        }

        private Transform CreateAnchor(string objectName, Vector3 localPosition)
        {
            Transform anchor = new GameObject(objectName).transform;
            anchor.SetParent(transform, false);
            anchor.localPosition = localPosition;
            anchor.localRotation = Quaternion.identity;
            anchor.localScale = Vector3.one;
            return anchor;
        }

        private static void ConfigureEngineSource(
            AudioSource source,
            AudioClip clip,
            float minimumDistance,
            float maximumDistance,
            int priority,
            float dopplerLevel)
        {
            ConfigureSpatialSource(
                source,
                minimumDistance,
                maximumDistance,
                priority,
                dopplerLevel);
            source.loop = true;
            source.clip = clip;
            source.volume = 0f;
            source.pitch = CityBusActor.EngineIdlePitch;
        }

        private static void ConfigureDoorSource(
            AudioSource source,
            float pitch)
        {
            ConfigureSpatialSource(
                source,
                CityBusAudioMix.DoorMinimumDistance,
                CityBusAudioMix.DoorMaximumDistance,
                CityBusAudioMix.DoorPriority,
                0f);
            source.loop = false;
            source.pitch = pitch;
            source.volume = 0f;
        }

        private static void ConfigureSpatialSource(
            AudioSource source,
            float minimumDistance,
            float maximumDistance,
            int priority,
            float dopplerLevel)
        {
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = minimumDistance;
            source.maxDistance = maximumDistance;
            source.priority = priority;
            source.dopplerLevel = dopplerLevel;
            GameAudioMixer.Route(source, GameAudioGroup.SfxWorld);
        }

        private void PositionDoorSource(
            AudioSource source,
            Transform doorEntryAnchor)
        {
            if (source == null || doorEntryAnchor == null)
            {
                throw new InvalidOperationException(
                    "Every bus door audio source requires its physical entry anchor.");
            }

            source.transform.localPosition = transform.InverseTransformPoint(
                doorEntryAnchor.position + (transform.up * 1.65f));
        }

        private CityBusDoorAudioCue ResolveDoorCue(
            CityBusDriverDoorSample sample)
        {
            if (sample.DoorPhase == CityBusDoorPhase.Opening &&
                previousDoorPhase == CityBusDoorPhase.Closed)
            {
                return CityBusDoorAudioCue.Opening;
            }

            if (sample.DoorPhase == CityBusDoorPhase.Closing &&
                previousDoorPhase != CityBusDoorPhase.Closing)
            {
                return CityBusDoorAudioCue.Closing;
            }

            bool skippedClosingPhase =
                sample.DoorPhase == CityBusDoorPhase.Closed &&
                previousDoorPhase != CityBusDoorPhase.Closed &&
                previousDoorPhase != CityBusDoorPhase.Closing;
            return skippedClosingPhase
                ? CityBusDoorAudioCue.Closing
                : CityBusDoorAudioCue.None;
        }

        private void PlayLoop(AudioSource source)
        {
            if (Application.isPlaying && !source.isPlaying)
            {
                source.Play();
            }
        }

        private void PlayDoorCue(
            AudioSource source,
            AudioClip clip,
            float volume)
        {
            source.Stop();
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.time = 0f;
            if (Application.isPlaying && isRunning)
            {
                source.Play();
            }
        }

        private static void StopSource(
            AudioSource source,
            bool clearClip)
        {
            if (source == null)
            {
                return;
            }

            source.Stop();
            source.volume = 0f;
            source.time = 0f;
            if (clearClip)
            {
                source.clip = null;
            }
            else
            {
                source.pitch = CityBusActor.EngineIdlePitch;
            }
        }

        private void ResetDoorState()
        {
            previousDoorPhase = CityBusDoorPhase.Closed;
            LastDoorCue = CityBusDoorAudioCue.None;
            DoorOpeningCueCount = 0;
            DoorClosingCueCount = 0;
        }

        private static AudioClip GetOrCreateExteriorEngineClip()
        {
            if (exteriorEngineClip != null)
            {
                return exteriorEngineClip;
            }

            var samples = new float[CityBusActor.EngineSampleRate];
            for (int index = 0; index < samples.Length; index++)
            {
                float time = index / (float)CityBusActor.EngineSampleRate;
                float pulse = 0.78f +
                              (Mathf.Sin(Mathf.PI * 2f * 12f * time) *
                               0.10f);
                float low =
                    (Mathf.Sin(Mathf.PI * 2f * 48f * time) * 0.22f) +
                    (Mathf.Sin(Mathf.PI * 2f * 96f * time) * 0.09f) +
                    (Mathf.Sin(Mathf.PI * 2f * 144f * time) * 0.045f);
                float readableDiesel =
                    (Mathf.Sin(Mathf.PI * 2f * 220f * time) * 0.060f) +
                    (Mathf.Sin(Mathf.PI * 2f * 330f * time) * 0.036f) +
                    (Mathf.Sin(Mathf.PI * 2f * 440f * time) * 0.022f);
                float clatter = Mathf.Sin(
                    Mathf.PI * 2f * 660f * time +
                    (Mathf.Sin(Mathf.PI * 2f * 6f * time) * 0.7f)) *
                    0.015f;
                samples[index] = Mathf.Clamp(
                    ((low * pulse) + readableDiesel + clatter) * 1.28f,
                    -0.92f,
                    0.92f);
            }

            exteriorEngineClip = CreateClip(
                ExteriorEngineClipName,
                samples,
                CityBusActor.EngineSampleRate);
            return exteriorEngineClip;
        }

        private static AudioClip GetOrCreateInteriorEngineClip()
        {
            if (interiorEngineClip != null)
            {
                return interiorEngineClip;
            }

            var samples = new float[CityBusActor.EngineSampleRate];
            for (int index = 0; index < samples.Length; index++)
            {
                float time = index / (float)CityBusActor.EngineSampleRate;
                float structure =
                    (Mathf.Sin(Mathf.PI * 2f * 43f * time) * 0.20f) +
                    (Mathf.Sin(Mathf.PI * 2f * 86f * time) * 0.085f) +
                    (Mathf.Sin(Mathf.PI * 2f * 172f * time) * 0.045f);
                float bodyRattle =
                    (Mathf.Sin(Mathf.PI * 2f * 257f * time) * 0.040f) +
                    (Mathf.Sin(Mathf.PI * 2f * 389f * time) * 0.024f);
                float uneasyBeat = 0.80f +
                    (Mathf.Sin(Mathf.PI * 2f * 11f * time) * 0.13f);
                samples[index] = Mathf.Clamp(
                    ((structure * uneasyBeat) + bodyRattle) * 1.35f,
                    -0.86f,
                    0.86f);
            }

            interiorEngineClip = CreateClip(
                InteriorEngineClipName,
                samples,
                CityBusActor.EngineSampleRate);
            return interiorEngineClip;
        }

        private static AudioClip GetOrCreateDoorOpeningClip()
        {
            if (doorOpeningClip == null)
            {
                doorOpeningClip = CreateDoorClip(true);
            }

            return doorOpeningClip;
        }

        private static AudioClip GetOrCreateDoorClosingClip()
        {
            if (doorClosingClip == null)
            {
                doorClosingClip = CreateDoorClip(false);
            }

            return doorClosingClip;
        }

        private static AudioClip CreateDoorClip(bool opening)
        {
            int sampleCount = Mathf.RoundToInt(
                DoorSampleRate * DoorClipDuration);
            var samples = new float[sampleCount];
            uint noiseState = opening ? 0x4255534Fu : 0x42555343u;
            float smoothedNoise = 0f;
            float valvePhase = 0f;
            for (int index = 0; index < samples.Length; index++)
            {
                float time = index / (float)DoorSampleRate;
                float amount = time / DoorClipDuration;
                noiseState = NextNoise(noiseState);
                float rawNoise = (((noiseState >> 8) & 0x00FFFFFFu) /
                                  8388607.5f) - 1f;
                smoothedNoise += (rawNoise - smoothedNoise) * 0.16f;
                float airEnvelope = SmoothEnvelope(amount, 0.025f, 0.78f);
                float air = (rawNoise - (smoothedNoise * 0.65f)) *
                            airEnvelope * 0.28f;

                float valveFrequency = opening
                    ? Mathf.Lerp(315f, 165f, amount)
                    : Mathf.Lerp(235f, 125f, amount);
                valvePhase += Mathf.PI * 2f * valveFrequency /
                              DoorSampleRate;
                float valve = Mathf.Sin(valvePhase) *
                              airEnvelope * 0.085f;

                float terminalStart = opening ? 0.76f : 0.72f;
                float terminalTime = Mathf.Max(
                    0f,
                    amount - terminalStart) * DoorClipDuration;
                float terminal = amount >= terminalStart
                    ? Mathf.Exp(-terminalTime * 34f) *
                      ((Mathf.Sin(Mathf.PI * 2f * 118f * terminalTime) *
                        0.34f) +
                       (Mathf.Sin(Mathf.PI * 2f * 472f * terminalTime) *
                        0.13f))
                    : 0f;
                float relay = Mathf.Exp(-time * 48f) *
                              Mathf.Sin(Mathf.PI * 2f * 710f * time) *
                              0.16f;
                samples[index] = Mathf.Clamp(
                    (air + valve + terminal + relay) * 1.18f,
                    -0.78f,
                    0.78f);
            }

            return CreateClip(
                opening ? DoorOpeningClipName : DoorClosingClipName,
                samples,
                DoorSampleRate);
        }

        private static float SmoothEnvelope(
            float amount,
            float attack,
            float releaseStart)
        {
            float rise = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(amount / attack));
            float fall = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(
                    (amount - releaseStart) /
                    Mathf.Max(0.0001f, 1f - releaseStart)));
            return rise * fall;
        }

        private static uint NextNoise(uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state != 0u ? state : 0xA341316Cu;
        }

        private static AudioClip CreateClip(
            string clipName,
            float[] samples,
            int sampleRate)
        {
            AudioClip clip = AudioClip.Create(
                clipName,
                samples.Length,
                1,
                sampleRate,
                false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
