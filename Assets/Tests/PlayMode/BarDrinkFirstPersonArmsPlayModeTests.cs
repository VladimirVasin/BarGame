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

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DestroyObject(ownerObject);
            DestroyObject(cameraObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Presentation_BuildsAndAnimatesSharedMaterialArms()
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

            arms.ApplyPresentation(1f, 0f, 0f);
            yield return null;

            Assert.That(arms.IsVisible, Is.True);
            Vector3 rightRestPosition =
                arms.RightBottleGripAnchor.position;
            Vector3 leftRestPosition =
                arms.LeftVesselGripAnchor.position;

            Renderer[] renderers =
                arms.PresentationRoot.GetComponentsInChildren<Renderer>();
            Assert.That(renderers.Length, Is.GreaterThan(20));
            for (int index = 0; index < renderers.Length; index++)
            {
                Assert.That(
                    renderers[index].sharedMaterial,
                    Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial));
                Assert.That(
                    renderers[index].shadowCastingMode,
                    Is.EqualTo(ShadowCastingMode.Off));
                Assert.That(renderers[index].receiveShadows, Is.False);
            }

            Assert.That(
                arms.PresentationRoot
                    .GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                arms.PresentationRoot
                    .GetComponentsInChildren<Light>(true),
                Is.Empty);

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

            arms.ResetPresentation();
            Assert.That(arms.IsVisible, Is.False);
            Assert.That(arms.VisibilityAmount, Is.Zero);
            Assert.That(arms.RightGripAmount, Is.Zero);
            Assert.That(arms.DrinkLiftAmount, Is.Zero);
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
