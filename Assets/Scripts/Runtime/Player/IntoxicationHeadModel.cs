using UnityEngine;

namespace BarPromenade
{
    /// <summary>What the drink does to the head this frame, degrees; all zero sober.</summary>
    public readonly struct IntoxicationHeadPose
    {
        public IntoxicationHeadPose(float yawDegrees, float pitchDownDegrees, float rollDegrees)
        {
            YawDegrees = yawDegrees;
            PitchDownDegrees = pitchDownDegrees;
            RollDegrees = rollDegrees;
        }

        public static IntoxicationHeadPose None => default;

        /// <summary>A turn of the head, positive to the hero's right.</summary>
        public float YawDegrees { get; }

        /// <summary>The chin dropping, positive down.</summary>
        public float PitchDownDegrees { get; }

        /// <summary>The head tilting, positive toward the hero's right shoulder.</summary>
        public float RollDegrees { get; }

        public bool IsNone => YawDegrees == 0f && PitchDownDegrees == 0f && RollDegrees == 0f;
    }

    /// <summary>The numbers of the drunk head, each scaled by the level <c>t</c>.</summary>
    public static class IntoxicationHeadRules
    {
        /// <summary>The chin sinks by this much at the full level, fading in over the first fifth of the scale.</summary>
        public const float DroopMinimumDegrees = 2f;
        public const float DroopMaximumDegrees = 12f;
        public const float DroopFadeLevel = 0.2f;

        /// <summary>The slow wander of a head that will not hold still, amplitude (times <c>t</c>) and hertz.</summary>
        public const float WanderPitchDegrees = 3f;
        public const float WanderPitchHertz = 0.15f;
        public const float WanderYawDegrees = 8f;
        public const float WanderYawHertz = 0.10f;
        public const float WanderRollDegrees = 4f;
        public const float WanderRollHertz = 0.12f;

        /// <summary>Past this level the head nods off now and then: this deep, this quick, this slow to come back, this often.</summary>
        public const float NodLevel = 0.6f;
        public const float NodDegrees = 15f;
        public const float NodDropSeconds = 0.25f;
        public const float NodReturnSeconds = 0.8f;
        public const float NodIntervalMinimumSeconds = 6f;
        public const float NodIntervalMaximumSeconds = 14f;

        /// <summary>The head follows the body's lean late and past it: the spring it hangs on, and how much of the lean it shows.</summary>
        public const float LagFrequency = 6f;
        public const float LagDampingRatio = 0.5f;
        public const float LagShare = 0.6f;

        /// <summary>The shape of one nod over its whole duration (<c>0..1</c> of the depth).</summary>
        public static float NodShape(float secondsIntoNod)
        {
            if (secondsIntoNod <= 0f)
            {
                return 0f;
            }

            if (secondsIntoNod < NodDropSeconds)
            {
                return Mathf.SmoothStep(0f, 1f, secondsIntoNod / NodDropSeconds);
            }

            float back = (secondsIntoNod - NodDropSeconds) / NodReturnSeconds;
            return back >= 1f ? 0f : 1f - Mathf.SmoothStep(0f, 1f, back);
        }

        public static float NodSeconds => NodDropSeconds + NodReturnSeconds;
    }

    /// <summary>
    /// The drunk head, pure and seeded: the chin sinks with the level,
    /// the head wanders slowly on three seeded phases, nods off now and
    /// then when far gone, and follows the body's lean late and past it
    /// on a loose spring. Sober it is exactly still and spends nothing.
    /// </summary>
    public sealed class IntoxicationHeadModel
    {
        private readonly System.Random random;
        private readonly float pitchPhase;
        private readonly float yawPhase;
        private readonly float rollPhase;
        private float time;
        private float nextNodAt;
        private float nodStartedAt = float.NegativeInfinity;
        private SecondOrderFilter rollLag;
        private SecondOrderFilter pitchLag;
        private IntoxicationHeadPose pose = IntoxicationHeadPose.None;

        public IntoxicationHeadModel(int seed)
        {
            random = new System.Random(seed);
            pitchPhase = Unit() * Mathf.PI * 2f;
            yawPhase = Unit() * Mathf.PI * 2f;
            rollPhase = Unit() * Mathf.PI * 2f;
            nextNodAt = NextNodInterval();
            rollLag = new SecondOrderFilter(
                IntoxicationHeadRules.LagFrequency,
                IntoxicationHeadRules.LagDampingRatio);
            pitchLag = new SecondOrderFilter(
                IntoxicationHeadRules.LagFrequency,
                IntoxicationHeadRules.LagDampingRatio);
        }

        public IntoxicationHeadPose Pose => pose;
        public int NodsTaken { get; private set; }
        public bool Nodding => time - nodStartedAt < IntoxicationHeadRules.NodSeconds;

        /// <summary>Forgets the motion in progress (a clip took the head); the seed's sequence goes on.</summary>
        public void Reset()
        {
            rollLag.Reset();
            pitchLag.Reset();
            pose = IntoxicationHeadPose.None;
        }

        /// <summary>
        /// Advances by <paramref name="deltaTime"/> at <paramref name="intoxication"/>,
        /// given the body's lean this frame (roll positive right, pitch
        /// positive forward, degrees).
        /// </summary>
        public IntoxicationHeadPose Advance(
            float deltaTime,
            float intoxication,
            float leanRollDegrees,
            float leanPitchDegrees)
        {
            float t = Mathf.Clamp01(intoxication);
            if (t <= 0f || deltaTime <= 0f || float.IsNaN(deltaTime))
            {
                if (t <= 0f)
                {
                    Reset();
                }

                return pose;
            }

            deltaTime = Mathf.Min(deltaTime, 0.25f);
            time += deltaTime;

            float droop = Mathf.Lerp(
                              IntoxicationHeadRules.DroopMinimumDegrees,
                              IntoxicationHeadRules.DroopMaximumDegrees,
                              t) *
                          Mathf.Clamp01(t / IntoxicationHeadRules.DroopFadeLevel);
            float wanderPitch = IntoxicationHeadRules.WanderPitchDegrees * t *
                                Mathf.Sin(time * IntoxicationHeadRules.WanderPitchHertz * Mathf.PI * 2f + pitchPhase);
            float wanderYaw = IntoxicationHeadRules.WanderYawDegrees * t *
                              Mathf.Sin(time * IntoxicationHeadRules.WanderYawHertz * Mathf.PI * 2f + yawPhase);
            float wanderRoll = IntoxicationHeadRules.WanderRollDegrees * t *
                               Mathf.Sin(time * IntoxicationHeadRules.WanderRollHertz * Mathf.PI * 2f + rollPhase);

            float nod = 0f;
            if (t > IntoxicationHeadRules.NodLevel)
            {
                if (!Nodding && time >= nextNodAt)
                {
                    nodStartedAt = time;
                    nextNodAt = time + IntoxicationHeadRules.NodSeconds + NextNodInterval();
                    NodsTaken++;
                }

                if (Nodding)
                {
                    nod = IntoxicationHeadRules.NodDegrees *
                          IntoxicationHeadRules.NodShape(time - nodStartedAt);
                }
            }
            else if (time >= nextNodAt)
            {
                // Not far gone enough to nod: the clock keeps its rhythm
                // without spending a draw.
                nextNodAt = time + IntoxicationHeadRules.NodIntervalMinimumSeconds;
            }

            // The head is the last thing to arrive at a lean, and it
            // overshoots: relative to the body it trails the lean, then
            // swings past it.
            float roll = rollLag.Advance(leanRollDegrees, deltaTime);
            float pitch = pitchLag.Advance(leanPitchDegrees, deltaTime);
            float lagRoll = (roll - leanRollDegrees) * IntoxicationHeadRules.LagShare;
            float lagPitch = (pitch - leanPitchDegrees) * IntoxicationHeadRules.LagShare;

            pose = new IntoxicationHeadPose(
                wanderYaw,
                droop + wanderPitch + nod + lagPitch,
                wanderRoll + lagRoll);
            return pose;
        }

        private float NextNodInterval()
        {
            return Mathf.Lerp(
                IntoxicationHeadRules.NodIntervalMinimumSeconds,
                IntoxicationHeadRules.NodIntervalMaximumSeconds,
                Unit());
        }

        private float Unit()
        {
            return (float)random.NextDouble();
        }
    }
}
