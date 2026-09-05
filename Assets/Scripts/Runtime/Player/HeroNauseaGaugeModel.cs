using UnityEngine;

namespace BarPromenade
{
    public enum HeroNauseaOutcome
    {
        None = 0,
        Success,
        Fail
    }

    /// <summary>
    /// One bout of holding it down, as a gauge: a marker the held key
    /// lifts and gravity drops, and a safe zone that climbs from the
    /// bottom of the track to the top at its own pace. Inside the zone the
    /// strain eases; outside it builds, and full strain is the bout lost.
    /// The zone reaching the top with him still holding is the bout won.
    ///
    /// Pure and fixed-step like <see cref="CemeteryStrokeModel"/>, so a
    /// replay with the same seed and the same key history gives the same
    /// picture. The outcomes are, for now, only reported: nothing in the
    /// world happens on either yet.
    /// </summary>
    public sealed class HeroNauseaGaugeModel
    {
        public const float FixedStep = 1f / 120f;
        public const float MaximumAdvanceSeconds = 0.5f;

        /// <summary>
        /// The marker's physics, in track heights per second (squared).
        /// Softened once at the user's "чуть попроще": a lighter gravity
        /// and lower speed caps, so a released marker coasts a sixth of the
        /// track and not a quarter, and a late correction is not a plunge.
        /// </summary>
        public const float LiftAcceleration = 2.2f;
        public const float Gravity = 1.5f;
        public const float MaximumRiseSpeed = 0.7f;
        public const float MaximumFallSpeed = 0.9f;

        /// <summary>Where the zone starts and where it ends the bout.</summary>
        public const float ZoneStart = 0.12f;
        public const float ZoneEnd = 0.88f;

        /// <summary>How fast the zone climbs, track heights per second, at pace 0 and 1, and the seeded spread around that.</summary>
        public const float SlowZoneSpeed = 0.10f;
        public const float FastZoneSpeed = 0.13f;
        public const float ZoneSpeedJitter = 0.15f;

        /// <summary>Half the zone's height at pace 0 and at pace 1: a third of the track at the first level, a quarter at the top.</summary>
        public const float WideZoneHalfHeight = 0.17f;
        public const float NarrowZoneHalfHeight = 0.13f;

        /// <summary>
        /// Strain per second outside the zone, and how fast it eases inside
        /// it. Two and a half seconds out in all loses the bout; a second
        /// back inside forgives most of a slip.
        /// </summary>
        public const float StrainRate = 0.4f;
        public const float StrainRecovery = 0.45f;

        private float accumulator;

        /// <summary>The marker, 0 at the bottom of the track and 1 at the top.</summary>
        public float Marker { get; private set; }

        /// <summary>Track heights per second, positive upward.</summary>
        public float MarkerVelocity { get; private set; }

        public float ZoneCenter { get; private set; }
        public float ZoneHalfHeight { get; private set; }
        public float ZoneSpeed { get; private set; }

        /// <summary>How close he is to losing it, 0..1.</summary>
        public float Strain { get; private set; }

        public float Elapsed { get; private set; }
        public HeroNauseaOutcome Outcome { get; private set; }
        public bool IsRunning { get; private set; }

        public bool IsInside =>
            IsInsideZone(Marker, ZoneCenter, ZoneHalfHeight);

        /// <summary>
        /// Starts a bout. The marker begins inside the zone at its foot,
        /// so the first thing asked of the player is to keep up, not to
        /// catch up.
        /// </summary>
        public void Begin(float pace, int seed)
        {
            var random = new System.Random(seed);
            float clampedPace = float.IsNaN(pace) ? 0f : Mathf.Clamp01(pace);
            float jitter = Mathf.Lerp(
                -ZoneSpeedJitter,
                ZoneSpeedJitter,
                (float)random.NextDouble());
            ZoneSpeed = Mathf.Lerp(SlowZoneSpeed, FastZoneSpeed, clampedPace) *
                        (1f + jitter);
            ZoneHalfHeight = Mathf.Lerp(
                WideZoneHalfHeight,
                NarrowZoneHalfHeight,
                clampedPace);
            ZoneCenter = ZoneStart;
            Marker = ZoneStart;
            MarkerVelocity = 0f;
            Strain = 0f;
            Elapsed = 0f;
            accumulator = 0f;
            Outcome = HeroNauseaOutcome.None;
            IsRunning = true;
        }

        /// <summary>
        /// Advances the bout by <paramref name="deltaTime"/> with the key
        /// held or not. Steps at <see cref="FixedStep"/>; a hitch longer
        /// than <see cref="MaximumAdvanceSeconds"/> is cut to it, so one
        /// frozen frame cannot decide the bout.
        /// </summary>
        public void Advance(float deltaTime, bool held)
        {
            if (!IsRunning || float.IsNaN(deltaTime) || deltaTime <= 0f)
            {
                return;
            }

            accumulator += Mathf.Min(deltaTime, MaximumAdvanceSeconds);
            while (IsRunning && accumulator >= FixedStep)
            {
                accumulator -= FixedStep;
                Step(held);
            }
        }

        /// <summary>The bout is abandoned: no outcome, nothing reported.</summary>
        public void Cancel()
        {
            IsRunning = false;
            Outcome = HeroNauseaOutcome.None;
        }

        public static bool IsInsideZone(
            float marker,
            float center,
            float halfHeight)
        {
            return Mathf.Abs(marker - center) <= halfHeight;
        }

        /// <summary>How long a bout lasts when it is won, before the seeded spread.</summary>
        public static float ExpectedDurationSeconds(float pace)
        {
            float clampedPace = float.IsNaN(pace) ? 0f : Mathf.Clamp01(pace);
            return (ZoneEnd - ZoneStart) /
                   Mathf.Lerp(SlowZoneSpeed, FastZoneSpeed, clampedPace);
        }

        private void Step(bool held)
        {
            Elapsed += FixedStep;

            MarkerVelocity += (held ? LiftAcceleration : -Gravity) * FixedStep;
            MarkerVelocity = Mathf.Clamp(
                MarkerVelocity,
                -MaximumFallSpeed,
                MaximumRiseSpeed);
            Marker += MarkerVelocity * FixedStep;
            if (Marker <= 0f)
            {
                Marker = 0f;
                if (MarkerVelocity < 0f)
                {
                    MarkerVelocity = 0f;
                }
            }
            else if (Marker >= 1f)
            {
                Marker = 1f;
                if (MarkerVelocity > 0f)
                {
                    MarkerVelocity = 0f;
                }
            }

            ZoneCenter = Mathf.Min(ZoneEnd, ZoneCenter + ZoneSpeed * FixedStep);

            bool inside = IsInsideZone(Marker, ZoneCenter, ZoneHalfHeight);
            Strain = Mathf.Clamp01(
                Strain + (inside ? -StrainRecovery : StrainRate) * FixedStep);
            if (Strain >= 1f)
            {
                Resolve(HeroNauseaOutcome.Fail);
                return;
            }

            if (ZoneCenter >= ZoneEnd)
            {
                Resolve(HeroNauseaOutcome.Success);
            }
        }

        private void Resolve(HeroNauseaOutcome outcome)
        {
            Outcome = outcome;
            IsRunning = false;
        }
    }
}
