using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityChurchCourtyardPlanningTests
    {
        private const float Tolerance = 0.001f;

        [Test]
        [Category("CityChurchCourtyard")]
        public void DefaultCity_MovesChurchAndBuildsMaintainedLinkedYard()
        {
            CityLayout layout = CreateLayout();
            CityChurchPlan church = CityChurchPlanner.Create(layout);
            CityChurchCemeteryPassagePlan passage =
                CityChurchCemeteryPassagePlanner.Create(layout, church);
            CityChurchCourtyardPlan courtyard =
                CityChurchCourtyardPlanner.Create(
                    layout,
                    church,
                    passage);

            Assert.That(CityChurchPlanner.StreetSetback, Is.EqualTo(10f));
            Assert.That(
                church.ModelFootprint.xMin,
                Is.EqualTo(church.Grounds.xMin + 10f).Within(Tolerance));
            Assert.That(passage, Is.Not.Null);
            Assert.That(passage.AxisX, Is.EqualTo(226f).Within(Tolerance));
            Assert.That(passage.OpeningWidth, Is.EqualTo(3f));
            Assert.That(
                passage.StepHeight,
                Is.LessThanOrEqualTo(
                    CityRoadGroundBoundaryPlanner.MaximumSafeStep));

            Assert.That(courtyard, Is.Not.Null);
            Assert.That(
                courtyard.GetFixtureCount(
                    CityChurchCourtyardFixtureKind.Bench),
                Is.EqualTo(2));
            Assert.That(
                courtyard.GetFixtureCount(
                    CityChurchCourtyardFixtureKind.Tree),
                Is.EqualTo(2));
            Assert.That(
                courtyard.GetFixtureCount(
                    CityChurchCourtyardFixtureKind.Shrub),
                Is.EqualTo(6));
            Assert.That(
                courtyard.GetFixtureCount(
                    CityChurchCourtyardFixtureKind.FlowerBed),
                Is.EqualTo(2));
            Assert.That(
                courtyard.Surfaces.Any(surface => surface.Kind ==
                    CityChurchCourtyardSurfaceKind.Lawn),
                Is.True);
            Assert.That(
                courtyard.Surfaces.Any(
                    surface => surface.Kind ==
                                   CityChurchCourtyardSurfaceKind.Gravel &&
                               surface.Bounds.Contains(new Vector2(
                                   passage.AxisX,
                                   passage.BoundaryZ + 0.01f))),
                Is.True);

            Assert.DoesNotThrow(() =>
                CityChurchCourtyardPlanner.ValidateOrThrow(
                    layout,
                    church,
                    courtyard));

            var teleportGround = new CityMapCityTeleportGround(layout);
            for (int index = 0; index < courtyard.Fixtures.Count; index++)
            {
                CityChurchCourtyardFixtureDescriptor fixture =
                    courtyard.Fixtures[index];
                Assert.That(
                    teleportGround.TryResolveStandingPosition(
                        fixture.BlockerBounds.center,
                        out _),
                    Is.False,
                    fixture.Id);
            }
        }

        [Test]
        [Category("CityChurchCourtyard")]
        public void DefaultCity_CourtyardBenchesAreSittableAndStable()
        {
            CityLayout layout = CreateLayout();
            CityChurchPlan church = CityChurchPlanner.Create(layout);
            CityChurchCemeteryPassagePlan passage =
                CityChurchCemeteryPassagePlanner.Create(layout, church);
            CityChurchCourtyardPlan first =
                CityChurchCourtyardPlanner.Create(
                    layout,
                    church,
                    passage);
            CityChurchCourtyardPlan second =
                CityChurchCourtyardPlanner.Create(
                    layout,
                    church,
                    passage);

            Assert.That(second.Surfaces, Is.EqualTo(first.Surfaces));
            Assert.That(second.Fixtures, Is.EqualTo(first.Fixtures));

            var seats = new List<CityBenchSeat>();
            CityChurchCourtyardWorldBuilder.AppendBenchSeats(first, seats);
            Assert.That(seats, Has.Count.EqualTo(2));
            for (int index = 0; index < seats.Count; index++)
            {
                Assert.That(seats[index].IsPresent, Is.True);
                Assert.That(
                    seats[index].SeatTopCenter.y,
                    Is.EqualTo(first.GroundTopY + 0.49f)
                        .Within(Tolerance));
                Assert.That(Vector3.Distance(seats[index].FaceDirection,
                    index == 0 ? Vector3.back : Vector3.left),
                    Is.LessThan(Tolerance));
            }

            CityOpenAreaDecorationPlan openArea =
                CityOpenAreaDecorationPlanner.Create(layout);
            RoadFencePlan fence = RoadFencePlanner.CreatePlan(layout);
            CityNightFixturePlan night =
                CityNightFixturePlanner.CreatePlan(layout);
            CityDecorationPlan decorations =
                CityDecorationPlanner.CreatePlan(layout, fence, night);
            CityBusPlan busPlan = CityBusPlanner.Create(layout, decorations);
            CityStreetSurfacePlan streetSurface =
                CityStreetSurfacePlanner.Create(layout);
            List<CityBenchSitPlan> allSeats = CityBenchSitPlan.CreateAll(
                layout,
                openArea,
                CityCemeteryPlanner.Create(layout, passage),
                busPlan,
                decorations,
                streetSurface,
                null,
                first);
            Assert.That(
                allSeats.Count(seat => seat.Id.StartsWith(
                    "church-courtyard-bench-",
                    System.StringComparison.Ordinal)),
                Is.EqualTo(2));
        }

        [Test]
        [Category("CityChurchCourtyard")]
        public void PassageVisibleKit_IsBlenderAuthoredAndMetric()
        {
            CityMiscAssetProvider provider =
                CityMiscAssetProvider.LoadOrThrow();
            CityMiscMeshPart gravel = provider.GetPartOrThrow(
                CityMiscKind.ChurchCourtyardSurface,
                (int)CityChurchCourtyardSurfaceKind.Gravel,
                0);
            CityMiscMeshPart post = provider.GetPartOrThrow(
                CityMiscKind.CemeteryFencePost,
                0,
                0);
            CityMiscMeshPart rail = provider.GetPartOrThrow(
                CityMiscKind.CemeteryFenceRail,
                0,
                0);

            Assert.That(gravel.Mesh, Is.Not.Null);
            Assert.That(
                gravel.Mesh.bounds.size.x,
                Is.EqualTo(1f).Within(Tolerance));
            Assert.That(
                gravel.Mesh.bounds.size.y,
                Is.EqualTo(0.04f).Within(Tolerance));
            Assert.That(
                gravel.Mesh.bounds.size.z,
                Is.EqualTo(1f).Within(Tolerance));
            Assert.That(post.Mesh, Is.Not.Null);
            Assert.That(
                post.Mesh.bounds.size.x,
                Is.EqualTo(0.18f).Within(Tolerance));
            Assert.That(
                post.Mesh.bounds.size.y,
                Is.EqualTo(1.48f).Within(Tolerance));
            Assert.That(
                post.Mesh.bounds.size.z,
                Is.EqualTo(0.18f).Within(Tolerance));
            Assert.That(rail.Mesh, Is.Not.Null);
            Assert.That(
                rail.Mesh.bounds.size.x,
                Is.EqualTo(1f).Within(Tolerance));
            Assert.That(
                rail.Mesh.bounds.size.y,
                Is.EqualTo(0.12f).Within(Tolerance));
            Assert.That(
                rail.Mesh.bounds.size.z,
                Is.EqualTo(0.16f).Within(Tolerance));
            Assert.That(
                rail.Mesh.triangles.Length / 3,
                Is.EqualTo(12),
                "A scalable rail keeps flat longitudinal ends.");

            CityLayout layout = CreateLayout();
            CityChurchPlan church = CityChurchPlanner.Create(layout);
            CityChurchCemeteryPassagePlan passage =
                CityChurchCemeteryPassagePlanner.Create(layout, church);
            CityChurchCourtyardPlan courtyard =
                CityChurchCourtyardPlanner.Create(
                    layout,
                    church,
                    passage);
            CityCemeteryPlan cemetery = CityCemeteryPlanner.Create(layout);
            var owner = new GameObject("Church Passage Blender Test");
            try
            {
                GameObject courtyardRoot =
                    CityChurchCourtyardWorldBuilder.Build(
                        owner.transform,
                        courtyard);
                Assert.That(
                    courtyardRoot.name,
                    Is.EqualTo(CityChurchCourtyardWorldBuilder.RootName));
                Assert.That(
                    courtyardRoot.GetComponentsInChildren<Renderer>(true)
                        .Count(renderer => renderer.enabled),
                    Is.GreaterThanOrEqualTo(6));

                GameObject root = CityCemeteryWorldBuilder.Build(
                    owner.transform,
                    cemetery);
                Assert.That(
                    root.GetComponentsInChildren<Renderer>(true).Any(
                        renderer => renderer.name.StartsWith(
                                        "Imported Cemetery Chunk",
                                        System.StringComparison.Ordinal) &&
                                    renderer.name.EndsWith(
                                        CityCemeteryStyle.Gravel.ToString(),
                                        System.StringComparison.Ordinal)),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        private static CityLayout CreateLayout()
        {
            return CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
        }
    }
}
