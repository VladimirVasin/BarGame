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
                    bed.Bounds.xMax -
                    HomeBedInteractionPlan
                        .DoorSideDockFootInset)
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
                Is.EqualTo(bed.Bounds.xMax)
                    .Within(Tolerance));
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
            Assert.That(
                plan.ActionHipPosition.y,
                Is.EqualTo(
                    HomeInteriorWorldBuilder
                        .BedDressingSurfaceHeight +
                    HomeBedInteractionPlan
                        .BedSurfaceClearance)
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
