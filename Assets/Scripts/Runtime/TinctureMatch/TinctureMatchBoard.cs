using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public sealed class TinctureMatchBoard :
        IEquatable<TinctureMatchBoard>
    {
        private readonly TinctureTileKind[] tiles;

        public TinctureMatchBoard(
            int rows,
            int columns,
            IEnumerable<TinctureTileKind> tiles)
        {
            if (rows < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(rows));
            }

            if (columns < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(columns));
            }

            if (tiles == null)
            {
                throw new ArgumentNullException(nameof(tiles));
            }

            Rows = rows;
            Columns = columns;
            var copy = new List<TinctureTileKind>(tiles).ToArray();
            if (copy.Length != rows * columns)
            {
                throw new ArgumentException(
                    "The tile count must match the board dimensions.",
                    nameof(tiles));
            }

            for (int index = 0; index < copy.Length; index++)
            {
                ValidateTile(copy[index], nameof(tiles));
            }

            this.tiles = copy;
        }

        public int Rows { get; }
        public int Columns { get; }
        public int Count => tiles.Length;

        public TinctureTileKind this[int row, int column] =>
            GetTile(row, column);

        public TinctureTileKind GetTile(int row, int column)
        {
            return tiles[GetIndex(row, column)];
        }

        public bool Contains(TinctureMatchCell cell)
        {
            return
                cell.Row >= 0 &&
                cell.Row < Rows &&
                cell.Column >= 0 &&
                cell.Column < Columns;
        }

        public int CountTiles(TinctureTileKind kind)
        {
            ValidateTile(kind, nameof(kind));
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

        public TinctureTileKind[] ToArray()
        {
            return (TinctureTileKind[])tiles.Clone();
        }

        public bool Equals(TinctureMatchBoard other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other == null ||
                Rows != other.Rows ||
                Columns != other.Columns)
            {
                return false;
            }

            for (int index = 0; index < tiles.Length; index++)
            {
                if (tiles[index] != other.tiles[index])
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as TinctureMatchBoard);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Rows * 397 ^ Columns;
                for (int index = 0; index < tiles.Length; index++)
                {
                    hash = hash * 31 + (int)tiles[index];
                }

                return hash;
            }
        }

        internal int GetIndex(TinctureMatchCell cell)
        {
            return GetIndex(cell.Row, cell.Column);
        }

        internal int GetIndex(int row, int column)
        {
            if (row < 0 || row >= Rows)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            if (column < 0 || column >= Columns)
            {
                throw new ArgumentOutOfRangeException(nameof(column));
            }

            return row * Columns + column;
        }

        private static void ValidateTile(
            TinctureTileKind kind,
            string parameterName)
        {
            if (kind < TinctureTileKind.Empty ||
                kind > TinctureTileKind.Moonshine)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }
}
