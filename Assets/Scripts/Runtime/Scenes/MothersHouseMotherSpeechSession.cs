using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// When his mother speaks next, and which line she has left.
    ///
    /// The Ferryman's road-speech shape with the journey taken out: a
    /// shuffled bag so nothing repeats before the pool is spent, a
    /// silence that only visible unpaused time consumes, and a hold
    /// while a line is on screen so typing and reading never eat the
    /// next one.
    ///
    /// It carries NO per-visit cap. A ride ends on its own and five
    /// lines fill it; a room does not, and a mother who fell silent
    /// for good after five would be a bug the player would read as
    /// her having nothing left to say.
    /// </summary>
    public sealed class MothersHouseMotherSpeechState
    {
        public const float FirstMinimumSeconds = 14f;
        public const float FirstMaximumSeconds = 22f;
        public const float SilenceMinimumSeconds = 35f;
        public const float SilenceMaximumSeconds = 55f;

        private readonly int[] bag;
        private int cursor;
        private int lastLine = -1;
        private uint random;

        public MothersHouseMotherSpeechState(uint seed)
        {
            random = seed == 0u ? 0x68E31DA4u : seed;
            bag = new int[MothersHouseMotherQuips.LineKeys.Length];
            // Starts exhausted, so the first draw shuffles rather than
            // handing out index zero.
            cursor = bag.Length;
            SilenceRemaining = Range(
                FirstMinimumSeconds,
                FirstMaximumSeconds);
        }

        /// <summary>A line is on screen. Nothing is drawn and no
        /// silence is spent until it comes down.</summary>
        public bool IsLineActive { get; private set; }

        public float SilenceRemaining { get; private set; }

        public int LastLineIndex => lastLine;

        /// <summary>
        /// Spends visible time and returns the line to say, or `-1`
        /// for nothing yet. A zero or nonsense delta is a no-op with
        /// no backlog: a paused frame must not owe her a line.
        /// </summary>
        public int AdvanceSilence(float seconds, bool scarfWorn)
        {
            if (IsLineActive ||
                seconds <= 0f ||
                float.IsNaN(seconds) ||
                float.IsInfinity(seconds))
            {
                return -1;
            }

            SilenceRemaining = Mathf.Max(
                0f,
                SilenceRemaining - seconds);
            if (SilenceRemaining > 0f)
            {
                return -1;
            }

            int line = Draw(scarfWorn);
            if (line < 0)
            {
                return -1;
            }

            lastLine = line;
            IsLineActive = true;
            return line;
        }

        /// <summary>The line came down. Idempotent: a second call does
        /// not re-roll a silence already rolled.</summary>
        public void FinishLine()
        {
            if (!IsLineActive)
            {
                return;
            }

            IsLineActive = false;
            SilenceRemaining = Range(
                SilenceMinimumSeconds,
                SilenceMaximumSeconds);
        }

        /// <summary>
        /// Takes a line for the other channel — the answer to `E`.
        /// It comes out of the same bag, so talking to her also uses
        /// up what she would otherwise have said on her own.
        /// </summary>
        public int TakeSpokenLine(bool scarfWorn)
        {
            int line = Draw(scarfWorn);
            if (line >= 0)
            {
                lastLine = line;
            }

            return line;
        }

        /// <summary>
        /// The next live index, refilling the bag when it runs out.
        /// A retired line is skipped rather than returned, which is
        /// why this can walk more than one entry.
        /// </summary>
        private int Draw(bool scarfWorn)
        {
            for (int attempt = 0; attempt <= bag.Length; attempt++)
            {
                if (cursor >= bag.Length)
                {
                    Shuffle();
                }

                int candidate = bag[cursor++];
                if (MothersHouseMotherQuips.IsLineLive(
                        candidate,
                        scarfWorn))
                {
                    return candidate;
                }
            }

            return -1;
        }

        private void Shuffle()
        {
            for (int index = 0; index < bag.Length; index++)
            {
                bag[index] = index;
            }

            for (int index = bag.Length - 1; index > 0; index--)
            {
                int other = (int)(Next() % (uint)(index + 1));
                int swap = bag[index];
                bag[index] = bag[other];
                bag[other] = swap;
            }

            // A fresh bag must not open on the line the last one closed
            // with, or the seam is the one place she repeats herself.
            if (bag[0] == lastLine && bag.Length > 1)
            {
                int swap = bag[0];
                bag[0] = bag[1];
                bag[1] = swap;
            }

            cursor = 0;
        }

        private float Range(float minimum, float maximum)
        {
            return minimum +
                   (Next() & 0x00FFFFFFu) / 16777215f *
                   (maximum - minimum);
        }

        private uint Next()
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            return random;
        }
    }

    /// <summary>
    /// Her bag and her silence, held across visits.
    ///
    /// The house is reloaded every time he walks through the door, and
    /// a state rebuilt with it would start the bag again on each
    /// entry: she would say the same two or three lines forever and
    /// the other eight would be dead weight. The Ferryman keeps his
    /// across the tunnel for the same reason.
    /// </summary>
    public static class MothersHouseMotherSpeechSession
    {
        public static MothersHouseMotherSpeechState State
        {
            get;
            private set;
        } = new MothersHouseMotherSpeechState(
            unchecked((uint)System.Environment.TickCount));

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetSession()
        {
            State = new MothersHouseMotherSpeechState(
                unchecked((uint)System.Environment.TickCount));
        }
    }
}
