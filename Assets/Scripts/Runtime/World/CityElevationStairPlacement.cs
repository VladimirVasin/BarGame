using System;
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
        public static CityElevationStairPlacement Create(
            CityLayout layout,
            CityElevationStairDescriptor stair)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

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
            Vector3 lowerApproachStart =
                lower + sideDirection * sideOffset;
            lowerApproachStart.y +=
                CityStreetSurfacePlanner.SidewalkTop;
            Vector3 upperApproachEnd =
                upper + sideDirection * sideOffset;
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
