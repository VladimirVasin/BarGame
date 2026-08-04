using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HomeBalconySmokingPlanTests
    {
        [Test]
        public void Create_DerivesEntryExitPosesTriggerAndCameraFromBalcony()
        {
            HomeInteriorLayoutPlan interior =
                HomeInteriorLayoutPlanner.Generate();
            HomeBalconyLayoutPlan balcony =
                HomeBalconyLayoutPlanner.Generate(interior);

            HomeBalconySmokingPlan plan =
                HomeBalconySmokingPlan.Create(
                    interior,
                    balcony);
            var walkable = new RoadWalkableArea(
                balcony.WalkableRectangles);

            Assert.That(
                Vector3.Distance(
                    plan.EntryRootPosition,
                    new Vector3(
                        6.60f,
                        PlayerFactory.GroundedRootOffset,
                        -1.45f)),
                Is.LessThan(0.0001f));
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
            Assert.That(
                plan.ExitRootPosition,
                Is.EqualTo(plan.EntryRootPosition));
            Assert.That(
                Vector3.Distance(
                    plan.TriggerCenter,
                    new Vector3(6.50f, 0.90f, -1.45f)),
                Is.LessThan(0.0001f));
            Assert.That(
                plan.TriggerSize,
                Is.EqualTo(
                    new Vector3(0.70f, 1.80f, 1.20f)));
            Assert.That(
                plan.EntryHipPosition,
                Is.EqualTo(plan.ActionHipPosition));
            Assert.That(
                plan.ExitHipPosition,
                Is.EqualTo(plan.EntryHipPosition));
            AssertFinite(plan.EntryHipPosition);
            AssertFinite(plan.ExitHipPosition);
            Assert.That(
                plan.ActionHipPosition.y,
                Is.EqualTo(
                        plan.EntryRootPosition.y +
                        PlayerCharacterDimensions.PelvisHeight +
                        HomeBalconySmokingPlan.UprightVisualOffset)
                    .Within(0.0001f));
            float animatedFeetY =
                plan.ActionHipPosition.y +
                -PlayerCharacterDimensions.PelvisHeight;
            Assert.That(
                animatedFeetY,
                Is.EqualTo(
                        plan.EntryRootPosition.y +
                        HomeBalconySmokingPlan.UprightVisualOffset)
                    .Within(0.0001f));
            Assert.That(
                plan.CameraLookAt,
                Is.EqualTo(
                    plan.ActionHipPosition +
                    new Vector3(
                        HomeBalconySmokingPlan.CameraCityLookOffset,
                        0.50f,
                        0f)));
            Assert.That(
                Vector3.Dot(
                    plan.CameraLookAt - plan.ActionHipPosition,
                    HomeBalconySmokingPlan.FacingDirection),
                Is.EqualTo(
                        HomeBalconySmokingPlan.CameraCityLookOffset)
                    .Within(0.0001f),
                "The close shot must look past the hero toward city-local " +
                "+X so the city receives more of the frame.");
            Assert.That(
                Vector3.Angle(
                    plan.EntryFacingRotation * Vector3.forward,
                    Vector3.right),
                Is.LessThan(0.001f));
            Assert.That(
                Vector3.Angle(
                    plan.ExitFacingRotation * Vector3.forward,
                    Vector3.right),
                Is.LessThan(0.001f));
            Assert.That(
                plan.EntryFacingDirection,
                Is.EqualTo(Vector3.right));
            Assert.That(
                plan.ExitFacingDirection,
                Is.EqualTo(Vector3.right));
            AssertFinite(plan.EntryFacingDirection);
            AssertFinite(plan.ExitFacingDirection);
            Assert.That(
                plan.DockRootPosition,
                Is.EqualTo(plan.EntryRootPosition));
            Assert.That(
                plan.StandHipPosition,
                Is.EqualTo(plan.EntryHipPosition));
            Assert.That(
                plan.FacingRotation,
                Is.EqualTo(plan.EntryFacingRotation));
            Assert.That(
                plan.EntryRotation,
                Is.EqualTo(plan.EntryFacingRotation));
            Assert.That(
                plan.ExitRotation,
                Is.EqualTo(plan.ExitFacingRotation));
            Assert.That(
                plan.CanInteractAt(plan.EntryRootPosition),
                Is.True);
            Assert.That(
                plan.CanInteractAt(
                    plan.EntryRootPosition +
                    Vector3.left * 0.50f),
                Is.False);
            Assert.That(
                plan.CanInteractAt(
                    plan.EntryRootPosition +
                    Vector3.up *
                    (PlayerMotor.InteractionVerticalTolerance +
                     0.001f)),
                Is.False);
        }

        [Test]
        public void AnimationDefinition_UsesAll64FramesAndSlowLoopHolds()
        {
            HomeInteriorLayoutPlan interior =
                HomeInteriorLayoutPlanner.Generate();
            HomeBalconySmokingPlan plan =
                HomeBalconySmokingPlan.Create(
                    interior,
                    HomeBalconyLayoutPlanner.Generate(
                        interior));

            PlayerAnimatedInteractionDefinition definition =
                plan.CreateAnimationDefinition();

            Assert.That(
                definition.EnterClipName,
                Is.EqualTo("SmokeEnter"));
            Assert.That(
                definition.LoopClipName,
                Is.EqualTo("SmokeLoop"));
            Assert.That(
                definition.ExitClipName,
                Is.EqualTo("SmokeExit"));
            Assert.That(definition.EnterFrameCount, Is.EqualTo(24));
            Assert.That(
                definition.EnterFramesPerSecond,
                Is.EqualTo(6f));
            Assert.That(definition.LoopFrameCount, Is.EqualTo(24));
            Assert.That(
                definition.LoopFramesPerSecond,
                Is.EqualTo(6f));
            Assert.That(definition.ExitFrameCount, Is.EqualTo(16));
            Assert.That(
                definition.ExitFramesPerSecond,
                Is.EqualTo(8f));
            Assert.That(definition.TotalFrameCount, Is.EqualTo(64));
            Assert.That(
                definition.LoopDurationSeconds,
                Is.EqualTo(9.5d).Within(0.0001d));
            Assert.That(
                definition.GetLoopFrameExtraHoldSeconds(3),
                Is.EqualTo(2f));
            Assert.That(
                definition.GetLoopFrameExtraHoldSeconds(11),
                Is.EqualTo(0.65f));
            Assert.That(
                definition.GetLoopFrameExtraHoldSeconds(14),
                Is.EqualTo(0.55f));
            Assert.That(
                definition.GetLoopFrameExtraHoldSeconds(23),
                Is.EqualTo(2.30f));
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
