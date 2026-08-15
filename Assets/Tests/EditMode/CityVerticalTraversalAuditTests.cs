using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests
{
    public sealed class CityVerticalTraversalAuditTests
    {
        private const int ProductionSeed = 20260727;
        private const float Tolerance = 0.001f;

        [Test]
        [Category("CityTraversal")]
        public void ProductionCity_ContinuousTerrainAndParkStayRoadReachable()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                ProductionSeed);

            CityVerticalTraversalPlan first =
                CityVerticalTraversalAudit.Create(layout);
            CityVerticalTraversalPlan repeated =
                CityVerticalTraversalAudit.Create(layout);

            CityVerticalTraversalTransitionDescriptor[] continuousSeams =
                first.Transitions
                    .Where(transition =>
                        transition.Kind ==
                            CityVerticalTraversalTransitionKind.GroundSeam &&
                        IsContinuous(transition.SurfaceKind) &&
                        IsContinuous(transition.OtherSurfaceKind))
                    .ToArray();
            Assert.That(
                continuousSeams,
                Is.Not.Empty,
                "The production plan needs audited continuous-ground seams.");
            foreach (
                CityVerticalTraversalTransitionDescriptor seam in
                continuousSeams)
            {
                Assert.That(
                    seam.Classification,
                    Is.EqualTo(
                        CityVerticalTraversalClassification.Authorized),
                    $"{seam.Cell} -> {seam.OtherCell}");
                Assert.That(
                    seam.MaximumStepDelta,
                    Is.LessThanOrEqualTo(
                        CityVerticalTraversalAudit.MaximumSafeStep +
                        Tolerance),
                    $"{seam.Cell} -> {seam.OtherCell}");
            }

            Assert.That(layout.Park.Regions, Is.Not.Empty);
            foreach (CityParkRegionPlan region in layout.Park.Regions)
            {
                Assert.That(region.Cells, Is.Not.Empty, region.Id);
                foreach (Vector2Int cell in region.Cells)
                {
                    Assert.That(
                        first.IsInAuthorizedRoadComponent(cell),
                        Is.True,
                        $"Park region '{region.Id}' cell {cell} must be " +
                        "reachable from an authorized road frontage.");
                }
            }

            foreach (CityParkGateDescriptor gate in layout.Park.Gates)
            {
                AssertAuthorizedAccess(
                    first,
                    gate.Center,
                    null,
                    null,
                    $"park gate '{gate.Id}'");
            }

            foreach (CityOpenAreaAccessDescriptor access in
                     layout.OpenAreaAccesses)
            {
                AssertAuthorizedAccess(
                    first,
                    access.Center,
                    access.Cell,
                    access.FrontageEdge,
                    $"open-area access '{access.Id}'");
            }

            CitySurfaceDescriptor[] traversableSurfaces = layout.Surfaces
                .Where(CityRoadGroundBoundaryPlanner
                    .SupportsGroundTraversal)
                .ToArray();
            Assert.That(traversableSurfaces, Is.Not.Empty);
            foreach (CitySurfaceDescriptor surface in traversableSurfaces)
            {
                Assert.That(
                    first.IsInAuthorizedRoadComponent(surface.Cell),
                    Is.True,
                    $"{surface.AreaId} {surface.Kind} at {surface.Cell} " +
                    "must have an authored route to the production road " +
                    "component.");
            }

            AssertDeterministic(first, repeated);
        }

        private static void AssertAuthorizedAccess(
            CityVerticalTraversalPlan plan,
            Vector3 center,
            Vector2Int? cell,
            RoadEdge? edge,
            string label)
        {
            Vector2 point = new Vector2(center.x, center.z);
            CityVerticalTraversalTransitionDescriptor[] matching =
                plan.Transitions
                    .Where(transition =>
                        transition.Kind ==
                            CityVerticalTraversalTransitionKind
                                .RoadFrontage &&
                        (!cell.HasValue || transition.Cell == cell.Value) &&
                        (!edge.HasValue ||
                         transition.RoadEdge == edge.Value) &&
                        LiesOnSegment(
                            point,
                            transition.StartWorldXZ,
                            transition.EndWorldXZ))
                    .ToArray();
            Assert.That(
                matching,
                Is.Not.Empty,
                $"{label} must appear in the vertical audit.");
            Assert.That(
                matching.Any(transition => transition.IsAuthorized),
                Is.True,
                $"{label} must own a capsule-width, step-safe road " +
                "transition.");
        }

        private static bool LiesOnSegment(
            Vector2 point,
            Vector2 start,
            Vector2 end)
        {
            Vector2 delta = end - start;
            if (delta.sqrMagnitude <= Tolerance * Tolerance)
            {
                return Vector2.Distance(point, start) <= Tolerance;
            }

            float amount = Mathf.Clamp01(
                Vector2.Dot(point - start, delta) /
                delta.sqrMagnitude);
            Vector2 closest = start + delta * amount;
            return Vector2.Distance(point, closest) <= Tolerance;
        }

        private static bool IsContinuous(CitySurfaceKind kind)
        {
            return kind == CitySurfaceKind.BuildableGround ||
                   kind == CitySurfaceKind.ParkGround ||
                   kind == CitySurfaceKind.OpenGround ||
                   kind == CitySurfaceKind.Beach;
        }

        private static void AssertDeterministic(
            CityVerticalTraversalPlan expected,
            CityVerticalTraversalPlan actual)
        {
            Assert.That(
                actual.Transitions.Count,
                Is.EqualTo(expected.Transitions.Count));
            for (int index = 0;
                 index < expected.Transitions.Count;
                 index++)
            {
                CityVerticalTraversalTransitionDescriptor left =
                    expected.Transitions[index];
                CityVerticalTraversalTransitionDescriptor right =
                    actual.Transitions[index];
                Assert.That(right.Kind, Is.EqualTo(left.Kind));
                Assert.That(right.Cell, Is.EqualTo(left.Cell));
                Assert.That(right.OtherCell, Is.EqualTo(left.OtherCell));
                Assert.That(right.RoadEdge, Is.EqualTo(left.RoadEdge));
                Assert.That(
                    right.Classification,
                    Is.EqualTo(left.Classification));
                Assert.That(
                    right.MaximumStepDelta,
                    Is.EqualTo(left.MaximumStepDelta));
            }

            Assert.That(
                actual.RoadReachableCells,
                Is.EqualTo(expected.RoadReachableCells));
            Assert.That(
                actual.Defects.Select(defect =>
                    new
                    {
                        defect.Cell,
                        defect.AreaId,
                        defect.SurfaceKind,
                        defect.Delta,
                        defect.Type
                    }),
                Is.EqualTo(expected.Defects.Select(defect =>
                    new
                    {
                        defect.Cell,
                        defect.AreaId,
                        defect.SurfaceKind,
                        defect.Delta,
                        defect.Type
                    })));
        }
    }
}
