using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class Player3DGameplaySceneIntegrationPlayModeTests
    {
        [TestCase("City", false, RuntimeSceneSetup.CityFarClipPlane)]
        [TestCase("Bar", true, RuntimeSceneSetup.DefaultFarClipPlane)]
        [TestCase("Supermarket", true, RuntimeSceneSetup.DefaultFarClipPlane)]
        [TestCase("Home", true, RuntimeSceneSetup.DefaultFarClipPlane)]
        [TestCase("Stairwell", true, RuntimeSceneSetup.DefaultFarClipPlane)]
        public void GameplaySceneProfileFramesAndLightsContinuousMeshPlayer(
            string profile,
            bool interior,
            float expectedFarClipPlane)
        {
            Light previousSun = RenderSettings.sun;
            bool previousFog = RenderSettings.fog;
            Color previousFogColor = RenderSettings.fogColor;
            FogMode previousFogMode = RenderSettings.fogMode;
            float previousFogDensity = RenderSettings.fogDensity;
            AmbientMode previousAmbientMode = RenderSettings.ambientMode;
            Color previousAmbientLight = RenderSettings.ambientLight;
            float previousReflectionIntensity =
                RenderSettings.reflectionIntensity;

            GameObject cameraObject = null;
            GameObject lightObject = null;
            GameObject playerObject = null;
            GameObject occlusionRoot = null;
            try
            {
                cameraObject = new GameObject(
                    $"{profile} Player3D Contract Camera");
                cameraObject.tag = "MainCamera";
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                UniversalAdditionalCameraData cameraData =
                    camera.GetUniversalAdditionalCameraData();
                cameraData.renderShadows = false;
                cameraData.requiresDepthTexture = false;

                lightObject = new GameObject(
                    $"{profile} Player3D Contract Light");
                Light directional = lightObject.AddComponent<Light>();
                directional.type = LightType.Directional;
                directional.enabled = false;
                directional.cullingMask = 0;
                directional.shadowBias = 1f;
                directional.shadowNormalBias = 1f;
                directional.shadowNearPlane = 1f;
                RenderSettings.sun = directional;

                Camera configuredCamera = Configure(profile);
                Assert.That(configuredCamera, Is.SameAs(camera));
                Assert.That(configuredCamera.orthographic, Is.False);
                Assert.That(
                    configuredCamera.nearClipPlane,
                    Is.EqualTo(RuntimeSceneSetup.GameplayNearClipPlane)
                        .Within(0.0001f));
                Assert.That(
                    configuredCamera.farClipPlane,
                    Is.EqualTo(expectedFarClipPlane).Within(0.0001f));
                Assert.That(cameraData.renderShadows, Is.True);
                Assert.That(cameraData.requiresDepthTexture, Is.True);

                Assert.That(RenderSettings.sun, Is.SameAs(directional));
                Assert.That(directional.isActiveAndEnabled, Is.True);
                Assert.That(directional.type, Is.EqualTo(LightType.Directional));
                Assert.That(directional.shadows, Is.EqualTo(LightShadows.Hard));
                Assert.That(directional.shadowStrength, Is.GreaterThan(0f));
                Assert.That(directional.cullingMask, Is.EqualTo(~0));
                Assert.That(
                    directional.shadowBias,
                    Is.EqualTo(RuntimeSceneSetup.PlayerMeshShadowBias)
                        .Within(0.0001f));
                Assert.That(
                    directional.shadowNormalBias,
                    Is.EqualTo(RuntimeSceneSetup.PlayerMeshShadowNormalBias)
                        .Within(0.0001f));
                Assert.That(
                    directional.shadowNearPlane,
                    Is.EqualTo(RuntimeSceneSetup.PlayerMeshShadowNearPlane)
                        .Within(0.0001f));

                PlayerRuntime player = PlayerFactory.Create(
                    null,
                    Vector3.up * PlayerFactory.GroundedRootOffset,
                    configuredCamera,
                    null,
                    null);
                playerObject = player.GameObject;
                player.Motor.enabled = false;

                PlayerCameraFollow follow =
                    configuredCamera.gameObject.AddComponent<
                        PlayerCameraFollow>();
                follow.Initialize(
                    configuredCamera,
                    player.GameObject.transform,
                    interior);

                Assert.That(player.Visual, Is.Not.Null);
                Assert.That(
                    player.GameObject.GetComponentsInChildren<
                        SpriteRenderer>(true),
                    Is.Empty);

                IReadOnlyList<Renderer> renderers =
                    player.Visual.Renderers;
                Assert.That(
                    renderers.Count,
                    Is.GreaterThanOrEqualTo(16));
                Bounds playerBounds = CombineBounds(renderers);
                for (int index = 0; index < renderers.Count; index++)
                {
                    Renderer renderer = renderers[index];
                    Assert.That(renderer, Is.Not.Null);
                    Assert.That(
                        renderer is MeshRenderer ||
                        renderer is SkinnedMeshRenderer,
                        Is.True,
                        $"'{renderer.name}' must be backed by a 3D mesh renderer.");
                    Assert.That(renderer.enabled, Is.True);
                    Assert.That(
                        renderer.shadowCastingMode,
                        Is.EqualTo(ShadowCastingMode.On));
                    Assert.That(renderer.receiveShadows, Is.True);
                }

                AssertPlayerFitsCamera(configuredCamera, playerBounds);

                Assert.That(player.ContactShadow, Is.Not.Null);
                Assert.That(player.ContactShadow.IsInitialized, Is.True);
                Assert.That(player.ContactShadow.Renderer, Is.Not.Null);
                Assert.That(
                    player.ContactShadow.Renderer.shadowCastingMode,
                    Is.EqualTo(ShadowCastingMode.Off));

                if (string.Equals(
                        profile,
                        "Home",
                        StringComparison.Ordinal))
                {
                    occlusionRoot = VerifyHomeOcclusionUsesMeshBounds(
                        configuredCamera,
                        player,
                        playerBounds);
                }
            }
            finally
            {
                DestroyImmediate(occlusionRoot);
                DestroyImmediate(playerObject);
                DestroyImmediate(cameraObject);
                DestroyImmediate(lightObject);

                RenderSettings.sun = previousSun;
                RenderSettings.fog = previousFog;
                RenderSettings.fogColor = previousFogColor;
                RenderSettings.fogMode = previousFogMode;
                RenderSettings.fogDensity = previousFogDensity;
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbientLight;
                RenderSettings.reflectionIntensity =
                    previousReflectionIntensity;
            }
        }

        private static Camera Configure(string profile)
        {
            switch (profile)
            {
                case "City":
                    return RuntimeSceneSetup.EnsureCityNight();
                case "Bar":
                    return RuntimeSceneSetup.EnsureBarInterior();
                case "Supermarket":
                    return RuntimeSceneSetup.EnsureSupermarketInterior();
                case "Home":
                    return RuntimeSceneSetup.EnsureHomeInterior();
                case "Stairwell":
                    return RuntimeSceneSetup.EnsureStairwellInterior();
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(profile),
                        profile,
                        "Unknown gameplay scene profile.");
            }
        }

        private static Bounds CombineBounds(
            IReadOnlyList<Renderer> renderers)
        {
            bool hasBounds = false;
            Bounds combined = default;
            for (int index = 0; index < renderers.Count; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    combined = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(renderer.bounds);
                }
            }

            Assert.That(hasBounds, Is.True);
            return combined;
        }

        private static void AssertPlayerFitsCamera(
            Camera camera,
            Bounds playerBounds)
        {
            Vector3 feet = new Vector3(
                playerBounds.center.x,
                playerBounds.min.y,
                playerBounds.center.z);
            Vector3 head = new Vector3(
                playerBounds.center.x,
                playerBounds.max.y,
                playerBounds.center.z);
            AssertAnchorFitsCamera(camera, feet, "feet");
            AssertAnchorFitsCamera(camera, head, "head");
        }

        private static void AssertAnchorFitsCamera(
            Camera camera,
            Vector3 worldPosition,
            string anchorName)
        {
            Vector3 viewport = camera.WorldToViewportPoint(worldPosition);
            Assert.That(
                viewport.z,
                Is.GreaterThan(camera.nearClipPlane + 0.01f),
                $"Player {anchorName} must be beyond the near plane.");
            Assert.That(
                viewport.z,
                Is.LessThan(camera.farClipPlane),
                $"Player {anchorName} must be before the far plane.");
            Assert.That(
                viewport.x,
                Is.InRange(-0.02f, 1.02f),
                $"Player {anchorName} must remain horizontally framed.");
            Assert.That(
                viewport.y,
                Is.InRange(-0.02f, 1.02f),
                $"Player {anchorName} must remain vertically framed.");
        }

        private static GameObject VerifyHomeOcclusionUsesMeshBounds(
            Camera camera,
            PlayerRuntime player,
            Bounds playerBounds)
        {
            GameObject root = new GameObject(
                "Home Player3D Occlusion Contract");
            GameObject blocker = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            blocker.name = "Home Player3D Foreground Blocker";
            blocker.transform.SetParent(root.transform, false);
            blocker.transform.position = Vector3.Lerp(
                camera.transform.position,
                playerBounds.center,
                0.5f);
            blocker.transform.localScale =
                new Vector3(1.6f, 1.8f, 0.5f);
            Renderer blockerRenderer = blocker.GetComponent<Renderer>();
            blockerRenderer.sharedMaterial =
                RuntimePrimitiveFactory.DefaultMaterial;

            HomeOcclusionRegistry registry =
                root.AddComponent<HomeOcclusionRegistry>();
            HomeOccluderGroup group = registry.Register(
                "player-3d-contract",
                HomeOccluderKind.FurnitureBlocker,
                0.25f,
                blockerRenderer);
            HomePlayerOcclusionController controller =
                root.AddComponent<HomePlayerOcclusionController>();
            camera.enabled = true;
            controller.Initialize(
                camera,
                player.Visual.Renderers,
                registry);
            controller.ReevaluateImmediately();

            Assert.That(
                controller.GetVisibility(group),
                Is.EqualTo(group.MinimumVisibility).Within(0.0001f));
            for (int index = 0;
                 index < player.Visual.Renderers.Count;
                 index++)
            {
                Renderer renderer = player.Visual.Renderers[index];
                Assert.That(renderer.enabled, Is.True);
                Assert.That(
                    renderer.shadowCastingMode,
                    Is.EqualTo(ShadowCastingMode.On));
            }

            return root;
        }

        private static void DestroyImmediate(GameObject target)
        {
            if (target != null)
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
