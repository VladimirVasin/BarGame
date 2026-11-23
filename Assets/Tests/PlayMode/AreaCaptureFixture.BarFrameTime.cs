using System;
using System.Collections;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Stopwatch = System.Diagnostics.Stopwatch;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Explicit("Measure the populated production bar from a fixed camera.")]
        [PrebuildSetup(typeof(CompactBarAssetsSetup))]
        public IEnumerator BarFullRoomFrameTime()
        {
            const int warmup = 120, frames = 180, windows = 3;
            var originalRandom = UnityEngine.Random.state;
            int originalVsync = QualitySettings.vSyncCount, originalCap = Application.targetFrameRate;
            float originalStep = Time.captureDeltaTime;
            Camera camera = null;
            RenderTexture previousTarget = null, target = null;
            PlayerCameraFollow follow = null;
            bool followWasEnabled = false;
            Pose initialPose = default;
            float originalFov = 0;
            int draws = 0;
            void CountDraw(ScriptableRenderContext _, Camera rendered) { if (rendered == camera) draws++; }
            try
            {
                GameSessionState.BeginNewGame("pub-timing");
                UnityEngine.Random.InitState(71921);
                BarTestTime.StartFullAttendance("pub-timing");
                GameSessionState.EnterBar("pub-timing", BarActivityKind.SplitTheG, CityDistrictKind.Residential);
                yield return SceneManager.LoadSceneAsync(SceneIds.BarInterior, LoadSceneMode.Single);
                var bar = Object.FindAnyObjectByType<BarInteriorRoot>();
                for (int f = 0; f < 600 && (bar == null || !bar.IsInitialized); f++) yield return null;
                Assert.That(bar != null && bar.IsInitialized, Is.True);
                Debug.Log("PUB_GEOMETRY triangles=" + Resources.Load<GameObject>("Bar/BarInterior3D").GetComponent<BarAssetRegistry>().SourceTriangleCount);
                bar.ArrivalPresentation.Skip();
                camera = Camera.main;
                follow = camera.GetComponent<PlayerCameraFollow>();
                followWasEnabled = follow.enabled;
                initialPose = new Pose(camera.transform.position, camera.transform.rotation);
                originalFov = camera.fieldOfView;
                previousTarget = camera.targetTexture;
                target = new RenderTexture(1280, 720, 24);
                Assert.That(target.Create(), Is.True);
                follow.enabled = false;
                camera.targetTexture = target;
                camera.transform.position = bar.Room.TransformPoint(new Vector3(3.1f, 1.83f, -3.31f));
                camera.transform.LookAt(bar.Room.TransformPoint(new Vector3(-1.89f, 1.07f, 1.20f)), bar.Room.up);
                camera.fieldOfView = 73f;
                Application.targetFrameRate = -1;
                QualitySettings.vSyncCount = 0;
                Time.captureDeltaTime = 1f / 60f;
                RenderPipelineManager.endCameraRendering += CountDraw;
                for (int i = 0; i < warmup; i++) yield return null;
                Assert.That(bar.Patrons.Count, Is.EqualTo(8));
                var samples = new double[frames];
                for (int window = 0; window < windows; window++)
                {
                    int startDraw = draws;
                    long previous = Stopwatch.GetTimestamp();
                    for (int i = 0; i < frames; i++)
                    {
                        yield return null;
                        long now = Stopwatch.GetTimestamp();
                        samples[i] = (now - previous) * 1000d / Stopwatch.Frequency;
                        previous = now;
                    }
                    Assert.That(draws - startDraw, Is.InRange(frames-2, frames+2), "The game camera must actually draw each sampled frame.");
                    Assert.That(bar.Patrons.Count, Is.EqualTo(8));
                    Array.Sort(samples);
                    Debug.Log(string.Format(CultureInfo.InvariantCulture,
                        "PUB_FRAME window={0} n={1} median_ms={2:F3} p95_ms={3:F3} guests={4} gpu={5}",
                        window+1, frames, samples[frames/2], samples[(int)Math.Ceiling(frames*.95)-1],
                        bar.Patrons.Count, SystemInfo.graphicsDeviceName));
                }
            }
            finally
            {
                RenderPipelineManager.endCameraRendering -= CountDraw;
                Application.targetFrameRate = originalCap;
                QualitySettings.vSyncCount = originalVsync;
                Time.captureDeltaTime = originalStep;
                UnityEngine.Random.state = originalRandom;
                if (camera != null)
                {
                    camera.targetTexture = previousTarget;
                    camera.fieldOfView = originalFov;
                    camera.transform.SetPositionAndRotation(initialPose.position, initialPose.rotation);
                }
                if (follow != null) follow.enabled = followWasEnabled;
                if (target != null) { target.Release(); Object.Destroy(target); }
            }
        }
    }
}
