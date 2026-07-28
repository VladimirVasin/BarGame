using System.Collections;
using System.Reflection;
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
                shadow.Renderers,
                Has.Count.EqualTo(PlayerSpriteRig.PartCount));
            Assert.That(
                shadow.DirectionSprites,
                Has.Count.EqualTo(PlayerSpriteRig.DirectionCount));
            Assert.That(
                playerObject.GetComponentsInChildren<SpriteRenderer>(true),
                Has.Length.EqualTo(PlayerSpriteRig.PartCount * 2));
            Assert.That(
                shadow.ShadowRoot.GetComponentsInChildren<Collider>(true),
                Is.Empty);

            for (int partIndex = 0;
                 partIndex < shadow.Renderers.Count;
                 partIndex++)
            {
                SpriteRenderer renderer =
                    shadow.Renderers[partIndex];
                Assert.That(renderer, Is.Not.Null);
                Assert.That(
                    renderer.shadowCastingMode,
                    Is.EqualTo(ShadowCastingMode.ShadowsOnly));
                Assert.That(renderer.receiveShadows, Is.False);
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(
                        PlayerShadowResources.ShadowCasterMaterial));
            }

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
            AssertAllShadowPartsEnabled(shadow, false);

            light.shadows = LightShadows.Hard;
            yield return null;
            AssertAllShadowPartsEnabled(shadow, true);

            visual.enabled = false;
            yield return null;
            Vector3 originalShadowPosition =
                shadow.ShadowRoot.position;
            Vector3 actorDelta = new Vector3(1.75f, 0f, -2.25f);
            playerObject.transform.position += actorDelta;
            yield return null;

            Assert.That(
                Vector3.Distance(
                    shadow.ShadowRoot.position,
                    originalShadowPosition + actorDelta),
                Is.LessThan(0.001f),
                "Directional shadow must follow actor movement.");
            AssertFacesLight(shadow.ShadowRoot, light);
        }

        [UnityTest]
        public IEnumerator DirectionalShadow_MirrorsTheArticulatedGait()
        {
            PlayerDynamicShadow shadow =
                playerObject.GetComponent<PlayerDynamicShadow>();
            PlayerSpriteRig visual =
                playerObject.GetComponentInChildren<PlayerSpriteRig>();
            FieldInfo phaseField = typeof(PlayerSpriteRig).GetField(
                "animationPhase",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo animateMethod =
                typeof(PlayerSpriteRig).GetMethod(
                    "AnimatePuppet",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo refreshMethod =
                typeof(PlayerDynamicShadow).GetMethod(
                    "RefreshShadow",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(phaseField, Is.Not.Null);
            Assert.That(animateMethod, Is.Not.Null);
            Assert.That(refreshMethod, Is.Not.Null);

            visual.enabled = false;
            visual.SetMotion(Vector3.forward * 5.2f);
            Quaternion firstLegRotation = SampleShadowPose(
                visual,
                shadow,
                phaseField,
                animateMethod,
                refreshMethod,
                Mathf.PI * 0.5f);
            Quaternion oppositeLegRotation = SampleShadowPose(
                visual,
                shadow,
                phaseField,
                animateMethod,
                refreshMethod,
                Mathf.PI * 1.5f);

            Assert.That(
                Quaternion.Angle(
                    firstLegRotation,
                    oppositeLegRotation),
                Is.GreaterThan(35f),
                "The directional silhouette must reproduce the " +
                "walking leg swing instead of remaining a static card.");
            for (int partIndex = 0;
                 partIndex < PlayerSpriteRig.PartCount;
                 partIndex++)
            {
                PlayerPuppetPart part =
                    (PlayerPuppetPart)partIndex;
                Assert.That(
                    shadow.GetPartRenderer(part).sprite,
                    Is.SameAs(
                        visual.GetPartSprite(
                            part,
                            shadow.CurrentDirection)));
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Factory_CreatesPoseIndependentGroundContactShadow()
        {
            PlayerSpriteRig visual =
                playerObject.GetComponentInChildren<PlayerSpriteRig>();
            PlayerContactShadow contactShadow =
                playerObject.GetComponent<PlayerContactShadow>();
            Light light = lightObject.GetComponent<Light>();

            Assert.That(
                visual.transform.localPosition.y,
                Is.EqualTo(0.005f).Within(0.0001f));
            Assert.That(contactShadow, Is.Not.Null);
            Assert.That(contactShadow.IsInitialized, Is.True);
            Assert.That(contactShadow.ShadowRoot, Is.Not.Null);
            Assert.That(
                contactShadow.ShadowRoot.name,
                Is.EqualTo("Player Ground Contact Shadow"));
            Assert.That(
                contactShadow.ShadowRoot.parent,
                Is.SameAs(playerObject.transform));
            Assert.That(
                contactShadow.ShadowRoot.localPosition,
                Is.EqualTo(
                    new Vector3(
                        0f,
                        PlayerContactShadow.GroundOffset,
                        0f)));
            Assert.That(
                contactShadow.ShadowRoot.localScale.x,
                Is.EqualTo(PlayerContactShadow.BaseWidth)
                    .Within(0.0001f));
            Assert.That(
                contactShadow.ShadowRoot.localScale.z,
                Is.EqualTo(PlayerContactShadow.BaseDepth)
                    .Within(0.0001f));
            Assert.That(
                contactShadow.Filter.sharedMesh,
                Is.SameAs(PlayerShadowResources.ContactShadowMesh));
            Assert.That(
                contactShadow.Renderer.sharedMaterial,
                Is.SameAs(
                    PlayerShadowResources.ContactShadowMaterial));
            Assert.That(
                contactShadow.Renderer.shadowCastingMode,
                Is.EqualTo(ShadowCastingMode.Off));
            Assert.That(
                contactShadow.Renderer.receiveShadows,
                Is.False);
            Assert.That(
                contactShadow.Renderer.lightProbeUsage,
                Is.EqualTo(LightProbeUsage.Off));
            Assert.That(
                contactShadow.Renderer.reflectionProbeUsage,
                Is.EqualTo(ReflectionProbeUsage.Off));
            Assert.That(
                contactShadow.Renderer.motionVectorGenerationMode,
                Is.EqualTo(
                    MotionVectorGenerationMode.ForceNoMotion));
            Assert.That(
                contactShadow.ShadowRoot
                    .GetComponentsInChildren<Collider>(true),
                Is.Empty);

            visual.enabled = false;
            Vector3 originalContactPosition =
                contactShadow.ShadowRoot.position;
            visual.PoseRoot.localPosition =
                new Vector3(0.08f, 0.16f, 0f);
            visual.PoseRoot.localRotation =
                Quaternion.Euler(0f, 0f, 7f);
            yield return null;

            Assert.That(
                Vector3.Distance(
                    contactShadow.ShadowRoot.position,
                    originalContactPosition),
                Is.LessThan(0.0001f),
                "Contact shadow must ignore puppet bob and sway.");

            light.shadows = LightShadows.None;
            yield return null;
            Assert.That(contactShadow.Renderer.enabled, Is.True);

            Vector3 actorDelta = new Vector3(2f, 0f, -3f);
            playerObject.transform.position += actorDelta;
            yield return null;
            Assert.That(
                Vector3.Distance(
                    contactShadow.ShadowRoot.position,
                    originalContactPosition + actorDelta),
                Is.LessThan(0.0001f),
                "Contact shadow must follow the grounded actor root.");
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

        private static Quaternion SampleShadowPose(
            PlayerSpriteRig visual,
            PlayerDynamicShadow shadow,
            FieldInfo phaseField,
            MethodInfo animateMethod,
            MethodInfo refreshMethod,
            float targetPhase)
        {
            const float sampleDeltaTime = 1f;
            float phaseAdvance =
                5.2f /
                2.7f *
                Mathf.PI *
                2f *
                sampleDeltaTime;
            phaseField.SetValue(
                visual,
                targetPhase - phaseAdvance);
            animateMethod.Invoke(
                visual,
                new object[] { sampleDeltaTime });
            refreshMethod.Invoke(shadow, null);
            return shadow.GetPartTransform(
                PlayerPuppetPart.LeftUpperLeg).localRotation;
        }

        private static void AssertAllShadowPartsEnabled(
            PlayerDynamicShadow shadow,
            bool expected)
        {
            for (int index = 0;
                 index < shadow.Renderers.Count;
                 index++)
            {
                Assert.That(
                    shadow.Renderers[index].enabled,
                    Is.EqualTo(expected));
            }
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
