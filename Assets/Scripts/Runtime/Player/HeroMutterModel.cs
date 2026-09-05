using UnityEngine;

namespace BarPromenade
{
    public enum HeroMutterPhase
    {
        Rest = 0,
        Speaking
    }

    /// <summary>
    /// When the drunk hero says something. A pure, seeded clock: long silences
    /// with one short line in them, the silences shortening as he gets drunker.
    /// The same shape as <see cref="IntoxicationVertigoModel"/> and the dolly
    /// zoom — the step is clamped so one dropped frame cannot skip a whole
    /// silence, the remainder carries across a phase boundary, and the rest is
    /// drawn when the phase starts so a seed replays whatever the level did in
    /// between.
    ///
    /// The cadence is deliberately slow. A line every ten seconds is a HUD
    /// element; the city has no interest in him (§16.6) and neither should the
    /// frame. At the balance threshold he speaks about every forty seconds, at
    /// the top about every seventeen.
    ///
    /// One line at a time, and never queued: <see cref="Speaking"/> lasts
    /// exactly as long as the bubble does, and a second line arriving inside it
    /// would truncate the first mid-word.
    /// </summary>
    public sealed class HeroMutterModel
    {
        public const float MaximumStepSeconds = 0.1f;

        /// <summary>Silence behind every door: he does not open his mouth on
        /// the first second of a scene.</summary>
        public const float InitialRestSeconds = 12f;

        /// <summary>The bubble's own life, not a second opinion about it.
        /// </summary>
        public const float SpeakingSeconds =
            NpcSpeechBubbleView.VisibleSeconds;

        public const float SlowRestMinimumSeconds = 26f;
        public const float SlowRestMaximumSeconds = 48f;
        public const float FastRestMinimumSeconds = 9f;
        public const float FastRestMaximumSeconds = 18f;

        private const float MinimumPhaseSeconds = 0.01f;

        private readonly System.Random random;
        private HeroMutterPhase phase;
        private float phaseElapsed;
        private float phaseDuration;
        private bool lineDue;

        public HeroMutterModel(int seed)
        {
            random = new System.Random(seed);
            Reset(InitialRestSeconds);
        }

        public HeroMutterPhase Phase => phase;
        public float PhaseElapsed => phaseElapsed;
        public float PhaseDuration => phaseDuration;

        /// <summary>Whether a line is open right now.</summary>
        public bool IsSpeaking => phase == HeroMutterPhase.Speaking;

        /// <summary>
        /// Back to silence, holding for <paramref name="restSeconds"/> before
        /// the next line may come. The random stream is kept, and a pending cue
        /// is dropped: a line that was never shown is not owed to anybody.
        /// </summary>
        public void Reset(float restSeconds = InitialRestSeconds)
        {
            phase = HeroMutterPhase.Rest;
            phaseElapsed = 0f;
            phaseDuration = float.IsNaN(restSeconds)
                ? 0f
                : Mathf.Max(0f, restSeconds);
            lineDue = false;
        }

        /// <summary>
        /// Advances the clock. <paramref name="allowed"/> false holds the
        /// silence with its hold spent, so the frame the gate opens is the
        /// frame he may speak on rather than the start of a fresh wait;
        /// <paramref name="pace"/> is 0 at the balance threshold and 1 at the
        /// top level and sets how short the silences are drawn.
        /// </summary>
        public void Advance(float deltaTime, bool allowed, float pace)
        {
            if (float.IsNaN(deltaTime) || deltaTime <= 0f)
            {
                return;
            }

            float clampedPace = float.IsNaN(pace)
                ? 0f
                : Mathf.Clamp01(pace);
            float remaining = Mathf.Min(deltaTime, MaximumStepSeconds);
            while (remaining > 0f)
            {
                float room = phaseDuration - phaseElapsed;
                if (remaining < room)
                {
                    phaseElapsed += remaining;
                    break;
                }

                remaining -= Mathf.Max(0f, room);
                phaseElapsed = phaseDuration;
                if (!CompletePhase(allowed, clampedPace))
                {
                    break;
                }
            }
        }

        /// <summary>
        /// True exactly once per line, on the frame it should open. The caller
        /// draws the text; this class only says when.
        /// </summary>
        public bool ConsumeLineCue()
        {
            if (!lineDue)
            {
                return false;
            }

            lineDue = false;
            return true;
        }

        /// <summary>The silence this pace draws, in seconds. Exposed so the
        /// cadence can be measured rather than described.</summary>
        public static void ResolveRestRange(
            float pace,
            out float minimum,
            out float maximum)
        {
            float clamped = Mathf.Clamp01(pace);
            minimum = Mathf.Lerp(
                SlowRestMinimumSeconds,
                FastRestMinimumSeconds,
                clamped);
            maximum = Mathf.Lerp(
                SlowRestMaximumSeconds,
                FastRestMaximumSeconds,
                clamped);
        }

        private bool CompletePhase(bool allowed, float pace)
        {
            if (phase == HeroMutterPhase.Speaking)
            {
                ResolveRestRange(pace, out float minimum, out float maximum);
                EnterPhase(
                    HeroMutterPhase.Rest,
                    Mathf.Lerp(minimum, maximum, NextUnit()));
                return true;
            }

            if (!allowed)
            {
                // The hold is spent and stays spent: he is not owed a fresh
                // wait for having been on a stool while the clock ran out.
                return false;
            }

            lineDue = true;
            EnterPhase(HeroMutterPhase.Speaking, SpeakingSeconds);
            return true;
        }

        private void EnterPhase(HeroMutterPhase nextPhase, float seconds)
        {
            phase = nextPhase;
            phaseElapsed = 0f;
            phaseDuration = Mathf.Max(MinimumPhaseSeconds, seconds);
        }

        private float NextUnit()
        {
            return (float)random.NextDouble();
        }
    }
}
