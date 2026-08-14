using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class SupermarketSecurityCameraTests
    {
        [Test]
        public void HeadPositions_CoverAllFourCornersUnderTheCeiling()
        {
            SupermarketInteriorLayoutPlan plan =
                SupermarketInteriorLayoutPlanner.Generate(20260815);

            Vector3[] positions = SupermarketSecurityCameraWorldBuilder
                .ResolveHeadPositions(plan);

            Assert.That(
                positions,
                Has.Length.EqualTo(
                    SupermarketSecurityCameraWorldBuilder.CameraCount));
            float expectedX = (plan.RoomSize.x * 0.5f) -
                plan.WallThickness -
                SupermarketSecurityCameraWorldBuilder.CornerInsetMeters;
            float expectedZ = (plan.RoomSize.y * 0.5f) -
                plan.WallThickness -
                SupermarketSecurityCameraWorldBuilder.CornerInsetMeters;
            var seenQuadrants =
                new System.Collections.Generic.HashSet<(bool, bool)>();
            for (int index = 0; index < positions.Length; index++)
            {
                Vector3 position = positions[index];
                Assert.That(
                    Mathf.Abs(position.x),
                    Is.EqualTo(expectedX).Within(0.001f));
                Assert.That(
                    Mathf.Abs(position.z),
                    Is.EqualTo(expectedZ).Within(0.001f));
                Assert.That(
                    position.y,
                    Is.EqualTo(
                        plan.RoomHeight -
                        SupermarketSecurityCameraWorldBuilder
                            .HeadDropMeters).Within(0.001f));
                Assert.That(
                    position.y,
                    Is.LessThan(plan.RoomHeight));
                seenQuadrants.Add((position.x > 0f, position.z > 0f));
            }

            Assert.That(
                seenQuadrants.Count,
                Is.EqualTo(4),
                "Each camera must take its own corner.");
        }

        [Test]
        public void ResolveAim_PointsTheLensAtTheFocus()
        {
            Vector3 head = new Vector3(-7f, 3.2f, -4.5f);
            Vector3 focus = new Vector3(2f, 1.5f, 3f);

            Quaternion aim = SupermarketSecurityCamera.ResolveAim(
                head,
                focus);

            Vector3 lensForward = aim * Vector3.forward;
            Vector3 expected = (focus - head).normalized;
            Assert.That(
                Vector3.Dot(lensForward, expected),
                Is.GreaterThan(0.9999f));

            // A degenerate focus point must not throw or spin.
            Assert.That(
                SupermarketSecurityCamera.ResolveAim(head, head),
                Is.EqualTo(Quaternion.identity));
        }

        [Test]
        public void Build_TracksTheHeroFromEveryCorner()
        {
            SupermarketInteriorLayoutPlan plan =
                SupermarketInteriorLayoutPlanner.Generate(20260815);
            var parent = new GameObject("Camera Test Root");
            var hero = new GameObject("Camera Test Hero");
            try
            {
                hero.transform.position =
                    new Vector3(-3.2f, 0f, 1.4f);
                var cameras = SupermarketSecurityCameraWorldBuilder
                    .Build(
                        parent.transform,
                        plan,
                        hero.transform);

                Assert.That(
                    cameras,
                    Has.Count.EqualTo(
                        SupermarketSecurityCameraWorldBuilder
                            .CameraCount));
                Vector3 focus = hero.transform.position +
                    Vector3.up *
                    SupermarketSecurityCamera.FocusHeightMeters;
                for (int index = 0; index < cameras.Count; index++)
                {
                    SupermarketSecurityCamera camera = cameras[index];
                    Assert.That(camera.HeadPivot, Is.Not.Null);

                    // Aimed at the hero from the very first frame.
                    Vector3 toFocus =
                        (focus - camera.HeadPivot.position).normalized;
                    Assert.That(
                        Vector3.Dot(
                            camera.HeadPivot.forward,
                            toFocus),
                        Is.GreaterThan(0.999f));

                    // The servo follows when the hero moves.
                    hero.transform.position =
                        new Vector3(5.5f, 0f, -4.2f);
                    camera.Track(10f);
                    Vector3 movedFocus = hero.transform.position +
                        Vector3.up *
                        SupermarketSecurityCamera.FocusHeightMeters;
                    Assert.That(
                        Vector3.Dot(
                            camera.HeadPivot.forward,
                            (movedFocus -
                             camera.HeadPivot.position).normalized),
                        Is.GreaterThan(0.999f));
                    hero.transform.position =
                        new Vector3(-3.2f, 0f, 1.4f);
                    camera.Track(10f);
                }

                Assert.That(
                    parent.GetComponentsInChildren<Collider>(true),
                    Is.Empty,
                    "CCTV is dressing: no colliders.");
                Assert.That(
                    parent.GetComponentsInChildren<Light>(true),
                    Is.Empty,
                    "CCTV must not spend the light budget.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
                Object.DestroyImmediate(hero);
            }
        }
    }
}
