using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BarPromenade
{
    public enum TinctureTileKind
    {
        Empty = 0,
        Cherry,
        SeaBuckthorn,
        Blueberry,
        Mint,
        Horseradish,
        Moonshine
    }

    public enum TinctureMatchOrientation
    {
        Horizontal = 0,
        Vertical
    }

    public enum TinctureMatchMoveRejectionReason
    {
        None = 0,
        SessionFinished,
        OutOfBounds,
        NotAdjacent,
        EmptyTile,
        MoonshineRequiresNormalTile,
        NoMatchCreated
    }

    public readonly struct TinctureMatchCell :
        IEquatable<TinctureMatchCell>,
        IComparable<TinctureMatchCell>
    {
        public TinctureMatchCell(int row, int column)
        {
            Row = row;
            Column = column;
        }

        public int Row { get; }
        public int Column { get; }

        public bool IsOrthogonallyAdjacentTo(TinctureMatchCell other)
        {
            int rowDistance = Math.Abs(Row - other.Row);
            int columnDistance = Math.Abs(Column - other.Column);
            return rowDistance + columnDistance == 1;
        }

        public int CompareTo(TinctureMatchCell other)
        {
            int rowComparison = Row.CompareTo(other.Row);
            return rowComparison != 0
                ? rowComparison
                : Column.CompareTo(other.Column);
        }

        public bool Equals(TinctureMatchCell other)
        {
            return Row == other.Row && Column == other.Column;
        }

        public override bool Equals(object obj)
        {
            return
                obj is TinctureMatchCell other &&
                Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return Row * 397 ^ Column;
            }
        }

        public override string ToString()
        {
            return $"({Row}, {Column})";
        }

        public static bool operator ==(
            TinctureMatchCell left,
            TinctureMatchCell right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            TinctureMatchCell left,
            TinctureMatchCell right)
        {
            return !left.Equals(right);
        }
    }

    public readonly struct TinctureMatchSwap :
        IEquatable<TinctureMatchSwap>
    {
        public TinctureMatchSwap(
            TinctureMatchCell first,
            TinctureMatchCell second)
        {
            First = first;
            Second = second;
        }

        public TinctureMatchCell First { get; }
        public TinctureMatchCell Second { get; }

        public bool Equals(TinctureMatchSwap other)
        {
            return
                First.Equals(other.First) &&
                Second.Equals(other.Second);
        }

        public override bool Equals(object obj)
        {
            return obj is TinctureMatchSwap other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return First.GetHashCode() * 397 ^
                       Second.GetHashCode();
            }
        }
    }

    public sealed class TinctureMatchRun
    {
        private readonly ReadOnlyCollection<TinctureMatchCell> cells;

        internal TinctureMatchRun(
            TinctureTileKind kind,
            TinctureMatchOrientation orientation,
            IList<TinctureMatchCell> cells)
        {
            Kind = kind;
            Orientation = orientation;
            this.cells = Array.AsReadOnly(Copy(cells));
        }

        public TinctureTileKind Kind { get; }
        public TinctureMatchOrientation Orientation { get; }
        public IReadOnlyList<TinctureMatchCell> Cells => cells;
        public int Length => cells.Count;

        private static TinctureMatchCell[] Copy(
            IList<TinctureMatchCell> source)
        {
            var copy = new TinctureMatchCell[source.Count];
            source.CopyTo(copy, 0);
            return copy;
        }
    }

    public sealed class TinctureMatchSet
    {
        private readonly ReadOnlyCollection<TinctureMatchRun> runs;
        private readonly ReadOnlyCollection<TinctureMatchCell> cells;
        private readonly HashSet<TinctureMatchCell> cellLookup;

        internal TinctureMatchSet(
            IList<TinctureMatchRun> runs,
            IList<TinctureMatchCell> cells)
        {
            var runCopy = new TinctureMatchRun[runs.Count];
            runs.CopyTo(runCopy, 0);
            var cellCopy = new TinctureMatchCell[cells.Count];
            cells.CopyTo(cellCopy, 0);
            this.runs = Array.AsReadOnly(runCopy);
            this.cells = Array.AsReadOnly(cellCopy);
            cellLookup = new HashSet<TinctureMatchCell>(cellCopy);
        }

        public IReadOnlyList<TinctureMatchRun> Runs => runs;
        public IReadOnlyList<TinctureMatchCell> Cells => cells;
        public bool HasMatches => cells.Count > 0;
        public int UniqueCellCount => cells.Count;

        public bool Contains(TinctureMatchCell cell)
        {
            return cellLookup.Contains(cell);
        }
    }

    public readonly struct TinctureMatchClearedTile
    {
        public TinctureMatchClearedTile(
            TinctureMatchCell cell,
            TinctureTileKind kind)
        {
            Cell = cell;
            Kind = kind;
        }

        public TinctureMatchCell Cell { get; }
        public TinctureTileKind Kind { get; }
    }

    public sealed class TinctureMatchWaveResult
    {
        private readonly ReadOnlyCollection<TinctureMatchClearedTile>
            clearedTiles;
        private readonly ReadOnlyCollection<TinctureMatchRun> matchedRuns;

        internal TinctureMatchWaveResult(
            int depth,
            TinctureMatchBoard boardBeforeClear,
            TinctureMatchBoard boardAfterClear,
            TinctureMatchBoard boardAfterGravity,
            TinctureMatchBoard boardAfterRefill,
            IList<TinctureMatchClearedTile> clearedTiles,
            IList<TinctureMatchRun> matchedRuns,
            int cascadeMultiplier,
            int clearScore,
            int longRunBonus,
            int specialBonus,
            bool activatedMoonshine,
            TinctureTileKind activatedKind,
            bool createdMoonshine,
            TinctureMatchCell? createdMoonshineCell)
        {
            Depth = depth;
            BoardBeforeClear = boardBeforeClear;
            BoardAfterClear = boardAfterClear;
            BoardAfterGravity = boardAfterGravity;
            BoardAfterRefill = boardAfterRefill;
            this.clearedTiles = Array.AsReadOnly(
                Copy(clearedTiles));
            this.matchedRuns = Array.AsReadOnly(Copy(matchedRuns));
            CascadeMultiplier = cascadeMultiplier;
            ClearScore = clearScore;
            LongRunBonus = longRunBonus;
            SpecialBonus = specialBonus;
            ScoreAwarded =
                clearScore + longRunBonus + specialBonus;
            ActivatedMoonshine = activatedMoonshine;
            ActivatedKind = activatedKind;
            CreatedMoonshine = createdMoonshine;
            CreatedMoonshineCell = createdMoonshineCell;
        }

        public int Depth { get; }
        public TinctureMatchBoard BoardBeforeClear { get; }
        public TinctureMatchBoard BoardAfterClear { get; }
        public TinctureMatchBoard BoardAfterGravity { get; }
        public TinctureMatchBoard BoardAfterRefill { get; }
        public IReadOnlyList<TinctureMatchClearedTile> ClearedTiles =>
            clearedTiles;
        public IReadOnlyList<TinctureMatchRun> MatchedRuns => matchedRuns;
        public int ClearedTileCount => clearedTiles.Count;
        public int CascadeMultiplier { get; }
        public int ClearScore { get; }
        public int LongRunBonus { get; }
        public int SpecialBonus { get; }
        public int ScoreAwarded { get; }
        public bool ActivatedMoonshine { get; }
        public TinctureTileKind ActivatedKind { get; }
        public bool CreatedMoonshine { get; }
        public TinctureMatchCell? CreatedMoonshineCell { get; }

        private static TinctureMatchClearedTile[] Copy(
            IList<TinctureMatchClearedTile> source)
        {
            var copy = new TinctureMatchClearedTile[source.Count];
            source.CopyTo(copy, 0);
            return copy;
        }

        private static TinctureMatchRun[] Copy(
            IList<TinctureMatchRun> source)
        {
            var copy = new TinctureMatchRun[source.Count];
            source.CopyTo(copy, 0);
            return copy;
        }
    }

    public sealed class TinctureMatchMoveResult
    {
        private readonly ReadOnlyCollection<TinctureMatchWaveResult> waves;

        internal TinctureMatchMoveResult(
            bool accepted,
            TinctureMatchMoveRejectionReason rejectionReason,
            TinctureMatchCell from,
            TinctureMatchCell to,
            TinctureMatchBoard boardBeforeSwap,
            TinctureMatchBoard boardAfterSwap,
            TinctureMatchBoard boardFinal,
            IList<TinctureMatchWaveResult> waves,
            int moveNumber,
            int movesRemaining,
            int scoreAwarded,
            int totalScore,
            int cascadeDepth,
            bool activatedMoonshine,
            TinctureTileKind activatedKind,
            bool createdMoonshine,
            bool wasReshuffled,
            bool isFinished)
        {
            Accepted = accepted;
            RejectionReason = rejectionReason;
            From = from;
            To = to;
            BoardBeforeSwap = boardBeforeSwap;
            BoardAfterSwap = boardAfterSwap;
            BoardFinal = boardFinal;
            var waveCopy = new TinctureMatchWaveResult[waves.Count];
            waves.CopyTo(waveCopy, 0);
            this.waves = Array.AsReadOnly(waveCopy);
            MoveNumber = moveNumber;
            MovesRemaining = movesRemaining;
            ScoreAwarded = scoreAwarded;
            TotalScore = totalScore;
            CascadeDepth = cascadeDepth;
            ActivatedMoonshine = activatedMoonshine;
            ActivatedKind = activatedKind;
            CreatedMoonshine = createdMoonshine;
            WasReshuffled = wasReshuffled;
            IsFinished = isFinished;
        }

        public bool Accepted { get; }
        public TinctureMatchMoveRejectionReason RejectionReason { get; }
        public TinctureMatchCell From { get; }
        public TinctureMatchCell To { get; }
        public TinctureMatchBoard BoardBeforeSwap { get; }
        public TinctureMatchBoard BoardAfterSwap { get; }
        public TinctureMatchBoard BoardFinal { get; }
        public IReadOnlyList<TinctureMatchWaveResult> Waves => waves;
        public int MoveNumber { get; }
        public int MovesRemaining { get; }
        public int ScoreAwarded { get; }
        public int TotalScore { get; }
        public int CascadeDepth { get; }
        public bool ActivatedMoonshine { get; }
        public TinctureTileKind ActivatedKind { get; }
        public bool CreatedMoonshine { get; }
        public bool WasReshuffled { get; }
        public bool IsFinished { get; }
    }
}
