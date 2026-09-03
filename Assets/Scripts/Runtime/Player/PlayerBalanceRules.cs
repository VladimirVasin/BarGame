using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The rectangle of ground the hero's feet can push against, in the
    /// hero's own frame (<c>x</c> right, <c>y</c> forward, metres).
    /// </summary>
    public readonly struct BalanceSupportPolygon
    {
        public BalanceSupportPolygon(
            float minX,
            float maxX,
            float minForward,
            float maxForward)
        {
            MinX = Mathf.Min(minX, maxX);
            MaxX = Mathf.Max(minX, maxX);
            MinForward = Mathf.Min(minForward, maxForward);
            MaxForward = Mathf.Max(minForward, maxForward);
        }

        public float MinX { get; }
        public float MaxX { get; }
        public float MinForward { get; }
        public float MaxForward { get; }

        public float HalfWidth => (MaxX - MinX) * 0.5f;

        /// <summary>
        /// The polygon spanned by two stance feet plus the sole margins
        /// the ankles can still work with.
        /// </summary>
        public static BalanceSupportPolygon FromFeet(
            Vector2 leftFoot,
            Vector2 rightFoot,
            in PlayerBalanceSettings settings)
        {
            return new BalanceSupportPolygon(
                Mathf.Min(leftFoot.x, rightFoot.x) - settings.SupportSidePad,
                Mathf.Max(leftFoot.x, rightFoot.x) + settings.SupportSidePad,
                Mathf.Min(leftFoot.y, rightFoot.y) - settings.SupportBackPad,
                Mathf.Max(leftFoot.y, rightFoot.y) + settings.SupportFrontPad);
        }

        /// <summary>One foot only: what is left while the other steps.</summary>
        public static BalanceSupportPolygon FromFoot(
            Vector2 foot,
            in PlayerBalanceSettings settings)
        {
            return FromFeet(foot, foot, settings);
        }

        public bool Contains(Vector2 point)
        {
            return point.x >= MinX &&
                   point.x <= MaxX &&
                   point.y >= MinForward &&
                   point.y <= MaxForward;
        }

        public Vector2 Clamp(Vector2 point)
        {
            return new Vector2(
                Mathf.Clamp(point.x, MinX, MaxX),
                Mathf.Clamp(point.y, MinForward, MaxForward));
        }

        /// <summary>How far outside the polygon a point lies; zero inside.</summary>
        public float Excursion(Vector2 point)
        {
            return Vector2.Distance(point, Clamp(point));
        }

        /// <summary>The polygon grown on one side (a wall the hand holds).</summary>
        public BalanceSupportPolygon ExtendedToward(
            Vector2 direction,
            float distance)
        {
            if (distance <= 0f || direction.sqrMagnitude < 0.0001f)
            {
                return this;
            }

            Vector2 offset = direction.normalized * distance;
            return new BalanceSupportPolygon(
                Mathf.Min(MinX, MinX + offset.x),
                Mathf.Max(MaxX, MaxX + offset.x),
                Mathf.Min(MinForward, MinForward + offset.y),
                Mathf.Max(MaxForward, MaxForward + offset.y));
        }
    }

    /// <summary>
    /// Every knob of the balance model for one intoxication level. Sober
    /// is exactly zero noise, zero input coupling and zero slope bias, so
    /// the model is bit-for-bit inert until a drink lands.
    /// </summary>
    public readonly struct PlayerBalanceSettings
    {
        public const float Gravity = 9.81f;

        /// <summary>The COM stands at <c>0.54</c> of a <c>1.75 m</c> hero.</summary>
        public const float DefaultComHeight = 0.95f;

        /// <summary>
        /// Disturbance at the top stage, metres per second squared, and the
        /// power that brings it in: quasi-statically the COM settles at
        /// amplitude over omega squared, so <c>0.9</c> is a nine-centimetre
        /// lean with a step every few seconds and a fall when the noise
        /// stacks, while the level-60 threshold gets half of that.
        /// </summary>
        public const float NoiseAmplitudeAtMaximum = 0.9f;
        public const float NoiseAmplitudeExponent = 1.3f;

        public PlayerBalanceSettings(
            float intoxication,
            float comHeight,
            float noiseAmplitude,
            float noiseFrequency,
            float copStiffness,
            float copDamping,
            float reactionDelay,
            float stepDuration,
            float maximumStepReach,
            float captureMargin,
            float driftGain,
            float creepLimit,
            float runNoiseMultiplier,
            float inputCopShift,
            float slopeBias,
            float fallLeanDegrees,
            float headingWeaveDegrees,
            float headingWeaveFrequency)
        {
            Intoxication = Mathf.Clamp01(intoxication);
            ComHeight = Mathf.Max(0.3f, comHeight);
            NoiseAmplitude = Mathf.Max(0f, noiseAmplitude);
            NoiseFrequency = Mathf.Max(0.01f, noiseFrequency);
            CopStiffness = Mathf.Max(0f, copStiffness);
            CopDamping = Mathf.Max(0f, copDamping);
            ReactionDelay = Mathf.Max(0.001f, reactionDelay);
            StepDuration = Mathf.Max(0.05f, stepDuration);
            MaximumStepReach = Mathf.Max(0.05f, maximumStepReach);
            CaptureMargin = Mathf.Max(0f, captureMargin);
            DriftGain = Mathf.Max(0f, driftGain);
            CreepLimit = Mathf.Max(0f, creepLimit);
            RunNoiseMultiplier = Mathf.Max(1f, runNoiseMultiplier);
            InputCopShift = Mathf.Max(0f, inputCopShift);
            SlopeBias = Mathf.Max(0f, slopeBias);
            FallLeanDegrees = Mathf.Max(5f, fallLeanDegrees);
            HeadingWeaveDegrees = Mathf.Max(0f, headingWeaveDegrees);
            HeadingWeaveFrequency = Mathf.Max(0.01f, headingWeaveFrequency);
        }

        public float Intoxication { get; }
        public float ComHeight { get; }
        public float Omega => PlayerBalanceRules.Omega(ComHeight);

        /// <summary>Metres per second squared of low-passed disturbance.</summary>
        public float NoiseAmplitude { get; }

        /// <summary>Corner frequency of the disturbance filter, Hz.</summary>
        public float NoiseFrequency { get; }

        /// <summary>How hard the ankles pull the COM back (unused by the CoP path, kept for tuning).</summary>
        public float CopStiffness { get; }

        /// <summary>Velocity damping per second — what keeps a sober hero still.</summary>
        public float CopDamping { get; }

        /// <summary>Seconds the centre of pressure lags the capture point.</summary>
        public float ReactionDelay { get; }
        public float StepDuration { get; }
        public float MaximumStepReach { get; }

        /// <summary>Capture-point excursion tolerated before a step is planned.</summary>
        public float CaptureMargin { get; }

        /// <summary>Root creep per metre of COM offset between steps, per second.</summary>
        public float DriftGain { get; }

        /// <summary>The most creep allowed between steps, m/s.</summary>
        public float CreepLimit { get; }
        public float RunNoiseMultiplier { get; }

        /// <summary>Metres the A/D input shifts the centre of pressure.</summary>
        public float InputCopShift { get; }

        /// <summary>Downhill acceleration per unit of surface slope tangent.</summary>
        public float SlopeBias { get; }

        /// <summary>Body lean beyond which no step can save him.</summary>
        public float FallLeanDegrees { get; }

        /// <summary>Low-level weave of the walking line, degrees of heading.</summary>
        public float HeadingWeaveDegrees { get; }
        public float HeadingWeaveFrequency { get; }

        public float SupportSidePad => 0.05f;
        public float SupportBackPad => 0.06f;
        public float SupportFrontPad => 0.12f;

        /// <summary>The tuning table, one row per intoxication level.</summary>
        public static PlayerBalanceSettings FromIntoxication(
            float normalizedIntoxication)
        {
            float intoxication = Mathf.Clamp01(normalizedIntoxication);
            // The disturbance has to move the capture point past the
            // boots to be seen at all: quasi-statically the COM settles at
            // amplitude / omega^2, so a metre per second squared at the top
            // stage is a ten-centimetre lean and a step every few seconds,
            // while a third of that only ever showed as a two-degree sway.
            return new PlayerBalanceSettings(
                intoxication,
                DefaultComHeight,
                NoiseAmplitudeAtMaximum * Mathf.Pow(intoxication, NoiseAmplitudeExponent),
                Mathf.Lerp(0.4f, 1.2f, intoxication),
                Mathf.Lerp(14f, 6f, intoxication),
                Mathf.Lerp(5f, 1.6f, intoxication),
                Mathf.Lerp(0.08f, 0.26f, intoxication),
                Mathf.Lerp(0.24f, 0.36f, intoxication),
                Mathf.Lerp(0.55f, 0.38f, intoxication),
                0.03f,
                1.5f,
                0.06f,
                1.6f,
                0.05f * intoxication,
                0.15f * intoxication,
                28f,
                3f * Mathf.Clamp01(intoxication * 2.5f),
                0.3f);
        }

        public static PlayerBalanceSettings FromProfile(
            IntoxicationProfile profile)
        {
            return FromIntoxication(profile.Normalized);
        }
    }

    /// <summary>
    /// Pure formulas of the linear inverted pendulum the drunk hero is
    /// balanced as: the capture point, when it needs a step, where the
    /// step goes, when no step can help, and what a kerb does to it.
    /// </summary>
    public static class PlayerBalanceRules
    {
        /// <summary>
        /// Balance cannot be lost on a surface steeper than this: the Rise
        /// clip cannot get up on a stair, so the model staggers there but
        /// never latches a fall.
        /// </summary>
        public const float MaximumBalanceSurfaceAngle = 12f;

        /// <summary>Fixed step of the model, matching the old challenge.</summary>
        public const float FixedStep = 1f / 120f;

        /// <summary>The step lands this far beyond the capture point.</summary>
        public const float StepOvershoot = 1.25f;
        public const float StepOvershootPad = 0.06f;

        /// <summary>A sagittal step may reach a little further than a lateral one.</summary>
        public const float SagittalReachMultiplier = 1.15f;

        /// <summary>The stepping foot never crosses the other foot by less than this.</summary>
        public const float MinimumFootSeparation = 0.08f;

        public const float StepLiftBase = 0.06f;
        public const float StepLiftPerUrgency = 0.04f;

        /// <summary>Feet further apart than this get a gather step.</summary>
        public const float GatherThreshold = 0.30f;
        public const float GatherDelay = 0.10f;

        /// <summary>The capture point may sit this far past a step's reach and still be caught.</summary>
        public const float RecoverableReachFraction = 0.9f;

        /// <summary>Kerb rise from which a swinging boot trips.</summary>
        public const float TripRiseThreshold = 0.04f;
        public const float TripIntoxicationThreshold = 0.35f;
        public const float TripDuration = 0.15f;
        public const float TripReferenceRise = 0.06f;
        public const float TripAcceleration = 0.9f;

        /// <summary>Seconds the capture point may sit outside a blocked polygon before he goes.</summary>
        public const float BlockedFallSeconds = 0.45f;

        /// <summary>The wall hand extends the polygon this far toward the wall.</summary>
        public const float WallSupportReach = 0.35f;
        public const float WallCatchDistance = 0.55f;
        public const float WallCatchInstability = 0.25f;
        public const float WallAbsorb = 0.7f;
        public const float WallBounce = 0.6f;
        public const float WallPushOff = 0.15f;

        public static float Omega(float comHeight)
        {
            return Mathf.Sqrt(
                PlayerBalanceSettings.Gravity /
                Mathf.Max(0.3f, comHeight));
        }

        /// <summary>
        /// Where the centre of mass would come to rest if the centre of
        /// pressure were put under it now: <c>c + v / ω</c>.
        /// </summary>
        public static Vector2 CapturePoint(
            Vector2 comOffset,
            Vector2 comVelocity,
            float omega)
        {
            return comOffset + comVelocity / Mathf.Max(0.001f, omega);
        }

        public static bool NeedsStep(
            Vector2 capturePoint,
            in BalanceSupportPolygon support,
            float captureMargin)
        {
            return support.Excursion(capturePoint) > captureMargin;
        }

        /// <summary>
        /// Which foot steps for a capture point: the foot on the side the
        /// point escaped on, or for a purely sagittal escape the foot the
        /// caller says is freer.
        /// </summary>
        public static FootSide StepSide(
            Vector2 capturePoint,
            in BalanceSupportPolygon support,
            FootSide sagittalPreference)
        {
            float lateral = 0f;
            if (capturePoint.x > support.MaxX)
            {
                lateral = capturePoint.x - support.MaxX;
            }
            else if (capturePoint.x < support.MinX)
            {
                lateral = capturePoint.x - support.MinX;
            }

            float sagittal = 0f;
            if (capturePoint.y > support.MaxForward)
            {
                sagittal = capturePoint.y - support.MaxForward;
            }
            else if (capturePoint.y < support.MinForward)
            {
                sagittal = capturePoint.y - support.MinForward;
            }

            if (Mathf.Abs(lateral) >= Mathf.Abs(sagittal) &&
                Mathf.Abs(lateral) > 0.0001f)
            {
                return lateral > 0f ? FootSide.Right : FootSide.Left;
            }

            return sagittalPreference;
        }

        /// <summary>
        /// Where the stepping foot goes: past the capture point so the new
        /// polygon contains it, within reach, and never across the other
        /// foot.
        /// </summary>
        public static Vector2 StepTarget(
            Vector2 capturePoint,
            Vector2 otherFoot,
            FootSide steppingSide,
            in PlayerBalanceSettings settings)
        {
            Vector2 target = capturePoint * StepOvershoot;
            target.x += Mathf.Sign(capturePoint.x == 0f
                ? (steppingSide == FootSide.Right ? 1f : -1f)
                : capturePoint.x) * StepOvershootPad;
            target.x = Mathf.Clamp(
                target.x,
                -settings.MaximumStepReach,
                settings.MaximumStepReach);
            float sagittalReach =
                settings.MaximumStepReach * SagittalReachMultiplier;
            target.y = Mathf.Clamp(target.y, -sagittalReach, sagittalReach);
            if (steppingSide == FootSide.Right)
            {
                target.x = Mathf.Max(
                    target.x,
                    otherFoot.x + MinimumFootSeparation);
            }
            else
            {
                target.x = Mathf.Min(
                    target.x,
                    otherFoot.x - MinimumFootSeparation);
            }

            return target;
        }

        /// <summary>
        /// Whether a step can still bring the capture point back under
        /// the body: it must lie within the reach polygon a step can
        /// build.
        /// </summary>
        public static bool CanRecoverByStep(
            Vector2 capturePoint,
            in BalanceSupportPolygon support,
            in PlayerBalanceSettings settings)
        {
            float reach = settings.MaximumStepReach * RecoverableReachFraction;
            float lateralLimit = support.HalfWidth + reach;
            float centreX = (support.MinX + support.MaxX) * 0.5f;
            float sagittalReach = reach * SagittalReachMultiplier;
            return Mathf.Abs(capturePoint.x - centreX) <= lateralLimit &&
                   capturePoint.y <= support.MaxForward + sagittalReach &&
                   capturePoint.y >= support.MinForward - sagittalReach;
        }

        /// <summary>
        /// The reach polygon <see cref="CanRecoverByStep"/> tests against,
        /// used to pin the capture point where falls are not allowed.
        /// </summary>
        public static BalanceSupportPolygon RecoverablePolygon(
            in BalanceSupportPolygon support,
            in PlayerBalanceSettings settings)
        {
            float reach = settings.MaximumStepReach * RecoverableReachFraction;
            float sagittalReach = reach * SagittalReachMultiplier;
            return new BalanceSupportPolygon(
                support.MinX - reach,
                support.MaxX + reach,
                support.MinForward - sagittalReach,
                support.MaxForward + sagittalReach);
        }

        /// <summary>
        /// Forward acceleration a swinging boot receives from a kerb it
        /// catches: nothing sober, nearly a metre per second squared at a
        /// full <c>6 cm</c> kerb when blind drunk.
        /// </summary>
        public static float TripImpulse(float riseMetres, float intoxication)
        {
            if (riseMetres < TripRiseThreshold ||
                intoxication < TripIntoxicationThreshold)
            {
                return 0f;
            }

            return TripAcceleration *
                   (riseMetres / TripReferenceRise) *
                   Mathf.Clamp01(intoxication);
        }

        /// <summary>Body lean, degrees, for a COM offset at the settings' height.</summary>
        public static float LeanDegrees(float offset, float comHeight)
        {
            return Mathf.Atan2(offset, Mathf.Max(0.3f, comHeight)) *
                   Mathf.Rad2Deg;
        }

        /// <summary>
        /// The sign of a fall: the side the capture point favours, right
        /// on a tie because only Left and Right fall clips exist.
        /// </summary>
        public static float FallDirection(Vector2 capturePoint)
        {
            return capturePoint.x < 0f ? -1f : 1f;
        }

        /// <summary>
        /// The model's seed for one balance episode: the city seed mixed
        /// with the episode counter the session keeps, so a visit replays
        /// the same stagger and the next episode differs. The mixer is the
        /// one the retired arrow challenge used, kept so logs stay
        /// comparable.
        /// </summary>
        public static int EpisodeSeed(int citySeed, int sequence)
        {
            uint mixed = Mix(
                unchecked((uint)citySeed) ^
                unchecked((uint)sequence * 0x9E3779B9u));
            return unchecked((int)mixed);
        }

        private static uint Mix(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }
    }
}
