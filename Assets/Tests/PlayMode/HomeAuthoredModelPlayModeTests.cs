using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// One real Home load proves the authored opening, enlarged apartment,
    /// sleep, dining seat, silent painting inspection and reversible debug dressing. Camera frames are
    /// visual review artifacts, not a substitute for looking at the room.
    /// </summary>
    public sealed class HomeAuthoredModelPlayModeTests
    {
        private const float TimeoutSeconds = 25f;
        private IDisposable capturePause;
        private HomeInteriorRoot home;
        private bool? batchFocusBeforeTest;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            capturePause?.Dispose();
            capturePause = null;
            if (home != null)
            {
                home.DebugWindow?.Close();
                Scene scene = home.gameObject.scene;
                Scene cleanup = SceneManager.CreateScene("Home Authored Model Cleanup");
                SceneManager.SetActiveScene(cleanup);
                if (scene.IsValid() && scene.isLoaded)
                {
                    AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
                    while (unload != null && !unload.isDone)
                    {
                        yield return null;
                    }
                }
            }

            home = null;
            GameSessionState.BeginNewGame();
            if (batchFocusBeforeTest.HasValue)
            {
                GameInput.HandleApplicationFocus(batchFocusBeforeTest.Value);
                batchFocusBeforeTest = null;
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AuthoredHome_PreviewsAllDaysAndKeepsLockedRoomClosed()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.PrepareHomeArrival(HomeArrivalKind.OpeningSleep);
            Assert.That(Resources.Load<HomeInteriorModelLibrary>(
                HomeInteriorModelLibrary.ResourcePath), Is.Not.Null,
                "Import the Home Blender model and run HomeInteriorModelAssetSetup before this check.");

            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.HomeInterior, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!load.isDone && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.That(load.isDone, Is.True, "Home did not finish loading.");
            yield return WaitFor(() =>
            {
                home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                return home != null && home.IsInitialized &&
                       !SceneTransitionService.IsTransitioning;
            }, "Home failed to build its authored runtime.");
            for (int frame = 0; frame < 3; frame++) yield return null;

            Assert.That(home.Arrival, Is.EqualTo(HomeArrivalKind.OpeningSleep));
            Assert.That(home.Opening, Is.Not.Null);
            Assert.That(home.ApartmentDays, Is.Not.Null);
            Assert.That(home.DebugWindow, Is.Not.Null);
            Assert.That(home.DebugCityMapShortcut.KeyboardShortcutEnabled, Is.False);
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            yield return CaptureAndCompleteOpening(camera);

            // Keep the comparison sequence in this same apartment instance,
            // with the hero at its ordinary arrival point after the real wake.
            home.Player.Motor.Teleport(home.Layout.PlayerSpawn);
            home.Player.GameObject.transform.rotation = Quaternion.identity;
            home.FixedCamera.ReapplyActiveShot();
            for (int frame = 0; frame < 3; frame++) yield return null;
            capturePause = GameTimeScaleRuntime.AcquirePause();
            double minutesUntilNoon = 720d - GameSessionState.GameTimeOfDayMinutes;
            Assert.That(minutesUntilNoon, Is.GreaterThan(0d));
            GameSessionState.AdvanceGameTime((float)(minutesUntilNoon / GameTimeState.GameMinutesPerRealSecond));
            home.DayNight.RefreshImmediate();
            HomeApartmentDressing dressing = home.Room.GetComponent<HomeApartmentDressing>();
            Assert.That(dressing, Is.Not.Null);
            HomeInteriorLayoutValidator.ValidateOrThrow(home.Layout);
            HomeBalconyLayoutValidator.ValidateOrThrow(home.Layout, home.BalconyLayout);
            dressing.ValidateOrThrow(home.Layout, home.BalconyLayout);

            HomeInteriorModelLibrary library = HomeInteriorModelLibrary.Load();
            Dictionary<Mesh, HomeAuthoredPart> imported = ValidateImportedGeometry(library);
            ValidateLiveGeometry(home, imported);
            ValidateBedSurfaces(home);
            ValidateLockedDoor(home);
            ValidateDiningClearance(home);

            // One pause lease keeps every comparison and exported frame at
            // the same clock time without changing the session's running flag.
            Vector3 initialPosition = home.Player.GameObject.transform.position;
            Quaternion initialRotation = home.Player.GameObject.transform.rotation;
            double initialClock = GameSessionState.GameTimeOfDayMinutes;
            Dictionary<InventoryItemId, int> inventory = SnapshotInventory();
            int hunger = GameSessionState.HungerLevel;
            int fatigue = GameSessionState.FatigueLevel;
            int cash = GameSessionState.CashBalance;
            Assert.That(home.DebugWindow.Open(), Is.True);
            Assert.That(home.ApartmentDays.AppliedDayNumber, Is.EqualTo(1));
            HashSet<MeshFilter> firstDayParts = VisibleDecor(home, imported);
            int firstDayCount = dressing.VisiblePartCount;

            foreach (int day in new[] { 7, 3, 1 })
            {
                Assert.That(home.DebugWindow.TrySetGameDayNumber(day), Is.True);
                Assert.That(home.ApartmentDays.AppliedDayNumber, Is.EqualTo(day));
                Assert.That(dressing.AppliedDayNumber, Is.EqualTo(day));
                Assert.That(home.Player.GameObject.transform.position, Is.EqualTo(initialPosition));
                Assert.That(home.Player.GameObject.transform.rotation, Is.EqualTo(initialRotation));
                Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(initialClock));
                Assert.That(GameSessionState.IsGameTimeRunning, Is.True);
                Assert.That(GameSessionState.HungerLevel, Is.EqualTo(hunger));
                Assert.That(GameSessionState.FatigueLevel, Is.EqualTo(fatigue));
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash));
                AssertInventory(inventory);
                dressing.ValidateOrThrow(home.Layout, home.BalconyLayout);
                if (day == 7)
                {
                    Assert.That(dressing.VisiblePartCount, Is.GreaterThan(firstDayCount),
                        "The last day's deterioration must exist in actual room geometry.");
                    Assert.That(VisibleDecor(home, imported).SetEquals(firstDayParts), Is.False);
                }
            }
            Assert.That(VisibleDecor(home, imported).SetEquals(firstDayParts), Is.True,
                "Returning to day one must restore its exact active dressing, not retain late-day clutter.");
            Assert.That(home.DebugWindow.Close(), Is.True);

            using (var captures = new HomeCaptures(camera))
            {
                for (int day = 1; day <= 7; day++)
                {
                    SelectDay(day);
                    home.Player.Motor.Teleport(initialPosition);
                    home.Player.GameObject.transform.rotation = initialRotation;
                    home.FixedCamera.ReapplyActiveShot();
                    home.DayNight.RefreshImmediate();
                    yield return null;
                    yield return null;
                    Assert.That(home.FixedCamera.ActiveShotKind, Is.EqualTo(HomeCameraShotKind.MainRoom));
                    captures.Write($"day-{day:00}-main");
                    if (day == 1 || day == 7)
                    {
                        CaptureRoomDetails(captures, $"day-{day:00}");
                        home.FixedCamera.RestoreGameplayCamera();
                    }
                }

                foreach (int day in new[] { 1, 7 })
                {
                    SelectDay(day);
                    Rect bathroom = home.Layout.BathroomBounds;
                    home.Player.Motor.Teleport(new Vector3(
                        bathroom.xMin + 0.64f, PlayerFactory.GroundedRootOffset,
                        bathroom.yMin + 1.10f));
                    home.FixedCamera.ReapplyActiveShot();
                    home.DayNight.RefreshImmediate();
                    yield return null;
                    yield return null;
                    Assert.That(home.FixedCamera.ActiveShotKind, Is.EqualTo(HomeCameraShotKind.Bathroom));
                    captures.Write($"day-{day:00}-bathroom");

                    Rect balcony = home.BalconyLayout.BalconyCameraActivationBounds;
                    home.Player.Motor.Teleport(new Vector3(
                        balcony.center.x, PlayerFactory.GroundedRootOffset, balcony.center.y));
                    home.FixedCamera.ReapplyActiveShot();
                    home.DayNight.RefreshImmediate();
                    yield return null;
                    yield return null;
                    Assert.That(home.FixedCamera.ActiveShotKind, Is.EqualTo(HomeCameraShotKind.Balcony));
                    captures.Write($"day-{day:00}-balcony");
                }

                PlayerDoorActionTarget doorTarget = home.LockedRoomDoor.GetComponent<PlayerDoorActionTarget>();
                home.Player.Motor.Teleport(doorTarget.Plan.EntryRootPosition);
                home.Player.GameObject.transform.rotation = doorTarget.Plan.EntryRotation;
                home.FixedCamera.ReapplyActiveShot();
                home.DayNight.RefreshImmediate();
                yield return null;
                yield return null;
                Assert.That(home.FixedCamera.ActiveShotKind, Is.EqualTo(HomeCameraShotKind.MainRoom));
                captures.Write("day-07-closed-room-approach");
            }

            Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(initialClock));
            AssertInventory(inventory);
            capturePause.Dispose();
            capturePause = null;
            Physics.SyncTransforms();
            yield return WaitFor(() => ReferenceEquals(home.Player.Interactor.ActiveInteractable, home.LockedRoomDoor),
                "The player at the new doorway cannot reach its real interaction.");
            Assert.That(home.InteractionPrompt.TryInvokePrompt(), Is.True);
            PlayerDoorActionController doorAction = home.Player.GameObject.GetComponent<PlayerDoorActionController>();
            Assert.That(doorAction.IsPlaying, Is.True);
            Assert.That(home.DebugWindow.Open(), Is.False,
                "Changing the apartment must not interrupt a door gesture.");
            yield return WaitFor(() => home.InteractionPrompt.IsFeedbackVisibleAt(Time.unscaledTime),
                "The closed room did not answer the completed attempt with the missing-key thought.");
            Assert.That(home.InteractionPrompt.GetPromptKeyAt(Time.unscaledTime),
                Is.EqualTo("home.lockedRoomDoor.missingKey"));
            Assert.That(doorAction.IsPlaying, Is.False);
            Assert.That(SceneTransitionService.IsTransitioning, Is.False);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneIds.HomeInterior));
            AssertInventory(inventory);

            yield return ValidateSleepCycle(camera);
            yield return ValidateDiningCycle(camera);
            yield return ValidatePaintingInspection(camera);
            capturePause = GameTimeScaleRuntime.AcquirePause();
            double minutesUntilNight = 22d * 60d - GameSessionState.GameTimeOfDayMinutes;
            Assert.That(minutesUntilNight, Is.GreaterThan(0d));
            GameSessionState.AdvanceGameTime((float)(minutesUntilNight / GameTimeState.GameMinutesPerRealSecond));
            home.DayNight.RefreshImmediate();
            home.Player.Motor.Teleport(home.Layout.PlayerSpawn);
            home.Player.GameObject.transform.rotation = Quaternion.identity;
            Assert.That(home.FixedCamera.RestoreGameplayCamera(), Is.True);
            yield return null;
            yield return null;
            using (var captures = new HomeCaptures(camera))
            {
                captures.Write("night-main");
                CaptureRoomDetails(captures, "night");
            }
            Assert.That(home.FixedCamera.RestoreGameplayCamera(), Is.True);
            capturePause.Dispose();
            capturePause = null;
            yield return null;
            DebugFreeCamera freeCamera = home.Player.GameObject.GetComponent<DebugFreeCamera>();
            EnterFreeCamera(freeCamera);
            yield return null;
            using (var captures = new HomeCaptures(camera)) captures.Write("night-free-camera");
            Assert.That(freeCamera.Exit(), Is.True);
        }

        private IEnumerator ValidateSleepCycle(Camera camera)
        {
            HomeBedInteractionPlan plan = home.BedInteractionPlan;
            Vector3 approach = plan.EntryRootPosition + plan.EntryFacingDirection * .30f;
            home.Player.Motor.Teleport(approach);
            home.Player.GameObject.transform.rotation = plan.EntryRotation;
            home.FixedCamera.RestoreGameplayCamera();
            Physics.SyncTransforms();
            yield return null;
            var renderers = home.Player.Visual.Renderers;
            var visible = new bool[renderers.Count];
            for (int index = 0; index < visible.Length; index++) visible[index] = renderers[index].enabled;
            Assert.That(home.Bed.CanInteract(home.Player.Interactor), Is.True);
            Vector3 start = home.Player.GameObject.transform.position;
            home.Bed.Interact(home.Player.Interactor);
            Assert.That(home.Bed.OwnsActiveInteraction, Is.True);
            Assert.That(home.Player.GameObject.transform.position, Is.EqualTo(start),
                "The narrowed sofa still starts with a visible guided approach.");
            DebugFreeCamera sleepingFlight = home.Player.GameObject.GetComponent<DebugFreeCamera>();
            EnterFreeCamera(sleepingFlight);
            yield return WaitFor(() => home.Bed.OwnsActiveInteraction &&
                home.Bed.Controller.Phase == PlayerAnimatedInteractionPhase.Looping,
                "The hero could not enter the narrowed sofa and reach its real sleep loop.");
            Assert.That(sleepingFlight.IsActive, Is.True, "The sleep approach must continue during free flight.");
            Assert.That(sleepingFlight.Exit(), Is.True);
            yield return null;
            yield return null;
            var hero = (Player3DCharacterPresentation)home.Player.Visual;
            Assert.That(Vector3.Distance(hero.Registry.Anchors.Pelvis.position, plan.ActionHipPosition),
                Is.LessThan(.03f), "The full-size hero must sleep at the narrowed mattress's contact target.");
            Assert.That(home.BedSurface.MattressModel.BodyWeight, Is.GreaterThan(.99f));
            Assert.That(Vector3.Dot(hero.Registry.Anchors.Head.position - hero.Registry.Anchors.Pelvis.position,
                plan.HeadToFootAxis), Is.LessThan(-.35f));
            AssertSleepingHeadSupport(hero);
            using (var captures = new HomeCaptures(camera))
            {
                CaptureDetail(captures, "day-07-narrow-sofa-sleeping", new Vector3(-3.90f, 1.65f, -.20f),
                    new Vector3(-6.285f, .65f, -.175f), 62f);
                home.FixedCamera.RestoreGameplayCamera();
            }
            Assert.That(home.Bed.RequestWake(), Is.True);
            yield return WaitFor(() => !home.Bed.OwnsActiveInteraction && !home.Bed.Controller.IsActive &&
                home.Player.Motor.InputEnabled && home.Player.Interactor.InputEnabled,
                "The narrowed sofa's real wake did not restore its independent grounded exit.");
            yield return null;
            Assert.That(Vector3.Distance(home.Player.GameObject.transform.position, plan.ExitRootPosition), Is.LessThan(.03f));
            Assert.That(Quaternion.Angle(home.Player.GameObject.transform.rotation, plan.ExitRotation), Is.LessThan(.1f));
            for (int index = 0; index < visible.Length; index++)
                Assert.That(renderers[index].enabled, Is.EqualTo(visible[index]));
            Assert.That(hero.InteractionHandoffLocked, Is.False,
                "The completed wake must release contextual visual ownership before ordinary debug controls resume.");
            using (var captures = new HomeCaptures(camera)) captures.Write("day-07-narrow-sofa-awake");
        }

        private void AssertSleepingHeadSupport(Player3DCharacterPresentation hero)
        {
            Assert.That(hero.Registry.TryGetPart(Player3DAnatomicalPart.Head, out Player3DAnatomicalPartBinding head), Is.True);
            HomeBedDeformableSurface pillow = home.Room.Find("Home Pillow").GetComponent<HomeBedDeformableSurface>();
            var probe = new Mesh { name = "Test sleeping sofa contact probe" };
            var vertices = new List<Vector3>();
            float lowestSupportGap = float.PositiveInfinity;
            int measured = 0;
            try
            {
                foreach (Player3DMeshBinding binding in hero.Registry.MeshBindings)
                {
                    if (binding?.Renderer == null ||
                        (binding.Renderer != head.Renderer && binding.MeshName != "GEO_HairBack")) continue;
                    Renderer renderer = binding.Renderer;
                    Assert.That(renderer, Is.TypeOf<SkinnedMeshRenderer>());
                    probe.Clear(false);
                    ((SkinnedMeshRenderer)renderer).BakeMesh(probe, true);
                    vertices.Clear();
                    probe.GetVertices(vertices);
                    Vector3 lowest = new Vector3(0f, float.PositiveInfinity, 0f);
                    foreach (Vector3 vertex in vertices)
                    {
                        Vector3 point = renderer.transform.TransformPoint(vertex);
                        if (point.y < lowest.y) lowest = point;
                    }
                    Assert.That(pillow.ContainsPlanar(lowest), Is.True,
                        $"The narrowed pillow must physically support {binding.MeshName}.");
                    float gap = lowest.y - home.BedSurface.GetSurfaceHeight(lowest);
                    Assert.That(gap, Is.GreaterThan(-.03f), "The head must not sink through the narrowed pillow.");
                    lowestSupportGap = Mathf.Min(lowestSupportGap, gap);
                    measured++;
                }
                Assert.That(measured, Is.EqualTo(2));
                Assert.That(lowestSupportGap, Is.LessThan(.03f), "The sleeping head must meet its live deformed support.");
            }
            finally { Object.DestroyImmediate(probe); }
        }

        private IEnumerator ValidateDiningCycle(Camera camera)
        {
            CityBenchSitInteraction seat = home.DiningSeat;
            Assert.That(seat, Is.Not.Null, "The authored chair must expose the shared sitting interaction.");
            Assert.That(seat.Plan.Kind, Is.EqualTo(CityBenchSeatKind.DiningChair));
            Assert.That(seat.PromptKey, Is.EqualTo(CityBenchSitInteraction.DiningPromptKey));
            home.Player.Motor.Teleport(home.Layout.PlayerSpawn);
            home.Player.GameObject.transform.rotation = Quaternion.identity;
            Physics.SyncTransforms();
            yield return null;
            DebugFreeCamera freeCamera = home.Player.GameObject.GetComponent<DebugFreeCamera>();
            Assert.That(freeCamera, Is.Not.Null, "Every gameplay player must install the global free camera.");
            Pose ordinaryPose = new Pose(camera.transform.position, camera.transform.rotation);
            float ordinaryLens = camera.fieldOfView;
            yield return EnterFreeCameraWithF4(freeCamera, camera);
            Assert.That(home.Player.Motor.InputEnabled, Is.True,
                "Flight must reserve physical input without replacing the motor owner's state.");
            Assert.That(home.Player.Interactor.InputEnabled, Is.True);
            Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.False);
            yield return null;
            using (var captures = new HomeCaptures(camera)) captures.Write("day-07-free-camera");
            Assert.That(freeCamera.Exit(), Is.True);
            Assert.That(Vector3.Distance(camera.transform.position, ordinaryPose.position), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(camera.transform.rotation, ordinaryPose.rotation), Is.LessThan(.01f));
            Assert.That(camera.fieldOfView, Is.EqualTo(ordinaryLens).Within(.001f));
            yield return null;
            yield return null;
            // This admitted front-edge position previously chose the outdoor
            // table detour at z=2.05 and stalled against the locked room.
            home.Player.Motor.Teleport(new Vector3(-.88f, PlayerFactory.GroundedRootOffset, -.80f));
            home.Player.GameObject.transform.rotation = Quaternion.identity;
            Physics.SyncTransforms();
            yield return null;
            var rendererStates = new bool[home.Player.Visual.Renderers.Count];
            for (int index = 0; index < rendererStates.Length; index++)
                rendererStates[index] = home.Player.Visual.Renderers[index].enabled;
            bool shadowEnabled = home.Player.ContactShadow.enabled;
            Assert.That(seat.CanInteract(home.Player.Interactor), Is.True);
            Vector3 start = home.Player.GameObject.transform.position;
            seat.Interact(home.Player.Interactor);
            Assert.That(seat.OwnsActiveInteraction, Is.True);
            Assert.That(home.Player.GameObject.transform.position, Is.EqualTo(start),
                "Taking the chair starts the shared guided walk without teleporting the hero.");
            EnterFreeCamera(freeCamera);
            yield return WaitFor(() => seat.IsSeated &&
                seat.Controller.Phase == PlayerAnimatedInteractionPhase.Looping,
                "The real hero could not walk around the chair and sit at the dining table.");
            Assert.That(freeCamera.IsActive, Is.True, "Sitting must complete without closing debug flight.");
            Assert.That(freeCamera.Exit(), Is.True);
            yield return null;
            yield return null;
            var hero = (Player3DCharacterPresentation)home.Player.Visual;
            Assert.That(Vector3.Distance(hero.Registry.Anchors.Pelvis.position, seat.Plan.ActionHipPosition),
                Is.LessThan(.03f), "The seated pelvis must meet the visible chair rather than float beside it.");
            Assert.That(home.Player.Motor.InputEnabled, Is.False);
            Assert.That(home.CameraFollow.FixedPoseActive, Is.True);
            using (var captures = new HomeCaptures(camera))
            {
                CaptureDetail(captures, "day-07-dining-seated", new Vector3(-.6f, 1.8f, -2.9f),
                    new Vector3(-1.925f, .85f, -.8f), 60f);
                home.FixedCamera.RestoreGameplayCamera();
            }
            yield return StandFromDiningSeatWithE(seat);
            yield return WaitFor(() => !seat.OwnsActiveInteraction && !seat.Controller.IsActive &&
                home.Player.Motor.InputEnabled && home.Player.Interactor.InputEnabled,
                "Standing from the dining chair failed to restore the authored exit and ordinary input.");
            yield return null;
            yield return null;
            Assert.That(Vector3.Distance(home.Player.GameObject.transform.position, seat.Plan.EntryRootPosition),
                Is.LessThan(.03f));
            Assert.That(home.CameraFollow.FixedPoseActive, Is.True);
            Assert.That(home.Player.ContactShadow.enabled, Is.EqualTo(shadowEnabled));
            for (int index = 0; index < rendererStates.Length; index++)
                Assert.That(home.Player.Visual.Renderers[index].enabled, Is.EqualTo(rendererStates[index]));
            Assert.That(home.Player.Interactor.ActiveInteractable, Is.SameAs(home.Painting),
                "Standing must release the seat's filter so the nearby painting becomes available again.");
            EnterFreeCamera(freeCamera);
            using (var captures = new HomeCaptures(camera)) captures.Write("day-07-dining-stood-up-free-camera");
            Assert.That(freeCamera.Exit(), Is.True);
            Assert.That(home.CameraFollow.FixedPoseActive, Is.True);
        }

        private IEnumerator StandFromDiningSeatWithE(CityBenchSitInteraction seat)
        {
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                EnsureFreeCameraInputFocus();
                yield return null;
                yield return null;
                Assert.That(home.Player.Interactor.ActiveInteractable, Is.SameAs(seat),
                    "The closer painting must not replace the action owned by the seated hero.");
                Assert.That(home.Player.Interactor.PromptView.PromptKey,
                    Is.EqualTo(CityBenchSitInteraction.StandPromptKey));
                Assert.That(home.Player.Interactor.PromptView.IsClickable, Is.True);
                Assert.That(home.Painting.IsPresenting, Is.False);
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.eKey, queueEventOnly: true);
                yield return null;
                Assert.That(seat.Controller.Phase, Is.EqualTo(PlayerAnimatedInteractionPhase.Exiting),
                    "Real E input must stand from the chair through its normal interaction route.");
                Assert.That(home.Painting.IsPresenting, Is.False);
            }
            finally
            {
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        private void EnterFreeCamera(DebugFreeCamera freeCamera)
        {
            EnsureFreeCameraInputFocus();
            Assert.That(freeCamera.TryEnter(), Is.True,
                "The released Home player must admit global free flight: " + FreeCameraAdmissionState());
        }

        private IEnumerator EnterFreeCameraWithF4(DebugFreeCamera freeCamera, Camera camera)
        {
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                EnsureFreeCameraInputFocus();
                yield return null;
                input.Press(keyboard.f4Key, queueEventOnly: true);
                yield return null;
                Assert.That(freeCamera.IsActive, Is.True,
                    "Raw F4 must enter free flight with the real Home fixed camera: " + FreeCameraAdmissionState());
                input.Release(keyboard.f4Key, queueEventOnly: true);
                yield return null;
                Vector3 cameraStart = camera.transform.position;
                Vector3 heroStart = home.Player.GameObject.transform.position;
                input.Press(keyboard.dKey, queueEventOnly: true);
                for (int frame = 0; frame < 3; frame++) yield return null;
                input.Release(keyboard.dKey, queueEventOnly: true);
                yield return null;
                Assert.That(Vector3.Distance(cameraStart, camera.transform.position), Is.GreaterThan(.02f),
                    "The Home fixed shot must let raw flight input move the actual camera.");
                Assert.That(Vector3.Distance(heroStart, home.Player.GameObject.transform.position), Is.LessThan(.001f),
                    "Flight input must leave the hero in the world without moving him.");
            }
            finally
            {
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        private void EnsureFreeCameraInputFocus()
        {
            // Batch rendering has no focused game window. The dedicated raw
            // control fixture supplies this same input focus explicitly.
            if (Application.isBatchMode)
            {
                batchFocusBeforeTest ??= GameCursor.ApplicationFocused;
                GameInput.HandleApplicationFocus(true);
            }
        }

        private string FreeCameraAdmissionState()
        {
            var hero = (Player3DCharacterPresentation)home.Player.Visual;
            return $"focus={GameCursor.ApplicationFocused}, " +
                $"gameplay={GameInput.CanRead(GameInputContext.Gameplay)}, " +
                $"paused={GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused}, " +
                $"transition={SceneTransitionService.IsTransitioning}, modal={BarMinigameModalLock.IsAnyLocked}, " +
                $"flightRelease={DebugFreeCamera.BlocksWorldInput}, motor={home.Player.Motor.InputEnabled}, " +
                $"interactor={home.Player.Interactor.InputEnabled}, handoff={hero.InteractionHandoffLocked}, " +
                $"animation={home.AnimatedInteraction.IsActive}, rise={hero.RisePose.Active}, " +
                $"nausea={hero.NauseaPose.Active}, vomit={hero.VomitPose.Active}, " +
                $"cameraSuspended={home.CameraFollow.PresentationSuspended}, orbit={home.CameraFollow.OrbitInputEnabled}";
        }

        private IEnumerator ValidatePaintingInspection(Camera camera)
        {
            NarrativeInteraction painting = home.Painting;
            Assert.That(painting, Is.Not.Null, "The locked room's wall must offer the shared painting inspection.");
            Assert.That(painting.Definition.IsSilent, Is.True);
            Assert.That(painting.Definition.Pages, Is.Empty);
            Bounds frame = BoundsOf(home, HomeWallPainting.FrameName);
            Bounds image = BoundsOf(home, HomeWallPainting.ImageName);
            Assert.That(Vector3.Distance(frame.center, new Vector3(-.97f, 1.98f, .745f)), Is.LessThan(.01f));
            Assert.That(frame.size.x, Is.EqualTo(1.40f).Within(.01f));
            Assert.That(frame.size.y, Is.EqualTo(.82f).Within(.01f));
            Assert.That(image.size.x / image.size.y, Is.EqualTo(1672f / 941f).Within(.002f),
                "The supplied picture must keep its original aspect inside the visible frame.");
            Assert.That(image.max.z, Is.LessThan(frame.center.z), "The picture must face the accessible south side.");
            Renderer imageRenderer = painting.SubjectRoot.Find(HomeWallPainting.ImageName).GetComponent<Renderer>();
            var properties = new MaterialPropertyBlock();
            imageRenderer.GetPropertyBlock(properties);
            Assert.That(properties.GetTexture("_BaseMap"),
                Is.SameAs(Resources.Load<Texture2D>(HomeWallPainting.TexturePath)));
            Dictionary<InventoryItemId, int> inventory = SnapshotInventory();
            var controller = NarrativeInteractionController.For(home.Player.Interactor);
            DebugFreeCamera freeCamera = home.Player.GameObject.GetComponent<DebugFreeCamera>();
            for (int attempt = 0; attempt < 2; attempt++)
            {
                // Let the preceding modal's release key finish before taking
                // another owner, then visibly walk the final stretch to its dock.
                yield return null;
                yield return null;
                home.Player.Motor.Teleport(painting.Staging.Entry.RootPosition + Vector3.back * .20f);
                home.Player.GameObject.transform.rotation = painting.Staging.Entry.RootRotation;
                home.FixedCamera.RestoreGameplayCamera();
                Physics.SyncTransforms();
                yield return WaitFor(() => ReferenceEquals(home.Player.Interactor.ActiveInteractable, painting),
                    "The painting's accessible anchor did not offer its real E prompt.");
                Vector3 start = home.Player.GameObject.transform.position;
                Pose ordinaryCamera = new Pose(camera.transform.position, camera.transform.rotation);
                float ordinaryLens = camera.fieldOfView;
                double beforeInspection = GameSessionState.GameTimeOfDayMinutes;
                Assert.That(home.InteractionPrompt.TryInvokePrompt(), Is.True);
                Assert.That(controller.IsActive, Is.True, controller.LastFailureReason);
                Assert.That(home.Player.GameObject.transform.position, Is.EqualTo(start),
                    "Inspecting the wall painting must start its real guided approach.");
                EnterFreeCamera(freeCamera);
                yield return WaitFor(() => controller.Phase == NarrativeInteractionPhase.Reading,
                    "The painting never reached its silent inspection frame: " + controller.LastFailureReason);
                Assert.That(freeCamera.IsActive, Is.True, "Inspection staging must continue beneath debug flight.");
                Assert.That(freeCamera.Exit(), Is.True);
                yield return null;
                yield return null;
                Assert.That(painting.IsPresenting, Is.True);
                Assert.That(controller.CurrentPage.TextKey, Is.Null.Or.Empty);
                Assert.That(home.InteractionPrompt.HasHeldPage, Is.False,
                    "The painting must show its image without a thought, caption or document panel.");
                Assert.That(GameTimeScaleRuntime.IsPaused, Is.False);
                Assert.That(Time.timeScale, Is.GreaterThan(0f));
                Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.GreaterThan(beforeInspection),
                    "Inspecting the image must leave the ordinary world clock running.");
                AssertInventory(inventory);
                if (attempt == 0)
                {
                    using (var captures = new HomeCaptures(camera)) captures.Write("painting-inspection");
                    Assert.That(controller.Confirm(), Is.True, "E must close the silent inspection without requiring a page.");
                }
                else controller.Cancel();
                yield return WaitFor(() => !controller.IsActive && !painting.IsPresenting &&
                    home.Player.Motor.InputEnabled && home.Player.Interactor.InputEnabled,
                    "Closing the silent painting inspection did not release its animation, camera and hero input.");
                yield return null;
                Assert.That(home.InteractionPrompt.HasHeldPage, Is.False);
                Assert.That(home.CameraFollow.FixedPoseActive, Is.True);
                Assert.That(Vector3.Distance(camera.transform.position, ordinaryCamera.position), Is.LessThan(.01f));
                Assert.That(Quaternion.Angle(camera.transform.rotation, ordinaryCamera.rotation), Is.LessThan(.1f));
                Assert.That(camera.fieldOfView, Is.EqualTo(ordinaryLens).Within(.01f));
                Assert.That(Vector3.Distance(home.Player.GameObject.transform.position, painting.Staging.Exit.RootPosition),
                    Is.LessThan(.03f));
                AssertInventory(inventory);
            }
        }

        private void CaptureRoomDetails(HomeCaptures captures, string prefix)
        {
            CaptureDetail(captures, prefix + "-painting", new Vector3(HomeWallPainting.FrameCenter.x, 1.98f, -.40f),
                HomeWallPainting.FrameCenter, 52f);
            CaptureDetail(captures, prefix + "-kitchen", new Vector3(-4.65f, 2.05f, .65f),
                new Vector3(-4.95f, 1.05f, 2.90f), 66f);
            CaptureDetail(captures, prefix + "-stove", new Vector3(-3.65f, 1.90f, 1.10f),
                new Vector3(-3.685f, .88f, 2.85f), 48f);
            CaptureDetail(captures, prefix + "-sleeping-sofa", new Vector3(-3.80f, 1.70f, -.15f),
                new Vector3(-6.285f, .70f, -.175f), 65f);
            CaptureDetail(captures, prefix + "-bookcase", new Vector3(-4.45f, 1.55f, -2.50f),
                new Vector3(-6.31f, 1.10f, -3.435f), 62f);
            CaptureDetail(captures, prefix + "-sofa-table-closed-room", new Vector3(-.90f, 2.80f, -3.40f),
                new Vector3(-3.55f, 1.02f, -.05f), 78f);
            CaptureDetail(captures, prefix + "-closed-room-door", new Vector3(.65f, 1.65f, -1.90f),
                new Vector3(.68f, 1.05f, .855f), 52f);
        }

        private void CaptureDetail(HomeCaptures captures, string name, Vector3 position, Vector3 lookAt, float fieldOfView)
        {
            Vector3 worldPosition = home.transform.TransformPoint(position);
            Vector3 direction = home.transform.TransformPoint(lookAt) - worldPosition;
            home.CameraFollow.SetFixedPose(worldPosition, Quaternion.LookRotation(direction), fieldOfView);
            home.PlayerOcclusion.ClearOcclusion();
            captures.Write(name);
        }

        private IEnumerator CaptureAndCompleteOpening(Camera camera)
        {
            Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
            Assert.That(home.AlarmClock.DisplayedTime, Is.EqualTo("05:59"));
            Assert.That(home.DebugWindow.Open(), Is.False,
                "The debug window must leave the sleeping prologue in control.");
            Assert.That(home.DebugWindow.TrySetGameDayNumber(7), Is.False);
            using (var captures = new HomeCaptures(camera))
            {
                yield return WaitFor(() => home.AlarmClock.DisplayVisible,
                    "The opening clock never showed its digits.");
                captures.Write("opening-01-alarm");
                yield return WaitFor(() => home.Opening.Phase == HomeOpeningPhase.AwaitingWake,
                    "The ordinary five-second opening did not reveal its wake action.");
                Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
                Assert.That(home.DebugWindow.Open(), Is.False);
                Assert.That(home.Opening.TryWake(), Is.True);
                Assert.That(home.Opening.Phase, Is.EqualTo(HomeOpeningPhase.AlarmRinging));
                Assert.That(home.AlarmClock.IsRinging, Is.True);
                Assert.That(GameSessionState.IsGameTimeRunning, Is.True);
                Assert.That(home.DebugWindow.Open(), Is.False);

                yield return WaitFor(() =>
                    home.Opening.Phase == HomeOpeningPhase.Waking &&
                    home.Opening.Timeline.PhaseElapsedSeconds >=
                        HomeOpeningTimeline.WakeCameraTransitionSeconds,
                    "The wake never reached its authored sleeper camera.");
                Assert.That(home.DebugWindow.Open(), Is.False,
                    "The debug window must wait for the entire wake animation.");
                captures.Write("opening-02-waking");

                yield return WaitFor(() =>
                    home.Opening.Phase == HomeOpeningPhase.Complete &&
                    !HomeApartmentDayController.IsHomeBusy(home),
                    "The authored wake did not return the hero to ordinary Home control.");
                Assert.That(home.AlarmClock.IsRinging, Is.False);
                Assert.That(home.Player.Motor.InputEnabled, Is.True);
                Assert.That(home.Player.Interactor.InputEnabled, Is.True);
                Assert.That(home.FixedCamera.ActiveShotKind, Is.EqualTo(HomeCameraShotKind.MainRoom));
                Assert.That(home.DebugWindow.Open(), Is.True);
                Assert.That(home.DebugWindow.Close(), Is.True);
                captures.Write("opening-03-awake");
            }
        }

        private void SelectDay(int day)
        {
            Assert.That(home.DebugWindow.Open(), Is.True);
            if (GameSessionState.GameDayNumber != day)
                Assert.That(home.DebugWindow.TrySetGameDayNumber(day), Is.True);
            Assert.That(home.ApartmentDays.AppliedDayNumber, Is.EqualTo(day));
            Assert.That(home.DebugWindow.Close(), Is.True);
        }

        private static Dictionary<Mesh, HomeAuthoredPart> ValidateImportedGeometry(HomeInteriorModelLibrary library)
        {
            Assert.That(library.Parts, Is.Not.Empty);
            Assert.That(library.BuildSignature, Is.Not.Null.And.Not.Empty);
            var imported = new Dictionary<Mesh, HomeAuthoredPart>();
            foreach (HomeAuthoredPart part in library.Parts)
            {
                Assert.That(part.mesh, Is.Not.Null, $"Missing imported mesh: {part.name}");
                Assert.That(part.mesh.vertexCount, Is.GreaterThan(0), part.name);
                Assert.That(Vector3.Distance(part.mesh.bounds.min, HomeAuthoredPart.Vector(part.bounds_min)),
                    Is.LessThanOrEqualTo(0.012f), $"Wrong imported minimum/axis/scale: {part.name}");
                Assert.That(Vector3.Distance(part.mesh.bounds.max, HomeAuthoredPart.Vector(part.bounds_max)),
                    Is.LessThanOrEqualTo(0.012f), $"Wrong imported maximum/axis/scale: {part.name}");
                Assert.That(part.Size.x, Is.GreaterThan(0f), part.name);
                Assert.That(part.Size.y, Is.GreaterThan(0f), part.name);
                Assert.That(part.Size.z, Is.GreaterThan(0f), part.name);
                imported.Add(part.mesh, part);
            }
            return imported;
        }

        private static void ValidateLiveGeometry(HomeInteriorRoot root, Dictionary<Mesh, HomeAuthoredPart> imported)
        {
            int authoredRenderers = 0;
            int collisionPairs = 0;
            foreach (MeshFilter filter in root.Room.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || !imported.ContainsKey(filter.sharedMesh)) continue;
                authoredRenderers++;
                Renderer renderer = filter.GetComponent<Renderer>();
                Assert.That(renderer, Is.Not.Null, filter.name);
                BoxCollider collision = filter.GetComponent<BoxCollider>();
                if (collision == null || collision.isTrigger || !filter.gameObject.activeInHierarchy) continue;
                collisionPairs++;
                Assert.That(renderer.bounds.Intersects(collision.bounds), Is.True,
                    $"Authored visual and plan-owned collision do not meet: {filter.name}");
            }
            Assert.That(authoredRenderers, Is.GreaterThan(20), "Home did not instantiate its authored environment.");
            Assert.That(collisionPairs, Is.GreaterThan(5), "No real authored structure was checked against collision.");
            Assert.That(root.Layout.RoomSize, Is.EqualTo(new Vector2(12f, 8f)));
            Assert.That(root.Layout.RoomBounds.center, Is.EqualTo(new Vector2(-1f, 0f)));
            Bounds floor = BoundsOf(root, "Home Floor");
            Assert.That(floor.center.x, Is.EqualTo(-1f).Within(.01f));
            Assert.That(floor.size.x, Is.EqualTo(12f).Within(.01f));
            Assert.That(floor.size.z, Is.EqualTo(8f).Within(.01f));
            Bounds bed = BoundsOf(root, "Home Bed Frame");
            Assert.That(bed.min.x, Is.EqualTo(-6.86f).Within(.01f));
            Assert.That(bed.max.x, Is.EqualTo(-5.71f).Within(.01f));
            Assert.That(bed.size.x, Is.EqualTo(1.15f).Within(.01f),
                "Only sofa depth narrows; its full sleeping length remains unchanged.");
            Assert.That(bed.min.z, Is.EqualTo(-1.45f).Within(.01f));
            Assert.That(bed.max.z, Is.EqualTo(1.10f).Within(.01f));
            Assert.That(root.AlarmClockPlan.ClockPosition.x, Is.EqualTo(bed.center.x).Within(.001f));
            Bounds back = BoundsOf(root, "Home Sleeping Sofa Back");
            Bounds leftArm = BoundsOf(root, "Home Sleeping Sofa Arm Left");
            Bounds rightArm = BoundsOf(root, "Home Sleeping Sofa Arm Right");
            Assert.That(back.center.x, Is.LessThan(bed.center.x),
                "The sleeping sofa's backrest must face the west wall.");
            Assert.That(back.max.y, Is.GreaterThan(1f));
            Assert.That(leftArm.min.z, Is.EqualTo(bed.min.z).Within(.02f));
            Assert.That(rightArm.max.z, Is.EqualTo(bed.max.z).Within(.02f));
            Assert.That(root.Room.Find("Home Sofa Base"), Is.Null,
                "The hero sleeps on the single sofa; the obsolete second sofa must be gone.");
            Assert.That(root.Layout.TryGetFurniture(HomeFurnitureKind.Sofa, out _), Is.False);
            Bounds cupboard = BoundsOf(root, "Home Battered Cabinet");
            Assert.That(cupboard.min.x, Is.EqualTo(-6.86f).Within(.01f));
            Assert.That(cupboard.max.x, Is.EqualTo(-5.76f).Within(.01f));
            Assert.That(cupboard.min.z, Is.EqualTo(-3.76f).Within(.01f));
            Assert.That(cupboard.max.z, Is.EqualTo(-3.11f).Within(.01f));
            Assert.That(Vector3.Dot(root.Room.Find("Home Battered Cabinet").TransformDirection(Vector3.back),
                Vector3.right), Is.GreaterThan(.99f), "The bookcase shelves must face east into the room.");
            Assert.That(root.Layout.TryGetFurniture(HomeFurnitureKind.CameraCornerJunk, out _), Is.False);
            Assert.That(root.Room.Find("Home Camera Corner Junk Base"), Is.Null);
            Assert.That(root.Room.Find("Home Paired Slipper 0"), Is.Null);
            Assert.That(root.Room.Find("Home Paired Slipper 1"), Is.Null);
            Assert.That(root.OcclusionRegistry.TryGetGroup("home.furniture.camera-junk", out _), Is.False,
                "The removed junk pile must not leave an occlusion owner behind.");
            Assert.That(root.Layout.TryGetFurniture(HomeFurnitureKind.Kitchen, out HomeFurnitureFootprint kitchen), Is.True);
            Assert.That(kitchen.Bounds.xMin, Is.EqualTo(-6.65f).Within(.001f));
            Assert.That(kitchen.Bounds.xMax, Is.EqualTo(-3.28f).Within(.001f));
            Assert.That(kitchen.Bounds.yMin, Is.EqualTo(2.48f).Within(.001f));
            Assert.That(kitchen.Bounds.yMax, Is.EqualTo(3.30f).Within(.001f));
            Bounds stove = BoundsOf(root, "Home Stove Enamel Top");
            Assert.That(stove.center.x, Is.EqualTo(-3.685f).Within(.01f));
            Assert.That(stove.size.x, Is.EqualTo(.72f).Within(.01f));
            BoundsOf(root, "Home Stove Oven Glass");
            for (int index = 0; index < 4; index++) BoundsOf(root, $"Home Stove Burner {index}");
            BoxCollider stoveCounter = root.Room.Find("Home Kitchen Counter Right").GetComponent<BoxCollider>();
            Assert.That(stoveCounter, Is.Not.Null);
            Assert.That(stoveCounter.bounds.Contains(new Vector3(stove.center.x, .7f, stove.center.z)), Is.True,
                "The visible stove must share the real blocking kitchen fixture.");
            Vector3 dock = HomeBedInteractionPlan.Create(root.Layout).EntryRootPosition;
            foreach (HomeFurnitureFootprint item in root.Layout.Furniture)
            {
                if (!item.BlocksMovement) continue;
                var closest = new Vector2(Mathf.Clamp(dock.x, item.Bounds.xMin, item.Bounds.xMax),
                    Mathf.Clamp(dock.z, item.Bounds.yMin, item.Bounds.yMax));
                Assert.That(Vector2.Distance(new Vector2(dock.x, dock.z), closest),
                    Is.GreaterThan(HomeInteriorLayoutValidator.PlayerClearanceRadius),
                    $"Moving the bed must keep its standing approach clear of {item.Id}.");
            }
        }

        private static void ValidateBedSurfaces(HomeInteriorRoot root)
        {
            Assert.That(root.BedSurface, Is.Not.Null, "The migration lost the bed's runtime deformation binding.");
            Assert.That(root.BedSurface.MattressModel, Is.Not.Null);
            Assert.That(root.BedSurface.PillowModel, Is.Not.Null);
            HomeBedDeformableSurface[] surfaces = root.Room.GetComponentsInChildren<HomeBedDeformableSurface>(true);
            Assert.That(surfaces, Has.Length.EqualTo(2));
            foreach (HomeBedDeformableSurface surface in surfaces)
            {
                Assert.That(surface.Mesh, Is.Not.Null);
                Assert.That(surface.Columns, Is.GreaterThan(1));
                Assert.That(surface.Rows, Is.GreaterThan(1));
                Assert.That(surface.TopVertexCount, Is.LessThanOrEqualTo(surface.VertexCount));
                Assert.That(surface.Mesh.isReadable, Is.True, surface.name);
            }
        }

        private static void ValidateLockedDoor(HomeInteriorRoot root)
        {
            LockedDoorInteraction door = root.LockedRoomDoor;
            Assert.That(door, Is.Not.Null);
            Assert.That(door.IsConfigured, Is.True);
            Assert.That(door.PromptKey, Is.EqualTo("home.lockedRoomDoor.prompt"));
            Assert.That(door.LockedKey, Is.EqualTo("home.lockedRoomDoor.missingKey"));
            Assert.That(LocalizationService.Get(door.LockedKey), Is.Not.EqualTo(door.LockedKey));
            Assert.That(door.GetComponent<BoxCollider>(), Is.Not.Null);
            Assert.That(door.GetComponent<BoxCollider>().isTrigger, Is.True);
            Assert.That(door.GetComponent<PlayerDoorActionTarget>().IsConfigured, Is.True);
            Assert.That(door.GetComponent<HomeExit>(), Is.Null, "The shut room must not carry a destination exit.");
            Assert.That(door.GetComponent<HomeEntrance>(), Is.Null);
            Rect room = HomeLockedRoomPlan.Bounds;
            Assert.That(room.xMin, Is.EqualTo(-2.8f).Within(.001f));
            Assert.That(room.xMax, Is.EqualTo(1.46f).Within(.001f));
            Assert.That(room.width * room.height, Is.EqualTo(13.0356f).Within(.01f),
                "The existing story room must be enlarged rather than replaced with another accessible room.");
            BoxCollider solid = root.Room.Find("Home Locked Room Collision").GetComponent<BoxCollider>();
            Assert.That(solid, Is.Not.Null);
            Assert.That(solid.isTrigger, Is.False);
            Assert.That(solid.size.x, Is.EqualTo(room.width).Within(.001f));
            Assert.That(solid.size.z, Is.EqualTo(room.height).Within(.001f));
            Assert.That(solid.bounds.Contains(new Vector3(room.center.x, 1f, room.center.y)), Is.True);
            Ray ray = new Ray(HomeLockedRoomPlan.DockPosition + Vector3.up, Vector3.forward);
            Assert.That(solid.Raycast(ray, out RaycastHit hit, 2f), Is.True,
                "The shut door's actual approach must hit the inaccessible room's solid boundary.");
            Assert.That(hit.point.z, Is.EqualTo(room.yMin).Within(.01f));
        }

        private static void ValidateDiningClearance(HomeInteriorRoot root)
        {
            Bounds table = BoundsOf(root, "Home Scarred Table");
            Assert.That(Vector3.Distance(table.center, new Vector3(-1.925f, .78f, .15f)),
                Is.LessThan(.01f));
            Assert.That(table.size.x, Is.EqualTo(1.45f).Within(.01f));
            Assert.That(table.size.y, Is.EqualTo(.10f).Within(.01f));
            Assert.That(table.size.z, Is.EqualTo(1.20f).Within(.01f));
            Assert.That(table.min.x - BoundsOf(root, "Home Bed Frame").max.x,
                Is.GreaterThan(.80f), "The sofa-to-table aisle must fit the hero's capsule.");
            Assert.That(HomeLockedRoomPlan.Bounds.yMin - table.max.z, Is.InRange(.05f, .09f),
                "The dining table must sit against the closed room's partition.");
            BoundsOf(root, "Home Dining Table Base");
            Bounds chair = BoundsOf(root, HomeDiningSeatPlan.VisualName);
            Assert.That(chair.center.x, Is.EqualTo(table.center.x).Within(.01f));
            Assert.That(chair.max.y, Is.EqualTo(1.02f).Within(.01f));
            Physics.SyncTransforms();
            Assert.That(Physics.CheckSphere(new Vector3(table.center.x, .45f, table.center.z - .30f), .12f,
                ~0, QueryTriggerInteraction.Ignore), Is.False,
                "The table's visible legs must leave genuine empty knee space, not a solid pedestal collider.");
            HomeDiningSeatPlan.ValidateOrThrow(HomeDiningSeatPlan.Create(root.Layout), root.Layout);
        }

        private static Bounds BoundsOf(HomeInteriorRoot root, string name)
        {
            Transform part = root.Room.Find(name);
            if (part == null)
            {
                foreach (Transform child in root.Room.GetComponentsInChildren<Transform>(true))
                    if (child.name == name) { part = child; break; }
            }
            Assert.That(part, Is.Not.Null, $"Missing authored room part: {name}");
            Renderer renderer = part.GetComponent<Renderer>();
            Assert.That(renderer, Is.Not.Null, name);
            return renderer.bounds;
        }

        private static HashSet<MeshFilter> VisibleDecor(HomeInteriorRoot root, Dictionary<Mesh, HomeAuthoredPart> imported)
        {
            var result = new HashSet<MeshFilter>();
            foreach (MeshFilter filter in root.Room.GetComponentsInChildren<MeshFilter>(false))
                if (filter.sharedMesh != null && imported.TryGetValue(filter.sharedMesh, out HomeAuthoredPart part) &&
                    part.role == "decor" && filter.GetComponent<Renderer>().enabled)
                    result.Add(filter);
            return result;
        }

        private static Dictionary<InventoryItemId, int> SnapshotInventory()
        {
            var result = new Dictionary<InventoryItemId, int>();
            foreach (InventoryItemStack item in GameSessionState.InventoryItems)
                result.Add(item.ItemId, item.Count);
            return result;
        }

        private static void AssertInventory(Dictionary<InventoryItemId, int> expected)
        {
            Assert.That(GameSessionState.InventoryItems.Count, Is.EqualTo(expected.Count));
            foreach (KeyValuePair<InventoryItemId, int> item in expected)
                Assert.That(GameSessionState.GetInventoryItemCount(item.Key), Is.EqualTo(item.Value), item.Key.ToString());
        }

        private static IEnumerator WaitFor(Func<bool> condition, string message)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, message);
        }

        private sealed class HomeCaptures : IDisposable
        {
            private const int Width = 1280;
            private const int Height = 720;
            private readonly Camera camera;
            private readonly RenderTexture target = new RenderTexture(Width, Height, 24);
            private readonly Texture2D frame = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            private readonly string directory;

            public HomeCaptures(Camera sceneCamera)
            {
                camera = sceneCamera;
                directory = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "HomeAuthoredModel");
                Directory.CreateDirectory(directory);
            }

            public void Write(string name)
            {
                RenderTexture previousTarget = camera.targetTexture;
                RenderTexture previousActive = RenderTexture.active;
                try
                {
                    camera.targetTexture = target;
                    camera.Render();
                    RenderTexture.active = target;
                    frame.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                    frame.Apply();
                    string path = Path.Combine(directory, name + ".png");
                    File.WriteAllBytes(path, frame.EncodeToPNG());
                    Debug.Log($"Home authored capture wrote {path}");
                    Color32 first = frame.GetPixel(0, 0);
                    bool varied = false;
                    for (int y = 0; y < 16 && !varied; y++)
                        for (int x = 0; x < 16 && !varied; x++)
                        {
                            Color32 sample = frame.GetPixel(x * (Width - 1) / 15, y * (Height - 1) / 15);
                            varied = sample.r != first.r || sample.g != first.g || sample.b != first.b;
                        }
                    Assert.That(varied, Is.True, $"Home capture '{name}' is blank.");
                }
                finally
                {
                    camera.targetTexture = previousTarget;
                    RenderTexture.active = previousActive;
                }
            }

            public void Dispose()
            {
                Object.DestroyImmediate(frame);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}
