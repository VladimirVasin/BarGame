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
        private AudioSource motorSource;
        private AudioClip motorClip;
        private AudioClip clackClip;
        private float travelledDistance;
        private float currentSpeed;
        private bool initialized;

        // The dock request, while one is in flight.
        private bool docking;
        private bool docked;
        private int dockedCabinIndex = -1;
        private float dockRemaining;

        // How far the line has run since it was last let go, which is what
        // the launch ramp is a function of.
        private float travelledSinceResume;

        /// <summary>
        /// Raised the instant the cabins have been posed, before anything
        /// else this frame.
        ///
        /// Anything CARRIED by a cabin listens to this rather than doing its
        /// own work in a `LateUpdate` and trusting the two to be ordered.
        /// They are not reliably: a component added during a scene build can
        /// have its first update deferred a frame relative to one that
        /// already existed, and a passenger written a frame late rides
        /// visibly behind the box he is sitting in.
        /// </summary>
        public event Action Moved;

        public bool IsInitialized => initialized;

        /// <summary>Rope run since the line was built, in metres. This is the
        /// parameter everything visible is a function of.</summary>
        public float TravelledDistance => travelledDistance;

        public float CurrentSpeed => currentSpeed;

        /// <summary>A cabin is standing on the boarding point and the line is
        /// still.</summary>
        public bool IsDocked => docked;

        /// <summary>A cabin has been called and the line is slowing.</summary>
        public bool IsDocking => docking;

        public IReadOnlyList<Transform> Cabins => cabins;
        public IReadOnlyList<AudioSource> AudioSources => audioSources;

        /// <summary>
        /// The cabin standing at the boarding point, or null while the line
        /// runs. This is what a ride attaches its passenger to.
        /// </summary>
        public Transform DockedCabin =>
            docked && dockedCabinIndex >= 0 && dockedCabinIndex < cabins.Count
                ? cabins[dockedCabinIndex]
                : null;

        /// <summary>Loop distance of the cabin at <paramref name="index"/>.
        /// </summary>
        public float GetCabinDistance(int index)
        {
            if (!initialized || index < 0 || index >= phases.Count)
            {
                return 0f;
            }

            return phases[index] * plan.LoopLength + travelledDistance;
        }

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
            // The reducer is optional: only a DRIVE station has one, and the
            // return station at the other end of the line is not entitled to
            // a motor it does not contain.
            if (cabinRoots == null ||
                cabinRoots.Count != plan.Cabins.Count ||
                visibleBullwheel == null ||
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
            // A return station has no reducer, because the drive is at the
            // other end of the line. It gets no motor voice at all rather
            // than a silent one hung off a fictional gearbox.
            motorSource = visibleReducer != null
                ? CreateMotorSource(visibleReducer)
                : null;
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

            travelledDistance = 0f;
            travelledSinceResume = float.PositiveInfinity;

            // The line is built STANDING, with a cabin already on the boarding
            // point, and turns only once somebody is in it.
            //
            // It used to be built running, and boarding was: press, wait about
            // nineteen seconds of silence while the rope brought one round,
            // then be seated. That wait carried a whole knot with it - a
            // `waitingForCabin` flag, a poll in `Update`, and the unanswered
            // question of what confirms to the player that the call landed.
            // A cabin that is simply there costs none of it, and it is the
            // honest reading of a freight line living out its last years: it
            // runs when somebody needs it, not around the clock.
            dockedCabinIndex = FindCabinOnPoint(plan.BoardingLoopDistance);
            docked = dockedCabinIndex >= 0;
            docking = false;
            dockRemaining = 0f;
            currentSpeed = docked ? 0f : plan.CabinSpeed;
            initialized = true;
            ApplyPresentation(0f, 0f, false);
            ApplyMotorVoice();
            if (Application.isPlaying && motorSource != null)
            {
                motorSource.Play();
            }
        }

        /// <summary>
        /// Which cabin is standing on the boarding point at build time, or
        /// `-1` if none is.
        ///
        /// A search rather than "cabin zero", so the two planners stay free to
        /// author their phases in any order - and so a line whose cabins
        /// happen to straddle the point starts running and is called in the
        /// old way rather than pretending to be docked somewhere it is not.
        /// </summary>
        private int FindCabinOnPoint(float loopDistance)
        {
            float dock = MountainCablewayMotion.WrapDistance(
                loopDistance,
                plan.LoopLength);
            for (int index = 0; index < phases.Count; index++)
            {
                float offset = MountainCablewayMotion.WrapDistance(
                    phases[index] * plan.LoopLength - dock,
                    plan.LoopLength);
                if (offset <= MountainCablewayDriveRules.DockEpsilon ||
                    plan.LoopLength - offset <=
                    MountainCablewayDriveRules.DockEpsilon)
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// Calls the next cabin to a loop distance and brings the line to
        /// rest with it standing there.
        ///
        /// The cabin is chosen and the distance fixed HERE, once. Re-deciding
        /// every frame would let a cabin that drifts past the point be
        /// re-targeted round the loop, and the line would never stop.
        /// </summary>
        public bool RequestDockAt(float loopDistance)
        {
            if (!initialized || docking || docked)
            {
                return false;
            }

            float dock = MountainCablewayMotion.WrapDistance(
                loopDistance,
                plan.LoopLength);
            float best = float.PositiveInfinity;
            int bestIndex = -1;
            for (int index = 0; index < cabins.Count; index++)
            {
                float approach =
                    MountainCablewayDriveRules.EvaluateApproachDistance(
                        GetCabinDistance(index),
                        dock,
                        plan.LoopLength);
                if (approach >= best)
                {
                    continue;
                }

                best = approach;
                bestIndex = index;
            }

            if (bestIndex < 0)
            {
                return false;
            }

            docking = true;
            docked = false;
            dockedCabinIndex = bestIndex;
            dockRemaining = best;
            return true;
        }

        /// <summary>Lets the line go again. Safe to call when it is already
        /// running.</summary>
        public bool Resume()
        {
            if (!initialized || (!docking && !docked))
            {
                return false;
            }

            docking = false;
            docked = false;
            dockedCabinIndex = -1;
            dockRemaining = 0f;
            travelledSinceResume = 0f;
            return true;
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

            float previousDistance = travelledDistance;
            float step = AdvanceDistance(deltaTime);
            travelledDistance = previousDistance + step;
            ApplyMotorVoice();
            ApplyPresentation(
                previousDistance,
                travelledDistance,
                Application.isPlaying && step > 0f);
            Moved?.Invoke();
        }

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        /// <summary>
        /// How far the rope runs this frame, and the whole of the start/stop
        /// behaviour.
        ///
        /// While a dock is in flight the speed is a function of the distance
        /// LEFT rather than of time, so the cabin comes to rest exactly on the
        /// point instead of near it - and the last step is clamped to what
        /// remains, which is what makes "exactly" true down to the millimetre
        /// at any frame rate.
        /// </summary>
        private float AdvanceDistance(float deltaTime)
        {
            if (docked)
            {
                currentSpeed = 0f;
                return 0f;
            }

            if (docking)
            {
                currentSpeed =
                    MountainCablewayDriveRules.EvaluateApproachSpeed(
                        dockRemaining,
                        plan.CabinSpeed);
                float step = Mathf.Min(
                    currentSpeed * deltaTime,
                    dockRemaining);
                dockRemaining -= step;
                if (dockRemaining <= MountainCablewayDriveRules.DockEpsilon)
                {
                    // Take up the remainder in this same step. Leaving it for
                    // a later frame is what turns an exact dock into an
                    // asymptote nobody can seat a passenger against.
                    step += dockRemaining;
                    dockRemaining = 0f;
                    docking = false;
                    docked = true;
                    currentSpeed = 0f;
                }

                return step;
            }

            currentSpeed = MountainCablewayDriveRules.EvaluateLaunchSpeed(
                travelledSinceResume,
                plan.CabinSpeed);
            float running = currentSpeed * deltaTime;
            if (!float.IsInfinity(travelledSinceResume))
            {
                travelledSinceResume += running;
            }

            return running;
        }

        /// <summary>
        /// The gearbox is heard braking and picking up. It is one line
        /// because the loop was already there; what it needed was a speed to
        /// be a function of.
        /// </summary>
        private void ApplyMotorVoice()
        {
            if (motorSource == null || plan.CabinSpeed <= 0f)
            {
                return;
            }

            float fraction = Mathf.Clamp01(currentSpeed / plan.CabinSpeed);
            motorSource.pitch = Mathf.Lerp(0.42f, 0.94f, fraction);

            // To SILENCE at rest, not to an idle hum. The line now spends most
            // of its life standing at the platform, and a gearbox murmuring
            // under a drive that is not turning would be the loudest wrong
            // thing on the summit.
            motorSource.volume = Mathf.Lerp(0f, 0.17f, fraction);
        }

        private void ApplyPresentation(
            float previousTravel,
            float currentTravel,
            bool allowAudio)
        {
            for (int index = 0; index < cabins.Count; index++)
            {
                float phaseDistance = phases[index] * plan.LoopLength;
                float previousDistance = phaseDistance + previousTravel;
                float currentDistance = phaseDistance + currentTravel;
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
            float wheelDegrees = currentTravel / circumference * 360f;
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
            motorSource = null;
            docking = false;
            docked = false;
            dockedCabinIndex = -1;
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
