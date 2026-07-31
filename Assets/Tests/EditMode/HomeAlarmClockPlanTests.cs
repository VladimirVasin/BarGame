using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HomeAlarmClockPlanTests
    {
        [Test]
        public void Plan_StaysBesideThePillowAndOutsideProtectedPaths()
        {
            HomeInteriorLayoutPlan layout =
                HomeInteriorLayoutPlanner.Generate();
            HomeAlarmClockPlan plan =
                HomeAlarmClockPlan.Create(layout);
            Assert.That(
                layout.TryGetFurniture(
                    HomeFurnitureKind.Bed,
                    out HomeFurnitureFootprint bed),
                Is.True);

            Assert.That(
                plan.NightstandFootprint.Overlaps(
                    bed.Bounds,
                    true),
                Is.False);
            Assert.That(
                plan.NightstandFootprint.center.x,
                Is.LessThan(bed.Bounds.center.x));
            Assert.That(
                plan.NightstandFootprint.yMin,
                Is.GreaterThan(bed.Bounds.yMax));
            Assert.That(
                plan.ClockPosition.y,
                Is.GreaterThan(
                    HomeAlarmClockPlan.NightstandHeight +
                    HomeAlarmClockPlan.NightstandTopThickness +
                    HomeAlarmClockPlan.ClockBodySize.y *
                    0.5f));
            Assert.That(
                plan.ClockPosition.x,
                Is.EqualTo(
                    plan.NightstandFootprint.center.x)
                    .Within(0.0001f));
            Assert.That(
                plan.ClockPosition.z,
                Is.EqualTo(
                    plan.NightstandFootprint.center.y)
                    .Within(0.0001f));

            for (int index = 0;
                 index < layout.Paths.Count;
                 index++)
            {
                Assert.That(
                    plan.NightstandFootprint.Overlaps(
                        layout.Paths[index].Bounds,
                        true),
                    Is.False,
                    $"Clock overlaps path '{layout.Paths[index].Id}'.");
            }

            for (int index = 0;
                 index < layout.Furniture.Count;
                 index++)
            {
                HomeFurnitureFootprint furniture =
                    layout.Furniture[index];
                if (furniture.Kind == HomeFurnitureKind.Bed)
                {
                    continue;
                }

                Assert.That(
                    plan.NightstandFootprint.Overlaps(
                        furniture.Bounds,
                        true),
                    Is.False,
                    $"Clock overlaps furniture '{furniture.Id}'.");
            }

            Assert.That(
                layout.RoomBounds.Contains(
                    new Vector2(
                        plan.NightstandFootprint.xMin,
                        plan.NightstandFootprint.yMin)),
                Is.True);
            Assert.That(
                layout.RoomBounds.Contains(
                    new Vector2(
                        plan.NightstandFootprint.xMax,
                        plan.NightstandFootprint.yMax)),
                Is.True);
        }

        [Test]
        public void Plan_IsDeterministic()
        {
            HomeInteriorLayoutPlan layout =
                HomeInteriorLayoutPlanner.Generate();

            HomeAlarmClockPlan first =
                HomeAlarmClockPlan.Create(layout);
            HomeAlarmClockPlan second =
                HomeAlarmClockPlan.Create(layout);

            Assert.That(
                second.NightstandFootprint,
                Is.EqualTo(first.NightstandFootprint));
            Assert.That(
                second.ClockPosition,
                Is.EqualTo(first.ClockPosition));
        }
    }
}
