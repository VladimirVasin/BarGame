using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HomeBalconySmokingPlanTests
    {
        [Test]
        public void Create_DerivesDockTriggerAndCameraFromBalcony()
        {
            HomeInteriorLayoutPlan interior =
                HomeInteriorLayoutPlanner.Generate();
            HomeBalconyLayoutPlan balcony =
                HomeBalconyLayoutPlanner.Generate(interior);

            HomeBalconySmokingPlan plan =
                HomeBalconySmokingPlan.Create(
                    interior,
                    balcony);

            Assert.That(
                Vector3.Distance(
                    plan.DockRootPosition,
                    new Vector3(6.60f, 0.12f, -1.45f)),
                Is.LessThan(0.0001f));
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
                plan.StandHipPosition,
                Is.EqualTo(plan.ActionHipPosition));
            Assert.That(
                plan.ActionHipPosition.y,
                Is.EqualTo(0.875f).Within(0.0001f));
            float animatedFeetY =
                plan.ActionHipPosition.y +
                (PlayerSpriteRig.FeetPivotPixels -
                 PlayerAnimatedInteractionController.HipPivotYPixels) /
                PlayerAnimatedInteractionController.PixelsPerUnit;
            Assert.That(
                animatedFeetY,
                Is.EqualTo(
                        plan.DockRootPosition.y +
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
                    plan.FacingRotation * Vector3.forward,
                    Vector3.right),
                Is.LessThan(0.001f));
            Assert.That(
                plan.CanInteractAt(plan.DockRootPosition),
                Is.True);
            Assert.That(
                plan.CanInteractAt(
                    plan.DockRootPosition +
                    Vector3.left * 0.50f),
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
                definition.TextureResourcePath,
                Is.EqualTo(
                    HomeBalconySmokingPlan.AtlasResourcePath));
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
            Assert.That(definition.RenderAboveSceneDepth, Is.False);
            Assert.That(
                definition.TextureFlipX,
                Is.False,
                "The smoking atlas already faces city-local +X and must " +
                "not receive the shared bed-atlas mirror.");
            Assert.That(
                definition.VisualCrossfadeDurationSeconds,
                Is.EqualTo(
                        HomeBalconySmokingPlan
                            .VisualCrossfadeDurationSeconds)
                    .Within(0.0001f));
            Assert.That(
                definition.AlignBillboardToCameraPlane,
                Is.False,
                "The upright balcony pose must preserve world up instead " +
                "of inheriting the pitched close-camera plane.");
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
    }
}
