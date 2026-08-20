using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static class CityFringeYardValidator
    {
        private const float GeometryTolerance = 0.035f;

        public static void ValidateOrThrow(
            CityLayout layout,
            CityMountainBoundaryPlan mountains,
            CityFringeYardPlan plan)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (mountains == null)
            {
                throw new ArgumentNullException(nameof(mountains));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            bool isDefault = string.Equals(
                layout.BlueprintId,
                CityBlueprintCatalog.DefaultBlueprintId,
                StringComparison.Ordinal);
            if (!isDefault)
            {
                if (plan.IsEnabled ||
                    plan.Yards.Count != 0 ||
                    plan.PartCount != 0 ||
                    plan.HasTunnelForecourt)
                {
                    throw new InvalidOperationException(
                        "Only the default coastal blueprint may own fringe " +
                        "Yard compositions.");
                }

                return;
            }

            if (!plan.IsEnabled ||
                plan.Yards.Count != CityFringeYardPlanner.ExpectedYardCount ||
                !plan.HasTunnelForecourt ||
                plan.PartCount <= 0 ||
                plan.PartCount > CityFringeYardPlanner.MaximumPartCount)
            {
                throw new InvalidOperationException(
                    "The default coastal city requires five bounded fringe " +
                    "Yards and one budgeted tunnel forecourt.");
            }

            ValidateForecourt(mountains, plan.TunnelForecourt);
            var areaIds = new HashSet<string>(StringComparer.Ordinal);
            var accessIds = new HashSet<string>(StringComparer.Ordinal);
            var partIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < plan.Yards.Count; index++)
            {
                CityFringeYardDescriptor yard = plan.Yards[index];
                if (!areaIds.Add(yard.AreaId) ||
                    !accessIds.Add(yard.Access.Id))
                {
                    throw new InvalidOperationException(
                        "Fringe Yard areas and accesses must be unique.");
                }

                ValidateYard(layout, mountains, plan, yard, partIds);
            }

            ValidateExpectedMapping(plan);
        }

        private static void ValidateYard(
            CityLayout layout,
            CityMountainBoundaryPlan mountains,
            CityFringeYardPlan plan,
            CityFringeYardDescriptor yard,
            ISet<string> partIds)
        {
            if (yard == null ||
                string.IsNullOrWhiteSpace(yard.StableId) ||
                string.IsNullOrWhiteSpace(yard.AreaId) ||
                !Enum.IsDefined(typeof(CityFringeYardKind), yard.Kind) ||
                yard.AreaBounds.width <= 0f ||
                yard.AreaBounds.height <= 0f ||
                yard.TraversalBounds.width <= 0f ||
                yard.TraversalBounds.height <= 0f)
            {
                throw new InvalidOperationException(
                    "A fringe Yard descriptor is incomplete.");
            }

            if (!string.Equals(
                    yard.Access.AreaId,
                    yard.AreaId,
                    StringComparison.Ordinal) ||
                yard.Access.Feature != CityAreaFeatureKind.Yard ||
                !IsCardinalUnit(yard.Access.OutwardNormal))
            {
                throw new InvalidOperationException(
                    $"Fringe Yard '{yard.AreaId}' has an invalid access.");
            }

            List<CitySurfaceDescriptor> surfaces = GetAreaSurfaces(
                layout,
                yard.AreaId);
            Rect expectedBounds = CalculateBounds(surfaces);
            if (!Approximately(expectedBounds, yard.AreaBounds))
            {
                throw new InvalidOperationException(
                    $"Fringe Yard '{yard.AreaId}' does not cover its source " +
                    "surfaces exactly.");
            }

            for (int index = 0; index < surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = surfaces[index];
                if (surface.Kind != CitySurfaceKind.OpenGround ||
                    surface.Feature != CityAreaFeatureKind.Yard ||
                    !surface.IsWalkable)
                {
                    throw new InvalidOperationException(
                        $"Fringe Yard '{yard.AreaId}' must remain walkable " +
                        "OpenGround.");
                }
            }

            bool hasTrack = false;
            bool hasDrain = false;
            bool hasRetaining = false;
            bool hasPole = false;
            bool hasRepairStock = false;
            bool hasWheelRut = false;
            bool hasDrainCover = false;
            bool hasTunnelCheek = false;
            bool hasGabion = false;
            bool hasShed = false;
            bool hasBerm = false;
            for (int index = 0; index < yard.Parts.Count; index++)
            {
                CityFringeYardPartDescriptor part = yard.Parts[index];
                ValidatePart(
                    layout,
                    mountains,
                    yard,
                    part,
                    partIds);
                hasTrack |= part.Kind == CityFringeYardPartKind.ServiceTrack;
                hasDrain |= part.Kind == CityFringeYardPartKind.DrainChannel;
                hasRetaining |=
                    part.Kind == CityFringeYardPartKind.RetainingSection;
                hasPole |= part.Kind == CityFringeYardPartKind.UtilityPole;
                hasRepairStock |=
                    part.Kind == CityFringeYardPartKind.RepairStack;
                hasWheelRut |= part.Kind == CityFringeYardPartKind.WheelRut;
                hasDrainCover |=
                    part.Kind == CityFringeYardPartKind.DrainCover;
                hasTunnelCheek |=
                    part.Kind == CityFringeYardPartKind.TunnelCheek;
                hasGabion |= part.Kind == CityFringeYardPartKind.Gabion;
                hasShed |= part.Kind == CityFringeYardPartKind.UtilityShed;
                hasBerm |= part.Kind == CityFringeYardPartKind.EarthBerm;
            }

            if (!hasTrack || !hasDrain)
            {
                throw new InvalidOperationException(
                    $"Fringe Yard '{yard.AreaId}' is missing its service " +
                    "trace or drainage line.");
            }

            switch (yard.Kind)
            {
                case CityFringeYardKind.WestStoneTerraces:
                    Require(hasRetaining && hasPole, yard, "stone terrace");
                    break;
                case CityFringeYardKind.WestIndustrialBelt:
                    Require(
                        hasRetaining && hasPole && hasRepairStock,
                        yard,
                        "industrial service belt");
                    break;
                case CityFringeYardKind.SouthTunnelForecourt:
                    Require(
                        hasRetaining && hasWheelRut &&
                        hasDrainCover && hasTunnelCheek,
                        yard,
                        "sealed tunnel forecourt");
                    if (!Approximately(
                            yard.TraversalBounds,
                            plan.TunnelForecourt.DriveClearBounds))
                    {
                        throw new InvalidOperationException(
                            "The tunnel Yard must reserve the plan's exact " +
                            "drive-clear corridor.");
                    }
                    break;
                case CityFringeYardKind.SouthFloodWorks:
                    Require(hasGabion && hasPole, yard, "flood works");
                    break;
                case CityFringeYardKind.EastUtilityEdge:
                    Require(
                        hasPole && hasShed && hasBerm &&
                        !hasRetaining && !hasTunnelCheek,
                        yard,
                        "low eastern utility edge");
                    break;
            }
        }

        private static void ValidatePart(
            CityLayout layout,
            CityMountainBoundaryPlan mountains,
            CityFringeYardDescriptor yard,
            CityFringeYardPartDescriptor part,
            ISet<string> partIds)
        {
            if (string.IsNullOrWhiteSpace(part.StableId) ||
                !partIds.Add(part.StableId) ||
                !string.Equals(
                    part.AreaId,
                    yard.AreaId,
                    StringComparison.Ordinal) ||
                !Enum.IsDefined(typeof(CityFringeYardPartKind), part.Kind) ||
                !Enum.IsDefined(typeof(CityFringeYardStyle), part.Style) ||
                !IsFinite(part.Center) ||
                !IsFinite(part.Rotation) ||
                !IsPositiveFinite(part.Size))
            {
                throw new InvalidOperationException(
                    $"Fringe part '{part.StableId}' is invalid or duplicated.");
            }

            float quaternionMagnitude = Mathf.Sqrt(
                part.Rotation.x * part.Rotation.x +
                part.Rotation.y * part.Rotation.y +
                part.Rotation.z * part.Rotation.z +
                part.Rotation.w * part.Rotation.w);
            if (Mathf.Abs(quaternionMagnitude - 1f) > 0.01f ||
                !Contains(yard.AreaBounds, part.Footprint, GeometryTolerance))
            {
                throw new InvalidOperationException(
                    $"Fringe part '{part.StableId}' leaves its owner Yard.");
            }

            if (part.BlocksMovement &&
                (part.Footprint.Overlaps(yard.Access.ApproachBounds) ||
                 part.Footprint.Overlaps(yard.TraversalBounds)))
            {
                throw new InvalidOperationException(
                    $"Fringe part '{part.StableId}' blocks the declared " +
                    "street or traversal corridor.");
            }

            if (mountains.HasRiverNotch &&
                part.Footprint.Overlaps(mountains.RiverNotch.OpeningBounds))
            {
                throw new InvalidOperationException(
                    $"Fringe part '{part.StableId}' intrudes into the river " +
                    "mountain notch.");
            }

            bool hasOwnerSample = false;
            Vector2 xz = new Vector2(part.Center.x, part.Center.z);
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (string.Equals(
                        surface.AreaId,
                        yard.AreaId,
                        StringComparison.Ordinal) &&
                    Contains(surface.WorldBounds, xz, GeometryTolerance))
                {
                    hasOwnerSample = true;
                    break;
                }
            }

            if (!hasOwnerSample)
            {
                throw new InvalidOperationException(
                    $"Fringe part '{part.StableId}' has no terrain owner.");
            }
        }

        private static void ValidateForecourt(
            CityMountainBoundaryPlan mountains,
            CityTunnelForecourtDescriptor forecourt)
        {
            if (!mountains.IsEnabled || !mountains.HasTunnel)
            {
                throw new InvalidOperationException(
                    "A tunnel forecourt cannot exist without mountains.");
            }

            CityMountainTunnelDescriptor tunnel = mountains.Tunnel;
            if (string.IsNullOrWhiteSpace(forecourt.StableId) ||
                !string.Equals(
                    forecourt.AreaId,
                    tunnel.AreaId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    forecourt.TargetAccessId,
                    tunnel.TargetAccessId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    forecourt.TunnelStableId,
                    tunnel.StableId,
                    StringComparison.Ordinal) ||
                !forecourt.PortalAnchor.Equals(tunnel.PortalGroundCenter) ||
                !forecourt.Axis.Equals(tunnel.Axis.normalized) ||
                !forecourt.IsSealed ||
                !tunnel.IsSealed ||
                forecourt.DriveClearWidth <
                    CityFringeYardPlanner.MinimumTunnelDriveClearWidth ||
                forecourt.ApproachWidth < forecourt.DriveClearWidth ||
                !Contains(
                    forecourt.ApproachBounds,
                    new Vector2(
                        forecourt.StreetAnchor.x,
                        forecourt.StreetAnchor.z),
                    GeometryTolerance) ||
                !Contains(
                    forecourt.ApproachBounds,
                    new Vector2(
                        forecourt.PortalAnchor.x,
                        forecourt.PortalAnchor.z),
                    GeometryTolerance))
            {
                throw new InvalidOperationException(
                    "The fringe forecourt drifted from the sealed tunnel.");
            }
        }

        private static void ValidateExpectedMapping(CityFringeYardPlan plan)
        {
            RequireKind(
                plan,
                CityMountainBoundaryDefinition.WestNorthAreaId,
                CityFringeYardKind.WestStoneTerraces);
            RequireKind(
                plan,
                CityMountainBoundaryDefinition.WestSouthAreaId,
                CityFringeYardKind.WestIndustrialBelt);
            RequireKind(
                plan,
                CityMountainBoundaryDefinition.SouthWestAreaId,
                CityFringeYardKind.SouthTunnelForecourt);
            RequireKind(
                plan,
                CityMountainBoundaryDefinition.SouthEastAreaId,
                CityFringeYardKind.SouthFloodWorks);
            RequireKind(
                plan,
                "yard-east",
                CityFringeYardKind.EastUtilityEdge);
        }

        private static void RequireKind(
            CityFringeYardPlan plan,
            string areaId,
            CityFringeYardKind expected)
        {
            if (!plan.TryGetYard(areaId, out CityFringeYardDescriptor yard) ||
                yard.Kind != expected)
            {
                throw new InvalidOperationException(
                    $"Fringe Yard '{areaId}' has the wrong authored profile.");
            }
        }

        private static void Require(
            bool condition,
            CityFringeYardDescriptor yard,
            string vocabulary)
        {
            if (!condition)
            {
                throw new InvalidOperationException(
                    $"Fringe Yard '{yard.AreaId}' lacks its {vocabulary} " +
                    "vocabulary.");
            }
        }

        private static List<CitySurfaceDescriptor> GetAreaSurfaces(
            CityLayout layout,
            string areaId)
        {
            var result = new List<CitySurfaceDescriptor>();
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (string.Equals(
                        surface.AreaId,
                        areaId,
                        StringComparison.Ordinal))
                {
                    result.Add(surface);
                }
            }

            if (result.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Missing fringe surfaces for '{areaId}'.");
            }

            return result;
        }

        private static Rect CalculateBounds(
            IReadOnlyList<CitySurfaceDescriptor> surfaces)
        {
            Rect result = surfaces[0].WorldBounds;
            for (int index = 1; index < surfaces.Count; index++)
            {
                Rect next = surfaces[index].WorldBounds;
                result = Rect.MinMaxRect(
                    Mathf.Min(result.xMin, next.xMin),
                    Mathf.Min(result.yMin, next.yMin),
                    Mathf.Max(result.xMax, next.xMax),
                    Mathf.Max(result.yMax, next.yMax));
            }

            return result;
        }

        private static bool IsCardinalUnit(Vector3 value)
        {
            Vector3 flat = new Vector3(value.x, 0f, value.z);
            return Mathf.Abs(flat.magnitude - 1f) <= 0.001f &&
                   (Mathf.Abs(flat.x) <= 0.001f ||
                    Mathf.Abs(flat.z) <= 0.001f);
        }

        private static bool Approximately(Rect left, Rect right)
        {
            return Mathf.Abs(left.xMin - right.xMin) <= GeometryTolerance &&
                   Mathf.Abs(left.xMax - right.xMax) <= GeometryTolerance &&
                   Mathf.Abs(left.yMin - right.yMin) <= GeometryTolerance &&
                   Mathf.Abs(left.yMax - right.yMax) <= GeometryTolerance;
        }

        private static bool Contains(
            Rect outer,
            Rect inner,
            float tolerance)
        {
            return inner.xMin >= outer.xMin - tolerance &&
                   inner.xMax <= outer.xMax + tolerance &&
                   inner.yMin >= outer.yMin - tolerance &&
                   inner.yMax <= outer.yMax + tolerance;
        }

        private static bool Contains(
            Rect bounds,
            Vector2 point,
            float tolerance)
        {
            return point.x >= bounds.xMin - tolerance &&
                   point.x <= bounds.xMax + tolerance &&
                   point.y >= bounds.yMin - tolerance &&
                   point.y <= bounds.yMax + tolerance;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
        }

        private static bool IsPositiveFinite(Vector3 value)
        {
            return value.x > 0f &&
                   value.y > 0f &&
                   value.z > 0f &&
                   IsFinite(value);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
