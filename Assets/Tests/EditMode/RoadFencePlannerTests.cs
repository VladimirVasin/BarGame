using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class RoadFencePlannerTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void CreatePlan_WithSameLayout_ProducesIdenticalPlan()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                71923);

            RoadFencePlan first = RoadFencePlanner.CreatePlan(layout);
            RoadFencePlan second = RoadFencePlanner.CreatePlan(layout);

            CollectionAssert.AreEqual(first.Segments, second.Segments);
            CollectionAssert.AreEqual(first.Openings, second.Openings);
        }

        [Test]
        public void CreatePlan_RemovesEveryRoadSideSupportedByActiveLand()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                -4119);
            IReadOnlyList<Rect> streets = layout.CreateStreetRects();
            RoadFencePlan plan = RoadFencePlanner.CreatePlan(layout);

            Assert.That(plan.Segments, Is.Not.Empty);
            foreach (RoadFenceSegmentDescriptor segment in plan.Segments)
            {
                Assert.That(segment.Length, Is.GreaterThan(Tolerance));
                Assert.That(IsCardinal(segment.OutwardNormal), Is.True);

                Vector3 inward =
                    segment.Center - (segment.OutwardNormal * 0.02f);
                Vector3 outward =
                    segment.Center + (segment.OutwardNormal * 0.02f);
                Assert.That(IsInsideAny(streets, inward), Is.True);
                Assert.That(IsInsideAny(streets, outward), Is.False);

                if (segment.Purpose ==
                    RoadFenceSegmentPurpose.MapBoundary)
                {
                    Assert.That(
                        IsInsideActiveLand(layout, outward),
                        Is.False,
                        $"Map rail faces active land at {segment.Center}.");
                }
            }
        }

        [Test]
        public void CreatePlan_DeadEndsMatchDegreeOneTravelNodes()
        {
            CityGenerationSettings settings = CityGenerationSettings.Default;
            settings.LoopChance = 0f;
            CityLayout layout = CityLayoutGenerator.Generate(settings, 101);
            RoadFencePlan plan = RoadFencePlanner.CreatePlan(layout);
            var degreeByNode = CreateDegrees(layout.RoadEdges);
            int expectedCount = 0;

            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                if (layout.GetPathKind(edge) != CityPathKind.Street)
                {
                    continue;
                }

                expectedCount += degreeByNode[edge.A] == 1 ? 1 : 0;
                expectedCount += degreeByNode[edge.B] == 1 ? 1 : 0;
            }

            RoadFenceSegmentDescriptor[] deadEnds = plan.Segments
                .Where(segment =>
                    segment.Purpose == RoadFenceSegmentPurpose.DeadEnd)
                .ToArray();
            Assert.That(deadEnds, Has.Length.EqualTo(expectedCount));
            Assert.That(deadEnds, Is.Not.Empty);
            foreach (RoadFenceSegmentDescriptor segment in deadEnds)
            {
                Assert.That(
                    segment.Length,
                    Is.EqualTo(layout.RoadWidth).Within(Tolerance));
            }
        }

        [Test]
        public void CreatePlan_StreetToParkPathContinuationIsNotCapped()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                71923);
            RoadFencePlan plan = RoadFencePlanner.CreatePlan(layout);
            var incidentKinds =
                new Dictionary<Vector2Int, HashSet<CityPathKind>>();
            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                CityPathKind kind = layout.GetPathKind(edge);
                AddIncidentKind(incidentKinds, edge.A, kind);
                AddIncidentKind(incidentKinds, edge.B, kind);
            }

            Vector2Int[] continuations = incidentKinds
                .Where(pair =>
                    pair.Value.Contains(CityPathKind.Street) &&
                    pair.Value.Contains(CityPathKind.ParkPath))
                .Select(pair => pair.Key)
                .ToArray();
            Assert.That(continuations, Is.Not.Empty);
            foreach (Vector2Int node in continuations)
            {
                Vector3 world = layout.GetNodeWorldPosition(node);
                Assert.That(
                    plan.Segments.Any(segment =>
                        Vector3.Distance(
                            segment.Center,
                            world +
                            segment.OutwardNormal *
                            (layout.RoadWidth * 0.5f)) <= Tolerance),
                    Is.False,
                    $"Street/park continuation {node} was capped.");
            }
        }

        [Test]
        [Category("CityRiver")]
        public void CreatePlan_LeavesPromenadesAndBridgeParapetsToRiverBuilder()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            RoadFencePlan plan = RoadFencePlanner.CreatePlan(layout);

            Assert.That(layout.River.IsEnabled, Is.True);
            Assert.That(layout.River.Promenades, Has.Count.EqualTo(2));
            Assert.That(
                layout.River.Bridges.Count(bridge =>
                    bridge.Definition.CarriesRoadTraffic),
                Is.EqualTo(2));

            foreach (CityRiverPromenadeDescriptor promenade in
                     layout.River.Promenades)
            {
                float seamX = promenade.WestBank
                    ? promenade.Bounds.xMin
                    : promenade.Bounds.xMax;
                Vector3 outward = promenade.WestBank
                    ? Vector3.right
                    : Vector3.left;
                Assert.That(
                    plan.Segments.Any(segment =>
                        !segment.IsHorizontal &&
                        Mathf.Abs(segment.FixedCoordinate - seamX) <=
                        Tolerance &&
                        (segment.OutwardNormal - outward).sqrMagnitude <=
                        Tolerance * Tolerance &&
                        HasPositiveOverlap(
                            segment.MinimumCoordinate,
                            segment.MaximumCoordinate,
                            promenade.Bounds.yMin,
                            promenade.Bounds.yMax)),
                    Is.False,
                    $"A generic fence separates {promenade.Id} from " +
                    "its bank road.");
            }

            foreach (CityRiverBridgeDescriptor bridge in
                     layout.River.Bridges.Where(candidate =>
                         candidate.Definition.CarriesRoadTraffic))
            {
                float minimum = bridge.DeckBounds.xMin +
                                layout.RoadWidth * 0.5f;
                float maximum = bridge.DeckBounds.xMax -
                                layout.RoadWidth * 0.5f;
                Assert.That(
                    plan.Segments.Any(segment =>
                        segment.IsHorizontal &&
                        HasPositiveOverlap(
                            segment.MinimumCoordinate,
                            segment.MaximumCoordinate,
                            minimum,
                            maximum) &&
                        (Mathf.Abs(
                             segment.FixedCoordinate -
                             bridge.DeckBounds.yMin) <= Tolerance ||
                         Mathf.Abs(
                             segment.FixedCoordinate -
                             bridge.DeckBounds.yMax) <= Tolerance)),
                    Is.False,
                    $"Generic fences duplicate the authored parapets on " +
                    $"{bridge.Definition.Id}.");
            }
        }

        [Test]
        public void CreatePlan_PreservesClearanceOpeningMetadata()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                71923);
            RoadFencePlan plan = RoadFencePlanner.CreatePlan(layout);

            Assert.That(
                plan.EntranceOpenings.Count,
                Is.EqualTo(layout.BuildingLots.Count(lot => lot.IsBar)));
            Assert.That(
                plan.ParkGateOpenings.Count,
                Is.EqualTo(layout.Park.Gates.Count));
            Assert.That(plan.PlayerHomeOpenings, Has.Count.EqualTo(1));
            Assert.That(plan.SupermarketOpenings, Has.Count.EqualTo(1));
            Assert.That(
                plan.PublicSpaceOpenings.Count,
                Is.EqualTo(
                    layout.DistrictPointsOfInterest.Sum(
                        point => point.Accesses.Count)));
            Assert.That(
                plan.OpenAreaAccessOpenings.Count,
                Is.EqualTo(layout.OpenAreaAccesses.Count));
            Assert.That(
                plan.Openings.Count,
                Is.EqualTo(
                    plan.EntranceOpenings.Count +
                    plan.ParkGateOpenings.Count +
                    plan.PlayerHomeOpenings.Count +
                    plan.PublicSpaceOpenings.Count +
                    plan.SupermarketOpenings.Count +
                    plan.OpenAreaAccessOpenings.Count));
        }

        [Test]
        public void Build_CreatesPhysicalRailsAndVisualOnlyPosts()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                71923);
            RoadFencePlan plan = RoadFencePlanner.CreatePlan(layout);
            var parent = new GameObject("Fence Test Parent");

            try
            {
                GameObject root = RoadFenceWorldBuilder.Build(
                    parent.transform,
                    plan);
                MeshCollider[] colliders =
                    root.GetComponentsInChildren<MeshCollider>(true);
                Renderer[] rails = root.GetComponentsInChildren<Renderer>(
                        true)
                    .Where(renderer => renderer.name == "Safety Rails")
                    .ToArray();
                Renderer[] posts = root.GetComponentsInChildren<Renderer>(
                        true)
                    .Where(renderer => renderer.name == "Fence Posts")
                    .ToArray();

                Assert.That(rails, Is.Not.Empty);
                Assert.That(colliders, Has.Length.EqualTo(rails.Length));
                Assert.That(posts, Is.Not.Empty);
                foreach (Renderer postRenderer in posts)
                {
                    Assert.That(
                        postRenderer.GetComponent<Collider>(),
                        Is.Null);
                }
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void CreatePlan_WithNullLayout_Throws()
        {
            Assert.That(
                () => RoadFencePlanner.CreatePlan(null),
                Throws.ArgumentNullException);
        }

        private static Dictionary<Vector2Int, int> CreateDegrees(
            IReadOnlyList<RoadEdge> edges)
        {
            var result = new Dictionary<Vector2Int, int>();
            for (int index = 0; index < edges.Count; index++)
            {
                Increment(result, edges[index].A);
                Increment(result, edges[index].B);
            }

            return result;
        }

        private static void Increment(
            IDictionary<Vector2Int, int> destination,
            Vector2Int node)
        {
            destination.TryGetValue(node, out int count);
            destination[node] = count + 1;
        }

        private static void AddIncidentKind(
            IDictionary<Vector2Int, HashSet<CityPathKind>> destination,
            Vector2Int node,
            CityPathKind kind)
        {
            if (!destination.TryGetValue(
                    node,
                    out HashSet<CityPathKind> kinds))
            {
                kinds = new HashSet<CityPathKind>();
                destination.Add(node, kinds);
            }

            kinds.Add(kind);
        }

        private static bool IsInsideActiveLand(
            CityLayout layout,
            Vector3 point)
        {
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (!surface.IsWater &&
                    ContainsStrict(surface.WorldBounds, point))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsInsideAny(
            IReadOnlyList<Rect> rectangles,
            Vector3 point)
        {
            for (int index = 0; index < rectangles.Count; index++)
            {
                if (ContainsStrict(rectangles[index], point))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsStrict(Rect rectangle, Vector3 point)
        {
            return point.x > rectangle.xMin &&
                   point.x < rectangle.xMax &&
                   point.z > rectangle.yMin &&
                   point.z < rectangle.yMax;
        }

        private static bool HasPositiveOverlap(
            float firstMinimum,
            float firstMaximum,
            float secondMinimum,
            float secondMaximum) =>
            Mathf.Min(firstMaximum, secondMaximum) -
            Mathf.Max(firstMinimum, secondMinimum) > Tolerance;

        private static bool IsCardinal(Vector3 direction)
        {
            bool x = Mathf.Abs(Mathf.Abs(direction.x) - 1f) <=
                     Tolerance &&
                     Mathf.Abs(direction.z) <= Tolerance;
            bool z = Mathf.Abs(Mathf.Abs(direction.z) - 1f) <=
                     Tolerance &&
                     Mathf.Abs(direction.x) <= Tolerance;
            return Mathf.Abs(direction.y) <= Tolerance && (x || z);
        }
    }
}
