using System.Collections;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The drunk dolly zoom for the eye: twenty-four seconds of the city's OWN
    /// follow camera at level one hundred, the hero visible on his street
    /// in daylight, written as 1280x720 JPEG frames for ffmpeg to join.
    /// Explicit, like every capture in this project: it writes files and
    /// belongs to no sweep. The follow camera breathes on unscaled time,
    /// so the clock is NOT pinned - a frame is taken whenever real time
    /// crosses the next 1/24 s slot, and the report beside the frames
    /// lists the lens, the reach and the phase of every frame.
    /// </summary>
    public sealed class IntoxicationDollyZoomCapturePlayModeTests
    {
        private const int Width = 1280;
        private const int Height = 720;
        private const float FramesPerSecond = 24f;
        private const float CaptureSeconds = 24f;
        private const float SettleSeconds = 1f;
        private const float TimeoutSeconds = 90f;
        private const int DaylightMinute = 15 * 60;
        private const int Seed = 4242;

        [UnityTest]
        [Explicit("Capture, not a test. Writes dolly zoom frames to Captures/DollyZoom.")]
        public IEnumerator DrunkStreetAtOneHundred()
        {
            string outDir = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Captures", "DollyZoom"));
            string frames = Path.Combine(outDir, "frames");
            if (Directory.Exists(frames))
            {
                Directory.Delete(frames, true);
            }

            Directory.CreateDirectory(frames);

            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.City,
                LoadSceneMode.Single);
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!load.isDone && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(load.isDone, Is.True, "City did not load.");

            CityGameRoot root = null;
            deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                root = Object.FindAnyObjectByType<CityGameRoot>();
                if (root != null && root.IsInitialized && Camera.main != null)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(root, Is.Not.Null, "No CityGameRoot.");
            Assert.That(root.IsInitialized, Is.True, "City never initialized.");
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null, "No main camera.");
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            Assert.That(follow, Is.Not.Null, "The city camera carries no follow.");

            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            if (!GameSessionState.IsGameTimeRunning)
            {
                GameSessionState.TryStartGameTimeFromWake();
            }

            int delta = DaylightMinute - GameSessionState.GameMinuteOfDay;
            if (delta < 0)
            {
                delta += 24 * 60;
            }

            if (delta > 0)
            {
                GameSessionState.AdvanceGameTime(delta);
            }

            bool weatherWasEnabled = root.Weather != null && root.Weather.enabled;
            if (root.Weather != null)
            {
                root.Weather.enabled = false;
            }

            if (root.Rain != null)
            {
                root.Rain.SetIntensity(0f);
            }

            CityWetSurfaceRegistry.SetImmediate(0f);
            CityWaterResources.SetRainIntensity(0f);

            GameSessionState.UpdateDrinkingProgress(100, DrinkId.Vodka, 5);
            follow.ReseedDollyZoom(Seed);
            float settle = Time.realtimeSinceStartup + SettleSeconds;
            while (Time.realtimeSinceStartup < settle)
            {
                yield return null;
            }

            var target = new RenderTexture(Width, Height, 24);
            var buffer = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            var report = new StringBuilder();
            report.AppendLine(
                $"minute={GameSessionState.GameMinuteOfDay} level={GameSessionState.IntoxicationLevel} " +
                $"baseFov={follow.FollowFieldOfView:F1}");
            try
            {
                camera.targetTexture = target;
                int frameIndex = 0;
                float start = Time.realtimeSinceStartup;
                float nextSlot = start;
                while (Time.realtimeSinceStartup - start < CaptureSeconds)
                {
                    yield return null;
                    float now = Time.realtimeSinceStartup;
                    if (now < nextSlot)
                    {
                        continue;
                    }

                    nextSlot += 1f / FramesPerSecond;
                    camera.Render();

                    RenderTexture previousActive = RenderTexture.active;
                    RenderTexture.active = target;
                    buffer.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                    buffer.Apply();
                    RenderTexture.active = previousActive;

                    File.WriteAllBytes(
                        Path.Combine(frames, $"{frameIndex:D5}.jpg"),
                        buffer.EncodeToJPG(92));
                    report.AppendLine(
                        $"{frameIndex:D5} t={now - start:F2} fov={camera.fieldOfView:F1} " +
                        $"reach={follow.DollyZoomExponent:F2} phase={follow.DollyZoomPhase} " +
                        $"arm={Vector3.Distance(camera.transform.position, follow.CurrentFocusPoint):F2}");
                    frameIndex++;
                }

                report.AppendLine($"frames written: {frameIndex}");
                // Batch mode renders the city at ten to fifteen real
                // frames a second, so the slots are a ceiling, not a rate.
                Assert.That(
                    frameIndex,
                    Is.GreaterThan(Mathf.RoundToInt(CaptureSeconds * 4f)),
                    "Too few frames were written.");
            }
            finally
            {
                camera.targetTexture = previousTarget;
                if (root.Weather != null)
                {
                    root.Weather.enabled = weatherWasEnabled;
                }

                target.Release();
                Object.Destroy(target);
                Object.Destroy(buffer);
                File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString());
            }
        }
    }
}
