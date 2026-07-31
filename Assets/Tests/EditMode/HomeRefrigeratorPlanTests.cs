using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HomeRefrigeratorPlanTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void Create_DerivesVisibleGeometryFromKitchen()
        {
            HomeInteriorLayoutPlan layout =
                HomeInteriorLayoutPlanner.Generate();
            Assert.That(
                layout.TryGetFurniture(
                    HomeFurnitureKind.Kitchen,
                    out HomeFurnitureFootprint kitchen),
                Is.True);

            HomeRefrigeratorPlan plan =
                HomeRefrigeratorPlan.Create(layout);

            Assert.That(plan.KitchenBounds, Is.EqualTo(kitchen.Bounds));
            Assert.That(plan.RootPosition.x, Is.InRange(-2.25f, -2.10f));
            Assert.That(
                plan.RootPosition.x,
                Is.GreaterThan(-3.924f),
                "The refrigerator must no longer sit at the shot edge.");
            Assert.That(
                Contains(kitchen.Bounds, plan.Footprint),
                Is.True);
            AssertVector(
                plan.BodyCenterLocal,
                new Vector3(0f, plan.BodySize.y * 0.5f, 0f));
            Assert.That(plan.BodySize.y, Is.GreaterThan(2f));
            Assert.That(
                plan.CavitySize.x,
                Is.LessThan(plan.BodySize.x));
            Assert.That(
                plan.CavitySize.y,
                Is.LessThan(plan.BodySize.y));
            Assert.That(
                plan.CavitySize.z,
                Is.LessThan(plan.BodySize.z));

            Vector3 closedDoorCenter =
                plan.RootPosition +
                plan.DoorPivotLocal +
                plan.DoorClosedCenterLocal;
            Assert.That(
                closedDoorCenter.x,
                Is.EqualTo(plan.RootPosition.x).Within(Tolerance));
            Assert.That(
                closedDoorCenter.z,
                Is.LessThan(plan.Footprint.yMin));
            Assert.That(plan.DoorOpenAngle, Is.InRange(95f, 110f));
            Assert.That(
                plan.HandleCenterLocal.x,
                Is.GreaterThan(plan.DoorClosedCenterLocal.x));
            Assert.That(
                plan.HandleCenterLocal.x,
                Is.LessThan(plan.DoorSize.x));
        }

        [Test]
        public void Create_ProvidesSixShelfAndTwoDoorSlots()
        {
            HomeRefrigeratorPlan plan = CreatePlan();

            Assert.That(
                plan.Slots,
                Has.Count.EqualTo(
                    HomeRefrigeratorPlan.TotalSlotCount));
            Assert.That(
                plan.Slots.Count(
                    slot =>
                        slot.Parent ==
                        HomeRefrigeratorSlotParent.Cavity),
                Is.EqualTo(HomeRefrigeratorPlan.ShelfSlotCount));
            Assert.That(
                plan.Slots.Count(
                    slot =>
                        slot.Parent ==
                        HomeRefrigeratorSlotParent.Door),
                Is.EqualTo(HomeRefrigeratorPlan.DoorSlotCount));
            Assert.That(
                plan.Slots.Select(slot => slot.Id).Distinct().Count(),
                Is.EqualTo(plan.Slots.Count));

            foreach (HomeRefrigeratorSlotPlan slot in plan.Slots)
            {
                Assert.That(slot.Size.x, Is.GreaterThan(0f));
                Assert.That(slot.Size.y, Is.GreaterThan(0f));
                Assert.That(slot.Size.z, Is.GreaterThan(0f));
                Assert.That(
                    plan.TryGetSlot(slot.Id, out HomeRefrigeratorSlotPlan found),
                    Is.True);
                Assert.That(found.Id, Is.EqualTo(slot.Id));
            }

            Assert.That(
                plan.TryGetSlot("missing", out _),
                Is.False);
        }

        [Test]
        public void Create_PlacesAllInitialItemsInDifferentShelfSlots()
        {
            HomeRefrigeratorPlan plan = CreatePlan();
            HomeRefrigeratorSlotPlan[] occupied =
                plan.Slots.Where(slot => slot.IsOccupied).ToArray();

            Assert.That(occupied, Has.Length.EqualTo(3));
            Assert.That(
                occupied.Select(slot => slot.Id).Distinct().Count(),
                Is.EqualTo(3));
            Assert.That(
                occupied.All(
                    slot =>
                        slot.Parent ==
                        HomeRefrigeratorSlotParent.Cavity),
                Is.True);
            Assert.That(
                occupied.Select(slot => slot.Occupant),
                Is.EquivalentTo(
                    new[]
                    {
                        HomeRefrigeratorItemKind.VodkaBottle,
                        HomeRefrigeratorItemKind.ChickenEgg,
                        HomeRefrigeratorItemKind.OpenStewCan
                    }));

            foreach (HomeRefrigeratorItemKind item in
                     new[]
                     {
                         HomeRefrigeratorItemKind.VodkaBottle,
                         HomeRefrigeratorItemKind.ChickenEgg,
                         HomeRefrigeratorItemKind.OpenStewCan
                     })
            {
                Assert.That(
                    plan.TryGetOccupiedSlot(
                        item,
                        out HomeRefrigeratorSlotPlan slot),
                    Is.True);
                Assert.That(slot.Occupant, Is.EqualTo(item));
            }

            Assert.That(
                plan.TryGetOccupiedSlot(
                    HomeRefrigeratorItemKind.None,
                    out _),
                Is.False);
        }

        [Test]
        public void ApproachRoute_ClearsBlockingFurnitureAtPlayerRadius()
        {
            HomeInteriorLayoutPlan layout =
                HomeInteriorLayoutPlanner.Generate();
            HomeRefrigeratorPlan plan =
                HomeRefrigeratorPlan.Create(layout);
            Assert.That(
                layout.TryGetFurniture(
                    HomeFurnitureKind.Bed,
                    out HomeFurnitureFootprint bed),
                Is.True);
            Assert.That(
                layout.TryGetFurniture(
                    HomeFurnitureKind.Table,
                    out HomeFurnitureFootprint table),
                Is.True);

            float passageWidth =
                table.Bounds.yMin - bed.Bounds.yMax;
            Assert.That(table.Bounds.yMin, Is.EqualTo(1.25f));
            Assert.That(
                passageWidth,
                Is.EqualTo(plan.ApproachClearance)
                    .Within(Tolerance));
            Assert.That(
                passageWidth,
                Is.GreaterThanOrEqualTo(
                    HomeRefrigeratorPlan
                        .RequiredApproachClearance));
            Assert.That(
                layout.TryGetPath(
                    HomeInteriorPathKind.Main,
                    out HomeInteriorPath mainPath),
                Is.True);
            Assert.That(
                Contains(
                    mainPath.Bounds,
                    plan.ApproachWaypoints[0]),
                Is.True);
            AssertVector(
                plan.ApproachWaypoints[
                    plan.ApproachWaypoints.Count - 1],
                plan.ApproachPosition);

            for (int segment = 1;
                 segment < plan.ApproachWaypoints.Count;
                 segment++)
            {
                AssertSegmentClearsFurniture(
                    plan.ApproachWaypoints[segment - 1],
                    plan.ApproachWaypoints[segment],
                    layout);
            }
        }

        [Test]
        public void InteractionAnchors_FrameAndIlluminateOpenCavity()
        {
            HomeRefrigeratorPlan plan = CreatePlan();

            Assert.That(
                Contains(plan.ApproachBounds, plan.ApproachPosition),
                Is.True);
            Assert.That(
                ContainsTrigger(
                    plan.TriggerCenter,
                    plan.TriggerSize,
                    plan.ApproachPosition),
                Is.True);
            Assert.That(
                plan.CameraPosition.z,
                Is.LessThan(plan.Footprint.yMin));
            Assert.That(
                plan.CameraLookAt.z,
                Is.GreaterThan(plan.CameraPosition.z));
            Assert.That(plan.CameraFieldOfView, Is.InRange(40f, 55f));

            Vector3 cavityOrigin =
                plan.RootPosition + plan.CavityCenterLocal;
            Vector3 localLight =
                plan.InteriorLightPosition - cavityOrigin;
            Vector3 cavityHalf = plan.CavitySize * 0.5f;
            Assert.That(
                Mathf.Abs(localLight.x),
                Is.LessThanOrEqualTo(cavityHalf.x));
            Assert.That(
                Mathf.Abs(localLight.y),
                Is.LessThanOrEqualTo(cavityHalf.y));
            Assert.That(
                Mathf.Abs(localLight.z),
                Is.LessThanOrEqualTo(cavityHalf.z));
            Assert.That(
                plan.SoundAnchor.y,
                Is.InRange(0.5f, plan.BodySize.y));
            Assert.That(
                plan.SoundAnchor.x,
                Is.EqualTo(plan.RootPosition.x).Within(Tolerance));
        }

        [Test]
        public void Create_IsDeterministicAndRejectsMissingLayout()
        {
            HomeInteriorLayoutPlan layout =
                HomeInteriorLayoutPlanner.Generate();
            HomeRefrigeratorPlan first =
                HomeRefrigeratorPlan.Create(layout);
            HomeRefrigeratorPlan second =
                HomeRefrigeratorPlan.Create(layout);

            AssertVector(second.RootPosition, first.RootPosition);
            AssertVector(second.BodySize, first.BodySize);
            AssertVector(second.CavitySize, first.CavitySize);
            AssertVector(second.DoorPivotLocal, first.DoorPivotLocal);
            Assert.That(
                second.DoorOpenAngle,
                Is.EqualTo(first.DoorOpenAngle));
            Assert.That(second.Slots.Count, Is.EqualTo(first.Slots.Count));
            for (int index = 0; index < first.Slots.Count; index++)
            {
                Assert.That(
                    second.Slots[index].Id,
                    Is.EqualTo(first.Slots[index].Id));
                AssertVector(
                    second.Slots[index].LocalPosition,
                    first.Slots[index].LocalPosition);
                Assert.That(
                    second.Slots[index].Occupant,
                    Is.EqualTo(first.Slots[index].Occupant));
            }

            Assert.That(
                () => HomeRefrigeratorPlan.Create(null),
                Throws.TypeOf<ArgumentNullException>());
        }

        private static HomeRefrigeratorPlan CreatePlan()
        {
            return HomeRefrigeratorPlan.Create(
                HomeInteriorLayoutPlanner.Generate());
        }

        private static void AssertSegmentClearsFurniture(
            Vector3 start,
            Vector3 end,
            HomeInteriorLayoutPlan layout)
        {
            const int sampleCount = 128;
            float radius =
                HomeInteriorLayoutValidator.PlayerClearanceRadius;
            for (int index = 0;
                 index <= sampleCount;
                 index++)
            {
                Vector3 point = Vector3.Lerp(
                    start,
                    end,
                    index / (float)sampleCount);
                foreach (HomeFurnitureFootprint furniture in
                         layout.Furniture)
                {
                    if (!furniture.BlocksMovement)
                    {
                        continue;
                    }

                    Rect expanded = Expand(furniture.Bounds, radius);
                    Assert.That(
                        ContainsStrict(expanded, point),
                        Is.False,
                        $"Route segment crosses '{furniture.Id}' " +
                        $"at {point}.");
                }
            }
        }

        private static Rect Expand(Rect bounds, float radius)
        {
            return Rect.MinMaxRect(
                bounds.xMin - radius,
                bounds.yMin - radius,
                bounds.xMax + radius,
                bounds.yMax + radius);
        }

        private static bool ContainsStrict(Rect bounds, Vector3 point)
        {
            return point.x > bounds.xMin + Tolerance &&
                   point.x < bounds.xMax - Tolerance &&
                   point.z > bounds.yMin + Tolerance &&
                   point.z < bounds.yMax - Tolerance;
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin - Tolerance &&
                   inner.xMax <= outer.xMax + Tolerance &&
                   inner.yMin >= outer.yMin - Tolerance &&
                   inner.yMax <= outer.yMax + Tolerance;
        }

        private static bool Contains(Rect bounds, Vector3 point)
        {
            return point.x >= bounds.xMin - Tolerance &&
                   point.x <= bounds.xMax + Tolerance &&
                   point.z >= bounds.yMin - Tolerance &&
                   point.z <= bounds.yMax + Tolerance;
        }

        private static bool ContainsTrigger(
            Vector3 center,
            Vector3 size,
            Vector3 point)
        {
            Vector3 half = size * 0.5f;
            return point.x >= center.x - half.x - Tolerance &&
                   point.x <= center.x + half.x + Tolerance &&
                   point.y >= center.y - half.y - Tolerance &&
                   point.y <= center.y + half.y + Tolerance &&
                   point.z >= center.z - half.z - Tolerance &&
                   point.z <= center.z + half.z + Tolerance;
        }

        private static void AssertVector(
            Vector3 actual,
            Vector3 expected)
        {
            Assert.That(
                Vector3.Distance(actual, expected),
                Is.LessThan(Tolerance));
        }
    }
}
