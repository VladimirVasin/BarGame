using System;
using System.Collections;
using System.Linq;
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
        [Explicit("East road/church landscape, local lights, closed civilian post and distant mainland day/night views.")]
        [PrebuildSetup(typeof(EastGuardAssetsSetup))]
        public IEnumerator CityEastExit()
        {
            bool landscapeOnly = Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_EAST_LANDSCAPE") == "1";
            bool panoramaOnly = Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_EAST_PANORAMA") == "1";
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
                if (panoramaOnly)
                {
                    Vector3 panoramaEye = p.CheckpointPosition + new Vector3(-2.1f, EyeHeight, 0f);
                    return new[] { Shot.At("east-exit-panorama-initial", panoramaEye,
                        panoramaEye + Vector3.right * 8000f,
                        Camera.main.GetComponent<PlayerCameraFollow>().FollowFieldOfView) };
                }
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
                Vector2 benchApproach = new Vector2(p.CheckpointPosition.x - 8f, p.CheckpointPosition.z - 9.9f);
                Vector2 benchPoint = new Vector2(p.CheckpointPosition.x - 4.85f, p.CheckpointPosition.z - 11.4f);
                Vector3 cameraShift = Vector3.forward * .002f;
                Vector2 seamView = new Vector2(p.CheckpointPosition.x - 6f, p.YardBounds.yMin - 1.2f);
                CityTerrainSurfacePlan.TrySampleGroundTop(city.Layout, seamView, out float seamHeight, out _);
                Vector3 seamEye = new Vector3(seamView.x, seamHeight + EyeHeight, seamView.y);
                if (landscapeOnly)
                    return new[] { Shot.At("east-exit-landscape-post", postEye,
                        p.CheckpointPosition + Vector3.up * EyeHeight) };
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
                    Shot.At("east-exit-00-post-ground-b", postGroundEye + cameraShift, postGroundTarget + cameraShift),
                    Shot.At("east-exit-00-garden-seam", seamEye, p.BoothPosition + new Vector3(-.5f, .55f, -3.2f)),
                    Shot.At("east-exit-00-bench-approach",
                        new Vector3(benchApproach.x, p.SampleGroundTop(benchApproach) + EyeHeight, benchApproach.y),
                        new Vector3(benchPoint.x, p.SampleGroundTop(benchPoint) + .65f, benchPoint.y))
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
            if (panoramaOnly)
            {
                yield return CaptureEastPanorama(city, plan, landing);
                VerifyEastDistanceModel(plan);
                yield break;
            }
            VerifyEastStreetJoin(city, plan);
            VerifyEastDressing(city, plan, landing);
            VerifyEastGroundTransition(city, plan, landing);
            VerifyEastReliefAndTrees(city, plan, landing);
            VerifyEastOpenForefield(city, plan);
            VerifyEastLitter(city, plan, landing);
            VerifyEastSwale(city, plan, landing);
            yield return CaptureEastLitterDetails(city, plan);
            yield return CaptureEastFenceThirdPerson(city, plan, landing, "day");
            if (landscapeOnly)
            {
                GameSessionState.AdvanceGameTime((float)((21d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                    GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                yield return CaptureEastFenceThirdPerson(city, plan, landing, "night");
                Debug.Log("EAST FENCE LANDSCAPE: no service-road strip, relief and sparse trees on both sides, real dry swale, clear pedestrian lanes and third-person day/night views verified.");
                yield break;
            }
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

            VerifyEastDistanceModel(plan);

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
                Light serviceLamp = GameObject.Find(CityEastExitWorldBuilder.ServiceLightName).GetComponent<Light>();
                Light canopyLamp = GameObject.Find(CityEastExitWorldBuilder.CanopyLightName).GetComponent<Light>();
                Vector3 serviceTarget = ResolveEastLightGround(serviceLamp);
                Vector3 serviceView = ResolveEastServiceView(plan, landing, serviceTarget);
                var lightSamples = new System.Collections.Generic.List<EastSurfaceLightSample>();
                for (int phase = 0; phase < 2; phase++)
                {
                    if (phase == 1)
                        GameSessionState.AdvanceGameTime((float)((21d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                            GameTimeState.GameMinutesPerRealSecond));
                    city.DayNight.ApplyCurrentTime(true);
                    string light = phase == 0 ? "day" : "night";
                    VerifyEastLocalLights(city, phase == 0);
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
                        serviceView.x, serviceView.z, serviceTarget + Vector3.up * .45f);
                    Pose servicePose = new Pose(camera.transform.position, camera.transform.rotation);
                    // Look across the former hard texture boundary, then back
                    // from the usable southern approach. Both include human-
                    // scale furniture and keep the original fog/camera contract.
                    yield return EastView("08-garden-to-canopy-" + light,
                        plan.CheckpointPosition.x - 6f, plan.YardBounds.yMin - 1.2f,
                        plan.BoothPosition + new Vector3(-.5f, .55f, -3.2f));
                    Vector3 seamTarget = new Vector3(plan.CheckpointPosition.x - 5f,
                        plan.SampleGroundTop(new Vector2(plan.CheckpointPosition.x - 5f, plan.YardBounds.yMin)),
                        plan.YardBounds.yMin);
                    yield return EastView("09-canopy-to-garden-" + light,
                        plan.CheckpointPosition.x - 5.2f, plan.CheckpointPosition.z - 9.9f, seamTarget);
                    CaptureCurrentCamera(camera, SceneIds.City, "east-exit-09-seam-ground-a-" + light);
                    camera.transform.position += Vector3.right * .002f;
                    CaptureCurrentCamera(camera, SceneIds.City, "east-exit-09-seam-ground-b-" + light);
                    yield return EastView("10-canopy-pool-" + light,
                        plan.CheckpointPosition.x - 5.2f, plan.CheckpointPosition.z - 9.9f,
                        plan.BoothPosition + new Vector3(0f, .03f, -2.65f));
                    Pose canopyPose = new Pose(camera.transform.position, camera.transform.rotation);
                    yield return EastView("11-boundary-landscape-" + light,
                        plan.CheckpointPosition.x - 3f, plan.CheckpointPosition.z + 7f,
                        new Vector3(plan.CheckpointPosition.x, plan.CheckpointPosition.y + .45f,
                            plan.CheckpointPosition.z + 22f));
                    // Take the entire visual set before evaluating photometry.
                    // A faulty emitter must not hide the opposite time-of-day
                    // landscape from the person reviewing the capture.
                    camera.transform.SetPositionAndRotation(servicePose.position, servicePose.rotation);
                    lightSamples.Add(MeasureEastSurfaceLight(camera, serviceLamp, light));
                    camera.transform.SetPositionAndRotation(canopyPose.position, canopyPose.rotation);
                    lightSamples.Add(MeasureEastSurfaceLight(camera, canopyLamp, light));
                }
                foreach (EastSurfaceLightSample sample in lightSamples) sample.Verify();
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
            Debug.Log("EAST EXIT: continuous church/yard surface, physical road, closed boundary, local day/night illumination and passive far mainland verified.");

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

        private static void VerifyEastDistanceModel(CityEastExitPlan exit)
        {
            GameObject panorama = GameObject.Find(CityEastDistanceWorldBuilder.ObjectName);
            Assert.That(panorama, Is.Not.Null);
            Assert.That(panorama.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(panorama.GetComponentsInChildren<Light>(true), Is.Empty);
            Assert.That(panorama.GetComponentsInChildren<AudioSource>(true), Is.Empty);
            MeshFilter[] meshes = panorama.GetComponentsInChildren<MeshFilter>(true);
            MeshFilter skyline = Array.Find(meshes,
                mesh => mesh.name.StartsWith("DistanceCity", StringComparison.Ordinal));
            MeshFilter land = Array.Find(meshes,
                mesh => mesh.name.StartsWith("DistanceLand", StringComparison.Ordinal));
            Assert.That(skyline, Is.Not.Null);
            Assert.That(land, Is.Not.Null);
            Bounds measured = EastImportedBounds(skyline);
            Bounds terrain = EastImportedBounds(land);
            Assert.That(measured.min.x - exit.RoadEnd.x, Is.InRange(7000f, 8500f),
                "The imported city begins near its authored 7800 virtual metres; FBX scale and +X survive placement.");
            Assert.That(measured.size.z, Is.GreaterThan(5000f));
            Assert.That(measured.size.y, Is.InRange(100f, 500f));
            Assert.That(measured.min.y, Is.LessThan(exit.CheckpointPosition.y - 100f),
                "The skyline is grounded in the lower basin rather than raised on the distant approach.");
            float angularWidth = (Mathf.Atan2(measured.max.z - exit.RoadEnd.z, measured.center.x - exit.RoadEnd.x) -
                Mathf.Atan2(measured.min.z - exit.RoadEnd.z, measured.center.x - exit.RoadEnd.x)) * Mathf.Rad2Deg;
            Assert.That(angularWidth, Is.InRange(40f, 60f),
                "The closer city remains a broad distant silhouette, with its original authored width.");
            Assert.That(terrain.size.y, Is.GreaterThan(40f),
                "The middle distance must contain substantial slopes, not the former nearly flat plain.");
            foreach (string role in new[] { "DistanceLampBody", "DistanceLampLens", "DistanceLampHalo", "DistanceLampPool" })
                Assert.That(meshes.Any(mesh => mesh.name.StartsWith(role, StringComparison.Ordinal)), Is.True,
                    "The decorative road fixtures include their supported body, lens, haze and road pool: " + role);
            Assert.That(exit.RealRoadEnd.x - exit.CheckpointPosition.x, Is.InRange(25f, 38f),
                "The visible descent joins the panorama before the ordinary camera's far clip.");
            Assert.That(exit.RealRoadEnd.y, Is.LessThan(exit.CheckpointPosition.y - .1f));
            Assert.That(exit.SampleRoadTop(exit.CheckpointPosition.x + 8f),
                Is.EqualTo(exit.CheckpointPosition.y).Within(.005f), "The short checkpoint apron stays level.");
            MeshFilter road = Array.Find(meshes, mesh => mesh.name.StartsWith("DistanceRoad", StringComparison.Ordinal));
            Assert.That(road, Is.Not.Null);
            Vector3 closest = road.sharedMesh.vertices.Select(road.transform.TransformPoint)
                .OrderBy(vertex => (vertex - exit.RealRoadEnd).sqrMagnitude).First();
            Assert.That(Vector3.Distance(closest, exit.RealRoadEnd), Is.LessThan(.005f),
                "The actual imported road starts at the same centre and height as the real approach.");
            MeshCollider realRoad = GameObject.Find("EEX_Road_Collision").GetComponent<MeshCollider>();
            Assert.That(realRoad.bounds.max.x, Is.EqualTo(exit.RealRoadEnd.x).Within(.04f));
            Vector3 lastRealSample = exit.SampleRoadCenter(exit.RealRoadEnd.x - .05f);
            Assert.That(realRoad.Raycast(new Ray(lastRealSample + Vector3.up * 2f, Vector3.down),
                out RaycastHit roadHit, 4f), Is.True);
            Assert.That(roadHit.point.y, Is.EqualTo(lastRealSample.y).Within(.015f),
                "The fitted real asphalt reaches the shared descending profile without a step.");
            foreach (MeshRenderer renderer in panorama.GetComponentsInChildren<MeshRenderer>(true))
            {
                Assert.That(renderer.sharedMaterial, Is.Not.Null);
                Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("Bar Promenade/City East Distance"));
                Assert.That(renderer.sharedMaterial.GetVector("_ViewDirection"),
                    Is.EqualTo(new Vector4(1f, 0f, 0f, 0f)),
                    "The village's rotated panorama must not change the checkpoint profile.");
                Assert.That(renderer.sharedMaterial.GetFloat("_Visibility"), Is.EqualTo(1f),
                    "Village storm waves must not dim the checkpoint's city.");
                Assert.That(renderer.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off));
                Assert.That(renderer.receiveShadows, Is.False);
            }
            Debug.Log("EAST PANORAMA imported city=" + measured + "; terrain=" + terrain +
                "; city angular width=" + angularWidth + "; root=" + panorama.transform.rotation.eulerAngles);
        }

        // Renderer.bounds deliberately encloses the shader projection. Measure
        // the imported vertices through their actual FBX transform instead.
        private static Bounds EastImportedBounds(MeshFilter filter)
        {
            Bounds source = filter.sharedMesh.bounds;
            Bounds result = new Bounds(filter.transform.TransformPoint(source.center), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
                result.Encapsulate(filter.transform.TransformPoint(new Vector3(
                    (corner & 1) == 0 ? source.min.x : source.max.x,
                    (corner & 2) == 0 ? source.min.y : source.max.y,
                    (corner & 4) == 0 ? source.min.z : source.max.z)));
            return result;
        }

        private static IEnumerator CaptureEastPanorama(CityGameRoot city, CityEastExitPlan exit,
            CityMapCityTeleportGround landing)
        {
            PlayerMotor motor = city.Player.Motor;
            Camera camera = Camera.main;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            CityEastDistanceTraffic traffic = GameObject.Find(CityEastDistanceWorldBuilder.ObjectName)
                .GetComponent<CityEastDistanceTraffic>();
            Assert.That(follow, Is.Not.Null);
            Assert.That(follow.FixedPoseActive, Is.False);
            Assert.That(traffic, Is.Not.Null);
            Assert.That(traffic.Vehicles.Count, Is.EqualTo(2));
            Renderer[] hero = city.Player.GameObject.GetComponentsInChildren<Renderer>();
            bool[] heroEnabled = Array.ConvertAll(hero, renderer => renderer.enabled);
            Assert.That(heroEnabled.Any(value => value), Is.True,
                "The normal third-person camera keeps the hero visible for scale.");
            bool oldFollow = follow.enabled, oldInput = motor.InputEnabled, oldAdvance = traffic.AutoAdvance;
            Vector3 oldFeet = motor.transform.position, oldEye = camera.transform.position;
            Quaternion oldBody = motor.transform.rotation, oldCamera = camera.transform.rotation;
            float oldPitch = follow.TargetOrbitPitch, oldFov = camera.fieldOfView;
            double oldTrafficTime = traffic.ElapsedSeconds;
            float dayRoadLampIntensity = 0f;
            try
            {
                motor.SetInputEnabled(false);
                traffic.AutoAdvance = false;
                traffic.SampleAt(traffic.VisibleWitnessSeconds);
                for (int phase = 0; phase < 2; phase++)
                {
                    if (phase == 1)
                        GameSessionState.AdvanceGameTime((float)((21d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                            GameTimeState.GameMinutesPerRealSecond));
                    city.DayNight.ApplyCurrentTime(true);
                    string light = phase == 0 ? "day" : "night";
                    traffic.SampleAt(traffic.VisibleWitnessSeconds);
                    Assert.That(traffic.Vehicles[0].gameObject.activeSelf, Is.True,
                        "The route sequence starts with an actual active car in its visible road window.");
                    yield return ThirdPerson("actual-road-" + light,
                        new Vector2(exit.CheckpointPosition.x - 3f, exit.CheckpointPosition.z), 90f);
                    CityEastRoadProfile.Lamp realLamp = exit.RoadProfile.Lamps.Single(lamp => lamp.real);
                    Vector3 lampAnchor = exit.RoadEnd + realLamp.Position +
                        Quaternion.LookRotation(realLamp.Forward, Vector3.up) * new Vector3(0f, 4.70f, 1.07f);
                    Assert.That(city.Night.Atmosphere.LampAnchors.Any(anchor =>
                        Vector3.Distance(anchor.position, lampAnchor) < .01f), Is.True);
                    Light leasedLamp = city.Night.Atmosphere.StreetLightPool.FirstOrDefault(lamp =>
                        lamp.enabled && Vector3.Distance(lamp.transform.position, lampAnchor) < .01f);
                    Assert.That(leasedLamp, Is.Not.Null, "The first road lamp actually illuminates the near road from the existing pool.");
                    Assert.That(city.Night.Atmosphere.StreetLightPool.Count + city.Night.Atmosphere.BarLights.Count,
                        Is.LessThanOrEqualTo(CityNightAtmosphere.MaximumRealtimeLights));
                    if (phase == 0) dayRoadLampIntensity = leasedLamp.intensity;
                    else Assert.That(dayRoadLampIntensity, Is.GreaterThanOrEqualTo(leasedLamp.intensity * (2f / 3f) - .001f));
                    yield return ThirdPerson("fence-north-" + light,
                        new Vector2(exit.CheckpointPosition.x - 3f, exit.CheckpointPosition.z + 20f), 100f);
                    yield return ThirdPerson("fence-south-" + light,
                        new Vector2(exit.CheckpointPosition.x - 8f, exit.CheckpointPosition.z - 12.5f), 80f);
                    yield return Horizon("horizon-" + light, Vector3.right);
                    // The same ordinary eye and lens expose road/ridge/car
                    // overlap at several points in the actual authored route.
                    foreach (double seconds in new[] { 8d, 16d, 32d })
                    {
                        traffic.SampleAt(traffic.VisibleWitnessSeconds + seconds);
                        for (int frame = 0; frame < 2; frame++) yield return null;
                        CaptureCurrentCamera(camera, SceneIds.City,
                            "east-exit-panorama-traffic-" + seconds + "-" + light);
                    }
                    if (phase == 0) yield return Horizon("north-open-sea", Vector3.forward);
                }

                // At zero the outbound actor is moving for every seeded
                // phase. Later route frames may legitimately put both cars
                // in their offscreen waiting intervals.
                traffic.SampleAt(0d);
                Vector3[] before = traffic.Vehicles.Select(vehicle => vehicle.position).ToArray();
                double beforeSeconds = traffic.ElapsedSeconds;
                traffic.Advance(.1f);
                Assert.That(traffic.ElapsedSeconds, Is.GreaterThan(beforeSeconds));
                Assert.That(traffic.Vehicles.Where((vehicle, i) =>
                    Vector3.Distance(vehicle.position, before[i]) > .001f).Any(), Is.True,
                    "Distant traffic must advance on its authored road.");
                before = traffic.Vehicles.Select(vehicle => vehicle.position).ToArray();
                beforeSeconds = traffic.ElapsedSeconds;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    traffic.Advance(5f);
                    traffic.AutoAdvance = true;
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    Assert.That(traffic.ElapsedSeconds, Is.EqualTo(beforeSeconds));
                    for (int i = 0; i < before.Length; i++)
                        Assert.That(Vector3.Distance(traffic.Vehicles[i].position, before[i]), Is.LessThan(.0001f),
                            "Pause must freeze every distant car without hiding it.");
                }
                traffic.AutoAdvance = false;
                Debug.Log("EAST PANORAMA: imported terrain/city scale, passive rendering and moving/pause-aware traffic verified; " +
                    "normal third-person, oblique, horizon and route-sequence day/night frames captured for overlap review.");
            }
            finally
            {
                traffic.SampleAt(oldTrafficTime);
                traffic.AutoAdvance = oldAdvance;
                for (int i = 0; i < hero.Length; i++) hero[i].enabled = heroEnabled[i];
                motor.Teleport(oldFeet);
                motor.transform.rotation = oldBody;
                follow.enabled = true;
                follow.RotateYaw(Mathf.DeltaAngle(camera.transform.eulerAngles.y, oldCamera.eulerAngles.y));
                follow.RotatePitch(oldPitch - follow.TargetOrbitPitch);
                follow.Snap();
                follow.enabled = oldFollow;
                camera.transform.SetPositionAndRotation(oldEye, oldCamera);
                camera.fieldOfView = oldFov;
                motor.SetInputEnabled(oldInput);
            }

            IEnumerator ThirdPerson(string name, Vector2 point, float yaw)
            {
                for (int i = 0; i < hero.Length; i++) hero[i].enabled = heroEnabled[i];
                follow.enabled = true;
                Vector3 feet = StandingPoint(point, name);
                motor.Teleport(feet);
                motor.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                follow.Snap();
                follow.RotateYaw(Mathf.DeltaAngle(camera.transform.eulerAngles.y, yaw));
                follow.Snap();
                city.Night.Atmosphere.RefreshImmediate();
                for (int frame = 0; frame < 8; frame++) yield return null;
                VerifyLens();
                CaptureCurrentCamera(camera, SceneIds.City, "east-exit-panorama-" + name);
            }

            IEnumerator Horizon(string name, Vector3 direction)
            {
                follow.enabled = false;
                foreach (Renderer renderer in hero) renderer.enabled = false;
                Vector3 feet = StandingPoint(new Vector2(exit.CheckpointPosition.x - 2.1f,
                    exit.CheckpointPosition.z), name);
                motor.Teleport(feet);
                camera.transform.SetPositionAndRotation(feet + Vector3.up * EyeHeight,
                    Quaternion.LookRotation(direction));
                for (int frame = 0; frame < 4; frame++) yield return null;
                VerifyLens();
                CaptureCurrentCamera(camera, SceneIds.City, "east-exit-panorama-" + name);
            }

            Vector3 StandingPoint(Vector2 point, string name)
            {
                Assert.That(landing.TryResolveStandingPosition(point, out Vector3 feet), Is.True,
                    "Panorama capture stands on accessible ground: " + name);
                Assert.That(Vector2.Distance(new Vector2(feet.x, feet.z), point), Is.LessThan(.02f),
                    "A panorama view cannot silently clamp away from its declared position: " + name);
                return feet;
            }

            void VerifyLens()
            {
                Assert.That(camera.fieldOfView, Is.EqualTo(follow.FollowFieldOfView).Within(.1f));
                Assert.That(camera.farClipPlane, Is.EqualTo(48f).Within(.001f));
                Assert.That(RenderSettings.fogDensity, Is.EqualTo(.070f).Within(.0001f));
            }
        }

        private static IEnumerator CaptureEastFenceThirdPerson(CityGameRoot city, CityEastExitPlan exit,
            CityMapCityTeleportGround landing, string phase)
        {
            PlayerMotor motor = city.Player.Motor;
            Camera camera = Camera.main;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            Assert.That(follow, Is.Not.Null);
            Assert.That(follow.FixedPoseActive, Is.False);
            Assert.That(city.Player.GameObject.GetComponentsInChildren<Renderer>().Any(renderer => renderer.enabled),
                Is.True, "Landscape acceptance keeps the actual hero visible for scale.");
            bool oldFollow = follow.enabled, oldInput = motor.InputEnabled;
            Vector3 oldFeet = motor.transform.position, oldEye = camera.transform.position;
            Quaternion oldBody = motor.transform.rotation, oldCamera = camera.transform.rotation;
            float oldPitch = follow.TargetOrbitPitch, oldFov = camera.fieldOfView;
            try
            {
                follow.enabled = true;
                motor.SetInputEnabled(false);
                float laneX = exit.YardBounds.xMin + 4.5f;
                yield return View("front-near", new Vector2(laneX, exit.CheckpointPosition.z + 20f), 25f);
                yield return View("front-middle", new Vector2(laneX,
                    Mathf.Lerp(exit.CheckpointPosition.z + 15f, exit.NorthYardBounds.yMax - 4f, .5f)), 25f);
                yield return View("front-north", new Vector2(laneX, exit.NorthYardBounds.yMax - 16f), 155f);
                float swaleZ = EastSwaleBroadSection(exit.Swale, .15f);
                yield return View("swale-shoulder", new Vector2(
                    exit.Swale.CenterX(swaleZ) - exit.Swale.HalfWidth(swaleZ) - .6f, swaleZ), 45f);
                swaleZ = EastSwaleBroadSection(exit.Swale, .50f);
                yield return View("swale-bed", new Vector2(exit.Swale.CenterX(swaleZ), swaleZ), 12f);
                float crossingZ = exit.Swale.CrossingZ[exit.Swale.CrossingZ.Count / 2];
                yield return View("swale-crossing", new Vector2(laneX, crossingZ), 90f);
                // Match the reported close fence view: the normal camera is
                // behind the hero while both near and closed-side ground
                // remain in frame. The crossing gives an unobstructed stance.
                yield return View("close-cross-fence", new Vector2(
                    exit.CheckpointPosition.x - 1.3f, crossingZ), 32f);
                yield return View("actual-road", new Vector2(
                    exit.CheckpointPosition.x - 3f, exit.CheckpointPosition.z), 90f);
                yield return View("south-return", new Vector2(
                    Mathf.Lerp(exit.CheckpointPosition.x, exit.YardBounds.xMax, .5f), exit.YardBounds.yMin - 2.5f), 65f);
            }
            finally
            {
                motor.Teleport(oldFeet);
                motor.transform.rotation = oldBody;
                follow.RotateYaw(Mathf.DeltaAngle(camera.transform.eulerAngles.y, oldCamera.eulerAngles.y));
                follow.RotatePitch(oldPitch - follow.TargetOrbitPitch);
                follow.Snap();
                follow.enabled = oldFollow;
                camera.transform.SetPositionAndRotation(oldEye, oldCamera);
                camera.fieldOfView = oldFov;
                motor.SetInputEnabled(oldInput);
            }

            IEnumerator View(string name, Vector2 point, float yaw)
            {
                Assert.That(landing.TryResolveStandingPosition(point, out Vector3 feet), Is.True,
                    "The full-fence view must stand on accessible ground: " + name);
                Assert.That(Vector2.Distance(new Vector2(feet.x, feet.z), point), Is.LessThan(.02f),
                    "A capture must not silently clamp to a different part of the fence: " + name);
                motor.Teleport(feet);
                motor.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                follow.Snap();
                follow.RotateYaw(Mathf.DeltaAngle(camera.transform.eulerAngles.y, yaw));
                follow.Snap();
                for (int frame = 0; frame < 8; frame++) yield return null;
                Assert.That(camera.fieldOfView, Is.EqualTo(follow.FollowFieldOfView).Within(.1f),
                    "Full-fence shots retain the normal third-person lens.");
                Assert.That(camera.farClipPlane, Is.EqualTo(48f).Within(.001f));
                Assert.That(RenderSettings.fogDensity, Is.EqualTo(.070f).Within(.0001f));
                CaptureCurrentCamera(camera, SceneIds.City, "east-exit-fence-" + name + "-" + phase);
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
                var movementTrace = new System.Collections.Generic.Queue<string>();
                for (int frame = 0; frame < 600 && motor.transform.position.x < exit.CheckpointPosition.x - .9f; frame++)
                {
                    yield return null;
                    Assert.That(GameInput.ReadMovement().y, Is.EqualTo(1f), "The traversal must use the ordinary held W input.");
                    Assert.That(motor.InteractionPoseMoveActive, Is.False, "Guided interaction movement must not substitute for normal walking.");
                    float x = motor.transform.position.x;
                    stalledFrames = x - previousX < .001f ? stalledFrames + 1 : 0;
                    previousX = x;
                    RecordEastMotorTrace(motor, frame, movementTrace);
                    if (stalledFrames == 60) DiagnoseEastWalkingStall(city, camera, "asphalt", movementTrace);
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

                input.Release(keyboard.wKey, queueEventOnly: true);
                yield return null;
                // Reproduce the two reported off-asphalt approaches with the
                // same actual controller and ordinary held movement. The south
                // lane ends before the visible booth; the north reaches iron.
                foreach (float offset in new[] { -5f, 6.6f })
                {
                    float z = exit.ApproachStart.z + offset;
                    float goalX = exit.CheckpointPosition.x - (offset < 0f ? 6f : .9f);
                    Assert.That(landing.TryResolveStandingPosition(new Vector2(
                        exit.ApproachStart.x - city.Layout.RoadWidth * .5f, z), out start), Is.True);
                    motor.Teleport(start);
                    motor.transform.rotation = Quaternion.LookRotation(Vector3.right);
                    motor.SetInputEnabled(true);
                    follow.Snap();
                    input.Press(keyboard.wKey, queueEventOnly: true);
                    previousX = motor.transform.position.x;
                    stalledFrames = 0;
                    movementTrace.Clear();
                    for (int frame = 0; frame < 480 && motor.transform.position.x < goalX; frame++)
                    {
                        yield return null;
                        Assert.That(GameInput.ReadMovement().y, Is.EqualTo(1f));
                        Assert.That(motor.InteractionPoseMoveActive, Is.False);
                        float x = motor.transform.position.x;
                        stalledFrames = x - previousX < .001f ? stalledFrames + 1 : 0;
                        previousX = x;
                        RecordEastMotorTrace(motor, frame, movementTrace);
                        if (stalledFrames == 45) DiagnoseEastWalkingStall(city, camera,
                            offset < 0f ? "south-shoulder" : "north-shoulder", movementTrace);
                        Assert.That(stalledFrames, Is.LessThan(45),
                            "Held W hits an invisible wall beside the asphalt at " + motor.transform.position + "; side=" + offset);
                    }
                    Assert.That(motor.transform.position.x, Is.GreaterThanOrEqualTo(goalX),
                        "Walking off the main road must cross the " + (offset < 0f ? "south" : "north") + " shoulder.");
                    if (offset > 0f)
                    {
                        for (int frame = 0; frame < 30; frame++) yield return null;
                        Assert.That(motor.transform.position.x,
                            Is.InRange(exit.CheckpointPosition.x - 1.1f, exit.CheckpointPosition.x - .25f),
                            "Only the physical fence ends the north off-asphalt approach.");
                    }
                    CaptureCurrentCamera(camera, SceneIds.City,
                        "east-exit-00-normal-walk-" + (offset < 0f ? "south" : "north") + "-shoulder");
                    input.Release(keyboard.wKey, queueEventOnly: true);
                    yield return null;
                }
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
