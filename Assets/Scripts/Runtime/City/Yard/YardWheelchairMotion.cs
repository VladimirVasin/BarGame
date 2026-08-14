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
            float rightWheelSpin)
        {
            Position = position;
            Rotation = rotation;
            HeadingDegrees = headingDegrees;
            SlipDegrees = slipDegrees;
            Speed = speed;
            LeftWheelSpin = leftWheelSpin;
            RightWheelSpin = rightWheelSpin;
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

        /// <summary>How far the line wanders off the worn ring, in metres.</summary>
        public const float RadiusWander = 0.14f;

        public const float RadiusWanderLaps = 0.83f;

        /// <summary>Outward lean held through the slide, in degrees.</summary>
        public const float LeanDegrees = 4.5f;

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

            // The ridden line is never exactly the worn ring.
            float wander = Mathf.Sin(
                lap / Mathf.Max(0.01f, RadiusWanderLaps) * Mathf.PI * 2f);
            float ridingRadius = radius + wander * RadiusWander;

            float angle = turn * distance / radius;
            var offset = new Vector3(
                Mathf.Cos(angle) * ridingRadius,
                0f,
                Mathf.Sin(angle) * ridingRadius);
            Vector3 position = plan.Center + offset;
            position.y = plan.GroundY;

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
            float speed = CruiseSpeed * (1f - SpeedSway * 0.5f +
                                         breath * SpeedSway * 0.5f);

            Quaternion rotation =
                Quaternion.Euler(0f, heading + slip, 0f) *
                Quaternion.Euler(0f, 0f, turn * LeanDegrees);

            // Differential: the outer wheel covers more ground than the
            // inner one, and the drift adds scrub on top.
            float scrub = 1f + Mathf.Abs(Mathf.Sin(slip * Mathf.Deg2Rad)) *
                          0.35f;
            float innerFactor =
                (ridingRadius - WheelHalfTrack) / ridingRadius;
            float outerFactor =
                (ridingRadius + WheelHalfTrack) / ridingRadius;
            float spin = distance * scrub / WheelRadius * Mathf.Rad2Deg;
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
                rightSpin);
        }

        /// <summary>
        /// Distance covered over one step. Sampling the speed at the start
        /// of the step keeps the advance a pure function of state.
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
            float lapLength = Mathf.PI * 2f * Mathf.Max(0.5f, plan.Radius);
            return Mathf.Repeat(
                distance + pose.Speed * deltaTime,
                lapLength);
        }
    }
}
