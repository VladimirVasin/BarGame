using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Which of the two old men is speaking.</summary>
    public enum ParkQuarrelSpeaker
    {
        Chess = 0,
        Checkers = 1
    }

    /// <summary>
    /// The clock of the quarrel at the park chess set: one shout every
    /// ten seconds, strictly alternating, for as long as somebody is
    /// there to hear it. Pure and EditMode-testable — the mourner
    /// timeline idiom, with the turn taking the place of the phase.
    ///
    /// The opening speaker is drawn from the city seed rather than
    /// fixed, so the same approach does not always begin with the same
    /// man; after that neither of them ever gets two in a row. That is
    /// the whole joke of the pair: it is not a conversation, it is two
    /// monologues taking turns.
    /// </summary>
    public sealed class ParkQuarrelTimeline
    {
        /// <summary>
        /// The turn. One shout every ten seconds, so a full exchange —
        /// a line and the answer to it — runs twenty.
        ///
        /// The bubble is down for well over half of that: a line is read
        /// in four seconds and then the park is quiet again. That gap is
        /// the point. Two men shouting without pause read as a machine;
        /// two men who go back to brooding between rounds read as two
        /// men who have been doing this for years.
        /// </summary>
        public const float TauntIntervalSeconds = 10f;

        /// <summary>The wait before the first shout after the hero
        /// arrives. Short on purpose: the balcony pedestrian profile
        /// skips its first delay for the same reason — a scene that
        /// starts with an empty pause reads as a scene that is
        /// broken.</summary>
        public const float FirstTauntDelaySeconds = 1.2f;

        private readonly ParkQuarrelSpeaker openingSpeaker;

        private float secondsUntilNextTaunt;
        private ParkQuarrelSpeaker nextSpeaker;
        private ParkQuarrelSpeaker cueSpeaker;
        private bool cuePending;
        private int tauntCount;

        public ParkQuarrelTimeline(int citySeed)
        {
            openingSpeaker = ResolveOpeningSpeaker(citySeed);
            Reset();
        }

        /// <summary>Who shouts next.</summary>
        public ParkQuarrelSpeaker NextSpeaker => nextSpeaker;

        /// <summary>Who the seed put first, before any turn was taken.</summary>
        public ParkQuarrelSpeaker OpeningSpeaker => openingSpeaker;

        public float SecondsUntilNextTaunt => secondsUntilNextTaunt;

        /// <summary>Shouts served since the last <see cref="Reset"/>.</summary>
        public int TauntCount => tauntCount;

        /// <summary>
        /// Back to the state the hero finds on arrival: the seeded
        /// opener holds the turn again and the short first delay is
        /// re-armed. Called when he walks out of earshot.
        /// </summary>
        public void Reset()
        {
            secondsUntilNextTaunt = FirstTauntDelaySeconds;
            nextSpeaker = openingSpeaker;
            cuePending = false;
            tauntCount = 0;
        }

        public void Advance(float deltaSeconds)
        {
            if (float.IsNaN(deltaSeconds) ||
                float.IsInfinity(deltaSeconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaSeconds));
            }

            secondsUntilNextTaunt -= Mathf.Max(0f, deltaSeconds);
            if (secondsUntilNextTaunt > 0f)
            {
                return;
            }

            cueSpeaker = nextSpeaker;
            cuePending = true;
            tauntCount++;
            nextSpeaker = Opposite(nextSpeaker);

            // The overshoot of an ordinary frame is carried, so the
            // cadence does not drift a frame later every turn. A hitch
            // long enough to owe several shouts instead forfeits them:
            // a backlog fired off in one frame would be two men
            // screaming over each other, which is not the scene.
            secondsUntilNextTaunt += TauntIntervalSeconds;
            if (secondsUntilNextTaunt <= 0f)
            {
                secondsUntilNextTaunt = TauntIntervalSeconds;
            }
        }

        /// <summary>
        /// One-shot: true exactly once per shout, naming the man whose
        /// turn it was.
        /// </summary>
        public bool ConsumeTauntCue(out ParkQuarrelSpeaker speaker)
        {
            speaker = cueSpeaker;
            if (!cuePending)
            {
                return false;
            }

            cuePending = false;
            return true;
        }

        public static ParkQuarrelSpeaker Opposite(
            ParkQuarrelSpeaker speaker)
        {
            return speaker == ParkQuarrelSpeaker.Chess
                ? ParkQuarrelSpeaker.Checkers
                : ParkQuarrelSpeaker.Chess;
        }

        /// <summary>The watchman's hash idiom, on its own salt.</summary>
        public static ParkQuarrelSpeaker ResolveOpeningSpeaker(
            int citySeed)
        {
            unchecked
            {
                uint hash = ((uint)citySeed * 2654435761u) ^
                            0x51525254u; // "QRRT"
                return (hash & 1u) == 0u
                    ? ParkQuarrelSpeaker.Chess
                    : ParkQuarrelSpeaker.Checkers;
            }
        }
    }
}
