using System.Collections;
using System.IO;
using System.Text;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The vertigo whirlpool for the eye: twenty-four seconds of the city's
    /// own follow camera at level one hundred, the hero on his street in
    /// daylight, written as 1280x720 JPEG frames for ffmpeg to join.
    /// Explicit, like every capture in this project: it writes files and
    /// belongs to no sweep. The water breathes on unscaled time, so the clock
    /// is NOT pinned - a frame is taken whenever real time crosses the next
    /// 1/24 s slot, and the report beside the frames lists the twist, the
    /// phase and where the eye sat on screen for every frame, which is what
    /// the mp4's pace has to be read from rather than from the nominal rate.
    /// </summary>
    public sealed class IntoxicationVertigoCapturePlayModeTests
    {
        private const int Width = 1280;
        private const int Height = 720;
        private const float FramesPerSecond = 24f;
        private const float CaptureSeconds = 24f;
        private const float SettleSeconds = 1f;
        private const float TimeoutSeconds = 90f;
        private const int DaylightMinute = 15 * 60;
        private const int Seed = 7311;

        [UnityTest]
        [Explicit("Capture, not a test. Writes whirlpool frames to Captures/Vertigo.")]
        public IEnumerator DrunkStreetAtOneHundred()
        {
            string outDir = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Captures", "Vertigo"));
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
            IntoxicationStatusController status =
                Object.FindAnyObjectByType<IntoxicationStatusController>();
            Assert.That(status, Is.Not.Null, "The city carries no status controller.");

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

            bool lensFxWasEnabled =
                GraphicsEffectsSettings.IntoxicationLensFxEnabled;
            GraphicsEffectsSettings.IntoxicationLensFxEnabled = true;
            GameSessionState.UpdateDrinkingProgress(100, DrinkId.Vodka, 5);
            float settle = Time.realtimeSinceStartup + SettleSeconds;
            while (Time.realtimeSinceStartup < settle)
            {
                yield return null;
            }

            // The opening rest is spent up front: twenty-four seconds should
            // hold a whole attack rather than two seconds of still water.
            status.ReseedVertigo(Seed);
            status.Vertigo.Reset(0f);

            var target = new RenderTexture(Width, Height, 24);
            var buffer = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            var report = new StringBuilder();
            report.AppendLine(
                $"minute={GameSessionState.GameMinuteOfDay} level={GameSessionState.IntoxicationLevel} " +
                $"maxTwist={IntoxicationVertigoModel.MaximumTwistDegrees:F1}deg " +
                $"inner={IntoxicationWhirlpool.InnerRadius:F2} pull={IntoxicationWhirlpool.RadialPull:F2}");
            float peakTwist = 0f;
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
                    IntoxicationRenderParameters published =
                        IntoxicationRenderState.Current;
                    Vector3 eye = camera.WorldToViewportPoint(
                        published.VertigoEyeWorldPosition);
                    float twistDegrees =
                        published.VertigoTwistRadians * Mathf.Rad2Deg;
                    peakTwist = Mathf.Max(peakTwist, Mathf.Abs(twistDegrees));
                    report.AppendLine(
                        $"{frameIndex:D5} t={now - start:F2} twist={twistDegrees:F1}deg " +
                        $"phase={status.Vertigo.Phase} core={published.VertigoCorePixels.magnitude:F2}px " +
                        $"eye=({eye.x:F2},{eye.y:F2})");
                    frameIndex++;
                }

                report.AppendLine($"frames written: {frameIndex}");
                report.AppendLine($"peak twist: {peakTwist:F1}deg");
                // Batch mode renders the city at ten to fifteen real
                // frames a second, so the slots are a ceiling, not a rate.
                Assert.That(
                    frameIndex,
                    Is.GreaterThan(Mathf.RoundToInt(CaptureSeconds * 4f)),
                    "Too few frames were written.");
                Assert.That(
                    peakTwist,
                    Is.GreaterThan(10f),
                    "The capture never caught the water winding up.");
            }
            finally
            {
                camera.targetTexture = previousTarget;
                if (root.Weather != null)
                {
                    root.Weather.enabled = weatherWasEnabled;
                }

                GraphicsEffectsSettings.IntoxicationLensFxEnabled =
                    lensFxWasEnabled;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(buffer);
                File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString());
            }
        }
    }
}
