using System;

namespace BarPromenade
{
    public static class TinctureMatchGenerator
    {
        private static readonly TinctureTileKind[] NormalKinds =
        {
            TinctureTileKind.Cherry,
            TinctureTileKind.SeaBuckthorn,
            TinctureTileKind.Blueberry,
            TinctureTileKind.Mint,
            TinctureTileKind.Horseradish
        };

        private static readonly int[] StandardFallbackPattern =
        {
            4, 0, 1, 2, 4, 3, 0,
            0, 2, 4, 1, 4, 2, 2,
            3, 3, 0, 4, 3, 4, 0,
            1, 3, 4, 2, 2, 1, 0,
            0, 4, 4, 0, 0, 4, 3,
            2, 0, 2, 4, 2, 1, 3,
            2, 4, 1, 2, 2, 4, 1
        };

        public static TinctureMatchBoard Generate(
            int seed,
            TinctureMatchSettings settings = null)
        {
            TinctureMatchSettings resolved =
                settings ?? TinctureMatchSettings.Standard;
            var random = new TinctureMatchRandom(seed);
            return Generate(
                ref random,
                resolved,
                1,
                resolved.MinimumInitialLegalSwaps);
        }

        public static TinctureMatchBoard Reshuffle(
            TinctureMatchBoard board,
            int seed,
            TinctureMatchSettings settings = null)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            TinctureMatchSettings resolved =
                settings ?? TinctureMatchSettings.Standard;
            ValidateDimensions(board, resolved);
            ValidatePlayableTiles(board);
            var random = new TinctureMatchRandom(seed);
            return Reshuffle(ref random, board, resolved);
        }

        internal static TinctureMatchBoard Generate(
            ref TinctureMatchRandom random,
            TinctureMatchSettings settings,
            int moonshineCount,
            int minimumLegalSwaps)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (moonshineCount < 0 || moonshineCount > 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(moonshineCount));
            }

            if (minimumLegalSwaps < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumLegalSwaps));
            }

            int count = settings.Rows * settings.Columns;
            for (int attempt = 0;
                 attempt < settings.GenerationAttemptLimit;
                 attempt++)
            {
                TinctureTileKind[] tiles = FillWithoutMatches(
                    ref random,
                    settings.Rows,
                    settings.Columns);
                if (moonshineCount == 1)
                {
                    tiles[random.NextInt(count)] =
                        TinctureTileKind.Moonshine;
                }

                var candidate = new TinctureMatchBoard(
                    settings.Rows,
                    settings.Columns,
                    tiles);
                if (!TinctureMatchResolver
                        .FindMatches(candidate)
                        .HasMatches &&
                    TinctureMatchResolver.CountLegalNormalSwaps(
                        candidate) >= minimumLegalSwaps)
                {
                    return candidate;
                }
            }

            return CreateFallback(
                ref random,
                settings,
                moonshineCount,
                minimumLegalSwaps);
        }

        internal static TinctureMatchBoard Reshuffle(
            ref TinctureMatchRandom random,
            TinctureMatchBoard board,
            TinctureMatchSettings settings)
        {
            ValidateDimensions(board, settings);
            ValidatePlayableTiles(board);
            TinctureTileKind[] original = board.ToArray();
            for (int attempt = 0;
                 attempt < settings.ReshuffleAttemptLimit;
                 attempt++)
            {
                var shuffled =
                    (TinctureTileKind[])original.Clone();
                Shuffle(shuffled, ref random);
                var candidate = new TinctureMatchBoard(
                    settings.Rows,
                    settings.Columns,
                    shuffled);
                if (!TinctureMatchResolver
                        .FindMatches(candidate)
                        .HasMatches &&
                    TinctureMatchResolver.CountLegalNormalSwaps(
                        candidate) >=
                    settings.MinimumInitialLegalSwaps)
                {
                    return candidate;
                }
            }

            int moonshineCount =
                board.CountTiles(TinctureTileKind.Moonshine);
            return Generate(
                ref random,
                settings,
                moonshineCount,
                settings.MinimumInitialLegalSwaps);
        }

        private static TinctureTileKind[] FillWithoutMatches(
            ref TinctureMatchRandom random,
            int rows,
            int columns)
        {
            var tiles = new TinctureTileKind[rows * columns];
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0;
                     column < columns;
                     column++)
                {
                    int start = random.NextInt(NormalKinds.Length);
                    bool assigned = false;
                    for (int offset = 0;
                         offset < NormalKinds.Length;
                         offset++)
                    {
                        TinctureTileKind candidate =
                            NormalKinds[
                                (start + offset) %
                                NormalKinds.Length];
                        if (WouldCreateImmediateRun(
                                tiles,
                                rows,
                                columns,
                                row,
                                column,
                                candidate))
                        {
                            continue;
                        }

                        tiles[row * columns + column] =
                            candidate;
                        assigned = true;
                        break;
                    }

                    if (!assigned)
                    {
                        throw new InvalidOperationException(
                            "No normal tincture can fill this cell.");
                    }
                }
            }

            return tiles;
        }

        private static bool WouldCreateImmediateRun(
            TinctureTileKind[] tiles,
            int rows,
            int columns,
            int row,
            int column,
            TinctureTileKind candidate)
        {
            _ = rows;
            bool horizontal =
                column >= 2 &&
                tiles[row * columns + column - 1] == candidate &&
                tiles[row * columns + column - 2] == candidate;
            bool vertical =
                row >= 2 &&
                tiles[(row - 1) * columns + column] ==
                candidate &&
                tiles[(row - 2) * columns + column] ==
                candidate;
            return horizontal || vertical;
        }

        private static TinctureMatchBoard CreateFallback(
            ref TinctureMatchRandom random,
            TinctureMatchSettings settings,
            int moonshineCount,
            int minimumLegalSwaps)
        {
            if (settings.Rows !=
                    TinctureMatchSettings.DefaultRows ||
                settings.Columns !=
                    TinctureMatchSettings.DefaultColumns)
            {
                throw new InvalidOperationException(
                    "Unable to generate a playable custom-size board.");
            }

            var permutation =
                (TinctureTileKind[])NormalKinds.Clone();
            Shuffle(permutation, ref random);
            var tiles =
                new TinctureTileKind[StandardFallbackPattern.Length];
            for (int index = 0; index < tiles.Length; index++)
            {
                tiles[index] =
                    permutation[StandardFallbackPattern[index]];
            }

            if (moonshineCount == 1)
            {
                int start = random.NextInt(tiles.Length);
                for (int offset = 0;
                     offset < tiles.Length;
                     offset++)
                {
                    int index = (start + offset) % tiles.Length;
                    TinctureTileKind replaced = tiles[index];
                    tiles[index] = TinctureTileKind.Moonshine;
                    var candidate = new TinctureMatchBoard(
                        settings.Rows,
                        settings.Columns,
                        tiles);
                    if (TinctureMatchResolver
                            .CountLegalNormalSwaps(candidate) >=
                        minimumLegalSwaps)
                    {
                        return candidate;
                    }

                    tiles[index] = replaced;
                }
            }
            else
            {
                var candidate = new TinctureMatchBoard(
                    settings.Rows,
                    settings.Columns,
                    tiles);
                if (TinctureMatchResolver
                        .CountLegalNormalSwaps(candidate) >=
                    minimumLegalSwaps)
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException(
                "The deterministic fallback board is not playable.");
        }

        private static void ValidateDimensions(
            TinctureMatchBoard board,
            TinctureMatchSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (board.Rows != settings.Rows ||
                board.Columns != settings.Columns)
            {
                throw new ArgumentException(
                    "Board dimensions must match the settings.",
                    nameof(board));
            }
        }

        private static void ValidatePlayableTiles(
            TinctureMatchBoard board)
        {
            if (board.CountTiles(TinctureTileKind.Empty) > 0)
            {
                throw new ArgumentException(
                    "A playable board cannot contain empty cells.",
                    nameof(board));
            }

            if (board.CountTiles(TinctureTileKind.Moonshine) > 1)
            {
                throw new ArgumentException(
                    "A board cannot contain more than one moonshine.",
                    nameof(board));
            }
        }

        private static void Shuffle<T>(
            T[] values,
            ref TinctureMatchRandom random)
        {
            for (int index = values.Length - 1; index > 0; index--)
            {
                int other = random.NextInt(index + 1);
                T temporary = values[index];
                values[index] = values[other];
                values[other] = temporary;
            }
        }
    }
}
