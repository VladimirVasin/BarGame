using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class BarDrinkFirstPersonArmsPlayModeTests
    {
        private GameObject cameraObject;
        private GameObject ownerObject;
        private GameObject handleObject;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DestroyObject(ownerObject);
            DestroyObject(handleObject);
            DestroyObject(cameraObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BarArms_UsePlayer3DPartsAndReleaseLocalRig()
        {
            cameraObject = new GameObject("Bar Arms Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.1f;

            ownerObject = new GameObject("Bar Arms Test Owner");
            BarDrinkFirstPersonArms arms =
                ownerObject.AddComponent<BarDrinkFirstPersonArms>();
            arms.Initialize(camera);

            Assert.That(arms.IsInitialized, Is.True);
            Assert.That(arms.IsVisible, Is.False);
            Assert.That(arms.PresentationRoot, Is.Not.Null);
            Assert.That(
                arms.PresentationRoot.parent,
                Is.SameAs(camera.transform));
            Assert.That(arms.RightBottleGripAnchor, Is.Not.Null);
            Assert.That(arms.LeftVesselGripAnchor, Is.Not.Null);
            AssertModelDerivedArm(arms.RightModelRegistry, "Right");
            AssertModelDerivedArm(arms.LeftModelRegistry, "Left");
            Assert.That(
                arms.RightBottleGripAnchor,
                Is.SameAs(arms.RightModelRegistry.Anchors.RightGrip));
            Assert.That(
                arms.LeftVesselGripAnchor,
                Is.SameAs(arms.LeftModelRegistry.Anchors.LeftGrip));

            arms.ApplyPresentation(1f, 0f, 0f);
            yield return null;

            Assert.That(arms.IsVisible, Is.True);
            Vector3 rightRestPosition =
                arms.RightBottleGripAnchor.position;
            Vector3 leftRestPosition =
                arms.LeftVesselGripAnchor.position;

            arms.ApplyPresentation(1f, 1f, 1f);
            yield return null;

            Assert.That(
                Vector3.Distance(
                    rightRestPosition,
                    arms.RightBottleGripAnchor.position),
                Is.GreaterThan(0.02f));
            Assert.That(
                Vector3.Distance(
                    leftRestPosition,
                    arms.LeftVesselGripAnchor.position),
                Is.GreaterThan(0.02f));

            camera.transform.SetPositionAndRotation(
                new Vector3(2f, 1.5f, -3f),
                Quaternion.Euler(8f, 37f, 0f));
            yield return null;

            Assert.That(
                Vector3.Distance(
                    arms.PresentationRoot.position,
                    camera.transform.position),
                Is.LessThan(0.0001f));
            Assert.That(
                Quaternion.Angle(
                    arms.PresentationRoot.rotation,
                    camera.transform.rotation),
                Is.LessThan(0.01f));

            Transform firstRoot = arms.PresentationRoot;
            arms.Initialize(camera);
            Assert.That(firstRoot.gameObject.activeSelf, Is.False);
            Assert.That(arms.PresentationRoot, Is.Not.SameAs(firstRoot));
            yield return null;
            Assert.That(firstRoot == null, Is.True);

            Transform releasedRoot = arms.PresentationRoot;
            DestroyObject(ownerObject);
            ownerObject = null;
            yield return null;
            Assert.That(releasedRoot == null, Is.True);
        }

        [UnityTest]
        public IEnumerator RefrigeratorHand_UsesPlayer3DArmAndTracksHandle()
        {
            cameraObject = new GameObject("Refrigerator Hand Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.1f;

            handleObject = new GameObject("Refrigerator Handle");
            handleObject.transform.SetPositionAndRotation(
                new Vector3(0.22f, -0.03f, 0.68f),
                Quaternion.Euler(0f, 18f, 0f));

            ownerObject = new GameObject("Refrigerator Hand Test Owner");
            HomeRefrigeratorFirstPersonHand hand = ownerObject.AddComponent<
                HomeRefrigeratorFirstPersonHand>();
            hand.Initialize(camera, handleObject.transform);

            Assert.That(hand.IsInitialized, Is.True);
            Assert.That(hand.IsVisible, Is.False);
            Assert.That(hand.PresentationRoot.parent, Is.SameAs(camera.transform));
            Assert.That(hand.HandModelRoot, Is.Not.Null);
            AssertModelDerivedArm(hand.ModelRegistry, "Right");

            hand.ApplyReach(1f);
            yield return null;

            Assert.That(hand.IsVisible, Is.True);
            Vector3 initialPosition = hand.PresentationRoot.position;
            Vector3 expectedGrip =
                handleObject.transform.position +
                (camera.transform.position - handleObject.transform.position)
                    .normalized * 0.047f;
            Assert.That(
                Vector3.Distance(
                    hand.ModelRegistry.Anchors.RightGrip.position,
                    expectedGrip),
                Is.LessThan(0.002f));
            Assert.That(
                hand.PresentationRoot.localPosition.z,
                Is.GreaterThanOrEqualTo(camera.nearClipPlane + 0.079f));

            handleObject.transform.position +=
                new Vector3(-0.08f, 0.12f, 0.04f);
            yield return null;
            Assert.That(
                Vector3.Distance(initialPosition, hand.PresentationRoot.position),
                Is.GreaterThan(0.05f));

            hand.enabled = false;
            Assert.That(hand.IsVisible, Is.False);
            Assert.That(hand.ReachAmount, Is.Zero);

            Transform releasedRoot = hand.PresentationRoot;
            DestroyObject(ownerObject);
            ownerObject = null;
            yield return null;
            Assert.That(releasedRoot == null, Is.True);
        }

        private static void AssertModelDerivedArm(
            Player3DAssetRegistry registry,
            string expectedSide)
        {
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry.Animator, Is.Not.Null);
            Assert.That(registry.Animator.enabled, Is.False);

            int enabledRendererCount = 0;
            bool hasUpperArm = false;
            bool hasForearm = false;
            bool hasHand = false;
            for (int index = 0; index < registry.MeshBindings.Count; index++)
            {
                Player3DMeshBinding binding = registry.MeshBindings[index];
                if (binding == null ||
                    binding.Renderer == null ||
                    !binding.Renderer.enabled)
                {
                    continue;
                }

                enabledRendererCount++;
                Assert.That(binding.AnatomicalSide, Is.EqualTo(expectedSide));
                Assert.That(
                    binding.BoneName,
                    Does.Match(@"^(upper_arm|forearm|hand)\.[LR]$"));
                Material material = binding.Renderer.sharedMaterial;
                Assert.That(material, Is.Not.Null);
                Assert.That(
                    material,
                    Is.Not.SameAs(RuntimePrimitiveFactory.DefaultMaterial));
                Assert.That(material.shader, Is.Not.Null);
                Assert.That(
                    material.shader.name,
                    Is.EqualTo("Bar Promenade/PS1 Lit"));
                Assert.That(
                    binding.Renderer.shadowCastingMode,
                    Is.EqualTo(ShadowCastingMode.Off));
                Assert.That(binding.Renderer.receiveShadows, Is.False);
                hasUpperArm |= binding.MeshName.StartsWith("GEO_UpperArm");
                hasForearm |= binding.MeshName.StartsWith("GEO_Forearm");
                hasHand |= binding.MeshName.StartsWith("GEO_Hand");
            }

            Assert.That(enabledRendererCount, Is.GreaterThanOrEqualTo(6));
            Assert.That(hasUpperArm, Is.True);
            Assert.That(hasForearm, Is.True);
            Assert.That(hasHand, Is.True);

            Renderer[] hierarchyRenderers =
                registry.GetComponentsInChildren<Renderer>(true);
            int hierarchyEnabledCount = 0;
            for (int index = 0; index < hierarchyRenderers.Length; index++)
            {
                if (hierarchyRenderers[index].enabled)
                {
                    hierarchyEnabledCount++;
                }
            }
            Assert.That(hierarchyEnabledCount, Is.EqualTo(enabledRendererCount));

            Collider[] colliders =
                registry.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                Assert.That(colliders[index].enabled, Is.False);
            }

            Light[] lights = registry.GetComponentsInChildren<Light>(true);
            for (int index = 0; index < lights.Length; index++)
            {
                Assert.That(lights[index].enabled, Is.False);
            }
        }

        private static void DestroyObject(Object value)
        {
            if (value != null)
            {
                Object.Destroy(value);
            }
        }
    }
}
