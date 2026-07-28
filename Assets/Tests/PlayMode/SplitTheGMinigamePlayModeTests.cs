using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class SplitTheGMinigamePlayModeTests
    {
        private const float PhaseCompletionPadding = 0.01f;

        private GameObject playerObject;
        private GameObject cameraObject;
        private GameObject uiObject;
        private PlayerMotor motor;
        private PlayerInteractor interactor;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView hud;
        private SplitTheGMinigameView view;
        private SplitTheGMinigameController controller;
        private PlayerRuntime playerRuntime;
        private InputTestFixture inputFixture;
        private Keyboard keyboard;
        private Mouse mouse;
        private Gamepad gamepad;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            CloseExistingModalOwners();
            ResetSession();

            inputFixture = new InputTestFixture();
            inputFixture.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            gamepad = InputSystem.AddDevice<Gamepad>();

            playerObject = new GameObject("Split the G Test Player");
            playerObject.AddComponent<CharacterController>();
            motor = playerObject.AddComponent<PlayerMotor>();
            interactor =
                playerObject.AddComponent<PlayerInteractor>();

            cameraObject = new GameObject("Split the G Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            cameraFollow =
                cameraObject.AddComponent<PlayerCameraFollow>();
            cameraFollow.Initialize(
                camera,
                playerObject.transform,
                true);
            motor.Initialize(camera, null, null);

            uiObject = new GameObject("Split the G Test UI");
            hud = uiObject.AddComponent<IntoxicationHudView>();
            view = uiObject.AddComponent<SplitTheGMinigameView>();
            controller =
                uiObject.AddComponent<SplitTheGMinigameController>();
            playerRuntime = new PlayerRuntime(
                playerObject,
                motor,
                interactor,
                null);
            controller.Initialize(
                view,
                hud,
                playerRuntime,
                cameraFollow,
                true);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            controller?.Cancel();
            if (keyboard != null && keyboard.added)
            {
                InputSystem.RemoveDevice(keyboard);
            }

            if (gamepad != null && gamepad.added)
            {
                InputSystem.RemoveDevice(gamepad);
            }

            if (mouse != null && mouse.added)
            {
                InputSystem.RemoveDevice(mouse);
            }

            Destroy(uiObject);
            Destroy(cameraObject);
            Destroy(playerObject);
            ResetSession();
            inputFixture?.TearDown();
            inputFixture = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator OpenAndCancel_LockAndRestoreModalState()
        {
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(cameraFollow.CinematicMotionEnabled, Is.True);
            Assert.That(hud.Visible, Is.True);

            Assert.That(controller.Open(interactor), Is.True);

            Assert.That(motor.InputEnabled, Is.False);
            Assert.That(interactor.InputEnabled, Is.False);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.False);
            Assert.That(cameraFollow.CinematicMotionEnabled, Is.False);
            Assert.That(hud.Visible, Is.False);

            controller.Cancel();

            Assert.That(controller.IsOpen, Is.False);
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(cameraFollow.CinematicMotionEnabled, Is.True);
            Assert.That(hud.Visible, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PerfectRelease_HidesLevelUntilSettledAndLocksGulp()
        {
            OpenAndArm();
            Assert.That(controller.BeginDrink(), Is.True);
            Assert.That(controller.IsExactLevelHidden, Is.True);

            float perfectDrinkTime = (float)(
                (1d - controller.Settings.TargetLevel) /
                controller.Settings.DrinkSpeed);
            controller.AdvancePresentation(perfectDrinkTime);
            Assert.That(
                controller.RemainingLevel,
                Is.EqualTo(controller.TargetLevel).Within(0.00001f));

            Assert.That(controller.ReleaseDrink(), Is.True);

            Assert.That(
                controller.Phase,
                Is.EqualTo(SplitTheGPhase.Settling));
            Assert.That(controller.IsExactLevelHidden, Is.True);
            Assert.That(controller.LastResult.Score, Is.EqualTo(100));
            Assert.That(
                controller.LastResult.Band,
                Is.EqualTo(SplitTheGResultBand.Perfect));
            Assert.That(controller.BeginDrink(), Is.False);
            Assert.That(controller.ReleaseDrink(), Is.False);

            controller.AdvancePresentation(
                (float)controller.Settings.SettlingTime * 0.5f +
                PhaseCompletionPadding);
            Assert.That(controller.IsExactLevelHidden, Is.True);
            controller.AdvancePresentation(
                (float)controller.Settings.SettlingTime * 0.5f);

            Assert.That(
                controller.Phase,
                Is.EqualTo(SplitTheGPhase.AttemptResult));
            Assert.That(controller.IsExactLevelHidden, Is.False);
            Assert.That(
                controller.RemainingLevel,
                Is.EqualTo(controller.TargetLevel).Within(0.00001f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThirdAttempt_RaisesCompletedExactlyOnce()
        {
            int completionCount = 0;
            controller.Completed += () => completionCount++;
            OpenAndArm();

            for (int attempt = 1;
                 attempt <= controller.MaximumAttempts;
                 attempt++)
            {
                FinishCurrentAttempt(0.8f);
                Assert.That(
                    controller.AttemptsCompleted,
                    Is.EqualTo(attempt));

                if (attempt < controller.MaximumAttempts)
                {
                    Assert.That(
                        controller.Phase,
                        Is.EqualTo(
                            SplitTheGPhase.AttemptResult));
                    Assert.That(completionCount, Is.Zero);
                    Assert.That(controller.Retry(), Is.True);
                    Arm();
                }
            }

            Assert.That(
                controller.Phase,
                Is.EqualTo(SplitTheGPhase.FinalResult));
            Assert.That(completionCount, Is.EqualTo(1));
            controller.AdvancePresentation(5f);
            Assert.That(completionCount, Is.EqualTo(1));
            Assert.That(controller.CloseFinalResult(), Is.True);
            Assert.That(completionCount, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReleasedDarkBeer_PersistsAcrossCancel()
        {
            GameSessionState.UpdateDrinkingProgress(
                17,
                DrinkId.RedWine,
                2);
            OpenAndArm();
            Assert.That(controller.BeginDrink(), Is.True);
            controller.AdvancePresentation(1f);
            Assert.That(controller.ReleaseDrink(), Is.True);

            int expectedGain = (int)Math.Round(
                DrinkRules.GetIntoxicationGain(DrinkId.DarkBeer) *
                controller.LastResult.ConsumedFraction,
                MidpointRounding.AwayFromZero);
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(17 + expectedGain));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.DarkBeer));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(3));

            controller.Cancel();

            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(17 + expectedGain));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.DarkBeer));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(3));
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NonPersistentLaunch_DoesNotChangeSessionState()
        {
            GameSessionState.UpdateDrinkingProgress(
                64,
                DrinkId.RedWine,
                7);
            controller.Initialize(
                view,
                hud,
                playerRuntime,
                cameraFollow,
                false);
            OpenAndArm();
            Assert.That(controller.BeginDrink(), Is.True);
            controller.AdvancePresentation(1f);
            Assert.That(controller.ReleaseDrink(), Is.True);
            Assert.That(controller.IntoxicationLevel, Is.GreaterThan(0));

            controller.Cancel();

            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(64));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.RedWine));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(7));
            Assert.That(GameSessionState.IsWasted, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PerfectSipAtNinetySix_CompletesWastedAfterSettling()
        {
            GameSessionState.UpdateDrinkingProgress(
                96,
                DrinkId.RedWine,
                4);
            int completionCount = 0;
            controller.Completed += () => completionCount++;
            OpenAndArm();
            Assert.That(controller.BeginDrink(), Is.True);
            float perfectDrinkTime = (float)(
                (1d - controller.Settings.TargetLevel) /
                controller.Settings.DrinkSpeed);
            controller.AdvancePresentation(perfectDrinkTime);
            Assert.That(controller.ReleaseDrink(), Is.True);

            Assert.That(
                controller.Phase,
                Is.EqualTo(SplitTheGPhase.Settling));
            Assert.That(completionCount, Is.Zero);
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(100));
            Assert.That(GameSessionState.IsWasted, Is.True);

            AdvanceThroughSettling();

            Assert.That(
                controller.Phase,
                Is.EqualTo(SplitTheGPhase.FinalResult));
            Assert.That(completionCount, Is.EqualTo(1));
            Assert.That(
                GameSessionState.WastedSecondsRemaining,
                Is.EqualTo(
                    SplitTheGMinigameController
                        .WastedDurationSeconds)
                    .Within(0.01f));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.DarkBeer));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(5));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SpaceMouseAndGamepadSouth_UsePressAndRelease()
        {
            Assert.That(controller.Open(interactor), Is.True);
            inputFixture.Press(
                keyboard.spaceKey,
                queueEventOnly: true);
            yield return null;

            controller.AdvancePresentation(
                (float)controller.Settings.CountdownTime);
            Assert.That(
                controller.Phase,
                Is.EqualTo(SplitTheGPhase.Armed));
            Assert.That(controller.IsAwaitingFreshPress, Is.True);
            yield return null;
            Assert.That(
                controller.Phase,
                Is.EqualTo(SplitTheGPhase.Armed));

            inputFixture.Release(
                keyboard.spaceKey,
                queueEventOnly: true);
            yield return null;
            Assert.That(controller.IsAwaitingFreshPress, Is.False);
            Assert.That(
                controller.Phase,
                Is.EqualTo(SplitTheGPhase.Armed));

            inputFixture.Press(
                keyboard.spaceKey,
                queueEventOnly: true);
            yield return null;
            Assert.That(
                controller.Phase,
                Is.EqualTo(SplitTheGPhase.Drinking));
            controller.AdvancePresentation(0.4f);
            inputFixture.Release(
                keyboard.spaceKey,
                queueEventOnly: true);
            yield return null;
            Assert.That(
                controller.Phase,
                Is.EqualTo(SplitTheGPhase.Settling));

            AdvanceThroughSettling();
            Assert.That(controller.Retry(), Is.True);
            Arm();

            inputFixture.Press(
                gamepad.buttonSouth,
                queueEventOnly: true);
            yield return null;
            Assert.That(
                controller.Phase,
                Is.EqualTo(SplitTheGPhase.Drinking));
            controller.AdvancePresentation(0.4f);
            inputFixture.Release(
                gamepad.buttonSouth,
                queueEventOnly: true);
            yield return null;
            Assert.That(
                controller.Phase,
                Is.EqualTo(SplitTheGPhase.Settling));

            AdvanceThroughSettling();
            Assert.That(controller.Retry(), Is.True);
            Arm();

            inputFixture.Set(
                mouse.position,
                new Vector2(
                    Screen.width * 0.5f,
                    Screen.height * 0.5f),
                queueEventOnly: true);
            yield return null;
            inputFixture.Press(
                mouse.leftButton,
                queueEventOnly: true);
            yield return null;
            Assert.That(
                controller.Phase,
                Is.EqualTo(SplitTheGPhase.Drinking));
            controller.AdvancePresentation(0.4f);
            inputFixture.Release(
                mouse.leftButton,
                queueEventOnly: true);
            yield return null;
            Assert.That(
                controller.Phase,
                Is.EqualTo(SplitTheGPhase.Settling));
        }

        private void OpenAndArm()
        {
            Assert.That(controller.Open(interactor), Is.True);
            Arm();
        }

        private void Arm()
        {
            controller.AdvancePresentation(
                (float)controller.Settings.CountdownTime);
            Assert.That(
                controller.Phase,
                Is.EqualTo(SplitTheGPhase.Armed));
        }

        private void FinishCurrentAttempt(float drinkTime)
        {
            Assert.That(controller.BeginDrink(), Is.True);
            controller.AdvancePresentation(drinkTime);
            Assert.That(controller.ReleaseDrink(), Is.True);
            AdvanceThroughSettling();
        }

        private void AdvanceThroughSettling()
        {
            controller.AdvancePresentation(
                (float)controller.Settings.SettlingTime +
                PhaseCompletionPadding);
        }

        private static void CloseExistingModalOwners()
        {
            foreach (MinigameDebugWindow window in
                     UnityEngine.Object.FindObjectsByType<
                         MinigameDebugWindow>(
                         FindObjectsInactive.Include))
            {
                window.Close();
            }

            foreach (CocktailMinigameController minigame in
                     UnityEngine.Object.FindObjectsByType<
                         CocktailMinigameController>(
                         FindObjectsInactive.Include))
            {
                minigame.Cancel();
            }

            foreach (BeerPongMinigameController minigame in
                     UnityEngine.Object.FindObjectsByType<
                         BeerPongMinigameController>(
                         FindObjectsInactive.Include))
            {
                minigame.Cancel();
            }

            foreach (SplitTheGMinigameController minigame in
                     UnityEngine.Object.FindObjectsByType<
                         SplitTheGMinigameController>(
                         FindObjectsInactive.Include))
            {
                minigame.Cancel();
            }
        }

        private static void ResetSession()
        {
            GameSessionState.EnterBar(null);
            GameSessionState.CompleteCityReturn();
            GameSessionState.ResetDrinkingState();
            GameSessionState.ClearRoute();
            GameSessionState.ClearVisitedBars();
        }

        private static void Destroy(GameObject gameObject)
        {
            if (gameObject != null)
            {
                UnityEngine.Object.Destroy(gameObject);
            }
        }
    }
}
