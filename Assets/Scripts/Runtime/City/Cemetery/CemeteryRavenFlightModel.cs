using System;
using UnityEngine;

namespace BarPromenade
{
    public enum CemeteryRavenFlightKind
    {
        /// <summary>Startled off a perch: hop, deploy, climb away
        /// into the fog until the city cannot see the bird.</summary>
        Takeoff = 0,

        /// <summary>Back out of the fog onto an exact point: glide,
        /// flare, touch, refold, settle.</summary>
        Return = 1
    }

    public enum CemeteryRavenFlightPhase
    {
        Hop = 0,
        Climb = 1,
        Glide = 2,
        Flare = 3,
        Settle = 4,
        Done = 5
    }

    /// <summary>
    /// One evaluated moment of a flight. Position and yaw are what
    /// the actor writes to its root; the remaining channels feed
    /// <see cref="CemeteryRavenPoseRules.FlightPose"/>. Body pitch
    /// and dip ride along because the landing is not just a path:
    /// the flare pitches the breast up and the settle sinks the body
    /// a step, and both belong to the same pure timeline as the
    /// touch they decorate.
    /// </summary>
    public readonly struct CemeteryRavenFlightSample
    {
        public CemeteryRavenFlightSample(
            Vector3 position,
            float yawDegrees,
            float wingFold01,
            float flapPhaseRadians,
            float bodyPitchDegrees,
            float bodyDipMeters,
            CemeteryRavenFlightPhase phase,
            bool done)
        {
            Position = position;
            YawDegrees = yawDegrees;
            WingFold01 = wingFold01;
            FlapPhaseRadians = flapPhaseRadians;
            BodyPitchDegrees = bodyPitchDegrees;
            BodyDipMeters = bodyDipMeters;
            Phase = phase;
            Done = done;
        }

        public Vector3 Position { get; }
        public float YawDegrees { get; }
        public float WingFold01 { get; }
        public float FlapPhaseRadians { get; }
        public float BodyPitchDegrees { get; }
        public float BodyDipMeters { get; }
        public CemeteryRavenFlightPhase Phase { get; }
        public bool Done { get; }
    }

    /// <summary>
    /// A raven's whole flight as a pure timeline: constructed once
    /// with where the bird stands and where it must end up, then
    /// <see cref="Evaluate"/> is a pure function of absolute time, so
    /// every timing assertion runs in EditMode on the model directly
    /// and batchmode frame pacing can never touch it. The caller
    /// chooses the endpoints — a takeoff's end is a bearing away from
    /// the hero, a return's start is a spawn point outside the fog —
    /// and the model owns everything between them.
    /// </summary>
    public sealed class CemeteryRavenFlightModel
    {
        /// <summary>The push off the perch before the wings carry:
        /// legs first, the way a heavy bird actually leaves.</summary>
        public const float HopSeconds = 0.15f;
        public const float HopHeightMeters = 0.12f;

        public const float WingDeploySeconds = 0.25f;
        public const float ClimbSpeedMetersPerSecond = 7f;
        public const float ClimbRampSeconds = 1.5f;
        public const float FlapFrequencyHz = 5.5f;
        public const float ClimbBodyPitchDegrees = 12f;
        public const float ClimbPitchRampSeconds = 0.5f;
        public const float YawBlendSeconds = 0.4f;

        /// <summary>
        /// Where a takeoff may end: past the 48 m far plane's fog a
        /// bird has a contrast of nothing, so 46 m of travel is
        /// already invisible, and the time cap is the guard against a
        /// caller handing in a degenerate bearing.
        /// </summary>
        public const float DoneDistanceMeters = 46f;
        public const float TakeoffTimeoutSeconds = 8f;

        /// <summary>The climb bows sideways by up to this much, seeded
        /// per bird, so the pair never leaves on two parallel rails.
        /// </summary>
        public const float ArcMaximumDegrees = 20f;

        public const float GlideSpeedMetersPerSecond = 6.5f;
        public const float TouchdownSpeedMetersPerSecond = 1.8f;
        public const float DecelerationDistanceMeters = 4f;
        public const float FlareSeconds = 0.6f;
        public const float FlareFlapFrequencyHz = 2f;
        public const float FlareBodyPitchDegrees = 18f;
        public const float RefoldSeconds = 0.5f;
        public const float SettleDipMeters = 0.03f;

        private readonly Vector3 start;
        private readonly float startYawDegrees;
        private readonly Vector3 end;
        private readonly float endYawDegrees;
        private readonly CemeteryRavenFlightKind kind;

        private readonly Vector3 planarDirection;
        private readonly Vector3 planarRight;
        private readonly float planarDistance;
        private readonly float bearingYawDegrees;
        private readonly float arcLateralMeters;

        private readonly float cruiseSeconds;
        private readonly float decelerationSeconds;
        private readonly float cruiseLengthMeters;
        private readonly float touchSeconds;
        private readonly float flareStartSeconds;
        private readonly float doneSeconds;
        private readonly float flapPhaseAtTouchRadians;

        public CemeteryRavenFlightModel(
            Vector3 start,
            float startYawDegrees,
            Vector3 end,
            float endYawDegrees,
            CemeteryRavenFlightKind kind,
            int seed)
        {
            this.start = start;
            this.startYawDegrees = startYawDegrees;
            this.end = end;
            this.endYawDegrees = endYawDegrees;
            this.kind = kind;

            var planarDelta = new Vector3(
                end.x - start.x,
                0f,
                end.z - start.z);
            planarDistance = Mathf.Max(
                planarDelta.magnitude,
                0.001f);
            planarDirection = planarDelta.sqrMagnitude > 0.000001f
                ? planarDelta / planarDelta.magnitude
                : Quaternion.Euler(0f, endYawDegrees, 0f) *
                  Vector3.forward;
            planarRight = new Vector3(
                planarDirection.z,
                0f,
                -planarDirection.x);
            bearingYawDegrees = Mathf.Atan2(
                planarDirection.x,
                planarDirection.z) * Mathf.Rad2Deg;

            // The arc is a takeoff thing only. A returning bird aims
            // at one exact point, and bowing that approach would move
            // the touch unless it were compensated back out — the
            // caller's seeded spawn azimuth already varies the way
            // home, so exactness wins here.
            float arcDegrees =
                (Hash01(unchecked((uint)seed)) * 2f - 1f) *
                ArcMaximumDegrees;
            arcLateralMeters =
                Mathf.Tan(arcDegrees * Mathf.Deg2Rad) *
                DoneDistanceMeters * 0.25f;

            float decelerationLength = Mathf.Min(
                DecelerationDistanceMeters,
                planarDistance);
            cruiseLengthMeters = planarDistance - decelerationLength;
            cruiseSeconds =
                cruiseLengthMeters / GlideSpeedMetersPerSecond;
            decelerationSeconds =
                decelerationLength * 2f /
                (GlideSpeedMetersPerSecond +
                 TouchdownSpeedMetersPerSecond);
            touchSeconds = cruiseSeconds + decelerationSeconds;
            flareStartSeconds = Mathf.Max(
                0f,
                touchSeconds - FlareSeconds);
            doneSeconds = touchSeconds + RefoldSeconds;
            flapPhaseAtTouchRadians =
                ComputeReturnFlapPhase(touchSeconds);
        }

        public CemeteryRavenFlightKind Kind => kind;
        public Vector3 Start => start;
        public Vector3 End => end;
        public float StartYawDegrees => startYawDegrees;
        public float EndYawDegrees => endYawDegrees;

        public CemeteryRavenFlightSample Evaluate(double timeSeconds)
        {
            double time =
                double.IsNaN(timeSeconds) || timeSeconds < 0d
                    ? 0d
                    : timeSeconds;
            return kind == CemeteryRavenFlightKind.Takeoff
                ? EvaluateTakeoff(time)
                : EvaluateReturn(time);
        }

        private CemeteryRavenFlightSample EvaluateTakeoff(
            double timeSeconds)
        {
            float t = (float)Math.Min(
                timeSeconds,
                TakeoffTimeoutSeconds);
            float fold01 = Mathf.Clamp01(t / WingDeploySeconds);
            float flapPhase =
                Mathf.PI * 2f * FlapFrequencyHz * t;
            if (t < HopSeconds)
            {
                Vector3 hopPosition = start + Vector3.up *
                    (HopHeightMeters * SmoothStep01(t / HopSeconds));
                return new CemeteryRavenFlightSample(
                    hopPosition,
                    startYawDegrees,
                    fold01,
                    flapPhase,
                    0f,
                    0f,
                    CemeteryRavenFlightPhase.Hop,
                    false);
            }

            float climbTime = t - HopSeconds;
            float distance = climbTime < ClimbRampSeconds
                ? 0.5f * ClimbSpeedMetersPerSecond *
                  climbTime * climbTime / ClimbRampSeconds
                : 0.5f * ClimbSpeedMetersPerSecond *
                  ClimbRampSeconds +
                  ClimbSpeedMetersPerSecond *
                  (climbTime - ClimbRampSeconds);
            float progress01 = Mathf.Clamp01(
                distance / DoneDistanceMeters);
            float lateral =
                arcLateralMeters *
                Mathf.Sin(Mathf.PI * progress01);
            Vector3 planar = start +
                planarDirection * distance +
                planarRight * lateral;
            float hopTopY = start.y + HopHeightMeters;
            var position = new Vector3(
                planar.x,
                Mathf.Lerp(
                    hopTopY,
                    end.y,
                    SmoothStep01(progress01)),
                planar.z);
            float yaw = Mathf.LerpAngle(
                startYawDegrees,
                bearingYawDegrees,
                Mathf.Clamp01(climbTime / YawBlendSeconds));
            float pitch = ClimbBodyPitchDegrees * Mathf.Clamp01(
                climbTime / ClimbPitchRampSeconds);
            float displacement = new Vector2(
                position.x - start.x,
                position.z - start.z).magnitude;
            bool done =
                displacement >= DoneDistanceMeters ||
                timeSeconds >= TakeoffTimeoutSeconds;
            return new CemeteryRavenFlightSample(
                position,
                yaw,
                fold01,
                flapPhase,
                pitch,
                0f,
                done
                    ? CemeteryRavenFlightPhase.Done
                    : CemeteryRavenFlightPhase.Climb,
                done);
        }

        private CemeteryRavenFlightSample EvaluateReturn(
            double timeSeconds)
        {
            if (timeSeconds >= doneSeconds)
            {
                // Landed, folded, still: the exact perch, nothing
                // residual — the perched idle takes over from a
                // clean zero.
                return new CemeteryRavenFlightSample(
                    end,
                    endYawDegrees,
                    0f,
                    flapPhaseAtTouchRadians,
                    0f,
                    0f,
                    CemeteryRavenFlightPhase.Done,
                    true);
            }

            float t = (float)timeSeconds;
            if (t >= touchSeconds)
            {
                float settle01 = Mathf.Clamp01(
                    (t - touchSeconds) / RefoldSeconds);
                return new CemeteryRavenFlightSample(
                    end,
                    endYawDegrees,
                    1f - settle01,
                    flapPhaseAtTouchRadians,
                    FlareBodyPitchDegrees * (1f - settle01),
                    SettleDipMeters *
                    Mathf.Sin(Mathf.PI * settle01),
                    CemeteryRavenFlightPhase.Settle,
                    false);
            }

            float distance;
            if (t <= cruiseSeconds)
            {
                distance = GlideSpeedMetersPerSecond * t;
            }
            else if (decelerationSeconds > 0.0001f)
            {
                float braking = t - cruiseSeconds;
                distance = cruiseLengthMeters +
                    GlideSpeedMetersPerSecond * braking -
                    0.5f *
                    (GlideSpeedMetersPerSecond -
                     TouchdownSpeedMetersPerSecond) *
                    braking * braking / decelerationSeconds;
            }
            else
            {
                distance = planarDistance;
            }

            // Positions are laid off from the END rather than the
            // start, so the touch lands on the perch to the last
            // float bit instead of to a sum of increments.
            float remaining = Mathf.Max(
                0f,
                planarDistance - distance);
            float remaining01 = Mathf.Clamp01(
                remaining / planarDistance);
            Vector3 planar = end - planarDirection * remaining;
            var position = new Vector3(
                planar.x,
                end.y + (start.y - end.y) *
                SmoothStep01(remaining01),
                planar.z);

            if (t < flareStartSeconds)
            {
                return new CemeteryRavenFlightSample(
                    position,
                    bearingYawDegrees,
                    1f,
                    Mathf.PI * 2f * FlapFrequencyHz * t,
                    0f,
                    0f,
                    CemeteryRavenFlightPhase.Glide,
                    false);
            }

            float flareDuration = Mathf.Max(
                0.0001f,
                touchSeconds - flareStartSeconds);
            float flare01 = Mathf.Clamp01(
                (t - flareStartSeconds) / flareDuration);
            return new CemeteryRavenFlightSample(
                position,
                Mathf.LerpAngle(
                    bearingYawDegrees,
                    endYawDegrees,
                    flare01),
                1f,
                ComputeReturnFlapPhase(t),
                FlareBodyPitchDegrees * flare01,
                0f,
                CemeteryRavenFlightPhase.Flare,
                false);
        }

        /// <summary>
        /// The wingbeat's phase is an integral of its slowing
        /// frequency, in closed form, so it is the same pure function
        /// of absolute time as everything else here — a beat that
        /// accumulated per frame would drift with the frame rate.
        /// </summary>
        private float ComputeReturnFlapPhase(float t)
        {
            float steady = Mathf.Min(t, flareStartSeconds);
            float phaseTurns = FlapFrequencyHz * steady;
            if (t > flareStartSeconds)
            {
                float flareDuration = Mathf.Max(
                    0.0001f,
                    touchSeconds - flareStartSeconds);
                float braking = Mathf.Min(
                    t - flareStartSeconds,
                    flareDuration);
                phaseTurns +=
                    FlapFrequencyHz * braking -
                    0.5f *
                    (FlapFrequencyHz - FlareFlapFrequencyHz) *
                    braking * braking / flareDuration;
            }

            return Mathf.PI * 2f * phaseTurns;
        }

        private static float SmoothStep01(float value)
        {
            float clamped = Mathf.Clamp01(value);
            return clamped * clamped * (3f - 2f * clamped);
        }

        private static float Hash01(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 16777215f;
        }
    }
}
