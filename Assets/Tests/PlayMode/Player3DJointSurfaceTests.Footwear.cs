using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class Player3DJointSurfaceTests
    {
        [Test]
        public void Footwear_FlexesAtAnkleAndForefootWhileKeepingSupportedSolesClear()
        {
            var owner = new GameObject("Test hero footwear");
            try
            {
                Player3DAssetRegistry registry = Player3DResources.Instantiate(owner.transform);
                registry.Animator.enabled = false;
                PlayerBootDeformation boots = registry.GetComponent<PlayerBootDeformation>();
                Assert.That(boots, Is.Not.Null);
                Assert.That(boots.HasBindings, Is.True);
                boots.enabled = false;
                PlayerBootDeformation.FootBinding foot = boots.Bindings.Single(binding => binding.Side == FootSide.Right);
                SkinnedMeshRenderer sole = foot.Shapes.Single(shape => shape.Renderer.name == "CLO_BootSole.R").Renderer;
                SkinnedMeshRenderer upper = foot.Shapes.Single(shape => shape.Renderer.name == "CLO_Boot.R").Renderer;
                var immutable = foot.Shapes.ToDictionary(shape => shape.Renderer.sharedMesh, shape => shape.Renderer.sharedMesh.vertices);
                Vector3[] soleRest = BakedFootwearPoints(sole), upperRest = BakedFootwearPoints(upper);
                Vector3 forward = Vector3.ProjectOnPlane(foot.Foot.TransformDirection(foot.FootForwardLocal), Vector3.up).normalized;
                float toe = soleRest.Max(point => Vector3.Dot(point, forward));
                float floor = foot.Foot.TransformPoint(foot.BallContactLocal).y;
                int[] frontSole = Enumerable.Range(0, soleRest.Length).Where(index =>
                    soleRest[index].y < floor + .002f && Vector3.Dot(soleRest[index], forward) > toe - .035f).ToArray();
                float shaftTop = upperRest.Max(point => point.y);
                int[] shaft = Enumerable.Range(0, upperRest.Length).Where(index => upperRest[index].y > shaftTop - .025f).ToArray();
                Assert.That(frontSole, Is.Not.Empty, "The actual forefoot sole must be measurable.");
                Assert.That(shaft, Is.Not.Empty, "The leather shaft must retain a measurable upper edge.");
                Vector3 position = registry.transform.position;
                var rest = registry.GetComponentsInChildren<Transform>(true).ToDictionary(bone => bone, bone => bone.localRotation);
                foreach (float angle in new[] { 0f, 7.5f, 15f, 22.5f, 30f, 37.5f, 45f })
                {
                    foreach (KeyValuePair<Transform, Quaternion> pose in rest) pose.Key.localRotation = pose.Value;
                    registry.transform.position = position;
                    FootGroundSample support = PoseBootRoll(registry, angle, floor);
                    boots.ApplyPose(FootGroundSample.None, FootGroundSample.None);
                    Vector3[] rigid = BakedFootwearPoints(sole);
                    boots.ApplyPose(support, support);
                    Vector3 placement = GroundCompletedBoot(registry, sole, floor);
                    for (int index = 0; index < rigid.Length; index++) rigid[index] += placement;
                    Vector3[] flexible = BakedFootwearPoints(sole), leather = BakedFootwearPoints(upper);
                    Assert.That(flexible.All(IsFinite), Is.True);
                    Assert.That(leather.All(IsFinite), Is.True);
                    Assert.That(frontSole.Min(index => flexible[index].y), Is.GreaterThanOrEqualTo(floor - .002f),
                        "The supported front sole must clear the floor at " + angle + " degrees.");
                    Assert.That(frontSole.Max(index => flexible[index].y), Is.LessThan(floor + .018f),
                        "The toe must stay near its support instead of curling away.");
                    if (angle >= 15f)
                    {
                        Assert.That(frontSole.Min(index => rigid[index].y), Is.LessThan(floor - .005f),
                            "This pose must actually require forefoot flex, rather than testing an unchanged boot.");
                        Assert.That(frontSole.Max(index => Vector3.Distance(rigid[index], flexible[index])), Is.GreaterThan(.005f));
                    }
                    Vector3 carried = registry.transform.position - position;
                    Assert.That(shaft.Max(index => Vector3.Distance(upperRest[index] + carried, leather[index])), Is.LessThan(.015f),
                        "The shaft must follow the shin while the foot rolls inside the boot.");
                    boots.ApplyPose(FootGroundSample.None, FootGroundSample.None);
                    AssertToeKeysClear(foot, "Airborne feet must release forefoot roll.");
                    var missingToe = new FootGroundSample(true, floor, floor, Vector3.up, FootSurfaceKind.Flat, false);
                    boots.ApplyPose(missingToe, missingToe);
                    AssertToeKeysClear(foot, "A heel hit must not invent support for a missed toe ray.");
                }
                boots.ApplyPose(new FootGroundSample(true, floor, floor, Vector3.up, FootSurfaceKind.Flat),
                    new FootGroundSample(true, floor, floor, Vector3.up, FootSurfaceKind.Flat));
                Player3DAssetRegistry mirror = Player3DResources.Instantiate(owner.transform);
                mirror.Animator.enabled = false;
                PlayerBootDeformation reflected = mirror.GetComponent<PlayerBootDeformation>();
                reflected.enabled = false;
                var sourceBones = registry.GetComponentsInChildren<Transform>(true).GroupBy(bone => bone.name)
                    .ToDictionary(group => group.Key, group => group.First());
                foreach (Transform bone in mirror.GetComponentsInChildren<Transform>(true))
                    if (sourceBones.TryGetValue(bone.name, out Transform original))
                    { bone.localPosition = original.localPosition; bone.localRotation = original.localRotation; bone.localScale = original.localScale; }
                boots.CopyPoseTo(reflected);
                PlayerBootDeformation.FootBinding reflectedFoot = reflected.Bindings.Single(binding => binding.Side == FootSide.Right);
                foreach (PlayerBootDeformation.ShapeBinding shape in foot.Shapes)
                {
                    SkinnedMeshRenderer copy = reflectedFoot.Shapes.Single(binding => binding.Renderer.name == shape.Renderer.name).Renderer;
                    Assert.That(BakedFootwearPoints(shape.Renderer).Zip(BakedFootwearPoints(copy), Vector3.Distance).Max(), Is.LessThan(.0001f),
                        "The mirror must display the completed leather/sole pose without another ground decision.");
                    Assert.That(shape.Renderer.sharedMesh.vertices, Is.EqualTo(immutable[shape.Renderer.sharedMesh]),
                        "Footwear deformation must preserve imported geometry.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        internal static FootGroundSample PoseBootRoll(Player3DAssetRegistry registry, float degrees, float floor)
        {
            PlayerBootDeformation boots = registry.GetComponent<PlayerBootDeformation>();
            PlayerBootDeformation.FootBinding foot = boots.Bindings.Single(binding => binding.Side == FootSide.Right);
            Vector3 forward = Vector3.ProjectOnPlane(foot.Foot.TransformDirection(foot.FootForwardLocal), Vector3.up).normalized;
            foot.Foot.rotation = Quaternion.AngleAxis(degrees, Vector3.Cross(Vector3.up, forward)) * foot.Foot.rotation;
            registry.transform.position += Vector3.up * (floor - foot.Foot.TransformPoint(foot.BallContactLocal).y);
            return new FootGroundSample(true, floor, floor, Vector3.up, FootSurfaceKind.Flat);
        }

        internal static Vector3 GroundCompletedBoot(Player3DAssetRegistry registry, SkinnedMeshRenderer sole, float floor)
        {
            // Like the world leg placement, ground the completed visible sole,
            // rather than an undeformed point below the leather flex hinge.
            Vector3 placement = Vector3.up * (floor - BakedFootwearPoints(sole).Min(point => point.y));
            registry.transform.position += placement;
            return placement;
        }

        private static void AssertToeKeysClear(PlayerBootDeformation.FootBinding foot, string message)
        {
            foreach (PlayerBootDeformation.ShapeBinding shape in foot.Shapes)
            {
                Assert.That(shape.Renderer.GetBlendShapeWeight(shape.Toe15), Is.Zero, message);
                Assert.That(shape.Renderer.GetBlendShapeWeight(shape.Toe30), Is.Zero, message);
                Assert.That(shape.Renderer.GetBlendShapeWeight(shape.Toe45), Is.Zero, message);
            }
        }

        private static bool IsFinite(Vector3 point) => !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
            !float.IsNaN(point.y) && !float.IsInfinity(point.y) && !float.IsNaN(point.z) && !float.IsInfinity(point.z);

        private static Vector3[] BakedFootwearPoints(SkinnedMeshRenderer renderer)
        {
            var mesh = new Mesh();
            try { renderer.BakeMesh(mesh, true); return mesh.vertices.Select(renderer.transform.TransformPoint).ToArray(); }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
}
