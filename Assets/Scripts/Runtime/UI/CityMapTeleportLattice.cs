using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The ground one area will accept an arrival on.
    ///
    /// The map owns one of these per area rather than one for the world,
    /// because the two areas are separately loaded scenes that share nothing
    /// but the coordinate system. Asking the city's walkable mask about a
    /// mountain-road coordinate does return an answer - the mountain route
    /// starts at the world origin, right on top of the city - and that
    /// answer is nonsense.
    /// </summary>
    public interface ICityMapTeleportGround
    {
        GameAreaId Area { get; }

        /// <summary>
        /// Where a capsule can stand at, or nearest to, this chart
        /// coordinate, height included. It has to answer for ground the
        /// chart merely draws as well as for ground a marker names, because
        /// this is what decides whether a lattice square is a destination
        /// at all.
        /// </summary>
        bool TryResolveStandingPosition(
            Vector2 worldXZ,
            out Vector3 standingPosition);

        /// <summary>
        /// Holds an authored arrival to ground the player can stand on. The
        /// caller already carries a height it believes in, so this only has
        /// to correct the ones the mask refuses.
        /// </summary>
        bool TryClampArrival(Vector3 arrival, out Vector3 destination);
    }

    /// <summary>
    /// One square of the map's teleport lattice: the ground it covers, and
    /// the exact point inside it the player is put down on. A square is only
    /// ever built for ground that answered, so a square that exists is a
    /// square you can go to.
    /// </summary>
    public readonly struct CityMapTeleportSquare
    {
        internal CityMapTeleportSquare(
            Vector2Int cell,
            Rect worldBounds,
            Vector3 standingPosition)
        {
            Cell = cell;
            WorldBounds = worldBounds;
            StandingPosition = standingPosition;
        }

        public Vector2Int Cell { get; }
        public Rect WorldBounds { get; }
        public Vector3 StandingPosition { get; }
    }

    /// <summary>
    /// An even square lattice laid over one area's chart, carrying only the
    /// squares its ground answered for.
    ///
    /// The lattice is what makes the whole map a destination instead of only
    /// the places a marker happens to name - streets, shoulders, the
    /// switchback shelves and the plateau at the top of the road all have a
    /// square. Named markers stay in front of it: a square is the answer
    /// when nothing else claims the pointer.
    /// </summary>
    public sealed class CityMapTeleportLattice
    {
        private static readonly IReadOnlyList<CityMapTeleportSquare>
            NoSquares = new ReadOnlyCollection<CityMapTeleportSquare>(
                new List<CityMapTeleportSquare>());

        private readonly Dictionary<Vector2Int, int> indexByCell;

        internal CityMapTeleportLattice(
            GameAreaId area,
            Vector2 originAnchor,
            float cellSize,
            Vector2Int minimumCell,
            Vector2Int maximumCell,
            IList<CityMapTeleportSquare> squares)
        {
            Area = area;
            OriginAnchor = originAnchor;
            CellSize = cellSize;
            MinimumCell = minimumCell;
            MaximumCell = maximumCell;
            Squares = new ReadOnlyCollection<CityMapTeleportSquare>(
                new List<CityMapTeleportSquare>(squares));
            indexByCell = new Dictionary<Vector2Int, int>(Squares.Count);
            for (int index = 0; index < Squares.Count; index++)
            {
                indexByCell[Squares[index].Cell] = index;
            }
        }

        private CityMapTeleportLattice()
        {
            Area = GameAreaId.City;
            OriginAnchor = Vector2.zero;
            CellSize = 1f;
            MinimumCell = Vector2Int.zero;
            MaximumCell = new Vector2Int(-1, -1);
            Squares = NoSquares;
            indexByCell = new Dictionary<Vector2Int, int>();
        }

        public static CityMapTeleportLattice Empty { get; } =
            new CityMapTeleportLattice();

        public GameAreaId Area { get; }

        /// <summary>
        /// The world point every lattice line is measured from. The city
        /// anchors on its own world origin, so one square is one city cell
        /// and the lattice never cuts a block in half; the mountain road
        /// anchors on the tunnel portal.
        /// </summary>
        public Vector2 OriginAnchor { get; }

        public float CellSize { get; }
        public Vector2Int MinimumCell { get; }
        public Vector2Int MaximumCell { get; }
        public IReadOnlyList<CityMapTeleportSquare> Squares { get; }
        public bool IsEmpty => Squares.Count == 0;

        public Vector2Int GetCell(Vector2 worldXZ)
        {
            return new Vector2Int(
                Mathf.FloorToInt((worldXZ.x - OriginAnchor.x) / CellSize),
                Mathf.FloorToInt((worldXZ.y - OriginAnchor.y) / CellSize));
        }

        public Rect GetCellWorldBounds(Vector2Int cell)
        {
            return new Rect(
                OriginAnchor.x + cell.x * CellSize,
                OriginAnchor.y + cell.y * CellSize,
                CellSize,
                CellSize);
        }

        public bool TryGetSquareIndex(Vector2Int cell, out int index)
        {
            return indexByCell.TryGetValue(cell, out index);
        }

        public bool TryGetSquareIndexAt(Vector2 worldXZ, out int index)
        {
            return TryGetSquareIndex(GetCell(worldXZ), out index);
        }

        public bool TryGetSquare(
            Vector2Int cell,
            out CityMapTeleportSquare square)
        {
            if (!TryGetSquareIndex(cell, out int index))
            {
                square = default;
                return false;
            }

            square = Squares[index];
            return true;
        }
    }

    public static class CityMapTeleportLatticeBuilder
    {
        public const float MinimumCellSize = 0.5f;
        public const int MaximumCellCount = 20000;

        /// <summary>
        /// Where inside a square the ground is asked, as fractions of the
        /// square. The centre first, then close to each edge midpoint, then
        /// the quarters - because a road is a strip, and a lattice aligned
        /// to city cells puts the carriageway on the SEAM between two
        /// squares rather than through the middle of one. Probing the centre
        /// alone would leave every street off the map and put every block
        /// square inside its own building.
        /// </summary>
        private static readonly Vector2[] ProbeOffsets =
        {
            new Vector2(0f, 0f),
            new Vector2(-0.45f, 0f),
            new Vector2(0.45f, 0f),
            new Vector2(0f, -0.45f),
            new Vector2(0f, 0.45f),
            new Vector2(-0.25f, -0.25f),
            new Vector2(0.25f, -0.25f),
            new Vector2(-0.25f, 0.25f),
            new Vector2(0.25f, 0.25f)
        };

        public static CityMapTeleportLattice Create(
            Rect chartBounds,
            Vector2 originAnchor,
            float cellSize,
            ICityMapTeleportGround ground)
        {
            if (ground == null)
            {
                throw new ArgumentNullException(nameof(ground));
            }

            if (!IsFinite(originAnchor))
            {
                throw new ArgumentOutOfRangeException(nameof(originAnchor));
            }

            if (!IsFinite(cellSize) || cellSize < MinimumCellSize)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize));
            }

            if (!IsFinite(chartBounds) ||
                chartBounds.width <= 0f ||
                chartBounds.height <= 0f)
            {
                return CityMapTeleportLattice.Empty;
            }

            var minimumCell = new Vector2Int(
                Mathf.FloorToInt(
                    (chartBounds.xMin - originAnchor.x) / cellSize),
                Mathf.FloorToInt(
                    (chartBounds.yMin - originAnchor.y) / cellSize));
            var maximumCell = new Vector2Int(
                Mathf.CeilToInt(
                    (chartBounds.xMax - originAnchor.x) / cellSize) - 1,
                Mathf.CeilToInt(
                    (chartBounds.yMax - originAnchor.y) / cellSize) - 1);
            maximumCell = new Vector2Int(
                Mathf.Max(maximumCell.x, minimumCell.x),
                Mathf.Max(maximumCell.y, minimumCell.y));
            long columns = (long)maximumCell.x - minimumCell.x + 1L;
            long rows = (long)maximumCell.y - minimumCell.y + 1L;
            if (columns * rows > MaximumCellCount)
            {
                return CityMapTeleportLattice.Empty;
            }

            var squares = new List<CityMapTeleportSquare>(
                (int)Math.Min(columns * rows, 4096L));
            for (int cellZ = minimumCell.y; cellZ <= maximumCell.y; cellZ++)
            {
                for (int cellX = minimumCell.x;
                     cellX <= maximumCell.x;
                     cellX++)
                {
                    var cell = new Vector2Int(cellX, cellZ);
                    var bounds = new Rect(
                        originAnchor.x + cellX * cellSize,
                        originAnchor.y + cellZ * cellSize,
                        cellSize,
                        cellSize);
                    if (TryResolveSquare(
                            ground,
                            cell,
                            bounds,
                            out CityMapTeleportSquare square))
                    {
                        squares.Add(square);
                    }
                }
            }

            return new CityMapTeleportLattice(
                ground.Area,
                originAnchor,
                cellSize,
                minimumCell,
                maximumCell,
                squares);
        }

        /// <summary>
        /// A square is a destination when some point inside it is ground the
        /// player can stand on. The answer nearest the middle wins, so a
        /// square holding both a pavement and a strip of road puts the hero
        /// down on whichever of them sits more centrally rather than on
        /// whichever happened to be probed first.
        /// </summary>
        private static bool TryResolveSquare(
            ICityMapTeleportGround ground,
            Vector2Int cell,
            Rect bounds,
            out CityMapTeleportSquare square)
        {
            Vector2 center = bounds.center;
            bool found = false;
            float bestDistance = float.PositiveInfinity;
            Vector3 best = default;
            for (int index = 0; index < ProbeOffsets.Length; index++)
            {
                Vector2 probe = center +
                                ProbeOffsets[index] * bounds.width;
                if (!ground.TryResolveStandingPosition(
                        probe,
                        out Vector3 standing) ||
                    !IsFinite(standing))
                {
                    continue;
                }

                // The ground answers with the NEAREST place it will accept,
                // which for a probe over rock or roof is somewhere else
                // entirely. Only an answer that stayed inside this square
                // makes this square a destination - otherwise the square
                // would quietly teleport the player into its neighbour.
                var landing = new Vector2(standing.x, standing.z);
                if (!bounds.Contains(landing))
                {
                    continue;
                }

                float distance = (landing - center).sqrMagnitude;
                if (found && distance >= bestDistance)
                {
                    continue;
                }

                found = true;
                bestDistance = distance;
                best = standing;
            }

            square = found
                ? new CityMapTeleportSquare(cell, bounds, best)
                : default;
            return found;
        }

        private static bool IsFinite(Rect value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.width) &&
                   IsFinite(value.height);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
