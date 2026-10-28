using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        // Routine regression runs keep no frames. Opt in for visual review.
        internal static bool CafeRelocationFramesRequested =>
            System.Environment.GetEnvironmentVariable("BARGAME_CAFE_RELOCATION_CAPTURE") == "1";

        internal static IEnumerator CaptureRelocatedCafe(CityGameRoot city, string phase)
        {
            if (!CafeRelocationFramesRequested) yield break;
            MountainRoadCafePlan plan = city.World.Cafe.Plan;
            Camera camera = city.Camera;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            Pose saved = new Pose(camera.transform.position, camera.transform.rotation);
            float savedFov = camera.fieldOfView;
            bool following = follow.enabled;
            var hidden = new List<Renderer>();
            foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled) { renderer.enabled = false; hidden.Add(renderer); }
            try
            {
                follow.enabled = false;
                Vector3 outside = plan.DoorForward;
                Vector3 eye = Vector3.up * 1.7f;
                Shot[] shots =
                {
                    Shot.At("diner-" + phase + "-approach", plan.DoorCenter + outside * 9f + eye,
                        plan.Center + Vector3.up * 1.6f, 72f),
                    Shot.At("diner-" + phase + "-glass-corner", plan.DoorCenter + outside * 6f + plan.Right * 9f + eye,
                        plan.Center + Vector3.up * 1.4f, 68f),
                    Shot.At("diner-" + phase + "-entrance", plan.DoorCenter + outside * 3f + eye,
                        plan.DoorCenter - outside * 2f + Vector3.up * 1.3f, 68f),
                    Shot.At("diner-" + phase + "-counter", plan.Center - plan.Forward * 3.8f + eye,
                        plan.Center + plan.Forward + Vector3.up * 1.35f, 76f)
                };
                foreach (Shot shot in shots)
                {
                    camera.transform.SetPositionAndRotation(shot.Position,
                        Quaternion.LookRotation(shot.Target - shot.Position, Vector3.up));
                    camera.fieldOfView = shot.FieldOfView;
                    yield return null;
                    yield return null;
                    CaptureCurrentCamera(camera, SceneIds.City, shot.Name);
                }
            }
            finally
            {
                foreach (Renderer renderer in hidden) if (renderer != null) renderer.enabled = true;
                camera.transform.SetPositionAndRotation(saved.position, saved.rotation);
                camera.fieldOfView = savedFov;
                follow.enabled = following;
            }
        }

        internal static IEnumerator CaptureTerminalAfterCafeRelocation()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MountainRoad, LoadSceneMode.Single);
            MountainRoadRoot road = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while ((road == null || !road.IsInitialized) && Time.realtimeSinceStartup < deadline)
            {
                road = Object.FindAnyObjectByType<MountainRoadRoot>();
                yield return null;
            }
            Assert.That(road, Is.Not.Null);
            Assert.That(road.IsInitialized, Is.True);
            Assert.That(road.World.Cafe, Is.Null);
            Assert.That(road.Plan.Terminal.Cafe, Is.Null);
            Assert.That(road.Plan.Terminal.Site.GetCount(MountainRoadSiteGroup.CafeThreshold), Is.Zero);
            Assert.That(road.Seats.Count, Is.EqualTo(1));
            Assert.That(road.World.Cableway, Is.Not.Null);
            Assert.That(Object.FindAnyObjectByType<MountainRoadCafeCastController>(), Is.Null);
            if (CafeRelocationFramesRequested)
            {
                Camera.main.GetComponent<PlayerCameraFollow>().enabled = false;
                Vector3 center = road.Plan.Plateau.Center;
                Vector3 forward = road.Plan.Plateau.Forward;
                Vector3 right = road.Plan.Plateau.Right;
                road.Player.Motor.Teleport(center + Vector3.up * PlayerFactory.GroundedRootOffset);
                Shot[] shots =
                {
                    Shot.At("diner-relocated-terminal", center - forward * 12f + Vector3.up * 1.7f,
                        center + forward * 6f - right * 7f + Vector3.up, 78f),
                    Shot.At("diner-relocated-former-site", center - right * 11f - forward * 2f + Vector3.up * 1.7f,
                        center - right * 13f + forward * 9f + Vector3.up, 72f)
                };
                foreach (Shot shot in shots)
                {
                    Camera camera = Camera.main;
                    camera.transform.SetPositionAndRotation(shot.Position,
                        Quaternion.LookRotation(shot.Target - shot.Position, Vector3.up));
                    camera.fieldOfView = shot.FieldOfView;
                    for (int frame = 0; frame < SettleFrames; frame++) yield return null;
                    CaptureCurrentCamera(camera, SceneIds.MountainRoad, shot.Name);
                }
            }
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
        }
    }
}
