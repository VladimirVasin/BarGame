namespace BarPromenade
{
    /// <summary>
    /// The two localized cafe repertoires and their deterministic fixed
    /// authored order. Text lives in the ordinary localization catalogs;
    /// this class owns only the stable keys and their ten response pairs.
    /// </summary>
    public static class MountainRoadCafeConversationLines
    {
        public const int LinesPerSpeaker = 10;
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
