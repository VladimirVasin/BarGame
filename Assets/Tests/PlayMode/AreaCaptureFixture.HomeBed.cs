using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        // Opt into visual evidence within the focused bed regression. Normal
        // test runs create no screenshots and never start an extra opening.
        private static bool CaptureHomeBedEnabled =>
            Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_HOME_BED") == "1";

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
