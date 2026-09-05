using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What the nausea asks of the body this frame: how far the right hand
    /// is up at the mouth, how hard the hiccup is jerking him right now,
    /// and how close he is to losing it. Exactly <see cref="None"/> costs
    /// the presentation nothing.
    /// </summary>
    public readonly struct PlayerNauseaPose
    {
        public PlayerNauseaPose(
            bool active,
            float handWeight,
            float hiccupAmount,
            float strain)
        {
            Active = active;
            HandWeight = Mathf.Clamp01(handWeight);
            HiccupAmount = Mathf.Clamp01(hiccupAmount);
            Strain = Mathf.Clamp01(strain);
        }

        public static PlayerNauseaPose None => default;

        /// <summary>A bout is in progress.</summary>
        public bool Active { get; }

        /// <summary>0 hanging, 1 palm over the mouth.</summary>
        public float HandWeight { get; }

        /// <summary>The hiccup's envelope, 0..1.</summary>
        public float HiccupAmount { get; }

        public float Strain { get; }

        public bool IsNone =>
            !Active && HandWeight <= 0f && HiccupAmount <= 0f;
    }

    /// <summary>A presentation that can draw the nausea over the body.</summary>
    public interface IPlayerNauseaPresentation
    {
        void SetNausea(in PlayerNauseaPose pose);
    }

    /// <summary>The numbers of the nausea overlay.</summary>
    public static class PlayerNauseaRules
    {
        /// <summary>The hand comes up quicker than it goes down.</summary>
        public const float HandBlendInSeconds = 0.3f;
        public const float HandBlendOutSeconds = 0.35f;

        /// <summary>The first hiccup announces the bout; the rest come at their own pace.</summary>
        public const float FirstHiccupDelaySeconds = 0.3f;
        public const float HiccupIntervalMinimumSeconds = 2.5f;
        public const float HiccupIntervalMaximumSeconds = 5f;

        /// <summary>One hiccup: a snap and a slower settle.</summary>
        public const float HiccupRiseSeconds = 0.08f;
        public const float HiccupReturnSeconds = 0.22f;

        /// <summary>The chest snaps BACK by this much at the peak (a negative forward pitch), and the chin comes up.</summary>
        public const float HiccupChestPitchDegrees = 3f;
        public const float HiccupHeadPitchDegrees = 4f;

        public static float HiccupSeconds =>
            HiccupRiseSeconds + HiccupReturnSeconds;

        /// <summary>The shape of one hiccup over its whole duration, 0..1 of the jerk.</summary>
        public static float HiccupShape(float secondsIntoHiccup)
        {
            if (float.IsNaN(secondsIntoHiccup) || secondsIntoHiccup <= 0f)
            {
                return 0f;
            }

            if (secondsIntoHiccup < HiccupRiseSeconds)
            {
                return Mathf.SmoothStep(
                    0f,
                    1f,
                    secondsIntoHiccup / HiccupRiseSeconds);
            }

            float back = (secondsIntoHiccup - HiccupRiseSeconds) /
                         HiccupReturnSeconds;
            return back >= 1f ? 0f : 1f - Mathf.SmoothStep(0f, 1f, back);
        }
    }

    /// <summary>
    /// The body's side of a bout, pure and seeded: the hand blends up to
    /// the mouth while the bout runs and back down after it, and while it
    /// runs he hiccups on seeded intervals — a hiccup already in flight
    /// finishes on its own after the bout ends. At rest with the hand
    /// down it is exactly inert.
    /// </summary>
    public sealed class PlayerNauseaModel
    {
        private readonly System.Random random;
        private float time;
        private float handWeight;
        private float strain;
        private float nextHiccupAt = float.PositiveInfinity;
        private float hiccupStartedAt = float.NegativeInfinity;
        private bool active;
        private bool hiccupDue;

        public PlayerNauseaModel(int seed)
        {
            random = new System.Random(seed);
        }

        public bool IsActive => active;
        public float HandWeight => handWeight;
        public int HiccupsTaken { get; private set; }

        public float HiccupAmount =>
            PlayerNauseaRules.HiccupShape(time - hiccupStartedAt);

        public PlayerNauseaPose Pose =>
            new PlayerNauseaPose(active, handWeight, HiccupAmount, strain);

        /// <summary>Nothing to draw and nothing pending.</summary>
        public bool IsInert =>
            !active && handWeight <= 0f && HiccupAmount <= 0f && !hiccupDue;

        /// <summary>
        /// One frame. <paramref name="boutActive"/> opens and closes the
        /// bout; <paramref name="boutStrain"/> is the gauge's own number.
        /// </summary>
        public void Advance(float deltaTime, bool boutActive, float boutStrain)
        {
            float step = float.IsNaN(deltaTime) ? 0f : Mathf.Max(0f, deltaTime);
            if (boutActive && !active)
            {
                nextHiccupAt = time + PlayerNauseaRules.FirstHiccupDelaySeconds;
            }
            else if (!boutActive && active)
            {
                nextHiccupAt = float.PositiveInfinity;
            }

            active = boutActive;
            strain = active
                ? (float.IsNaN(boutStrain) ? 0f : Mathf.Clamp01(boutStrain))
                : 0f;

            time += step;
            handWeight = Mathf.MoveTowards(
                handWeight,
                active ? 1f : 0f,
                step / (active
                    ? PlayerNauseaRules.HandBlendInSeconds
                    : PlayerNauseaRules.HandBlendOutSeconds));

            if (active && time >= nextHiccupAt)
            {
                hiccupStartedAt = time;
                hiccupDue = true;
                HiccupsTaken++;
                nextHiccupAt = time + NextInterval();
            }
        }

        /// <summary>True exactly once per hiccup, on the frame it starts —
        /// the sound goes here.</summary>
        public bool ConsumeHiccupCue()
        {
            if (!hiccupDue)
            {
                return false;
            }

            hiccupDue = false;
            return true;
        }

        /// <summary>Everything down at once, for a shutdown.</summary>
        public void Reset()
        {
            active = false;
            handWeight = 0f;
            strain = 0f;
            hiccupDue = false;
            hiccupStartedAt = float.NegativeInfinity;
            nextHiccupAt = float.PositiveInfinity;
        }

        private float NextInterval()
        {
            return Mathf.Lerp(
                PlayerNauseaRules.HiccupIntervalMinimumSeconds,
                PlayerNauseaRules.HiccupIntervalMaximumSeconds,
                (float)random.NextDouble());
        }
    }
}
