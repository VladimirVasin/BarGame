using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static class CityFringeYardPracticalValidator
    {
        public const int ExpectedPracticalCount = 4;

        public static void ValidateOrThrow(
            CityLayout layout,
            CityFringeYardPlan plan)
        {
            if (plan.Practicals.Count != ExpectedPracticalCount)
            {
                throw new InvalidOperationException(
                    "The mountain service belt requires four practicals.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var areas = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < plan.Practicals.Count; index++)
            {
                CityFringeYardPracticalDescriptor practical =
                    plan.Practicals[index];
                if (string.IsNullOrWhiteSpace(practical.StableId) ||
                    !ids.Add(practical.StableId) ||
                    !areas.Add(practical.AreaId) ||
                    !Enum.IsDefined(
                        typeof(CityFringeYardPracticalKind),
                        practical.Kind) ||
                    !Enum.IsDefined(
                        typeof(CityFringeYardKind),
                        practical.YardKind) ||
                    !IsFinite(practical.Position) ||
                    !IsFinite(practical.Forward) ||
                    !IsPositiveFinite(practical.LensSize) ||
                    !IsFinite(practical.LitColor) ||
                    Mathf.Abs(practical.Forward.magnitude - 1f) > 0.01f ||
                    practical.Forward.y >= -0.10f)
                {
                    throw new InvalidOperationException(
                        $"Fringe practical '{practical.StableId}' is invalid.");
                }

                if (!plan.TryGetYard(
                        practical.AreaId,
                        out CityFringeYardDescriptor yard) ||
                    yard.Kind != practical.YardKind ||
                    yard.Kind == CityFringeYardKind.EastUtilityEdge)
                {
                    throw new InvalidOperationException(
                        $"Fringe practical '{practical.StableId}' has no " +
                        "mountain Yard owner.");
                }

                Vector2 xz = new Vector2(
                    practical.Position.x,
                    practical.Position.z);
                bool isTunnelPortalLight =
                    practical.YardKind ==
                    CityFringeYardKind.SouthTunnelForecourt &&
                    practical.Kind ==
                    CityFringeYardPracticalKind.TunnelReturnLamp;
                bool overlapsReservedRoute =
                    yard.Access.ApproachBounds.Contains(xz) ||
                    yard.TraversalBounds.Contains(xz);
                if (!Contains(yard.AreaBounds, xz) ||
                    (overlapsReservedRoute && !isTunnelPortalLight) ||
                    !HasOwnerGround(layout, yard.AreaId, xz))
                {
                    throw new InvalidOperationException(
                        $"Fringe practical '{practical.StableId}' blocks or " +
                        "leaves its Yard.");
                }

                ValidateMapping(plan, yard, practical);
            }

            if (areas.Contains("yard-east"))
            {
                throw new InvalidOperationException(
                    "The low east utility edge must remain unlit.");
            }
        }

        private static void ValidateMapping(
            CityFringeYardPlan plan,
            CityFringeYardDescriptor yard,
            CityFringeYardPracticalDescriptor practical)
        {
            CityFringeYardPracticalKind expected;
            switch (practical.YardKind)
            {
                case CityFringeYardKind.WestStoneTerraces:
                    expected = CityFringeYardPracticalKind.CulvertLamp;
                    break;
                case CityFringeYardKind.WestIndustrialBelt:
                    expected = CityFringeYardPracticalKind.RepairLamp;
                    break;
                case CityFringeYardKind.SouthTunnelForecourt:
                    expected = CityFringeYardPracticalKind.TunnelReturnLamp;
                    Vector3 expectedPosition =
                        CityTunnelPortalLightGeometry.ResolvePosition(
                            plan.TunnelForecourt);
                    Vector3 expectedForward =
                        CityTunnelPortalLightGeometry.ResolveForward(
                            plan.TunnelForecourt);
                    if (Vector3.Distance(
                            practical.Position,
                            expectedPosition) > 0.01f ||
                        Vector3.Angle(
                            practical.Forward,
                            expectedForward) > 0.1f ||
                        Vector3.Dot(
                            practical.Forward,
                            plan.TunnelForecourt.Axis) <= 0.60f)
                    {
                        throw new InvalidOperationException(
                            "The tunnel practical must sit over the portal " +
                            "and point down the sealed throat.");
                    }
                    break;
                case CityFringeYardKind.SouthFloodWorks:
                    expected = CityFringeYardPracticalKind.FloodGaugeLamp;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Only the four mountain Yards may own practicals.");
            }

            if (practical.Kind != expected)
            {
                throw new InvalidOperationException(
                    $"Fringe practical '{practical.StableId}' has the " +
                    "wrong authored profile.");
            }

            if (practical.YardKind !=
                    CityFringeYardKind.SouthTunnelForecourt &&
                Vector3.Dot(
                    practical.Forward,
                    -yard.Access.OutwardNormal) <= 0.50f)
            {
                throw new InvalidOperationException(
                    $"Fringe practical '{practical.StableId}' must aim " +
                    "back across its service work, not at the map edge.");
            }
        }

        private static bool HasOwnerGround(
            CityLayout layout,
            string areaId,
            Vector2 xz)
        {
            return CityTerrainSurfacePlan.TrySampleGroundTop(
                       layout,
                       xz,
                       out _,
                       out CitySurfaceDescriptor surface) &&
                   string.Equals(
                       surface.AreaId,
                       areaId,
                       StringComparison.Ordinal);
        }

        private static bool Contains(Rect rect, Vector2 point)
        {
            const float tolerance = 0.035f;
            return point.x >= rect.xMin - tolerance &&
                   point.x <= rect.xMax + tolerance &&
                   point.y >= rect.yMin - tolerance &&
                   point.y <= rect.yMax + tolerance;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(Color value)
        {
            return IsFinite(value.r) &&
                   IsFinite(value.g) &&
                   IsFinite(value.b) &&
                   IsFinite(value.a);
        }

        private static bool IsPositiveFinite(Vector3 value)
        {
            return IsFinite(value) &&
                   value.x > 0f &&
                   value.y > 0f &&
                   value.z > 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
