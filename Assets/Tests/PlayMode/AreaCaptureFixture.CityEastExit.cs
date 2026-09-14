using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("East road ground, closed civilian post, map boundary and distant mainland day/night views.")]
        [PrebuildSetup(typeof(EastGuardAssetsSetup))]
        public IEnumerator CityEastExit()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime((float)((12d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                GameTimeState.GameMinutesPerRealSecond));
            CityGameRoot city = null;
            yield return Capture(SceneIds.City, () =>
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                return city != null && city.IsInitialized ? city : null;
            }, () =>
            {
                CityEastExitPlan p = CityEastExitPlanner.Create(city.Layout);
                Vector3 eye = new Vector3(p.ApproachStart.x + 20f,
                    p.SampleRoadTop(p.ApproachStart.x + 20f) + EyeHeight, p.ApproachStart.z);
                Vector3 postEye = p.CheckpointPosition + new Vector3(-9f, EyeHeight, 1.7f);
                Vector3 roadEye = p.CheckpointPosition + new Vector3(-2f, EyeHeight, 0f);
                return new[]
                {
                    Shot.At("east-exit-00-road-approach", eye, eye + Vector3.right * 80f),
                    Shot.At("east-exit-00-post-first", postEye, p.CheckpointPosition + Vector3.up * EyeHeight),
                    Shot.At("east-exit-00-horizon-first", roadEye, p.RoadEnd + new Vector3(12000f, 90f, 0f))
                };
            });

            CityEastExitPlan plan = CityEastExitPlanner.Create(city.Layout);
            var landing = new CityMapCityTeleportGround(city.Layout);
            Assert.That(plan.IsEnabled, Is.True);
            Assert.That(plan.RoadBounds.yMin, Is.GreaterThan(city.World.ChurchCourtyardPlan.Grounds.yMax),
                "The road remains north of the existing church and its garden.");
            Assert.That(Camera.main.farClipPlane, Is.EqualTo(48f).Within(.001f));
            Assert.That(RenderSettings.fogDensity, Is.EqualTo(.070f).Within(.0001f));
            Assert.That(landing.TryResolveStandingPosition(new Vector2(
                city.World.ChurchPlan.DoorGroundPosition.x - 2f,
                city.World.ChurchPlan.DoorGroundPosition.z), out _), Is.True);
            Physics.SyncTransforms();
            VerifyEastDressing(city, plan, landing);
            for (float x = plan.ApproachStart.x + 1f; x < plan.CheckpointPosition.x - 1.5f; x += 3f)
            {
                Assert.That(landing.TryResolveStandingPosition(new Vector2(x, plan.ApproachStart.z),
                    out Vector3 standing), Is.True, "Map lost the actual road approach at X=" + x);
                Assert.That(Physics.Raycast(new Vector3(x, plan.SampleRoadTop(x) + 5f,
                    plan.ApproachStart.z), Vector3.down, out RaycastHit hit, 8f,
                    ~0, QueryTriggerInteraction.Ignore), Is.True);
                Assert.That(hit.point.y, Is.EqualTo(plan.SampleRoadTop(x)).Within(.085f),
                    "The visible road and its standing surface must coincide at X=" + x);
            }
            foreach (Vector2 point in new[]
            {
                new Vector2(plan.CheckpointPosition.x + 2f, plan.CheckpointPosition.z),
                new Vector2(plan.CheckpointPosition.x + 2f, plan.YardBounds.yMin + 2f),
                new Vector2(plan.CheckpointPosition.x + 2f, plan.NorthYardBounds.yMax - 2f)
            })
                Assert.That(landing.TryResolveStandingPosition(point, out _), Is.False,
                    "The map cannot bypass the closed post: " + point);
            // The map's existing outside-map contract may clamp a request to
            // the nearest safe point, including the church outside the
            // southern return fence. It must not enter the closed yard.
            if (landing.TryResolveStandingPosition(new Vector2(plan.RoadEnd.x + 10f,
                plan.CheckpointPosition.z), out Vector3 clamped))
                Assert.That(plan.ClosedGroundBounds.Contains(new Vector2(clamped.x, clamped.z)), Is.False,
                    "An out-of-map request cannot place the hero beyond the checkpoint.");
            Assert.That(Physics.Raycast(plan.CheckpointPosition + new Vector3(-2f, .7f, 0f),
                Vector3.right, out _, 4f, ~0, QueryTriggerInteraction.Ignore), Is.True,
                "The closed barrier needs its actual physical lower grille.");

            // Sweep the production motor across the road joins. This proves
            // that a collection of height samples is also a walkable route.
            var motor = city.Player.Motor;
            motor.SetInputEnabled(false);
            Assert.That(landing.TryResolveStandingPosition(new Vector2(plan.ApproachStart.x + 1f,
                plan.ApproachStart.z), out Vector3 start), Is.True);
            motor.Teleport(start);
            yield return null;
            Vector3 stop = new Vector3(plan.CheckpointPosition.x - 2f,
                plan.SampleRoadTop(plan.CheckpointPosition.x - 2f), plan.CheckpointPosition.z);
            bool arrived = false;
            for (int frame = 0; frame < 160 && !arrived; frame++)
            {
                arrived = motor.MoveTowardsApproachWaypoint(stop, .10f, .35f);
                yield return null;
            }
            motor.CancelInteractionPoseMove();
            Assert.That(arrived, Is.True, "Physical road traversal stopped at " + motor.transform.position);

            GameObject panorama = GameObject.Find(CityEastDistanceWorldBuilder.ObjectName);
            Assert.That(panorama, Is.Not.Null);
            Assert.That(panorama.GetComponentsInChildren<Collider>(), Is.Empty);
            Assert.That(panorama.GetComponentsInChildren<Light>(), Is.Empty);
            MeshFilter skyline = Array.Find(panorama.GetComponentsInChildren<MeshFilter>(),
                mesh => mesh.name.StartsWith("DistanceCity", StringComparison.Ordinal));
            Assert.That(skyline, Is.Not.Null);
            Bounds sourceBounds = skyline.sharedMesh.bounds;
            Bounds measured = new Bounds(skyline.transform.TransformPoint(sourceBounds.center), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
                measured.Encapsulate(skyline.transform.TransformPoint(new Vector3(
                    (corner & 1) == 0 ? sourceBounds.min.x : sourceBounds.max.x,
                    (corner & 2) == 0 ? sourceBounds.min.y : sourceBounds.max.y,
                    (corner & 4) == 0 ? sourceBounds.min.z : sourceBounds.max.z)));
            Vector3 minimum = measured.min;
            Vector3 maximum = measured.max;
            Debug.Log("EAST DISTANCE imported world bounds: " + measured + "; root " + panorama.transform.rotation.eulerAngles);
            Assert.That(minimum.x - plan.RoadEnd.x, Is.GreaterThan(9000f),
                "The imported source describes a far city, preserving metre scale and the east axis.");
            Assert.That(maximum.z - minimum.z, Is.GreaterThan(5000f));
            Assert.That(maximum.y - minimum.y, Is.InRange(100f, 500f));
            Assert.That(minimum.y, Is.GreaterThan(plan.RoadEnd.y), "The skyline must rise above its ground datum.");

            Camera camera = Camera.main;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool oldFollow = follow != null && follow.enabled;
            Vector3 oldPosition = camera.transform.position;
            Quaternion oldRotation = camera.transform.rotation;
            float oldFov = camera.fieldOfView;
            Renderer[] hero = city.Player.GameObject.GetComponentsInChildren<Renderer>();
            bool[] enabled = Array.ConvertAll(hero, renderer => renderer.enabled);
            try
            {
                if (follow != null) follow.enabled = false;
                foreach (Renderer renderer in hero) renderer.enabled = false;
                Vector2 firstShed = new Vector2(plan.YardBounds.xMin + 32.4f,
                    Mathf.Lerp(plan.YardBounds.yMin + 12f, plan.YardBounds.yMax - 12f, .18f));
                for (int phase = 0; phase < 2; phase++)
                {
                    if (phase == 1)
                        GameSessionState.AdvanceGameTime((float)((21d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                            GameTimeState.GameMinutesPerRealSecond));
                    city.DayNight.ApplyCurrentTime(true);
                    string light = phase == 0 ? "day" : "night";
                    yield return EastView("01-checkpoint-" + light,
                        plan.CheckpointPosition.x - 11f, plan.CheckpointPosition.z + 1.7f,
                        plan.CheckpointPosition + Vector3.up * 1.7f);
                    yield return EastView("02-long-road-" + light,
                        plan.CheckpointPosition.x - 2.1f, plan.CheckpointPosition.z,
                        plan.RoadEnd + new Vector3(12000f, 90f, 0f));
                    yield return EastView("03-church-edge-" + light,
                        plan.ApproachStart.x + 30f, plan.YardBounds.yMin + 2f,
                        plan.CheckpointPosition + Vector3.up * 1.7f);
                    yield return EastView("04-north-edge-" + light,
                        plan.CheckpointPosition.x - 15f, plan.NorthYardBounds.yMin + 28f,
                        plan.RoadEnd + new Vector3(12000f, 90f, 0f));
                    yield return EastView("05-booth-side-" + light,
                        plan.CheckpointPosition.x - 9f, plan.YardBounds.yMin + 2.8f,
                        plan.BoothPosition + new Vector3(-.7f, 1.15f, -1f));
                    yield return EastView("06-oblique-approach-" + light,
                        plan.ApproachStart.x + 27f, plan.CheckpointPosition.z + 8f,
                        plan.CheckpointPosition + new Vector3(-12f, 1.1f, 0f));
                    yield return EastView("07-service-approach-" + light,
                        plan.YardBounds.xMin + 18f, plan.CheckpointPosition.z + 6f,
                        new Vector3(firstShed.x, plan.SampleGroundTop(firstShed) + .8f, firstShed.y));
                }
                yield return VerifyEastGuards(city, camera, landing);
            }
            finally
            {
                if (follow != null) follow.enabled = oldFollow;
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                camera.fieldOfView = oldFov;
                for (int i = 0; i < hero.Length; i++) hero[i].enabled = enabled[i];
                motor.SetInputEnabled(true);
            }
            Debug.Log("EAST EXIT: physical road, closed boundary, passive far mainland and day/night captures verified.");

            IEnumerator EastView(string name, float x, float z, Vector3 target)
            {
                Assert.That(landing.TryResolveStandingPosition(new Vector2(x, z), out Vector3 ground),
                    Is.True, "Capture must stand on accessible ground: " + name);
                motor.Teleport(ground);
                Vector3 eye = ground + Vector3.up * EyeHeight;
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                camera.fieldOfView = 60f;
                for (int frame = 0; frame < 4; frame++) yield return null;
                CaptureCurrentCamera(camera, SceneIds.City, "east-exit-" + name);
            }
        }
    }
}
