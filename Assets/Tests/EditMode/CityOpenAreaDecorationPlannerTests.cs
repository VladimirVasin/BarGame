using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityOpenAreaDecorationPlannerTests
    {
        [Test]
        public void DefaultCity_CreatesDeterministicClearLakeAndCemeteryLandmarks()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);

            CityOpenAreaDecorationPlan first =
                CityOpenAreaDecorationPlanner.Create(layout);
            CityOpenAreaDecorationPlan second =
                CityOpenAreaDecorationPlanner.Create(layout);

            CollectionAssert.AreEqual(
                first.Descriptors,
                second.Descriptors);
            Assert.That(
                first.Count,
                Is.LessThanOrEqualTo(
                    CityOpenAreaDecorationPlan.MaximumPartCount));
            Assert.That(
                first.GetCount(
                    CityOpenAreaDecorationKind.LakeWaterEdge),
                Is.GreaterThanOrEqualTo(8));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.LakeReeds),
                Is.GreaterThan(0));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.LakeBoat),
                Is.EqualTo(1));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.CemeteryFence),
                Is.GreaterThan(12));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.CemeteryGate),
                Is.EqualTo(2));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.CemeteryPath),
                Is.EqualTo(1));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.CemeteryGrave),
                Is.GreaterThan(20));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.CemeteryTree),
                Is.GreaterThan(0));

            Rect[] lakeWater = layout.Surfaces
                .Where(surface =>
                    surface.Feature == CityAreaFeatureKind.Lake &&
                    surface.IsWater)
                .Select(surface => surface.WorldBounds)
                .ToArray();
            foreach (CityOpenAreaDecorationDescriptor shorelineDetail in
                     first.Descriptors.Where(item =>
                         item.Kind == CityOpenAreaDecorationKind.LakeReeds ||
                         item.Kind == CityOpenAreaDecorationKind.LakeRock ||
                         item.Kind == CityOpenAreaDecorationKind.LakeBoat))
            {
                float nearestWater = lakeWater.Min(water =>
                    RectDistance(ToXZRect(shorelineDetail.Bounds), water));
                Assert.That(
                    nearestWater,
                    Is.LessThanOrEqualTo(1.5f),
                    shorelineDetail.StableId);
            }

            CityOpenAreaDecorationPlanner.ValidateOrThrow(layout, first);
            foreach (CityOpenAreaDecorationDescriptor descriptor in
                     first.Descriptors.Where(item => item.BlocksMovement))
            {
                // Several yards share one feature, so every access of the
                // matching feature has to stay clear, not just the first.
                foreach (CityOpenAreaAccessDescriptor access in
                         layout.OpenAreaAccesses.Where(item =>
                             item.Feature == descriptor.Feature))
                {
                    Assert.That(
                        Overlaps(
                            ToXZRect(descriptor.Bounds),
                            access.ApproachBounds),
                        Is.False,
                        descriptor.StableId);
                }
            }
        }

        [Test]
        public void DefaultCity_DressesOnlyTheHomeYardWithACircuitAndTraces()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);

            CityOpenAreaDecorationPlan first =
                CityOpenAreaDecorationPlanner.Create(layout);
            CityOpenAreaDecorationPlan second =
                CityOpenAreaDecorationPlanner.Create(layout);

            CollectionAssert.AreEqual(
                first.Descriptors,
                second.Descriptors);
            Assert.That(
                first.Count,
                Is.LessThanOrEqualTo(
                    CityOpenAreaDecorationPlan.MaximumPartCount));

            CityOpenAreaDecorationDescriptor[] yardParts =
                first.Descriptors
                    .Where(item =>
                        item.Feature == CityAreaFeatureKind.Yard)
                    .ToArray();
            Assert.That(yardParts, Is.Not.Empty);
            Assert.That(
                yardParts.All(item =>
                    item.StableId.StartsWith(
                        "home-yard-",
                        StringComparison.Ordinal)),
                Is.True,
                "Only the yard beside the home is dressed in this pass.");

            // The circuit is unbroken: a ring worn by years of the same
            // lap cannot have a gap chewed out of it by the entrance.
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.YardRingTrack),
                Is.EqualTo(24));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.YardDeadTree),
                Is.EqualTo(3));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.YardChildToy),
                Is.EqualTo(1));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.YardBottle),
                Is.EqualTo(1));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.YardBench),
                Is.EqualTo(3));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.YardCarpetFrame),
                Is.EqualTo(3));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.YardSandpit),
                Is.EqualTo(4));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.YardDeadLamp),
                Is.EqualTo(2));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.YardBin),
                Is.EqualTo(2));

            // The ring is a flat trace, so it must never block a body.
            Assert.That(
                yardParts
                    .Where(item =>
                        item.Kind ==
                        CityOpenAreaDecorationKind.YardRingTrack)
                    .All(item => !item.BlocksMovement),
                Is.True);

            // Nothing may stand inside either building: the yard is the
            // gap between them.
            Assert.That(layout.PlayerHome, Is.Not.Null);
            Rect homeFootprint = Rect.MinMaxRect(
                layout.PlayerHome.Center.x -
                layout.PlayerHome.Size.x * 0.5f,
                layout.PlayerHome.Center.z -
                layout.PlayerHome.Size.y * 0.5f,
                layout.PlayerHome.Center.x +
                layout.PlayerHome.Size.x * 0.5f,
                layout.PlayerHome.Center.z +
                layout.PlayerHome.Size.y * 0.5f);
            foreach (CityOpenAreaDecorationDescriptor part in yardParts)
            {
                Assert.That(
                    Overlaps(ToXZRect(part.Bounds), homeFootprint),
                    Is.False,
                    $"{part.StableId} stands inside the hero's building.");
            }

            // The middle of the circuit stays free for the rider who will
            // be added once the model lands: only the dead tree stands
            // inside the ring.
            CityOpenAreaDecorationDescriptor trunk = yardParts.Single(item =>
                string.Equals(
                    item.StableId,
                    "home-yard-tree-trunk",
                    StringComparison.Ordinal));
            var ringCenter = new Vector2(
                trunk.Bounds.center.x,
                trunk.Bounds.center.z);
            foreach (CityOpenAreaDecorationDescriptor part in yardParts)
            {
                if (part.Kind == CityOpenAreaDecorationKind.YardDeadTree ||
                    part.Kind == CityOpenAreaDecorationKind.YardRingTrack)
                {
                    continue;
                }

                float distance = Vector2.Distance(
                    ringCenter,
                    new Vector2(part.Bounds.center.x, part.Bounds.center.z));
                Assert.That(
                    distance,
                    Is.GreaterThan(3.4f),
                    $"{part.StableId} stands on the circuit.");
            }

            // This is the yard against the hero's own wall: everything
            // must be within a few steps of it.
            var home = new Vector2(
                layout.PlayerHome.Center.x,
                layout.PlayerHome.Center.z);
            foreach (CityOpenAreaDecorationDescriptor part in yardParts)
            {
                float distance = Vector2.Distance(
                    home,
                    new Vector2(part.Bounds.center.x, part.Bounds.center.z));
                Assert.That(
                    distance,
                    Is.LessThan(22f),
                    $"{part.StableId} is not beside the hero's building.");
            }

            Assert.That(
                Vector2.Distance(
                    home,
                    new Vector2(trunk.Bounds.center.x, trunk.Bounds.center.z)),
                Is.LessThan(16f),
                "The dead tree must stand in the gap beside the house.");

            Assert.DoesNotThrow(() =>
                CityOpenAreaDecorationPlanner.ValidateOrThrow(layout, first));
        }

        private static Rect ToXZRect(Bounds bounds)
        {
            return Rect.MinMaxRect(
                bounds.min.x,
                bounds.min.z,
                bounds.max.x,
                bounds.max.z);
        }

        private static bool Overlaps(Rect left, Rect right)
        {
            return left.xMin < right.xMax &&
                   left.xMax > right.xMin &&
                   left.yMin < right.yMax &&
                   left.yMax > right.yMin;
        }

        private static float RectDistance(Rect left, Rect right)
        {
            float x = Mathf.Max(
                0f,
                Mathf.Max(right.xMin - left.xMax, left.xMin - right.xMax));
            float y = Mathf.Max(
                0f,
                Mathf.Max(right.yMin - left.yMax, left.yMin - right.yMax));
            return Mathf.Sqrt(x * x + y * y);
        }
    }
}
