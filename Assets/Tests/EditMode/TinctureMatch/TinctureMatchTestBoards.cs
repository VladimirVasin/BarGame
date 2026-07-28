using System;

namespace BarPromenade.Tests.EditMode
{
    internal static class TinctureMatchTestBoards
    {
        private static readonly int[] LongMatchValues =
        {
            0, 2, 2, 3, 0, 3, 0,
            3, 2, 1, 1, 2, 1, 4,
            2, 3, 2, 2, 3, 3, 1,
            3, 2, 2, 3, 4, 1, 1,
            2, 3, 3, 4, 4, 2, 3,
            2, 4, 3, 1, 2, 3, 3,
            4, 4, 0, 3, 1, 1, 0
        };

        public static TinctureMatchBoard LongMatchBoard(
            bool withMoonshine = false)
        {
            TinctureTileKind[] tiles = Convert(LongMatchValues);
            if (withMoonshine)
            {
                tiles[0] = TinctureTileKind.Moonshine;
            }

            return new TinctureMatchBoard(7, 7, tiles);
        }

        public static TinctureMatchBoard NoMoveBoard(
            bool withMoonshine = false)
        {
            var tiles = new TinctureTileKind[49];
            for (int row = 0; row < 7; row++)
            {
                for (int column = 0; column < 7; column++)
                {
                    tiles[row * 7 + column] =
                        (TinctureTileKind)(
                            1 + (row + column) % 5);
                }
            }

            if (withMoonshine)
            {
                tiles[0] = TinctureTileKind.Moonshine;
            }

            return new TinctureMatchBoard(7, 7, tiles);
        }

        public static TinctureMatchBoard CrossBoard()
        {
            TinctureTileKind A = TinctureTileKind.Cherry;
            TinctureTileKind B = TinctureTileKind.SeaBuckthorn;
            TinctureTileKind C = TinctureTileKind.Blueberry;
            TinctureTileKind D = TinctureTileKind.Mint;
            TinctureTileKind E = TinctureTileKind.Horseradish;
            return new TinctureMatchBoard(
                5,
                5,
                new[]
                {
                    A, B, C, D, E,
                    B, C, A, E, D,
                    C, A, A, A, B,
                    D, E, A, B, C,
                    E, D, B, C, A
                });
        }

        public static TinctureMatchBoard WithTile(
            TinctureMatchBoard board,
            TinctureMatchCell cell,
            TinctureTileKind kind)
        {
            TinctureTileKind[] tiles = board.ToArray();
            tiles[cell.Row * board.Columns + cell.Column] = kind;
            return new TinctureMatchBoard(
                board.Rows,
                board.Columns,
                tiles);
        }

        public static void AssertBoardsEqual(
            TinctureMatchBoard expected,
            TinctureMatchBoard actual)
        {
            if (!expected.Equals(actual))
            {
                throw new InvalidOperationException(
                    "Expected boards to contain identical tiles.");
            }
        }

        private static TinctureTileKind[] Convert(int[] values)
        {
            var tiles = new TinctureTileKind[values.Length];
            for (int index = 0; index < values.Length; index++)
            {
                tiles[index] =
                    (TinctureTileKind)(values[index] + 1);
            }

            return tiles;
        }
    }
}
