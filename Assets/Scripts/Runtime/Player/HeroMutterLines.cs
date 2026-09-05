namespace BarPromenade
{
    /// <summary>
    /// What the hero mutters to himself past the balance threshold, and the
    /// deterministic authored order he works through it in. Text lives in the
    /// ordinary localization catalogs; this class owns only the stable keys.
    ///
    /// TWO POOLS, ONE VOICE. The Unsteady pool is whole sentences. The Very
    /// Drunk pool is the SAME observations with the subject fallen off and the
    /// words doubling — «Ноги ещё держат.» becomes «Ещё держат.» — because that
    /// is what late drunk speech does, and because it keeps him one man rather
    /// than two. On top of the shorter pool the slur eats what is left and the
    /// letters scatter, so the incoherence at the top is PRODUCED rather than
    /// authored: every line in both pools is a legible sentence in his own
    /// register.
    ///
    /// The Very Drunk pool is held to <see
    /// cref="MaximumScatteredLineLength"/> because it is the pool that flies
    /// apart, and the per-glyph layout that scatters it does not wrap: a line
    /// plus its worst slur has to fit one row of the panel.
    /// </summary>
    public static class HeroMutterLines
    {
        public const int LinesPerStage = 10;

        /// <summary>The Unsteady pool is drawn by the ordinary wrapped label,
        /// so it may run to two rows like any other spoken line.</summary>
        public const int MaximumLineLength = 44;

        /// <summary>The Very Drunk pool scatters, and scattered text is laid
        /// out glyph by glyph on one row.</summary>
        public const int MaximumScatteredLineLength = 22;

        public static readonly string[] UnsteadyLineKeys =
        {
            "hero.mutter.unsteady.01",
            "hero.mutter.unsteady.02",
            "hero.mutter.unsteady.03",
            "hero.mutter.unsteady.04",
            "hero.mutter.unsteady.05",
            "hero.mutter.unsteady.06",
            "hero.mutter.unsteady.07",
            "hero.mutter.unsteady.08",
            "hero.mutter.unsteady.09",
            "hero.mutter.unsteady.10"
        };

        public static readonly string[] VeryDrunkLineKeys =
        {
            "hero.mutter.very_drunk.01",
            "hero.mutter.very_drunk.02",
            "hero.mutter.very_drunk.03",
            "hero.mutter.very_drunk.04",
            "hero.mutter.very_drunk.05",
            "hero.mutter.very_drunk.06",
            "hero.mutter.very_drunk.07",
            "hero.mutter.very_drunk.08",
            "hero.mutter.very_drunk.09",
            "hero.mutter.very_drunk.10"
        };

        /// <summary>He is silent below the balance threshold's stage.</summary>
        public static bool HasPool(IntoxicationStage stage)
        {
            return stage == IntoxicationStage.Unsteady ||
                   stage == IntoxicationStage.VeryDrunk;
        }

        /// <summary>Whether this stage's lines are the ones that fly apart.
        /// </summary>
        public static bool ScattersAt(IntoxicationStage stage)
        {
            return stage == IntoxicationStage.VeryDrunk;
        }

        public static int MaximumLengthFor(IntoxicationStage stage)
        {
            return ScattersAt(stage)
                ? MaximumScatteredLineLength
                : MaximumLineLength;
        }

        /// <summary>
        /// How long a slurred line of this stage may come out. A scattered
        /// line is laid out glyph by glyph on one row, so its ceiling is the
        /// tighter one.
        /// </summary>
        public static int MaximumSlurredLengthFor(IntoxicationStage stage)
        {
            return ScattersAt(stage)
                ? HeroMutterSlur.MaximumSlurredLength
                : HeroMutterSlur.MaximumWrappedSlurredLength;
        }

        public static string[] LineKeysFor(IntoxicationStage stage)
        {
            return stage == IntoxicationStage.VeryDrunk
                ? VeryDrunkLineKeys
                : UnsteadyLineKeys;
        }

        public static string KeyAt(IntoxicationStage stage, int lineIndex)
        {
            string[] keys = LineKeysFor(stage);
            if (keys.Length == 0)
            {
                throw new System.InvalidOperationException(
                    "The hero has no mutter lines for " + stage + ".");
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
    /// Cursor over the two pools. Round-robin rather than a draw, like every
    /// other repertoire in this game: a shuffle is the only thing that can say
    /// the same line twice running, and a man repeating himself word for word
    /// reads as a bug rather than as drunkenness.
    ///
    /// The two cursors are independent so that sobering back under eighty and
    /// climbing over it again does not restart the pool he was working through.
    /// </summary>
    public sealed class HeroMutterOrder
    {
        private int nextUnsteadyIndex;
        private int nextVeryDrunkIndex;

        public int NextUnsteadyIndex => nextUnsteadyIndex;
        public int NextVeryDrunkIndex => nextVeryDrunkIndex;

        public string PeekKey(IntoxicationStage stage)
        {
            return HeroMutterLines.KeyAt(stage, IndexFor(stage));
        }

        public string ConsumeKey(IntoxicationStage stage)
        {
            string key = PeekKey(stage);
            if (stage == IntoxicationStage.VeryDrunk)
            {
                nextVeryDrunkIndex = Wrap(nextVeryDrunkIndex + 1);
            }
            else
            {
                nextUnsteadyIndex = Wrap(nextUnsteadyIndex + 1);
            }

            return key;
        }

        public void Reset()
        {
            nextUnsteadyIndex = 0;
            nextVeryDrunkIndex = 0;
        }

        private int IndexFor(IntoxicationStage stage)
        {
            return stage == IntoxicationStage.VeryDrunk
                ? nextVeryDrunkIndex
                : nextUnsteadyIndex;
        }

        private static int Wrap(int index)
        {
            int count = HeroMutterLines.LinesPerStage;
            int wrapped = index % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }
    }
}
