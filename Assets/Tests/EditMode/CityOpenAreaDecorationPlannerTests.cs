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
                CityOpenAreaAccessDescriptor access =
                    layout.OpenAreaAccesses.Single(item =>
                        item.Feature == descriptor.Feature);
                Assert.That(
                    Overlaps(
                        ToXZRect(descriptor.Bounds),
                        access.ApproachBounds),
                    Is.False,
                    descriptor.StableId);
            }
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
