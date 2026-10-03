using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityNightFixturePlanner
    {
        public const float FirstLampEdgeT = 0.28f;
        public const float SecondLampEdgeT = 0.72f;
        public const int MaximumSignalIntersections =
            CityStreetIntersectionSelector.MaximumIntersectionCount;

        private const float FixtureRoadClearance = 0.75f;
        private const float PublicSpaceFixtureClearance = 1.0f;
        // The 0.42 m square lower-pole collider has a 0.297 m half diagonal.
        // Reserve the full hero capsule and a margin beside the court route.
        private const float CourtyardRouteFixtureClearance = 0.95f;
        private const float CourtyardBuildingFixtureClearance = 0.55f;
        private const float ShiftedLampMinimumSpacing = 3f;
        private const uint LampSideSalt = 0x4C414D50u;
        private const uint SignalPhaseSalt = 0x50484153u;

        public static CityNightFixturePlan CreatePlan(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var streetLamps = new List<StreetLampDescriptor>(
                checked(layout.RoadEdges.Count * 2));
            CreateStreetLamps(layout, streetLamps);

            var trafficSignals = new List<TrafficSignalDescriptor>(
                MaximumSignalIntersections * 2);
            CreateTrafficSignals(layout, trafficSignals);

            return new CityNightFixturePlan(streetLamps, trafficSignals);
        }

        private static void CreateStreetLamps(
            CityLayout layout,
            ICollection<StreetLampDescriptor> target)
        {
            var sortedEdges = new List<RoadEdge>(layout.RoadEdges);
            sortedEdges.Sort(RoadEdge.Compare);

            for (int index = 0; index < sortedEdges.Count; index++)
            {
                RoadEdge edge = sortedEdges[index];
                if (layout.IsRiverBridgeEdge(edge))
                {
                    continue;
                }

                uint sideHash = StableHash(
                    layout.Seed,
                    edge.A.x,
                    edge.A.y,
                    edge.B.x,
                    edge.B.y,
                    LampSideSalt);
                StreetLampSide firstSide = (sideHash & 1u) == 0u
                    ? StreetLampSide.Left
                    : StreetLampSide.Right;
                StreetLampSide secondSide = firstSide == StreetLampSide.Left
                    ? StreetLampSide.Right
                    : StreetLampSide.Left;

                TryAddStreetLamp(
                    layout,
                    edge,
                    FirstLampEdgeT,
                    firstSide,
                    target);
                TryAddStreetLamp(
                    layout,
                    edge,
                    SecondLampEdgeT,
                    secondSide,
                    target);
            }
        }

        private static void TryAddStreetLamp(
            CityLayout layout,
            RoadEdge edge,
            float edgeT,
            StreetLampSide preferredSide,
            ICollection<StreetLampDescriptor> target)
        {
            StreetLampDescriptor preferred = CreateStreetLamp(
                layout,
                edge,
                edgeT,
                preferredSide);
            if (!IsFixtureBlocked(layout, preferred.Position))
            {
                target.Add(preferred);
                return;
            }

            StreetLampSide alternateSide =
                preferredSide == StreetLampSide.Left
                    ? StreetLampSide.Right
                    : StreetLampSide.Left;
            StreetLampDescriptor alternate = CreateStreetLamp(
                layout,
                edge,
                edgeT,
                alternateSide);
            if (!IsFixtureBlocked(layout, alternate.Position))
            {
                target.Add(alternate);
                return;
            }

            if (!TouchesCourtyardEdge(layout, edge)) return;
            // Keep the two ordinary lights when their original stations are
            // occupied by the court's entry or flank. Both sides are tried at
            // each bounded shift before moving farther along the same street.
            foreach (float shift in new[] { -.08f, .08f, -.16f, .16f })
                foreach (StreetLampSide side in new[] { preferredSide, alternateSide })
                {
                    StreetLampDescriptor shifted = CreateStreetLamp(layout, edge, edgeT + shift, side);
                    if (IsFixtureBlocked(layout, shifted.Position) || !IsLampSeparated(shifted.Position, target)) continue;
                    target.Add(shifted);
                    return;
                }
        }

        private static StreetLampDescriptor CreateStreetLamp(
            CityLayout layout,
            RoadEdge edge,
            float edgeT,
            StreetLampSide side)
        {
            Vector3 start = layout.GetNodeWorldPosition(edge.A);
            Vector3 end = layout.GetNodeWorldPosition(edge.B);
            Vector3 tangent = (end - start).normalized;
            Vector3 left = new Vector3(-tangent.z, 0f, tangent.x);
            float sideMultiplier = side == StreetLampSide.Left ? 1f : -1f;
            Vector3 outward = left * sideMultiplier;
            float offset = (layout.RoadWidth * 0.5f) + FixtureRoadClearance;
            Vector3 centerlinePosition = Vector3.Lerp(start, end, edgeT);
            bool courtyardEdge = TouchesCourtyardEdge(layout, edge);
            if (layout.RoadGeometry.IsCurved(edge) || courtyardEdge)
            {
                CityRoadPath path = layout.RoadGeometry.Get(edge);
                CityRoadSample sample = path.SampleDistance(path.Length * edgeT);
                centerlinePosition = new Vector3(sample.Position.x,
                    layout.ElevationPlan.SampleRoadDatum(edge, edgeT), sample.Position.y);
                outward = new Vector3(-sample.Right.x, 0f, -sample.Right.y) * sideMultiplier;
            }

            Vector3 position = centerlinePosition + (outward * offset);
            if (courtyardEdge && CityTerrainSurfacePlan.TrySampleGroundTop(layout,
                    new Vector2(position.x, position.z), out float top, out _))
                position.y = top;

            return new StreetLampDescriptor(
                edge,
                edgeT,
                side,
                position,
                -outward);
        }

        private static void CreateTrafficSignals(
            CityLayout layout,
            ICollection<TrafficSignalDescriptor> target)
        {
            IReadOnlyList<Vector2Int> selectedNodes =
                CityStreetIntersectionSelector.Select(
                    layout,
                    MaximumSignalIntersections);
            for (int index = 0; index < selectedNodes.Count; index++)
            {
                CreateTrafficSignalPair(
                    layout,
                    selectedNodes[index],
                    target);
            }
        }

        private static void CreateTrafficSignalPair(
            CityLayout layout,
            Vector2Int node,
            ICollection<TrafficSignalDescriptor> target)
        {
            Vector3 nodePosition = layout.GetNodeWorldPosition(node);
            Vector3 pairedOffset =
                CityStreetIntersectionSelector.GetPairedFixtureOffset(
                    layout,
                    node);
            Vector3 firstPosition = nodePosition + pairedOffset;
            Vector3 secondPosition = nodePosition - pairedOffset;
            float phase = HashToUnitFloat(StableHash(
                layout.Seed,
                node.x,
                node.y,
                SignalPhaseSalt));

            var first = new TrafficSignalDescriptor(
                node,
                0,
                firstPosition,
                (nodePosition - firstPosition).normalized,
                phase);
            var second = new TrafficSignalDescriptor(
                node,
                1,
                secondPosition,
                (nodePosition - secondPosition).normalized,
                phase);
            target.Add(first);
            target.Add(second);
        }

        private static bool IsFixtureBlocked(
            CityLayout layout,
            Vector3 position)
        {
            return layout.IsWater(position) ||
                   IntersectsCourtyardReservation(layout, position) ||
                   IntersectsRiverReservation(layout, position) ||
                   IntersectsDistrictPointOfInterestReservation(
                       layout,
                       position);
        }

        private static bool TouchesCourtyardEdge(CityLayout layout, RoadEdge edge)
        {
            foreach (CityCourtyardBlock block in layout.CourtyardBlocks)
                if (edge == RoadEdge.ForCellFrontage(block.Cell, Vector2Int.left) ||
                    edge == RoadEdge.ForCellFrontage(block.Cell, Vector2Int.right) ||
                    edge == RoadEdge.ForCellFrontage(block.Cell, Vector2Int.down) ||
                    edge == RoadEdge.ForCellFrontage(block.Cell, Vector2Int.up)) return true;
            return false;
        }

        private static bool IntersectsCourtyardReservation(CityLayout layout, Vector3 position)
        {
            var point = new Vector2(position.x, position.z);
            foreach (CityCourtyardBlock block in layout.CourtyardBlocks)
            {
                if (block.Route.Project(point).DistanceSquared <
                    CourtyardRouteFixtureClearance * CourtyardRouteFixtureClearance) return true;
                if (IntersectsBuilding(point, block.Primary) ||
                    (block.RearBuilding != null && IntersectsBuilding(point, block.RearBuilding))) return true;
            }
            return false;
        }

        private static bool IntersectsBuilding(Vector2 point, BuildingLot building)
        {
            foreach (Vector2[] polygon in building.CreateCollisionPolygons())
            {
                if (CityRoadPolygon.Contains(polygon, point)) return true;
                for (int i = 0; i < polygon.Length; i++)
                {
                    Vector2 a = polygon[i], delta = polygon[(i + 1) % polygon.Length] - a;
                    Vector2 nearest = a + delta * Mathf.Clamp01(Vector2.Dot(point - a, delta) /
                        Mathf.Max(.000001f, delta.sqrMagnitude));
                    if ((point - nearest).sqrMagnitude <
                        CourtyardBuildingFixtureClearance * CourtyardBuildingFixtureClearance) return true;
                }
            }
            return false;
        }

        private static bool IsLampSeparated(Vector3 position, IEnumerable<StreetLampDescriptor> lamps)
        {
            foreach (StreetLampDescriptor lamp in lamps)
                if (new Vector2(position.x - lamp.Position.x, position.z - lamp.Position.z).sqrMagnitude <
                    ShiftedLampMinimumSpacing * ShiftedLampMinimumSpacing) return false;
            return true;
        }

        private static bool IntersectsRiverReservation(
            CityLayout layout,
            Vector3 position)
        {
            if (!layout.River.IsEnabled)
            {
                return false;
            }

            Vector2 point = new Vector2(position.x, position.z);
            for (int index = 0;
                 index < layout.River.Promenades.Count;
                 index++)
            {
                if (layout.River.Promenades[index].Bounds.Contains(point))
                {
                    return true;
                }
            }

            for (int index = 0;
                 index < layout.River.Landings.Count;
                 index++)
            {
                CityRiverLandingDescriptor landing =
                    layout.River.Landings[index];
                if (landing.StairBounds.Contains(point) ||
                    landing.PlatformBounds.Contains(point))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IntersectsDistrictPointOfInterestReservation(
            CityLayout layout,
            Vector3 position)
        {
            Vector2 point = new Vector2(position.x, position.z);
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                CityDistrictPointOfInterestDescriptor pointOfInterest =
                    layout.DistrictPointsOfInterest[index];
                if (ContainsExpanded(
                        pointOfInterest.PublicBounds,
                        point,
                        PublicSpaceFixtureClearance))
                {
                    return true;
                }

                for (int accessIndex = 0;
                     accessIndex < pointOfInterest.Accesses.Count;
                     accessIndex++)
                {
                    if (ContainsExpanded(
                            pointOfInterest.Accesses[accessIndex]
                                .ApproachBounds,
                            point,
                            PublicSpaceFixtureClearance))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool ContainsExpanded(
            Rect bounds,
            Vector2 point,
            float expansion)
        {
            return point.x >= bounds.xMin - expansion &&
                   point.x <= bounds.xMax + expansion &&
                   point.y >= bounds.yMin - expansion &&
                   point.y <= bounds.yMax + expansion;
        }

        private static float HashToUnitFloat(uint hash)
        {
            return (hash >> 8) * (1f / 16777216f);
        }

        private static uint StableHash(
            int seed,
            int firstX,
            int firstY,
            int secondX,
            int secondY,
            uint salt)
        {
            uint hash = StableHash(
                unchecked((uint)seed),
                unchecked((uint)firstX));
            hash = StableHash(hash, unchecked((uint)firstY));
            hash = StableHash(hash, unchecked((uint)secondX));
            hash = StableHash(hash, unchecked((uint)secondY));
            return StableHash(hash, salt);
        }

        private static uint StableHash(
            int seed,
            int x,
            int y,
            uint salt)
        {
            uint hash = StableHash(
                unchecked((uint)seed),
                unchecked((uint)x));
            hash = StableHash(hash, unchecked((uint)y));
            return StableHash(hash, salt);
        }

        private static uint StableHash(uint first, uint second)
        {
            uint hash = first ^ 0x9E3779B9u;
            hash ^= second + 0x85EBCA6Bu + (hash << 6) + (hash >> 2);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return hash == 0u ? 0xA341316Cu : hash;
        }

    }
}
