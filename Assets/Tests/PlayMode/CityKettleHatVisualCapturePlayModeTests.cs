using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// Frames of the Kettle Hat walker on the boil, for looking at, not
    /// for asserting: eight frames over three seconds at three distances,
    /// so the lid can be seen moving between frames and the steam judged
    /// against the enamel. Explicit, like every capture in this project -
    /// it writes files and belongs to no sweep. The frames are 1280x720;
    /// view them at half size to see what 640x360 sees.
    /// </summary>
    public sealed class CityKettleHatVisualCapturePlayModeTests
    {
        private const int Width = 1280;
        private const int Height = 720;
        private const int FrameCount = 8;
        private const float SequenceSeconds = 3f;
        private const float FieldOfView = 53f;
        private const float KettleHeight = 1.55f;
        private const uint EffectSeed = 7u;
        private const string CaptureFolder = "KettleHat";
        private static readonly float[] Distances = { 3f, 6f, 12f };

        private GameObject root;
        private GameObject cameraObject;
        private GameObject lightObject;
        private RenderTexture renderTarget;
        private Texture2D frameBuffer;
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

            if (frameBuffer != null)
            {
                Object.Destroy(frameBuffer);
            }

            if (root != null)
            {
                Object.Destroy(root);
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
        [Explicit("Capture, not a test. Look at Captures/KettleHat/.")]
        public IEnumerator KettleHat_BoilsOnCameraAtThreeDistances()
        {
            Assert.That(
                CityPedestrianResources.TryGetArchetype(
                    CityPedestrianResources.KettleHatDesignId,
                    out CityPedestrianArchetype archetype),
                Is.True);
            GameObject prefab = CityPedestrianResources.LoadPrefab(archetype);
            Assert.That(prefab, Is.Not.Null);
            if (prefab.GetComponent<CityKettleHatRigAnchors>() == null)
            {
                Assert.Ignore(
                    "The Kettle Hat prefab carries no " +
                    "CityKettleHatRigAnchors yet: rebuild the pedestrian " +
                    "prefabs (NpcHumanV2AssetSetup.RunBatch) first.");
            }

            previousAmbientMode = RenderSettings.ambientMode;
            previousAmbientLight = RenderSettings.ambientLight;
            previousSun = RenderSettings.sun;
            previousFog = RenderSettings.fog;
            renderSettingsCaptured = true;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.52f, 0.54f, 0.58f);
            RenderSettings.fog = false;

            cameraObject = new GameObject("Kettle Hat Capture Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = FieldOfView;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 60f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.20f, 0.22f, 0.26f);
            UniversalAdditionalCameraData cameraData =
                camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = true;
            cameraData.volumeLayerMask = 0;

            renderTarget = new RenderTexture(
                Width,
                Height,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default)
            {
                name = "Kettle Hat Capture Target",
                antiAliasing = 1
            };
            renderTarget.Create();
            camera.targetTexture = renderTarget;
            frameBuffer = new Texture2D(
                Width,
                Height,
                TextureFormat.RGB24,
                false);

            lightObject = new GameObject("Kettle Hat Capture Key Light");
            Light keyLight = lightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.6f;
            keyLight.color = new Color(1f, 0.86f, 0.74f);
            keyLight.shadows = LightShadows.Hard;
            keyLight.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.sun = keyLight;

            root = new GameObject("Kettle Hat Capture Root");
            GameObject instance = Object.Instantiate(
                prefab,
                root.transform,
                false);
            CityPedestrianAssetRegistry registry =
                instance.GetComponent<CityPedestrianAssetRegistry>();
            CityKettleHatRigAnchors anchors =
                instance.GetComponent<CityKettleHatRigAnchors>();
            CityPedestrianPresentation presentation =
                instance.AddComponent<CityPedestrianPresentation>();
            presentation.Initialize(registry);
            registry.Animator.cullingMode =
                AnimatorCullingMode.AlwaysAnimate;
            CityKettleHatBoilEffect effect =
                instance.AddComponent<CityKettleHatBoilEffect>();
            effect.Initialize(presentation, anchors, EffectSeed);

            string folder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Captures",
                CaptureFolder);
            Directory.CreateDirectory(folder);

            // The walker faces the camera three-quarters on so the spout
            // and the lid both read; the model child is flipped 180
            // degrees in the prefab, and the first strip proved that the
            // model root's own forward is the side his face is on - the
            // negated guess photographed his back.
            Vector3 facing = registry.ModelRoot != null
                ? registry.ModelRoot.forward
                : Vector3.forward;
            Vector3 viewDirection = Quaternion.AngleAxis(35f, Vector3.up) *
                                    facing;
            Vector3 focus = root.transform.position +
                            (Vector3.up * KettleHeight);
            float frameInterval = SequenceSeconds / FrameCount;
            int written = 0;
            for (int distanceIndex = 0;
                 distanceIndex < Distances.Length;
                 distanceIndex++)
            {
                float distance = Distances[distanceIndex];
                camera.transform.position = focus +
                                            (viewDirection * distance) +
                                            (Vector3.up * 0.2f);
                camera.transform.rotation = Quaternion.LookRotation(
                    focus - camera.transform.position,
                    Vector3.up);
                float elapsed = 0f;
                float nextCapture = 0f;
                int frame = 0;
                while (frame < FrameCount)
                {
                    presentation.Advance(Time.deltaTime, true, true);
                    yield return null;
                    elapsed += Time.deltaTime;
                    if (elapsed < nextCapture)
                    {
                        continue;
                    }

                    camera.Render();
                    RenderTexture previousActive = RenderTexture.active;
                    RenderTexture.active = renderTarget;
                    frameBuffer.ReadPixels(
                        new Rect(0f, 0f, Width, Height),
                        0,
                        0);
                    frameBuffer.Apply();
                    RenderTexture.active = previousActive;

                    string path = Path.Combine(
                        folder,
                        $"kettle-{distance:00}m-{frame:00}.png");
                    File.WriteAllBytes(path, frameBuffer.EncodeToPNG());
                    Debug.Log(
                        $"Kettle capture wrote {path} " +
                        $"(lift {effect.LastLidLift * 1000f:0.0} mm, " +
                        $"tilt {effect.LastLidTilt.x:0.0} deg, " +
                        $"steam {effect.Steam.particleCount})");
                    Assert.That(
                        IsBlank(frameBuffer),
                        Is.False,
                        $"'{path}' came out a single flat colour.");
                    written++;
                    frame++;
                    nextCapture += frameInterval;
                }
            }

            Assert.That(written, Is.EqualTo(FrameCount * Distances.Length));
        }

        private static bool IsBlank(Texture2D image)
        {
            Color32[] pixels = image.GetPixels32();
            if (pixels.Length == 0)
            {
                return true;
            }

            Color32 first = pixels[0];
            for (int index = 1; index < pixels.Length; index++)
            {
                Color32 pixel = pixels[index];
                if (pixel.r != first.r ||
                    pixel.g != first.g ||
                    pixel.b != first.b)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
