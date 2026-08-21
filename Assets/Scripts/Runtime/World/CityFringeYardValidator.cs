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
                    plan.Practicals.Count != 0 ||
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
            CityRoadGroundBoundaryPlan roadGroundBoundaries =
                CityRoadGroundBoundaryPlanner.Create(layout);
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

                ValidateYard(
                    layout,
                    mountains,
                    plan,
                    roadGroundBoundaries,
                    yard,
                    partIds);
            }

            ValidateExpectedMapping(plan);
            CityFringeYardPracticalValidator.ValidateOrThrow(layout, plan);
        }

        private static void ValidateYard(
            CityLayout layout,
            CityMountainBoundaryPlan mountains,
            CityFringeYardPlan plan,
            CityRoadGroundBoundaryPlan roadGroundBoundaries,
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
            bool hasRoadShoulder = false;
            bool hasServiceSpur = false;
            bool hasToeTrack = false;
            float spurMinimumDepth = float.PositiveInfinity;
            float spurMaximumDepth = float.NegativeInfinity;
            float trackMaximumDepth = float.NegativeInfinity;
            bool isMountainYard =
                CityMountainBoundaryDefinition.IsMountainFacingAreaId(
                    yard.AreaId);
            var forefieldAnchors = new List<float>(
                CityFringeYardPlanner.MaximumForefieldAnchorCount);
            var poleCoordinates = new List<float>();
            for (int index = 0; index < yard.Parts.Count; index++)
            {
                CityFringeYardPartDescriptor part = yard.Parts[index];
                ValidatePart(
                    layout,
                    mountains,
                    roadGroundBoundaries,
                    yard,
                    part,
                    partIds);
                if (isMountainYard)
                {
                    ValidateMountainSurfacePolicy(yard, part);
                }

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
                float depth = DepthFromRoad(yard, part.Center);
                if (part.Kind == CityFringeYardPartKind.ServiceTrack)
                {
                    if (isMountainYard)
                    {
                        ValidateColliderlessRole(yard, part, "service track");
                        ValidateSurfaceCorners(
                            layout,
                            yard,
                            part,
                            "service track",
                            yard.Kind ==
                                    CityFringeYardKind.SouthTunnelForecourt
                                ? 0.02f
                                : 0.04f,
                            0.10f);
                    }
                    hasToeTrack |= depth >=
                        CityFringeYardPlanner.ForefieldMiddleBandMaximumDepth -
                        GeometryTolerance &&
                        depth <=
                        CityFringeYardPlanner.ForefieldToeBandMaximumDepth +
                        GeometryTolerance;
                    trackMaximumDepth = Mathf.Max(trackMaximumDepth, depth);
                }
                else if (yard.Kind == CityFringeYardKind.SouthFloodWorks &&
                         part.Kind == CityFringeYardPartKind.DrainChannel &&
                         depth >=
                             CityFringeYardPlanner
                                 .ForefieldMiddleBandMaximumDepth -
                             GeometryTolerance &&
                         depth <=
                             CityFringeYardPlanner
                                 .ForefieldToeBandMaximumDepth +
                             GeometryTolerance)
                {
                    hasToeTrack = true;
                    trackMaximumDepth = Mathf.Max(trackMaximumDepth, depth);
                }
                else if (part.Kind ==
                         CityFringeYardPartKind.RoadShoulder)
                {
                    ValidateRoadShoulder(layout, yard, part);
                    hasRoadShoulder = true;
                }
                else if (part.Kind == CityFringeYardPartKind.ServiceSpur)
                {
                    ValidateColliderlessRole(yard, part, "service spur");
                    if (yard.Kind ==
                        CityFringeYardKind.SouthTunnelForecourt)
                    {
                        ValidateSurfaceCorners(
                            layout,
                            yard,
                            part,
                            "tunnel service spur",
                            0.02f,
                            0.10f);
                    }
                    else
                    {
                        ValidateSurfaceContact(
                            layout,
                            yard,
                            part,
                            "service spur",
                            0.015f,
                            yard.Kind == CityFringeYardKind.SouthFloodWorks
                                ? 0.14f
                                : 0.06f);
                    }
                    GetDepthRange(
                        yard,
                        part.Footprint,
                        out float minimumDepth,
                        out float maximumDepth);
                    spurMinimumDepth = Mathf.Min(
                        spurMinimumDepth,
                        minimumDepth);
                    spurMaximumDepth = Mathf.Max(
                        spurMaximumDepth,
                        maximumDepth);
                    hasServiceSpur = true;
                }
                else if ((yard.Kind ==
                              CityFringeYardKind.SouthFloodWorks ||
                          yard.Kind ==
                              CityFringeYardKind.SouthTunnelForecourt) &&
                         part.Kind == CityFringeYardPartKind.AccessApron)
                {
                    ValidateNarrowAccessTrace(layout, yard, part);
                }
                else if (yard.Kind ==
                             CityFringeYardKind.SouthTunnelForecourt &&
                         part.Kind == CityFringeYardPartKind.WheelRut)
                {
                    ValidateColliderlessRole(yard, part, "tunnel wheel rut");
                    ValidateSurfaceCorners(
                        layout,
                        yard,
                        part,
                        "tunnel wheel rut",
                        0.02f,
                        0.10f);
                }
                else if (part.Kind ==
                         CityFringeYardPartKind.ForefieldAnchor)
                {
                    ValidateForefieldAnchor(layout, yard, part);
                    if (depth <
                            CityFringeYardPlanner
                                .ForefieldRoadBandMaximumDepth -
                            GeometryTolerance ||
                        depth >
                            CityFringeYardPlanner
                                .ForefieldMiddleBandMaximumDepth +
                            GeometryTolerance)
                    {
                        throw new InvalidOperationException(
                            $"Fringe part '{part.StableId}' leaves the " +
                            "middle forefield band.");
                    }

                    forefieldAnchors.Add(LongCoordinate(yard, part.Center));
                }

                if (part.Kind == CityFringeYardPartKind.UtilityPole)
                {
                    poleCoordinates.Add(LongCoordinate(yard, part.Center));
                }
            }

            bool expectsServiceTrack =
                yard.Kind != CityFringeYardKind.SouthFloodWorks;
            if ((expectsServiceTrack && !hasTrack) ||
                (!expectsServiceTrack && hasTrack) ||
                !hasDrain)
            {
                throw new InvalidOperationException(
                    $"Fringe Yard '{yard.AreaId}' has the wrong service-" +
                    "track or drainage contract.");
            }

            if (isMountainYard)
            {
                ValidateForefieldBands(
                    yard,
                    hasRoadShoulder,
                    hasServiceSpur,
                    hasToeTrack,
                    spurMinimumDepth,
                    spurMaximumDepth,
                    trackMaximumDepth,
                    forefieldAnchors);
                ValidatePoleSpacing(yard, poleCoordinates);
            }
            else if (hasRoadShoulder ||
                     hasServiceSpur ||
                     forefieldAnchors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Non-mountain fringe Yard '{yard.AreaId}' acquired " +
                    "mountain forefield vocabulary.");
            }

            if (hasRepairStock &&
                yard.Kind != CityFringeYardKind.WestIndustrialBelt)
            {
                throw new InvalidOperationException(
                    $"Fringe Yard '{yard.AreaId}' acquired generic repair " +
                    "stock outside the industrial service belt.");
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
            CityRoadGroundBoundaryPlan roadGroundBoundaries,
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

            if (part.BlocksMovement)
            {
                ValidateStepSafeSeamsClear(
                    roadGroundBoundaries,
                    yard,
                    part);
            }

            bool isLowReturn =
                part.Kind == CityFringeYardPartKind.Gabion &&
                part.StableId.IndexOf(
                    "forefield-low-return-",
                    StringComparison.Ordinal) >= 0;
            bool requiresCollision =
                part.Kind == CityFringeYardPartKind.RockfallMass ||
                (part.Kind == CityFringeYardPartKind.TerraceShelf &&
                 part.Size.y >= 0.20f) ||
                isLowReturn;
            if (requiresCollision && !part.BlocksMovement)
            {
                throw new InvalidOperationException(
                    $"Fringe mass '{part.StableId}' must block movement.");
            }

            if (isLowReturn)
            {
                if (part.Size.x >
                    CityFringeYardPlanner.MaximumLowReturnDepth +
                    GeometryTolerance)
                {
                    throw new InvalidOperationException(
                        $"Fringe low return '{part.StableId}' spans too " +
                        "far across the terrain slope.");
                }

                ValidateSurfaceContact(
                    layout,
                    yard,
                    part,
                    "low return",
                    0.015f,
                    0.14f);
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

        private static void ValidateRoadShoulder(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            CityFringeYardPartDescriptor part)
        {
            ValidateColliderlessRole(yard, part, "road shoulder");
            if (part.Size.x >
                CityFringeYardPlanner.MaximumRoadShoulderWidth +
                GeometryTolerance)
            {
                throw new InvalidOperationException(
                    $"Fringe road shoulder '{part.StableId}' is wide " +
                    "enough to read as a raised plate.");
            }

            ValidateSurfaceContact(
                layout,
                yard,
                part,
                "road shoulder",
                0.015f,
                0.06f);
            GetDepthRange(
                yard,
                part.Footprint,
                out float minimumDepth,
                out float maximumDepth);
            if (minimumDepth < -GeometryTolerance ||
                maximumDepth >
                CityFringeYardPlanner.ForefieldRoadBandMaximumDepth +
                GeometryTolerance)
            {
                throw new InvalidOperationException(
                    $"Fringe part '{part.StableId}' leaves the road-side " +
                    "forefield band.");
            }
        }

        private static void ValidateMountainSurfacePolicy(
            CityFringeYardDescriptor yard,
            CityFringeYardPartDescriptor part)
        {
            if (part.Kind == CityFringeYardPartKind.RepairPad ||
                part.Kind == CityFringeYardPartKind.SiltFan)
            {
                throw new InvalidOperationException(
                    $"Mountain Yard '{yard.AreaId}' acquired a broad " +
                    $"floating surface platform '{part.StableId}'.");
            }

            bool isSurfaceTrace =
                part.Kind == CityFringeYardPartKind.ServiceTrack ||
                (yard.Kind == CityFringeYardKind.SouthTunnelForecourt &&
                 (part.Kind == CityFringeYardPartKind.AccessApron ||
                  part.Kind == CityFringeYardPartKind.ServiceSpur ||
                  part.Kind == CityFringeYardPartKind.WheelRut ||
                  part.Kind == CityFringeYardPartKind.ForefieldAnchor)) ||
                (yard.Kind == CityFringeYardKind.SouthFloodWorks &&
                 (part.Kind == CityFringeYardPartKind.AccessApron ||
                  part.Kind == CityFringeYardPartKind.RoadShoulder ||
                  part.Kind == CityFringeYardPartKind.ServiceSpur ||
                  part.Kind == CityFringeYardPartKind.ForefieldAnchor ||
                  part.Kind == CityFringeYardPartKind.DrainChannel));
            bool isTunnelTrace =
                yard.Kind == CityFringeYardKind.SouthTunnelForecourt &&
                (part.Kind == CityFringeYardPartKind.ServiceTrack ||
                 part.Kind == CityFringeYardPartKind.AccessApron ||
                 part.Kind == CityFringeYardPartKind.ServiceSpur ||
                 part.Kind == CityFringeYardPartKind.WheelRut ||
                 part.Kind == CityFringeYardPartKind.ForefieldAnchor);
            float maximumWidth = isTunnelTrace
                ? CityFringeYardPlanner.MaximumTunnelTraceWidth
                : CityFringeYardPlanner.MaximumRoadShoulderWidth;
            if (isSurfaceTrace &&
                part.Size.x >
                    maximumWidth + GeometryTolerance)
            {
                throw new InvalidOperationException(
                    $"Mountain surface trace '{part.StableId}' is wider " +
                    "than the narrow terrain-mark contract.");
            }
        }

        private static void ValidateForefieldAnchor(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            CityFringeYardPartDescriptor part)
        {
            ValidateColliderlessRole(yard, part, "forefield anchor");
            float maximumWidth =
                yard.Kind == CityFringeYardKind.SouthTunnelForecourt
                    ? CityFringeYardPlanner.MaximumTunnelTraceWidth
                    : CityFringeYardPlanner.MaximumForefieldAnchorWidth;
            if (part.Size.x >
                maximumWidth + GeometryTolerance)
            {
                throw new InvalidOperationException(
                    $"Fringe anchor '{part.StableId}' is wide enough to " +
                    "read as a raised plate.");
            }

            Vector3 forward = part.Rotation * Vector3.forward;
            forward.y = 0f;
            Vector3 outward = yard.Access.OutwardNormal;
            outward.y = 0f;
            if (forward.sqrMagnitude <= 0.001f ||
                Vector3.Dot(forward.normalized, outward.normalized) < 0.99f)
            {
                throw new InvalidOperationException(
                    $"Fringe anchor '{part.StableId}' must follow the " +
                    "road-to-mountain terrain slope.");
            }

            if (yard.Kind == CityFringeYardKind.SouthTunnelForecourt)
            {
                ValidateSurfaceCorners(
                    layout,
                    yard,
                    part,
                    "tunnel forefield anchor",
                    0.02f,
                    0.10f);
            }
            else
            {
                ValidateSurfaceContact(
                    layout,
                    yard,
                    part,
                    "forefield anchor",
                    0.015f,
                    0.06f);
            }
        }

        private static void ValidateNarrowAccessTrace(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            CityFringeYardPartDescriptor part)
        {
            bool isFlood =
                yard.Kind == CityFringeYardKind.SouthFloodWorks;
            string role = isFlood
                ? "flood access trace"
                : "tunnel approach trace";
            ValidateColliderlessRole(yard, part, role);
            float maximumWidth = isFlood
                ? CityFringeYardPlanner.MaximumRoadShoulderWidth
                : CityFringeYardPlanner.MaximumTunnelTraceWidth;
            if (part.Size.x >
                maximumWidth +
                GeometryTolerance)
            {
                throw new InvalidOperationException(
                    $"Fringe {role} '{part.StableId}' is wide enough to " +
                    "read as a raised earth slab.");
            }

            ValidateSurfaceCorners(
                layout,
                yard,
                part,
                role,
                isFlood ? 0.04f : 0.02f,
                isFlood ? 0.16f : 0.10f);
        }

        private static void ValidateSurfaceCorners(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            CityFringeYardPartDescriptor part,
            string role,
            float maximumGap,
            float maximumEmbed)
        {
            Vector3 right =
                (part.Rotation * Vector3.right) * (part.Size.x * 0.5f);
            Vector3 forward =
                (part.Rotation * Vector3.forward) * (part.Size.z * 0.5f);
            Vector3 down =
                (part.Rotation * Vector3.up) * (part.Size.y * 0.5f);
            for (int rightSign = -1; rightSign <= 1; rightSign += 2)
            {
                for (int forwardSign = -1;
                     forwardSign <= 1;
                     forwardSign += 2)
                {
                    Vector3 corner = part.Center - down +
                        right * rightSign +
                        forward * forwardSign;
                    ValidateSurfacePoint(
                        layout,
                        yard,
                        part,
                        role,
                        corner,
                        maximumGap,
                        maximumEmbed);
                }
            }
        }

        private static void ValidateSurfaceContact(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            CityFringeYardPartDescriptor part,
            string role,
            float maximumGap,
            float maximumEmbed)
        {
            Vector3 bottom = part.Center -
                (part.Rotation * Vector3.up) * (part.Size.y * 0.5f);
            ValidateSurfacePoint(
                layout,
                yard,
                part,
                role,
                bottom,
                maximumGap,
                maximumEmbed);
        }

        private static void ValidateSurfacePoint(
            CityLayout layout,
            CityFringeYardDescriptor yard,
            CityFringeYardPartDescriptor part,
            string role,
            Vector3 point,
            float maximumGap,
            float maximumEmbed)
        {
            Vector2 sample = new Vector2(point.x, point.z);
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (!string.Equals(
                        surface.AreaId,
                        yard.AreaId,
                        StringComparison.Ordinal) ||
                    !Contains(
                        surface.WorldBounds,
                        sample,
                        GeometryTolerance))
                {
                    continue;
                }

                float ground = CityTerrainSurfacePlan.SampleTop(
                    layout,
                    surface,
                    sample);
                float offset = point.y - ground;
                if (offset > maximumGap || offset < -maximumEmbed)
                {
                    throw new InvalidOperationException(
                        $"Fringe {role} '{part.StableId}' is not seated " +
                        $"on its terrain surface (bottom offset " +
                        $"{offset:0.000} m).");
                }

                return;
            }

            throw new InvalidOperationException(
                $"Fringe {role} '{part.StableId}' has no terrain sample.");
        }

        private static void ValidateColliderlessRole(
            CityFringeYardDescriptor yard,
            CityFringeYardPartDescriptor part,
            string role)
        {
            if (part.BlocksMovement)
            {
                throw new InvalidOperationException(
                    $"Fringe Yard '{yard.AreaId}' {role} must remain " +
                    "collider-free.");
            }
        }

        private static void ValidateForefieldBands(
            CityFringeYardDescriptor yard,
            bool hasRoadShoulder,
            bool hasServiceSpur,
            bool hasToeTrack,
            float spurMinimumDepth,
            float spurMaximumDepth,
            float trackMaximumDepth,
            List<float> forefieldAnchors)
        {
            if (!hasRoadShoulder ||
                !hasServiceSpur ||
                !hasToeTrack ||
                spurMinimumDepth >
                    CityFringeYardPlanner.ForefieldRoadBandMaximumDepth +
                    GeometryTolerance ||
                spurMaximumDepth <
                    CityFringeYardPlanner.ForefieldMiddleBandMaximumDepth -
                    GeometryTolerance ||
                spurMaximumDepth > trackMaximumDepth + 0.12f)
            {
                throw new InvalidOperationException(
                    $"Fringe Yard '{yard.AreaId}' does not connect its " +
                    "road, middle and toe-service depth bands.");
            }

            if (forefieldAnchors.Count <
                    CityFringeYardPlanner.MinimumForefieldAnchorCount ||
                forefieldAnchors.Count >
                    CityFringeYardPlanner.MaximumForefieldAnchorCount)
            {
                throw new InvalidOperationException(
                    $"Fringe Yard '{yard.AreaId}' needs three or four " +
                    "explicit forefield anchors.");
            }

            forefieldAnchors.Sort();
            GetLongRange(yard, out float minimum, out float maximum);
            float previous = minimum;
            for (int index = 0; index < forefieldAnchors.Count; index++)
            {
                float coordinate = forefieldAnchors[index];
                if (coordinate - previous >
                    CityFringeYardPlanner.MaximumForefieldAnchorGap +
                    GeometryTolerance)
                {
                    throw new InvalidOperationException(
                        $"Fringe Yard '{yard.AreaId}' has an empty " +
                        "forefield interval wider than 40 metres.");
                }

                previous = coordinate;
            }

            if (maximum - previous >
                CityFringeYardPlanner.MaximumForefieldAnchorGap +
                GeometryTolerance)
            {
                throw new InvalidOperationException(
                    $"Fringe Yard '{yard.AreaId}' leaves its final " +
                    "forefield interval empty.");
            }
        }

        private static void ValidatePoleSpacing(
            CityFringeYardDescriptor yard,
            List<float> poleCoordinates)
        {
            float maximumSpacing =
                yard.Kind == CityFringeYardKind.WestStoneTerraces
                    ? 40f
                    : 34f;
            poleCoordinates.Sort();
            for (int index = 1; index < poleCoordinates.Count; index++)
            {
                if (poleCoordinates[index] - poleCoordinates[index - 1] >
                    maximumSpacing + GeometryTolerance)
                {
                    throw new InvalidOperationException(
                        $"Fringe Yard '{yard.AreaId}' utility poles exceed " +
                        $"their {maximumSpacing:0}-metre maximum spacing.");
                }
            }
        }

        private static void ValidateStepSafeSeamsClear(
            CityRoadGroundBoundaryPlan boundaries,
            CityFringeYardDescriptor yard,
            CityFringeYardPartDescriptor part)
        {
            float radius = CityGroundTraversalPlanner.MaximumAgentRadius;
            Rect expandedPart = Expand(part.Footprint, radius + 0.05f);
            for (int index = 0;
                 index < boundaries.SafeConnections.Count;
                 index++)
            {
                CityRoadGroundBoundarySpan span =
                    boundaries.SafeConnections[index];
                if (!string.Equals(
                        span.Surface.AreaId,
                        yard.AreaId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                Rect seam = span.CreateConnector(radius + 0.10f);
                if (expandedPart.Overlaps(seam))
                {
                    throw new InvalidOperationException(
                        $"Fringe part '{part.StableId}' blocks a step-safe " +
                        "road seam for the player capsule.");
                }
            }
        }

        private static float DepthFromRoad(
            CityFringeYardDescriptor yard,
            Vector3 point)
        {
            Vector3 outward = yard.Access.OutwardNormal;
            if (outward.x < -0.5f)
            {
                return yard.AreaBounds.xMax - point.x;
            }

            if (outward.x > 0.5f)
            {
                return point.x - yard.AreaBounds.xMin;
            }

            if (outward.z < -0.5f)
            {
                return yard.AreaBounds.yMax - point.z;
            }

            return point.z - yard.AreaBounds.yMin;
        }

        private static void GetDepthRange(
            CityFringeYardDescriptor yard,
            Rect footprint,
            out float minimum,
            out float maximum)
        {
            Vector3 outward = yard.Access.OutwardNormal;
            if (outward.x < -0.5f)
            {
                minimum = yard.AreaBounds.xMax - footprint.xMax;
                maximum = yard.AreaBounds.xMax - footprint.xMin;
            }
            else if (outward.x > 0.5f)
            {
                minimum = footprint.xMin - yard.AreaBounds.xMin;
                maximum = footprint.xMax - yard.AreaBounds.xMin;
            }
            else if (outward.z < -0.5f)
            {
                minimum = yard.AreaBounds.yMax - footprint.yMax;
                maximum = yard.AreaBounds.yMax - footprint.yMin;
            }
            else
            {
                minimum = footprint.yMin - yard.AreaBounds.yMin;
                maximum = footprint.yMax - yard.AreaBounds.yMin;
            }
        }

        private static float LongCoordinate(
            CityFringeYardDescriptor yard,
            Vector3 point)
        {
            return Mathf.Abs(yard.Access.OutwardNormal.x) > 0.5f
                ? point.z
                : point.x;
        }

        private static void GetLongRange(
            CityFringeYardDescriptor yard,
            out float minimum,
            out float maximum)
        {
            if (Mathf.Abs(yard.Access.OutwardNormal.x) > 0.5f)
            {
                minimum = yard.AreaBounds.yMin;
                maximum = yard.AreaBounds.yMax;
            }
            else
            {
                minimum = yard.AreaBounds.xMin;
                maximum = yard.AreaBounds.xMax;
            }
        }

        private static Rect Expand(Rect source, float amount)
        {
            return Rect.MinMaxRect(
                source.xMin - amount,
                source.yMin - amount,
                source.xMax + amount,
                source.yMax + amount);
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
