using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class Player3DShadowRenderPlayModeTests
    {
        private GameObject cameraObject;
        private GameObject lightObject;
        private GameObject floorObject;
        private GameObject playerObject;
        private RenderTexture target;
        private Texture2D withShadow;
        private Texture2D withoutShadow;
        private Light previousSun;
        private bool previousFog;
        private AmbientMode previousAmbientMode;
        private Color previousAmbientLight;
        private bool renderSettingsCaptured;

        [UnityTest]
        public IEnumerator PlayerMesh_DarkensARealtimeReceiver()
        {
            previousSun = RenderSettings.sun;
            previousFog = RenderSettings.fog;
            previousAmbientMode = RenderSettings.ambientMode;
            previousAmbientLight = RenderSettings.ambientLight;
            renderSettingsCaptured = true;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore(
                    "Realtime mesh shadows require a graphics device.");
            }

            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.18f, 0.18f, 0.18f);

            const int testLayer = 29;
            cameraObject = new GameObject("Player3D Shadow Render Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.white;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.cullingMask = 1 << testLayer;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 30f;
            cameraObject.transform.position =
                new Vector3(0f, 4.5f, -6.5f);
            cameraObject.transform.LookAt(
                new Vector3(0f, 0.45f, 0f));
            UniversalAdditionalCameraData cameraData =
                camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = true;
            cameraData.volumeLayerMask = 0;

            lightObject = new GameObject("Player3D Shadow Render Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Hard;
            light.shadowStrength = 1f;
            lightObject.transform.rotation =
                Quaternion.Euler(52f, -35f, 0f);
            RenderSettings.sun = light;

            floorObject = RuntimePrimitiveFactory.CreateBox(
                "Player3D Shadow Receiver",
                null,
                Vector3.zero,
                new Vector3(7f, 0.16f, 7f),
                new Color(0.82f, 0.82f, 0.82f));
            floorObject.layer = testLayer;
            floorObject.GetComponent<Renderer>().receiveShadows = true;

            PlayerRuntime player = PlayerFactory.Create(
                null,
                new Vector3(0f, 0.12f, 0f),
                camera,
                null,
                null);
            playerObject = player.GameObject;
            player.Motor.enabled = false;
            SetLayerRecursively(playerObject.transform, testLayer);
            Assert.That(player.Visual.Renderers, Is.Not.Empty);
            for (int index = 0;
                 index < player.Visual.Renderers.Count;
                 index++)
            {
                Assert.That(
                    player.Visual.Renderers[index].shadowCastingMode,
                    Is.EqualTo(ShadowCastingMode.On));
            }

            target = new RenderTexture(
                640,
                360,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                name = "Player3D Shadow Render Target"
            };
            Assert.That(target.Create(), Is.True);
            camera.targetTexture = target;

            yield return null;
            withShadow = Capture(camera, target);

            for (int index = 0;
                 index < player.Visual.Renderers.Count;
                 index++)
            {
                player.Visual.Renderers[index].shadowCastingMode =
                    ShadowCastingMode.Off;
            }

            yield return null;
            withoutShadow = Capture(camera, target);

            Color32[] shadowPixels = withShadow.GetPixels32();
            Color32[] clearPixels = withoutShadow.GetPixels32();
            int darkenedPixelCount = 0;
            int totalLuminanceDelta = 0;
            for (int index = 0; index < shadowPixels.Length; index++)
            {
                int shadowLuminance =
                    shadowPixels[index].r +
                    shadowPixels[index].g +
                    shadowPixels[index].b;
                int clearLuminance =
                    clearPixels[index].r +
                    clearPixels[index].g +
                    clearPixels[index].b;
                int delta = clearLuminance - shadowLuminance;
                if (delta > 18)
                {
                    darkenedPixelCount++;
                    totalLuminanceDelta += delta;
                }
            }

            Assert.That(
                darkenedPixelCount,
                Is.GreaterThan(24),
                "The 3D player meshes must cast a readable receiver shadow.");
            Assert.That(totalLuminanceDelta, Is.GreaterThan(1800));
        }

        [UnityTest]
        public IEnumerator ContactShadow_DarkensTheGroundAtThePlayersFeet()
        {
            previousSun = RenderSettings.sun;
            previousFog = RenderSettings.fog;
            previousAmbientMode = RenderSettings.ambientMode;
            previousAmbientLight = RenderSettings.ambientLight;
            renderSettingsCaptured = true;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore(
                    "Contact-shadow output requires a graphics device.");
            }

            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.8f, 0.8f, 0.8f);

            const int testLayer = 29;
            cameraObject = new GameObject("Contact Shadow Render Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.white;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.cullingMask = 1 << testLayer;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 20f;
            cameraObject.transform.position =
                new Vector3(0f, 3.2f, -4.2f);
            cameraObject.transform.LookAt(
                new Vector3(0f, 0.08f, 0f));
            UniversalAdditionalCameraData cameraData =
                camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = false;
            cameraData.volumeLayerMask = 0;

            lightObject = new GameObject("Contact Shadow Render Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1f;
            light.shadows = LightShadows.None;
            lightObject.transform.rotation =
                Quaternion.Euler(52f, -35f, 0f);
            RenderSettings.sun = light;

            floorObject = RuntimePrimitiveFactory.CreateBox(
                "Contact Shadow Receiver",
                null,
                Vector3.zero,
                new Vector3(4f, 0.16f, 4f),
                new Color(0.72f, 0.72f, 0.72f));
            floorObject.layer = testLayer;

            PlayerRuntime player = PlayerFactory.Create(
                null,
                new Vector3(0f, 0.08f, 0f),
                camera,
                null,
                null);
            playerObject = player.GameObject;
            player.Motor.enabled = false;
            SetLayerRecursively(playerObject.transform, testLayer);
            for (int index = 0;
                 index < player.Visual.Renderers.Count;
                 index++)
            {
                player.Visual.Renderers[index].enabled = false;
            }

            target = new RenderTexture(
                640,
                360,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                name = "Player Contact Shadow Render Target"
            };
            Assert.That(target.Create(), Is.True);
            camera.targetTexture = target;

            yield return null;
            withShadow = Capture(camera, target);

            player.ContactShadow.enabled = false;
            yield return null;
            withoutShadow = Capture(camera, target);

            Color32[] shadowPixels = withShadow.GetPixels32();
            Color32[] clearPixels = withoutShadow.GetPixels32();
            int darkenedPixelCount = 0;
            int totalLuminanceDelta = 0;
            for (int index = 0; index < shadowPixels.Length; index++)
            {
                int shadowLuminance =
                    shadowPixels[index].r +
                    shadowPixels[index].g +
                    shadowPixels[index].b;
                int clearLuminance =
                    clearPixels[index].r +
                    clearPixels[index].g +
                    clearPixels[index].b;
                int delta = clearLuminance - shadowLuminance;
                if (delta > 6)
                {
                    darkenedPixelCount++;
                    totalLuminanceDelta += delta;
                }
            }

            Assert.That(
                darkenedPixelCount,
                Is.GreaterThan(40),
                "The analytic contact patch must visibly darken the floor.");
            Assert.That(totalLuminanceDelta, Is.GreaterThan(1200));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (renderSettingsCaptured)
            {
                RenderSettings.sun = previousSun;
                RenderSettings.fog = previousFog;
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbientLight;
                renderSettingsCaptured = false;
            }

            DestroyObject(playerObject);
            DestroyObject(floorObject);
            DestroyObject(lightObject);
            DestroyObject(cameraObject);
            if (target != null)
            {
                target.Release();
                Object.Destroy(target);
            }

            if (withShadow != null)
            {
                Object.Destroy(withShadow);
            }

            if (withoutShadow != null)
            {
                Object.Destroy(withoutShadow);
            }

            yield return null;
        }

        private static Texture2D Capture(
            Camera camera,
            RenderTexture renderTarget)
        {
            camera.Render();
            Texture2D result = new Texture2D(
                renderTarget.width,
                renderTarget.height,
                TextureFormat.RGBA32,
                false,
                true);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTarget;
            result.ReadPixels(
                new Rect(
                    0f,
                    0f,
                    renderTarget.width,
                    renderTarget.height),
                0,
                0,
                false);
            result.Apply(false, false);
            RenderTexture.active = previous;
            return result;
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

        private static void DestroyObject(GameObject value)
        {
            if (value != null)
            {
                Object.Destroy(value);
            }
        }
    }
}
