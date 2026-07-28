using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class PlayerDynamicShadowPlayModeTests
    {
        private Light previousSun;
        private GameObject cameraObject;
        private GameObject lightObject;
        private GameObject playerObject;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousSun = RenderSettings.sun;

            cameraObject = new GameObject("Shadow Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.transform.position = new Vector3(0f, 5f, 8f);
            cameraObject.transform.LookAt(Vector3.up);

            lightObject = new GameObject("Shadow Test Main Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Hard;
            lightObject.transform.rotation =
                Quaternion.Euler(48f, -34f, 0f);
            RenderSettings.sun = light;

            PlayerRuntime player = PlayerFactory.Create(
                null,
                new Vector3(0f, 0.12f, 0f),
                camera,
                null,
                null);
            playerObject = player.GameObject;
            player.Motor.enabled = false;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RenderSettings.sun = previousSun;
            DestroyObject(playerObject);
            DestroyObject(lightObject);
            DestroyObject(cameraObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Factory_CreatesStableLightFacingShadowCaster()
        {
            PlayerDynamicShadow shadow =
                playerObject.GetComponent<PlayerDynamicShadow>();
            PlayerSpriteRig visual =
                playerObject.GetComponentInChildren<PlayerSpriteRig>();
            Light light = lightObject.GetComponent<Light>();

            Assert.That(shadow, Is.Not.Null);
            Assert.That(shadow.IsInitialized, Is.True);
            Assert.That(shadow.MainLight, Is.SameAs(light));
            Assert.That(shadow.ShadowRoot, Is.Not.Null);
            Assert.That(
                shadow.ShadowRoot.name,
                Is.EqualTo("Dynamic Player Shadow Caster"));
            Assert.That(shadow.Renderer, Is.Not.Null);
            Assert.That(
                shadow.Renderer.shadowCastingMode,
                Is.EqualTo(ShadowCastingMode.ShadowsOnly));
            Assert.That(shadow.Renderer.receiveShadows, Is.False);
            Assert.That(
                shadow.Renderer.lightProbeUsage,
                Is.EqualTo(LightProbeUsage.Off));
            Assert.That(
                shadow.Renderer.reflectionProbeUsage,
                Is.EqualTo(ReflectionProbeUsage.Off));
            Assert.That(
                shadow.Renderer.sharedMaterial,
                Is.SameAs(
                    PlayerShadowResources.ShadowCasterMaterial));
            Assert.That(
                shadow.DirectionSprites,
                Has.Count.EqualTo(PlayerSpriteRig.DirectionCount));
            Assert.That(
                playerObject.GetComponentsInChildren<SpriteRenderer>(true),
                Has.Length.EqualTo(PlayerSpriteRig.PartCount + 1));
            Assert.That(
                shadow.ShadowRoot.GetComponentsInChildren<Collider>(true),
                Is.Empty);

            for (int index = 0;
                 index < shadow.DirectionSprites.Count;
                 index++)
            {
                Sprite sprite = shadow.DirectionSprites[index];
                Assert.That(sprite, Is.Not.Null);
                Assert.That(
                    sprite.rect.x,
                    Is.EqualTo(
                        index * PlayerSpriteRig.FrameWidth));
                Assert.That(
                    sprite.pixelsPerUnit,
                    Is.EqualTo(PlayerSpriteRig.PixelsPerUnit));
            }

            Assert.That(
                shadow.Renderer.sprite,
                Is.SameAs(
                    shadow.DirectionSprites[
                        (int)shadow.CurrentDirection]));
            AssertFacesLight(shadow.ShadowRoot, light);
            Assert.That(
                shadow.ShadowRoot.position.y,
                Is.EqualTo(
                    visual.transform.position.y +
                    visual.PoseRoot.localPosition.y)
                    .Within(0.001f));

            Vector3 originalForward = shadow.ShadowRoot.forward;
            cameraObject.transform.position =
                new Vector3(8f, 5f, 0f);
            cameraObject.transform.LookAt(Vector3.up);
            yield return null;

            Assert.That(
                Vector3.Angle(
                    originalForward,
                    shadow.ShadowRoot.forward),
                Is.LessThan(0.01f));

            lightObject.transform.rotation =
                Quaternion.Euler(48f, 56f, 0f);
            yield return null;
            AssertFacesLight(shadow.ShadowRoot, light);

            light.shadows = LightShadows.None;
            yield return null;
            Assert.That(shadow.Renderer.enabled, Is.False);

            light.shadows = LightShadows.Hard;
            yield return null;
            Assert.That(shadow.Renderer.enabled, Is.True);
        }

        private static void AssertFacesLight(
            Transform shadowTransform,
            Light light)
        {
            Vector3 expected = Vector3.ProjectOnPlane(
                -light.transform.forward,
                Vector3.up).normalized;
            Vector3 actual = Vector3.ProjectOnPlane(
                shadowTransform.forward,
                Vector3.up).normalized;
            Assert.That(
                Vector3.Dot(expected, actual),
                Is.GreaterThan(0.9999f));
        }

        private static void DestroyObject(GameObject value)
        {
            if (value != null)
            {
                Object.Destroy(value);
            }
        }
    }
}
