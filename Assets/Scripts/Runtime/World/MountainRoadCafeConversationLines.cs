namespace BarPromenade
{
    /// <summary>
    /// The localized cafe repertoires and their deterministic fixed authored
    /// order. Text lives in the ordinary localization catalogs; this class
    /// owns only the stable keys, the ten response pairs and the sleeping
    /// husband's short interruption pool.
    /// </summary>
    public static class MountainRoadCafeConversationLines
    {
        public const int LinesPerSpeaker = 10;
        public const int LonePatronLineCount = 4;
        public const int MaximumLineLength = 48;

        public static readonly string[] PairManLineKeys =
        {
            "mountain.cafe.pair.man.line.01",
            "mountain.cafe.pair.man.line.02",
            "mountain.cafe.pair.man.line.03",
            "mountain.cafe.pair.man.line.04",
            "mountain.cafe.pair.man.line.05",
            "mountain.cafe.pair.man.line.06",
            "mountain.cafe.pair.man.line.07",
            "mountain.cafe.pair.man.line.08",
            "mountain.cafe.pair.man.line.09",
            "mountain.cafe.pair.man.line.10"
        };

        public static readonly string[] PairWomanLineKeys =
        {
            "mountain.cafe.pair.woman.line.01",
            "mountain.cafe.pair.woman.line.02",
            "mountain.cafe.pair.woman.line.03",
            "mountain.cafe.pair.woman.line.04",
            "mountain.cafe.pair.woman.line.05",
            "mountain.cafe.pair.woman.line.06",
            "mountain.cafe.pair.woman.line.07",
            "mountain.cafe.pair.woman.line.08",
            "mountain.cafe.pair.woman.line.09",
            "mountain.cafe.pair.woman.line.10"
        };

        public static readonly string[] LonePatronLineKeys =
        {
            "mountain.cafe.lone.line.01",
            "mountain.cafe.lone.line.02",
            "mountain.cafe.lone.line.03",
            "mountain.cafe.lone.line.04"
        };

        public static string[] LineKeysFor(
            MountainRoadCafeConversationSpeaker speaker)
        {
            return speaker == MountainRoadCafeConversationSpeaker.PairMan
                ? PairManLineKeys
                : PairWomanLineKeys;
        }

        public static string KeyAt(
            MountainRoadCafeConversationSpeaker speaker,
            int lineIndex)
        {
            string[] keys = LineKeysFor(speaker);
            if (keys.Length == 0)
            {
                throw new System.InvalidOperationException(
                    "The mountain cafe conversation has no lines.");
            }

            int wrapped = lineIndex % keys.Length;
            if (wrapped < 0)
            {
                wrapped += keys.Length;
            }

            return keys[wrapped];
        }
    }

    /// <summary>
    /// Counts fully displayed Man/Woman exchanges. Every third completed
    /// exchange admits one lone-patron interruption and advances its own
    /// four-line pool. Blocked or rolled-back bubbles never reach this clock.
    /// </summary>
    public sealed class MountainRoadCafeLonePatronInterjectionSchedule
    {
        public const int PairExchangesPerInterjection = 3;

        private int completedPairExchanges;
        private int nextLineIndex;
        private bool hasCompletedManLine;

        public int CompletedPairExchanges => completedPairExchanges;
        public int NextLineIndex => nextLineIndex;

        public bool RecordCompletedLine(
            MountainRoadCafeConversationSpeaker speaker)
        {
            if (speaker == MountainRoadCafeConversationSpeaker.PairMan)
            {
                hasCompletedManLine = true;
                return false;
            }

            if (!hasCompletedManLine)
            {
                return false;
            }

            hasCompletedManLine = false;
            completedPairExchanges++;
            return completedPairExchanges %
                   PairExchangesPerInterjection == 0;
        }

        public string ConsumeLonePatronLineKey()
        {
            string[] keys = MountainRoadCafeConversationLines
                .LonePatronLineKeys;
            if (keys.Length == 0)
            {
                throw new System.InvalidOperationException(
                    "The mountain cafe lone patron has no lines.");
            }

            string key = keys[nextLineIndex];
            nextLineIndex = (nextLineIndex + 1) % keys.Length;
            return key;
        }

        public void Reset()
        {
            completedPairExchanges = 0;
            nextLineIndex = 0;
            hasCompletedManLine = false;
        }
    }

    /// <summary>
    /// Cursor over the ten authored response pairs. Peeking a blocked cue is
    /// side-effect free; only the moment a line actually appears consumes it.
    /// </summary>
    public sealed class MountainRoadCafeConversationOrder
    {
        private int nextManLineIndex;
        private int nextWomanLineIndex;

        public int NextManLineIndex => nextManLineIndex;
        public int NextWomanLineIndex => nextWomanLineIndex;

        public MountainRoadCafeConversationOrder()
        {
            Reset();
        }

        public string PeekKey(
            MountainRoadCafeConversationSpeaker speaker)
        {
            return MountainRoadCafeConversationLines.KeyAt(
                speaker,
                IndexFor(speaker));
        }

        public string ConsumeKey(
            MountainRoadCafeConversationSpeaker speaker)
        {
            string key = PeekKey(speaker);
            if (speaker == MountainRoadCafeConversationSpeaker.PairMan)
            {
                nextManLineIndex = Wrap(nextManLineIndex + 1);
            }
            else
            {
                nextWomanLineIndex = Wrap(nextWomanLineIndex + 1);
            }

            return key;
        }

        public void UndoLast(
            MountainRoadCafeConversationSpeaker speaker)
        {
            if (speaker == MountainRoadCafeConversationSpeaker.PairMan)
            {
                nextManLineIndex = Wrap(nextManLineIndex - 1);
            }
            else
            {
                nextWomanLineIndex = Wrap(nextWomanLineIndex - 1);
            }
        }

        public void Reset()
        {
            nextManLineIndex = 0;
            nextWomanLineIndex = 0;
        }

        private int IndexFor(
            MountainRoadCafeConversationSpeaker speaker)
        {
            return speaker == MountainRoadCafeConversationSpeaker.PairMan
                ? nextManLineIndex
                : nextWomanLineIndex;
        }

        private static int Wrap(int index)
        {
            int count = MountainRoadCafeConversationLines.LinesPerSpeaker;
            int wrapped = index % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }
    }
}
