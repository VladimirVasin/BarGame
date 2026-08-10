using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityPedestrianPlanner
    {
        public const int TargetPedestrianCount = 12;
        public const int MaximumPedestrianCount = 24;
        public const int PaletteVariantCount = 4;
        public const float AgentRadius = 0.35f;
        public const float SidewalkWaypointHeight =
            CityStreetSurfacePlanner.SidewalkTop -
            CityPedestrianActor.StreetSurfaceHeight;
        public const float IntersectionClearanceMargin = 0.25f;
        public const float MinimumRouteLength = 5f;
        public const float MinimumSpeed = 1f;
        public const float MaximumSpeed = 1.3f;
        public const float MinimumAnimationSpeed = 0.88f;
        public const float MaximumAnimationSpeed = 0.94f;

        private const float MinimumCarriagewayWidth = 0.0001f;
        private const int RouteSafetySamples = 8;
        private const uint CandidateRankSalt = 0x52414E4Bu;
        private const uint RouteSideSalt = 0x53494445u;
        private const uint SpeedSalt = 0x53504545u;
        private const uint AnimationSpeedSalt = 0x414E5350u;
        private const uint AnimationPhaseSalt = 0x50484153u;
        private const uint PaletteSalt = 0x50414C45u;
        private const uint DirectionSalt = 0x44495245u;
        private const uint BehaviorSalt = 0x42454856u;

        public static CityPedestrianPlan Create(
            CityLayout layout,
            int populationSeed,
            int desiredCount = TargetPedestrianCount)
        {
            return Create(
                layout,
                populationSeed,
                null,
                desiredCount);
        }

        public static CityPedestrianPlan Create(
            CityLayout layout,
            int populationSeed,
            CityStreetSurfacePlan streetSurfacePlan,
            int desiredCount = TargetPedestrianCount)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            layout.ValidateOrThrow();
            int boundedDesiredCount = Math.Max(
                0,
                Math.Min(MaximumPedestrianCount, desiredCount));
            uint stableSeed = CityPedestrianStableHash.Combine(
                CityPedestrianStableHash.Combine(
                    unchecked((uint)layout.Seed),
                    unchecked((uint)populationSeed)),
                CityPedestrianStableHash.String(layout.BlueprintId));
            var definitions = new List<CityPedestrianDefinition>(
                boundedDesiredCount);

            if (boundedDesiredCount > 0 &&
                CanFitSidewalkRoute(layout))
            {
                Dictionary<RoadEdge, int> attractionScores =
                    CreateAttractionScores(layout);
                List<RouteCandidate> candidates = CreateCandidates(
                    layout,
                    stableSeed,
                    attractionScores);
                candidates.Sort(CompareCandidates);
                if (streetSurfacePlan == null)
                {
                    streetSurfacePlan =
                        CityStreetSurfacePlanner.Create(layout);
                }

                RoadWalkableArea sidewalkArea =
                    CreateSidewalkWalkableArea(streetSurfacePlan);

                for (int index = 0;
                     index < candidates.Count &&
                     definitions.Count < boundedDesiredCount;
                     index++)
                {
                    if (TryCreateDefinition(
                            layout,
                            sidewalkArea,
                            stableSeed,
                            candidates[index].Edge,
                            out CityPedestrianDefinition definition))
                    {
                        definitions.Add(definition);
                    }
                }
            }

            definitions.Sort(CompareDefinitions);
            return new CityPedestrianPlan(
                layout.Seed,
                populationSeed,
                stableSeed,
                boundedDesiredCount,
                AgentRadius,
                definitions);
        }

        public static float GetIntersectionClearance(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            return (layout.RoadWidth * 0.5f) +
                   AgentRadius +
                   IntersectionClearanceMargin;
        }

        public static float GetSidewalkCenterOffset(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            return (layout.RoadWidth * 0.5f) -
                   (CityStreetSurfacePlanner.SidewalkWidth * 0.5f);
        }

        public static RoadWalkableArea CreateSidewalkWalkableArea(
            CityStreetSurfacePlan streetSurfacePlan)
        {
            if (streetSurfacePlan == null)
            {
                throw new ArgumentNullException(nameof(streetSurfacePlan));
            }

            return new RoadWalkableArea(
                streetSurfacePlan.SidewalkWalkableRectangles);
        }

        private static Dictionary<RoadEdge, int> CreateAttractionScores(
            CityLayout layout)
        {
            var scores = new Dictionary<RoadEdge, int>();
            for (int index = 0; index < layout.BuildingLots.Count; index++)
            {
                BuildingLot lot = layout.BuildingLots[index];
                if (!lot.IsBar &&
                    !lot.IsPlayerHome &&
                    !lot.IsSupermarket)
                {
                    continue;
                }

                if (!layout.TryGetFrontageEdge(lot, out RoadEdge edge))
                {
                    continue;
                }

                int score = lot.IsPlayerHome
                    ? 12
                    : lot.IsSupermarket
                        ? 11
                        : 9;
                AddAttractionScore(layout, scores, edge, score);
            }

            for (int pointIndex = 0;
                 pointIndex < layout.DistrictPointsOfInterest.Count;
                 pointIndex++)
            {
                CityDistrictPointOfInterestDescriptor point =
                    layout.DistrictPointsOfInterest[pointIndex];
                for (int accessIndex = 0;
                     accessIndex < point.Accesses.Count;
                     accessIndex++)
                {
                    AddAttractionScore(
                        layout,
                        scores,
                        point.Accesses[accessIndex].FrontageEdge,
                        6);
                }
            }

            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                AddAttractionScore(
                    layout,
                    scores,
                    layout.OpenAreaAccesses[index].FrontageEdge,
                    4);
            }

            for (int index = 0; index < layout.Park.Gates.Count; index++)
            {
                if (TryFindNearestStreetEdge(
                        layout,
                        layout.Park.Gates[index].Center,
                        out RoadEdge edge))
                {
                    AddAttractionScore(layout, scores, edge, 5);
                }
            }

            return scores;
        }

        private static void AddAttractionScore(
            CityLayout layout,
            IDictionary<RoadEdge, int> scores,
            RoadEdge edge,
            int score)
        {
            if (!layout.HasRoad(edge) ||
                layout.GetPathKind(edge) != CityPathKind.Street)
            {
                return;
            }

            scores.TryGetValue(edge, out int current);
            scores[edge] = current + score;
        }

        private static bool TryFindNearestStreetEdge(
            CityLayout layout,
            Vector3 position,
            out RoadEdge nearest)
        {
            nearest = default;
            float nearestDistance = float.PositiveInfinity;
            bool found = false;
            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                if (layout.GetPathKind(edge) != CityPathKind.Street)
                {
                    continue;
                }

                Vector3 start = layout.GetNodeWorldPosition(edge.A);
                Vector3 end = layout.GetNodeWorldPosition(edge.B);
                float distance = SquaredDistanceToSegment(
                    position,
                    start,
                    end);
                if (distance < nearestDistance)
                {
                    found = true;
                    nearest = edge;
                    nearestDistance = distance;
                }
            }

            return found;
        }

        private static List<RouteCandidate> CreateCandidates(
            CityLayout layout,
            uint stableSeed,
            IReadOnlyDictionary<RoadEdge, int> attractionScores)
        {
            var candidates = new List<RouteCandidate>();
            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                if (layout.GetPathKind(edge) != CityPathKind.Street)
                {
                    continue;
                }

                attractionScores.TryGetValue(edge, out int score);
                uint edgeSeed = CreateEdgeSeed(stableSeed, edge);
                uint rank = CityPedestrianStableHash.Combine(
                    edgeSeed,
                    CandidateRankSalt);
                candidates.Add(new RouteCandidate(edge, score, rank));
            }

            return candidates;
        }

        private static bool TryCreateDefinition(
            CityLayout layout,
            RoadWalkableArea sidewalkArea,
            uint stableSeed,
            RoadEdge edge,
            out CityPedestrianDefinition definition)
        {
            Vector3 start = layout.GetNodeWorldPosition(edge.A);
            Vector3 end = layout.GetNodeWorldPosition(edge.B);
            Vector3 tangent = end - start;
            tangent.y = 0f;
            float edgeLength = tangent.magnitude;
            float intersectionClearance =
                GetIntersectionClearance(layout);
            float requiredLength =
                (intersectionClearance * 2f) + MinimumRouteLength;
            if (edgeLength < requiredLength)
            {
                definition = null;
                return false;
            }

            tangent /= edgeLength;
            Vector3 left = new Vector3(-tangent.z, 0f, tangent.x);
            uint edgeSeed = CreateEdgeSeed(stableSeed, edge);
            float side =
                (CityPedestrianStableHash.Combine(
                    edgeSeed,
                    RouteSideSalt) & 1u) == 0u
                    ? -1f
                    : 1f;
            float sideOffset = GetSidewalkCenterOffset(layout);
            Vector3 laneOffset = left * (side * sideOffset);
            Vector3 first =
                start +
                (tangent * intersectionClearance) +
                laneOffset;
            Vector3 second =
                end -
                (tangent * intersectionClearance) +
                laneOffset;
            first.y = SidewalkWaypointHeight;
            second.y = SidewalkWaypointHeight;
            if (!IsRouteSafe(sidewalkArea, first, second))
            {
                definition = null;
                return false;
            }

            float speed = LerpFromHash(
                MinimumSpeed,
                MaximumSpeed,
                CityPedestrianStableHash.Combine(edgeSeed, SpeedSalt));
            float animationSpeed = LerpFromHash(
                MinimumAnimationSpeed,
                MaximumAnimationSpeed,
                CityPedestrianStableHash.Combine(
                    edgeSeed,
                    AnimationSpeedSalt));
            float animationPhase = CityPedestrianStableHash.ToUnitFloat(
                CityPedestrianStableHash.Combine(
                    edgeSeed,
                    AnimationPhaseSalt));
            int paletteVariant = (int)(
                CityPedestrianStableHash.Combine(
                    edgeSeed,
                    PaletteSalt) %
                PaletteVariantCount);
            bool startsReversed =
                (CityPedestrianStableHash.Combine(
                    edgeSeed,
                    DirectionSalt) & 1u) != 0u;
            uint behaviorSeed = CityPedestrianStableHash.Combine(
                edgeSeed,
                BehaviorSalt);
            string id = $"city-pedestrian:" +
                        $"{edge.A.x}:{edge.A.y}:" +
                        $"{edge.B.x}:{edge.B.y}";
            definition = new CityPedestrianDefinition(
                id,
                new[] { edge },
                new[] { first, second },
                speed,
                animationSpeed,
                animationPhase,
                paletteVariant,
                behaviorSeed,
                startsReversed);
            return true;
        }

        private static bool IsRouteSafe(
            RoadWalkableArea sidewalkArea,
            Vector3 start,
            Vector3 end)
        {
            for (int index = 0; index <= RouteSafetySamples; index++)
            {
                Vector3 point = Vector3.Lerp(
                    start,
                    end,
                    index / (float)RouteSafetySamples);
                if (!sidewalkArea.Contains(point, AgentRadius))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool CanFitSidewalkRoute(CityLayout layout)
        {
            return CityStreetSurfacePlanner.SidewalkWidth >=
                       AgentRadius * 2f &&
                   layout.RoadWidth -
                       (CityStreetSurfacePlanner.SidewalkWidth * 2f) >
                   MinimumCarriagewayWidth;
        }

        private static float SquaredDistanceToSegment(
            Vector3 point,
            Vector3 start,
            Vector3 end)
        {
            Vector3 delta = end - start;
            delta.y = 0f;
            Vector3 offset = point - start;
            offset.y = 0f;
            float denominator = delta.sqrMagnitude;
            float amount = denominator <= 0.000001f
                ? 0f
                : Mathf.Clamp01(Vector3.Dot(offset, delta) / denominator);
            Vector3 difference = offset - (delta * amount);
            return difference.sqrMagnitude;
        }

        private static uint CreateEdgeSeed(uint stableSeed, RoadEdge edge)
        {
            uint hash = CityPedestrianStableHash.Combine(
                stableSeed,
                unchecked((uint)edge.A.x));
            hash = CityPedestrianStableHash.Combine(
                hash,
                unchecked((uint)edge.A.y));
            hash = CityPedestrianStableHash.Combine(
                hash,
                unchecked((uint)edge.B.x));
            return CityPedestrianStableHash.Combine(
                hash,
                unchecked((uint)edge.B.y));
        }

        private static float LerpFromHash(
            float minimum,
            float maximum,
            uint hash)
        {
            return Mathf.Lerp(
                minimum,
                maximum,
                CityPedestrianStableHash.ToUnitFloat(hash));
        }

        private static int CompareCandidates(
            RouteCandidate left,
            RouteCandidate right)
        {
            int attractionComparison =
                right.AttractionScore.CompareTo(left.AttractionScore);
            if (attractionComparison != 0)
            {
                return attractionComparison;
            }

            int rankComparison = left.Rank.CompareTo(right.Rank);
            return rankComparison != 0
                ? rankComparison
                : RoadEdge.Compare(left.Edge, right.Edge);
        }

        private static int CompareDefinitions(
            CityPedestrianDefinition left,
            CityPedestrianDefinition right)
        {
            return string.CompareOrdinal(left.Id, right.Id);
        }

        private readonly struct RouteCandidate
        {
            public RouteCandidate(
                RoadEdge edge,
                int attractionScore,
                uint rank)
            {
                Edge = edge;
                AttractionScore = attractionScore;
                Rank = rank;
            }

            public RoadEdge Edge { get; }
            public int AttractionScore { get; }
            public uint Rank { get; }
        }
    }

    internal static class CityPedestrianStableHash
    {
        public static uint String(string value)
        {
            uint hash = 2166136261u;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                hash ^= (byte)character;
                hash *= 16777619u;
                hash ^= (byte)(character >> 8);
                hash *= 16777619u;
            }

            return hash == 0u ? 0xA341316Cu : hash;
        }

        public static uint Combine(uint first, uint second)
        {
            uint hash = first ^ 0x9E3779B9u;
            hash ^= second + 0x85EBCA6Bu +
                    (hash << 6) + (hash >> 2);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return hash == 0u ? 0xA341316Cu : hash;
        }

        public static float ToUnitFloat(uint hash)
        {
            return (hash >> 8) * (1f / 16777216f);
        }
    }
}
