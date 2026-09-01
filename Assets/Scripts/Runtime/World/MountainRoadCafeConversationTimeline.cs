using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Which member of the cafe pair owns the current line.</summary>
    public enum MountainRoadCafeConversationSpeaker
    {
        PairMan = 0,
        PairWoman = 1
    }

    /// <summary>
    /// Pure turn clock for the two patrons at the Mountain Road cafe. The
    /// man always opens, then the pair alternate without exception so the ten
    /// authored statement/response pairs retain their order. A hitch produces
    /// one cue, never a backlog of overlapping lines.
    /// </summary>
    public sealed class MountainRoadCafeConversationTimeline
    {
        public const float LineIntervalSeconds = 8f;
        public const float FirstLineDelaySeconds = 1.2f;

        private float secondsUntilNextLine;
        private MountainRoadCafeConversationSpeaker nextSpeaker;
        private MountainRoadCafeConversationSpeaker cueSpeaker;
        private bool cuePending;
        private int lineCount;

        public MountainRoadCafeConversationTimeline(int seed)
        {
            // Kept in the signature beside the other cafe clocks. The
            // conversation itself is authored response order, not a shuffle.
            _ = seed;
            Reset();
        }

        public MountainRoadCafeConversationSpeaker OpeningSpeaker =>
            MountainRoadCafeConversationSpeaker.PairMan;
        public MountainRoadCafeConversationSpeaker NextSpeaker =>
            nextSpeaker;
        public float SecondsUntilNextLine => secondsUntilNextLine;
        public int LineCount => lineCount;

        public void Reset()
        {
            secondsUntilNextLine = FirstLineDelaySeconds;
            nextSpeaker = MountainRoadCafeConversationSpeaker.PairMan;
            cuePending = false;
            lineCount = 0;
        }

        public void Advance(float deltaSeconds)
        {
            if (float.IsNaN(deltaSeconds) ||
                float.IsInfinity(deltaSeconds) ||
                deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaSeconds));
            }

            // Once a due turn is latched, time cannot replace it with the
            // following speaker. The caller may keep advancing through a
            // long Drink window and still consume this exact cue afterward.
            if (cuePending)
            {
                return;
            }

            secondsUntilNextLine -= deltaSeconds;
            if (secondsUntilNextLine > 0f)
            {
                return;
            }

            cueSpeaker = nextSpeaker;
            cuePending = true;
            lineCount++;
            nextSpeaker = Opposite(nextSpeaker);
            secondsUntilNextLine += LineIntervalSeconds;
            if (secondsUntilNextLine <= 0f)
            {
                secondsUntilNextLine = LineIntervalSeconds;
            }
        }

        public bool ConsumeLineCue(
            out MountainRoadCafeConversationSpeaker speaker)
        {
            speaker = cueSpeaker;
            if (!cuePending)
            {
                return false;
            }

            cuePending = false;
            return true;
        }

        public static MountainRoadCafeConversationSpeaker Opposite(
            MountainRoadCafeConversationSpeaker speaker)
        {
            return speaker == MountainRoadCafeConversationSpeaker.PairMan
                ? MountainRoadCafeConversationSpeaker.PairWoman
                : MountainRoadCafeConversationSpeaker.PairMan;
        }

    }
}
