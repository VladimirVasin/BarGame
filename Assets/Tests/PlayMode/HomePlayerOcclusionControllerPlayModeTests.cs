using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomePlayerOcclusionControllerPlayModeTests
    {
        private const float VisibilityTolerance = 0.002f;
        private const float TimeoutSeconds = 2f;

        private readonly List<GameObject> cleanupObjects =
            new List<GameObject>();
        private Camera gpuCamera;
        private RenderTexture previousCameraTarget;
        private RenderTexture gpuTarget;
        private Texture2D fullVisibilityReadback;
        private Texture2D ditheredReadback;
        private bool renderSettingsCaptured;
        private bool previousFog;
        private AmbientMode previousAmbientMode;
        private Color previousAmbientLight;

        private GameObject listenerObject;

        /// <summary>
        /// These tests build a synthetic scene and end on
        /// `LogAssert.NoUnexpectedReceived()`, which makes them sensitive to
        /// an engine notice that has nothing to do with them: a scene test
        /// running before this one can leave music playing, and the moment
        /// its scene unloads Unity logs "There are no audio listeners in the
        /// scene". Standing one up before the body runs removes the coupling
        /// rather than deafening the assertion.
        /// </summary>
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (Object.FindAnyObjectByType<AudioListener>() == null)
            {
                listenerObject =
                    new GameObject("Synthetic Audio Listener");
                listenerObject.AddComponent<AudioListener>();
            }

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (listenerObject != null)
            {
                Object.Destroy(listenerObject);
                listenerObject = null;
            }

            if (gpuCamera != null)
            {
                gpuCamera.targetTexture = previousCameraTarget;
            }

            if (renderSettingsCaptured)
            {
                RenderSettings.fog = previousFog;
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbientLight;
                renderSettingsCaptured = false;
            }

            if (gpuTarget != null)
            {
                gpuTarget.Release();
                Object.Destroy(gpuTarget);
                gpuTarget = null;
            }

            if (fullVisibilityReadback != null)
            {
                Object.Destroy(fullVisibilityReadback);
                fullVisibilityReadback = null;
            }

            if (ditheredReadback != null)
            {
                Object.Destroy(ditheredReadback);
                ditheredReadback = null;
            }

            gpuCamera = null;
            previousCameraTarget = null;
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
        public IEnumerator
            SyntheticScene_RevealsOnlyFrontGroupsAndPreservesRendererState()
        {
            Camera camera = CreateCamera();
            Renderer player = CreatePrimitive(
                    "Synthetic Player",
                    new Vector3(0f, 1f, 0f),
                    new Vector3(1f, 2f, 0.20f))
                .GetComponent<Renderer>();

            GameObject frontRoot = CreateObject("Front Group");
            Renderer frontA = CreateChildPrimitive(
                    frontRoot,
                    "Front A",
                    new Vector3(0f, 1f, -5f),
                    new Vector3(1.2f, 2.2f, 0.4f))
                .GetComponent<Renderer>();
            Renderer frontB = CreateChildPrimitive(
                    frontRoot,
                    "Front B",
                    new Vector3(0f, 1f, -4.45f),
                    new Vector3(0.6f, 1.4f, 0.3f))
                .GetComponent<Renderer>();
            Renderer side = CreatePrimitive(
                    "Side Blocker",
                    new Vector3(4f, 1f, -5f),
                    new Vector3(1f, 2f, 0.5f))
                .GetComponent<Renderer>();
            Renderer behind = CreatePrimitive(
                    "Behind Blocker",
                    new Vector3(0f, 1f, 2f),
                    new Vector3(1f, 2f, 0.5f))
                .GetComponent<Renderer>();

            Material originalMaterial =
                RuntimePrimitiveFactory.DefaultMaterial;
            Renderer[] occluderRenderers =
            {
                frontA,
                frontB,
                side,
                behind
            };
            for (int index = 0;
                 index < occluderRenderers.Length;
                 index++)
            {
                occluderRenderers[index].sharedMaterial =
                    originalMaterial;
            }

            int colorProperty =
                Shader.PropertyToID("_BaseColor");
            var markerColor =
                new Color(0.17f, 0.43f, 0.29f, 1f);
            var initialProperties =
                new MaterialPropertyBlock();
            initialProperties.SetColor(
                colorProperty,
                markerColor);
            frontA.SetPropertyBlock(initialProperties);

            HomeOcclusionRegistry registry =
                CreateObject("Synthetic Registry")
                    .AddComponent<HomeOcclusionRegistry>();
            HomeOccluderGroup frontGroup = registry.Register(
                "front",
                HomeOccluderKind.FurnitureBlocker,
                0.23f,
                frontA,
                frontB);
            HomeOccluderGroup sideGroup = registry.Register(
                "side",
                HomeOccluderKind.FurnitureBlocker,
                0.25f,
                side);
            HomeOccluderGroup behindGroup = registry.Register(
                "behind",
                HomeOccluderKind.StructuralCutaway,
                0.18f,
                behind);

            Collider[] colliders =
            {
                frontA.GetComponent<Collider>(),
                frontB.GetComponent<Collider>(),
                side.GetComponent<Collider>(),
                behind.GetComponent<Collider>()
            };
            GameObject controllerObject =
                CreateObject("Synthetic Occlusion Controller");
            HomePlayerOcclusionController controller =
                controllerObject.AddComponent<
                    HomePlayerOcclusionController>();

            controller.Initialize(
                camera,
                new[] { player },
                registry);

            Assert.That(controller.IsInitialized, Is.True);
            AssertVisibility(
                controller,
                frontGroup,
                frontGroup.MinimumVisibility);
            AssertVisibility(controller, sideGroup, 1f);
            AssertVisibility(controller, behindGroup, 1f);
            AssertGroupProperty(
                frontGroup,
                frontGroup.MinimumVisibility);
            AssertGroupProperty(sideGroup, 1f);
            AssertGroupProperty(behindGroup, 1f);
            AssertSharedOcclusionMaterial(occluderRenderers);
            AssertCollidersUnchanged(colliders);
            AssertPropertyColor(
                frontA,
                colorProperty,
                markerColor);

            frontRoot.transform.position = Vector3.right * 4f;
            yield return WaitForVisibility(
                controller,
                frontGroup,
                1f,
                occluderRenderers,
                colliders);
            AssertGroupProperty(frontGroup, 1f);
            AssertPropertyColor(
                frontA,
                colorProperty,
                markerColor);

            frontRoot.transform.position = Vector3.zero;
            yield return WaitForVisibility(
                controller,
                frontGroup,
                frontGroup.MinimumVisibility,
                occluderRenderers,
                colliders);
            AssertGroupProperty(
                frontGroup,
                frontGroup.MinimumVisibility);

            controller.ClearOcclusion();
            AssertVisibility(controller, frontGroup, 1f);
            AssertGroupProperty(frontGroup, 1f);
            AssertSharedOcclusionMaterial(occluderRenderers);
            AssertCollidersUnchanged(colliders);

            controller.ReevaluateImmediately();
            AssertVisibility(
                controller,
                frontGroup,
                frontGroup.MinimumVisibility);
            controller.enabled = false;
            AssertVisibility(controller, frontGroup, 1f);
            AssertGroupProperty(frontGroup, 1f);
            AssertSharedOcclusionMaterial(occluderRenderers);
            AssertCollidersUnchanged(colliders);

            Object.Destroy(controller);
            yield return null;
            for (int index = 0;
                 index < occluderRenderers.Length;
                 index++)
            {
                Assert.That(
                    occluderRenderers[index].sharedMaterial,
                    Is.SameAs(originalMaterial),
                    "Destroying the controller must restore the pre-existing shared material.");
            }

            AssertPropertyColor(
                frontA,
                colorProperty,
                markerColor);
            AssertCollidersUnchanged(colliders);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator
            DitherShader_ReducesCoverageWithoutErasingOccluder()
        {
            if (SystemInfo.graphicsDeviceType ==
                GraphicsDeviceType.Null)
            {
                Assert.Ignore(
                    "Home dither coverage requires a graphics device.");
            }

            previousFog = RenderSettings.fog;
            previousAmbientMode = RenderSettings.ambientMode;
            previousAmbientLight = RenderSettings.ambientLight;
            renderSettingsCaptured = true;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white;

            const int testLayer = 28;
            const int targetSize = 128;
            Vector3 sceneOrigin =
                new Vector3(8000f, 8000f, 0f);
            GameObject cameraObject =
                CreateObject("Home Dither GPU Camera");
            gpuCamera = cameraObject.AddComponent<Camera>();
            gpuCamera.enabled = false;
            gpuCamera.clearFlags =
                CameraClearFlags.SolidColor;
            gpuCamera.backgroundColor = Color.black;
            gpuCamera.cullingMask = 1 << testLayer;
            gpuCamera.allowHDR = false;
            gpuCamera.allowMSAA = false;
            gpuCamera.allowDynamicResolution = false;
            gpuCamera.orthographic = true;
            gpuCamera.orthographicSize = 1f;
            gpuCamera.aspect = 1f;
            gpuCamera.nearClipPlane = 0.1f;
            gpuCamera.farClipPlane = 10f;
            cameraObject.transform.position =
                sceneOrigin + Vector3.back * 3f;
            cameraObject.transform.rotation =
                Quaternion.identity;
            UniversalAdditionalCameraData cameraData =
                gpuCamera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = false;
            cameraData.volumeLayerMask = 0;

            GameObject occluder =
                GameObject.CreatePrimitive(PrimitiveType.Quad);
            occluder.name = "Home Dither GPU Occluder";
            occluder.transform.position = sceneOrigin;
            occluder.transform.localScale =
                new Vector3(1.2f, 1.2f, 1f);
            cleanupObjects.Add(occluder);
            occluder.layer = testLayer;
            Renderer renderer =
                occluder.GetComponent<Renderer>();
            Material ditherMaterial =
                HomeOcclusionResources.DitherMaterial;
            renderer.sharedMaterial = ditherMaterial;
            Assert.That(
                ditherMaterial.FindPass("ForwardLit"),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(
                ditherMaterial.FindPass("ShadowCaster"),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(
                ditherMaterial.FindPass("DepthOnly"),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(
                ditherMaterial.FindPass("DepthNormals"),
                Is.GreaterThanOrEqualTo(0),
                "Home occluders must remain present in the SSAO normals prepass.");
            renderer.shadowCastingMode =
                ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            gpuTarget = new RenderTexture(
                targetSize,
                targetSize,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                name = "Home Dither GPU Target",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Assert.That(gpuTarget.Create(), Is.True);
            previousCameraTarget = gpuCamera.targetTexture;
            gpuCamera.targetTexture = gpuTarget;

            var properties = new MaterialPropertyBlock();
            properties.SetColor(
                Shader.PropertyToID("_BaseColor"),
                Color.white);
            properties.SetFloat(
                HomePlayerOcclusionController
                    .VisibilityPropertyId,
                1f);
            renderer.SetPropertyBlock(properties);
            yield return null;

            fullVisibilityReadback = Capture(
                gpuCamera,
                gpuTarget,
                "Home Dither Full Visibility Readback");
            int fullCoverage = CountForegroundPixels(
                fullVisibilityReadback);

            renderer.GetPropertyBlock(properties);
            properties.SetFloat(
                HomePlayerOcclusionController
                    .VisibilityPropertyId,
                0.25f);
            renderer.SetPropertyBlock(properties);
            ditheredReadback = Capture(
                gpuCamera,
                gpuTarget,
                "Home Dither Partial Visibility Readback");
            int ditheredCoverage = CountForegroundPixels(
                ditheredReadback);
            Assert.That(
                fullCoverage,
                Is.GreaterThan(2000),
                "The fully visible primitive must produce a stable foreground mask.");
            Assert.That(
                ditheredCoverage,
                Is.GreaterThan(fullCoverage * 0.10f),
                "Quarter visibility must retain a readable portion of the occluder.");
            Assert.That(
                ditheredCoverage,
                Is.LessThan(fullCoverage * 0.45f),
                "Quarter visibility must remove substantially more pixels than full visibility.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator
            DitherShader_ReceivesForwardPlusAdditionalLight()
        {
            if (SystemInfo.graphicsDeviceType ==
                GraphicsDeviceType.Null)
            {
                Assert.Ignore(
                    "Home Forward+ lighting requires a graphics device.");
            }

            previousFog = RenderSettings.fog;
            previousAmbientMode = RenderSettings.ambientMode;
            previousAmbientLight = RenderSettings.ambientLight;
            renderSettingsCaptured = true;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;

            const int testLayer = 28;
            const int targetSize = 128;
            Vector3 sceneOrigin =
                new Vector3(9000f, 9000f, 0f);
            GameObject cameraObject =
                CreateObject("Home Forward Plus GPU Camera");
            gpuCamera = cameraObject.AddComponent<Camera>();
            gpuCamera.enabled = false;
            gpuCamera.clearFlags = CameraClearFlags.SolidColor;
            gpuCamera.backgroundColor = Color.black;
            gpuCamera.cullingMask = 1 << testLayer;
            gpuCamera.allowHDR = false;
            gpuCamera.allowMSAA = false;
            gpuCamera.allowDynamicResolution = false;
            gpuCamera.orthographic = true;
            gpuCamera.orthographicSize = 1f;
            gpuCamera.aspect = 1f;
            gpuCamera.nearClipPlane = 0.1f;
            gpuCamera.farClipPlane = 10f;
            cameraObject.transform.position =
                sceneOrigin + Vector3.back * 3f;
            cameraObject.transform.rotation =
                Quaternion.identity;
            UniversalAdditionalCameraData cameraData =
                gpuCamera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = false;
            cameraData.volumeLayerMask = 0;

            GameObject occluder =
                GameObject.CreatePrimitive(PrimitiveType.Quad);
            occluder.name = "Home Forward Plus GPU Occluder";
            occluder.layer = testLayer;
            occluder.transform.position = sceneOrigin;
            occluder.transform.localScale =
                new Vector3(1.2f, 1.2f, 1f);
            cleanupObjects.Add(occluder);
            Renderer renderer = occluder.GetComponent<Renderer>();
            renderer.sharedMaterial =
                HomeOcclusionResources.DitherMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var properties = new MaterialPropertyBlock();
            properties.SetColor(
                Shader.PropertyToID("_BaseColor"),
                Color.white);
            properties.SetFloat(
                HomePlayerOcclusionController
                    .VisibilityPropertyId,
                1f);
            renderer.SetPropertyBlock(properties);

            GameObject lightObject =
                CreateObject("Home Forward Plus GPU Light");
            lightObject.layer = testLayer;
            lightObject.transform.position =
                sceneOrigin + Vector3.back * 1.25f;
            Light additionalLight =
                lightObject.AddComponent<Light>();
            additionalLight.type = LightType.Point;
            additionalLight.color = Color.white;
            additionalLight.intensity = 5f;
            additionalLight.range = 4f;
            additionalLight.shadows = LightShadows.None;
            additionalLight.cullingMask = 1 << testLayer;
            additionalLight.renderMode =
                LightRenderMode.ForcePixel;

            gpuTarget = new RenderTexture(
                targetSize,
                targetSize,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                name = "Home Forward Plus GPU Target",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Assert.That(gpuTarget.Create(), Is.True);
            previousCameraTarget = gpuCamera.targetTexture;
            gpuCamera.targetTexture = gpuTarget;
            yield return null;

            fullVisibilityReadback = Capture(
                gpuCamera,
                gpuTarget,
                "Home Forward Plus GPU Readback");
            Assert.That(
                CountForegroundPixels(fullVisibilityReadback),
                Is.GreaterThan(2000),
                "The shared Home dither material must receive clustered additional lights in Forward+.");
            LogAssert.NoUnexpectedReceived();
        }

        private static IEnumerator WaitForVisibility(
            HomePlayerOcclusionController controller,
            HomeOccluderGroup group,
            float expected,
            Renderer[] renderers,
            Collider[] colliders)
        {
            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline &&
                   Mathf.Abs(
                       controller.GetVisibility(group) -
                       expected) > VisibilityTolerance)
            {
                AssertSharedOcclusionMaterial(renderers);
                AssertCollidersUnchanged(colliders);
                yield return null;
            }

            AssertVisibility(controller, group, expected);
        }

        private static void AssertVisibility(
            HomePlayerOcclusionController controller,
            HomeOccluderGroup group,
            float expected)
        {
            Assert.That(
                controller.GetVisibility(group),
                Is.EqualTo(expected)
                    .Within(VisibilityTolerance));
            Assert.That(
                controller.GetVisibility(group.Id),
                Is.EqualTo(expected)
                    .Within(VisibilityTolerance));
        }

        private static void AssertGroupProperty(
            HomeOccluderGroup group,
            float expected)
        {
            var properties = new MaterialPropertyBlock();
            for (int index = 0;
                 index < group.Renderers.Count;
                 index++)
            {
                Renderer renderer = group.Renderers[index];
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetFloat(
                        HomePlayerOcclusionController
                            .VisibilityPropertyId),
                    Is.EqualTo(expected)
                        .Within(VisibilityTolerance),
                    $"'{renderer.name}' must share its group's visibility.");
                properties.Clear();
            }
        }

        private static void AssertSharedOcclusionMaterial(
            Renderer[] renderers)
        {
            Material expected =
                HomeOcclusionResources.DitherMaterial;
            for (int index = 0;
                 index < renderers.Length;
                 index++)
            {
                Assert.That(
                    renderers[index].sharedMaterial,
                    Is.SameAs(expected),
                    "Occlusion animation must reuse one packaged material.");
            }
        }

        private static void AssertCollidersUnchanged(
            Collider[] colliders)
        {
            for (int index = 0;
                 index < colliders.Length;
                 index++)
            {
                Assert.That(colliders[index], Is.Not.Null);
                Assert.That(colliders[index].enabled, Is.True);
                Assert.That(
                    colliders[index].gameObject.activeInHierarchy,
                    Is.True);
            }
        }

        private static void AssertPropertyColor(
            Renderer renderer,
            int propertyId,
            Color expected)
        {
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            Color actual = properties.GetColor(propertyId);
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f));
        }

        private static Texture2D Capture(
            Camera camera,
            RenderTexture target,
            string textureName)
        {
            camera.Render();
            var result = new Texture2D(
                target.width,
                target.height,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = textureName,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.DontSave
            };
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                result.ReadPixels(
                    new Rect(
                        0f,
                        0f,
                        target.width,
                        target.height),
                    0,
                    0,
                    false);
                result.Apply(false, false);
            }
            catch
            {
                Object.Destroy(result);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
            }

            return result;
        }

        private static int CountForegroundPixels(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            Color32 background = pixels[0];
            int visible = 0;
            for (int index = 0; index < pixels.Length; index++)
            {
                Color32 pixel = pixels[index];
                int difference =
                    Mathf.Abs(pixel.r - background.r) +
                    Mathf.Abs(pixel.g - background.g) +
                    Mathf.Abs(pixel.b - background.b);
                // URP render scaling can leave dim filtered pixels around a
                // one-pixel dither sample. Count only the opaque lit sample,
                // not that sub-pixel reconstruction footprint.
                if (difference > 240)
                {
                    visible++;
                }
            }

            return visible;
        }

        private Camera CreateCamera()
        {
            GameObject cameraObject =
                CreateObject("Synthetic Camera");
            cameraObject.transform.position =
                new Vector3(0f, 1f, -10f);
            cameraObject.transform.rotation =
                Quaternion.identity;
            return cameraObject.AddComponent<Camera>();
        }

        private GameObject CreateChildPrimitive(
            GameObject parent,
            string name,
            Vector3 position,
            Vector3 scale)
        {
            GameObject result =
                CreatePrimitive(name, position, scale);
            result.transform.SetParent(parent.transform, true);
            return result;
        }

        private GameObject CreatePrimitive(
            string name,
            Vector3 position,
            Vector3 scale)
        {
            GameObject result =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            result.name = name;
            result.transform.position = position;
            result.transform.localScale = scale;
            cleanupObjects.Add(result);
            return result;
        }

        private GameObject CreateObject(string name)
        {
            var result = new GameObject(name);
            cleanupObjects.Add(result);
            return result;
        }
    }
}
