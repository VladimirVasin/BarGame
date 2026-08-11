using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Selects Road v2.1 three- or four-way junctions whose corner sidewalks
    /// can move onto non-water ground clear of buildings. The surface planner
    /// then exposes the full core and each real flush approach for a long
    /// vehicle while retaining the one-metre pedestrian route.
    /// </summary>
    public static class CityBusIntersectionSelector
    {
        public const float BuildingClearance = 0.1f;

        private static readonly Vector2Int[] CardinalDirections =
        {
            Vector2Int.left,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.up
        };

        public static IReadOnlyList<Vector2Int> Select(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var signalNodes = new HashSet<Vector2Int>(
                CityStreetIntersectionSelector.Select(layout));
            var selected = new List<Vector2Int>();
            var nodes = new List<Vector2Int>(layout.Nodes);
            nodes.Sort(CompareNodes);
            for (int index = 0; index < nodes.Count; index++)
            {
                Vector2Int node = nodes[index];
                if (!signalNodes.Contains(node) &&
                    HasTurnCapableStreetIntersection(layout, node) &&
                    HasSafeCornerSetbacks(layout, node))
                {
                    selected.Add(node);
                }
            }

            return new ReadOnlyCollection<Vector2Int>(selected);
        }

        public static float GetCornerCenterOffset(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            return (layout.RoadWidth * 0.5f) +
                   (CityStreetSurfacePlanner.SidewalkWidth * 0.5f);
        }

        private static bool HasTurnCapableStreetIntersection(
            CityLayout layout,
            Vector2Int node)
        {
            var directions = new HashSet<Vector2Int>();
            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                if (!edge.Contains(node))
                {
                    continue;
                }

                if (layout.GetPathKind(edge) != CityPathKind.Street)
                {
                    return false;
                }

                directions.Add(edge.Other(node) - node);
            }

            if (directions.Count < 3 ||
                directions.Count > CardinalDirections.Length)
            {
                return false;
            }

            foreach (Vector2Int direction in directions)
            {
                bool isCardinal = false;
                for (int index = 0;
                     index < CardinalDirections.Length;
                     index++)
                {
                    if (direction == CardinalDirections[index])
                    {
                        isCardinal = true;
                        break;
                    }
                }

                if (!isCardinal)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasSafeCornerSetbacks(
            CityLayout layout,
            Vector2Int node)
        {
            Vector3 center = layout.GetNodeWorldPosition(node);
            float offset = GetCornerCenterOffset(layout);
            float sidewalkWidth =
                CityStreetSurfacePlanner.SidewalkWidth;
            for (int xSign = -1; xSign <= 1; xSign += 2)
            {
                for (int zSign = -1; zSign <= 1; zSign += 2)
                {
                    Rect corner = Rect.MinMaxRect(
                        center.x + (xSign * offset) -
                            (sidewalkWidth * 0.5f),
                        center.z + (zSign * offset) -
                            (sidewalkWidth * 0.5f),
                        center.x + (xSign * offset) +
                            (sidewalkWidth * 0.5f),
                        center.z + (zSign * offset) +
                            (sidewalkWidth * 0.5f));
                    if (!HasSupportingGroundSurface(layout, corner) ||
                        OverlapsAnyBuilding(layout, corner))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool HasSupportingGroundSurface(
            CityLayout layout,
            Rect corner)
        {
            for (int surfaceIndex = 0;
                 surfaceIndex < layout.Surfaces.Count;
                 surfaceIndex++)
            {
                CitySurfaceDescriptor surface =
                    layout.Surfaces[surfaceIndex];
                if (surface.IsWater ||
                    !Contains(surface.WorldBounds, corner))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool OverlapsAnyBuilding(
            CityLayout layout,
            Rect corner)
        {
            for (int lotIndex = 0;
                 lotIndex < layout.BuildingLots.Count;
                 lotIndex++)
            {
                BuildingLot lot = layout.BuildingLots[lotIndex];
                if (lot.HasBuilding && OverlapsBuilding(corner, lot))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool OverlapsBuilding(Rect corner, BuildingLot lot)
        {
            Bounds bounds = lot.WorldBounds;
            Rect building = Rect.MinMaxRect(
                bounds.min.x - BuildingClearance,
                bounds.min.z - BuildingClearance,
                bounds.max.x + BuildingClearance,
                bounds.max.z + BuildingClearance);
            return HasPositiveOverlap(corner, building);
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin &&
                   inner.xMax <= outer.xMax &&
                   inner.yMin >= outer.yMin &&
                   inner.yMax <= outer.yMax;
        }

        private static bool HasPositiveOverlap(Rect first, Rect second)
        {
            return Mathf.Min(first.xMax, second.xMax) >
                       Mathf.Max(first.xMin, second.xMin) &&
                   Mathf.Min(first.yMax, second.yMax) >
                       Mathf.Max(first.yMin, second.yMin);
        }

        private static int CompareNodes(Vector2Int left, Vector2Int right)
        {
            int xComparison = left.x.CompareTo(right.x);
            return xComparison != 0
                ? xComparison
                : left.y.CompareTo(right.y);
        }
    }
}
