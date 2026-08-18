using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The handful of numbers that both the drawn board and anything
    /// standing on it have to agree about, in the chess recipe's own
    /// local frame: `x` along the decoration `Tangent`, `z` along its
    /// `Forward`, `y` above the decoration origin.
    ///
    /// It exists because the board stopped being decoration the moment
    /// it got men on it. Everything else `CityDecorationWorldBuilder`
    /// draws is allowed to be approximately the size it looks; a chess
    /// board is a lattice a second system has to land on to the
    /// millimetre, and two copies of `0.15` in two files is how a set
    /// ends up half a square off its own squares.
    ///
    /// Pure and Unity-free apart from the vector type, so the placement
    /// of an entire set can be proved without building a city.
    /// </summary>
    public static class CityChessBoardGeometry
    {
        /// <summary>Each table's offset from the middle of the set,
        /// along the recipe's tangent. The set has two.</summary>
        public const float TableOffsetMeters = 1.85f;

        /// <summary>Middle of one bench, along the recipe's forward.
        /// The set has one on each side of each table.</summary>
        public const float BenchCenterZMeters = 1.10f;

        /// <summary>The plank's length, along the recipe's tangent.</summary>
        public const float BenchWidthMeters = 1.12f;

        /// <summary>And its width, along the forward.</summary>
        public const float BenchDepthMeters = 0.42f;

        /// <summary>Top of the plank, over the decoration origin: what a
        /// sitter's weight actually rests on.</summary>
        public const float BenchSeatTopY = 0.54f;

        /// <summary>
        /// Half the logical blocking box one table stands inside, along
        /// the tangent and the forward. The proxy is deliberately one
        /// box per table rather than a table and two planks, so the
        /// benches are solid to a walker and the only way onto one is
        /// past its end. Anything that has to route a body around this
        /// set reads the extents from here.
        /// </summary>
        public const float TableBlockHalfWidthMeters = 0.75f;

        public const float TableBlockHalfDepthMeters = 1.35f;

        public const int SquaresPerSide = 8;
        public const float SquareSizeMeters = 0.15f;
        public const float FieldSizeMeters =
            SquareSizeMeters * SquaresPerSide;

        /// <summary>Top of the stone slab the board is laid on.</summary>
        public const float TableTopY = 0.90f;

        /// <summary>Top of the light plate — the playing surface for a
        /// man standing on a light square.</summary>
        public const float LightTopY = TableTopY + 0.035f;

        /// <summary>How far a dark inlay stands over the light plate.
        /// Small, but a piece that ignored it would float on half the
        /// board and sink on the other half.</summary>
        public const float DarkSquareProudMeters = 0.005f;

        /// <summary>
        /// Which squares are dark. Not a free choice: both planks face
        /// along `+Forward` and `Tangent` runs to the left of a man
        /// facing that way, so his near-right corner is `(0, 0)` and the
        /// man opposite has `(7, 7)`. Both must be light, and on an even
        /// board they share a parity, so the dark squares are the odd
        /// ones. That also puts `a1` dark and the white queen on a light
        /// `d1`, which is the same rule stated the way a player states
        /// it.
        /// </summary>
        public static bool IsDarkSquare(int file, int rank)
        {
            return ((file + rank) & 1) == 1;
        }

        /// <summary>
        /// The middle of one square, in the recipe's local frame, with
        /// `y` at the surface a man on that square actually stands on.
        /// </summary>
        /// <param name="table">`-1` or `+1`: which of the two tables.</param>
        public static Vector3 SquareCenterLocal(
            int table,
            int file,
            int rank)
        {
            const float edge = (SquaresPerSide - 1) * 0.5f;
            return new Vector3(
                table * TableOffsetMeters +
                (file - edge) * SquareSizeMeters,
                SquareTopY(file, rank),
                (rank - edge) * SquareSizeMeters);
        }

        public static float SquareTopY(int file, int rank)
        {
            return IsDarkSquare(file, rank)
                ? LightTopY + DarkSquareProudMeters
                : LightTopY;
        }

        /// <summary>
        /// The same point in world space, given the recipe frame the
        /// decoration was placed in.
        /// </summary>
        public static Vector3 SquareCenterWorld(
            Vector3 origin,
            Vector3 tangent,
            Vector3 forward,
            int table,
            int file,
            int rank)
        {
            Vector3 local = SquareCenterLocal(table, file, rank);
            return origin +
                tangent * local.x +
                Vector3.up * local.y +
                forward * local.z;
        }

        public static bool IsOnBoard(int file, int rank)
        {
            return file >= 0 &&
                   file < SquaresPerSide &&
                   rank >= 0 &&
                   rank < SquaresPerSide;
        }
    }
}
