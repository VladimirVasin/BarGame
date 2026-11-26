using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// A door into the City used to pay the whole city build inside one
    /// frozen frame after the door sequence, because only area travel
    /// pumped a registered composition. What has to hold here is the door
    /// path's own staging: the City registers from its Awake while the
    /// black is still up, is built over several frames with the transition
    /// guard held, and the door scene's camera does not outlive the load.
    /// </summary>
    public sealed class DoorPathCompositionPlayModeTests
    {
        private const float DeadlineSeconds = 120f;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            float deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while ((SceneTransitionService.IsTransitioning || AreaTravelService.IsTraveling) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(SceneTransitionService.IsTransitioning || AreaTravelService.IsTraveling, Is.False);
            Scene blank = SceneManager.CreateScene("Door Path Composition Cleanup");
            SceneManager.SetActiveScene(blank);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != blank && (AreaSceneCatalog.TryGetArea(scene.name, out _) ||
                    scene.name == SceneIds.AreaLoading || scene.name == SceneIds.BarInterior ||
                    scene.name == SceneIds.DoorTransition || scene.name == SceneIds.MainMenu ||
                    scene.name == SceneIds.StairwellInterior || scene.name == SceneIds.CityRidge))
                    yield return SceneManager.UnloadSceneAsync(scene);
            }
            // Voices that outlive the city (pooled effects, a detached music
            // tail) would play into the next test's listener-less scene and
            // trip its log assertions.
            foreach (AudioSource source in Object.FindObjectsByType<AudioSource>(
                         FindObjectsInactive.Include))
            {
                if (source != null) source.Stop();
            }
            MusicMix.ClearFadeOuts();
            GameSessionState.BeginNewGame();
        }

        [UnityTest]
        public IEnumerator RidgeStart_KeepsThePhysicalCliffAtTheReportedEdge()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            var blueprint = CityBlueprintCatalog.Default;
            var settings = CityGenerationSettings.Default;
            int seed = GameSessionState.DefaultCitySeed;
            CityLayoutCache.PrimeCityPlans(blueprint, settings, seed, nameof(RidgeStart_KeepsThePhysicalCliffAtTheReportedEdge));
            var plans = CityLayoutCache.WaitForPrimedPlans(blueprint, settings, seed);
            yield return WaitForStreaming(() => plans == null || plans.IsCompleted, "menu plans ready");
            CityLayoutCache.GetOrGenerate(blueprint, settings, seed);
            Assert.That(NewGameStartService.TryStart(NewGameLocation.Ridge, 4 * 60 + 40), Is.True);
            Assert.That(GameSessionState.TrySetDebugWeather(WeatherKind.HeavyRain), Is.True);
            yield return WaitForDoorDestination(SceneIds.City);
            var city = Object.FindAnyObjectByType<CityGameRoot>();
            Assert.That(city.RidgeDistrictReady, Is.True);
            Assert.That(city.LowerDistrictReady, Is.False);
            var boundary = city.World.MountainBoundaryRoot;
            Assert.That(boundary, Is.Not.Null, "A Ridge-only arrival must build the physical mountains.");
            Assert.That(boundary.scene.name, Is.EqualTo(SceneIds.CityRidge));
            var rim = boundary.transform.Find("Physical Ridges/mountain-south-east-outer-rim");
            Assert.That(rim, Is.Not.Null);
            var faces = rim.GetComponentsInChildren<MeshRenderer>();
            var hero = city.Player;
            hero.Motor.SetInputEnabled(false);
            hero.Motor.Teleport(new Vector3(57.9f, 40.6f, -373.7f));
            hero.GameObject.transform.rotation = Quaternion.LookRotation(Vector3.left);
            Physics.SyncTransforms();
            var camera = city.Camera;
            var follow = camera.GetComponent<PlayerCameraFollow>();
            follow.SetOrbitInputEnabled(false);
            follow.Snap();
            follow.RotateYaw(Mathf.DeltaAngle(camera.transform.eulerAngles.y, 90f));
            follow.RotatePitch(-follow.CurrentOrbitPitch);
            follow.Snap();
            for (int frame = 0; frame < 8; frame++) yield return null;
            Assert.That(city.LowerDistrictReady, Is.False);
            Assert.That(RenderSettings.fog && city.Night.FogField.Particles.particleCount > 0, Is.True);
            Assert.That(city.Rain.Intensity, Is.GreaterThan(.5f));

            using (GameTimeScaleRuntime.AcquirePause())
            {
                foreach (var (name, yaw, horizontal) in new[] {
                    ("streamed-rim-east", 90f, .82f), ("streamed-rim-south", 180f, .75f) })
                {
                    follow.RotateYaw(Mathf.DeltaAngle(camera.transform.eulerAngles.y, yaw));
                    follow.Snap();
                    if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                        AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.City, name);
                    var ray = camera.ViewportPointToRay(new Vector3(horizontal, .5f));
                    Assert.That(Physics.Raycast(ray, out var hit, 35f, Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore) && hit.transform.IsChildOf(rim), Is.True,
                        $"{name}: expected the rock in the shoulder camera, hit {hit.collider}.");
                    if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) continue;
                    try
                    {
                        foreach (var face in faces) face.forceRenderingOff = true;
                        AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.City, name + "-hidden");
                    }
                    finally { foreach (var face in faces) face.forceRenderingOff = false; }
                    Assert.That(AreaCaptureFixture.RimDifference(name, name + "-hidden", horizontal),
                        Is.GreaterThan(8f), name + ": the cliff must remain distinguishable in predawn rain.");
                }
            }
        }

        [UnityTest]
        public IEnumerator MenuWarmup_ReusesSharedPlansAtTheStreetDoor()
        {
            CityLayoutCache.Reset();
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            StartMenuRoot menu = Object.FindAnyObjectByType<StartMenuRoot>();
            Assert.That(menu, Is.Not.Null);
            float menuStarted = Time.realtimeSinceStartup;
            float deadline = menuStarted + DeadlineSeconds;
            while (!CityLayoutCache.HasPendingCityPlans && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(CityLayoutCache.HasPendingCityPlans, Is.True);

            CityBlueprint blueprint = CityBlueprintCatalog.Default;
            CityGenerationSettings settings = CityGenerationSettings.Default;
            int seed = GameSessionState.DefaultCitySeed;
            var plans = CityLayoutCache.WaitForPrimedPlans(blueprint, settings, seed);
            while (plans != null && !plans.IsCompleted && Time.realtimeSinceStartup < deadline)
            {
                Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
                Assert.That(SceneTransitionService.IsTransitioning, Is.False);
                Assert.That(AreaAssetWarmup.InFlightCount, Is.LessThanOrEqualTo(1));
                yield return null;
            }
            Assert.That(plans == null || plans.IsCompleted, Is.True);
            Assert.That(Object.FindAnyObjectByType<CityGameRoot>(), Is.Null);
            CityLayout prepared = CityLayoutCache.GetLayoutForBarSelection(blueprint, settings, seed);
            Assert.That(CityLayoutCache.GetOrGenerate(blueprint, settings, seed), Is.SameAs(prepared));
            Assert.That(CityWorldPlans.IsMemoised(prepared), Is.True);
            TestContext.WriteLine($"Menu plans ready in {Time.realtimeSinceStartup - menuStarted:F2}s.");

            Assert.That(menu.OpenDebugMenu(), Is.True);
            Assert.That(menu.ConfirmSelection(), Is.True);
            Assert.That(menu.ConfirmSelection(), Is.True);
            Assert.That(menu.SelectLocation(NewGameLocation.Stairwell), Is.True);
            Assert.That(menu.ConfirmSelection(), Is.True);
            yield return WaitForDoorDestination(SceneIds.StairwellInterior);

            float departed = Time.realtimeSinceStartup;
            Assert.That(SceneTransitionService.RequestDoorLoad(
                SceneIds.City, DoorTransitionDirection.ExitBuilding), Is.True);
            GameSessionState.PrepareHomeReturn();
            yield return WaitForDoorDestination(SceneIds.City);
            CityGameRoot city = Object.FindAnyObjectByType<CityGameRoot>();
            Assert.That(city, Is.Not.Null);
            Assert.That(city.IsInitialized, Is.True);
            Assert.That(city.Layout, Is.SameAs(prepared));
            Scene ridge = SceneManager.GetSceneByName(SceneIds.CityRidge);
            Assert.That(ridge.isLoaded, Is.True);
            Assert.That(ridge.path, Does.EndWith("/CityRidge.unity"));
            Assert.That(city.World.RidgeRoot.scene, Is.EqualTo(ridge));
            Assert.That(city.World.PlayerHome.gameObject.scene, Is.EqualTo(ridge));
            Assert.That(city.Player.GameObject.scene, Is.EqualTo(city.Scene));
            Assert.That(city.RidgeDistrictReady, Is.True);
            Assert.That(city.LowerDistrictReady, Is.False, "The first street door must only generate the Ridge.");
            Assert.That(Object.FindAnyObjectByType<CityPortController>(), Is.Null);
            TestContext.WriteLine($"First street door to City ready in {Time.realtimeSinceStartup - departed:F2}s.");
            ReportMemory("First street");

            Vector3 homeDock = city.World.PlayerHome.ReturnPosition;
            CityBusDirector hillsideBus = city.BusVehicles.First(bus => bus.LineIndex == 2);
            GameSessionState.EnterHome();
            GameSessionState.PrepareStairwellArrival(StairwellArrivalKind.StreetDoor);
            Assert.That(SceneTransitionService.RequestDoorLoad(
                SceneIds.StairwellInterior, DoorTransitionDirection.EnterBuilding), Is.True);
            yield return WaitForDoorDestination(SceneIds.StairwellInterior);
            Assert.That(city.IsDormant, Is.True);
            Assert.That(ridge.isLoaded, Is.True);
            Assert.That(city.World.RidgeRoot.activeInHierarchy, Is.False);

            GameSessionState.PrepareHomeReturn();
            Assert.That(SceneTransitionService.RequestDoorLoad(
                SceneIds.City, DoorTransitionDirection.ExitBuilding), Is.True);
            yield return WaitForDoorDestination(SceneIds.City);
            Assert.That(city.IsDormant, Is.False);
            Assert.That(city.World.RidgeRoot.scene, Is.EqualTo(ridge));
            Assert.That(city.World.RidgeRoot.activeInHierarchy, Is.True);
            CollectionAssert.Contains(city.BusVehicles, hillsideBus);
            Assert.That(Vector3.Distance(city.Player.GameObject.transform.position, homeDock), Is.LessThan(.5f));
            yield return RideFromRidgeIntoCity(city, hillsideBus, ridge, homeDock);
        }

        private static IEnumerator RideFromRidgeIntoCity(CityGameRoot city, CityBusDirector bus,
            Scene firstRidgeScene, Vector3 homeDock)
        {
            CityBusStopDescriptor stop = bus.Plan.Stops.Last();
            PlayerRuntime hero = city.Player;
            var districtPlan = new CityDistrictStreamingPlan(city.Layout);
            Assert.That(districtPlan.IsRidge(stop.ShelterPosition), Is.True);
            Vector3 pavement = CityBusStopWaitPlanner.StandingPosition(bus.Plan, stop, city.Layout);
            Assert.That(districtPlan.SignedDistance(pavement), Is.InRange(-CityDistrictStreamingPlan.PreloadDistance, -5f));
            hero.Motor.Teleport(pavement + Vector3.up * PlayerFactory.GroundedRootOffset);
            Physics.SyncTransforms();
            yield return WaitForStreaming(() => city.LowerDistrictReady, "approach from the Ridge");
            ReportStreamStage(city);
            Assert.That(firstRidgeScene.isLoaded, Is.True);
            Assert.That(Object.FindAnyObjectByType<CityPortController>(), Is.Not.Null);

            foreach (var service in city.BusVehicles)
            {
                service.enabled = false;
                service.Actor.ReleasePresentation(service.transform);
            }

            var actor = bus.Actor;
            var ride = city.BusRides[city.BusVehicles.ToList().IndexOf(bus)];
            float along = Mathf.Max(0, stop.DistanceAlongLink - 8f);
            CityBusActor.EvaluateLink(bus.Plan.Links[stop.LinkIndex], along, out var spawn, out var heading);
            actor.PrepareSpawn(bus.Plan, new CityBusSpawnAnchor("ridge-crossing", stop.LinkIndex,
                along, spawn, heading, stop.RoadEdge), 7u);
            for (int frame = 0; frame < 260 && !(actor.CurrentStop == stop && actor.DoorsFullyOpen); frame++)
            {
                bus.Advance(.08f);
                yield return null;
            }
            Assert.That(actor.CurrentStop, Is.EqualTo(stop));
            Assert.That(actor.DoorsFullyOpen, Is.True);

            var capsule = hero.GameObject.GetComponent<CharacterController>();
            var rig = (Player3DCharacterPresentation)hero.Visual;
            Vector3 pelvis = hero.GameObject.transform.InverseTransformPoint(rig.Registry.Anchors.Pelvis.position);
            Assert.That(CityBusRidePlan.TryCreate(actor, city.World.WalkableArea, pelvis, capsule.radius,
                CityBusPassengerDoor.Front, CityStreetSurfacePlanner.Create(city.Layout), out var dock), Is.True);
            hero.Motor.Teleport(dock.EntryPose.RootPosition);
            hero.GameObject.transform.rotation = dock.EntryPose.RootRotation;
            Physics.SyncTransforms();
            Assert.That(ride.BeginBoarding(), Is.True);
            yield return WaitForStreaming(() => ride.State == CityBusRideState.Riding, "boarding line 03");

            Transform seat = ride.ActivePlan.SeatAnchor;
            float before = bus.DistanceAlongLoop;
            for (int frame = 0; frame < 1600 && districtPlan.IsRidge(actor.Position); frame++)
            {
                bus.Advance(.12f);
                yield return null;
                Assert.That(ride.ActivePlan.SeatAnchor, Is.SameAs(seat));
                Assert.That(ride.IsPassengerAboard, Is.True);
            }
            Assert.That(districtPlan.IsRidge(actor.Position), Is.False, "Line 03 crossed into the lower city.");
            Assert.That(bus.DistanceAlongLoop, Is.Not.EqualTo(before));
            Assert.That(city.Player.GameObject, Is.SameAs(hero.GameObject));
            Assert.That(city.BusVehicles, Does.Contain(bus));
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneIds.City));
            AssertPhysicalStreet(actor.Position);

            // Stay aboard the same vehicle through the Works Bridge leg
            // and back onto the western ascent.
            for (int frame = 0; frame < 2200 && !districtPlan.IsRidge(actor.Position); frame++)
            {
                bus.Advance(.12f);
                yield return null;
                Assert.That(ride.ActivePlan.SeatAnchor, Is.SameAs(seat));
                Assert.That(ride.IsPassengerAboard, Is.True);
            }
            Assert.That(districtPlan.IsRidge(actor.Position), Is.True, "Line 03 crossed back onto the Ridge.");
            AssertPhysicalStreet(actor.Position);
            CaptureStreamingFrame(city, "line-03-west");

            ride.ForceReleaseForBusLifecycle();
            Vector3 north = city.BusPlans[0].Stops.OrderByDescending(point => point.ShelterPosition.z).First().ShelterPosition;
            hero.Motor.Teleport(north + Vector3.up * PlayerFactory.GroundedRootOffset);
            yield return WaitForStreaming(() => !city.RidgeDistrictReady && !firstRidgeScene.isLoaded,
                "Ridge eviction after travelling away");
            yield return Resources.UnloadUnusedAssets();
            ReportMemory("Lower city alone");

            Vector3 lowerApproach = city.BusPlans.SelectMany(line => line.Stops.Select(
                    platform => CityBusStopWaitPlanner.StandingPosition(line, platform, city.Layout)))
                .First(point => {
                    float distance = districtPlan.SignedDistance(point);
                    return distance > 5f && distance < CityDistrictStreamingPlan.PreloadDistance;
                });
            hero.Motor.Teleport(lowerApproach + Vector3.up * PlayerFactory.GroundedRootOffset);
            yield return WaitForStreaming(() => city.RidgeDistrictReady, "approach from the lower city");
            ReportStreamStage(city);
            hero.Motor.Teleport(homeDock);
            yield return WaitForStreaming(() => !city.LowerDistrictReady, "lower city eviction");
            yield return Resources.UnloadUnusedAssets();
            ReportMemory("Ridge after round trip");
            CaptureStreamingFrame(city, "ridge-alone");
        }

        private static void AssertPhysicalStreet(Vector3 position)
        {
            Physics.SyncTransforms();
            Assert.That(Physics.RaycastAll(position + Vector3.up * .45f, Vector3.down, 1.4f)
                .Any(hit => hit.collider.GetComponent<FootstepGround>() != null),
                Is.True, "The bus must cross over a composed, physical street.");
        }

        private static void CaptureStreamingFrame(CityGameRoot city, string frame)
        {
            if (System.Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_CITY_STREAM") == "1" &&
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                city.Camera.GetComponent<PlayerCameraFollow>()?.Snap();
                AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "CityDistrictStreaming", frame);
            }
        }

        private static IEnumerator WaitForStreaming(System.Func<bool> ready, string description)
        {
            float deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            float previous = Time.realtimeSinceStartup, peak = 0f;
            while (!ready() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                float now = Time.realtimeSinceStartup;
                peak = Mathf.Max(peak, now - previous);
                previous = now;
            }
            TestContext.WriteLine($"{description}: peak frame gap {peak * 1000f:F1} ms.");
            Assert.That(ready(), Is.True, description);
        }

        private static void ReportStreamStage(CityGameRoot city) =>
            TestContext.WriteLine($"Longest district composition frame: {city.SlowestDistrictBuildPhase}, " +
                $"{city.SlowestDistrictBuildMilliseconds:F1} ms.");

        private static void ReportMemory(string point)
        {
            TestContext.WriteLine($"{point} Unity memory: allocated {Profiler.GetTotalAllocatedMemoryLong()} B, " +
                $"reserved {Profiler.GetTotalReservedMemoryLong()} B, managed {Profiler.GetMonoUsedSizeLong()} B.");
        }

        private static IEnumerator WaitForDoorDestination(string destination)
        {
            float deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (SceneTransitionService.IsTransitioning && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(SceneTransitionService.IsTransitioning, Is.False);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(destination));
        }

        [UnityTest]
        public IEnumerator LeavingTheBar_BuildsTheCityOverSeveralFramesBehindTheBlack()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.EnterBar("door-path-composition-test");
            yield return SceneManager.LoadSceneAsync(SceneIds.BarInterior, LoadSceneMode.Single);
            BarInteriorRoot bar = null;
            float deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (bar == null || !bar.IsInitialized)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Bar initialization timed out.");
                bar = Object.FindAnyObjectByType<BarInteriorRoot>();
                yield return null;
            }

            bool previousAudioPause = AudioListener.pause;
            int previousFrameInterval = OnDemandRendering.renderFrameInterval;
            // The same entry BarExit takes once its door action completes.
            Assert.That(SceneTransitionService.RequestDoorLoad(
                SceneIds.City, DoorTransitionDirection.ExitBar, out string operationId), Is.True);
            GameSessionState.PrepareCityReturn();
            Assert.That(operationId, Is.Not.Empty);

            int composingFrames = 0;
            deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (SceneTransitionService.IsTransitioning && Time.realtimeSinceStartup < deadline)
            {
                if (CompositionDriver.IsComposing)
                {
                    composingFrames++;
                    Assert.That(AreaTravelService.IsTraveling, Is.False, "The door path composes without area travel.");
                    Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneIds.City));
                    Assert.That(Object.FindAnyObjectByType<TransitionBlackoutOverlay>(), Is.Not.Null,
                        "The black must cover the partially built city.");
                    Assert.That(GameTimeScaleRuntime.IsPaused, Is.True);
                    Assert.That(AudioListener.pause, Is.True);
                }
                yield return null;
            }

            Assert.That(SceneTransitionService.IsTransitioning, Is.False, "The door transition never finished.");
            Assert.That(composingFrames, Is.GreaterThan(1), "The city must be built over several frames, not one.");
            Assert.That(CompositionDriver.IsComposing, Is.False);
            CityGameRoot city = Object.FindAnyObjectByType<CityGameRoot>();
            Assert.That(city, Is.Not.Null);
            Assert.That(city.IsInitialized, Is.True, "The transition ended before the city was ready.");
            Assert.That(GameTimeScaleRuntime.IsPaused, Is.False);
            Assert.That(AudioListener.pause, Is.EqualTo(previousAudioPause));
            Assert.That(OnDemandRendering.renderFrameInterval, Is.EqualTo(previousFrameInterval),
                "The finished city must restore the caller's drawing rate.");
            yield return null;
            Assert.That(Object.FindAnyObjectByType<TransitionBlackoutOverlay>(), Is.Null,
                "The black must leave with the transition.");
            Assert.That(Object.FindAnyObjectByType<DoorTransitionRoot>(), Is.Null);
            Camera main = Camera.main;
            Assert.That(main, Is.Not.Null);
            Assert.That(main.gameObject.scene.name, Is.EqualTo(SceneIds.City),
                "The door scene's camera must not outlive the Single load.");
        }
    }
}
