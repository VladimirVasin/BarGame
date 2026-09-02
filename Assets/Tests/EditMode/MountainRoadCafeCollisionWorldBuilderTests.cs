using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class MountainRoadCafeCollisionWorldBuilderTests
    {
        [Test]
        [Category("MountainRoad")]
        public void Build_CreatesOnlyPlanOwnedPrimitiveObstacles()
        {
            var parent = new GameObject("Cafe Collision Test Root");
            try
            {
                MountainRoadCafePlan plan = MountainRoadPlanner.Create(
                    GameSessionState.DefaultCitySeed).Terminal.Cafe;
                MountainRoadCafeCollisionWorldResult result =
                    MountainRoadCafeCollisionWorldBuilder.Build(
                        parent.transform,
                        plan);

                Assert.That(
                    result.ColliderCount,
                    Is.EqualTo(
                        MountainRoadCafeCollisionWorldBuilder
                            .ExpectedColliderCount));
                Assert.That(
                    result.StoolColliders,
                    Has.Count.EqualTo(
                        MountainRoadCafeCollisionWorldBuilder
                            .StoolColliderCount));
                Assert.That(
                    result.EntranceClearWidth,
                    Is.EqualTo(plan.DoorWidth).Within(0.0001f));
                Assert.That(
                    result.EntranceClearWidth,
                    Is.EqualTo(
                        MountainRoadCafeCollisionWorldBuilder
                            .RequiredEntranceWidth).Within(0.0001f));

                Assert.That(
                    result.Root.GetComponentsInChildren<Renderer>(true),
                    Is.Empty);
                Assert.That(
                    result.Root.GetComponentsInChildren<MeshFilter>(true),
                    Is.Empty);
                Assert.That(
                    result.Root.GetComponentsInChildren<MeshCollider>(true),
                    Is.Empty);
                Assert.That(
                    result.Root.GetComponentsInChildren<Light>(true),
                    Is.Empty);
                Assert.That(
                    result.Root.GetComponentsInChildren<Camera>(true),
                    Is.Empty);
                Assert.That(
                    result.Root.GetComponentsInChildren<AudioSource>(true),
                    Is.Empty);

                Collider[] colliders =
                    result.Root.GetComponentsInChildren<Collider>(true);
                Assert.That(
                    colliders,
                    Has.Length.EqualTo(result.ColliderCount));
                for (int index = 0; index < colliders.Length; index++)
                {
                    Assert.That(
                        colliders[index] is BoxCollider ||
                        colliders[index] is CapsuleCollider,
                        Is.True,
                        $"Unexpected collider type at index {index}.");
                    Assert.That(colliders[index].isTrigger, Is.False);
                }

                AssertPublishedDescriptorOrder(result);
                AssertExpandedKitchenRun(plan, result);
                AssertStoolPositions(plan, result);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        private static void AssertPublishedDescriptorOrder(
            MountainRoadCafeCollisionWorldResult result)
        {
            string[] expectedIds =
            {
                "boundary-west",
                "boundary-rear",
                "boundary-south-left",
                "boundary-south-right",
                "boundary-chamfer",
                "boundary-east",
                "counter-main",
                "counter-return",
                "service-cabinet",
                "fridge",
                "stool-00",
                "stool-01",
                "stool-02",
                "stool-03",
                "stool-04",
                "stool-05",
                "stool-06"
            };
            Assert.That(result.Colliders, Has.Count.EqualTo(expectedIds.Length));
            for (int index = 0; index < expectedIds.Length; index++)
            {
                Assert.That(
                    result.Colliders[index].name,
                    Is.EqualTo(expectedIds[index]),
                    $"Collider descriptor order diverged at {index}.");
                if (index < 10)
                {
                    Assert.That(
                        result.Colliders[index],
                        Is.InstanceOf<BoxCollider>());
                }
                else
                {
                    Assert.That(
                        result.Colliders[index],
                        Is.InstanceOf<CapsuleCollider>());
                }
            }
        }

        private static void AssertExpandedKitchenRun(
            MountainRoadCafePlan plan,
            MountainRoadCafeCollisionWorldResult result)
        {
            BoxCollider serviceCabinet = result.Colliders
                .Single(collider => collider.name == "service-cabinet")
                as BoxCollider;
            Assert.That(serviceCabinet, Is.Not.Null);
            Assert.That(
                serviceCabinet.size,
                Is.EqualTo(new Vector3(5.68f, 0.86f, 0.78f)));

            Vector3 offset = serviceCabinet.transform.position - plan.Center;
            Assert.That(
                Vector3.Dot(offset, plan.Right),
                Is.EqualTo(0.19f).Within(0.0001f));
            Assert.That(
                offset.y,
                Is.EqualTo(0.43f).Within(0.0001f));
            Assert.That(
                Vector3.Dot(offset, plan.Forward),
                Is.EqualTo(4.8625f).Within(0.0001f));

            BoxCollider fridge = result.Colliders
                .Single(collider => collider.name == "fridge")
                as BoxCollider;
            Assert.That(fridge, Is.Not.Null);
            Vector3 fridgeOffset = fridge.transform.position - plan.Center;
            Assert.That(
                Vector3.Dot(fridgeOffset, plan.Right),
                Is.EqualTo(-3.82f).Within(0.0001f));
            Assert.That(
                Vector3.Dot(fridgeOffset, plan.Forward),
                Is.EqualTo(4.9095f).Within(0.0001f));
        }

        private static void AssertStoolPositions(
            MountainRoadCafePlan plan,
            MountainRoadCafeCollisionWorldResult result)
        {
            foreach (CapsuleCollider stool in result.StoolColliders)
            {
                Assert.That(
                    stool.height,
                    Is.EqualTo(
                        MountainRoadCafeCollisionWorldBuilder
                            .StoolColliderHeight).Within(0.0001f));
                Assert.That(
                    stool.center.y,
                    Is.EqualTo(
                        MountainRoadCafeCollisionWorldBuilder
                            .StoolColliderCenterAboveFloor).Within(0.0001f));
                Assert.That(
                    stool.center.y + stool.height * 0.5f,
                    Is.EqualTo(
                        MountainRoadCafeWorldBuilder
                            .StoolSeatTopAboveFloor).Within(0.0001f),
                    "The physical capsule top must meet the authored bar " +
                    "stool seat instead of ending beneath the sitter.");
            }

            for (int index = 0;
                 index < MountainRoadCafeCollisionWorldBuilder
                     .MainRowStoolCount;
                 index++)
            {
                Vector3 offset =
                    result.StoolColliders[index].transform.position -
                    plan.Center;
                Assert.That(
                    Vector3.Dot(offset, plan.Right),
                    Is.EqualTo(
                        MountainRoadCafeCollisionWorldBuilder
                            .MainRowStoolRightOffsets[index])
                        .Within(0.0001f));
                Assert.That(
                    Vector3.Dot(offset, plan.Forward),
                    Is.EqualTo(
                        MountainRoadCafeCollisionWorldBuilder.StoolForward)
                        .Within(0.0001f));
            }

            for (int index = 0;
                 index < MountainRoadCafeCollisionWorldBuilder
                     .ReturnStoolLocalPositions.Length;
                 index++)
            {
                int stoolIndex =
                    MountainRoadCafeCollisionWorldBuilder.MainRowStoolCount +
                    index;
                Vector3 offset =
                    result.StoolColliders[stoolIndex].transform.position -
                    plan.Center;
                Vector2 expected =
                    MountainRoadCafeCollisionWorldBuilder
                        .ReturnStoolLocalPositions[index];
                Assert.That(
                    Vector3.Dot(offset, plan.Right),
                    Is.EqualTo(expected.x).Within(0.0001f));
                Assert.That(
                    Vector3.Dot(offset, plan.Forward),
                    Is.EqualTo(expected.y).Within(0.0001f));
                Assert.That(
                    plan.ContainsInterior(
                        result.StoolColliders[stoolIndex]
                            .transform.position,
                        MountainRoadCafeCollisionWorldBuilder
                            .StoolColliderRadius),
                    Is.True,
                    "A return stool left the five-sided footprint.");
            }
        }
    }
}
