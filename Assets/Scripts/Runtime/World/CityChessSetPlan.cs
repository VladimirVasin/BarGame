using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One man, on one square, in the chess recipe's own frame.</summary>
    public readonly struct CityChessManPlacement
    {
        public CityChessManPlacement(
            CityChessPieceKind kind,
            bool isLight,
            int table,
            int file,
            int rank,
            Vector2 offset,
            float yawDegrees)
        {
            Kind = kind;
            IsLight = isLight;
            Table = table;
            File = file;
            Rank = rank;
            Offset = offset;
            YawDegrees = yawDegrees;
        }

        public CityChessPieceKind Kind { get; }

        /// <summary>Bone rather than bog oak. Which of the two men owns
        /// it follows from the board: light is always the side of the
        /// table its player sits at.</summary>
        public bool IsLight { get; }

        /// <summary>`-1` for the chess table, `+1` for the draughts one,
        /// matching the seat ids the recipe hands out.</summary>
        public int Table { get; }

        public int File { get; }
        public int Rank { get; }

        /// <summary>How far off the exact middle of the square he was
        /// actually put down, in the local `(tangent, forward)` plane.</summary>
        public Vector2 Offset { get; }

        /// <summary>Zero looks up the board along `+Forward`.</summary>
        public float YawDegrees { get; }
    }

    /// <summary>
    /// Both boards, set up. Pure, deterministic and Unity-free apart
    /// from the vector types, because "correct" here is a claim that can
    /// be checked rather than eyeballed: a1 dark, the white queen on her
    /// own colour, sixteen men a side facing the right way, and twelve
    /// draughts on the dark squares of the three rows nearest the man
    /// who plays them.
    ///
    /// Neither game is in progress. The two men have been sitting over
    /// two untouched openings for years, which is the same joke the
    /// empty planks opposite them are: the set is laid out for four and
    /// used by nobody. Nothing in the runtime moves a piece.
    ///
    /// The chess player holds `-seat-a1`, so the chess board is the
    /// `-1` table and White is the near side of it; the draughts player
    /// holds `-seat-b2`, so his board is `+1` and his side is the far
    /// ranks. Each is White at his own board, and each has a light
    /// square on his right.
    /// </summary>
    public static class CityChessSetPlan
    {
        /// <summary>The table the chess player sits at.</summary>
        public const int ChessTable = -1;

        /// <summary>And the one the draughts player sits at.</summary>
        public const int DraughtsTable = 1;

        public const int ChessMenPerSide = 16;
        public const int DraughtsPerSide = 12;

        /// <summary>
        /// How far a man may sit off the middle of his square. Two old
        /// men set these up by hand and a set placed to the micron reads
        /// as a render; this is small enough that no man crosses his own
        /// square's edge and large enough to see.
        /// </summary>
        public const float MaximumOffsetMeters = 0.006f;

        public const float MaximumYawJitterDegrees = 6f;

        /// <summary>The back rank, from the `a` file to the `h` file.</summary>
        private static readonly CityChessPieceKind[] BackRank =
        {
            CityChessPieceKind.Rook,
            CityChessPieceKind.Knight,
            CityChessPieceKind.Bishop,
            CityChessPieceKind.Queen,
            CityChessPieceKind.King,
            CityChessPieceKind.Bishop,
            CityChessPieceKind.Knight,
            CityChessPieceKind.Rook
        };

        /// <summary>
        /// The `a` file is the far one from a man's right hand, and his
        /// right is `-Tangent` because the tangent runs to the left of
        /// anyone facing `+Forward`. So `a` is file `7` and `h` is file
        /// `0`, which is what puts the queen on `d1` — file `4` — and
        /// `d1` light.
        /// </summary>
        public static int FileOfChessColumn(int column)
        {
            if (column < 0 || column >= CityChessBoardGeometry.SquaresPerSide)
            {
                throw new ArgumentOutOfRangeException(nameof(column));
            }

            return CityChessBoardGeometry.SquaresPerSide - 1 - column;
        }

        public static IReadOnlyList<CityChessManPlacement> Create(
            int citySeed)
        {
            uint state = CreateState(citySeed);
            var men = new List<CityChessManPlacement>(
                (ChessMenPerSide + DraughtsPerSide) * 2);
            AppendChess(men, ref state);
            AppendDraughts(men, ref state);
            return men;
        }

        private static void AppendChess(
            List<CityChessManPlacement> men,
            ref uint state)
        {
            const int size = CityChessBoardGeometry.SquaresPerSide;
            for (int column = 0; column < size; column++)
            {
                int file = FileOfChessColumn(column);
                CityChessPieceKind officer = BackRank[column];

                // White is the near side of the chess player's own
                // board, so his officers stand on rank 0 and his pawns
                // on rank 1. Black is the same eight ranks away, and a
                // knight always looks at the other side.
                men.Add(Place(
                    officer, true, ChessTable, file, 0, 0f, ref state));
                men.Add(Place(
                    CityChessPieceKind.Pawn, true, ChessTable, file, 1,
                    0f, ref state));
                men.Add(Place(
                    CityChessPieceKind.Pawn, false, ChessTable, file,
                    size - 2, 180f, ref state));
                men.Add(Place(
                    officer, false, ChessTable, file, size - 1, 180f,
                    ref state));
            }
        }

        private static void AppendDraughts(
            List<CityChessManPlacement> men,
            ref uint state)
        {
            const int size = CityChessBoardGeometry.SquaresPerSide;
            for (int rank = 0; rank < size; rank++)
            {
                // Three rows each, and the middle two stay empty. The
                // draughts player sits at the far end of his own board,
                // so the light men are the high ranks.
                bool light = rank >= size - 3;
                if (!light && rank > 2)
                {
                    continue;
                }

                for (int file = 0; file < size; file++)
                {
                    // A draught only ever stands on a dark square,
                    // because a draught only ever moves on a diagonal.
                    if (!CityChessBoardGeometry.IsDarkSquare(file, rank))
                    {
                        continue;
                    }

                    men.Add(Place(
                        CityChessPieceKind.Draught,
                        light,
                        DraughtsTable,
                        file,
                        rank,
                        0f,
                        ref state));
                }
            }
        }

        private static CityChessManPlacement Place(
            CityChessPieceKind kind,
            bool isLight,
            int table,
            int file,
            int rank,
            float yawDegrees,
            ref uint state)
        {
            var offset = new Vector2(
                NextSigned(ref state) * MaximumOffsetMeters,
                NextSigned(ref state) * MaximumOffsetMeters);
            float yaw = yawDegrees +
                NextSigned(ref state) * MaximumYawJitterDegrees;
            return new CityChessManPlacement(
                kind,
                isLight,
                table,
                file,
                rank,
                offset,
                yaw);
        }

        /// <summary>The watchman's hash idiom, on its own salt.</summary>
        public static uint CreateState(int citySeed)
        {
            unchecked
            {
                uint state = ((uint)citySeed * 2654435761u) ^
                             0x43485353u; // "CHSS"
                return state == 0u ? 0x9E3779B9u : state;
            }
        }

        public static uint NextRandomState(ref uint state)
        {
            uint value = state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value;
            return value;
        }

        /// <summary>A deterministic draw in `[-1, 1]`.</summary>
        private static float NextSigned(ref uint state)
        {
            return (NextRandomState(ref state) / (float)uint.MaxValue) *
                2f - 1f;
        }
    }
}
