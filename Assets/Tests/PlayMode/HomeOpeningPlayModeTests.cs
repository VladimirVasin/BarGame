using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeOpeningPlayModeTests
    {
        private const float TimeoutSeconds = 20f;
        private InputTestFixture inputFixture;
        private Keyboard keyboard;
        private float previousTimeScale;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            inputFixture = new InputTestFixture();
            inputFixture.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            GameSessionState.BeginNewGame();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = previousTimeScale;
            GameSessionState.BeginNewGame();
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() &&
                (active.name == SceneIds.MainMenu ||
                 active.name == SceneIds.HomeInterior ||
                 active.name == SceneIds.DoorTransition ||
                 active.name == SceneIds.StairwellInterior ||
                 active.name == SceneIds.City))
            {
                Scene cleanup = SceneManager.CreateScene(
                    "Home Opening Test Cleanup");
                SceneManager.SetActiveScene(cleanup);
                AsyncOperation unload =
                    SceneManager.UnloadSceneAsync(active);
                if (unload != null)
                {
                    while (!unload.isDone)
                    {
                        yield return null;
                    }
                }
            }

            if (keyboard != null && keyboard.added)
            {
                InputSystem.RemoveDevice(keyboard);
            }

            inputFixture?.TearDown();
            inputFixture = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            MainMenu_WakeStartsAlarmThenRestoresGameplay()
        {
            GameSessionState.SetCitySeed(-91);
            GameSessionState.TryAddRouteStop("stale-route");
            GameSessionState.UpdateDrinkingProgress(
                63,
                DrinkId.RedWine,
                3);

            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.MainMenu,
                LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
            {
                yield return null;
            }

            MainMenuRoot menu =
                Object.FindAnyObjectByType<MainMenuRoot>();
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.LaunchCamera, Is.Not.Null);
            Assert.That(
                menu.LaunchCamera.clearFlags,
                Is.EqualTo(CameraClearFlags.SolidColor));
            Assert.That(menu.LaunchCamera.backgroundColor, Is.EqualTo(Color.black));
            Assert.That(menu.LaunchCamera.cullingMask, Is.Zero);

            HomeInteriorRoot home = null;
            yield return WaitUntil(
                () =>
                {
                    home = Object.FindAnyObjectByType<
                        HomeInteriorRoot>();
                    return SceneManager.GetActiveScene().name ==
                           SceneIds.HomeInterior &&
                           home != null &&
                           home.IsInitialized &&
                           home.Opening != null;
                },
                "The startup scene did not initialize the sleeping Home.");

            Assert.That(
                home.Opening.Phase,
                Is.EqualTo(HomeOpeningPhase.ClockHold));
            Assert.That(home.Opening.Timeline.MenuVisible, Is.False);
            Assert.That(
                home.AlarmClock.DisplayedTime,
                Is.EqualTo("05:59"));
            Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
            Assert.That(
                GameSessionState.GameTimeOfDayMinutes,
                Is.EqualTo(359d));
            Assert.That(
                home.AlarmClock.IsFollowingSessionTime,
                Is.False);
            GameSessionState.AdvanceGameTime(30f);
            yield return null;
            Assert.That(
                home.AlarmClock.DisplayedTime,
                Is.EqualTo("05:59"),
                "The opening clock must stay frozen until Wake Up is accepted.");
            Assert.That(
                GameSessionState.GameTimeOfDayMinutes,
                Is.EqualTo(359d));
            Assert.That(
                home.AlarmClock.ConfiguredDisplaySegmentCount,
                Is.EqualTo(HomeAlarmClock.DisplaySegmentCount));
            Assert.That(
                home.AlarmClock.ConfiguredDisplayPunctuationCount,
                Is.EqualTo(
                    HomeAlarmClock.DisplayPunctuationCount));
            Assert.That(
                home.AlarmClock.LitDisplaySegmentCount,
                Is.EqualTo(22));
            Assert.That(home.AlarmClock.DisplayVisible, Is.True);
            Renderer colonRenderer =
                home.AlarmClock.transform
                    .Find("Alarm Clock Colon Upper")
                    .GetComponent<Renderer>();
            Renderer digitRenderer =
                home.AlarmClock.transform
                    .Find("Alarm Clock Digit 0 Segment 0")
                    .GetComponent<Renderer>();
            Material displayMaterial =
                digitRenderer.sharedMaterial;
            Mesh displayMesh =
                digitRenderer
                    .GetComponent<MeshFilter>()
                    .sharedMesh;
            GameObject displayObject =
                digitRenderer.gameObject;
            Assert.That(colonRenderer.enabled, Is.True);
            Assert.That(digitRenderer.enabled, Is.True);
            Assert.That(
                home.AlarmClock.transform
                    .Find("Alarm Clock Digit 3 Segment 4")
                    .gameObject.activeSelf,
                Is.False,
                "05:59 must leave the final nine's lower-left segment dark.");
            Assert.That(
                home.AlarmClock.transform
                    .Find("Alarm Clock Digit 3 Segment 6")
                    .gameObject.activeSelf,
                Is.True,
                "05:59 must light the final nine's middle segment.");
            AssertDisplayRenderersEnabled(
                home.AlarmClock,
                true);
            Assert.That(home.AlarmClock.IsRinging, Is.False);
            Assert.That(
                home.CameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(42f).Within(0.001f));
            Vector3 clockCameraPosition =
                home.CameraFollow.FixedBasePosition;
            Quaternion clockCameraRotation =
                home.CameraFollow.FixedBaseRotation;
            Assert.That(home.CameraFollow.OrbitInputEnabled, Is.False);
            Assert.That(home.CameraFollow.CinematicMotionEnabled, Is.False);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.True);
            Assert.That(home.Opening.TryWake(), Is.False);

            yield return PressAndRelease(keyboard.eKey);
            Assert.That(
                home.Opening.Phase,
                Is.EqualTo(HomeOpeningPhase.ClockHold),
                "Wake input must stay locked during the first five seconds.");
            Assert.That(
                home.AlarmClock.DisplayedTime,
                Is.EqualTo("05:59"));
            Assert.That(home.AlarmClock.IsRinging, Is.False);

            AdvanceToDisplayBlink(home.Opening.Timeline);
            yield return null;
            Assert.That(home.AlarmClock.DisplayVisible, Is.False);
            Assert.That(colonRenderer.enabled, Is.False);
            Assert.That(digitRenderer.enabled, Is.False);
            AssertDisplayRenderersEnabled(
                home.AlarmClock,
                false);
            Assert.That(
                home.AlarmClock.VisibleLitDisplaySegmentCount,
                Is.Zero);
            Assert.That(
                home.AlarmClock.LitDisplaySegmentCount,
                Is.EqualTo(22),
                "Blinking must not rebuild or mutate the 05:59 pattern.");

            home.Opening.Timeline.Advance(
                HomeOpeningTimeline.DisplayBlinkOffSeconds);
            yield return null;
            Assert.That(home.AlarmClock.DisplayVisible, Is.True);
            Assert.That(colonRenderer.enabled, Is.True);
            Assert.That(digitRenderer.enabled, Is.True);
            AssertDisplayRenderersEnabled(
                home.AlarmClock,
                true);
            Assert.That(
                home.AlarmClock.VisibleLitDisplaySegmentCount,
                Is.EqualTo(22));

            float lockedClockRemaining =
                HomeOpeningTimeline.LockedClockSeconds -
                home.Opening.Timeline.PhaseElapsedSeconds;
            if (lockedClockRemaining > 0f)
            {
                home.Opening.Timeline.Advance(
                    lockedClockRemaining);
            }

            yield return null;

            Assert.That(
                home.Opening.Phase,
                Is.EqualTo(HomeOpeningPhase.AwaitingWake));
            Assert.That(home.Opening.Timeline.MenuVisible, Is.True);
            Assert.That(
                home.AlarmClock.DisplayedTime,
                Is.EqualTo("05:59"));
            Assert.That(
                home.AlarmClock.LitDisplaySegmentCount,
                Is.EqualTo(22));
            Assert.That(home.AlarmClock.DisplayVisible, Is.True);
            Assert.That(
                digitRenderer.gameObject,
                Is.SameAs(displayObject));
            Assert.That(
                digitRenderer.sharedMaterial,
                Is.SameAs(displayMaterial));
            Assert.That(
                digitRenderer
                    .GetComponent<MeshFilter>()
                    .sharedMesh,
                Is.SameAs(displayMesh));

            Assert.That(
                home.Arrival,
                Is.EqualTo(HomeArrivalKind.OpeningSleep));
            Assert.That(
                GameSessionState.HomeArrival,
                Is.EqualTo(HomeArrivalKind.Normal));
            Assert.That(
                GameSessionState.CitySeed,
                Is.EqualTo(GameSessionState.DefaultCitySeed));
            Assert.That(GameSessionState.PlannedBarRoute, Is.Empty);
            Assert.That(GameSessionState.IntoxicationLevel, Is.Zero);
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Looping));
            Assert.That(
                home.AnimatedInteraction.FrameIndex,
                Is.InRange(
                    home.Bed.Definition.LoopStartFrame,
                    home.Bed.Definition.ExitStartFrame - 1));
            Assert.That(home.Player.Motor.InputEnabled, Is.False);
            Assert.That(
                home.Player.Interactor.InputEnabled,
                Is.False);
            Assert.That(home.IntoxicationHud.Visible, Is.False);
            Assert.That(home.InteractionPrompt.PromptKey, Is.Empty);
            AssertWorldPresentationEnabled(home.Player);
            Assert.That(home.AlarmClock.IsRinging, Is.False);
            Assert.That(
                home.CameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(42f).Within(0.001f));
            Assert.That(
                home.CameraFollow.FixedBasePosition,
                Is.EqualTo(clockCameraPosition));
            Assert.That(
                Quaternion.Angle(
                    home.CameraFollow.FixedBaseRotation,
                    clockCameraRotation),
                Is.LessThan(0.001f));
            Assert.That(
                home.AlarmClock.Source.spatialBlend,
                Is.EqualTo(1f));
            AudioMixerGroup worldGroup =
                GameAudioMixer.SfxWorldGroup;
            Assert.That(
                home.AlarmClock.Source.outputAudioMixerGroup,
                Is.SameAs(worldGroup));
            Assert.That(
                home.GetComponentsInChildren<AudioSource>(true),
                Has.Length.EqualTo(
                    3 +
                    HomeSoundscape.OwnedSourceCount +
                    HomeAlarmClock.OwnedSourceCount));
            Assert.That(home.CameraFollow.FixedPoseActive, Is.True);
            Assert.That(
                Object.FindObjectsByType<Camera>(
                    FindObjectsInactive.Exclude),
                Has.Length.EqualTo(1));
            Assert.That(
                Object.FindObjectsByType<AudioListener>(
                    FindObjectsInactive.Exclude),
                Has.Length.EqualTo(1));

            AdvanceToDisplayBlink(home.Opening.Timeline);
            yield return null;
            Assert.That(
                home.Opening.Phase,
                Is.EqualTo(HomeOpeningPhase.AwaitingWake));
            Assert.That(home.Opening.Timeline.MenuVisible, Is.True);
            Assert.That(
                home.AlarmClock.DisplayedTime,
                Is.EqualTo("05:59"));
            Assert.That(home.AlarmClock.DisplayVisible, Is.False);
            Assert.That(home.AlarmClock.IsRinging, Is.False);
            AssertDisplayRenderersEnabled(
                home.AlarmClock,
                false);
            home.Opening.Timeline.Advance(
                HomeOpeningTimeline.DisplayBlinkOffSeconds);
            yield return null;
            Assert.That(home.AlarmClock.DisplayVisible, Is.True);

            keyboard.MakeCurrent();
            inputFixture.Press(
                keyboard.eKey,
                queueEventOnly: true);
            yield return null;
            Assert.That(
                home.Opening.Phase,
                Is.EqualTo(HomeOpeningPhase.AlarmRinging),
                "The real E-key path must activate ПРОСНУТЬСЯ.");
            Assert.That(home.Opening.TryWake(), Is.False);
            Assert.That(home.Opening.Timeline.MenuVisible, Is.False);
            Assert.That(home.AlarmClock.IsRinging, Is.True);
            Assert.That(
                home.AlarmClock.DisplayedTime,
                Is.EqualTo("06:00"));
            Assert.That(
                home.AlarmClock.LitDisplaySegmentCount,
                Is.EqualTo(24));
            Assert.That(home.AlarmClock.DisplayVisible, Is.True);
            Assert.That(GameSessionState.IsGameTimeRunning, Is.True);
            Assert.That(
                home.AlarmClock.IsFollowingSessionTime,
                Is.True);
            Assert.That(
                digitRenderer.gameObject,
                Is.SameAs(displayObject));
            Assert.That(
                digitRenderer.sharedMaterial,
                Is.SameAs(displayMaterial));
            Assert.That(
                digitRenderer
                    .GetComponent<MeshFilter>()
                    .sharedMesh,
                Is.SameAs(displayMesh));
            inputFixture.Release(
                keyboard.eKey,
                queueEventOnly: true);
            yield return null;
            Assert.That(
                home.Opening.Phase,
                Is.EqualTo(HomeOpeningPhase.AlarmRinging));
            Assert.That(home.AlarmClock.IsRinging, Is.True);
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Looping));
            Assert.That(
                home.CameraFollow.FixedBasePosition,
                Is.EqualTo(clockCameraPosition));
            Assert.That(
                Quaternion.Angle(
                    home.CameraFollow.FixedBaseRotation,
                    clockCameraRotation),
                Is.LessThan(0.001f));
            Assert.That(home.Opening.IsCameraTransitioning, Is.False);

            GameSessionState.AdvanceGameTime(1f);
            yield return null;
            Assert.That(
                home.Opening.Phase,
                Is.EqualTo(HomeOpeningPhase.AlarmRinging));
            Assert.That(home.AlarmClock.IsRinging, Is.True);
            Assert.That(
                home.AlarmClock.DisplayedTime,
                Is.EqualTo("06:01"),
                "Session time must already advance during the alarm hold.");
            Assert.That(
                home.AlarmClock.DisplayedHour,
                Is.EqualTo(GameSessionState.GameHour));
            Assert.That(
                home.AlarmClock.DisplayedMinute,
                Is.EqualTo(GameSessionState.GameMinute));

            Time.timeScale = 0f;
            const float alarmBoundaryOffset = 0.001f;
            float alarmHoldRemaining =
                HomeOpeningTimeline.WakeAlarmSeconds -
                alarmBoundaryOffset -
                home.Opening.Timeline.PhaseElapsedSeconds;
            if (alarmHoldRemaining > 0f)
            {
                home.Opening.Timeline.Advance(
                    alarmHoldRemaining);
            }

            Assert.That(
                home.Opening.Phase,
                Is.EqualTo(HomeOpeningPhase.AlarmRinging));
            Assert.That(
                home.Opening.Timeline.WakeAnimationReady,
                Is.False);
            Assert.That(home.AlarmClock.IsRinging, Is.True);
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Looping));
            Assert.That(
                home.CameraFollow.FixedBasePosition,
                Is.EqualTo(clockCameraPosition));
            Assert.That(home.Opening.IsCameraTransitioning, Is.False);

            home.Opening.Timeline.Advance(
                HomeOpeningTimeline.WakeAlarmSeconds -
                home.Opening.Timeline.PhaseElapsedSeconds +
                alarmBoundaryOffset);
            Assert.That(
                home.Opening.Timeline.WakeAnimationReady,
                Is.True);
            yield return null;

            Assert.That(
                home.Opening.Phase,
                Is.EqualTo(HomeOpeningPhase.Waking));
            Assert.That(home.AlarmClock.IsRinging, Is.False);
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Exiting));
            Assert.That(
                home.AnimatedInteraction.ExitDurationMultiplier,
                Is.EqualTo(
                    HomeOpeningController
                        .OpeningWakeDurationMultiplier));
            Assert.That(
                home.AnimatedInteraction.ExitDurationSeconds,
                Is.EqualTo(
                    home.Bed.Definition.ExitFrameCount /
                    (double)home.Bed.Definition.ExitFramesPerSecond *
                    HomeOpeningController
                        .OpeningWakeDurationMultiplier)
                    .Within(0.0001d));
            Assert.That(
                home.CameraFollow.FixedBaseFieldOfView,
                Is.LessThan(57f - 0.01f),
                "The post-alarm wake must move instead of snapping.");
            Assert.That(home.Opening.IsCameraTransitioning, Is.True);

            float cameraMidpointRemaining =
                HomeOpeningTimeline
                    .WakeCameraTransitionSeconds *
                0.5f -
                home.Opening.Timeline.PhaseElapsedSeconds;
            if (cameraMidpointRemaining > 0f)
            {
                home.Opening.Timeline.Advance(
                    cameraMidpointRemaining);
            }

            yield return null;
            Assert.That(
                home.Opening.Timeline
                    .WakeCameraTransitionProgress,
                Is.EqualTo(0.5f).Within(0.0001f));
            Vector3 sleeperCameraPosition =
                home.Opening.WakeCameraTargetPosition;
            Vector3 cameraPathControl =
                Vector3.Lerp(
                    clockCameraPosition,
                    sleeperCameraPosition,
                    0.5f) +
                Vector3.up *
                HomeOpeningController.WakeCameraPathHeight;
            Vector3 expectedCameraMidpoint =
                0.25f * clockCameraPosition +
                0.5f * cameraPathControl +
                0.25f * sleeperCameraPosition;
            Assert.That(
                Vector3.Distance(
                    home.CameraFollow.FixedBasePosition,
                    expectedCameraMidpoint),
                Is.LessThan(0.001f));
            Assert.That(
                Vector3.Distance(
                    expectedCameraMidpoint,
                    Vector3.Lerp(
                        clockCameraPosition,
                        sleeperCameraPosition,
                        0.5f)),
                Is.GreaterThan(0.15f),
                "The transition must retain its lifted quadratic path.");
            Assert.That(
                home.CameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(49.5f).Within(0.001f));

            float focusRemaining =
                HomeOpeningTimeline
                    .WakeCameraTransitionSeconds -
                home.Opening.Timeline.PhaseElapsedSeconds;
            if (focusRemaining > 0f)
            {
                home.Opening.Timeline.Advance(focusRemaining);
            }

            yield return null;
            Assert.That(
                home.Opening.Phase,
                Is.EqualTo(HomeOpeningPhase.Waking));
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Exiting));
            Assert.That(
                home.CameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(57f).Within(0.001f));
            Assert.That(
                Vector3.Distance(
                    home.CameraFollow.FixedBasePosition,
                    clockCameraPosition),
                Is.GreaterThan(0.5f));
            Assert.That(
                home.Opening.IsCameraTransitioning,
                Is.True,
                "The camera must keep settling toward gameplay without a cut.");

            float wakeDuration =
                (float)home.AnimatedInteraction
                    .ExitDurationSeconds;
            float settleMidpoint =
                Mathf.Lerp(
                    HomeOpeningTimeline
                        .WakeCameraTransitionSeconds,
                    wakeDuration,
                    0.5f);
            home.Opening.Timeline.Advance(
                settleMidpoint -
                home.Opening.Timeline.PhaseElapsedSeconds);
            yield return null;
            HomeCameraShot gameplayShot =
                home.FixedCamera.ActiveShot;
            Assert.That(
                Vector3.Distance(
                    home.CameraFollow.FixedBasePosition,
                    Vector3.Lerp(
                        sleeperCameraPosition,
                        gameplayShot.Position,
                        0.5f)),
                Is.LessThan(0.001f));
            Assert.That(
                home.CameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(
                        Mathf.Lerp(
                            57f,
                            gameplayShot.FieldOfView,
                            0.5f))
                    .Within(0.001f));
            Assert.That(
                home.Opening.IsCameraTransitioning,
                Is.True);

            home.Opening.Timeline.Advance(
                wakeDuration -
                home.Opening.Timeline.PhaseElapsedSeconds);
            yield return null;
            Assert.That(
                home.CameraFollow.FixedBasePosition,
                Is.EqualTo(gameplayShot.Position));
            Assert.That(
                Quaternion.Angle(
                    home.CameraFollow.FixedBaseRotation,
                    gameplayShot.Rotation),
                Is.LessThan(0.01f));
            Assert.That(
                home.CameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(gameplayShot.FieldOfView)
                    .Within(0.001f));
            Assert.That(
                home.Opening.IsCameraTransitioning,
                Is.False);

            Time.timeScale = 20f;
            yield return WaitUntil(
                () =>
                    home.Opening.Phase ==
                    HomeOpeningPhase.Complete &&
                    home.AnimatedInteraction.Phase ==
                    PlayerAnimatedInteractionPhase.Idle,
                "The wake animation did not restore normal gameplay.");
            Time.timeScale = 1f;
            yield return null;

            Assert.That(home.Opening.enabled, Is.False);
            Assert.That(home.Player.Motor.InputEnabled, Is.True);
            Assert.That(
                home.Player.Interactor.InputEnabled,
                Is.True);
            Assert.That(home.IntoxicationHud.Visible, Is.True);
            AssertWorldPresentationEnabled(home.Player);
            Assert.That(home.CameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(
                home.CameraFollow.CinematicMotionEnabled,
                Is.True);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
            Assert.That(home.AlarmClock.IsRinging, Is.False);
            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));
            Assert.That(home.CameraFollow.FixedPoseActive, Is.True);
            Assert.That(
                home.CameraFollow.FixedBasePosition,
                Is.EqualTo(home.FixedCamera.ActiveShot.Position));
            Assert.That(
                Quaternion.Angle(
                    home.CameraFollow.FixedBaseRotation,
                    home.FixedCamera.ActiveShot.Rotation),
                Is.LessThan(0.01f));

            home.Exit.Interact(home.Player.Interactor);
            Time.timeScale = 20f;
            StairwellInteriorRoot stairwell = null;
            yield return WaitUntil(
                () =>
                {
                    stairwell = Object.FindAnyObjectByType<
                        StairwellInteriorRoot>();
                    return SceneManager.GetActiveScene().name ==
                           SceneIds.StairwellInterior &&
                           stairwell != null &&
                           stairwell.IsInitialized &&
                           !SceneTransitionService.IsTransitioning;
                },
                "The restored player could not leave Home for the stairwell.");
            Time.timeScale = 1f;
            Assert.That(
                stairwell.Arrival,
                Is.EqualTo(StairwellArrivalKind.ApartmentDoor));
        }

        [UnityTest]
        public IEnumerator
            MainMenu_F9SkipsHomeAndOpensCityDebugTeleportMap()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.MainMenu,
                LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
            {
                yield return null;
            }

            HomeInteriorRoot home = null;
            yield return WaitUntil(
                () =>
                {
                    home = Object.FindAnyObjectByType<
                        HomeInteriorRoot>();
                    return home != null &&
                           home.IsInitialized &&
                           home.Opening != null &&
                           home.DebugCityMapShortcut != null;
                },
                "The startup Home did not install its debug City shortcut.");

            yield return WaitUntil(
                () => home.Opening.Phase ==
                      HomeOpeningPhase.AwaitingWake,
                "The startup Home did not reach the Wake Up menu.");

            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.True);
            Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
            Assert.That(
                GameSessionState.DebugCityMapOnArrivalRequested,
                Is.False);

            yield return PressAndRelease(keyboard.f9Key);

            CityGameRoot city = null;
            yield return WaitUntil(
                () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return SceneManager.GetActiveScene().name ==
                           SceneIds.City &&
                           city != null &&
                           city.IsInitialized &&
                           !SceneTransitionService.IsTransitioning &&
                           city.Map != null &&
                           city.Map.IsOpen;
                },
                "F9 did not skip Home and open the City debug map.");

            Assert.That(city.Map.DebugTeleportEnabled, Is.True);
            Assert.That(
                GameSessionState.DebugCityMapOnArrivalRequested,
                Is.False,
                "The map-open request must be consumed exactly once.");
            Assert.That(GameSessionState.IsGameTimeRunning, Is.True);
            Assert.That(GameSessionState.GameHour, Is.EqualTo(6));
            Assert.That(
                GameSessionState.ReturnKind,
                Is.EqualTo(CityReturnKind.None));
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(GameSessionState.DefaultCash));
            Assert.That(
                GameSessionState.HasInventoryItem(
                    InventoryItemId.ApartmentKeys),
                Is.True);
            Assert.That(
                GameSessionState.HasInventoryItem(
                    InventoryItemId.Lighter),
                Is.True);
            Assert.That(
                Object.FindAnyObjectByType<HomeInteriorRoot>(),
                Is.Null);

            Vector3 actual =
                city.Player.GameObject.transform.position;
            Vector3 expected =
                city.World.PlayerHome.ReturnPosition;
            Assert.That(
                Vector2.Distance(
                    new Vector2(actual.x, actual.z),
                    new Vector2(expected.x, expected.z)),
                Is.LessThan(0.05f));
        }

        [UnityTest]
        public IEnumerator
            DirectHomeLoad_RemainsNormalAndKeepsClockAsRoomDressing()
        {
            Assert.That(
                GameSessionState.TryStartGameTimeFromWake(),
                Is.True);
            GameSessionState.AdvanceGameTime(360f);
            Assert.That(GameSessionState.GameHour, Is.EqualTo(12));
            Assert.That(GameSessionState.GameMinute, Is.Zero);

            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.HomeInterior,
                LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
            {
                yield return null;
            }

            HomeInteriorRoot home = null;
            yield return WaitUntil(
                () =>
                {
                    home = Object.FindAnyObjectByType<
                        HomeInteriorRoot>();
                    return home != null && home.IsInitialized;
                },
                "The normal Home interior did not initialize.");

            Assert.That(
                home.Arrival,
                Is.EqualTo(HomeArrivalKind.Normal));
            Assert.That(home.Opening, Is.Null);
            Assert.That(home.AlarmClock, Is.Not.Null);
            Assert.That(home.AlarmClock.IsInitialized, Is.True);
            Assert.That(home.AlarmClock.IsRinging, Is.False);
            Assert.That(
                home.AlarmClock.DisplayedTime,
                Is.EqualTo(
                    $"{GameSessionState.GameHour:00}:" +
                    $"{GameSessionState.GameMinute:00}"));
            Assert.That(
                home.AlarmClock.IsFollowingSessionTime,
                Is.True);
            Assert.That(
                home.AlarmClock.DisplayedHour,
                Is.EqualTo(GameSessionState.GameHour));
            Assert.That(
                home.AlarmClock.DisplayedMinute,
                Is.EqualTo(GameSessionState.GameMinute));
            Assert.That(home.AlarmClock.DisplayVisible, Is.True);
            Assert.That(
                Vector3.Distance(
                    home.AlarmClock.transform.position,
                    home.Room.TransformPoint(
                        home.AlarmClockPlan.ClockPosition)),
                Is.LessThan(0.0001f));
            Assert.That(
                Vector3.Distance(
                    home.Player.GameObject.transform.position,
                    home.Layout.PlayerSpawn),
                Is.LessThan(0.0001f));
            Assert.That(home.Player.Motor.InputEnabled, Is.True);
            Assert.That(
                home.Player.Interactor.InputEnabled,
                Is.True);
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Idle));
        }

        [UnityTest]
        public IEnumerator
            ExternalSleepCancellation_FromMenuRestoresInputAndClockDressing()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.MainMenu,
                LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
            {
                yield return null;
            }

            HomeInteriorRoot home = null;
            yield return WaitUntil(
                () =>
                {
                    home = Object.FindAnyObjectByType<
                        HomeInteriorRoot>();
                    return home != null &&
                           home.IsInitialized &&
                           home.Opening != null;
                },
                "The opening did not initialize its clock hold.");

            Assert.That(
                home.Opening.Phase,
                Is.EqualTo(HomeOpeningPhase.ClockHold));
            Assert.That(
                home.AlarmClock.DisplayedTime,
                Is.EqualTo("05:59"));
            home.Opening.Timeline.Advance(
                HomeOpeningTimeline.LockedClockSeconds);
            yield return null;
            Assert.That(
                home.Opening.Phase,
                Is.EqualTo(HomeOpeningPhase.AwaitingWake));
            Assert.That(home.Opening.Timeline.MenuVisible, Is.True);
            Assert.That(
                home.AlarmClock.DisplayedTime,
                Is.EqualTo("05:59"));
            Assert.That(home.AlarmClock.IsRinging, Is.False);
            home.AlarmClock.SetDisplayVisible(false);
            Assert.That(home.AlarmClock.DisplayVisible, Is.False);

            Assert.That(
                home.AnimatedInteraction.CancelActiveInteraction(),
                Is.True);
            yield return null;

            Assert.That(
                home.Opening.Phase,
                Is.EqualTo(HomeOpeningPhase.Complete));
            Assert.That(home.Opening.enabled, Is.False);
            Assert.That(home.AlarmClock.IsRinging, Is.False);
            Assert.That(
                home.AlarmClock.DisplayedTime,
                Is.EqualTo("05:59"));
            Assert.That(home.AlarmClock.DisplayVisible, Is.True);
            Assert.That(home.Player.Motor.InputEnabled, Is.True);
            Assert.That(
                home.Player.Interactor.InputEnabled,
                Is.True);
            Assert.That(home.IntoxicationHud.Visible, Is.True);
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Idle));
            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));

            home.Opening.enabled = true;
            yield return null;
            Assert.That(
                home.Opening.enabled,
                Is.False,
                "A completed opening must remain inert when re-enabled.");
            Assert.That(home.AlarmClock.IsRinging, Is.False);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
        }

        [UnityTest]
        public IEnumerator
            ExternalSleepCancellation_DuringWakeAlarmStopsRing()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.MainMenu,
                LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
            {
                yield return null;
            }

            HomeInteriorRoot home = null;
            yield return WaitUntil(
                () =>
                {
                    home = Object.FindAnyObjectByType<
                        HomeInteriorRoot>();
                    return home != null &&
                           home.IsInitialized &&
                           home.Opening != null;
                },
                "The opening did not initialize its alarm hold.");

            home.Opening.Timeline.Advance(
                HomeOpeningTimeline.LockedClockSeconds);
            yield return null;
            Assert.That(
                home.Opening.Phase,
                Is.EqualTo(HomeOpeningPhase.AwaitingWake));
            Assert.That(home.AlarmClock.IsRinging, Is.False);

            Vector3 clockCameraPosition =
                home.CameraFollow.FixedBasePosition;
            Assert.That(home.Opening.TryWake(), Is.True);
            Assert.That(
                home.Opening.Phase,
                Is.EqualTo(HomeOpeningPhase.AlarmRinging));
            Assert.That(home.AlarmClock.IsRinging, Is.True);
            Assert.That(
                home.AlarmClock.DisplayedTime,
                Is.EqualTo("06:00"));
            Assert.That(
                home.AlarmClock.IsFollowingSessionTime,
                Is.True);
            GameSessionState.AdvanceGameTime(1f);
            yield return null;
            Assert.That(
                home.AlarmClock.DisplayedTime,
                Is.EqualTo("06:01"));
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Looping));
            Assert.That(
                home.CameraFollow.FixedBasePosition,
                Is.EqualTo(clockCameraPosition));
            Assert.That(home.Opening.IsCameraTransitioning, Is.False);

            home.AlarmClock.SetDisplayVisible(false);
            Assert.That(
                home.AnimatedInteraction.CancelActiveInteraction(),
                Is.True);
            yield return null;

            Assert.That(
                home.Opening.Phase,
                Is.EqualTo(HomeOpeningPhase.Complete));
            Assert.That(home.Opening.enabled, Is.False);
            Assert.That(home.AlarmClock.IsRinging, Is.False);
            Assert.That(
                home.AlarmClock.DisplayedTime,
                Is.EqualTo("06:01"));
            Assert.That(GameSessionState.IsGameTimeRunning, Is.True);
            Assert.That(
                home.AlarmClock.IsFollowingSessionTime,
                Is.True);
            Assert.That(home.AlarmClock.DisplayVisible, Is.True);
            Assert.That(home.Player.Motor.InputEnabled, Is.True);
            Assert.That(
                home.Player.Interactor.InputEnabled,
                Is.True);
            Assert.That(home.IntoxicationHud.Visible, Is.True);
            Assert.That(
                home.AnimatedInteraction.Phase,
                Is.EqualTo(
                    PlayerAnimatedInteractionPhase.Idle));
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
        }

        private IEnumerator PressAndRelease(ButtonControl button)
        {
            inputFixture.Press(button, queueEventOnly: true);
            yield return null;
            inputFixture.Release(button, queueEventOnly: true);
            yield return null;
        }

        private static void AdvanceToDisplayBlink(
            HomeOpeningTimeline timeline)
        {
            float elapsed = timeline.DisplayElapsedSeconds;
            float firstBlink =
                HomeOpeningTimeline
                    .DisplayBlinkInitialDelaySeconds;
            const float testOffsetSeconds = 0.001f;
            if (elapsed < firstBlink)
            {
                timeline.Advance(
                    firstBlink -
                    elapsed +
                    testOffsetSeconds);
                return;
            }

            float period =
                HomeOpeningTimeline.DisplayBlinkPeriodSeconds;
            float periodElapsed =
                (elapsed - firstBlink) % period;
            if (periodElapsed < testOffsetSeconds)
            {
                timeline.Advance(
                    testOffsetSeconds -
                    periodElapsed);
                return;
            }

            timeline.Advance(
                period -
                periodElapsed +
                testOffsetSeconds);
        }

        private static void AssertDisplayRenderersEnabled(
            HomeAlarmClock alarm,
            bool expected)
        {
            Renderer[] renderers =
                alarm.GetComponentsInChildren<Renderer>(true);
            int displayRendererCount = 0;
            for (int index = 0;
                 index < renderers.Length;
                 index++)
            {
                string objectName =
                    renderers[index].gameObject.name;
                if (!objectName.StartsWith(
                        "Alarm Clock Digit ") &&
                    !objectName.StartsWith(
                        "Alarm Clock Colon "))
                {
                    continue;
                }

                displayRendererCount++;
                Assert.That(
                    renderers[index].enabled,
                    Is.EqualTo(expected),
                    $"Display renderer '{objectName}' visibility mismatch.");
            }

            Assert.That(
                displayRendererCount,
                Is.EqualTo(
                    HomeAlarmClock.DisplaySegmentCount +
                    HomeAlarmClock.DisplayPunctuationCount));
        }

        private static void AssertWorldPresentationEnabled(
            PlayerRuntime player)
        {
            Assert.That(player.Visual.Renderers, Is.Not.Empty);
            for (int index = 0;
                 index < player.Visual.Renderers.Count;
                 index++)
            {
                Assert.That(
                    player.Visual.Renderers[index].enabled,
                    Is.True,
                    $"World player renderer {index} was hidden.");
            }

            Assert.That(player.ContactShadow.enabled, Is.True);
            Assert.That(
                player.PresentationVisibility.IsHidden,
                Is.False);
        }

        private static IEnumerator WaitUntil(
            System.Func<bool> predicate,
            string failureMessage)
        {
            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (!predicate() &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(predicate(), Is.True, failureMessage);
        }
    }
}
