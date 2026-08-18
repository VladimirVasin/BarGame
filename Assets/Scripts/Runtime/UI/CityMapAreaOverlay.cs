using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One boundary segment of an area, in world XZ. The map draws the
    /// segments instead of stroking every cell so a precinct reads as one
    /// plot rather than as a grid of squares.
    /// </summary>
    public readonly struct CityMapAreaEdge : IEquatable<CityMapAreaEdge>
    {
        public CityMapAreaEdge(Vector2 start, Vector2 end)
        {
            Start = start;
            End = end;
        }

        public Vector2 Start { get; }
        public Vector2 End { get; }

        public bool Equals(CityMapAreaEdge other)
        {
            return Start.Equals(other.Start) && End.Equals(other.End);
        }

        public override bool Equals(object obj)
        {
            return obj is CityMapAreaEdge other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Start.GetHashCode() * 397) ^ End.GetHashCode();
            }
        }
    }

    /// <summary>
    /// One blueprint area as the map draws and names it: the ground it
    /// actually covers, the outline of that ground, the street openings
    /// that lead into it and the name a hovering pointer reveals.
    ///
    /// The bounds are the drawn surfaces rather than raw cells, so the
    /// outline stops at the kerb exactly where the ground does.
    /// </summary>
    public sealed class CityMapAreaRegion
    {
        internal CityMapAreaRegion(
            CityAreaDefinition definition,
            IList<Rect> landBounds,
            IList<Rect> waterBounds,
            IList<CityMapAreaEdge> outline,
            IList<Rect> gates)
        {
            Definition = definition ??
                throw new ArgumentNullException(nameof(definition));
            LandBounds = new ReadOnlyCollection<Rect>(
                new List<Rect>(landBounds));
            WaterBounds = new ReadOnlyCollection<Rect>(
                new List<Rect>(waterBounds));
            Outline = new ReadOnlyCollection<CityMapAreaEdge>(
                new List<CityMapAreaEdge>(outline));
            Gates = new ReadOnlyCollection<Rect>(new List<Rect>(gates));
        }

        public CityAreaDefinition Definition { get; }
        public string AreaId => Definition.Id;
        public CityAreaFeatureKind Feature => Definition.Feature;
        public CityDistrictKind Archetype => Definition.Archetype;
        public string LocalizationKey => Definition.LocalizationKey;
        public Color MapColor => Definition.MapColor;

        /// <summary>
        /// Whether the area is ordinary built city. Urban districts carry
        /// their own buildings and streets and are only named on hover;
        /// every other feature is a precinct the map has to draw itself.
        /// </summary>
        public bool IsUrban =>
            Feature == CityAreaFeatureKind.UrbanDistrict;

        public IReadOnlyList<Rect> LandBounds { get; }
        public IReadOnlyList<Rect> WaterBounds { get; }
        public IReadOnlyList<CityMapAreaEdge> Outline { get; }

        /// <summary>
        /// The canonical street approaches into the precinct. An open area
        /// is entered only through these, so the map marks them.
        /// </summary>
        public IReadOnlyList<Rect> Gates { get; }
    }

    /// <summary>
    /// Turns a layout into the per-area presentation the city map draws.
    /// Pure and deterministic: the map builds it once when it initializes
    /// instead of walking the blueprint on every OnGUI pass.
    /// </summary>
    public static class CityMapAreaOverlayBuilder
    {
        private static readonly Vector2Int[] Neighbours =
        {
            Vector2Int.left,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.up
        };

        public static IReadOnlyList<CityMapAreaRegion> Create(
            CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var boundsByCell = new Dictionary<Vector2Int, Rect>(
                layout.Surfaces.Count);
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                // River water borrows cells outside the blueprint; the
                // areas own every other surface exactly once.
                if (surface.Kind == CitySurfaceKind.RiverWater)
                {
                    continue;
                }

                boundsByCell[surface.Cell] = surface.WorldBounds;
            }

            Dictionary<string, List<Rect>> gatesByArea =
                CollectGates(layout);
            IReadOnlyList<CityAreaPlacement> areas =
                layout.Blueprint.Areas;
            var regions = new List<CityMapAreaRegion>(areas.Count);
            var land = new List<Rect>();
            var water = new List<Rect>();
            var outline = new List<CityMapAreaEdge>();
            var noGates = new List<Rect>();
            for (int index = 0; index < areas.Count; index++)
            {
                CityAreaPlacement area = areas[index];
                land.Clear();
                water.Clear();
                outline.Clear();
                AppendArea(area, boundsByCell, land, water, outline);
                regions.Add(new CityMapAreaRegion(
                    area.Definition,
                    land,
                    water,
                    outline,
                    gatesByArea.TryGetValue(area.Id, out List<Rect> gates)
                        ? gates
                        : noGates));
            }

            return new ReadOnlyCollection<CityMapAreaRegion>(regions);
        }

        private static Dictionary<string, List<Rect>> CollectGates(
            CityLayout layout)
        {
            var gatesByArea = new Dictionary<string, List<Rect>>(
                StringComparer.Ordinal);
            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                CityOpenAreaAccessDescriptor access =
                    layout.OpenAreaAccesses[index];
                if (!gatesByArea.TryGetValue(
                        access.AreaId,
                        out List<Rect> gates))
                {
                    gates = new List<Rect>(2);
                    gatesByArea.Add(access.AreaId, gates);
                }

                gates.Add(access.ApproachBounds);
            }

            return gatesByArea;
        }

        private static void AppendArea(
            CityAreaPlacement area,
            IReadOnlyDictionary<Vector2Int, Rect> boundsByCell,
            ICollection<Rect> land,
            ICollection<Rect> water,
            ICollection<CityMapAreaEdge> outline)
        {
            for (int index = 0; index < area.Cells.Count; index++)
            {
                Vector2Int cell = area.Cells[index];
                if (!boundsByCell.TryGetValue(cell, out Rect bounds))
                {
                    continue;
                }

                if (area.GetTopology(cell) == CityCellTopologyKind.Water)
                {
                    water.Add(bounds);
                }
                else
                {
                    land.Add(bounds);
                }

                for (int side = 0; side < Neighbours.Length; side++)
                {
                    if (area.TopologyByCell.ContainsKey(
                            cell + Neighbours[side]))
                    {
                        continue;
                    }

                    outline.Add(CreateEdge(bounds, Neighbours[side]));
                }
            }
        }

        private static CityMapAreaEdge CreateEdge(
            Rect bounds,
            Vector2Int side)
        {
            if (side == Vector2Int.left)
            {
                return new CityMapAreaEdge(
                    new Vector2(bounds.xMin, bounds.yMin),
                    new Vector2(bounds.xMin, bounds.yMax));
            }

            if (side == Vector2Int.right)
            {
                return new CityMapAreaEdge(
                    new Vector2(bounds.xMax, bounds.yMin),
                    new Vector2(bounds.xMax, bounds.yMax));
            }

            if (side == Vector2Int.down)
            {
                return new CityMapAreaEdge(
                    new Vector2(bounds.xMin, bounds.yMin),
                    new Vector2(bounds.xMax, bounds.yMin));
            }

            return new CityMapAreaEdge(
                new Vector2(bounds.xMin, bounds.yMax),
                new Vector2(bounds.xMax, bounds.yMax));
        }
    }
}
