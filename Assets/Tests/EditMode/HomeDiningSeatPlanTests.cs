using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HomeDiningSeatPlanTests
    {
        [TestCase(0f, -2.10f)]
        [TestCase(-.88f, -.80f)]
        [TestCase(-.88f, .46f)]
        public void DiningChair_KeepsGroundedDockAndRearApproachClearOfTheTable(float startX, float startZ)
        {
            HomeInteriorLayoutPlan layout = HomeInteriorLayoutPlanner.Generate();
            CityBenchSitPlan plan = HomeDiningSeatPlan.Create(layout);
            Assert.That(plan.Kind, Is.EqualTo(CityBenchSeatKind.DiningChair));
            Assert.That(plan.ActionHipPosition,
                Is.EqualTo(HomeDiningSeatPlan.SeatTopCenter +
                           Vector3.up * CityBenchSitPlan.SeatClearance));
            Assert.That(plan.EntryRootPosition.y,
                Is.EqualTo(PlayerFactory.GroundedRootOffset).Within(0.001f));
            Assert.That(plan.EntryRootPosition.x,
                Is.GreaterThan(HomeDiningSeatPlan.ChairCenter.x +
                               HomeDiningSeatPlan.ChairSize.x * 0.5f +
                               HomeInteriorLayoutValidator.PlayerClearanceRadius));
            Assert.That(plan.IsWithinApproachLane(plan.EntryRootPosition), Is.True);
            Assert.That(plan.IsWithinApproachLane(new Vector3(-.88f, plan.EntryRootPosition.y, -.80f)), Is.True,
                "The front-edge regression starts inside the actual admitted lane.");
            Assert.That(plan.IsWithinApproachLane(plan.InteractionPosition +
                                                 Vector3.forward), Is.False,
                "The seat must not offer a route across the tabletop or locked room.");

            var waypoints = new Vector3[CityBenchSitPlan.MaximumApproachWaypoints];
            Vector3 approach = new Vector3(startX, plan.EntryRootPosition.y, startZ);
            int count = plan.BuildApproachWaypoints(approach, waypoints);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(waypoints[0].x,
                Is.EqualTo(plan.EntryRootPosition.x).Within(0.001f));
            Assert.That(waypoints[0].z,
                Is.LessThan(HomeDiningSeatPlan.ChairCenter.z -
                            HomeDiningSeatPlan.ChairSize.z * 0.5f));
            HomeDiningSeatPlan.ValidateOrThrow(plan, layout);
        }

        [Test]
        public void DiningChair_UsesOneContinuousSideEntryFamilyAndDiningPrompt()
        {
            PlayerAnimatedInteractionDefinition definition =
                CityBenchSitInteraction.CreateDefinition(CityBenchSeatKind.DiningChair);
            Assert.That(definition.EnterClipName,
                Is.EqualTo(CityBenchSitInteraction.BoardEnterClipName));
            Assert.That(definition.LoopClipName,
                Is.EqualTo(CityBenchSitInteraction.BoardLoopClipName));
            Assert.That(definition.ExitClipName,
                Is.EqualTo(CityBenchSitInteraction.BoardExitClipName));
            Assert.That(CityBenchSitInteraction.ResolveSeatPromptKey(
                    CityBenchSeatKind.DiningChair),
                Is.EqualTo(CityBenchSitInteraction.DiningPromptKey));
        }
    }
}
