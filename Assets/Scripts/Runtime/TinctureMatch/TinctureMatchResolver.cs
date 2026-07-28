using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BarPromenade
{
    public static class TinctureMatchResolver
    {
        public static bool IsNormalTile(TinctureTileKind kind)
        {
            return
                kind >= TinctureTileKind.Cherry &&
                kind <= TinctureTileKind.Horseradish;
        }

        public static TinctureMatchSet FindMatches(
            TinctureMatchBoard board)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            return FindMatches(
                board.ToArray(),
                board.Rows,
                board.Columns);
        }

        public static bool IsLegalNormalSwap(
            TinctureMatchBoard board,
            TinctureMatchCell first,
            TinctureMatchCell second)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (!board.Contains(first) ||
                !board.Contains(second) ||
                !first.IsOrthogonallyAdjacentTo(second))
            {
                return false;
            }

            TinctureTileKind firstKind =
                board[first.Row, first.Column];
            TinctureTileKind secondKind =
                board[second.Row, second.Column];
            if (!IsNormalTile(firstKind) ||
                !IsNormalTile(secondKind) ||
                firstKind == secondKind)
            {
                return false;
            }

            TinctureTileKind[] swapped = board.ToArray();
            Swap(
                swapped,
                board.GetIndex(first),
                board.GetIndex(second));
            TinctureMatchSet matches = FindMatches(
                swapped,
                board.Rows,
                board.Columns);
            return
                matches.Contains(first) ||
                matches.Contains(second);
        }

        public static IReadOnlyList<TinctureMatchSwap>
            GetLegalNormalSwaps(TinctureMatchBoard board)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            var swaps = new List<TinctureMatchSwap>();
            for (int row = 0; row < board.Rows; row++)
            {
                for (int column = 0;
                     column < board.Columns;
                     column++)
                {
                    var current =
                        new TinctureMatchCell(row, column);
                    if (column + 1 < board.Columns)
                    {
                        var right =
                            new TinctureMatchCell(row, column + 1);
                        if (IsLegalNormalSwap(board, current, right))
                        {
                            swaps.Add(
                                new TinctureMatchSwap(current, right));
                        }
                    }

                    if (row + 1 < board.Rows)
                    {
                        var below =
                            new TinctureMatchCell(row + 1, column);
                        if (IsLegalNormalSwap(board, current, below))
                        {
                            swaps.Add(
                                new TinctureMatchSwap(current, below));
                        }
                    }
                }
            }

            return new ReadOnlyCollection<TinctureMatchSwap>(
                swaps.ToArray());
        }

        public static int CountLegalNormalSwaps(
            TinctureMatchBoard board)
        {
            return GetLegalNormalSwaps(board).Count;
        }

        internal static TinctureMatchSet FindMatches(
            TinctureTileKind[] tiles,
            int rows,
            int columns)
        {
            var runs = new List<TinctureMatchRun>();
            var uniqueCells = new HashSet<TinctureMatchCell>();

            for (int row = 0; row < rows; row++)
            {
                int start = 0;
                while (start < columns)
                {
                    TinctureTileKind kind =
                        tiles[row * columns + start];
                    int end = start + 1;
                    while (end < columns &&
                           tiles[row * columns + end] == kind)
                    {
                        end++;
                    }

                    int length = end - start;
                    if (IsNormalTile(kind) && length >= 3)
                    {
                        AddRun(
                            runs,
                            uniqueCells,
                            kind,
                            TinctureMatchOrientation.Horizontal,
                            row,
                            start,
                            length);
                    }

                    start = end;
                }
            }

            for (int column = 0; column < columns; column++)
            {
                int start = 0;
                while (start < rows)
                {
                    TinctureTileKind kind =
                        tiles[start * columns + column];
                    int end = start + 1;
                    while (end < rows &&
                           tiles[end * columns + column] == kind)
                    {
                        end++;
                    }

                    int length = end - start;
                    if (IsNormalTile(kind) && length >= 3)
                    {
                        AddRun(
                            runs,
                            uniqueCells,
                            kind,
                            TinctureMatchOrientation.Vertical,
                            column,
                            start,
                            length);
                    }

                    start = end;
                }
            }

            var orderedCells =
                new List<TinctureMatchCell>(uniqueCells);
            orderedCells.Sort();
            return new TinctureMatchSet(runs, orderedCells);
        }

        private static void AddRun(
            ICollection<TinctureMatchRun> destination,
            ISet<TinctureMatchCell> uniqueCells,
            TinctureTileKind kind,
            TinctureMatchOrientation orientation,
            int fixedCoordinate,
            int variableStart,
            int length)
        {
            var cells = new List<TinctureMatchCell>(length);
            for (int offset = 0; offset < length; offset++)
            {
                TinctureMatchCell cell =
                    orientation ==
                    TinctureMatchOrientation.Horizontal
                        ? new TinctureMatchCell(
                            fixedCoordinate,
                            variableStart + offset)
                        : new TinctureMatchCell(
                            variableStart + offset,
                            fixedCoordinate);
                cells.Add(cell);
                uniqueCells.Add(cell);
            }

            destination.Add(
                new TinctureMatchRun(kind, orientation, cells));
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
    }
}
