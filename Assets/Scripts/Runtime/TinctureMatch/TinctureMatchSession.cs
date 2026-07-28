using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public sealed class TinctureMatchSession
    {
        public const int PointsPerClearedTile = 10;
        public const int PointsPerLongRunCell = 20;
        public const int MoonshineCreationBonus = 50;
        public const int ExistingMoonshineMatchBonus = 50;
        public const int MoonshineActivationBonus = 100;
        public const int MaximumCascadeMultiplier = 5;

        private static readonly TinctureMatchWaveResult[] NoWaves =
            Array.Empty<TinctureMatchWaveResult>();

        private TinctureMatchRandom random;

        public TinctureMatchSession(int seed)
            : this(seed, TinctureMatchSettings.Normal)
        {
        }

        public TinctureMatchSession(
            int seed,
            TinctureMatchSettings settings)
        {
            Settings = settings ??
                throw new ArgumentNullException(nameof(settings));
            random = new TinctureMatchRandom(seed);
            Board = TinctureMatchGenerator.Generate(
                ref random,
                Settings,
                1,
                Settings.MinimumInitialLegalSwaps);
        }

        public TinctureMatchSession(
            int seed,
            TinctureMatchBoard initialBoard,
            TinctureMatchSettings settings = null)
        {
            Settings = settings ??
                TinctureMatchSettings.Normal;
            ValidateInitialBoard(initialBoard, Settings);
            random = new TinctureMatchRandom(seed);
            Board = initialBoard;
            if (TinctureMatchResolver.CountLegalNormalSwaps(Board) == 0)
            {
                Board = TinctureMatchGenerator.Reshuffle(
                    ref random,
                    Board,
                    Settings);
            }
        }

        public TinctureMatchSettings Settings { get; }
        public TinctureMatchBoard Board { get; private set; }
        public int Score { get; private set; }
        public int MovesCompleted { get; private set; }
        public int MovesRemaining =>
            Math.Max(0, Settings.MoveLimit - MovesCompleted);
        public int BestCascade { get; private set; }
        public bool IsFinished =>
            MovesCompleted >= Settings.MoveLimit;

        public TinctureTileKind GetTile(int row, int column)
        {
            return Board.GetTile(row, column);
        }

        public bool TrySwap(
            int fromRow,
            int fromColumn,
            int toRow,
            int toColumn,
            out TinctureMatchMoveResult result)
        {
            return TrySwap(
                new TinctureMatchCell(fromRow, fromColumn),
                new TinctureMatchCell(toRow, toColumn),
                out result);
        }

        public bool TrySwap(
            TinctureMatchCell from,
            TinctureMatchCell to,
            out TinctureMatchMoveResult result)
        {
            TinctureMatchBoard before = Board;
            if (IsFinished)
            {
                result = CreateRejectedResult(
                    TinctureMatchMoveRejectionReason.SessionFinished,
                    from,
                    to,
                    before);
                return false;
            }

            if (!before.Contains(from) || !before.Contains(to))
            {
                result = CreateRejectedResult(
                    TinctureMatchMoveRejectionReason.OutOfBounds,
                    from,
                    to,
                    before);
                return false;
            }

            if (!from.IsOrthogonallyAdjacentTo(to))
            {
                result = CreateRejectedResult(
                    TinctureMatchMoveRejectionReason.NotAdjacent,
                    from,
                    to,
                    before);
                return false;
            }

            TinctureTileKind fromKind =
                before[from.Row, from.Column];
            TinctureTileKind toKind =
                before[to.Row, to.Column];
            if (fromKind == TinctureTileKind.Empty ||
                toKind == TinctureTileKind.Empty)
            {
                result = CreateRejectedResult(
                    TinctureMatchMoveRejectionReason.EmptyTile,
                    from,
                    to,
                    before);
                return false;
            }

            bool fromMoonshine =
                fromKind == TinctureTileKind.Moonshine;
            bool toMoonshine =
                toKind == TinctureTileKind.Moonshine;
            bool isActivation = fromMoonshine ^ toMoonshine;
            if (fromMoonshine || toMoonshine)
            {
                TinctureTileKind otherKind =
                    fromMoonshine ? toKind : fromKind;
                if (!isActivation ||
                    !TinctureMatchResolver.IsNormalTile(otherKind))
                {
                    result = CreateRejectedResult(
                        TinctureMatchMoveRejectionReason
                            .MoonshineRequiresNormalTile,
                        from,
                        to,
                        before);
                    return false;
                }
            }
            else if (!TinctureMatchResolver.IsLegalNormalSwap(
                         before,
                         from,
                         to))
            {
                result = CreateRejectedResult(
                    TinctureMatchMoveRejectionReason.NoMatchCreated,
                    from,
                    to,
                    before);
                return false;
            }

            TinctureTileKind[] working = before.ToArray();
            Swap(
                working,
                GetIndex(from),
                GetIndex(to));
            var afterSwap = Snapshot(working);
            var waves = new List<TinctureMatchWaveResult>();
            bool createdMoonshine = false;
            bool wasReshuffled = false;
            TinctureTileKind activatedKind =
                TinctureTileKind.Empty;

            if (isActivation)
            {
                activatedKind =
                    fromMoonshine ? toKind : fromKind;
                waves.Add(
                    ResolveActivation(
                        working,
                        activatedKind));
            }
            else
            {
                TinctureMatchSet initialMatches =
                    TinctureMatchResolver.FindMatches(
                        working,
                        Settings.Rows,
                        Settings.Columns);
                TinctureMatchWaveResult firstWave =
                    ResolveMatches(
                        working,
                        initialMatches,
                        1,
                        true,
                        from,
                        to);
                waves.Add(firstWave);
                createdMoonshine =
                    firstWave.CreatedMoonshine;
            }

            while (true)
            {
                TinctureMatchSet matches =
                    TinctureMatchResolver.FindMatches(
                        working,
                        Settings.Rows,
                        Settings.Columns);
                if (!matches.HasMatches)
                {
                    break;
                }

                if (waves.Count >=
                    Settings.MaximumCascadeWaves)
                {
                    int moonshineCount = CountTiles(
                        working,
                        TinctureTileKind.Moonshine);
                    TinctureMatchBoard stabilized =
                        TinctureMatchGenerator.Generate(
                            ref random,
                            Settings,
                            moonshineCount,
                            Settings.MinimumInitialLegalSwaps);
                    working = stabilized.ToArray();
                    wasReshuffled = true;
                    break;
                }

                waves.Add(
                    ResolveMatches(
                        working,
                        matches,
                        waves.Count + 1,
                        false,
                        from,
                        to));
            }

            var stableBoard = Snapshot(working);
            if (TinctureMatchResolver.CountLegalNormalSwaps(
                    stableBoard) == 0)
            {
                stableBoard =
                    TinctureMatchGenerator.Reshuffle(
                        ref random,
                        stableBoard,
                        Settings);
                working = stableBoard.ToArray();
                wasReshuffled = true;
            }

            int scoreAwarded = 0;
            for (int index = 0; index < waves.Count; index++)
            {
                scoreAwarded += waves[index].ScoreAwarded;
            }

            MovesCompleted++;
            Score += scoreAwarded;
            BestCascade = Math.Max(BestCascade, waves.Count);
            Board = Snapshot(working);
            result = new TinctureMatchMoveResult(
                true,
                TinctureMatchMoveRejectionReason.None,
                from,
                to,
                before,
                afterSwap,
                Board,
                waves,
                MovesCompleted,
                MovesRemaining,
                scoreAwarded,
                Score,
                waves.Count,
                isActivation,
                activatedKind,
                createdMoonshine,
                wasReshuffled,
                IsFinished);
            return true;
        }

        private TinctureMatchWaveResult ResolveActivation(
            TinctureTileKind[] working,
            TinctureTileKind activatedKind)
        {
            TinctureMatchBoard beforeClear = Snapshot(working);
            var cleared =
                new List<TinctureMatchClearedTile>();
            for (int index = 0; index < working.Length; index++)
            {
                TinctureTileKind kind = working[index];
                if (kind != activatedKind &&
                    kind != TinctureTileKind.Moonshine)
                {
                    continue;
                }

                cleared.Add(
                    new TinctureMatchClearedTile(
                        GetCell(index),
                        kind));
                working[index] = TinctureTileKind.Empty;
            }

            TinctureMatchBoard afterClear = Snapshot(working);
            ApplyGravity(working);
            TinctureMatchBoard afterGravity = Snapshot(working);
            Refill(working);
            TinctureMatchBoard afterRefill = Snapshot(working);
            int multiplier = 1;
            int clearScore =
                cleared.Count *
                PointsPerClearedTile *
                multiplier;
            return new TinctureMatchWaveResult(
                1,
                beforeClear,
                afterClear,
                afterGravity,
                afterRefill,
                cleared,
                Array.Empty<TinctureMatchRun>(),
                multiplier,
                clearScore,
                0,
                MoonshineActivationBonus,
                true,
                activatedKind,
                false,
                null);
        }

        private TinctureMatchWaveResult ResolveMatches(
            TinctureTileKind[] working,
            TinctureMatchSet matches,
            int depth,
            bool allowMoonshineCreation,
            TinctureMatchCell preferredFrom,
            TinctureMatchCell preferredTo)
        {
            TinctureMatchBoard beforeClear = Snapshot(working);
            TinctureMatchCell creationCell = default;
            bool hasCreationPattern =
                allowMoonshineCreation &&
                TryChooseMoonshineCell(
                    matches,
                    preferredFrom,
                    preferredTo,
                    out creationCell);
            bool hasMoonshine =
                CountTiles(
                    working,
                    TinctureTileKind.Moonshine) > 0;
            bool createsMoonshine =
                hasCreationPattern && !hasMoonshine;
            int specialBonus = createsMoonshine
                ? MoonshineCreationBonus
                : hasCreationPattern && hasMoonshine
                    ? ExistingMoonshineMatchBonus
                    : 0;

            var cleared =
                new List<TinctureMatchClearedTile>();
            for (int index = 0;
                 index < matches.Cells.Count;
                 index++)
            {
                TinctureMatchCell cell = matches.Cells[index];
                if (createsMoonshine && cell == creationCell)
                {
                    continue;
                }

                int tileIndex = GetIndex(cell);
                cleared.Add(
                    new TinctureMatchClearedTile(
                        cell,
                        working[tileIndex]));
                working[tileIndex] = TinctureTileKind.Empty;
            }

            if (createsMoonshine)
            {
                working[GetIndex(creationCell)] =
                    TinctureTileKind.Moonshine;
            }

            TinctureMatchBoard afterClear = Snapshot(working);
            ApplyGravity(working);
            TinctureMatchBoard afterGravity = Snapshot(working);
            Refill(working);
            TinctureMatchBoard afterRefill = Snapshot(working);

            int multiplier = Math.Min(
                depth,
                MaximumCascadeMultiplier);
            int clearScore =
                cleared.Count *
                PointsPerClearedTile *
                multiplier;
            int longRunBonus = 0;
            var copiedRuns =
                new List<TinctureMatchRun>(matches.Runs.Count);
            for (int index = 0;
                 index < matches.Runs.Count;
                 index++)
            {
                TinctureMatchRun run = matches.Runs[index];
                copiedRuns.Add(run);
                longRunBonus +=
                    Math.Max(0, run.Length - 3) *
                    PointsPerLongRunCell;
            }

            return new TinctureMatchWaveResult(
                depth,
                beforeClear,
                afterClear,
                afterGravity,
                afterRefill,
                cleared,
                copiedRuns,
                multiplier,
                clearScore,
                longRunBonus,
                specialBonus,
                false,
                TinctureTileKind.Empty,
                createsMoonshine,
                createsMoonshine
                    ? (TinctureMatchCell?)creationCell
                    : null);
        }

        private void ApplyGravity(TinctureTileKind[] working)
        {
            for (int column = 0;
                 column < Settings.Columns;
                 column++)
            {
                int writeRow = Settings.Rows - 1;
                for (int row = Settings.Rows - 1;
                     row >= 0;
                     row--)
                {
                    int readIndex =
                        row * Settings.Columns + column;
                    TinctureTileKind kind = working[readIndex];
                    if (kind == TinctureTileKind.Empty)
                    {
                        continue;
                    }

                    int writeIndex =
                        writeRow * Settings.Columns + column;
                    working[writeIndex] = kind;
                    if (writeIndex != readIndex)
                    {
                        working[readIndex] =
                            TinctureTileKind.Empty;
                    }

                    writeRow--;
                }

                for (int row = writeRow; row >= 0; row--)
                {
                    working[row * Settings.Columns + column] =
                        TinctureTileKind.Empty;
                }
            }
        }

        private void Refill(TinctureTileKind[] working)
        {
            for (int row = 0; row < Settings.Rows; row++)
            {
                for (int column = 0;
                     column < Settings.Columns;
                     column++)
                {
                    int index =
                        row * Settings.Columns + column;
                    if (working[index] !=
                        TinctureTileKind.Empty)
                    {
                        continue;
                    }

                    working[index] =
                        (TinctureTileKind)(
                            (int)TinctureTileKind.Cherry +
                            random.NextInt(5));
                }
            }
        }

        private static bool TryChooseMoonshineCell(
            TinctureMatchSet matches,
            TinctureMatchCell preferredFrom,
            TinctureMatchCell preferredTo,
            out TinctureMatchCell cell)
        {
            var horizontal =
                new HashSet<TinctureMatchCell>();
            var vertical =
                new HashSet<TinctureMatchCell>();
            for (int index = 0;
                 index < matches.Runs.Count;
                 index++)
            {
                TinctureMatchRun run = matches.Runs[index];
                ISet<TinctureMatchCell> destination =
                    run.Orientation ==
                    TinctureMatchOrientation.Horizontal
                        ? horizontal
                        : vertical;
                for (int cellIndex = 0;
                     cellIndex < run.Cells.Count;
                     cellIndex++)
                {
                    destination.Add(run.Cells[cellIndex]);
                }
            }

            if (IsIntersection(
                    preferredTo,
                    horizontal,
                    vertical))
            {
                cell = preferredTo;
                return true;
            }

            if (IsIntersection(
                    preferredFrom,
                    horizontal,
                    vertical))
            {
                cell = preferredFrom;
                return true;
            }

            for (int index = 0;
                 index < matches.Cells.Count;
                 index++)
            {
                TinctureMatchCell candidate =
                    matches.Cells[index];
                if (IsIntersection(
                        candidate,
                        horizontal,
                        vertical))
                {
                    cell = candidate;
                    return true;
                }
            }

            if (IsInLongRun(matches, preferredTo))
            {
                cell = preferredTo;
                return true;
            }

            if (IsInLongRun(matches, preferredFrom))
            {
                cell = preferredFrom;
                return true;
            }

            for (int index = 0;
                 index < matches.Runs.Count;
                 index++)
            {
                TinctureMatchRun run = matches.Runs[index];
                if (run.Length >= 4)
                {
                    cell = run.Cells[0];
                    return true;
                }
            }

            cell = default;
            return false;
        }

        private static bool IsIntersection(
            TinctureMatchCell cell,
            ISet<TinctureMatchCell> horizontal,
            ISet<TinctureMatchCell> vertical)
        {
            return
                horizontal.Contains(cell) &&
                vertical.Contains(cell);
        }

        private static bool IsInLongRun(
            TinctureMatchSet matches,
            TinctureMatchCell cell)
        {
            for (int index = 0;
                 index < matches.Runs.Count;
                 index++)
            {
                TinctureMatchRun run = matches.Runs[index];
                if (run.Length < 4)
                {
                    continue;
                }

                for (int cellIndex = 0;
                     cellIndex < run.Cells.Count;
                     cellIndex++)
                {
                    if (run.Cells[cellIndex] == cell)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private TinctureMatchMoveResult CreateRejectedResult(
            TinctureMatchMoveRejectionReason reason,
            TinctureMatchCell from,
            TinctureMatchCell to,
            TinctureMatchBoard board)
        {
            return new TinctureMatchMoveResult(
                false,
                reason,
                from,
                to,
                board,
                board,
                board,
                NoWaves,
                MovesCompleted,
                MovesRemaining,
                0,
                Score,
                0,
                false,
                TinctureTileKind.Empty,
                false,
                false,
                IsFinished);
        }

        private TinctureMatchBoard Snapshot(
            TinctureTileKind[] tiles)
        {
            return new TinctureMatchBoard(
                Settings.Rows,
                Settings.Columns,
                tiles);
        }

        private int GetIndex(TinctureMatchCell cell)
        {
            return
                cell.Row * Settings.Columns +
                cell.Column;
        }

        private TinctureMatchCell GetCell(int index)
        {
            return new TinctureMatchCell(
                index / Settings.Columns,
                index % Settings.Columns);
        }

        private static int CountTiles(
            TinctureTileKind[] tiles,
            TinctureTileKind kind)
        {
            int count = 0;
            for (int index = 0; index < tiles.Length; index++)
            {
                if (tiles[index] == kind)
                {
                    count++;
                }
            }

            return count;
        }

        private static void Swap(
            TinctureTileKind[] tiles,
            int first,
            int second)
        {
            TinctureTileKind temporary = tiles[first];
            tiles[first] = tiles[second];
            tiles[second] = temporary;
        }

        private static void ValidateInitialBoard(
            TinctureMatchBoard board,
            TinctureMatchSettings settings)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (board.Rows != settings.Rows ||
                board.Columns != settings.Columns)
            {
                throw new ArgumentException(
                    "Initial board dimensions must match settings.",
                    nameof(board));
            }

            if (board.CountTiles(TinctureTileKind.Empty) > 0)
            {
                throw new ArgumentException(
                    "Initial board cannot contain empty cells.",
                    nameof(board));
            }

            if (board.CountTiles(TinctureTileKind.Moonshine) > 1)
            {
                throw new ArgumentException(
                    "Initial board cannot contain multiple moonshines.",
                    nameof(board));
            }

            if (TinctureMatchResolver
                .FindMatches(board)
                .HasMatches)
            {
                throw new ArgumentException(
                    "Initial board must be stable.",
                    nameof(board));
            }
        }
    }
}
