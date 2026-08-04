using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class Player3DVisualCapturePlayModeTests
    {
        private const int TileSize = 256;
        private GameObject playerRoot;
        private GameObject cameraObject;
        private GameObject lightObject;
        private RenderTexture renderTarget;
        private Texture2D contactSheet;
        private AmbientMode previousAmbientMode;
        private Color previousAmbientLight;
        private Light previousSun;
        private bool previousFog;
        private bool renderSettingsCaptured;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (renderSettingsCaptured)
            {
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbientLight;
                RenderSettings.sun = previousSun;
                RenderSettings.fog = previousFog;
                renderSettingsCaptured = false;
            }

            if (renderTarget != null)
            {
                renderTarget.Release();
                Object.Destroy(renderTarget);
            }

            if (contactSheet != null)
            {
                Object.Destroy(contactSheet);
            }

            if (playerRoot != null)
            {
                Object.Destroy(playerRoot);
            }

            if (cameraObject != null)
            {
                Object.Destroy(cameraObject);
            }

            if (lightObject != null)
            {
                Object.Destroy(lightObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator ProductionRig_RendersOrdinaryAndContextualPoseSheet()
        {
            previousAmbientMode = RenderSettings.ambientMode;
            previousAmbientLight = RenderSettings.ambientLight;
            previousSun = RenderSettings.sun;
            previousFog = RenderSettings.fog;
            renderSettingsCaptured = true;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.52f, 0.54f, 0.58f);
            RenderSettings.fog = false;

            const int captureLayer = 28;

            cameraObject = new GameObject("Player3D Visual QA Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.aspect = 1f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 20f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.cullingMask = 1 << captureLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.20f, 0.22f, 0.26f);
            UniversalAdditionalCameraData cameraData =
                camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = true;
            cameraData.volumeLayerMask = 0;

            renderTarget = new RenderTexture(
                TileSize,
                TileSize,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                name = "Player3D Visual QA Target",
                antiAliasing = 1
            };
            renderTarget.Create();
            camera.targetTexture = renderTarget;

            lightObject = new GameObject("Player3D Visual QA Key Light");
            Light keyLight = lightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.6f;
            keyLight.color = new Color(1f, 0.86f, 0.74f);
            keyLight.shadows = LightShadows.Hard;
            keyLight.cullingMask = 1 << captureLayer;
            RenderSettings.sun = keyLight;

            playerRoot = new GameObject("Player3D Visual QA Root");
            Player3DAssetRegistry registry =
                Player3DResources.Instantiate(playerRoot.transform);
            SetLayerRecursively(playerRoot.transform, captureLayer);
            Player3DCharacterPresentation presentation =
                registry.GetComponent<Player3DCharacterPresentation>();
            if (presentation == null)
            {
                presentation = registry.gameObject.AddComponent<
                    Player3DCharacterPresentation>();
            }

            presentation.Initialize(playerRoot.transform, registry);
            contactSheet = new Texture2D(
                TileSize * 2,
                TileSize * 2,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Player3D Visual QA Contact Sheet",
                filterMode = FilterMode.Point
            };

            string[] clips =
            {
                "Idle",
                "BedSleepLoop",
                "SmokeLoop",
                "CatFeedLoop"
            };
            float[] samples = { 0.35f, 0.55f, 0.50f, 0.50f };
            int[] foregroundCounts = new int[clips.Length];
            for (int index = 0; index < clips.Length; index++)
            {
                Assert.That(
                    presentation.TryBeginClip(clips[index]),
                    Is.True,
                    $"The production prefab must expose '{clips[index]}'.");
                presentation.SampleActiveClip(samples[index]);
                yield return null;

                Bounds bounds = GetEnabledBounds(presentation);
                FrameCamera(camera, registry, bounds);
                lightObject.transform.forward = camera.transform.forward;
                Texture2D tile = Capture(camera);
                try
                {
                    foregroundCounts[index] = CountForegroundPixels(
                        tile,
                        camera.backgroundColor);
                    int x = (index % 2) * TileSize;
                    int y = (index / 2) * TileSize;
                    contactSheet.SetPixels(x, y, TileSize, TileSize,
                        tile.GetPixels());
                }
                finally
                {
                    Object.Destroy(tile);
                }

                presentation.EndClip();
            }

            contactSheet.Apply(false, false);
            string outputDirectory = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "TestResults"));
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(
                outputDirectory,
                "player-3d-contact-sheet.png");
            File.WriteAllBytes(outputPath, contactSheet.EncodeToPNG());

            Assert.That(File.Exists(outputPath), Is.True);
            Assert.That(new FileInfo(outputPath).Length, Is.GreaterThan(4096));
            for (int index = 0; index < clips.Length; index++)
            {
                Assert.That(
                    foregroundCounts[index],
                    Is.GreaterThan(900),
                    $"'{clips[index]}' produced no substantial " +
                    "GPU-visible model.");
            }
        }

        private static Bounds GetEnabledBounds(
            IPlayerPresentation presentation)
        {
            bool hasBounds = false;
            Bounds combined = default;
            for (int index = 0;
                 index < presentation.Renderers.Count;
                 index++)
            {
                Renderer renderer = presentation.Renderers[index];
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
            Assert.That(combined.size.sqrMagnitude, Is.GreaterThan(0.25f));
            return combined;
        }

        private static void FrameCamera(
            Camera camera,
            Player3DAssetRegistry registry,
            Bounds bounds)
        {
            Vector3 forward = registry.transform.TransformDirection(
                registry.Metrics.LocalForward).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            float horizontalExtent = Mathf.Max(
                bounds.extents.x,
                bounds.extents.z);
            bool primarilyHorizontal =
                horizontalExtent > bounds.extents.y * 1.15f;
            float elevation = primarilyHorizontal ? 0.82f : 0.24f;
            Vector3 viewOffset =
                (forward + right * 0.24f + Vector3.up * elevation).normalized;
            camera.transform.position = bounds.center + viewOffset * 8f;
            camera.transform.rotation = Quaternion.LookRotation(
                bounds.center - camera.transform.position,
                Vector3.up);
            camera.orthographicSize = Mathf.Max(
                0.55f,
                Mathf.Max(bounds.extents.y, horizontalExtent) * 1.28f);
        }

        private Texture2D Capture(Camera camera)
        {
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTarget;
            var result = new Texture2D(
                TileSize,
                TileSize,
                TextureFormat.RGBA32,
                false,
                true);
            result.ReadPixels(
                new Rect(0f, 0f, TileSize, TileSize),
                0,
                0,
                false);
            result.Apply(false, false);
            RenderTexture.active = previous;
            return result;
        }

        private static int CountForegroundPixels(
            Texture2D image,
            Color background)
        {
            Color32 expected = background;
            Color32[] pixels = image.GetPixels32();
            int foreground = 0;
            for (int index = 0; index < pixels.Length; index++)
            {
                Color32 pixel = pixels[index];
                int difference =
                    Mathf.Abs(pixel.r - expected.r) +
                    Mathf.Abs(pixel.g - expected.g) +
                    Mathf.Abs(pixel.b - expected.b);
                if (difference >= 24)
                {
                    foreground++;
                }
            }

            return foreground;
        }

        private static void SetLayerRecursively(
            Transform root,
            int layer)
        {
            root.gameObject.layer = layer;
            for (int index = 0; index < root.childCount; index++)
            {
                SetLayerRecursively(root.GetChild(index), layer);
            }
        }
    }
}
