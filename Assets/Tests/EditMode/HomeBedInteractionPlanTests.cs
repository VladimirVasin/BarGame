using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HomeBedInteractionPlanTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void Create_DerivesTriggerAndEntryExitPosesFromBed()
        {
            HomeInteriorLayoutPlan home =
                HomeInteriorLayoutPlanner.Generate();
            Assert.That(
                home.TryGetFurniture(
                    HomeFurnitureKind.Bed,
                    out HomeFurnitureFootprint bed),
                Is.True);

            HomeBedInteractionPlan plan =
                HomeBedInteractionPlan.Create(home);
            var walkable = new RoadWalkableArea(
                new[] { home.WalkableBounds });

            Assert.That(plan.BedBounds, Is.EqualTo(bed.Bounds));
            AssertVector(
                plan.InteractionPosition,
                plan.EntryRootPosition);
            Assert.That(
                plan.EntryRootPosition.x,
                Is.EqualTo(
                    bed.Bounds.center.x +
                    HomeBedInteractionPlan
                        .ActionHipFootwardOffset)
                    .Within(Tolerance));
            Assert.That(
                plan.EntryRootPosition.z,
                Is.LessThan(bed.Bounds.yMin));
            Assert.That(
                plan.EntryRootPosition.y,
                Is.EqualTo(PlayerFactory.GroundedRootOffset)
                    .Within(Tolerance));
            AssertFinite(plan.EntryRootPosition);
            AssertFinite(plan.ExitRootPosition);
            Assert.That(
                walkable.Contains(
                    plan.EntryRootPosition,
                    HomeInteriorLayoutValidator
                        .PlayerClearanceRadius),
                Is.True);
            Assert.That(
                walkable.Contains(
                    plan.ExitRootPosition,
                    HomeInteriorLayoutValidator
                        .PlayerClearanceRadius),
                Is.True);
            AssertVector(
                plan.ExitRootPosition,
                plan.EntryRootPosition);

            // The old foot-side dock avoided the storage pile by accident.
            // Moving to the middle must preserve the actual standing space.
            foreach (HomeFurnitureFootprint furniture in home.Furniture)
            {
                if (!furniture.BlocksMovement) continue;
                float clearance = HomeInteriorLayoutValidator.PlayerClearanceRadius;
                Rect occupied = Rect.MinMaxRect(
                    furniture.Bounds.xMin - clearance, furniture.Bounds.yMin - clearance,
                    furniture.Bounds.xMax + clearance, furniture.Bounds.yMax + clearance);
                Assert.That(occupied.Contains(new Vector2(
                    plan.EntryRootPosition.x, plan.EntryRootPosition.z)), Is.False,
                    $"The bed dock must stay clear of {furniture.Id}.");
            }

            float triggerMaxX =
                plan.TriggerCenter.x +
                (plan.TriggerSize.x * 0.5f);
            float triggerMinY =
                plan.TriggerCenter.y -
                (plan.TriggerSize.y * 0.5f);
            float triggerMaxZ =
                plan.TriggerCenter.z +
                (plan.TriggerSize.z * 0.5f);
            Assert.That(
                triggerMaxX,
                Is.LessThan(bed.Bounds.xMax));
            Assert.That(
                plan.TriggerCenter.x - plan.TriggerSize.x * 0.5f,
                Is.GreaterThan(bed.Bounds.xMin));
            Assert.That(
                triggerMaxZ,
                Is.EqualTo(bed.Bounds.yMin)
                    .Within(Tolerance));
            Assert.That(
                triggerMinY,
                Is.EqualTo(0f).Within(Tolerance));
            Assert.That(
                plan.TriggerCenter.x,
                Is.EqualTo(plan.EntryRootPosition.x)
                    .Within(Tolerance));
            Assert.That(
                plan.TriggerSize.x,
                Is.GreaterThan(0f));
            Assert.That(
                plan.TriggerSize.y,
                Is.GreaterThan(0f));
            Assert.That(
                plan.TriggerSize.z,
                Is.GreaterThan(0f));
            Assert.That(
                plan.EntryRootPosition.z,
                Is.InRange(
                    triggerMaxZ - plan.TriggerSize.z,
                    triggerMaxZ));

            Assert.That(
                plan.EntryHipPosition.x,
                Is.EqualTo(plan.EntryRootPosition.x)
                    .Within(Tolerance));
            Assert.That(
                plan.EntryHipPosition.z,
                Is.EqualTo(plan.EntryRootPosition.z)
                    .Within(Tolerance));
            float expectedStandHipY =
                plan.EntryRootPosition.y +
                PlayerCharacterDimensions.PelvisHeight +
                HomeBedInteractionPlan.UprightVisualOffset;
            Assert.That(
                plan.EntryHipPosition.y,
                Is.EqualTo(expectedStandHipY)
                    .Within(Tolerance));
            AssertFinite(plan.EntryHipPosition);
            AssertFinite(plan.ExitHipPosition);
            AssertVector(
                plan.ExitHipPosition,
                plan.EntryHipPosition);
            Assert.That(
                plan.SeatHipPosition.x,
                Is.EqualTo(plan.EntryRootPosition.x)
                    .Within(Tolerance));
            Assert.That(
                plan.SeatHipPosition.x,
                Is.EqualTo(plan.ActionHipPosition.x).Within(Tolerance),
                "Sitting and sleeping must share the same longitudinal " +
                "pelvis position, without travel toward the pillow.");
            Assert.That(
                plan.SeatHipPosition.y,
                Is.EqualTo(HomeBedInteractionPlan.SeatedHipHeight)
                    .Within(Tolerance));
            Assert.That(
                plan.SeatHipPosition.z,
                Is.EqualTo(
                    bed.Bounds.yMin +
                    HomeBedInteractionPlan.DoorSideSeatInset)
                    .Within(Tolerance));
            Assert.That(
                plan.SeatHipPosition.z,
                Is.InRange(bed.Bounds.yMin, bed.Bounds.yMax));

            PlayerAnimatedInteractionPelvisTransition transition =
                plan.PelvisTransition;
            AssertVector(
                transition.EvaluateEntering(
                    plan.EntryHipPosition,
                    plan.ActionHipPosition,
                    HomeBedInteractionPlan.EnterSeatArrivalProgress),
                plan.SeatHipPosition);
            AssertVector(
                transition.EvaluateEntering(
                    plan.EntryHipPosition,
                    plan.ActionHipPosition,
                    HomeBedInteractionPlan.EnterSeatDepartureProgress),
                plan.SeatHipPosition);
            AssertVector(
                transition.EvaluateExiting(
                    plan.ActionHipPosition,
                    plan.ExitHipPosition,
                    HomeBedInteractionPlan.ExitSeatArrivalProgress),
                plan.SeatHipPosition);
            AssertVector(
                transition.EvaluateExiting(
                    plan.ActionHipPosition,
                    plan.ExitHipPosition,
                    HomeBedInteractionPlan.ExitSeatDepartureProgress),
                plan.SeatHipPosition);

            AssertFinite(plan.EntryFacingDirection);
            AssertFinite(plan.ExitFacingDirection);
            AssertVector(
                plan.EntryFacingDirection,
                Vector3.back);
            AssertVector(
                plan.ExitFacingDirection,
                Vector3.back);
            Assert.That(
                Vector3.Angle(
                    plan.EntryFacingRotation * Vector3.forward,
                    Vector3.back),
                Is.LessThan(0.001f));
            Assert.That(
                Vector3.Angle(
                    plan.ExitFacingRotation * Vector3.forward,
                    Vector3.back),
                Is.LessThan(0.001f));
            Assert.That(
                plan.EntryRotation,
                Is.EqualTo(plan.EntryFacingRotation));
            Assert.That(
                plan.ExitRotation,
                Is.EqualTo(plan.ExitFacingRotation));

            AssertVector(
                plan.ApproachRootPosition,
                plan.EntryRootPosition);
            AssertVector(
                plan.StandHipPosition,
                plan.EntryHipPosition);

            Assert.That(
                plan.ActionHipPosition.x,
                Is.EqualTo(
                    bed.Bounds.center.x +
                    HomeBedInteractionPlan
                        .ActionHipFootwardOffset)
                    .Within(Tolerance));
            Assert.That(
                plan.ActionHipPosition.z,
                Is.EqualTo(bed.Bounds.center.y)
                    .Within(Tolerance));
            // The mattress dents under him, and he lies in that dent
            // rather than hovering over it: the sleeping hip descends by
            // the sink depth.
            Assert.That(
                plan.ActionHipPosition.y,
                Is.EqualTo(
                    HomeInteriorWorldBuilder
                        .BedMattressSurfaceHeight +
                    PlayerCharacterDimensions
                        .SupinePelvisSupportOffset -
                    HomeInteriorWorldBuilder
                        .BedSleeperSinkDepth)
                    .Within(Tolerance));

            AssertVector(
                plan.HeadToFootAxis,
                Vector3.right);
            Assert.That(
                plan.HeadToFootAxis.magnitude,
                Is.EqualTo(1f).Within(Tolerance));
            Assert.That(
                plan.ActionHipPosition.x,
                Is.InRange(bed.Bounds.xMin, bed.Bounds.xMax));
            Assert.That(
                plan.ActionHipPosition.z,
                Is.InRange(bed.Bounds.yMin, bed.Bounds.yMax));
        }

        [Test]
        public void Create_RejectsMissingLayout()
        {
            Assert.That(
                () => HomeBedInteractionPlan.Create(null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PelvisPath_TransfersWeightBeforeMovingAcrossTheBed(bool entering)
        {
            HomeBedInteractionPlan plan = HomeBedInteractionPlan.Create(HomeInteriorLayoutPlanner.Generate());
            PlayerAnimatedInteractionPelvisTransition transition = plan.PelvisTransition;
            Vector3 Sample(float time) => entering
                ? transition.EvaluateEntering(plan.EntryHipPosition, plan.ActionHipPosition, time)
                : transition.EvaluateExiting(plan.ActionHipPosition, plan.ExitHipPosition, time);
            float begin = entering ? 0.30f : 0.32f;
            float end = entering ? 0.64f : 0.57f;
            Vector3 previous = Sample(begin);
            bool previouslyMoving = false;
            int transfers = 0;
            for (int index = 1; index <= 1000; index++)
            {
                Vector3 current = Sample(Mathf.Lerp(begin, end, index / 1000f));
                bool moving = Mathf.Abs(current.z - previous.z) > 0.000001f;
                if (moving)
                {
                    Assert.That(current.y, Is.GreaterThanOrEqualTo(
                        HomeBedInteractionPlan.SeatedHipHeight + 0.065f),
                        "The pelvis must leave the mattress before it moves across it.");
                    if (!previouslyMoving) transfers++;
                }
                Assert.That(current.x, Is.EqualTo(plan.ActionHipPosition.x).Within(Tolerance));
                previouslyMoving = moving;
                previous = current;
            }
            Assert.That(transfers, Is.EqualTo(2), "Two separate pushes must replace the continuous glide.");
            Vector3 rest = Sample(entering ? 0.455f : 0.445f);
            Assert.That(rest.y, Is.EqualTo(HomeBedInteractionPlan.SeatedHipHeight).Within(Tolerance));
            AssertVector(rest, Sample(entering ? 0.465f : 0.455f));
        }

        private static void AssertVector(
            Vector3 actual,
            Vector3 expected)
        {
            Assert.That(
                Vector3.Distance(actual, expected),
                Is.LessThan(Tolerance));
        }

        private static void AssertFinite(Vector3 value)
        {
            Assert.That(IsFinite(value.x), Is.True);
            Assert.That(IsFinite(value.y), Is.True);
            Assert.That(IsFinite(value.z), Is.True);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
