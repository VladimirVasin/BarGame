using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static class CityDistrictPointOfInterestPlanner
    {
        private const uint PrimaryLandmarkLotSalt = 0x4C4D4C54u;
        private const uint DistrictPointLotSalt = 0x44504F49u;

        private static readonly Vector2Int[] CardinalDirections =
        {
            Vector2Int.down,
            Vector2Int.right,
            Vector2Int.up,
            Vector2Int.left
        };

        internal static void Create(
            CityGenerationSettings settings,
            int seed,
            Vector3 origin,
            IReadOnlyCollection<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds,
            ISet<int> barLots,
            int homeLotIndex,
            out Dictionary<CityDistrictKind, Vector2Int>
                primaryLandmarkCells,
            out Dictionary<CityDistrictKind, int> districtPointLots,
            out List<CityDistrictPointOfInterestDescriptor> descriptors)
        {
            primaryLandmarkCells = SelectPrimaryLandmarkCells(
                settings,
                seed,
                barLots,
                homeLotIndex);
            districtPointLots = SelectDistrictPointOfInterestLots(
                settings,
                seed,
                roads,
                pathKinds,
                barLots,
                homeLotIndex,
                primaryLandmarkCells);
            descriptors = CreateDescriptors(
                settings,
                seed,
                origin,
                roads,
                pathKinds,
                districtPointLots);
        }

        private static Dictionary<CityDistrictKind, Vector2Int>
            SelectPrimaryLandmarkCells(
                CityGenerationSettings settings,
                int seed,
                ISet<int> barLots,
                int homeLotIndex)
        {
            var result =
                new Dictionary<CityDistrictKind, Vector2Int>();
            for (int districtIndex = 0;
                 districtIndex < CityLayoutGenerator.UrbanDistricts.Count;
                 districtIndex++)
            {
                CityDistrictKind district =
                    CityLayoutGenerator.UrbanDistricts[districtIndex];
                bool found = false;
                int bestPreference = int.MaxValue;
                uint bestRank = uint.MaxValue;
                Vector2Int bestCell = default;
                for (int z = 0; z < settings.BlocksZ; z++)
                {
                    for (int x = 0; x < settings.BlocksX; x++)
                    {
                        var cell = new Vector2Int(x, z);
                        if (settings.IsParkCell(cell) ||
                            CityLayoutGenerator.ResolveDistrict(
                                settings,
                                cell) != district)
                        {
                            continue;
                        }

                        int lotIndex = CityLayoutGenerator.ToLotIndex(
                            x,
                            z,
                            settings.BlocksX);
                        int preference =
                            barLots.Contains(lotIndex) ||
                            lotIndex == homeLotIndex
                                ? 1
                                : 0;
                        uint rank = CityLayoutGenerator.StableHash(
                            seed,
                            x,
                            z,
                            (uint)district,
                            PrimaryLandmarkLotSalt);
                        if (!found ||
                            preference < bestPreference ||
                            (preference == bestPreference &&
                             rank < bestRank))
                        {
                            found = true;
                            bestPreference = preference;
                            bestRank = rank;
                            bestCell = cell;
                        }
                    }
                }

                if (found)
                {
                    result.Add(district, bestCell);
                }
            }

            return result;
        }

        private static Dictionary<CityDistrictKind, int>
            SelectDistrictPointOfInterestLots(
                CityGenerationSettings settings,
                int seed,
                IReadOnlyCollection<RoadEdge> roads,
                IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds,
                ISet<int> barLots,
                int homeLotIndex,
                IReadOnlyDictionary<CityDistrictKind, Vector2Int>
                    primaryLandmarkCells)
        {
            var roadSet = new HashSet<RoadEdge>(roads);
            var result = new Dictionary<CityDistrictKind, int>();
            if (Mathf.Min(
                    settings.BlockWidth,
                    settings.BlockDepth) <
                CityLayoutGenerator.MinimumDistrictPointLotDimension)
            {
                return result;
            }

            for (int districtIndex = 0;
                 districtIndex < CityLayoutGenerator.UrbanDistricts.Count;
                 districtIndex++)
            {
                CityDistrictKind district =
                    CityLayoutGenerator.UrbanDistricts[districtIndex];
                if (!primaryLandmarkCells.TryGetValue(
                        district,
                        out Vector2Int primaryCell))
                {
                    continue;
                }

                bool found = false;
                int bestLotIndex = -1;
                int bestAccessCount = -1;
                int bestSeparation = -1;
                uint bestRank = uint.MaxValue;
                for (int z = 0; z < settings.BlocksZ; z++)
                {
                    for (int x = 0; x < settings.BlocksX; x++)
                    {
                        var cell = new Vector2Int(x, z);
                        int lotIndex = CityLayoutGenerator.ToLotIndex(
                            x,
                            z,
                            settings.BlocksX);
                        if (settings.IsParkCell(cell) ||
                            CityLayoutGenerator.ResolveDistrict(
                                settings,
                                cell) != district ||
                            barLots.Contains(lotIndex) ||
                            lotIndex == homeLotIndex ||
                            cell == primaryCell)
                        {
                            continue;
                        }

                        int accessCount = CountStreetSides(
                            cell,
                            roadSet,
                            pathKinds);
                        if (accessCount == 0)
                        {
                            continue;
                        }

                        int deltaX = cell.x - primaryCell.x;
                        int deltaZ = cell.y - primaryCell.y;
                        int separation =
                            (deltaX * deltaX) + (deltaZ * deltaZ);
                        uint rank = CityLayoutGenerator.StableHash(
                            seed,
                            x,
                            z,
                            (uint)district,
                            DistrictPointLotSalt);
                        if (!found ||
                            accessCount > bestAccessCount ||
                            (accessCount == bestAccessCount &&
                             separation > bestSeparation) ||
                            (accessCount == bestAccessCount &&
                             separation == bestSeparation &&
                             rank < bestRank))
                        {
                            found = true;
                            bestLotIndex = lotIndex;
                            bestAccessCount = accessCount;
                            bestSeparation = separation;
                            bestRank = rank;
                        }
                    }
                }

                if (found)
                {
                    result.Add(district, bestLotIndex);
                }
            }

            return result;
        }

        private static List<CityDistrictPointOfInterestDescriptor>
            CreateDescriptors(
                CityGenerationSettings settings,
                int seed,
                Vector3 origin,
                IReadOnlyCollection<RoadEdge> roads,
                IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds,
                IReadOnlyDictionary<CityDistrictKind, int>
                    districtPointLots)
        {
            var roadSet = new HashSet<RoadEdge>(roads);
            var result =
                new List<CityDistrictPointOfInterestDescriptor>(
                    districtPointLots.Count);
            for (int districtIndex = 0;
                 districtIndex < CityLayoutGenerator.UrbanDistricts.Count;
                 districtIndex++)
            {
                CityDistrictKind district =
                    CityLayoutGenerator.UrbanDistricts[districtIndex];
                if (!districtPointLots.TryGetValue(
                        district,
                        out int lotIndex))
                {
                    continue;
                }

                var cell = new Vector2Int(
                    lotIndex % settings.BlocksX,
                    lotIndex / settings.BlocksX);
                Vector3 center = CityLayoutGenerator.GetLotCenter(
                    settings,
                    origin,
                    cell);
                Rect publicBounds = Rect.MinMaxRect(
                    center.x - (settings.BlockWidth * 0.5f),
                    center.z - (settings.BlockDepth * 0.5f),
                    center.x + (settings.BlockWidth * 0.5f),
                    center.z + (settings.BlockDepth * 0.5f));
                string id =
                    $"city-poi-{unchecked((uint)seed):x8}-" +
                    $"district-{(int)district:D2}";
                var accesses =
                    new List<
                        CityDistrictPointOfInterestAccessDescriptor>(4);
                for (int directionIndex = 0;
                     directionIndex < CardinalDirections.Length;
                     directionIndex++)
                {
                    Vector2Int direction =
                        CardinalDirections[directionIndex];
                    RoadEdge frontage =
                        RoadEdge.ForCellFrontage(cell, direction);
                    if (!roadSet.Contains(frontage) ||
                        !pathKinds.TryGetValue(
                            frontage,
                            out CityPathKind pathKind) ||
                        pathKind != CityPathKind.Street)
                    {
                        continue;
                    }

                    float sideDistance = direction.x != 0
                        ? settings.BlockWidth * 0.5f
                        : settings.BlockDepth * 0.5f;
                    Vector3 accessCenter = center + new Vector3(
                        direction.x * sideDistance,
                        0f,
                        direction.y * sideDistance);
                    float fullOpenWidth = direction.x != 0
                        ? settings.BlockDepth
                        : settings.BlockWidth;
                    float approachWidth = Mathf.Min(
                        CityLayoutGenerator.DistrictPointApproachWidth,
                        fullOpenWidth);
                    float perpendicularSize = direction.x != 0
                        ? settings.BlockWidth
                        : settings.BlockDepth;
                    float approachDepth = Mathf.Min(
                        4f,
                        perpendicularSize);
                    Rect approachBounds = direction.x != 0
                        ? new Rect(
                            accessCenter.x - (approachDepth * 0.5f),
                            accessCenter.z - (approachWidth * 0.5f),
                            approachDepth,
                            approachWidth)
                        : new Rect(
                            accessCenter.x - (approachWidth * 0.5f),
                            accessCenter.z - (approachDepth * 0.5f),
                            approachWidth,
                            approachDepth);
                    Vector3 outward = new Vector3(
                        -direction.x,
                        0f,
                        -direction.y);
                    accesses.Add(
                        new CityDistrictPointOfInterestAccessDescriptor(
                            $"{id}-access-{ResolveDirectionId(direction)}",
                            direction,
                            accessCenter,
                            outward,
                            fullOpenWidth,
                            approachBounds,
                            frontage));
                }

                result.Add(
                    new CityDistrictPointOfInterestDescriptor(
                        id,
                        district,
                        ResolveKind(district),
                        cell,
                        center,
                        publicBounds,
                        accesses));
            }

            return result;
        }

        private static int CountStreetSides(
            Vector2Int cell,
            ISet<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds)
        {
            int count = 0;
            for (int index = 0; index < CardinalDirections.Length; index++)
            {
                RoadEdge edge = RoadEdge.ForCellFrontage(
                    cell,
                    CardinalDirections[index]);
                if (roads.Contains(edge) &&
                    pathKinds.TryGetValue(edge, out CityPathKind kind) &&
                    kind == CityPathKind.Street)
                {
                    count++;
                }
            }

            return count;
        }

        private static CityDistrictPointOfInterestKind ResolveKind(
            CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    return CityDistrictPointOfInterestKind
                        .OldTownWaterworksCourt;
                case CityDistrictKind.Residential:
                    return CityDistrictPointOfInterestKind
                        .ResidentialDryingYard;
                case CityDistrictKind.Industrial:
                    return CityDistrictPointOfInterestKind
                        .IndustrialWeighbridge;
                case CityDistrictKind.Nightlife:
                    return CityDistrictPointOfInterestKind
                        .NightlifeLastRouteIsland;
                default:
                    throw new InvalidOperationException(
                        $"District '{district}' cannot own a district point " +
                        "of interest.");
            }
        }

        private static string ResolveDirectionId(Vector2Int direction)
        {
            if (direction == Vector2Int.down)
            {
                return "south";
            }

            if (direction == Vector2Int.right)
            {
                return "east";
            }

            if (direction == Vector2Int.up)
            {
                return "north";
            }

            if (direction == Vector2Int.left)
            {
                return "west";
            }

            throw new ArgumentException(
                "Direction must be cardinal.",
                nameof(direction));
        }
    }
}
