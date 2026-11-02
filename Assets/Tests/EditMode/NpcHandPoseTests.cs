using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.EditMode
{
    public sealed class NpcHandPoseTests
    {
        private const string Cylinder = "CylinderGrip", Bottle = "BottleGrip";

        [Test]
        public void OwnedBottleGrip_RebindsChangedMeshesAndRestoresPreviousGrip()
        {
            var root = new GameObject("Synthetic hand grip regression");
            root.SetActive(false);
            Mesh leftMesh = CreateMesh("Left cylinder", Cylinder);
            Mesh original = CreateMesh("Original right grip", Cylinder, Bottle);
            Mesh replacement = CreateMesh("Replacement right grip", Cylinder, Bottle);
            NpcHandPose pose = null;
            SkinnedMeshRenderer rightRenderer = null;
            var owner = new GameObject("Bottle grip owner");
            var otherOwner = new GameObject("Other grip owner");
            owner.transform.SetParent(root.transform, false);
            otherOwner.transform.SetParent(root.transform, false);
            try
            {
                NpcHandPose.HandBinding left = CreateHand(root.transform, true, leftMesh, out SkinnedMeshRenderer leftRenderer);
                NpcHandPose.HandBinding right = CreateHand(root.transform, false, original, out rightRenderer);
                pose = root.AddComponent<NpcHandPose>();
                pose.Configure(new[] { left, right }, Cylinder, .015f);
                root.SetActive(true);
                pose.SetGrip(true, .2f);
                pose.SetGrip(false, .35f);
                Assert.That(pose.TryCaptureGrip(owner, false, NpcHandGripKind.Bottle, exclusive: true), Is.True);
                Assert.That(pose.SetOwnedGrip(owner, false, .7f), Is.True);
                AssertWeights(rightRenderer, 0f, 70f);

                // A live reimport may empty the same native mesh before its
                // morphs return; ownership and its requested weight survive.
                original.ClearBlendShapes();
                Assert.That(original.blendShapeCount, Is.Zero);
                Assert.That(pose.SetOwnedGrip(owner, false, .7f), Is.True);
                Assert.That(pose.GetGripKind(false), Is.EqualTo(NpcHandGripKind.Bottle));
                Assert.That(pose.RightGripWeight, Is.EqualTo(.7f));
                Assert.That(pose.SetOwnedGrip(otherOwner, false, .1f), Is.False);

                AddShape(original, Bottle);
                AddShape(original, Cylinder);
                Assert.That(pose.SetOwnedGrip(owner, false, .7f), Is.True);
                AssertWeights(rightRenderer, 0f, 70f);

                // The replacement has the same count but opposite mapping.
                rightRenderer.sharedMesh = replacement;
                Assert.That(pose.SetOwnedGrip(owner, false, .7f), Is.True);
                AssertWeights(rightRenderer, 0f, 70f);

                rightRenderer.sharedMesh = original;
                Assert.That(pose.SetOwnedGrip(owner, false, .7f), Is.True);
                AssertWeights(rightRenderer, 0f, 70f);
                Assert.That(pose.ReleaseGrip(owner, false), Is.True);
                Assert.That(pose.GetGripKind(false), Is.EqualTo(NpcHandGripKind.Cylindrical));
                Assert.That(pose.RightGripWeight, Is.EqualTo(.35f));
                AssertWeights(rightRenderer, 35f, 0f);
                Assert.That(leftRenderer.GetBlendShapeWeight(leftMesh.GetBlendShapeIndex(Cylinder)), Is.EqualTo(20f));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (rightRenderer != null) rightRenderer.sharedMesh = original;
                if (pose != null) pose.ReleaseGrip(owner, false);
                root.SetActive(false);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(leftMesh);
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(replacement);
            }
        }

        [Test]
        public void LegacyJointCorrection_RebindsChangedMeshesWithoutWritingUnrelatedShape()
        {
            const string joint = "JointVolume.Wrist.R", unrelated = "Unrelated";
            const float unrelatedWeight = 37f;
            var root = new GameObject("Synthetic wrist correction regression");
            Mesh original = CreateMesh("Original wrist correction", joint, unrelated);
            Mesh replacement = CreateMesh("Replacement wrist correction", joint, unrelated);
            try
            {
                var forearm = new GameObject("forearm.R").transform; forearm.SetParent(root.transform, false);
                var hand = new GameObject("hand.R").transform; hand.SetParent(forearm, false);
                hand.localPosition = Vector3.up;
                var surface = new GameObject("Wrist surface"); surface.transform.SetParent(hand, false);
                var renderer = surface.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = original; renderer.bones = new[] { hand }; renderer.rootBone = hand;
                CharacterJointDeformation.Configure(root);
                var correction = root.GetComponent<CharacterJointDeformation>();
                Assert.That(correction, Is.Not.Null); Assert.That(correction.HasBindings, Is.True);

                // Old serialized prefabs carry only the authored shape index.
                var bindings = (System.Array)typeof(CharacterJointDeformation).GetField("bindings",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(correction);
                object binding = bindings.GetValue(0);
                binding.GetType().GetField("ShapeName").SetValue(binding, string.Empty);
                binding.GetType().GetField("MeshCaptured").SetValue(binding, false);
                hand.localRotation = Quaternion.Euler(45f, 0f, 0f);
                correction.ApplyPose();
                float expected = renderer.GetBlendShapeWeight(original.GetBlendShapeIndex(joint));
                Assert.That(expected, Is.GreaterThan(0f));

                original.ClearBlendShapes();
                correction.ApplyPose();
                AddShape(original, unrelated); AddShape(original, joint);
                // Unity retains native weights by index across layout changes.
                // Seed another owner's value; the joint must preserve it.
                renderer.SetBlendShapeWeight(original.GetBlendShapeIndex(unrelated), unrelatedWeight);
                correction.ApplyPose();
                Assert.That(renderer.GetBlendShapeWeight(original.GetBlendShapeIndex(joint)), Is.EqualTo(expected).Within(.0001f));
                Assert.That(renderer.GetBlendShapeWeight(original.GetBlendShapeIndex(unrelated)), Is.EqualTo(unrelatedWeight));

                renderer.sharedMesh = replacement;
                renderer.SetBlendShapeWeight(replacement.GetBlendShapeIndex(unrelated), unrelatedWeight);
                correction.ApplyPose();
                Assert.That(renderer.GetBlendShapeWeight(replacement.GetBlendShapeIndex(joint)), Is.EqualTo(expected).Within(.0001f));
                Assert.That(renderer.GetBlendShapeWeight(replacement.GetBlendShapeIndex(unrelated)), Is.EqualTo(unrelatedWeight));

                renderer.sharedMesh = original;
                renderer.SetBlendShapeWeight(original.GetBlendShapeIndex(unrelated), unrelatedWeight);
                hand.localRotation = Quaternion.identity;
                correction.ApplyPose();
                Assert.That(renderer.GetBlendShapeWeight(original.GetBlendShapeIndex(joint)), Is.Zero);
                Assert.That(renderer.GetBlendShapeWeight(original.GetBlendShapeIndex(unrelated)), Is.EqualTo(unrelatedWeight));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                root.SetActive(false);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(replacement);
            }
        }

        private static NpcHandPose.HandBinding CreateHand(Transform root, bool isLeft, Mesh mesh,
            out SkinnedMeshRenderer renderer)
        {
            var hand = new GameObject(isLeft ? "Hand.L" : "Hand.R").transform;
            hand.SetParent(root, false);
            hand.localPosition = Vector3.right * (isLeft ? -.15f : .15f);
            Transform Anchor(string name, Vector3 offset)
            {
                var anchor = new GameObject(name).transform;
                anchor.SetParent(hand, false); anchor.localPosition = offset;
                return anchor;
            }
            Transform socket = Anchor("Grip socket", Vector3.forward * .01f);
            Transform centre = Anchor("Cylinder centre", Vector3.zero);
            Transform axis = Anchor("Cylinder axis", Vector3.up * .1f);
            Transform palm = Anchor("Palm normal", Vector3.forward * .1f);
            var surface = new GameObject("Hand surface"); surface.transform.SetParent(hand, false);
            renderer = surface.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh; renderer.bones = new[] { hand }; renderer.rootBone = hand;
            return new NpcHandPose.HandBinding(isLeft, hand, socket, centre, axis, palm, new[] { renderer });
        }

        private static Mesh CreateMesh(string name, params string[] shapes)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { Vector3.zero, Vector3.right * .01f, Vector3.up * .01f };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.bindposes = new[] { Matrix4x4.identity };
            mesh.boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f } };
            foreach (string shape in shapes) AddShape(mesh, shape);
            return mesh;
        }

        private static void AddShape(Mesh mesh, string name)
            => mesh.AddBlendShapeFrame(name, 100f, new Vector3[mesh.vertexCount], null, null);

        private static void AssertWeights(SkinnedMeshRenderer renderer, float cylinder, float bottle)
        {
            Mesh mesh = renderer.sharedMesh;
            Assert.That(renderer.GetBlendShapeWeight(mesh.GetBlendShapeIndex(Cylinder)), Is.EqualTo(cylinder));
            Assert.That(renderer.GetBlendShapeWeight(mesh.GetBlendShapeIndex(Bottle)), Is.EqualTo(bottle));
        }
    }
}
