using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class InteriorSoundscapeAnchorPlannerTests
    {
        [Test]
        public void StairwellAnchors_AreStableAndInsideLayout()
        {
            StairwellLayoutPlan plan =
                StairwellLayoutPlanner.Generate();
            StairwellSoundscapeAnchors first =
                InteriorSoundscapeAnchorPlanner
                    .CreateStairwellLocal(plan);
            StairwellSoundscapeAnchors second =
                InteriorSoundscapeAnchorPlanner
                    .CreateStairwellLocal(plan);

            AssertStairwellAnchorsEqual(first, second);
            Rect room = Rect.MinMaxRect(
                plan.RoomSize.x * -0.5f,
                plan.RoomSize.y * -0.5f,
                plan.RoomSize.x * 0.5f,
                plan.RoomSize.y * 0.5f);
            Vector3[] anchors =
            {
                first.Ventilation,
                first.Electrical,
                first.PipeKnock,
                first.MetalStress,
                first.DistantWater,
                first.DistantMovement
            };
            for (int index = 0; index < anchors.Length; index++)
            {
                AssertPointInside(room, anchors[index]);
                Assert.That(
                    anchors[index].y,
                    Is.InRange(0f, plan.RoomHeight));
            }

            AssertPointInside(
                plan.UpperFlightBounds,
                first.MetalStress);
            Assert.That(
                first.DistantMovement,
                Is.EqualTo(plan.ApartmentEntrancePosition));
            Assert.That(
                first.Ventilation.z,
                Is.EqualTo(
                    plan.RoomSize.y * 0.5f - 0.145f));
        }

        [Test]
        public void HomeAnchors_AreStableAndMatchFixtures()
        {
            HomeInteriorLayoutPlan plan =
                HomeInteriorLayoutPlanner.Generate();
            HomeBalconyLayoutPlan balcony =
                HomeBalconyLayoutPlanner.Generate(plan);
            HomeSoundscapeAnchors first =
                InteriorSoundscapeAnchorPlanner.CreateHomeLocal(
                    plan,
                    balcony);
            HomeSoundscapeAnchors second =
                InteriorSoundscapeAnchorPlanner.CreateHomeLocal(
                    plan,
                    balcony);

            AssertHomeAnchorsEqual(first, second);
            Assert.That(
                plan.TryGetFurniture(
                    HomeFurnitureKind.Kitchen,
                    out HomeFurnitureFootprint kitchen),
                Is.True);
            Assert.That(
                plan.TryGetFurniture(
                    HomeFurnitureKind.Bed,
                    out HomeFurnitureFootprint bed),
                Is.True);
            Assert.That(
                plan.TryGetFurniture(
                    HomeFurnitureKind.Bookcase,
                    out HomeFurnitureFootprint bookcase),
                Is.True);

            AssertPointInside(kitchen.Bounds, first.Refrigerator);
            Assert.That(
                first.Refrigerator,
                Is.EqualTo(
                    HomeRefrigeratorPlan.Create(plan).SoundAnchor));
            AssertPointInside(bed.Bounds, first.SoftWood);
            AssertPointInside(bookcase.Bounds, first.Radio);
            AssertPointInside(plan.RoomBounds, first.Radiator);
            AssertPointInside(plan.BathroomBounds, first.Bathroom);
            AssertPointInside(
                balcony.BalconyBounds,
                first.BalconyNightAir);

            Vector3[] interiorAnchors =
            {
                first.Refrigerator,
                first.SoftWood,
                first.Radiator,
                first.Radio,
                first.Bathroom
            };
            for (int index = 0;
                 index < interiorAnchors.Length;
                 index++)
            {
                Assert.That(
                    interiorAnchors[index].y,
                    Is.InRange(0f, plan.RoomHeight));
            }
        }

        [Test]
        public void WorldAnchors_UseExplicitRootTransform()
        {
            var rootObject =
                new GameObject("Soundscape Anchor Root");
            try
            {
                rootObject.transform.SetPositionAndRotation(
                    new Vector3(7f, 2f, -9f),
                    Quaternion.Euler(0f, 37f, 0f));
                rootObject.transform.localScale =
                    new Vector3(1.2f, 0.9f, 1.1f);

                StairwellLayoutPlan stairwell =
                    StairwellLayoutPlanner.Generate();
                StairwellSoundscapeAnchors stairwellLocal =
                    InteriorSoundscapeAnchorPlanner
                        .CreateStairwellLocal(stairwell);
                StairwellSoundscapeAnchors stairwellWorld =
                    InteriorSoundscapeAnchorPlanner
                        .CreateStairwellWorld(
                            stairwell,
                            rootObject.transform);
                Assert.That(
                    stairwellWorld.Ventilation,
                    Is.EqualTo(
                        rootObject.transform.TransformPoint(
                            stairwellLocal.Ventilation)));
                Assert.That(
                    stairwellWorld.DistantMovement,
                    Is.EqualTo(
                        rootObject.transform.TransformPoint(
                            stairwellLocal.DistantMovement)));

                HomeInteriorLayoutPlan home =
                    HomeInteriorLayoutPlanner.Generate();
                HomeBalconyLayoutPlan balcony =
                    HomeBalconyLayoutPlanner.Generate(home);
                HomeSoundscapeAnchors homeLocal =
                    InteriorSoundscapeAnchorPlanner
                        .CreateHomeLocal(home, balcony);
                HomeSoundscapeAnchors homeWorld =
                    InteriorSoundscapeAnchorPlanner
                        .CreateHomeWorld(
                            home,
                            balcony,
                            rootObject.transform);
                Assert.That(
                    homeWorld.Refrigerator,
                    Is.EqualTo(
                        rootObject.transform.TransformPoint(
                            homeLocal.Refrigerator)));
                Assert.That(
                    homeWorld.Bathroom,
                    Is.EqualTo(
                        rootObject.transform.TransformPoint(
                            homeLocal.Bathroom)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        public void NullPlansAndRoots_AreRejected()
        {
            HomeInteriorLayoutPlan home =
                HomeInteriorLayoutPlanner.Generate();
            HomeBalconyLayoutPlan balcony =
                HomeBalconyLayoutPlanner.Generate(home);
            StairwellLayoutPlan stairwell =
                StairwellLayoutPlanner.Generate();

            Assert.Throws<ArgumentNullException>(
                () => InteriorSoundscapeAnchorPlanner
                    .CreateStairwellLocal(null));
            Assert.Throws<ArgumentNullException>(
                () => InteriorSoundscapeAnchorPlanner
                    .CreateStairwellWorld(stairwell, null));
            Assert.Throws<ArgumentNullException>(
                () => InteriorSoundscapeAnchorPlanner
                    .CreateHomeLocal(null, balcony));
            Assert.Throws<ArgumentNullException>(
                () => InteriorSoundscapeAnchorPlanner
                    .CreateHomeLocal(home, null));
            Assert.Throws<ArgumentNullException>(
                () => InteriorSoundscapeAnchorPlanner
                    .CreateHomeWorld(home, balcony, null));
        }

        private static void AssertPointInside(
            Rect bounds,
            Vector3 point)
        {
            Assert.That(
                bounds.Contains(new Vector2(point.x, point.z)),
                Is.True,
                $"{point} is outside {bounds}.");
        }

        private static void AssertStairwellAnchorsEqual(
            StairwellSoundscapeAnchors first,
            StairwellSoundscapeAnchors second)
        {
            Assert.That(second.Ventilation, Is.EqualTo(first.Ventilation));
            Assert.That(second.Electrical, Is.EqualTo(first.Electrical));
            Assert.That(second.PipeKnock, Is.EqualTo(first.PipeKnock));
            Assert.That(second.MetalStress, Is.EqualTo(first.MetalStress));
            Assert.That(second.DistantWater, Is.EqualTo(first.DistantWater));
            Assert.That(
                second.DistantMovement,
                Is.EqualTo(first.DistantMovement));
        }

        private static void AssertHomeAnchorsEqual(
            HomeSoundscapeAnchors first,
            HomeSoundscapeAnchors second)
        {
            Assert.That(
                second.Refrigerator,
                Is.EqualTo(first.Refrigerator));
            Assert.That(
                second.BalconyNightAir,
                Is.EqualTo(first.BalconyNightAir));
            Assert.That(second.SoftWood, Is.EqualTo(first.SoftWood));
            Assert.That(second.Radiator, Is.EqualTo(first.Radiator));
            Assert.That(second.Radio, Is.EqualTo(first.Radio));
            Assert.That(second.Bathroom, Is.EqualTo(first.Bathroom));
        }
    }
}
