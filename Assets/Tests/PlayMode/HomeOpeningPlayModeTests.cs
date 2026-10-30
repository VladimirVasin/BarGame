using System.Collections;
using System.Collections.Generic;
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
            MainMenuRoot.ClearLegacyOpeningRequest();
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
        public IEnumerator NewGame_MenuToNoonIntroRequiresFreshWakeAndStand()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            StartMenuRoot menu = Object.FindAnyObjectByType<StartMenuRoot>();
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.SelectedOption, Is.EqualTo(StartMenuOption.NewGame));
            Assert.That(menu.IsDebugMenu, Is.False);
            Assert.That(Object.FindAnyObjectByType<MainMenuRoot>(), Is.Null);
            GameSessionState.SetCitySeed(-91);
            GameSessionState.UpdateDrinkingProgress(63, DrinkId.RedWine, 3);
            GameSessionState.TrySetDebugHeroCorruptionLevel(7);
            GameSessionState.TrySetDebugGameDay(7);
            keyboard.MakeCurrent();
            inputFixture.Press(keyboard.eKey, queueEventOnly: true);
            Assert.That(menu.ConfirmSelection(), Is.True);
            Assert.That(menu.ConfirmSelection(), Is.False, "The story start is a single menu transaction.");
            HomeInteriorRoot home = null;
            yield return WaitForNewGameHome(value => home = value);
            Assert.That(home.Arrival, Is.EqualTo(HomeArrivalKind.NewGameSleep));
            Assert.That(GameSessionState.HomeArrival, Is.EqualTo(HomeArrivalKind.Normal));
            Assert.That(home.Opening.IsNewGameProfile, Is.True);
            Assert.That(home.Sleep.IsNormalOpening, Is.True);
            Assert.That(home.Opening.ShotCount, Is.EqualTo(5));
            Assert.That(home.Opening.Phase, Is.EqualTo(HomeOpeningPhase.AwaitingWake));
            Assert.That(home.AnimatedInteraction.Phase, Is.EqualTo(PlayerAnimatedInteractionPhase.Looping));
            Assert.That(home.Player.Motor.InputEnabled, Is.False);
            Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
            Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(720d));
            Assert.That(home.AlarmClock.DisplayedTime, Is.EqualTo("12:00"));
            Assert.That(home.AlarmClock.LitDisplaySegmentCount, Is.EqualTo(19));
            Assert.That(home.AlarmClock.IsRinging, Is.False);
            Assert.That(GameSessionState.CitySeed, Is.EqualTo(GameSessionState.DefaultCitySeed));
            Assert.That(GameSessionState.IntoxicationLevel, Is.Zero);
            var hero = home.Player.Visual as Player3DCharacterPresentation;
            Assert.That(hero, Is.Not.Null);
            AssertFreshStoryAppearance(home, hero);
            AssertWorldPresentationEnabled(home.Player);
            AssertNoTransformationCue(home);
            int hunger = GameSessionState.HungerLevel, fatigue = GameSessionState.FatigueLevel;
            GameSessionState.AdvanceGameTime(GameTimeState.RealSecondsPerGameDay);
            yield return null;
            yield return null;
            Assert.That(home.Opening.Phase, Is.EqualTo(HomeOpeningPhase.AwaitingWake),
                "E carried from before Home loaded must not wake the hero.");
            Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(720d));
            Assert.That(GameSessionState.HungerLevel, Is.EqualTo(hunger));
            Assert.That(GameSessionState.FatigueLevel, Is.EqualTo(fatigue));
            var poses = new List<Vector3>();
            bool clockFramed = false;
            using (GameTimeScaleRuntime.AcquirePause())
            {
                yield return null;
                for (int shot = 0; shot < home.Opening.ShotCount; shot++)
                {
                    Assert.That(home.Opening.ApplyNewGameShot(shot, .5f), Is.True,
                        "Every authored room shot must pass its real camera clearance.");
                    Assert.That(home.Opening.CurrentShotIndex, Is.EqualTo(shot));
                    hero.ReapplyLatePresentationPose();
                    Camera camera = Camera.main;
                    Assert.That(camera, Is.Not.Null);
                    Assert.That(camera.fieldOfView, Is.InRange(50f, 65f));
                    foreach (Vector3 other in poses)
                        Assert.That(Vector3.Distance(camera.transform.position, other), Is.GreaterThan(.3f));
                    poses.Add(camera.transform.position);
                    Player3DBoneAnchors anchors = hero.Registry.Anchors;
                    foreach (Transform anchor in new[] { anchors.Head, anchors.Chest, anchors.Pelvis,
                                 anchors.LeftFoot, anchors.RightFoot })
                        AssertInsideNewGameFrame(camera, anchor.position, .015f, anchor.name);
                    Vector3 clockView = camera.WorldToViewportPoint(home.AlarmClock.transform.position);
                    clockFramed |= clockView.z > camera.nearClipPlane && clockView.x > .02f &&
                        clockView.x < .98f && clockView.y > .02f && clockView.y < .98f;
                    Assert.That(hero.CurrentFacialExpression, Is.Not.EqualTo(PlayerFacialExpression.Horror));
                    Assert.That(home.Opening.Phase, Is.EqualTo(HomeOpeningPhase.AwaitingWake));
                    Assert.That(home.Sleep.IsScreenDark, Is.False);
                    CaptureNewGameFrame("shot-" + (shot + 1).ToString("00"));
                }
                Assert.That(clockFramed, Is.True, "At least one ordinary room view includes the real noon clock.");
                Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(720d));
            }
            inputFixture.Release(keyboard.eKey, queueEventOnly: true);
            yield return null;
            yield return null;
            Assert.That(home.Player.Interactor.ActiveInteractable, Is.SameAs(home.Bed));
            Assert.That(home.Player.Interactor.PromptView.PromptKey, Is.EqualTo(HomeBedInteraction.WakePromptKey));
            inputFixture.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            Assert.That(home.Opening.Phase, Is.EqualTo(HomeOpeningPhase.Waking));
            Assert.That(home.Opening.TryWake(), Is.False);
            Assert.That(GameSessionState.IsGameTimeRunning, Is.True);
            Assert.That(GameSessionState.GameHour, Is.EqualTo(12), "Wake starts the prepared clock without06:00 snapping.");
            Assert.That(home.AlarmClock.IsRinging, Is.False);
            Assert.That(home.AnimatedInteraction.ExitDurationSeconds,
                Is.EqualTo(10.62d).Within(.001d),
                "The neutral motion reaches .87 after 1.4+3.42s, holds 3s and reserves 2.8s to stand.");
            Time.timeScale = 3f;
            yield return WaitUntil(() => home.Bed.WakeRecoveryActive,
                "The neutral wake did not reach its supported seated recovery.");
            float recoveryStart = Time.time - home.Bed.WakeRecoveryProgress * HomeBedInteraction.WakeRecoveryHoldSeconds;
            bool capturedRecovery = false;
            float recoveryDeadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (home.Sleep.Phase != HomeSleepPhase.Returning && Time.realtimeSinceStartup < recoveryDeadline)
            {
                Assert.That(home.Sleep.IsActive, Is.True);
                Assert.That(home.Sleep.IsScreenDark, Is.False);
                Assert.That(home.AlarmClock.IsRinging, Is.False);
                Assert.That(home.AnimatedInteraction.ClipProgress,
                    Is.EqualTo(HomeBedInteraction.WakeRecoveryClipProgress).Within(.0001f));
                Assert.That(home.Player.Motor.InputEnabled, Is.False);
                hero.ReapplyLatePresentationPose();
                Assert.That(hero.CurrentFacialExpression, Is.Not.EqualTo(PlayerFacialExpression.Horror));
                if (!capturedRecovery && home.Bed.WakeRecoveryProgress >= .4f)
                {
                    capturedRecovery = true;
                    AssertNoTransformationCue(home);
                    CaptureNewGameFrame("wake-recovery");
                }
                AssertFreshStoryAppearance(home, hero);
                yield return null;
            }
            Assert.That(home.Sleep.Phase, Is.EqualTo(HomeSleepPhase.Returning));
            Assert.That(capturedRecovery, Is.True);
            Assert.That(Time.time - recoveryStart, Is.GreaterThanOrEqualTo(2.9f));
            float returnStart = Time.time - home.Sleep.PhaseElapsedSeconds;
            using (GameTimeScaleRuntime.AcquirePause())
            {
                yield return null;
                Vector3 heldCamera = home.CameraFollow.FixedBasePosition;
                float heldClip = home.AnimatedInteraction.ClipProgress;
                yield return new WaitForSecondsRealtime(.08f);
                Assert.That(home.CameraFollow.FixedBasePosition, Is.EqualTo(heldCamera));
                Assert.That(home.AnimatedInteraction.ClipProgress, Is.EqualTo(heldClip));
            }
            yield return WaitUntil(() => home.Opening.Phase == HomeOpeningPhase.AwaitingStand,
                "The ordinary opening did not return to gameplay framing and wait for a separate stand input.");
            Assert.That(Time.time - returnStart, Is.GreaterThanOrEqualTo(HomeSleepController.CameraReturnSeconds - .15f));
            Assert.That(home.Sleep.WaitingForStandUp, Is.True);
            Assert.That(home.Player.Interactor.ActiveInteractable, Is.SameAs(home.Bed));
            Assert.That(home.Player.Interactor.PromptView.PromptKey, Is.EqualTo(HomeBedInteraction.StandPromptKey));
            Assert.That(home.Player.Motor.InputEnabled, Is.False);
            Assert.That(Vector3.Distance(home.CameraFollow.FixedBasePosition, home.FixedCamera.ActiveShot.Position), Is.LessThan(.01f));
            hero.ReapplyLatePresentationPose();
            Player3DBoneAnchors idle = hero.Registry.Anchors;
            Vector3 leftFoot = idle.LeftFoot.position, rightFoot = idle.RightFoot.position;
            Vector3 leftHand = idle.LeftGrip.position, rightHand = idle.RightGrip.position;
            float idleUntil = Time.time + 30f;
            float idleDeadline = Time.realtimeSinceStartup + TimeoutSeconds;
            Time.timeScale = 10f;
            while (Time.time < idleUntil && Time.realtimeSinceStartup < idleDeadline)
            {
                hero.ReapplyLatePresentationPose();
                Assert.That(home.Opening.Phase, Is.EqualTo(HomeOpeningPhase.AwaitingStand),
                    "Elapsed time or E held from wake must never consume the separate stand action.");
                Assert.That(home.AnimatedInteraction.ClipProgress,
                    Is.EqualTo(HomeBedInteraction.WakeRecoveryClipProgress).Within(.0001f));
                Assert.That(Vector3.Distance(idle.LeftFoot.position, leftFoot), Is.LessThan(.005f));
                Assert.That(Vector3.Distance(idle.RightFoot.position, rightFoot), Is.LessThan(.005f));
                Assert.That(Vector3.Distance(idle.LeftGrip.position, leftHand), Is.LessThan(.02f));
                Assert.That(Vector3.Distance(idle.RightGrip.position, rightHand), Is.LessThan(.02f));
                AssertFreshStoryAppearance(home, hero);
                yield return null;
            }
            Assert.That(Time.time, Is.GreaterThanOrEqualTo(idleUntil));
            CaptureNewGameFrame("seated-await-E");
            inputFixture.Release(keyboard.eKey, queueEventOnly: true);
            Time.timeScale = 1f;
            yield return null;
            yield return null;
            float standStart = Time.time;
            inputFixture.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            Assert.That(home.Opening.Phase, Is.EqualTo(HomeOpeningPhase.Waking));
            Assert.That(home.Opening.TryStand(), Is.False, "One physical E edge owns a single stand action.");
            inputFixture.Release(keyboard.eKey, queueEventOnly: true);
            bool terminalPresented = false;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (home.Opening.Phase != HomeOpeningPhase.Complete && Time.realtimeSinceStartup < deadline)
            {
                if (home.AnimatedInteraction.ClipProgress >= 1f)
                {
                    terminalPresented = true;
                    CaptureNewGameFrame("standing-terminal");
                }
                AssertFreshStoryAppearance(home, hero);
                yield return null;
            }
            Assert.That(home.Opening.Phase, Is.EqualTo(HomeOpeningPhase.Complete));
            Assert.That(terminalPresented, Is.True);
            Assert.That(Time.time - standStart, Is.GreaterThanOrEqualTo(HomeBedInteraction.WakeStandSeconds - .1f));
            Assert.That(home.AnimatedInteraction.IsActive, Is.False);
            Assert.That(home.Bed.OwnsActiveInteraction, Is.False);
            Assert.That(home.Sleep.IsActive, Is.False);
            Assert.That(home.Sleep.IsNormalOpening, Is.False);
            Assert.That(home.Player.Motor.InputEnabled, Is.True);
            Assert.That(home.Player.Interactor.InputEnabled, Is.True);
            Assert.That(home.IntoxicationHud.Visible, Is.True);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
            Assert.That(hero.HasContextualFacialExpression, Is.False);
            AssertWorldPresentationEnabled(home.Player);
            AssertNoTransformationCue(home);
            CaptureNewGameFrame("standing-gameplay");
        }

        [UnityTest]
        public IEnumerator NewGame_CancelledIntroRestoresModalAndCameraWithoutCorruption()
        {
            Assert.That(GameSessionState.TryPrepareGameTimeAt(NewGameStartService.StoryStartMinuteOfDay), Is.True);
            GameSessionState.PrepareHomeArrival(HomeArrivalKind.NewGameSleep);
            yield return SceneManager.LoadSceneAsync(SceneIds.HomeInterior, LoadSceneMode.Single);
            HomeInteriorRoot home = null;
            yield return WaitForNewGameHome(value => home = value);
            Assert.That(home.Opening.IsNewGameProfile, Is.True);
            Assert.That(home.Opening.Phase, Is.EqualTo(HomeOpeningPhase.AwaitingWake));
            Assert.That(home.Opening.TryStand(), Is.False);
            var hero = home.Player.Visual as Player3DCharacterPresentation;
            Assert.That(hero, Is.Not.Null);
            using (GameTimeScaleRuntime.AcquirePause())
            {
                yield return null;
                home.Opening.Cancel();
                yield return null;
                Assert.That(home.Opening.Phase, Is.EqualTo(HomeOpeningPhase.Complete));
                Assert.That(home.Sleep.IsActive, Is.False);
                Assert.That(home.Sleep.IsNormalOpening, Is.False);
                Assert.That(home.AnimatedInteraction.IsActive, Is.False);
                Assert.That(home.Player.Motor.InputEnabled, Is.True);
                Assert.That(home.Player.Interactor.InputEnabled, Is.True);
                Assert.That(home.IntoxicationHud.Visible, Is.True);
                Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
                Assert.That(GameSessionState.IsGameTimeRunning, Is.True,
                    "Cancellation leaves ordinary Home gameplay with its prepared noon clock running.");
                Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(720d));
                Assert.That(home.Opening.TryWake(), Is.False);
                Assert.That(home.Opening.TryStand(), Is.False);
                Assert.That(Vector3.Distance(home.CameraFollow.FixedBasePosition,
                    home.FixedCamera.ActiveShot.Position), Is.LessThan(.01f));
                AssertFreshStoryAppearance(home, hero);
                AssertWorldPresentationEnabled(home.Player);
                AssertNoTransformationCue(home);
                CaptureNewGameFrame("cancel-restored");
            }
        }

        private static IEnumerator WaitForNewGameHome(System.Action<HomeInteriorRoot> result)
        {
            HomeInteriorRoot home = null;
            float deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline)
            {
                home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                if (home != null && home.IsInitialized && home.Opening != null && home.Sleep != null &&
                    home.Sleep.WaitingForWake) break;
                yield return null;
            }
            Assert.That(home, Is.Not.Null);
            Assert.That(home.IsInitialized, Is.True);
            Assert.That(home.Opening, Is.Not.Null);
            Assert.That(home.Sleep.WaitingForWake, Is.True, "The story did not initialize its ordinary sleeping intro.");
            result(home);
        }

        private static void AssertFreshStoryAppearance(HomeInteriorRoot home, Player3DCharacterPresentation hero)
        {
            Assert.That(GameSessionState.GameDayNumber, Is.EqualTo(1));
            Assert.That(GameSessionState.HomeAppearanceDayNumber, Is.EqualTo(1));
            Assert.That(GameSessionState.HeroCorruptionLevel, Is.EqualTo(1));
            Assert.That(hero.CorruptionAppearance.AppliedLevel, Is.EqualTo(1));
            Assert.That(home.ApartmentDays.AppliedDayNumber, Is.EqualTo(1));
            Assert.That(home.ApartmentDays.BalconyBarrier.IsSealed, Is.False);
            Assert.That(home.ApartmentDays.BalconyBarrier.DoorCollider.enabled, Is.False);
            Assert.That(Quaternion.Angle(home.ApartmentDays.BalconyBarrier.DoorPivot.localRotation,
                Quaternion.Euler(0, 76f, 0)), Is.LessThan(.001f));
        }

        private static void AssertInsideNewGameFrame(Camera camera, Vector3 point, float margin, string label)
        {
            Vector3 view = camera.WorldToViewportPoint(point);
            Assert.That(view.z, Is.GreaterThan(camera.nearClipPlane), label + " is behind the intro lens.");
            Assert.That(view.x, Is.InRange(margin, 1f - margin), label + " is horizontally cropped.");
            Assert.That(view.y, Is.InRange(margin, 1f - margin), label + " is vertically cropped.");
        }

        private static void CaptureNewGameFrame(string name)
        {
            if (System.Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_NEW_GAME") == "1")
                AreaCaptureFixture.CaptureCurrentCamera(Camera.main, "NewGame", name);
        }

        private static void AssertNoTransformationCue(HomeInteriorRoot home)
        {
            AudioClip cue = home.Audio.GetClip(RetroSfxId.HomeSleepStartle);
            if (cue == null) return;
            foreach (AudioSource voice in home.Audio.GetComponentsInChildren<AudioSource>(true))
                Assert.That(voice.clip == cue && voice.isPlaying, Is.False,
                    "An ordinary new-game wake must not play the corruption cue.");
        }

        [UnityTest]
        public IEnumerator
            MainMenu_WakeStartsAlarmThenRestoresGameplay()
        {
            GameSessionState.SetCitySeed(-91);
            GameSessionState.UpdateDrinkingProgress(
                63,
                DrinkId.RedWine,
                3);

            MainMenuRoot.RequestLegacyOpening();
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
            AudioMixerGroup gameplayGroup =
                GameAudioMixer.SfxGameplayGroup;
            Assert.That(
                home.AlarmClock.Source.outputAudioMixerGroup,
                Is.SameAs(gameplayGroup));
            HomeAudioSourceContract.AssertSourcesAreDeclaredOrOnDemand(
                home);
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

            // One game minute, however many real seconds the day makes it.
            GameSessionState.AdvanceGameTime(
                (float)(1f / GameTimeState.GameMinutesPerRealSecond));
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

            PlayerDoorActionTarget exitAction =
                home.Exit.GetComponent<PlayerDoorActionTarget>();
            Assert.That(exitAction, Is.Not.Null);
            Assert.That(
                exitAction.Plan.EntryRootPosition.y,
                Is.EqualTo(PlayerFactory.GroundedRootOffset)
                    .Within(0.0001f));
            Vector3 exitApproach =
                exitAction.Plan.EntryRootPosition +
                Vector3.forward * 0.45f;
            exitApproach.y = PlayerFactory.GroundedRootOffset;
            home.Player.Motor.Teleport(exitApproach);
            Physics.SyncTransforms();
            yield return WaitUntil(
                () => ReferenceEquals(
                    home.Player.Interactor.ActiveInteractable,
                    home.Exit),
                "The grounded Home exit did not become the active E target.");
            yield return PressAndRelease(keyboard.eKey);
            Assert.That(
                home.Player.GameObject.GetComponent<
                    PlayerDoorActionController>().IsPlaying,
                Is.True,
                "The real E-key path did not start the Home door action.");
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
            HomeDebug_AfterWakePreviewsEverySelectedDayAndOffersCitySkip()
        {
            MainMenuRoot.RequestLegacyOpening();
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

            MinigameDebugWindow debugWindow = home.GetComponentInChildren<
                MinigameDebugWindow>();
            HomeApartmentDayController dayController = home.GetComponentInChildren<
                HomeApartmentDayController>();
            Assert.That(debugWindow, Is.Not.Null);
            Assert.That(dayController, Is.Not.Null);
            Assert.That(debugWindow.IsOpen, Is.False,
                "F9 must not interrupt the opening or start its clock.");
            Assert.That(home.DebugCityMapShortcut.KeyboardShortcutEnabled, Is.False);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneIds.HomeInterior));
            Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
            Assert.That(debugWindow.TrySetGameDayNumber(7), Is.False);
            Assert.That(home.Opening.TryWake(), Is.True);
            yield return WaitUntil(
                () => home.Opening.Phase == HomeOpeningPhase.Complete &&
                      !HomeApartmentDayController.IsHomeBusy(home),
                "The ordinary wake did not restore Home debug access.");

            yield return PressAndRelease(keyboard.f9Key);
            Assert.That(debugWindow.IsOpen, Is.True);
            Assert.That(dayController.AppliedDayNumber, Is.EqualTo(1));
            double clockBeforePreview = GameSessionState.GameTimeOfDayMinutes;
            int hungerBeforePreview = GameSessionState.HungerLevel;
            int fatigueBeforePreview = GameSessionState.FatigueLevel;
            Vector3 positionBeforePreview = home.Player.GameObject.transform.position;
            foreach (int previewDay in new[] { 7, 3, 1 })
            {
                Assert.That(debugWindow.TrySetGameDayNumber(previewDay), Is.True);
                Assert.That(dayController.AppliedDayNumber, Is.EqualTo(previewDay));
                Assert.That(GameSessionState.GameTimeOfDayMinutes,
                    Is.EqualTo(clockBeforePreview));
                Assert.That(GameSessionState.HungerLevel, Is.EqualTo(hungerBeforePreview));
                Assert.That(GameSessionState.FatigueLevel, Is.EqualTo(fatigueBeforePreview));
                Assert.That(home.Player.GameObject.transform.position,
                    Is.EqualTo(positionBeforePreview));
                Assert.That(debugWindow.IsOpen, Is.True);
            }

            Assert.That(debugWindow.TrySkipHomeToCityMap(), Is.True);
            Assert.That(debugWindow.IsOpen, Is.False);

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
                "The Home debug button did not open the City debug map.");

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
            GameSessionState.AdvanceGameTime((float)(360f / GameTimeState.GameMinutesPerRealSecond));
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
            MainMenuRoot.RequestLegacyOpening();
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

            // Once the opening lets go, the clock FOLLOWS session time -
            // so pinning the literal "05:59" here was racing it, and lost
            // the moment the session ticked over to 06:00. What the
            // restored dressing actually promises is that the clock is
            // showing the session's own time, which is what this reads.
            // GameTimeRuntime runs at execution order -1000, so the minute
            // read here is exactly the one the clock synced to this frame.
            Assert.That(
                home.AlarmClock.IsFollowingSessionTime,
                Is.True,
                "A cancelled sleep must hand the clock back to the " +
                "session.");
            Assert.That(
                home.AlarmClock.DisplayedTime,
                Is.EqualTo(
                    $"{GameSessionState.GameHour:00}:" +
                    $"{GameSessionState.GameMinute:00}"));
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
            MainMenuRoot.RequestLegacyOpening();
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
            GameSessionState.AdvanceGameTime((float)(1f / GameTimeState.GameMinutesPerRealSecond));
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
            // Session equipment rides `Visual.Renderers` (the scarf, since
            // 4ff4f116) but draws only while it is worn: its visibility
            // answers to the inventory, not to the wake. The world
            // contract here is the hero's own body.
            PlayerScarfController scarf =
                player.GameObject.GetComponent<PlayerScarfController>();
            Player3DCharacterPresentation hero = player.Visual as Player3DCharacterPresentation;
            PlayerWardrobe wardrobe = hero != null && hero.Registry != null
                ? hero.Registry.GetComponent<PlayerWardrobe>() : null;
            IReadOnlyList<Renderer> equipment =
                scarf != null && scarf.Presentation != null
                    ? scarf.Presentation.Renderers
                    : System.Array.Empty<Renderer>();
            Assert.That(player.Visual.Renderers, Is.Not.Empty);
            int bodyRendererCount = 0;
            for (int index = 0;
                 index < player.Visual.Renderers.Count;
                 index++)
            {
                Renderer renderer = player.Visual.Renderers[index];
                if (IsEquipmentRenderer(equipment, renderer))
                {
                    Assert.That(
                        renderer.enabled || !scarf.Presentation.IsEquipped,
                        Is.True,
                        $"Worn equipment renderer {index} was hidden.");
                    continue;
                }

                // Covered anatomy is intentionally hidden by the worn outfit:
                // e.g. GEO_Foot.L/R belong under hero_boots. This is the same
                // logical contract ConfigureWorldRenderers applies, independent
                // of any temporary presentation visibility lease.
                bool expected = wardrobe == null || !wardrobe.IsConfigured || wardrobe.IsRendererWorn(renderer);
                if (expected) bodyRendererCount++;
                Assert.That(
                    renderer.enabled,
                    Is.EqualTo(expected),
                    $"World player renderer '{renderer.name}' disagrees with the worn outfit.");
            }

            Assert.That(
                bodyRendererCount,
                Is.GreaterThan(0),
                "The hero's body must have renderers of its own.");
            Assert.That(player.ContactShadow.enabled, Is.True);
            Assert.That(
                player.PresentationVisibility.IsHidden,
                Is.False);
        }

        private static bool IsEquipmentRenderer(
            IReadOnlyList<Renderer> equipment,
            Renderer renderer)
        {
            for (int index = 0; index < equipment.Count; index++)
            {
                if (ReferenceEquals(equipment[index], renderer))
                {
                    return true;
                }
            }

            return false;
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
