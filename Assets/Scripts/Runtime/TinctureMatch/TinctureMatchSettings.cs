using System;

namespace BarPromenade
{
    public sealed class TinctureMatchSettings
    {
        public const int DefaultRows = 7;
        public const int DefaultColumns = 7;
        public const int DefaultMoveLimit = 15;
        public const int DefaultMinimumInitialLegalSwaps = 3;

        public static TinctureMatchSettings Standard { get; } =
            new TinctureMatchSettings();

        public static TinctureMatchSettings Normal => Standard;

        public TinctureMatchSettings(
            int rows = DefaultRows,
            int columns = DefaultColumns,
            int moveLimit = DefaultMoveLimit,
            int minimumInitialLegalSwaps =
                DefaultMinimumInitialLegalSwaps,
            int generationAttemptLimit = 256,
            int reshuffleAttemptLimit = 256,
            int maximumCascadeWaves = 64)
        {
            if (rows < 3)
            {
                throw new ArgumentOutOfRangeException(nameof(rows));
            }

            if (columns < 3)
            {
                throw new ArgumentOutOfRangeException(nameof(columns));
            }

            if (moveLimit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(moveLimit));
            }

            if (minimumInitialLegalSwaps <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumInitialLegalSwaps));
            }

            if (generationAttemptLimit <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(generationAttemptLimit));
            }

            if (reshuffleAttemptLimit <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(reshuffleAttemptLimit));
            }

            if (maximumCascadeWaves <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumCascadeWaves));
            }

            Rows = rows;
            Columns = columns;
            MoveLimit = moveLimit;
            MinimumInitialLegalSwaps = minimumInitialLegalSwaps;
            GenerationAttemptLimit = generationAttemptLimit;
            ReshuffleAttemptLimit = reshuffleAttemptLimit;
            MaximumCascadeWaves = maximumCascadeWaves;
        }

        public int Rows { get; }
        public int Columns { get; }
        public int MoveLimit { get; }
        public int MinimumInitialLegalSwaps { get; }
        public int GenerationAttemptLimit { get; }
        public int ReshuffleAttemptLimit { get; }
        public int MaximumCascadeWaves { get; }
    }
}
