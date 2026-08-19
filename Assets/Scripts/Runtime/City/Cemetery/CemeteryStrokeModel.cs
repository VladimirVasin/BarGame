using UnityEngine;

namespace BarPromenade
{
    /// <summary>What the blade did.</summary>
    public enum CemeteryStrokeOutcome
    {
        /// <summary>Nothing was swung.</summary>
        None = 0,

        /// <summary>In. A full strike of the course.</summary>
        Bite = 1,

        /// <summary>Off the edge of it — the blade slid and took
        /// nothing.</summary>
        Graze = 2,

        /// <summary>Flat on the ground. It rings, and a root you were
        /// cutting has to be started again.</summary>
        Jar = 3
    }

    /// <summary>
    /// One swing of the spade, as a bar the hero times rather than a
    /// button he mashes. The marker runs the swing back and forth
    /// through the ground's biting window; he holds the key while it
    /// runs and lets go when it is in.
    ///
    /// Letting the marker pass is free — it comes round again. That
    /// is deliberate: the game is timing, not reflex punishment, and a
    /// man leaning on a spade waiting for his moment is exactly the
    /// picture.
    ///
    /// The simulation steps at a fixed rate, like
    /// <see cref="BalanceChallengeModel"/>, so a hitched frame cannot
    /// slide the marker past the window.
    /// </summary>
    public sealed class CemeteryStrokeModel
    {
        public const float FixedStep = 1f / 120f;

        private CemeterySoilProfile profile;
        private float phase;
        private float accumulator;

        /// <summary>True while a swing is up and running.</summary>
        public bool IsSwinging { get; private set; }

        /// <summary>Where the marker is on the bar, from
        /// <c>-1</c> to <c>1</c>. The biting window is centred on
        /// <c>0</c>.</summary>
        public float Position { get; private set; }

        /// <summary>The ground this swing is being taken against.
        /// </summary>
        public CemeterySoilProfile Profile => profile;

        /// <summary>Full sweeps completed since the swing began, for
        /// a view that wants to darken a marker the hero has been
        /// sitting on too long.</summary>
        public int Sweeps { get; private set; }

        /// <summary>
        /// Winds up a swing against one kind of ground. The seed only
        /// shifts where on the bar the marker starts, so two strikes
        /// on the same segment do not feel like a metronome.
        /// </summary>
        public void Begin(CemeterySoilProfile soilProfile, int seed)
        {
            profile = soilProfile;
            phase = UnitFromBits(unchecked((uint)seed)) *
                    Mathf.PI *
                    2f;
            accumulator = 0f;
            Sweeps = 0;
            IsSwinging = true;
            Position = Mathf.Sin(phase);
        }

        public void Advance(float deltaTime)
        {
            if (!IsSwinging)
            {
                return;
            }

            accumulator += Mathf.Clamp(deltaTime, 0f, 0.5f);
            while (accumulator + 0.000001f >= FixedStep)
            {
                SimulateStep(FixedStep);
                accumulator -= FixedStep;
            }
        }

        /// <summary>
        /// Lets the blade go. Reads the marker where it stands and
        /// ends the swing. A release with nothing swinging is
        /// <see cref="CemeteryStrokeOutcome.None"/> and changes
        /// nothing.
        /// </summary>
        public CemeteryStrokeOutcome Release()
        {
            if (!IsSwinging)
            {
                return CemeteryStrokeOutcome.None;
            }

            IsSwinging = false;
            return Resolve(Position, profile);
        }

        /// <summary>Drops the swing without striking — the hero was
        /// interrupted, or the act ended under him.</summary>
        public void Cancel()
        {
            IsSwinging = false;
            Position = 0f;
            accumulator = 0f;
            Sweeps = 0;
        }

        /// <summary>
        /// The one place a marker position becomes an outcome, so the
        /// view can colour its bands from the same rule the strike is
        /// judged by.
        /// </summary>
        public static CemeteryStrokeOutcome Resolve(
            float position,
            CemeterySoilProfile soilProfile)
        {
            float distance = Mathf.Abs(position);
            if (distance <= soilProfile.BiteHalfWidth)
            {
                return CemeteryStrokeOutcome.Bite;
            }

            return distance <=
                   soilProfile.BiteHalfWidth +
                   soilProfile.GrazeHalfWidth
                ? CemeteryStrokeOutcome.Graze
                : CemeteryStrokeOutcome.Jar;
        }

        private void SimulateStep(float step)
        {
            float previous = phase;
            phase += profile.SwingsPerSecond *
                     Mathf.PI *
                     2f *
                     step;
            if (phase >= Mathf.PI * 2f)
            {
                phase -= Mathf.PI * 2f;
            }

            if (phase < previous)
            {
                Sweeps++;
            }

            Position = Mathf.Sin(phase);
        }

        private static float UnitFromBits(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            return (value & 0x00FFFFFFu) / 16777215f;
        }
    }
}
