using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    public sealed class CityElevationStairPlacement
    {
        internal CityElevationStairPlacement(
            CityExteriorStairPlan exteriorPlan,
            Vector3 lowerApproachStart,
            Vector3 lowerApproachEnd,
            Vector3 upperApproachStart,
            Vector3 upperApproachEnd,
            Vector3 sideDirection,
            Rect footprint,
            Rect lowerApproachFootprint,
            Rect upperApproachFootprint,
            Rect groundCutFootprint,
            CityExteriorStairRailDescriptor lowerInnerRail,
            CityExteriorStairRailDescriptor upperInnerRail)
        {
            ExteriorPlan = exteriorPlan ??
                throw new ArgumentNullException(nameof(exteriorPlan));
            LowerApproachStart = lowerApproachStart;
            LowerApproachEnd = lowerApproachEnd;
            UpperApproachStart = upperApproachStart;
            UpperApproachEnd = upperApproachEnd;
            SideDirection = sideDirection;
            Footprint = footprint;
            LowerApproachFootprint = lowerApproachFootprint;
            UpperApproachFootprint = upperApproachFootprint;
            GroundCutFootprint = groundCutFootprint;
            LowerInnerRail = lowerInnerRail;
            UpperInnerRail = upperInnerRail;
        }

        public CityExteriorStairPlan ExteriorPlan { get; }
        public Vector3 LowerApproachStart { get; }
        public Vector3 LowerApproachEnd { get; }
        public Vector3 UpperApproachStart { get; }
        public Vector3 UpperApproachEnd { get; }
        public Vector3 SideDirection { get; }
        public Rect Footprint { get; }
        public Rect LowerApproachFootprint { get; }
        public Rect UpperApproachFootprint { get; }
        public Rect GroundCutFootprint { get; }
        public CityExteriorStairRailDescriptor LowerInnerRail { get; }
        public CityExteriorStairRailDescriptor UpperInnerRail { get; }
    }

    public static class CityElevationStairPlacementPlanner
    {
        private static readonly Vector2Int[] NeighbourSteps =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1)
        };

        // A placement is a pure function of the immutable layout and the
        // descriptor, and every ground builder, walkable area, boundary plan
        // and street planner re-plans the same handful of stairs. The layout
        // keys the memo so it dies with the layout; the descriptor is matched
        // field by field, floats by their bits, so only an identical request
        // is ever answered from the cache.
        private static readonly ConditionalWeakTable<
            CityLayout,
            List<KeyValuePair<CityElevationStairDescriptor,
                CityElevationStairPlacement>>> Placements =
            new ConditionalWeakTable<
                CityLayout,
                List<KeyValuePair<CityElevationStairDescriptor,
                    CityElevationStairPlacement>>>();

        public static CityElevationStairPlacement Create(
            CityLayout layout,
            CityElevationStairDescriptor stair)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            List<KeyValuePair<CityElevationStairDescriptor,
                CityElevationStairPlacement>> cache = Placements.GetValue(
                layout,
                _ => new List<KeyValuePair<CityElevationStairDescriptor,
                    CityElevationStairPlacement>>());
            lock (cache)
            {
                for (int index = 0; index < cache.Count; index++)
                {
                    if (SameStair(cache[index].Key, stair))
                    {
                        return cache[index].Value;
                    }
                }

                CityElevationStairPlacement placement = Plan(layout, stair);
                cache.Add(new KeyValuePair<CityElevationStairDescriptor,
                    CityElevationStairPlacement>(stair, placement));
                return placement;
            }
        }

        private static bool SameStair(
            in CityElevationStairDescriptor first,
            in CityElevationStairDescriptor second)
        {
            return string.Equals(first.Id, second.Id, StringComparison.Ordinal) &&
                   first.District == second.District &&
                   first.Edge == second.Edge &&
                   first.LowerNode == second.LowerNode &&
                   first.UpperNode == second.UpperNode &&
                   first.Side == second.Side &&
                   first.StepCount == second.StepCount &&
                   SameBits(first.StepRise, second.StepRise) &&
                   SameBits(first.TreadDepth, second.TreadDepth) &&
                   SameBits(first.Width, second.Width) &&
                   SameBits(first.LandingLength, second.LandingLength);
        }

        private static bool SameBits(float first, float second)
        {
            return BitConverter.SingleToInt32Bits(first) ==
                   BitConverter.SingleToInt32Bits(second);
        }

        private static CityElevationStairPlacement Plan(
            CityLayout layout,
            CityElevationStairDescriptor stair)
        {
            Vector3 lower = layout.GetNodeWorldPosition(stair.LowerNode);
            Vector3 upper = layout.GetNodeWorldPosition(stair.UpperNode);
            Vector3 ascent = upper - lower;
            ascent.y = 0f;
            ascent.Normalize();
            Vector3 right = new Vector3(ascent.z, 0f, -ascent.x);
            Vector3 sideDirection = stair.Side ==
                CityElevationStairSide.Right
                ? right
                : -right;
            float carriagewayHalf =
                (layout.RoadWidth -
                 CityStreetSurfacePlanner.SidewalkWidth * 2f) * 0.5f;
            float sideOffset = carriagewayHalf + stair.Width * 0.5f;
            Vector3 connectorCenter =
                (lower + upper) * 0.5f + sideDirection * sideOffset;
            float runLength = stair.RunLength;
            Vector3 flightStart = connectorCenter -
                ascent * (runLength * 0.5f);
            flightStart.y = lower.y +
                            CityStreetSurfacePlanner.SidewalkTop;
            CityExteriorStairDropSide dropSides =
                CityExteriorStairDropSide.Both;
            CityExteriorStairPlan exterior =
                CityExteriorStairPlanner.CreateStraightFlight(
                    stair.Id,
                    flightStart,
                    ascent,
                    stair.Width,
                    stair.StepCount,
                    stair.StepRise,
                    stair.TreadDepth,
                    stair.LandingLength,
                    dropSides,
                    true);
            CityExteriorStairLandingDescriptor lowerLanding =
                exterior.Landings[0];
            CityExteriorStairLandingDescriptor upperLanding =
                exterior.Landings[1];
            Vector3 lowerApproachEnd = lowerLanding.StartEdgeCenter;
            Vector3 upperApproachStart = upperLanding.EndEdgeCenter;
            float halfRoad = layout.RoadWidth * 0.5f;
            float lowerInset = ResolveApproachInset(
                layout,
                stair.LowerNode,
                halfRoad,
                Vector3.Dot(lowerApproachEnd - lower, ascent));
            float upperInset = ResolveApproachInset(
                layout,
                stair.UpperNode,
                halfRoad,
                Vector3.Dot(upper - upperApproachStart, ascent));
            Vector3 lowerApproachStart =
                lower +
                ascent * lowerInset +
                sideDirection * sideOffset;
            lowerApproachStart.y +=
                CityStreetSurfacePlanner.SidewalkTop;
            Vector3 upperApproachEnd =
                upper -
                ascent * upperInset +
                sideDirection * sideOffset;
            upperApproachEnd.y +=
                CityStreetSurfacePlanner.SidewalkTop;
            Rect footprint = CreateFootprint(
                lowerApproachEnd,
                upperApproachStart,
                stair.Width);
            Rect lowerApproachFootprint = CreateFootprint(
                lowerApproachStart,
                lowerApproachEnd,
                stair.Width);
            Rect upperApproachFootprint = CreateFootprint(
                upperApproachStart,
                upperApproachEnd,
                stair.Width);
            Rect groundCutFootprint = Union(
                Union(footprint, lowerApproachFootprint),
                upperApproachFootprint);
            Vector3 innerOffset = -sideDirection * (stair.Width * 0.5f);
            var lowerInnerRail = new CityExteriorStairRailDescriptor(
                $"{stair.Id}:rail:approach:lower:inner",
                $"{stair.Id}:approach:lower",
                CityExteriorStairRailOwnerKind.Approach,
                CityExteriorStairDropSide.Left,
                lowerApproachStart + innerOffset,
                lowerApproachEnd + innerOffset,
                CityExteriorStairPlanner.DefaultRailHeight,
                CityExteriorStairPlanner.DefaultRailThickness);
            var upperInnerRail = new CityExteriorStairRailDescriptor(
                $"{stair.Id}:rail:approach:upper:inner",
                $"{stair.Id}:approach:upper",
                CityExteriorStairRailOwnerKind.Approach,
                CityExteriorStairDropSide.Left,
                upperApproachStart + innerOffset,
                upperApproachEnd + innerOffset,
                CityExteriorStairPlanner.DefaultRailHeight,
                CityExteriorStairPlanner.DefaultRailThickness);
            return new CityElevationStairPlacement(
                exterior,
                lowerApproachStart,
                lowerApproachEnd,
                upperApproachStart,
                upperApproachEnd,
                sideDirection,
                footprint,
                lowerApproachFootprint,
                upperApproachFootprint,
                groundCutFootprint,
                lowerInnerRail,
                upperInnerRail);
        }

        /// <summary>
        /// An approach ends where the sidewalk it continues ends, so that
        /// neither its paving nor its guard rail reaches into the crossing
        /// carriageway. Mirrors the street sidewalk endpoint inset and never
        /// bites deeper than the approach is long.
        /// </summary>
        private static float ResolveApproachInset(
            CityLayout layout,
            Vector2Int node,
            float halfRoad,
            float approachLength)
        {
            int count = 0;
            Vector2Int sum = Vector2Int.zero;
            for (int index = 0; index < NeighbourSteps.Length; index++)
            {
                Vector2Int step = NeighbourSteps[index];
                if (!layout.HasRoad(node, node + step))
                {
                    continue;
                }

                count++;
                sum += step;
            }

            bool isIntersectionCore = count >= 3 ||
                (count == 2 && sum != Vector2Int.zero);
            float inset = isIntersectionCore
                ? halfRoad
                : count == 1
                    ? -halfRoad
                    : 0f;
            return Mathf.Min(inset, Mathf.Max(0f, approachLength));
        }

        private static Rect Union(Rect first, Rect second)
        {
            return Rect.MinMaxRect(
                Mathf.Min(first.xMin, second.xMin),
                Mathf.Min(first.yMin, second.yMin),
                Mathf.Max(first.xMax, second.xMax),
                Mathf.Max(first.yMax, second.yMax));
        }

        private static Rect CreateFootprint(
            Vector3 first,
            Vector3 second,
            float width)
        {
            Vector3 direction = second - first;
            direction.y = 0f;
            direction.Normalize();
            Vector3 right = new Vector3(
                direction.z,
                0f,
                -direction.x);
            Vector3 halfWidth = right * (width * 0.5f);
            float xMin = Mathf.Min(
                first.x - halfWidth.x,
                second.x - halfWidth.x,
                first.x + halfWidth.x,
                second.x + halfWidth.x);
            float xMax = Mathf.Max(
                first.x - halfWidth.x,
                second.x - halfWidth.x,
                first.x + halfWidth.x,
                second.x + halfWidth.x);
            float zMin = Mathf.Min(
                first.z - halfWidth.z,
                second.z - halfWidth.z,
                first.z + halfWidth.z,
                second.z + halfWidth.z);
            float zMax = Mathf.Max(
                first.z - halfWidth.z,
                second.z - halfWidth.z,
                first.z + halfWidth.z,
                second.z + halfWidth.z);
            return Rect.MinMaxRect(xMin, zMin, xMax, zMax);
        }
    }
}
