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
}
