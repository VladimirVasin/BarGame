using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>Contact proof of the imported production rigs in the focused test.</summary>
    internal static class CityPedestrianPersonalSpaceCapture
    {
        public static void Write(PlayerRuntime player, CityPedestrianActor actor, string shot)
        {
            var cameraObject = new GameObject("Personal Space Contact Camera");
            var lightObject = new GameObject("Personal Space Contact Light");
            var target = new RenderTexture(960, 720, 24);
            var pixels = new Texture2D(960, 720, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            bool previousFog = RenderSettings.fog;
            AmbientMode previousMode = RenderSettings.ambientMode;
            Color previousAmbient = RenderSettings.ambientLight;
            try
            {
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.3f;
                light.transform.rotation = Quaternion.Euler(35f, -35f, 0f);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.14f, 0.15f);
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 20f;
                camera.fieldOfView = 42f;
                Vector3 centre = (actor.Position + player.GameObject.transform.position) * 0.5f;
                camera.transform.position = centre + new Vector3(2.8f, 1.9f, -1.1f);
                camera.transform.LookAt(centre + Vector3.up * 1f);
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0f, 0f, 960f, 720f), 0, 0);
                pixels.Apply();
                string folder = Path.Combine(Directory.GetCurrentDirectory(),
                    "Captures", "CityPersonalSpace");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, shot + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderSettings.fog = previousFog;
                RenderSettings.ambientMode = previousMode;
                RenderSettings.ambientLight = previousAmbient;
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(lightObject);
                Object.DestroyImmediate(pixels);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}
