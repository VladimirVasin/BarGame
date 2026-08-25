using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The car's springs, as three independent damped oscillators: how far
    /// the body has sunk, how far the nose has tipped and how far it has
    /// leaned.
    ///
    /// Nothing here drives anything and nothing reads a transform, so the
    /// whole spring is EditMode-testable - the bar patron's timeline idiom
    /// applied to a suspension. The bus's own suspension is a road wave
    /// sampled from distance travelled; this car never moves again, so what
    /// it needs instead is a KICK: a man's weight leaving the bonnet, a
    /// man's weight arriving in a seat, and the two or three seconds of
    /// settling afterwards that say the thing has springs at all.
    ///
    /// Under-damped on purpose. A critically damped car sags once and reads
    /// as a lift lowering; this rocks twice and stops, which is what a tired
    /// saloon on twenty-year-old dampers does.
    /// </summary>
    public sealed class LastRouteCarSuspensionModel
    {
        /// <summary>
        /// Ring frequency of the body on its springs, in radians per second
        /// - a shade over one hertz, the band a full-size car actually sits
        /// in.
        /// </summary>
        public const float AngularFrequency = 6.9f;

        /// <summary>Damping ratio. Under one, so it oscillates; high
        /// enough that it is done inside about two seconds.</summary>
        public const float DampingRatio = 0.35f;

        /// <summary>Travel ceilings. A car that heaves more than three
        /// and a half centimetres reads as a boat.</summary>
        public const float MaximumHeave = 0.035f;
        public const float MaximumPitchDegrees = 1.2f;
        public const float MaximumRollDegrees = 1.4f;

        /// <summary>
        /// The longest step the integrator will take in one go. A hitch
        /// longer than this is walked in several - an explicit Euler spring
        /// handed a whole dropped frame diverges instead of settling.
        /// </summary>
        public const float MaximumSubStepSeconds = 0.02f;

        private float heaveVelocity;
        private float pitchVelocity;
        private float rollVelocity;

        /// <summary>Metres the body has sunk below its rest height;
        /// positive is UP.</summary>
        public float Heave { get; private set; }

        /// <summary>Degrees of nose lift; positive tips the nose UP.
        /// </summary>
        public float PitchDegrees { get; private set; }

        /// <summary>Degrees of lean; positive dips the car's own RIGHT
        /// side.</summary>
        public float RollDegrees { get; private set; }

        /// <summary>True while any channel is still visibly moving.
        /// </summary>
        public bool IsSettled =>
            Mathf.Abs(Heave) < 0.0002f &&
            Mathf.Abs(PitchDegrees) < 0.005f &&
            Mathf.Abs(RollDegrees) < 0.005f &&
            Mathf.Abs(heaveVelocity) < 0.002f &&
            Mathf.Abs(pitchVelocity) < 0.05f &&
            Mathf.Abs(rollVelocity) < 0.05f;

        /// <summary>
        /// A weight arriving or leaving. Impulses are velocities, not
        /// displacements, so a kick starts at rest and swings out - which
        /// is the difference between a car that is pushed and one that
        /// teleports into a lean.
        /// </summary>
        public void Nudge(
            float heaveImpulse,
            float pitchImpulse,
            float rollImpulse)
        {
            heaveVelocity += Sanitize(heaveImpulse);
            pitchVelocity += Sanitize(pitchImpulse);
            rollVelocity += Sanitize(rollImpulse);
        }

        public void Reset()
        {
            Heave = 0f;
            PitchDegrees = 0f;
            RollDegrees = 0f;
            heaveVelocity = 0f;
            pitchVelocity = 0f;
            rollVelocity = 0f;
        }

        public void Advance(float deltaTime)
        {
            float remaining = Sanitize(deltaTime);
            if (remaining <= 0f)
            {
                return;
            }

            while (remaining > 0f)
            {
                float step = Mathf.Min(remaining, MaximumSubStepSeconds);
                remaining -= step;

                float heave = Heave;
                float pitch = PitchDegrees;
                float roll = RollDegrees;
                Integrate(ref heave, ref heaveVelocity, step);
                Integrate(ref pitch, ref pitchVelocity, step);
                Integrate(ref roll, ref rollVelocity, step);

                // A channel that reaches its stop loses its velocity with
                // it. Clamping the displacement alone would leave the
                // spring pressed against the ceiling carrying all its
                // energy, and it would sit there until the sign flipped.
                Heave = Clamp(heave, MaximumHeave, ref heaveVelocity);
                PitchDegrees = Clamp(
                    pitch, MaximumPitchDegrees, ref pitchVelocity);
                RollDegrees = Clamp(
                    roll, MaximumRollDegrees, ref rollVelocity);
            }
        }

        private static float Clamp(
            float displacement,
            float limit,
            ref float velocity)
        {
            if (displacement > limit)
            {
                velocity = Mathf.Min(velocity, 0f);
                return limit;
            }

            if (displacement < -limit)
            {
                velocity = Mathf.Max(velocity, 0f);
                return -limit;
            }

            return displacement;
        }

        private static void Integrate(
            ref float displacement,
            ref float velocity,
            float step)
        {
            float acceleration =
                (-AngularFrequency * AngularFrequency * displacement) -
                (2f * DampingRatio * AngularFrequency * velocity);
            velocity += acceleration * step;
            displacement += velocity * step;
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }
}
