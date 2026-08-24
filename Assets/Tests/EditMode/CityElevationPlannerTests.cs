using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityElevationPlannerTests
    {
        private const int Seed = 481516;
        private const float Tolerance = 0.001f;
        private const float MinimumGlobalRange = 8f;
        private const float MinimumDistrictRange = 1.5f;

        private static readonly CityDistrictKind[] UrbanDistricts =
        {
            CityDistrictKind.OldTown,
            CityDistrictKind.Residential,
            CityDistrictKind.Industrial,
            CityDistrictKind.Nightlife
        };

        private CityLayout defaultLayout;

        [OneTimeSetUp]
        public void CreateDefaultLayout()
        {
            defaultLayout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                Seed);
        }

        [Test]
        public void Create_WithSameSeed_ProducesIdenticalElevationPlan()
        {
            CityLayout repeated = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                Seed);
            CityElevationPlan expected = defaultLayout.ElevationPlan;
            CityElevationPlan actual = repeated.ElevationPlan;

            Assert.That(actual.BlueprintId, Is.EqualTo(expected.BlueprintId));
            Assert.That(actual.Seed, Is.EqualTo(expected.Seed));
            Assert.That(actual.IsElevated, Is.EqualTo(expected.IsElevated));
            Assert.That(actual.WorldOrigin, Is.EqualTo(expected.WorldOrigin));
            Assert.That(actual.NodeSpacing, Is.EqualTo(expected.NodeSpacing));
            Assert.That(actual.RoadWidth, Is.EqualTo(expected.RoadWidth));
            Assert.That(
                actual.NodeElevations.Count,
                Is.EqualTo(expected.NodeElevations.Count));
            foreach (KeyValuePair<Vector2Int, float> pair in
                     expected.NodeElevations)
            {
                Assert.That(
                    actual.GetNodeElevation(pair.Key),
                    Is.EqualTo(pair.Value),
                    pair.Key.ToString());
            }

            Assert.That(
                actual.CellElevations.Count,
                Is.EqualTo(expected.CellElevations.Count));
            foreach (KeyValuePair<Vector2Int, float> pair in
                     expected.CellElevations)
            {
                Assert.That(
                    actual.GetCellElevation(pair.Key),
                    Is.EqualTo(pair.Value),
                    pair.Key.ToString());
            }

            Assert.That(
                actual.Transitions.Count,
                Is.EqualTo(expected.Transitions.Count));
            foreach (RoadEdge edge in defaultLayout.RoadEdges)
            {
                AssertTransitionEqual(
                    expected.GetTransition(edge),
                    actual.GetTransition(edge));
            }

            Assert.That(
                actual.SignatureStairs.Count,
                Is.EqualTo(expected.SignatureStairs.Count));
            for (int index = 0;
                 index < expected.SignatureStairs.Count;
                 index++)
            {
                AssertStairEqual(
                    expected.SignatureStairs[index],
                    actual.SignatureStairs[index]);
            }
        }

        [Test]
        public void DefaultCity_HasSeriousGlobalAndLocalElevationRanges()
        {
            CityElevationPlan plan = defaultLayout.ElevationPlan;

            Assert.That(plan.IsElevated, Is.True);
            Assert.That(
                plan.MaximumElevation - plan.MinimumElevation,
                Is.GreaterThanOrEqualTo(MinimumGlobalRange),
                "The default city needs a readable city-wide height range.");
            foreach (CityDistrictKind district in UrbanDistricts)
            {
                float[] elevations = defaultLayout.Blueprint.Cells
                    .Where(cell =>
                        cell.Area.Archetype == district &&
                        !cell.IsWater)
                    .Select(cell => plan.GetCellElevation(cell.Cell))
                    .ToArray();

                Assert.That(elevations, Is.Not.Empty, district.ToString());
                Assert.That(
                    elevations.Max() - elevations.Min(),
                    Is.GreaterThanOrEqualTo(MinimumDistrictRange),
                    $"{district} needs meaningful local variation.");
            }
        }

        [Test]
        [Category("CityTraversal")]
        public void DefaultCity_KeepsAllWaterOnTheGlobalCoastalDatum()
        {
            // The lake and its elevated basin are gone; every water
            // cell left in the city is the sea, and the sea stays on
            // the global coastal datum whatever its access does.
            CityElevationPlan plan = defaultLayout.ElevationPlan;
            CityBlueprintCell[] seaCells = defaultLayout.Blueprint.Cells
                .Where(cell => cell.IsWater)
                .ToArray();
            Assert.That(seaCells, Is.Not.Empty);
            foreach (CityBlueprintCell cell in seaCells)
            {
                Assert.That(
                    plan.GetCellElevation(cell.Cell),
                    Is.EqualTo(0f).Within(Tolerance),
                    $"{cell.Area.Feature} water at {cell.Cell}");
            }
        }

        [Test]
        [Category("CityRiver")]
        public void DefaultCity_RiverDescendsToSeaInsideTenMeterChannel()
        {
            CityRiverDefinition river = defaultLayout.Blueprint.River;
            Assert.That(river, Is.Not.Null);

            Assert.That(
                CityRiverPlanner.ResolveWaterY(
                    river,
                    river.CoreMinimumZ),
                Is.EqualTo(2.4f).Within(Tolerance));
            Assert.That(
                CityRiverPlanner.ResolveWaterY(
                    river,
                    river.CoreMaximumZExclusive + 1),
                Is.EqualTo(0f).Within(Tolerance));

            CitySurfaceDescriptor[] riverSurfaces = defaultLayout.Surfaces
                .Where(surface =>
                    surface.Kind == CitySurfaceKind.RiverWater)
                .OrderBy(surface => surface.Cell.y)
                .ToArray();
            Assert.That(
                riverSurfaces,
                Has.Length.EqualTo(
                    river.CoreMaximumZExclusive - river.CoreMinimumZ));

            float previousWater = float.PositiveInfinity;
            for (int index = 0; index < riverSurfaces.Length; index++)
            {
                CitySurfaceDescriptor surface = riverSurfaces[index];
                int z = river.CoreMinimumZ + index;
                float southWater = CityRiverPlanner.ResolveWaterY(river, z);
                float northWater = CityRiverPlanner.ResolveWaterY(
                    river,
                    z + 1);
                Assert.That(surface.Cell, Is.EqualTo(new Vector2Int(
                    river.CorridorCellX,
                    z)));
                Assert.That(
                    surface.WorldBounds.width,
                    Is.EqualTo(river.ChannelWidth).Within(Tolerance));
                Assert.That(
                    surface.DatumY,
                    Is.EqualTo((southWater + northWater) * 0.5f)
                        .Within(Tolerance));
                Assert.That(surface.IsWater, Is.True);
                Assert.That(surface.IsWalkable, Is.False);
                Assert.That(southWater, Is.LessThanOrEqualTo(
                    previousWater + Tolerance));

                float westBank = defaultLayout.ElevationPlan
                    .GetNodeElevation(new Vector2Int(
                        river.CorridorCellX,
                        z));
                float eastBank = defaultLayout.ElevationPlan
                    .GetNodeElevation(new Vector2Int(
                        river.CorridorCellX + 1,
                        z));
                Assert.That(
                    eastBank,
                    Is.EqualTo(westBank).Within(Tolerance));
                Assert.That(
                    westBank - southWater,
                    Is.EqualTo(1.8f).Within(Tolerance));
                previousWater = northWater;
            }
        }

        [Test]
        public void DefaultCity_ClassifiesEveryRoadWithinBusGrade()
        {
            CityElevationPlan plan = defaultLayout.ElevationPlan;

            Assert.That(
                plan.Transitions.Count,
                Is.EqualTo(defaultLayout.RoadEdges.Count));
            foreach (RoadEdge edge in defaultLayout.RoadEdges)
            {
                CityElevationTransitionDescriptor transition =
                    plan.GetTransition(edge);
                float delta = Mathf.Abs(
                    plan.GetNodeElevation(edge.B) -
                    plan.GetNodeElevation(edge.A));
                CityElevationTransitionKind expectedKind =
                    delta <= 0.02f
                        ? CityElevationTransitionKind.Level
                        : CityElevationTransitionKind.VehicleGrade;

                Assert.That(transition.Edge, Is.EqualTo(edge));
                Assert.That(
                    transition.PathKind,
                    Is.EqualTo(defaultLayout.GetPathKind(edge)));
                Assert.That(transition.Kind, Is.EqualTo(expectedKind));
                Assert.That(
                    transition.GradePercent,
                    Is.GreaterThanOrEqualTo(0f));
                Assert.That(
                    transition.GradePercent,
                    Is.LessThanOrEqualTo(
                        CityElevationPlan.MaximumBusGradePercent +
                        Tolerance),
                    edge.ToString());
                Assert.That(
                    transition.Mobility &
                    (CityTraversalMobility.Player |
                     CityTraversalMobility.Pedestrian),
                    Is.EqualTo(
                        CityTraversalMobility.Player |
                        CityTraversalMobility.Pedestrian));
                if (transition.PathKind == CityPathKind.Street)
                {
                    Assert.That(
                        transition.Mobility &
                        (CityTraversalMobility.Vehicle |
                         CityTraversalMobility.Bus),
                        Is.EqualTo(
                            CityTraversalMobility.Vehicle |
                            CityTraversalMobility.Bus));
                }
            }
        }

        [Test]
        [Category("CityTraversal")]
        public void DefaultCity_UsesOneSafeRoadGroundBoundaryContract()
        {
            CityRoadGroundBoundaryPlan boundaries =
                CityRoadGroundBoundaryPlanner.Create(defaultLayout);
            CityGroundTraversalPlan traversal =
                CityGroundTraversalPlanner.CreatePlan(defaultLayout);
            RoadWalkableArea walkable =
                RoadWalkableArea.FromLayout(defaultLayout);

            Assert.That(boundaries.SafeConnections, Is.Not.Empty);
            Assert.That(boundaries.ProtectedDrops, Is.Not.Empty);
            float connectorReach =
                (CityGroundTraversalPlanner.MaximumAgentRadius * 2f) +
                0.1f;
            foreach (CityRoadGroundBoundarySpan safe in
                     boundaries.SafeConnections)
            {
                Assert.That(
                    CityRoadGroundBoundaryPlanner
                        .SupportsGroundTraversal(safe.Surface),
                    Is.True,
                    safe.Surface.Cell.ToString());
                Assert.That(
                    Mathf.Abs(
                        safe.FirstTravelTopY -
                        safe.FirstGroundTopY),
                    Is.LessThanOrEqualTo(
                        CityRoadGroundBoundaryPlanner.MaximumSafeStep +
                        Tolerance));
                Assert.That(
                    Mathf.Abs(
                        safe.SecondTravelTopY -
                        safe.SecondGroundTopY),
                    Is.LessThanOrEqualTo(
                        CityRoadGroundBoundaryPlanner.MaximumSafeStep +
                        Tolerance));
                Assert.That(
                    safe.Length,
                    Is.GreaterThanOrEqualTo(
                        CityGroundTraversalPlanner.MaximumAgentRadius *
                        2f));
                Assert.That(
                    traversal.ConnectorRectangles,
                    Does.Contain(safe.CreateConnector(connectorReach)));
                if (RequiresAuthoredOpenAreaAccess(safe.Surface))
                {
                    Assert.That(
                        defaultLayout.OpenAreaAccesses.Any(access =>
                            access.Cell == safe.Surface.Cell &&
                            access.FrontageEdge == safe.Edge &&
                            SpanFitsOpening(
                                safe,
                                access.Center,
                                access.Width)),
                        Is.True,
                        safe.Surface.AreaId);
                }
            }

            foreach (CityOpenAreaAccessDescriptor access in
                     defaultLayout.OpenAreaAccesses)
            {
                Assert.That(
                    walkable.Contains(access.Center, 0.28f),
                    Is.True,
                    access.Id);
            }

            Assert.That(
                defaultLayout.Park.Gates.Count(gate =>
                    walkable.Contains(gate.Center, 0.28f)),
                Is.GreaterThanOrEqualTo(1),
                "At least one level-safe gate must keep the terraced park " +
                 "connected to the street network.");
        }

        [Test]
        [Category("CityTraversal")]
        public void DefaultCity_ContinuousGroundSeamsStayTraversable()
        {
            var byCell = defaultLayout.Surfaces.ToDictionary(
                surface => surface.Cell);
            CityGroundTraversalPlan traversal =
                CityGroundTraversalPlanner.CreatePlan(defaultLayout);
            Vector2Int[] directions =
            {
                Vector2Int.right,
                Vector2Int.up
            };
            int continuousSeamCount = 0;
            int repairedTerraceSeamCount = 0;

            foreach (CitySurfaceDescriptor surface in
                     defaultLayout.Surfaces)
            {
                if (!CityTerrainSurfacePlan.UsesContinuousTop(surface))
                {
                    continue;
                }

                if (surface.Kind != CitySurfaceKind.BuildableGround &&
                    !surface.IsWalkable)
                {
                    continue;
                }

                for (int directionIndex = 0;
                     directionIndex < directions.Length;
                     directionIndex++)
                {
                    Vector2Int direction = directions[directionIndex];
                    if (!byCell.TryGetValue(
                            surface.Cell + direction,
                            out CitySurfaceDescriptor neighbour) ||
                        !CityTerrainSurfacePlan.UsesContinuousTop(neighbour) ||
                        (neighbour.Kind !=
                             CitySurfaceKind.BuildableGround &&
                         !neighbour.IsWalkable) ||
                        defaultLayout.HasRoad(
                            RoadEdge.ForCellFrontage(
                                surface.Cell,
                                direction)))
                    {
                        continue;
                    }

                    bool horizontal = direction.y != 0;
                    float fixedCoordinate = horizontal
                        ? (surface.WorldBounds.yMax +
                           neighbour.WorldBounds.yMin) * 0.5f
                        : (surface.WorldBounds.xMax +
                           neighbour.WorldBounds.xMin) * 0.5f;
                    float minimum = horizontal
                        ? Mathf.Max(
                            surface.WorldBounds.xMin,
                            neighbour.WorldBounds.xMin)
                        : Mathf.Max(
                            surface.WorldBounds.yMin,
                            neighbour.WorldBounds.yMin);
                    float maximum = horizontal
                        ? Mathf.Min(
                            surface.WorldBounds.xMax,
                            neighbour.WorldBounds.xMax)
                        : Mathf.Min(
                            surface.WorldBounds.yMax,
                            neighbour.WorldBounds.yMax);
                    Assert.That(
                        maximum,
                        Is.GreaterThan(minimum),
                        $"{surface.Cell} -> {neighbour.Cell}");
                    Assert.That(
                        CityRoadGroundBoundaryPlanner.IsGroundBoundarySafe(
                            defaultLayout,
                            surface,
                            neighbour,
                            horizontal,
                            fixedCoordinate,
                            minimum,
                            maximum),
                        Is.True,
                        $"{surface.Cell} -> {neighbour.Cell}");

                    float middle = (minimum + maximum) * 0.5f;
                    Vector2 seamCenter = horizontal
                        ? new Vector2(middle, fixedCoordinate)
                        : new Vector2(fixedCoordinate, middle);
                    Assert.That(
                        traversal.ConnectorRectangles.Any(
                            connector => connector.Contains(seamCenter)),
                        Is.True,
                        $"{surface.Cell} -> {neighbour.Cell}");

                    continuousSeamCount++;
                    if (Mathf.Abs(
                            surface.PhysicalTopY -
                            neighbour.PhysicalTopY) >
                        CityRoadGroundBoundaryPlanner.MaximumSafeStep)
                    {
                        repairedTerraceSeamCount++;
                    }
                }
            }

            Assert.That(continuousSeamCount, Is.GreaterThan(0));
            Assert.That(
                repairedTerraceSeamCount,
                Is.GreaterThan(0),
                "The production fixture must cover seams that the former " +
                "constant-terrace comparison incorrectly blocked.");
        }

        [Test]
        public void DefaultCity_RebasesCriticalLotsParkAndAccessAnchors()
        {
            CityElevationPlan plan = defaultLayout.ElevationPlan;
            BuildingLot[] criticalLots = defaultLayout.BuildingLots
                .Where(lot =>
                    lot.IsPlayerHome ||
                    lot.IsSupermarket ||
                    lot.IsBar)
                .ToArray();

            Assert.That(criticalLots, Is.Not.Empty);
            foreach (BuildingLot lot in criticalLots)
            {
                float lotDatum = plan.GetCellElevation(lot.Cell);
                Assert.That(
                    lot.Center.y,
                    Is.EqualTo(lotDatum).Within(Tolerance),
                    lot.Cell.ToString());
                Assert.That(
                    lot.DoorPosition.y,
                    Is.EqualTo(lotDatum).Within(Tolerance),
                    lot.Cell.ToString());
                Assert.That(lotDatum, Is.GreaterThan(0f));

                RoadEdge frontage = RoadEdge.ForCellFrontage(
                    lot.Cell,
                    lot.FrontageDirection);
                AssertRoadAnchorMatches(
                    plan,
                    frontage,
                    lot.ReturnPosition,
                    "road return");
                AssertRoadAnchorMatches(
                    plan,
                    frontage,
                    lot.SidewalkArrivalPosition,
                    "sidewalk arrival");
            }

            Assert.That(
                plan.TrySampleSurface(
                    new Vector2(
                        defaultLayout.Park.Center.x,
                        defaultLayout.Park.Center.z),
                    CitySurfaceRole.RoadDatum,
                    out float parkDatum,
                    out _),
                Is.True);
            Assert.That(
                defaultLayout.Park.Center.y,
                Is.EqualTo(parkDatum).Within(Tolerance));
            Assert.That(parkDatum, Is.GreaterThan(0f));
            Assert.That(defaultLayout.Park.Gates, Is.Not.Empty);
            foreach (CityParkGateDescriptor gate in defaultLayout.Park.Gates)
            {
                Assert.That(
                    plan.TrySampleSurface(
                        new Vector2(gate.Center.x, gate.Center.z),
                        CitySurfaceRole.RoadDatum,
                        out float gateDatum,
                        out _),
                    Is.True,
                    gate.Id);
                Assert.That(
                    gate.Center.y,
                    Is.EqualTo(gateDatum).Within(Tolerance),
                    gate.Id);
                Assert.That(gate.Center.y, Is.GreaterThan(0f), gate.Id);
            }

            Assert.That(
                defaultLayout.DistrictPointsOfInterest,
                Is.Not.Empty);
            foreach (CityDistrictPointOfInterestDescriptor point in
                     defaultLayout.DistrictPointsOfInterest)
            {
                Assert.That(
                    point.Center.y,
                    Is.EqualTo(plan.GetCellElevation(point.Cell))
                        .Within(Tolerance),
                    point.Id);
                Assert.That(point.Center.y, Is.GreaterThan(0f), point.Id);
                Assert.That(point.Accesses, Is.Not.Empty, point.Id);
                foreach (
                    CityDistrictPointOfInterestAccessDescriptor access in
                    point.Accesses)
                {
                    AssertRoadAnchorMatches(
                        plan,
                        access.FrontageEdge,
                        access.Center,
                        access.Id);
                }
            }

            Assert.That(defaultLayout.OpenAreaAccesses, Is.Not.Empty);
            foreach (CityOpenAreaAccessDescriptor access in
                     defaultLayout.OpenAreaAccesses)
            {
                AssertRoadAnchorMatches(
                    plan,
                    access.FrontageEdge,
                    access.Center,
                    access.Id);
                CitySurfaceDescriptor[] areaSurfaces =
                    defaultLayout.Surfaces
                        .Where(surface =>
                            string.Equals(
                                surface.AreaId,
                                access.AreaId,
                                StringComparison.Ordinal) &&
                            surface.Kind != CitySurfaceKind.Water)
                        .ToArray();
                Assert.That(areaSurfaces, Is.Not.Empty, access.Id);
                Assert.That(
                    areaSurfaces.All(surface =>
                        Mathf.Abs(surface.DatumY - access.Center.y) <=
                        Tolerance),
                    Is.True,
                    $"{access.Id} must meet its authored terrace without " +
                    "a hidden vertical step.");
            }
        }

        [Test]
        public void DefaultCity_ProvidesValidSignatureExteriorStairs()
        {
            CityElevationPlan plan = defaultLayout.ElevationPlan;
            CityRoadGroundBoundaryPlan boundaries =
                CityRoadGroundBoundaryPlanner.Create(defaultLayout);
            CityStreetSurfacePlan streetSurfaces =
                CityStreetSurfacePlanner.Create(defaultLayout);
            RoadFencePlan roadFences =
                RoadFencePlanner.CreatePlan(defaultLayout);

            Assert.That(
                plan.SignatureStairs,
                Has.Count.EqualTo(UrbanDistricts.Length),
                "Each urban district profile declares one signature " +
                "exterior stair connection.");
            Assert.That(
                plan.SignatureStairs.Select(stair => stair.District),
                Is.EquivalentTo(UrbanDistricts));
            foreach (CityElevationStairDescriptor stair in
                     plan.SignatureStairs)
            {
                CityElevationStairPlacement placement =
                    CityElevationStairPlacementPlanner.Create(
                        defaultLayout,
                        stair);
                Assert.DoesNotThrow(
                    () => CityExteriorStairValidator.ValidateOrThrow(
                        placement.ExteriorPlan),
                    stair.Id);

                CityExteriorStairFlightDescriptor flight =
                    placement.ExteriorPlan.Flights.Single();
                Assert.That(flight.StepCount, Is.EqualTo(stair.StepCount));
                Assert.That(
                    flight.StepRise,
                    Is.EqualTo(stair.StepRise).Within(Tolerance));
                Assert.That(
                    flight.TreadDepth,
                    Is.EqualTo(stair.TreadDepth).Within(Tolerance));
                Assert.That(
                    flight.TotalRise,
                    Is.EqualTo(stair.TotalRise).Within(Tolerance));
                Assert.That(
                    placement.ExteriorPlan.Landings.All(landing =>
                        landing.Length >= 1.5f),
                    Is.True,
                    stair.Id);
                Assert.That(
                    placement.ExteriorPlan.Rails,
                    Has.Count.EqualTo(6),
                    stair.Id);
                Assert.That(
                    placement.LowerApproachEnd.y,
                    Is.EqualTo(
                        plan.GetNodeElevation(stair.LowerNode) +
                        CityStreetSurfacePlanner.SidewalkTop)
                        .Within(Tolerance),
                    stair.Id);
                Assert.That(
                    placement.UpperApproachStart.y,
                    Is.EqualTo(
                        plan.GetNodeElevation(stair.UpperNode) +
                        CityStreetSurfacePlanner.SidewalkTop)
                        .Within(Tolerance),
                    stair.Id);
                Assert.That(placement.Footprint.width, Is.GreaterThan(0f));
                Assert.That(placement.Footprint.height, Is.GreaterThan(0f));
                Assert.That(
                    Contains(
                        placement.GroundCutFootprint,
                        placement.LowerApproachFootprint),
                    Is.True,
                    stair.Id);
                Assert.That(
                    Contains(
                        placement.GroundCutFootprint,
                        placement.UpperApproachFootprint),
                    Is.True,
                    stair.Id);
                Assert.That(
                    Contains(
                        placement.GroundCutFootprint,
                        placement.Footprint),
                    Is.True,
                    stair.Id);
                AssertApproachInnerRail(
                    placement.LowerInnerRail,
                    placement.LowerApproachStart,
                    placement.LowerApproachEnd,
                    placement.SideDirection,
                    stair.Width,
                    $"{stair.Id} lower approach");
                AssertApproachInnerRail(
                    placement.UpperInnerRail,
                    placement.UpperApproachStart,
                    placement.UpperApproachEnd,
                    placement.SideDirection,
                    stair.Width,
                    $"{stair.Id} upper approach");
                AssertApproachClearsCrossingCarriageway(
                    defaultLayout,
                    stair,
                    stair.LowerNode,
                    placement.LowerInnerRail,
                    placement.LowerApproachFootprint,
                    $"{stair.Id} lower approach");
                AssertApproachClearsCrossingCarriageway(
                    defaultLayout,
                    stair,
                    stair.UpperNode,
                    placement.UpperInnerRail,
                    placement.UpperApproachFootprint,
                    $"{stair.Id} upper approach");
                AssertStairGuardClearance(
                    boundaries.SafeConnections,
                    boundaries.ProtectedDrops,
                    stair,
                    placement);

                CityElevationTransitionDescriptor parallelRoad =
                    plan.GetTransition(stair.Edge);
                Assert.That(
                    parallelRoad.Mobility & CityTraversalMobility.Bus,
                    Is.EqualTo(CityTraversalMobility.Bus),
                    stair.Id);

                AssertStreetGeometryClearsStair(
                    streetSurfaces.StreetGeometry,
                    placement,
                    stair.Id);
                Assert.That(
                    roadFences.Segments.Any(segment =>
                        IntersectsInterior(
                            CreateRoadFenceRailRect(segment),
                            placement.GroundCutFootprint)),
                    Is.False,
                    $"{stair.Id} corridor must not be crossed by a map " +
                    "boundary or dead-end fence collider.");
            }

        }

        [Test]
        public void LegacyAndCustomBlueprints_KeepFlatFallback()
        {
            CityLayout legacy = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            // A blueprint carrying a river is required to be terraced:
            // its banks must sit above the water, which a flat plan
            // cannot do. So the custom fixture clones the dry legacy
            // grid under a new id — the point of the case is that an
            // id the terracing does not know falls back to flat.
            CityBlueprint customBlueprint = CloneWithId(
                legacy.Blueprint,
                "custom-flat-elevation-fixture");
            CityLayout custom = CityLayoutGenerator.Generate(
                customBlueprint,
                CityGenerationSettings.Default,
                Seed);

            Assert.That(
                legacy.BlueprintId,
                Is.EqualTo(CityBlueprintCatalog.LegacyBlueprintId));
            Assert.That(custom.BlueprintId, Is.EqualTo(customBlueprint.Id));
            AssertFlatFallback(legacy.ElevationPlan);
            AssertFlatFallback(custom.ElevationPlan);
        }

        private static void AssertTransitionEqual(
            CityElevationTransitionDescriptor expected,
            CityElevationTransitionDescriptor actual)
        {
            Assert.That(actual.Edge, Is.EqualTo(expected.Edge));
            Assert.That(actual.PathKind, Is.EqualTo(expected.PathKind));
            Assert.That(actual.Kind, Is.EqualTo(expected.Kind));
            Assert.That(actual.Mobility, Is.EqualTo(expected.Mobility));
            Assert.That(
                actual.StartElevation,
                Is.EqualTo(expected.StartElevation));
            Assert.That(actual.EndElevation, Is.EqualTo(expected.EndElevation));
            Assert.That(actual.HorizontalRun, Is.EqualTo(expected.HorizontalRun));
            Assert.That(actual.GradePercent, Is.EqualTo(expected.GradePercent));
        }

        private static void AssertStairEqual(
            CityElevationStairDescriptor expected,
            CityElevationStairDescriptor actual)
        {
            Assert.That(actual.Id, Is.EqualTo(expected.Id));
            Assert.That(actual.District, Is.EqualTo(expected.District));
            Assert.That(actual.Edge, Is.EqualTo(expected.Edge));
            Assert.That(actual.LowerNode, Is.EqualTo(expected.LowerNode));
            Assert.That(actual.UpperNode, Is.EqualTo(expected.UpperNode));
            Assert.That(actual.Side, Is.EqualTo(expected.Side));
            Assert.That(actual.StepCount, Is.EqualTo(expected.StepCount));
            Assert.That(actual.StepRise, Is.EqualTo(expected.StepRise));
            Assert.That(actual.TreadDepth, Is.EqualTo(expected.TreadDepth));
            Assert.That(actual.Width, Is.EqualTo(expected.Width));
            Assert.That(actual.LandingLength, Is.EqualTo(expected.LandingLength));
        }

        private static void AssertStairGuardClearance(
            IReadOnlyList<CityRoadGroundBoundarySpan> safeConnections,
            IReadOnlyList<CityRoadGroundBoundarySpan> protectedDrops,
            CityElevationStairDescriptor stair,
            CityElevationStairPlacement placement)
        {
            for (int index = 0; index < safeConnections.Count; index++)
            {
                CityRoadGroundBoundarySpan safe = safeConnections[index];
                if (safe.Edge == stair.Edge)
                {
                    Assert.That(
                        IntersectsInterior(
                            safe,
                            placement.GroundCutFootprint),
                        Is.False,
                        $"{stair.Id} owns its complete traversal corridor");
                }
            }

            for (int index = 0; index < protectedDrops.Count; index++)
            {
                CityRoadGroundBoundarySpan span = protectedDrops[index];
                Assert.That(
                    IntersectsInterior(
                        span,
                        placement.GroundCutFootprint),
                    Is.False,
                    $"{stair.Id} traversal corridor must be clear of " +
                    "terrain guard rails");
                if (span.Edge != stair.Edge)
                {
                    continue;
                }

                Assert.That(
                    IntersectsInterior(span, placement.Footprint),
                    Is.False,
                    $"{stair.Id} flight/landing aperture");
                AssertApproachGuardAtOuterEdge(
                    span,
                    placement.LowerApproachFootprint,
                    placement.SideDirection,
                    $"{stair.Id} lower approach");
                AssertApproachGuardAtOuterEdge(
                    span,
                    placement.UpperApproachFootprint,
                    placement.SideDirection,
                    $"{stair.Id} upper approach");
            }

        }

        private static void AssertStreetGeometryClearsStair(
            IReadOnlyList<RuntimeOrientedBox> geometry,
            CityElevationStairPlacement placement,
            string stairId)
        {
            Assert.That(
                geometry.Any(box =>
                    IntersectsInterior(
                        CreateRect(box),
                        placement.Footprint)),
                Is.False,
                $"{stairId} flight and landings must not overlap a " +
                "street collider.");
            AssertStreetGeometryBelowApproach(
                geometry,
                placement.LowerApproachFootprint,
                placement.LowerApproachStart.y,
                $"{stairId} lower approach");
            AssertStreetGeometryBelowApproach(
                geometry,
                placement.UpperApproachFootprint,
                placement.UpperApproachStart.y,
                $"{stairId} upper approach");
        }

        private static void AssertStreetGeometryBelowApproach(
            IReadOnlyList<RuntimeOrientedBox> geometry,
            Rect approach,
            float approachTopY,
            string label)
        {
            for (int index = 0; index < geometry.Count; index++)
            {
                RuntimeOrientedBox box = geometry[index];
                if (!IntersectsInterior(CreateRect(box), approach))
                {
                    continue;
                }

                Assert.That(
                    MaximumY(box),
                    Is.LessThanOrEqualTo(approachTopY + Tolerance),
                    $"{label} must remain above every overlapping " +
                    "street collider.");
            }
        }

        private static bool RequiresAuthoredOpenAreaAccess(
            CitySurfaceDescriptor surface)
        {
            // Beach frontage opened along its whole street boundary
            // (the mountain belt's rule): safe beach spans no longer
            // need an authored access behind them.
            return surface.Kind == CitySurfaceKind.CemeteryGround ||
                   (surface.Kind == CitySurfaceKind.OpenGround &&
                    !CityMountainBoundaryDefinition.IsMountainFacingAreaId(
                        surface.AreaId));
        }

        private static void AssertApproachInnerRail(
            CityExteriorStairRailDescriptor rail,
            Vector3 approachStart,
            Vector3 approachEnd,
            Vector3 sideDirection,
            float width,
            string label)
        {
            Vector3 innerOffset = -sideDirection * (width * 0.5f);
            Assert.That(
                rail.OwnerKind,
                Is.EqualTo(CityExteriorStairRailOwnerKind.Approach),
                label);
            Assert.That(
                rail.SurfaceStart,
                Is.EqualTo(approachStart + innerOffset),
                label);
            Assert.That(
                rail.SurfaceEnd,
                Is.EqualTo(approachEnd + innerOffset),
                label);
            Assert.That(
                rail.SurfaceStart.y,
                Is.EqualTo(approachStart.y).Within(Tolerance),
                label);
            Assert.That(
                rail.SurfaceEnd.y,
                Is.EqualTo(approachEnd.y).Within(Tolerance),
                label);
            Assert.That(
                rail.Height,
                Is.GreaterThanOrEqualTo(0.9f),
                label);
        }

        private static bool SpanFitsOpening(
            CityRoadGroundBoundarySpan span,
            Vector3 center,
            float width)
        {
            float openingCenter = span.IsHorizontal ? center.x : center.z;
            float halfWidth = width * 0.5f;
            return span.MinimumCoordinate >=
                   openingCenter - halfWidth - Tolerance &&
                   span.MaximumCoordinate <=
                   openingCenter + halfWidth + Tolerance;
        }

        private static void AssertApproachGuardAtOuterEdge(
            CityRoadGroundBoundarySpan span,
            Rect approach,
            Vector3 sideDirection,
            string label)
        {
            float variableMinimum = span.IsHorizontal
                ? approach.xMin
                : approach.yMin;
            float variableMaximum = span.IsHorizontal
                ? approach.xMax
                : approach.yMax;
            if (span.MaximumCoordinate <= variableMinimum + Tolerance ||
                span.MinimumCoordinate >= variableMaximum - Tolerance)
            {
                return;
            }

            float fixedMinimum = span.IsHorizontal
                ? approach.yMin
                : approach.xMin;
            float fixedMaximum = span.IsHorizontal
                ? approach.yMax
                : approach.xMax;
            if (span.FixedCoordinate < fixedMinimum - Tolerance ||
                span.FixedCoordinate > fixedMaximum + Tolerance)
            {
                return;
            }

            float outward = span.IsHorizontal
                ? sideDirection.z
                : sideDirection.x;
            float expected = outward >= 0f
                ? fixedMaximum
                : fixedMinimum;
            Assert.That(
                span.FixedCoordinate,
                Is.EqualTo(expected).Within(Tolerance),
                label);
            Assert.That(
                IntersectsInterior(span, approach),
                Is.False,
                label);
        }

        private static bool IntersectsInterior(
            CityRoadGroundBoundarySpan span,
            Rect footprint)
        {
            float fixedMinimum = span.IsHorizontal
                ? footprint.yMin
                : footprint.xMin;
            float fixedMaximum = span.IsHorizontal
                ? footprint.yMax
                : footprint.xMax;
            float variableMinimum = span.IsHorizontal
                ? footprint.xMin
                : footprint.yMin;
            float variableMaximum = span.IsHorizontal
                ? footprint.xMax
                : footprint.yMax;
            return span.FixedCoordinate > fixedMinimum + Tolerance &&
                   span.FixedCoordinate < fixedMaximum - Tolerance &&
                   span.MaximumCoordinate >
                   variableMinimum + Tolerance &&
                   span.MinimumCoordinate <
                   variableMaximum - Tolerance;
        }

        /// <summary>
        /// A stair approach continues the sidewalk it replaces, so neither
        /// its paving nor its guard rail may reach past the corner into a
        /// carriageway that crosses the stair street.
        /// </summary>
        private static void AssertApproachClearsCrossingCarriageway(
            CityLayout layout,
            CityElevationStairDescriptor stair,
            Vector2Int node,
            CityExteriorStairRailDescriptor rail,
            Rect approach,
            string label)
        {
            Rect railFootprint = Rect.MinMaxRect(
                Mathf.Min(rail.SurfaceStart.x, rail.SurfaceEnd.x) -
                (rail.Thickness * 0.5f),
                Mathf.Min(rail.SurfaceStart.z, rail.SurfaceEnd.z) -
                (rail.Thickness * 0.5f),
                Mathf.Max(rail.SurfaceStart.x, rail.SurfaceEnd.x) +
                (rail.Thickness * 0.5f),
                Mathf.Max(rail.SurfaceStart.z, rail.SurfaceEnd.z) +
                (rail.Thickness * 0.5f));
            float halfCarriageway =
                (layout.RoadWidth -
                 (CityStreetSurfacePlanner.SidewalkWidth * 2f)) * 0.5f;
            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                if (edge.IsHorizontal == stair.Edge.IsHorizontal ||
                    (edge.A != node && edge.B != node) ||
                    layout.GetPathKind(edge) != CityPathKind.Street)
                {
                    continue;
                }

                Vector3 first = layout.GetNodeWorldPosition(edge.A);
                Vector3 second = layout.GetNodeWorldPosition(edge.B);
                float lateral = edge.IsHorizontal ? 0f : halfCarriageway;
                float longitudinal =
                    edge.IsHorizontal ? halfCarriageway : 0f;
                Rect carriageway = Rect.MinMaxRect(
                    Mathf.Min(first.x, second.x) - lateral,
                    Mathf.Min(first.z, second.z) - longitudinal,
                    Mathf.Max(first.x, second.x) + lateral,
                    Mathf.Max(first.z, second.z) + longitudinal);
                Assert.That(
                    IntersectsInterior(railFootprint, carriageway),
                    Is.False,
                    $"{label} guard rail stands in the carriageway of " +
                    $"crossing street {edge}.");
                Assert.That(
                    IntersectsInterior(approach, carriageway),
                    Is.False,
                    $"{label} paving reaches into the carriageway of " +
                    $"crossing street {edge}.");
            }
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin - Tolerance &&
                   inner.xMax <= outer.xMax + Tolerance &&
                   inner.yMin >= outer.yMin - Tolerance &&
                   inner.yMax <= outer.yMax + Tolerance;
        }

        private static Rect CreateRect(RuntimeOrientedBox box)
        {
            Vector3 right = box.Rotation *
                            (Vector3.right * (box.Size.x * 0.5f));
            Vector3 forward = box.Rotation *
                              (Vector3.forward * (box.Size.z * 0.5f));
            float halfX = Mathf.Abs(right.x) + Mathf.Abs(forward.x);
            float halfZ = Mathf.Abs(right.z) + Mathf.Abs(forward.z);
            return Rect.MinMaxRect(
                box.Center.x - halfX,
                box.Center.z - halfZ,
                box.Center.x + halfX,
                box.Center.z + halfZ);
        }

        private static float MaximumY(RuntimeOrientedBox box)
        {
            Vector3 right = box.Rotation *
                            (Vector3.right * (box.Size.x * 0.5f));
            Vector3 up = box.Rotation *
                         (Vector3.up * (box.Size.y * 0.5f));
            Vector3 forward = box.Rotation *
                              (Vector3.forward * (box.Size.z * 0.5f));
            return box.Center.y +
                   Mathf.Abs(right.y) +
                   Mathf.Abs(up.y) +
                   Mathf.Abs(forward.y);
        }

        private static Rect CreateRoadFenceRailRect(
            RoadFenceSegmentDescriptor segment)
        {
            const float fenceDepth = 0.16f;
            float halfDepth = fenceDepth * 0.5f;
            Vector3 offset = segment.OutwardNormal * halfDepth;
            float halfX = Mathf.Abs(segment.OutwardNormal.x) * halfDepth;
            float halfZ = Mathf.Abs(segment.OutwardNormal.z) * halfDepth;
            return Rect.MinMaxRect(
                Mathf.Min(segment.Start.x, segment.End.x) +
                    offset.x - halfX,
                Mathf.Min(segment.Start.z, segment.End.z) +
                    offset.z - halfZ,
                Mathf.Max(segment.Start.x, segment.End.x) +
                    offset.x + halfX,
                Mathf.Max(segment.Start.z, segment.End.z) +
                    offset.z + halfZ);
        }

        private static bool IntersectsInterior(Rect first, Rect second)
        {
            return Mathf.Min(first.xMax, second.xMax) -
                   Mathf.Max(first.xMin, second.xMin) > Tolerance &&
                   Mathf.Min(first.yMax, second.yMax) -
                   Mathf.Max(first.yMin, second.yMin) > Tolerance;
        }

        private static void AssertRoadAnchorMatches(
            CityElevationPlan plan,
            RoadEdge edge,
            Vector3 anchor,
            string label)
        {
            float expected = SampleEdgeAtWorldPoint(plan, edge, anchor);
            Assert.That(
                anchor.y,
                Is.EqualTo(expected).Within(Tolerance),
                label);
            Assert.That(anchor.y, Is.GreaterThan(0f), label);
        }

        private static float SampleEdgeAtWorldPoint(
            CityElevationPlan plan,
            RoadEdge edge,
            Vector3 point)
        {
            Vector2 start = GetNodeWorldXZ(plan, edge.A);
            Vector2 end = GetNodeWorldXZ(plan, edge.B);
            Vector2 delta = end - start;
            var pointXZ = new Vector2(point.x, point.z);
            float amount = delta.sqrMagnitude > 0.000001f
                ? Mathf.Clamp01(
                    Vector2.Dot(pointXZ - start, delta) /
                    delta.sqrMagnitude)
                : 0f;
            return plan.SampleRoadDatum(edge, amount);
        }

        private static Vector2 GetNodeWorldXZ(
            CityElevationPlan plan,
            Vector2Int node)
        {
            return new Vector2(
                plan.WorldOrigin.x + node.x * plan.NodeSpacing.x,
                plan.WorldOrigin.z + node.y * plan.NodeSpacing.y);
        }

        /// <summary>
        /// The whole blueprint under a name the terracing has never
        /// heard of. Rebuilding it by hand loses the river and the
        /// area requirements, and a blueprint missing either fails its
        /// own validation.
        /// </summary>
        private static CityBlueprint CloneWithId(
            CityBlueprint source,
            string id)
        {
            return CityBlueprintBuilder.From(source, id).Build();
        }

        private static void AssertFlatFallback(CityElevationPlan plan)
        {
            Assert.That(plan.IsElevated, Is.False);
            Assert.That(plan.MinimumElevation, Is.EqualTo(0f));
            Assert.That(plan.MaximumElevation, Is.EqualTo(0f));
            Assert.That(plan.NodeElevations.Values, Is.All.EqualTo(0f));
            Assert.That(plan.CellElevations.Values, Is.All.EqualTo(0f));
            Assert.That(plan.SignatureStairs, Is.Empty);
            foreach (CityElevationTransitionDescriptor transition in
                     plan.Transitions.Values)
            {
                Assert.That(
                    transition.Kind,
                    Is.EqualTo(CityElevationTransitionKind.Level));
                Assert.That(transition.GradePercent, Is.EqualTo(0f));
            }

            foreach (DistrictElevationProfile profile in
                     plan.Profiles.Values)
            {
                Assert.That(profile.MinimumElevation, Is.EqualTo(0f));
                Assert.That(profile.MaximumElevation, Is.EqualTo(0f));
            }
        }
    }
}
