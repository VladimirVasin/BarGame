using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// Photographs the six roaming designs on their STREET pair - the
    /// shared citizen gait - through the production presentation: the
    /// line-up waiting (street idle), then walking at four phases of the
    /// hero's cycle, from the front, from three-quarters and from the
    /// side. The numbers in <c>CityPedestrianStreetGaitTests</c> prove the
    /// arms hang and swing; a frame here shows whether the walk reads as
    /// a person walking.
    ///
    /// `[Explicit]`: a capture, not a regression. Frames land in
    /// `Captures/PedestrianGait/` (gitignored). Look at them.
    /// </summary>
    public sealed class CityPedestrianGaitCapturePlayModeTests
    {
        private const int Width = 960;
        private const int Height = 720;
        private const float Spacing = 1.3f;

        /// <summary>Well above any loaded world; the camera's 30 m far
        /// plane never reaches back down to it.</summary>
        private static readonly Vector3 StageOrigin = new Vector3(0f, 4000f, 0f);

        private static readonly string[] RoamingPrefabPaths =
        {
            CityPedestrianResources.BabushkaPrefabResourcePath,
            CityPedestrianResources.WeighAttendantPrefabResourcePath,
            CityPedestrianResources.WatchmanPrefabResourcePath,
            CityPedestrianResources.ChessPlayerPrefabResourcePath,
            CityPedestrianResources.CheckersPlayerPrefabResourcePath,
            CityPedestrianResources.MournerPrefabResourcePath
        };

        [UnityTest]
        [Explicit("Capture, not a test. Look at Captures/PedestrianGait/.")]
        public IEnumerator RoamingWalkers_WaitAndWalkOnCamera()
        {
            string folder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Captures",
                "PedestrianGait");
            Directory.CreateDirectory(folder);

            var stage = new GameObject("Gait Capture Stage");
            stage.transform.position = StageOrigin;
            var lightObject = new GameObject("Gait Capture Light");
            var cameraObject = new GameObject("Gait Capture Camera");
            var target = new RenderTexture(Width, Height, 24);
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            bool previousFog = RenderSettings.fog;
            AmbientMode previousMode = RenderSettings.ambientMode;
            Color previousAmbient = RenderSettings.ambientLight;
            var presentations = new CityPedestrianPresentation[RoamingPrefabPaths.Length];
            try
            {
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.3f;
                light.shadows = LightShadows.Soft;
                light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.14f, 0.15f);
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 30f;
                camera.targetTexture = target;

                // A floor under the boots, so the leg layer has ground to
                // probe and a frame shows whether the soles meet it.
                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "Floor";
                floor.transform.SetParent(stage.transform, false);
                floor.transform.localScale = new Vector3(3f, 1f, 3f);

                float lineHalf = (RoamingPrefabPaths.Length - 1) * Spacing * 0.5f;
                for (int index = 0; index < RoamingPrefabPaths.Length; index++)
                {
                    GameObject prefab = Resources.Load<GameObject>(RoamingPrefabPaths[index]);
                    Assert.That(prefab, Is.Not.Null, $"{RoamingPrefabPaths[index]} is missing.");
                    GameObject instance = Object.Instantiate(prefab, stage.transform);
                    instance.transform.localPosition =
                        new Vector3(-lineHalf + index * Spacing, 0f, 0f);
                    // Face the camera, which stands on -Z.
                    instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    var registry = instance.GetComponent<CityPedestrianAssetRegistry>();
                    Assert.That(registry, Is.Not.Null);
                    CityPedestrianPresentation presentation =
                        instance.AddComponent<CityPedestrianPresentation>();
                    presentation.Initialize(registry, CityPedestrianClipSource.Roaming);
                    registry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    presentations[index] = presentation;
                }

                yield return null;

                // Let the leg layer's blend settle on the floor.
                for (int frame = 0; frame < 30; frame++)
                {
                    foreach (CityPedestrianPresentation presentation in presentations)
                    {
                        presentation.Advance(1f / 60f, false, true);
                    }

                    yield return null;
                }

                Vector3 lineCentre = StageOrigin + new Vector3(0f, 0.95f, 0f);
                // The first render of a session has no shadow maps yet.
                Shoot(camera, pixels, target, null,
                    StageOrigin + new Vector3(0f, 1.6f, -6f), lineCentre, 55f);

                // Waiting at a crossing: the street idle at rest and mid-breath.
                foreach (CityPedestrianPresentation presentation in presentations)
                {
                    presentation.SetMoving(false, true);
                    presentation.ConfigureCycle(1f, 0f);
                    presentation.Advance(0f, false, true);
                }

                yield return null;
                Shoot(camera, pixels, target, Path.Combine(folder, "00-idle-front.png"),
                    StageOrigin + new Vector3(0f, 1.5f, -6.4f), lineCentre, 60f);
                Shoot(camera, pixels, target, Path.Combine(folder, "01-idle-three-quarter.png"),
                    StageOrigin + new Vector3(-4.6f, 1.7f, -4.6f), lineCentre, 60f);

                // Walking: four phases of the hero's cycle, every walker in
                // step so one frame compares all six.
                float[] phases = { 0f, 0.25f, 0.5f, 0.75f };
                for (int phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
                {
                    float phase = phases[phaseIndex];
                    foreach (CityPedestrianPresentation presentation in presentations)
                    {
                        presentation.SetMoving(true, true);
                        presentation.ConfigureCycle(1f, phase);
                        presentation.Advance(0f, true, true);
                    }

                    yield return null;
                    Shoot(camera, pixels, target,
                        Path.Combine(folder, $"1{phaseIndex}-walk-{phase:0.00}-front.png"),
                        StageOrigin + new Vector3(0f, 1.5f, -6.4f), lineCentre, 60f);
                    Shoot(camera, pixels, target,
                        Path.Combine(folder, $"2{phaseIndex}-walk-{phase:0.00}-three-quarter.png"),
                        StageOrigin + new Vector3(-4.6f, 1.7f, -4.6f), lineCentre, 60f);
                }

                // Side views at heel contact, one walker at a time, so the
                // fore-and-aft swing of each arm reads against the legs.
                // The line stands along X, so a camera out on X would sit
                // inside a neighbour: every walker turns to face +X instead
                // and is photographed from -Z, close enough that the
                // neighbours stay out of frame.
                foreach (CityPedestrianPresentation presentation in presentations)
                {
                    presentation.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    presentation.SetMoving(true, true);
                    presentation.ConfigureCycle(1f, 0f);
                    presentation.Advance(0f, true, true);
                }

                yield return null;
                for (int index = 0; index < presentations.Length; index++)
                {
                    Vector3 focus = presentations[index].transform.position +
                                    new Vector3(0f, 0.95f, 0f);
                    Shoot(camera, pixels, target,
                        Path.Combine(folder, $"3{index}-walk-side-{Path.GetFileName(RoamingPrefabPaths[index])}.png"),
                        focus + new Vector3(0f, 0.2f, -2.4f), focus, 40f);
                }

                Debug.Log($"Pedestrian gait captures wrote {folder}");
            }
            finally
            {
                foreach (CityPedestrianPresentation presentation in presentations)
                {
                    if (presentation != null)
                    {
                        presentation.Shutdown();
                    }
                }

                RenderSettings.fog = previousFog;
                RenderSettings.ambientMode = previousMode;
                RenderSettings.ambientLight = previousAmbient;
                Object.Destroy(stage);
                Object.Destroy(lightObject);
                Object.Destroy(cameraObject);
                Object.Destroy(pixels);
                target.Release();
                Object.Destroy(target);
            }
        }

        private static void Shoot(
            Camera camera,
            Texture2D pixels,
            RenderTexture target,
            string path,
            Vector3 position,
            Vector3 lookAt,
            float fieldOfView)
        {
            camera.transform.position = position;
            camera.transform.rotation = Quaternion.LookRotation(
                lookAt - position,
                Vector3.up);
            camera.fieldOfView = fieldOfView;
            camera.Render();
            if (path == null)
            {
                return;
            }

            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                pixels.Apply();
            }
            finally
            {
                RenderTexture.active = previous;
            }

            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
    }
}
