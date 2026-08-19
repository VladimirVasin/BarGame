using UnityEngine;

namespace BarPromenade
{
    /// <summary>Which half of setting the stone is running.</summary>
    public enum CemeteryStonePhase
    {
        /// <summary>Heaving it up off the ground onto its foot.
        /// </summary>
        Raising = 0,

        /// <summary>Driving it home with three blows from above.
        /// </summary>
        Setting = 1,

        /// <summary>Standing and fast.</summary>
        Done = 2
    }

    /// <summary>
    /// The numbers behind standing a monument up.
    /// </summary>
    public readonly struct CemeteryStoneSettleSettings
    {
        private CemeteryStoneSettleSettings(
            float pressLift,
            float sagRate,
            int strikesRequired,
            float biteHalfWidth,
            float grazeHalfWidth,
            float swingsPerSecond)
        {
            PressLift = pressLift;
            SagRate = sagRate;
            StrikesRequired = strikesRequired;
            BiteHalfWidth = biteHalfWidth;
            GrazeHalfWidth = grazeHalfWidth;
            SwingsPerSecond = swingsPerSecond;
        }

        /// <summary>How much of the lift one heave is worth.
        /// </summary>
        public float PressLift { get; }

        /// <summary>
        /// How fast it settles back when nobody is heaving. Gentle on
        /// purpose: this half is meant to be a steady effort against a
        /// weight, not a race — let go entirely and you lose ground,
        /// keep at it at all and you gain.
        /// </summary>
        public float SagRate { get; }

        /// <summary>Blows it takes to drive home once it is up.
        /// </summary>
        public int StrikesRequired { get; }

        /// <summary>
        /// The swing the blows are timed on. Generous: this is the
        /// last thing the hero does for the wage and it should land,
        /// not tease.
        /// </summary>
        public float BiteHalfWidth { get; }
        public float GrazeHalfWidth { get; }
        public float SwingsPerSecond { get; }

        public static CemeteryStoneSettleSettings Default =>
            new CemeteryStoneSettleSettings(
                0.075f,
                0.10f,
                3,
                0.30f,
                0.20f,
                0.55f);

        /// <summary>The tamping swing, as the stroke model wants it.
        /// </summary>
        public CemeterySwingProfile TampProfile =>
            new CemeterySwingProfile(
                BiteHalfWidth,
                GrazeHalfWidth,
                SwingsPerSecond);
    }

    /// <summary>
    /// Act four: the stone comes up off the grass and is driven home.
    ///
    /// It is two different efforts and they are deliberately not the
    /// same input. Getting a monument off the ground and onto its foot
    /// is dead weight and nothing else — no timing, no window, just
    /// heaving at it until it is up, and it sags back if the hero
    /// stops. Once it stands, setting it is three deliberate blows from
    /// above, and those are timed, because a blow is a thing you aim
    /// and a heave is not.
    ///
    /// Nothing here can be failed. It is the last thing between the
    /// hero and his wage, and a grave he has already dug, filled and
    /// closed should not be taken off him by a mistimed swing.
    /// </summary>
    public sealed class CemeteryStoneSettleModel
    {
        public const float FixedStep = 1f / 120f;

        private readonly CemeteryStoneSettleSettings settings;
        private float accumulator;

        public CemeteryStoneSettleModel(
            CemeteryStoneSettleSettings settleSettings)
        {
            settings = settleSettings;
        }

        public CemeteryStoneSettleSettings Settings => settings;

        public CemeteryStonePhase Phase { get; private set; } =
            CemeteryStonePhase.Raising;

        /// <summary>How far up it is, from flat on the grass to
        /// standing on its foot.</summary>
        public float Lift01 { get; private set; }

        /// <summary>Blows landed since it came up.</summary>
        public int StrikesLanded { get; private set; }

        /// <summary>How far it has been driven into the ground, from
        /// the three blows.</summary>
        public float Set01 =>
            settings.StrikesRequired <= 0
                ? 1f
                : StrikesLanded / (float)settings.StrikesRequired;

        public bool IsComplete => Phase == CemeteryStonePhase.Done;

        /// <summary>
        /// One heave. Only does anything while the stone is still on
        /// its way up.
        /// </summary>
        public void Press()
        {
            if (Phase != CemeteryStonePhase.Raising)
            {
                return;
            }

            Lift01 = Mathf.Clamp01(Lift01 + settings.PressLift);
            if (Lift01 >= 1f)
            {
                Phase = CemeteryStonePhase.Setting;
            }
        }

        /// <summary>
        /// Lands one blow. Only a clean strike counts, and a missed one
        /// costs nothing but the swing — three blows means three, not
        /// three in a row.
        /// </summary>
        public void Strike(CemeteryStrokeOutcome outcome)
        {
            if (Phase != CemeteryStonePhase.Setting ||
                outcome != CemeteryStrokeOutcome.Bite)
            {
                return;
            }

            StrikesLanded++;
            if (StrikesLanded >= settings.StrikesRequired)
            {
                Phase = CemeteryStonePhase.Done;
            }
        }

        /// <summary>
        /// Lets the weight settle back while nobody is under it.
        /// </summary>
        public void Advance(float deltaTime)
        {
            if (Phase != CemeteryStonePhase.Raising)
            {
                return;
            }

            accumulator += Mathf.Clamp(deltaTime, 0f, 0.5f);
            while (accumulator + 0.000001f >= FixedStep &&
                   Phase == CemeteryStonePhase.Raising)
            {
                Lift01 = Mathf.Max(
                    0f,
                    Lift01 - (settings.SagRate * FixedStep));
                accumulator -= FixedStep;
            }
        }
    }
}
