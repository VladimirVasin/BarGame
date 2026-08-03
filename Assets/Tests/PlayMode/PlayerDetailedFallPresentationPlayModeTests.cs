using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class PlayerDetailedFallPresentationPlayModeTests
    {
        private readonly List<GameObject> cleanupObjects =
            new List<GameObject>();
        private Light previousSun;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousSun = RenderSettings.sun;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RenderSettings.sun = previousSun;
            for (int index = cleanupObjects.Count - 1;
                 index >= 0;
                 index--)
            {
                if (cleanupObjects[index] != null)
                {
                    Object.Destroy(cleanupObjects[index]);
                }
            }

            cleanupObjects.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator FallAnimation_ReusesBodyRendererAndRestoresPuppet()
        {
            Camera camera = CreateCamera();
            GameObject actor = CreateObject("Detailed Fall Actor");
            Quaternion originalHeading =
                Quaternion.Euler(0f, 17f, 0f);
            actor.transform.rotation = originalHeading;

            GameObject rigObject = CreateObject("Detailed Fall Rig");
            rigObject.transform.SetParent(actor.transform, false);
            PlayerSpriteRig rig =
                rigObject.AddComponent<PlayerSpriteRig>();
            rig.Initialize(camera, actor.transform);
            yield return null;

            SpriteRenderer bodyRenderer = rig.BodyRenderer;
            Assert.That(rig.Renderers, Has.Count.EqualTo(9));
            Assert.That(
                rigObject.GetComponentsInChildren<SpriteRenderer>(true),
                Has.Length.EqualTo(9));

            rig.SetFallPose(-1f, 1f);
            rig.SetFallAnimation(
                PlayerFallAnimationPhase.Falling,
                0.5f);
            yield return null;

            Assert.That(rig.IsDetailedFallActive, Is.True);
            Assert.That(rig.DetailedFallFrameIndex, Is.EqualTo(7));
            Assert.That(rig.BodyRenderer, Is.SameAs(bodyRenderer));
            Assert.That(bodyRenderer.enabled, Is.True);
            Assert.That(bodyRenderer.flipX, Is.False);
            Assert.That(bodyRenderer.flipY, Is.False);
            Assert.That(
                bodyRenderer.sprite,
                Is.SameAs(
                    rig.GetDetailedFallSprite(
                        rig.CurrentDirection,
                        -1f,
                        7)));
            for (int partIndex = 1;
                 partIndex < PlayerSpriteRig.PartCount;
                 partIndex++)
            {
                Assert.That(
                    rig.Renderers[partIndex].enabled,
                    Is.False,
                    $"Puppet layer {partIndex} remained visible.");
            }

            Assert.That(
                actor.transform.rotation,
                Is.EqualTo(originalHeading));
            Assert.That(rig.PoseRoot.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(
                rig.PoseRoot.localRotation,
                Is.EqualTo(Quaternion.identity));

            Sprite leftFallSprite = bodyRenderer.sprite;
            rig.SetFallPose(1f, 1f);
            rig.SetFallAnimation(
                PlayerFallAnimationPhase.Down,
                0.5f);
            yield return null;

            Assert.That(rig.DetailedFallFrameIndex, Is.EqualTo(32));
            Assert.That(bodyRenderer.sprite, Is.Not.SameAs(leftFallSprite));
            Assert.That(
                bodyRenderer.sprite,
                Is.SameAs(
                    rig.GetDetailedFallSprite(
                        rig.CurrentDirection,
                        1f,
                        32)));
            Assert.That(bodyRenderer.flipX, Is.False);

            rig.SetFallPose(0f, 0f);
            rig.SetFallAnimation(PlayerFallAnimationPhase.None, 0f);
            yield return null;

            Assert.That(rig.IsDetailedFallActive, Is.False);
            for (int partIndex = 0;
                 partIndex < PlayerSpriteRig.PartCount;
                 partIndex++)
            {
                Assert.That(
                    rig.Renderers[partIndex].enabled,
                    Is.True,
                    $"Puppet layer {partIndex} was not restored.");
            }

            Assert.That(
                bodyRenderer.sprite,
                Is.SameAs(
                    rig.GetPartSprite(
                        PlayerPuppetPart.Body,
                        rig.CurrentDirection)));
            Assert.That(
                rigObject.GetComponentsInChildren<SpriteRenderer>(true),
                Has.Length.EqualTo(9));
        }

        [UnityTest]
        public IEnumerator DetailedFall_ShadowUsesOneAuthoredSilhouette()
        {
            Camera camera = CreateCamera();
            GameObject actor = CreateObject("Detailed Fall Shadow Actor");
            GameObject rigObject = CreateObject("Detailed Fall Shadow Rig");
            rigObject.transform.SetParent(actor.transform, false);
            PlayerSpriteRig rig =
                rigObject.AddComponent<PlayerSpriteRig>();
            rig.Initialize(camera, actor.transform);

            GameObject lightObject = CreateObject(
                "Detailed Fall Shadow Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Hard;
            lightObject.transform.rotation =
                Quaternion.Euler(48f, -34f, 0f);
            RenderSettings.sun = light;

            PlayerDynamicShadow shadow =
                actor.AddComponent<PlayerDynamicShadow>();
            shadow.Initialize(actor.transform, rig, light);
            yield return null;

            rig.SetFallPose(-1f, 1f);
            rig.SetFallAnimation(
                PlayerFallAnimationPhase.Down,
                0.25f);
            yield return null;

            Assert.That(shadow.Renderers, Has.Count.EqualTo(9));
            Assert.That(shadow.Renderer.enabled, Is.True);
            Assert.That(
                shadow.Renderer.shadowCastingMode,
                Is.EqualTo(ShadowCastingMode.ShadowsOnly));
            for (int partIndex = 1;
                 partIndex < PlayerSpriteRig.PartCount;
                 partIndex++)
            {
                Assert.That(shadow.Renderers[partIndex].enabled, Is.False);
            }

            Assert.That(
                shadow.Renderer.sprite,
                Is.SameAs(
                    rig.GetDetailedFallSprite(
                        shadow.CurrentDirection,
                        -1f,
                        rig.DetailedFallFrameIndex)));

            rig.SetFallPose(0f, 0f);
            rig.SetFallAnimation(PlayerFallAnimationPhase.None, 0f);
            yield return null;

            for (int partIndex = 0;
                 partIndex < PlayerSpriteRig.PartCount;
                 partIndex++)
            {
                Assert.That(shadow.Renderers[partIndex].enabled, Is.True);
            }
        }

        private Camera CreateCamera()
        {
            GameObject cameraObject = CreateObject(
                "Detailed Fall Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.transform.position = new Vector3(0f, 4f, 8f);
            cameraObject.transform.LookAt(Vector3.up);
            return camera;
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            cleanupObjects.Add(gameObject);
            return gameObject;
        }
    }
}
