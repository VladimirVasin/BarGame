using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        // Opt into visual evidence within the focused bed regression. Normal
        // test runs create no screenshots and never start an extra opening.
        internal static bool CaptureHomeBedEnabled =>
            Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_HOME_BED") == "1";

        private static HomeBedSceneCapture homeBedSceneCapture;

        internal static IEnumerator BeginHomeBedScene(HomeInteriorRoot home, string name)
        {
            if (!CaptureHomeBedEnabled) yield break;
            CompleteHomeBedScene();
            string audioFolder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "HomeBed", "audio-cues");
            Directory.CreateDirectory(audioFolder);
            foreach (RetroSfxId cueId in new[] { RetroSfxId.HomeBedCloth, RetroSfxId.HomeBedSettle,
                         RetroSfxId.HomeBedCreak, RetroSfxId.HomeBedInhale, RetroSfxId.HomeBedExhale, RetroSfxId.HomeBedGasp })
            {
                AudioClip cue = home.Audio.GetClip(cueId);
                var pcm = new float[cue.samples * cue.channels];
                Assert.That(cue.GetData(pcm, 0), Is.True);
                WritePcmWave(Path.Combine(audioFolder, cueId + ".wav"), pcm, cue.frequency, cue.channels, 1f);
            }
            float volume = AudioListener.volume, captureStep = Time.captureDeltaTime;
            bool started = false;
            try
            {
                Time.captureDeltaTime = 1f / 24f;
                Assert.That(AudioRenderer.Start(), Is.True, "Bed sound review needs native offline rendering.");
                started = true;
                AudioListener.volume = 1f;
                // Prime Unity's first offline session before recording: its
                // first native buffer can be empty even with playing sources.
                for (int frame = 0; frame < 8; frame++)
                {
                    yield return null;
                    int count = Mathf.Max(0, AudioRenderer.GetSampleCountForCaptureFrame());
                    using var buffer = new Unity.Collections.NativeArray<float>(count * 2,
                        Unity.Collections.Allocator.Temp);
                    AudioRenderer.Render(buffer);
                }
            }
            finally
            {
                AudioListener.volume = 0f;
                if (started) AudioRenderer.Stop();
                AudioListener.volume = volume;
                Time.captureDeltaTime = captureStep;
            }
            homeBedSceneCapture = home.gameObject.AddComponent<HomeBedSceneCapture>();
            homeBedSceneCapture.Initialize(home, name);
        }

        internal static void CompleteHomeBedScene()
        {
            if (homeBedSceneCapture == null) return;
            homeBedSceneCapture.Complete();
            Object.DestroyImmediate(homeBedSceneCapture);
            homeBedSceneCapture = null;
        }

        internal static IEnumerator VerifyHomeBedCameraReturn(HomeInteriorRoot home)
        {
            // UnityTest coroutines resume before LateUpdate. This probe reads
            // the final camera and final posed head together after all owners,
            // rather than comparing today's target with yesterday's lens.
            var probe = home.gameObject.AddComponent<HomeBedReturnCameraProbe>();
            probe.Initialize(home);
            float deadline = Time.realtimeSinceStartup + (CaptureHomeBedEnabled ? 45f : 12f);
            int lastFrame = -1, returnSamples = 0, heldSamples = 0;
            Pose previous = default;
            float previousFov = 0f;
            bool hasPrevious = false;
            try
            {
                while (heldSamples < 2 && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    if (probe.Frame == lastFrame || probe.Frame < 0) continue;
                    lastFrame = probe.Frame;
                    Assert.That(probe.Phase, Is.EqualTo(HomeSleepPhase.Returning).Or.EqualTo(HomeSleepPhase.AwaitingStandUp),
                        "The camera return must end at the seated wait, with the bed still owning the hero.");
                    if (probe.Phase == HomeSleepPhase.Returning) returnSamples++;
                    else heldSamples++;
                    Assert.That(probe.HeadViewport.z, Is.GreaterThan(probe.NearPlane),
                        "The hero's head must remain in front of the actual returning lens.");
                    Assert.That(probe.HeadViewport.x, Is.InRange(.025f, .975f),
                        "Camera return loses the seated hero horizontally.");
                    Assert.That(probe.HeadViewport.y, Is.InRange(.025f, .975f),
                        "Camera return loses the seated hero vertically.");
                    if (hasPrevious)
                    {
                        float dt = Mathf.Max(probe.DeltaTime, .001f);
                        Assert.That(Vector3.Distance(previous.position, probe.Pose.position),
                            Is.LessThan(.04f + dt * 6f),
                            "The rendered return must stay continuous through its final gameplay handoff.");
                        Assert.That(Quaternion.Angle(previous.rotation, probe.Pose.rotation),
                            Is.LessThan(1f + dt * 100f),
                            "The rendered camera must not snap its aim at the seated-wait boundary.");
                        Assert.That(Mathf.Abs(previousFov - probe.FieldOfView),
                            Is.LessThan(.3f + dt * 12f),
                            "The returning lens must not jump when gameplay framing resumes.");
                    }
                    previous = probe.Pose; previousFov = probe.FieldOfView; hasPrevious = true;
                }
                Assert.That(returnSamples, Is.GreaterThan(2), "Inspect the moving return, not only its endpoint.");
                Assert.That(heldSamples, Is.EqualTo(2), "Inspect both sides of the final rendered camera handoff.");
            }
            finally { Object.DestroyImmediate(probe); }
        }

        private static readonly Dictionary<string, HomeBedMotionCapture> HomeBedMotionCaptures =
            new Dictionary<string, HomeBedMotionCapture>();

        // Call for each rendered transition frame, then Complete in a finally.
        // elapsedSeconds is the interaction's game time, not wall-clock time:
        // ffconcat preserves motion speed even when tests use Time.timeScale=2.
        internal static void CaptureHomeBedMotionFrame(
            HomeInteriorRoot home, string clipName, float elapsedSeconds)
        {
            if (!CaptureHomeBedEnabled)
            {
                return;
            }
            Assert.That(clipName, Is.EqualTo("BedEnter").Or.EqualTo("BedExit"));
            if (!HomeBedMotionCaptures.TryGetValue(clipName, out HomeBedMotionCapture capture))
            {
                capture = new HomeBedMotionCapture(clipName);
                HomeBedMotionCaptures.Add(clipName, capture);
            }
            capture.Capture(home, elapsedSeconds);
        }

        internal static void CompleteHomeBedMotion(string clipName)
        {
            if (!HomeBedMotionCaptures.TryGetValue(clipName, out HomeBedMotionCapture capture))
            {
                return;
            }
            try
            {
                capture.Complete();
            }
            finally
            {
                HomeBedMotionCaptures.Remove(clipName);
                capture.Dispose();
            }
        }

        private sealed class HomeBedMotionCapture : IDisposable
        {
            private const int PanelWidth = 640;
            private const int PanelHeight = 360;
            private readonly string folder;
            private readonly string concatPath;
            private readonly RenderTexture target = new RenderTexture(PanelWidth, PanelHeight, 24);
            private readonly Texture2D buffer = new Texture2D(
                PanelWidth * 2, PanelHeight, TextureFormat.RGB24, false);
            private readonly StringBuilder concat = new StringBuilder("ffconcat version 1.0\n");
            private int frameCount;
            private float previousTime;
            private string pendingFrame;

            internal HomeBedMotionCapture(string clipName)
            {
                folder = Path.Combine(Directory.GetCurrentDirectory(),
                    "Captures", "HomeBed", "motion", clipName);
                Directory.CreateDirectory(folder);
                concatPath = Path.Combine(folder, "frames.ffconcat");
                File.WriteAllText(concatPath, concat.ToString());
                Debug.Log($"Home bed motion capture started: {folder}");
            }

            internal void Capture(HomeInteriorRoot home, float elapsedSeconds)
            {
                Assert.That(float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds), Is.False);
                // Several rendered frames can share an identical held clip
                // sample. Its full duration belongs to the preceding picture.
                if (pendingFrame != null && elapsedSeconds <= previousTime)
                {
                    return;
                }
                Camera camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                Vector3 previousPosition = camera.transform.position;
                Quaternion previousRotation = camera.transform.rotation;
                float previousFov = camera.fieldOfView;
                RenderTexture previousTarget = camera.targetTexture;
                RenderTexture previousActive = RenderTexture.active;
                Rect bed = home.BedInteractionPlan.BedBounds;
                Vector3 center = home.Room.TransformPoint(
                    new Vector3(bed.center.x, 0f, bed.center.y));
                string frameName = $"frame-{frameCount:00000}.png";
                try
                {
                    camera.targetTexture = target;
                    camera.fieldOfView = 55f;
                    CapturePanel(camera, center + new Vector3(0.5f, 2.15f, -3f),
                        center + Vector3.up * 0.95f, 0);
                    CapturePanel(camera, center + new Vector3(3f, 1.55f, -0.7f),
                        center + new Vector3(0.35f, 0.85f, -0.35f), PanelWidth);
                    buffer.Apply();
                    File.WriteAllBytes(Path.Combine(folder, frameName), buffer.EncodeToPNG());
                    if (pendingFrame != null)
                    {
                        AppendPending(elapsedSeconds - previousTime);
                    }
                    pendingFrame = frameName;
                    previousTime = elapsedSeconds;
                    frameCount++;
                    // Keep all completed intervals readable if an assertion
                    // aborts the run before its caller reaches Complete.
                    File.WriteAllText(concatPath, concat.ToString());
                }
                finally
                {
                    camera.targetTexture = previousTarget;
                    RenderTexture.active = previousActive;
                    camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                    camera.fieldOfView = previousFov;
                }
            }

            private void CapturePanel(Camera camera, Vector3 position, Vector3 lookAt, int offsetX)
            {
                camera.transform.SetPositionAndRotation(position,
                    Quaternion.LookRotation(lookAt - position, Vector3.up));
                camera.Render();
                RenderTexture.active = target;
                buffer.ReadPixels(new Rect(0, 0, PanelWidth, PanelHeight), offsetX, 0);
            }

            private void AppendPending(float duration)
            {
                concat.Append("file '").Append(pendingFrame).Append("'\n");
                concat.Append("option framerate 1000\n");
                concat.Append("duration ").Append(duration.ToString("0.######",
                    CultureInfo.InvariantCulture)).Append('\n');
            }

            internal void Complete()
            {
                if (pendingFrame != null)
                {
                    AppendPending(1f / 30f);
                    // The concat demuxer needs the terminal file repeated to
                    // retain its final duration. Earlier stale PNGs are never
                    // referenced, so repeated runs require no recursive delete.
                    concat.Append("file '").Append(pendingFrame).Append("'\n");
                    concat.Append("option framerate 1000\n");
                    File.WriteAllText(concatPath, concat.ToString());
                }
                Debug.Log($"Home bed motion wrote {frameCount} frames: {concatPath}");
            }

            public void Dispose()
            {
                Object.DestroyImmediate(buffer);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        internal static void CaptureHomeBedFrame(HomeInteriorRoot home, string name)
        {
            if (!CaptureHomeBedEnabled)
            {
                return;
            }

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            float previousFov = camera.fieldOfView;
            Rect bed = home.BedInteractionPlan.BedBounds;
            Vector3 center = home.Room.TransformPoint(
                new Vector3(bed.center.x, 0f, bed.center.y));
            try
            {
                camera.transform.position = center + new Vector3(0.5f, 2.15f, -3f);
                camera.transform.rotation = Quaternion.LookRotation(
                    center + Vector3.up * 0.95f - camera.transform.position,
                    Vector3.up);
                camera.fieldOfView = 55f;
                CaptureCurrentCamera(camera, "HomeBed", name);

                if (name.StartsWith("10-BedEnter-", StringComparison.Ordinal) ||
                    name.StartsWith("30-BedExit-", StringComparison.Ordinal))
                {
                    // The foot-end view exposes knee and spinal flexion that
                    // the ordinary door-side view foreshortens while seated.
                    camera.transform.position = center + new Vector3(3f, 1.55f, -0.7f);
                    camera.transform.rotation = Quaternion.LookRotation(
                        center + new Vector3(0.35f, 0.85f, -0.35f) -
                        camera.transform.position,
                        Vector3.up);
                    CaptureCurrentCamera(camera, "HomeBed", name + "-side");
                }

                if (name == "00-rest" || name == "20-sleep" || name == "41-recovered")
                {
                    Transform pillow = home.Room.Find("Home Pillow");
                    Assert.That(pillow, Is.Not.Null);
                    Vector3 target = pillow.position;
                    camera.transform.position = target + new Vector3(0.65f, 0.70f, -1.2f);
                    camera.transform.rotation = Quaternion.LookRotation(
                        target - camera.transform.position, Vector3.up);
                    camera.fieldOfView = 52f;
                    CaptureCurrentCamera(camera, "HomeBed", name + "-pillow");
                }
            }
            finally
            {
                camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                camera.fieldOfView = previousFov;
            }
        }

        internal static IEnumerator CaptureHomeBedOpening(HomeInteriorRoot home)
        {
            if (!CaptureHomeBedEnabled)
            {
                yield break;
            }

            // Use the production controller for both its clock shot and moving
            // wake camera. No copied private camera coordinates can go stale.
            var openingObject = new GameObject("Home Bed Opening Capture");
            HomeOpeningController opening = openingObject.AddComponent<HomeOpeningController>();
            home.AlarmClock.StopFollowingSessionTime();
            try
            {
                opening.Initialize(home);
                yield return null;
                yield return null;
                Assert.That(home.AlarmClock.DisplayedTime, Is.EqualTo("05:59"));
                CaptureCurrentCamera(Camera.main, "HomeBed", "50-opening-clock-0559");

                float deadline = Time.realtimeSinceStartup + 22f;
                while (opening.Phase != HomeOpeningPhase.AwaitingWake &&
                       Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
                Assert.That(opening.TryWake(), Is.True);

                float[] wakeSeconds = { 0.2f, 0.85f, 1.5f, 2.3f, 4f, 6.6f };
                int nextShot = 0;
                while (opening.Phase != HomeOpeningPhase.Complete &&
                       Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    if (opening.Phase == HomeOpeningPhase.Waking &&
                        nextShot < wakeSeconds.Length &&
                        opening.Timeline.PhaseElapsedSeconds >= wakeSeconds[nextShot])
                    {
                        CaptureCurrentCamera(Camera.main, "HomeBed",
                            $"51-opening-wake-{nextShot:00}");
                        nextShot++;
                    }
                }
                Assert.That(opening.Phase, Is.EqualTo(HomeOpeningPhase.Complete));
                yield return null;
                CaptureCurrentCamera(Camera.main, "HomeBed", "52-opening-gameplay");
            }
            finally
            {
                // Disabling an incomplete opening follows its real owned
                // cleanup path, including clip, clock and camera restoration.
                Object.DestroyImmediate(openingObject);
                home.AlarmClock.FollowSessionTime();
            }
        }
    }

    [DefaultExecutionOrder(11000)]
    internal sealed class HomeBedReturnCameraProbe : MonoBehaviour
    {
        private HomeInteriorRoot home;
        private Camera camera;
        private Transform head;
        internal int Frame { get; private set; } = -1;
        internal HomeSleepPhase Phase { get; private set; }
        internal Pose Pose { get; private set; }
        internal Vector3 HeadViewport { get; private set; }
        internal float FieldOfView { get; private set; }
        internal float NearPlane { get; private set; }
        internal float DeltaTime { get; private set; }

        internal void Initialize(HomeInteriorRoot owner)
        {
            home = owner; camera = Camera.main;
            head = ((Player3DCharacterPresentation)home.Player.Visual).Registry.Anchors.Head;
        }

        private void LateUpdate()
        {
            if (home == null || camera == null || head == null) return;
            Phase = home.Sleep.Phase;
            Pose = new Pose(camera.transform.position, camera.transform.rotation);
            FieldOfView = camera.fieldOfView; NearPlane = camera.nearClipPlane;
            HeadViewport = camera.WorldToViewportPoint(head.position);
            DeltaTime = Time.deltaTime; Frame = Time.frameCount;
        }
    }

    // Test-only, opt-in presentation evidence. The camera is the actual live
    // shot (no diagnostic repositioning); the two-angle contact video remains
    // a separate capture above. Camera.Render excludes OnGUI/UI overlays.
    [DefaultExecutionOrder(10000)]
    internal sealed class HomeBedSceneCapture : MonoBehaviour
    {
        private const int Rate = 24;
        private HomeInteriorRoot home;
        private Camera camera;
        private RenderTexture target;
        private Texture2D pixels;
        private BinaryWriter wave;
        private StreamWriter concat;
        private string folder;
        private float previousVolume, previousCaptureStep;
        private int frames, images, samples, sampleRate;
        private bool active;
        private HomeSleepPhase previousPhase;

        internal void Initialize(HomeInteriorRoot owner, string name)
        {
            home = owner; camera = Camera.main;
            previousVolume = AudioListener.volume;
            previousCaptureStep = Time.captureDeltaTime;
            sampleRate = Mathf.Max(8000, AudioSettings.outputSampleRate);
            folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "HomeBed", name);
            Directory.CreateDirectory(folder);
            target = new RenderTexture(640, 360, 24);
            pixels = new Texture2D(640, 360, TextureFormat.RGB24, false);
            concat = new StreamWriter(Path.Combine(folder, "frames.ffconcat"));
            concat.WriteLine("ffconcat version 1.0");
            wave = new BinaryWriter(File.Create(Path.Combine(folder, "scene.wav")));
            WriteWaveHeader(0);
            Time.captureDeltaTime = 1f / Rate;
            if (!AudioRenderer.Start())
            {
                Complete();
                throw new InvalidOperationException("Bed scene capture could not acquire offline audio.");
            }
            active = true;
            AudioListener.volume = 1f;
            previousPhase = home.Sleep.Phase;
            Debug.Log("Home bed scene review started: " + name);
        }

        private void LateUpdate()
        {
            if (!active) return;
            if (frames >= Rate * 100 || camera == null || home == null)
            { Complete(); return; }
            if (previousPhase != home.Sleep.Phase)
            {
                previousPhase = home.Sleep.Phase;
                Debug.Log("Home bed scene review: " + previousPhase);
            }
            int count = Mathf.Max(0, AudioRenderer.GetSampleCountForCaptureFrame());
            using (var buffer = new Unity.Collections.NativeArray<float>(count * 2,
                Unity.Collections.Allocator.Temp))
            {
                bool rendered = AudioRenderer.Render(buffer);
                if (rendered)
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        wave.Write((short)(Mathf.Clamp(buffer[i], -1f, 1f) * short.MaxValue));
                        samples++;
                    }
            }
            if (frames % 2 == 0) CaptureFrame();
            frames++;
        }

        private void CaptureFrame()
        {
            RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0f, 0f, 640f, 360f), 0, 0);
                pixels.Apply();
                string name = "frame-" + images.ToString("00000") + ".png";
                File.WriteAllBytes(Path.Combine(folder, name), pixels.EncodeToPNG());
                concat.WriteLine("file '" + name + "'");
                concat.WriteLine("option framerate 1000");
                concat.WriteLine("duration " + (2f / Rate).ToString("0.######", CultureInfo.InvariantCulture));
                images++;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
            }
        }

        private void WriteWaveHeader(int sampleCount)
        {
            wave.Write(Encoding.ASCII.GetBytes("RIFF")); wave.Write(36 + sampleCount * 2);
            wave.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); wave.Write(16);
            wave.Write((short)1); wave.Write((short)2); wave.Write(sampleRate);
            wave.Write(sampleRate * 4); wave.Write((short)4); wave.Write((short)16);
            wave.Write(Encoding.ASCII.GetBytes("data")); wave.Write(sampleCount * 2);
        }

        internal void Complete()
        {
            AudioListener.volume = 0f;
            if (active) AudioRenderer.Stop();
            active = false;
            if (wave != null)
            {
                wave.Seek(0, SeekOrigin.Begin); WriteWaveHeader(samples); wave.Dispose(); wave = null;
            }
            if (concat != null)
            {
                if (images > 0) concat.WriteLine("file 'frame-" + (images - 1).ToString("00000") + ".png'");
                concat.Dispose(); concat = null;
            }
            if (target != null) { target.Release(); DestroyImmediate(target); target = null; }
            if (pixels != null) { DestroyImmediate(pixels); pixels = null; }
            Time.captureDeltaTime = previousCaptureStep;
            AudioListener.volume = previousVolume;
            if (samples > 0) Debug.Log("Home bed scene review complete: " + images + " frames / " + samples + " stereo samples.");
        }

        private void OnDestroy() => Complete();
    }

}
