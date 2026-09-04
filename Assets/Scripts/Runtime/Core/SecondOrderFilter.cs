using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A second-order lag: the value chases its target like a mass on a
    /// spring with a damper, so a channel that would otherwise be written
    /// straight from a model arrives late, overshoots by what its damping
    /// ratio allows, and settles. It is the inertia of a limb: an arm the
    /// balance model throws out flies past the mark and comes back, a lean
    /// follows the centre of mass with a little lag instead of snapping to
    /// it.
    ///
    /// Pure, explicit sub-stepped Euler (the car suspension's idiom), and
    /// exactly inert at rest: a filter sitting at zero with a zero target
    /// stays bit-for-bit zero, and a filter that has decayed to within
    /// <see cref="SnapEpsilon"/> of zero snaps there, so the sober hero
    /// never integrates anything and a long idle cannot creep.
    /// </summary>
    public struct SecondOrderFilter
    {
        /// <summary>
        /// The longest integration step taken in one go. A dropped frame
        /// is walked in several so the spring settles instead of blowing
        /// up; with the stiffest filter in use (<c>14 rad/s</c>) the
        /// product <c>ω·h</c> stays at <c>0.14</c>.
        /// </summary>
        public const float MaximumSubStepSeconds = 0.01f;

        /// <summary>
        /// A delta longer than this is not integrated further: every
        /// filter in use has settled long before it, and a pause that
        /// long should not cost thousands of sub-steps on resume.
        /// </summary>
        public const float MaximumAdvanceSeconds = 1f;

        /// <summary>
        /// Below this, in target, value and velocity together, the filter
        /// is at rest and reads exactly zero.
        /// </summary>
        public const float SnapEpsilon = 1e-4f;

        private readonly float angularFrequency;
        private readonly float dampingRatio;
        private float value;
        private float velocity;

        /// <param name="angularFrequency">Natural frequency, radians per second.</param>
        /// <param name="dampingRatio">
        /// Under one overshoots and rings, one is critically damped, above
        /// one crawls.
        /// </param>
        public SecondOrderFilter(float angularFrequency, float dampingRatio)
        {
            this.angularFrequency = Mathf.Max(0.01f, angularFrequency);
            this.dampingRatio = Mathf.Max(0f, dampingRatio);
            value = 0f;
            velocity = 0f;
        }

        public float AngularFrequency => angularFrequency;
        public float DampingRatio => dampingRatio;

        /// <summary>Where the filtered channel is now.</summary>
        public float Value => value;

        /// <summary>How fast it is moving, units per second.</summary>
        public float Velocity => velocity;

        /// <summary>
        /// Chases <paramref name="target"/> for <paramref name="deltaTime"/>
        /// seconds and returns the new value. A non-positive or NaN delta
        /// changes nothing and returns the current value, which is what
        /// makes a zero-delta re-apply deterministic.
        /// </summary>
        public float Advance(float target, float deltaTime)
        {
            if (float.IsNaN(deltaTime) || deltaTime <= 0f)
            {
                return value;
            }

            if (float.IsNaN(target) || float.IsInfinity(target))
            {
                return value;
            }

            if (Mathf.Abs(target) < SnapEpsilon &&
                Mathf.Abs(value) < SnapEpsilon &&
                Mathf.Abs(velocity) < SnapEpsilon)
            {
                // At rest, at zero: nothing to integrate, and nothing to
                // integrate FROM — the sober channel stays exact.
                value = 0f;
                velocity = 0f;
                return 0f;
            }

            float remaining = Mathf.Min(deltaTime, MaximumAdvanceSeconds);
            while (remaining > 0f)
            {
                float step = Mathf.Min(remaining, MaximumSubStepSeconds);
                remaining -= step;
                float acceleration =
                    (angularFrequency * angularFrequency * (target - value)) -
                    (2f * dampingRatio * angularFrequency * velocity);
                velocity += acceleration * step;
                value += velocity * step;
            }

            return value;
        }

        /// <summary>Puts the channel at a value, at rest.</summary>
        public void Reset(float restValue = 0f)
        {
            value = float.IsNaN(restValue) || float.IsInfinity(restValue)
                ? 0f
                : restValue;
            velocity = 0f;
        }
    }
}
