using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One pose on the yard circuit, sampled from the distance travelled.
    /// </summary>
    public readonly struct YardWheelchairPose
    {
        public YardWheelchairPose(
            Vector3 position,
            Quaternion rotation,
            float headingDegrees,
            float slipDegrees,
            float speed,
            float leftWheelSpin,
            float rightWheelSpin,
            float pushPhase)
        {
            Position = position;
            Rotation = rotation;
            HeadingDegrees = headingDegrees;
            SlipDegrees = slipDegrees;
            Speed = speed;
            LeftWheelSpin = leftWheelSpin;
            RightWheelSpin = rightWheelSpin;
            PushPhase = pushPhase;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }

        /// <summary>Where the chair travels, in world degrees.</summary>
        public float HeadingDegrees { get; }

        /// <summary>
        /// How far the chair is turned away from where it is going. This is
        /// the whole drift: the nose points into the circle while the chair
        /// keeps sliding along it.
        /// </summary>
        public float SlipDegrees { get; }

        public float Speed { get; }
        public float LeftWheelSpin { get; }
        public float RightWheelSpin { get; }

        /// <summary>
        /// Where the chair is inside one hand-push cycle, 0..1: the
        /// stroke begins at 0, the coast carries the rest.
        /// </summary>
        public float PushPhase { get; }
    }

    /// <summary>
    /// The pure motion of the yard circuit. A man who has ridden the same
    /// ring for years does not steer it cleanly: the chair hangs sideways
    /// through the turn, the slip breathes as he shifts his weight, the
    /// radius never repeats exactly, and one wheel is always doing more
    /// work than the other.
    ///
    /// Everything here is a deterministic function of distance travelled,
    /// so the pose can be sampled in tests without a scene.
    /// </summary>
    public static class YardWheelchairMotion
    {
        /// <summary>Metres per second along the ring.</summary>
        public const float CruiseSpeed = 1.05f;

        /// <summary>How much the pace sags and recovers within one lap.</summary>
        public const float SpeedSway = 0.16f;

        /// <summary>The standing drift angle, in degrees.</summary>
        public const float BaseSlipDegrees = 19f;

        /// <summary>How far the drift breathes around the standing angle.</summary>
        public const float SlipBreathDegrees = 7.5f;

        /// <summary>Laps per full breath of the drift angle.</summary>
        public const float SlipBreathLaps = 0.37f;

        /// <summary>How far the line wanders off the ring, in metres.</summary>
        public const float RadiusWander = 0.14f;

        public const float RadiusWanderLaps = 0.83f;

        /// <summary>Outward lean held through the slide, in degrees.</summary>
        public const float LeanDegrees = 4.5f;

        /// <summary>
        /// Metres of ground one hand-push cycle covers: the stroke
        /// surges the chair forward, the coast bleeds it off again.
        /// </summary>
        public const float PushDistance = 1.35f;

        /// <summary>Fraction of the cycle spent on the stroke itself.</summary>
        public const float PushStrokeFraction = 0.24f;

        /// <summary>Speed multiplier at the top of the stroke.</summary>
        public const float PushSurge = 1.42f;

        /// <summary>Speed multiplier at the end of the coast.</summary>
        public const float CoastTrough = 0.62f;

        public const float WheelRadius = 0.3f;

        /// <summary>Half the track width, used for the wheel differential.</summary>
        public const float WheelHalfTrack = 0.31f;

        public static YardWheelchairPose Sample(
            YardWheelchairPlan plan,
            float distance)
        {
            float radius = Mathf.Max(0.5f, plan.Radius);
            float lapLength = Mathf.PI * 2f * radius;
            float lap = distance / lapLength;
            float turn = plan.Clockwise ? -1f : 1f;

            // The ridden line is never exactly the authored ring.
            float wander = Mathf.Sin(
                lap / Mathf.Max(0.01f, RadiusWanderLaps) * Mathf.PI * 2f);
            float ridingRadius = radius + wander * RadiusWander;

            float angle = turn * distance / radius;
            var offset = new Vector3(
                Mathf.Cos(angle) * ridingRadius,
                0f,
                Mathf.Sin(angle) * ridingRadius);
            Vector3 position = plan.Center + offset;

            // The yard is not one flat plane: the lap follows the
            // sampled ground profile across every terrace lip.
            position.y = plan.SampleGroundHeight(angle);

            // Travel direction: the tangent of the ring at this angle.
            var tangent = new Vector3(
                -Mathf.Sin(angle) * turn,
                0f,
                Mathf.Cos(angle) * turn);
            float heading = Mathf.Atan2(tangent.x, tangent.z) *
                            Mathf.Rad2Deg;

            // The drift itself: the chassis is yawed into the circle
            // against the direction of travel, and the angle breathes as
            // the weight shifts.
            float breath = Mathf.Sin(
                lap / Mathf.Max(0.01f, SlipBreathLaps) * Mathf.PI * 2f);
            float slip = -turn *
                         (BaseSlipDegrees + breath * SlipBreathDegrees);

            // The pace sags in the same rhythm, so the slide reads as
            // weariness rather than as sport.
            float swayFactor = 1f - SpeedSway * 0.5f +
                               breath * SpeedSway * 0.5f;

            // And over that, the ragged hand rhythm: every push is a
            // surge, every gap between pushes a slow bleed of speed.
            // Nothing here is periodic in time — the cycle lives on the
            // ground, so the wheels, the bellows and the pace can never
            // drift apart.
            float pushPhase =
                Mathf.Repeat(distance, PushDistance) / PushDistance;
            float pushFactor = SamplePushFactor(pushPhase);
            float speed = CruiseSpeed * swayFactor * pushFactor;

            Quaternion rotation =
                Quaternion.Euler(0f, heading + slip, 0f) *
                Quaternion.Euler(0f, 0f, turn * LeanDegrees);

            // Differential: the outer wheel covers more ground than the
            // inner one, and the drift adds scrub on top. The scrub is
            // integrated over the ground covered (linearised around the
            // standing angle) - multiplying the total distance by the
            // *current* scrub would swing the apparent wheel speed far
            // off the ground speed whenever the slip breathes.
            float breathRate = Mathf.PI * 2f /
                (Mathf.Max(0.01f, SlipBreathLaps) * lapLength);
            float scrubMean = 1f +
                0.35f * Mathf.Sin(BaseSlipDegrees * Mathf.Deg2Rad);
            float scrubSwing = 0.35f *
                Mathf.Cos(BaseSlipDegrees * Mathf.Deg2Rad) *
                SlipBreathDegrees * Mathf.Deg2Rad;
            float rolled = scrubMean * distance +
                scrubSwing / breathRate *
                (1f - Mathf.Cos(breathRate * distance));
            float innerFactor =
                (radius - WheelHalfTrack) / radius;
            float outerFactor =
                (radius + WheelHalfTrack) / radius;
            float spin = rolled / WheelRadius * Mathf.Rad2Deg;
            float leftSpin = spin *
                             (plan.Clockwise ? outerFactor : innerFactor);
            float rightSpin = spin *
                              (plan.Clockwise ? innerFactor : outerFactor);

            return new YardWheelchairPose(
                position,
                rotation,
                heading,
                slip,
                speed,
                leftSpin,
                rightSpin,
                pushPhase);
        }

        /// <summary>
        /// The speed multiplier across one push cycle: a fast smooth
        /// rise through the stroke, then a long fall through the coast.
        /// </summary>
        public static float SamplePushFactor(float pushPhase)
        {
            float phase = Mathf.Repeat(pushPhase, 1f);
            if (phase < PushStrokeFraction)
            {
                return Mathf.Lerp(
                    CoastTrough,
                    PushSurge,
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        phase / PushStrokeFraction));
            }

            return Mathf.Lerp(
                PushSurge,
                CoastTrough,
                Mathf.SmoothStep(
                    0f,
                    1f,
                    (phase - PushStrokeFraction) /
                    (1f - PushStrokeFraction)));
        }

        /// <summary>
        /// Distance covered over one step. Sampling the speed at the start
        /// of the step keeps the advance a pure function of state. The
        /// distance is never wrapped: the wander, breath, push and wheel
        /// channels are not lap-periodic, and a wrap snapped every one of
        /// them once per circuit - a visible pop. Float precision holds
        /// far past any session length at this pace.
        /// </summary>
        public static float Advance(
            YardWheelchairPlan plan,
            float distance,
            float deltaTime)
        {
            if (deltaTime <= 0f ||
                float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime))
            {
                return distance;
            }

            YardWheelchairPose pose = Sample(plan, distance);
            return distance + pose.Speed * deltaTime;
        }
    }
}
