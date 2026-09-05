using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// When the drunk hero's stomach turns. A pure, seeded clock in the
    /// shape of <see cref="HeroMutterModel"/>: a long rest, then one bout,
    /// then a rest drawn again — shorter the drunker he is. The step is
    /// clamped so a dropped frame cannot skip a rest, and the rest is drawn
    /// when it starts so a seed replays whatever the level did meanwhile.
    ///
    /// Unlike the mutter, a closed gate REARMS the rest rather than
    /// spending it: he does not walk through a door already retching, and
    /// crossing into the last stage buys a quiet <see cref="InitialRestSeconds"/>
    /// first. The bout itself is timed by <see cref="HeroNauseaGaugeModel"/>;
    /// this clock waits for it to be armed again.
    /// </summary>
    public sealed class HeroNauseaClock
    {
        public const float MaximumStepSeconds = 0.1f;

        /// <summary>
        /// Quiet behind every door and on entering the stage. Long enough
        /// that a fixture waiting fifteen or twenty seconds at level 100
        /// for a fall never meets a bout instead.
        /// </summary>
        public const float InitialRestSeconds = 20f;

        /// <summary>
        /// The rests between bouts: a quarter of a minute at the first
        /// level of the stage, ten seconds or so at the top — where a bout
        /// itself runs six, so the gauge is up about a third of the time.
        /// The user asked for them often; these are the numbers he chose.
        /// </summary>
        public const float SlowRestMinimumSeconds = 15f;
        public const float SlowRestMaximumSeconds = 25f;
        public const float FastRestMinimumSeconds = 8f;
        public const float FastRestMaximumSeconds = 14f;

        /// <summary>The first level of the last stage: «В стельку».</summary>
        public const int FirstLevel =
            IntoxicationStageRules.MaximumLevel -
            IntoxicationStageRules.StageSize +
            1;

        private const float MinimumRestSeconds = 0.01f;

        private readonly System.Random random;
        private float restElapsed;
        private float restDuration;
        private bool boutDue;
        private bool inBout;

        public HeroNauseaClock(int seed)
        {
            random = new System.Random(seed);
            Rearm(InitialRestSeconds);
        }

        public float RestElapsed => restElapsed;
        public float RestDuration => restDuration;

        /// <summary>A bout has been cued and not yet handed back.</summary>
        public bool IsInBout => inBout;

        /// <summary>
        /// Back to waiting for <paramref name="restSeconds"/>. A pending
        /// cue is dropped: a bout that never opened is not owed.
        /// </summary>
        public void Rearm(float restSeconds = InitialRestSeconds)
        {
            inBout = false;
            restElapsed = 0f;
            restDuration = float.IsNaN(restSeconds)
                ? MinimumRestSeconds
                : Mathf.Max(MinimumRestSeconds, restSeconds);
            boutDue = false;
        }

        /// <summary>
        /// Advances the rest. <paramref name="allowed"/> false rearms the
        /// full initial rest — the gate closing is a fresh start, not a
        /// hold. While a bout is in progress the clock stands still.
        /// </summary>
        public void Advance(float deltaTime, bool allowed)
        {
            if (inBout)
            {
                return;
            }

            if (!allowed)
            {
                Rearm(InitialRestSeconds);
                return;
            }

            if (float.IsNaN(deltaTime) || deltaTime <= 0f)
            {
                return;
            }

            restElapsed += Mathf.Min(deltaTime, MaximumStepSeconds);
            if (restElapsed < restDuration)
            {
                return;
            }

            restElapsed = restDuration;
            boutDue = true;
            inBout = true;
        }

        /// <summary>
        /// True exactly once per bout, on the frame it should open. The
        /// caller runs the gauge; this class only says when.
        /// </summary>
        public bool ConsumeBoutCue()
        {
            if (!boutDue)
            {
                return false;
            }

            boutDue = false;
            return true;
        }

        /// <summary>
        /// The bout is over: draw the next rest for this pace and wait.
        /// </summary>
        public void ArmRest(float pace)
        {
            ResolveRestRange(pace, out float minimum, out float maximum);
            Rearm(Mathf.Lerp(minimum, maximum, NextUnit()));
        }

        /// <summary>The rest this pace draws, in seconds. Exposed so the
        /// cadence can be measured rather than described.</summary>
        public static void ResolveRestRange(
            float pace,
            out float minimum,
            out float maximum)
        {
            float clamped = float.IsNaN(pace) ? 0f : Mathf.Clamp01(pace);
            minimum = Mathf.Lerp(
                SlowRestMinimumSeconds,
                FastRestMinimumSeconds,
                clamped);
            maximum = Mathf.Lerp(
                SlowRestMaximumSeconds,
                FastRestMaximumSeconds,
                clamped);
        }

        /// <summary>Zero at the first level of the last stage, one at the
        /// top — the bouts' own pace, not the balance threshold's.</summary>
        public static float ResolvePace(int level)
        {
            return Mathf.InverseLerp(
                FirstLevel,
                IntoxicationStageRules.MaximumLevel,
                level);
        }

        /// <summary>Whether this level is on the stage the bouts come on.</summary>
        public static bool IsNauseaStage(int level)
        {
            return IntoxicationStageRules.GetStage(level) ==
                   IntoxicationStage.VeryDrunk;
        }

        private float NextUnit()
        {
            return (float)random.NextDouble();
        }
    }
}
