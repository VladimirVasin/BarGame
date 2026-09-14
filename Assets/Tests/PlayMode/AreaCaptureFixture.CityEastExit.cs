using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
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
                float approachEyeX = p.ApproachStart.x + CityEastExitPlan.CheckpointSetback * .33f;
                Vector3 eye = new Vector3(approachEyeX,
                    p.SampleRoadTop(approachEyeX) + EyeHeight, p.ApproachStart.z);
                Vector3 postEye = p.CheckpointPosition + new Vector3(-9f, EyeHeight, 1.7f);
                Vector3 roadEye = p.CheckpointPosition + new Vector3(-2f, EyeHeight, 0f);
                Vector3 junctionEye = new Vector3(p.ApproachStart.x - 3.5f,
                    p.ApproachStart.y + EyeHeight, p.ApproachStart.z + 6f);
                Vector3 repairEye = new Vector3(p.ApproachStart.x + 2f,
                    p.SampleRoadTop(p.ApproachStart.x + 2f, p.ApproachStart.z - 2f) + .65f,
                    p.ApproachStart.z - 2f);
                Vector3 repairTarget = p.ApproachStart + new Vector3(7f, .01f, -.35f);
                foreach (CityEastExitDressingPart part in CityEastExitDressingPlan.Create(p).Parts)
                    if (part.Id == "Old Road Repair") repairTarget = part.Position + Vector3.up * .01f;
                Vector2 postGround = new Vector2(p.CheckpointPosition.x - 10f, p.CheckpointPosition.z - 4.9f);
                Vector3 postGroundEye = new Vector3(postGround.x, p.SampleGroundTop(postGround) + .65f, postGround.y);
                Vector3 postGroundTarget = p.BoothPosition + new Vector3(-1.5f, .04f, 1.8f);
                Vector3 cameraShift = Vector3.forward * .002f;
                return new[]
                {
                    Shot.At("east-exit-00-city-junction", junctionEye, p.ApproachStart + new Vector3(2f, .2f, 0f)),
                    Shot.At("east-exit-00-road-approach", eye, eye + Vector3.right * 80f),
                    Shot.At("east-exit-00-post-first", postEye, p.CheckpointPosition + Vector3.up * EyeHeight),
                    Shot.At("east-exit-00-horizon-first", roadEye, p.RoadEnd + new Vector3(12000f, 90f, 0f)),
                    // Consecutive renders share the same game time. The 2 mm
                    // translation exposes competing surfaces without actor motion.
                    Shot.At("east-exit-00-road-ground-a", repairEye, repairTarget),
                    Shot.At("east-exit-00-road-ground-b", repairEye + cameraShift, repairTarget + cameraShift),
                    Shot.At("east-exit-00-post-ground-a", postGroundEye, postGroundTarget),
                    Shot.At("east-exit-00-post-ground-b", postGroundEye + cameraShift, postGroundTarget + cameraShift)
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
            VerifyEastStreetJoin(city, plan);
            VerifyEastDressing(city, plan, landing);
            for (float x = plan.ApproachStart.x + 1f; x < plan.CheckpointPosition.x - 1.5f; x += 3f)
            {
                Assert.That(landing.TryResolveStandingPosition(new Vector2(x, plan.ApproachStart.z),
                    out Vector3 standing), Is.True, "Map lost the actual road approach at X=" + x);
                Assert.That(Physics.Raycast(new Vector3(x, plan.SampleRoadTop(x) + 5f,
                    plan.ApproachStart.z), Vector3.down, out RaycastHit hit, 8f,
                    ~0, QueryTriggerInteraction.Ignore), Is.True, "Road collision missing at X=" + x);
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

            var motor = city.Player.Motor;
            yield return VerifyEastNormalWalking(city, plan, landing);
            motor.SetInputEnabled(false);

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
                    yield return EastView("00-city-junction-" + light,
                        plan.ApproachStart.x - 3.5f, plan.ApproachStart.z + 6f,
                        plan.ApproachStart + new Vector3(2f, .2f, 0f));
                    yield return EastView("01-checkpoint-" + light,
                        plan.CheckpointPosition.x - 11f, plan.CheckpointPosition.z + 1.7f,
                        plan.CheckpointPosition + Vector3.up * 1.7f);
                    yield return EastView("02-long-road-" + light,
                        plan.CheckpointPosition.x - 2.1f, plan.CheckpointPosition.z,
                        plan.RoadEnd + new Vector3(12000f, 90f, 0f));
                    yield return EastView("03-church-edge-" + light,
                        plan.CheckpointPosition.x - 10f, plan.YardBounds.yMin + 2f,
                        plan.CheckpointPosition + Vector3.up * 1.7f);
                    yield return EastView("04-north-edge-" + light,
                        plan.CheckpointPosition.x - 15f, plan.NorthYardBounds.yMin + 28f,
                        plan.RoadEnd + new Vector3(12000f, 90f, 0f));
                    yield return EastView("05-booth-side-" + light,
                        plan.CheckpointPosition.x - 9f, plan.YardBounds.yMin + 2.8f,
                        plan.BoothPosition + new Vector3(-.7f, 1.15f, -1f));
                    yield return EastView("06-oblique-approach-" + light,
                        plan.CheckpointPosition.x - 16f, plan.CheckpointPosition.z + 8f,
                        plan.CheckpointPosition + new Vector3(-12f, 1.1f, 0f));
                    yield return EastView("07-service-approach-" + light,
                        plan.CheckpointPosition.x - 6f, plan.CheckpointPosition.z + 6f,
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

        private static void VerifyEastStreetJoin(CityGameRoot city, CityEastExitPlan exit)
        {
            Assert.That(exit.CheckpointPosition.x - exit.ApproachStart.x,
                Is.EqualTo(CityEastExitPlan.CheckpointSetback).Within(.001f),
                "The post must remain close enough to read from the main street through the unchanged city fog.");
            Assert.That(exit.RoadBounds.xMin, Is.EqualTo(exit.YardBounds.xMin).Within(.001f),
                "The side road begins at the street edge; it cannot overlay the city carriageway.");

            CityStreetSurfacePlan streets = CityStreetSurfacePlanner.Create(city.Layout);
            Rect mouth = Rect.MinMaxRect(exit.ApproachStart.x - CityStreetSurfacePlanner.SidewalkWidth + .001f,
                exit.RoadBounds.yMin + .001f, exit.ApproachStart.x - .001f, exit.RoadBounds.yMax - .001f);
            foreach (RuntimeOrientedBox sidewalk in streets.SidewalkGeometry)
            {
                Vector3 across = sidewalk.Rotation * Vector3.right * sidewalk.Size.x * .5f;
                Vector3 along = sidewalk.Rotation * Vector3.forward * sidewalk.Size.z * .5f;
                Vector3 centre = sidewalk.Center + sidewalk.Rotation * Vector3.up * sidewalk.Size.y * .5f;
                float halfX = Mathf.Abs(across.x) + Mathf.Abs(along.x);
                float halfZ = Mathf.Abs(across.z) + Mathf.Abs(along.z);
                Rect footprint = Rect.MinMaxRect(centre.x - halfX, centre.z - halfZ,
                    centre.x + halfX, centre.z + halfZ);
                Assert.That(footprint.Overlaps(mouth), Is.False,
                    "The visible raised sidewalk must stop at the road mouth; the lowered crossing remains walkable.");
            }
            foreach (Bounds marking in streets.CenterMarkings)
                Assert.That(Rect.MinMaxRect(marking.min.x, marking.min.z, marking.max.x, marking.max.z)
                    .Overlaps(exit.StreetOpening), Is.False, "Center dashes cannot continue through the T-junction.");

            // Radius is applied per walkable rectangle. Adjacent rectangles
            // alone leave an invisible band which a long scripted step skips.
            foreach (float radius in new[] { .30f, .35f })
            foreach (float zOffset in new[] { -2.4f, 0f, 2.4f })
            for (int step = -20; step <= 20; step++)
            {
                Vector3 point = exit.ApproachStart + new Vector3(step * .05f, 0f, zOffset);
                Assert.That(city.World.WalkableArea.Contains(point, radius), Is.True,
                    "The hero's radius must fit through every part of the street seam: " + point + "; radius=" + radius);
                Vector3 next = point + Vector3.right * .025f;
                Assert.That(city.World.WalkableArea.Constrain(point, next, radius).x,
                    Is.EqualTo(next.x).Within(.001f), "A normal frame-sized step cannot stop at the sidewalk opening.");
            }

            // Check the actual physical seam on both sides and across its
            // width: matching only the centre misses the city's sloping edge.
            foreach (float zOffset in new[] { -2.4f, 0f, 2.4f })
            {
                float z = exit.ApproachStart.z + zOffset;
                Assert.That(city.Layout.ElevationPlan.TrySampleSurface(new Vector2(exit.ApproachStart.x, z),
                    CitySurfaceRole.RoadTop, out float streetTop, out _), Is.True);
                Assert.That(exit.SampleRoadTop(exit.ApproachStart.x, z), Is.EqualTo(streetTop).Within(.001f));
                foreach (float xOffset in new[] { -.02f, .02f })
                {
                    float x = exit.ApproachStart.x + xOffset;
                    Assert.That(Physics.Raycast(new Vector3(x, streetTop + 2f, z), Vector3.down,
                        out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore), Is.True,
                        "The T-junction has a physical gap at " + new Vector2(x, z));
                    float expected = xOffset < 0f ? streetTop : exit.SampleRoadTop(x, z);
                    Assert.That(hit.point.y, Is.EqualTo(expected).Within(.003f),
                        "The junction must meet asphalt height without a raised sidewalk or doubled road skin.");
                }
            }
            Debug.Log("EAST JUNCTION: approach=" + exit.ApproachStart + "; checkpoint=" + exit.CheckpointPosition +
                "; approach length=" + (exit.CheckpointPosition.x - exit.ApproachStart.x) +
                "; physical street seam and radius-aware sidewalk crossing verified.");
        }

        private static IEnumerator VerifyEastNormalWalking(CityGameRoot city, CityEastExitPlan exit,
            CityMapCityTeleportGround landing)
        {
            PlayerMotor motor = city.Player.Motor;
            Camera camera = Camera.main;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            Assert.That(follow, Is.Not.Null);
            Assert.That(follow.FixedPoseActive, Is.False);
            float oldCaptureDeltaTime = Time.captureDeltaTime;
            bool oldInputEnabled = motor.InputEnabled;
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = null;
            try
            {
                keyboard = InputSystem.AddDevice<Keyboard>();
                Time.captureDeltaTime = 1f / 30f;
                Assert.That(landing.TryResolveStandingPosition(new Vector2(
                    exit.ApproachStart.x - city.Layout.RoadWidth * .5f, exit.ApproachStart.z), out Vector3 start), Is.True);
                motor.Teleport(start);
                motor.transform.rotation = Quaternion.LookRotation(Vector3.right);
                motor.SetInputEnabled(true);
                // Only turn the ordinary orbit toward the road. The production
                // third-person distance, pitch, hero, FOV and fog stay in place.
                follow.Snap();
                follow.RotateYaw(Mathf.DeltaAngle(camera.transform.eulerAngles.y, 90f));
                follow.Snap();
                for (int frame = 0; frame < 12; frame++) yield return null;
                CaptureCurrentCamera(camera, SceneIds.City, "east-exit-00-main-road-third-person");

                input.Press(keyboard.wKey, queueEventOnly: true);
                float previousX = motor.transform.position.x;
                int stalledFrames = 0;
                for (int frame = 0; frame < 600 && motor.transform.position.x < exit.CheckpointPosition.x - .9f; frame++)
                {
                    yield return null;
                    Assert.That(GameInput.ReadMovement().y, Is.EqualTo(1f), "The traversal must use the ordinary held W input.");
                    Assert.That(motor.InteractionPoseMoveActive, Is.False, "Guided interaction movement must not substitute for normal walking.");
                    float x = motor.transform.position.x;
                    stalledFrames = x - previousX < .001f ? stalledFrames + 1 : 0;
                    previousX = x;
                    Assert.That(stalledFrames, Is.LessThan(60),
                        "Ordinary W walking hit an invisible wall at " + motor.transform.position);
                }
                Assert.That(motor.transform.position.x, Is.GreaterThanOrEqualTo(exit.CheckpointPosition.x - .9f),
                    "Normal walking must reach the closed barrier from the city street.");
                // Continue the same held key into the actual barrier; only
                // this visible obstruction may finish the player's approach.
                for (int frame = 0; frame < 36; frame++) yield return null;
                Assert.That(motor.transform.position.x,
                    Is.InRange(exit.CheckpointPosition.x - 1.1f, exit.CheckpointPosition.x - .25f),
                    "The closed post must stop ordinary walking at its visible barrier.");
                CaptureCurrentCamera(camera, SceneIds.City, "east-exit-00-normal-walk-barrier");
                Debug.Log("EAST NORMAL WALK: held W crossed the city seam from " + start +
                    " and stopped at the visible barrier at " + motor.transform.position + ".");
            }
            finally
            {
                if (keyboard != null && keyboard.added)
                {
                    input.Release(keyboard.wKey, queueEventOnly: true);
                    InputSystem.RemoveDevice(keyboard);
                }
                input.TearDown();
                motor.SetInputEnabled(oldInputEnabled);
                Time.captureDeltaTime = oldCaptureDeltaTime;
            }
        }
    }
}
