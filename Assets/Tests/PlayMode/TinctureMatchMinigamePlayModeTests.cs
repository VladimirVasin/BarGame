using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class TinctureMatchMinigamePlayModeTests
    {
        private GameObject playerObject;
        private GameObject cameraObject;
        private GameObject uiObject;
        private PlayerMotor motor;
        private PlayerInteractor interactor;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView hud;
        private TinctureMatchMinigameView view;
        private TinctureMatchMinigameController controller;
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

            playerObject =
                new GameObject("Tincture Match Test Player");
            playerObject.AddComponent<CharacterController>();
            motor = playerObject.AddComponent<PlayerMotor>();
            interactor =
                playerObject.AddComponent<PlayerInteractor>();

            cameraObject =
                new GameObject("Tincture Match Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            cameraFollow =
                cameraObject.AddComponent<PlayerCameraFollow>();
            cameraFollow.Initialize(
                camera,
                playerObject.transform,
                true);
            motor.Initialize(camera, null, null);

            uiObject = new GameObject("Tincture Match Test UI");
            hud = uiObject.AddComponent<IntoxicationHudView>();
            view =
                uiObject.AddComponent<TinctureMatchMinigameView>();
            controller =
                uiObject.AddComponent<
                    TinctureMatchMinigameController>();
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

            if (mouse != null && mouse.added)
            {
                InputSystem.RemoveDevice(mouse);
            }

            if (gamepad != null && gamepad.added)
            {
                InputSystem.RemoveDevice(gamepad);
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
            Assert.That(
                cameraFollow.CinematicMotionEnabled,
                Is.False);
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
        public IEnumerator Reopen_UsesNextDeterministicBoard()
        {
            Assert.That(controller.Open(interactor), Is.True);
            TinctureTileKind[] firstBoard =
                controller.Board.ToArray();
            controller.Cancel();

            Assert.That(controller.Open(interactor), Is.True);
            TinctureTileKind[] secondBoard =
                controller.Board.ToArray();
            bool differs = false;
            for (int index = 0;
                 index < firstBoard.Length;
                 index++)
            {
                if (firstBoard[index] == secondBoard[index])
                {
                    continue;
                }

                differs = true;
                break;
            }

            Assert.That(
                differs,
                Is.True,
                "A replay must advance the deterministic board sequence.");
            controller.Cancel();
            yield return null;
        }

        [UnityTest]
        public IEnumerator InvalidNormalSwap_RollsBackWithoutMoveOrScore()
        {
            Assert.That(controller.Open(interactor), Is.True);
            TinctureMatchSwap invalid =
                FindInvalidNormalSwap(controller.Board);
            TinctureTileKind[] before =
                controller.Board.ToArray();

            Assert.That(
                controller.TrySwap(
                    invalid.First.Row,
                    invalid.First.Column,
                    invalid.Second.Row,
                    invalid.Second.Column),
                Is.False);
            Assert.That(
                controller.PresentationPhase,
                Is.EqualTo(
                    TinctureMatchPresentationPhase.InvalidSwap));
            Assert.That(controller.MovesCompleted, Is.Zero);
            Assert.That(controller.Score, Is.Zero);
            CollectionAssert.AreEqual(
                before,
                controller.Board.ToArray());

            controller.AdvancePresentation(
                TinctureMatchMinigameController
                    .InvalidSwapDuration +
                0.01f);
            Assert.That(
                controller.PresentationPhase,
                Is.EqualTo(
                    TinctureMatchPresentationPhase.AwaitingInput));
            yield return null;
        }

        [UnityTest]
        public IEnumerator MoonshineSwap_CommitsImmediatelyAndCancelDoesNotRefund()
        {
            Assert.That(controller.Open(interactor), Is.True);
            TinctureMatchSwap moonshine =
                FindMoonshineSwap(controller.Board);

            Assert.That(
                controller.TrySwap(
                    moonshine.First.Row,
                    moonshine.First.Column,
                    moonshine.Second.Row,
                    moonshine.Second.Column),
                Is.True);

            int expectedGain =
                DrinkRules.GetIntoxicationGain(DrinkId.Moonshine);
            Assert.That(
                controller.IntoxicationLevel,
                Is.EqualTo(expectedGain));
            Assert.That(controller.MoonshineActivations, Is.EqualTo(1));
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(expectedGain));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.Moonshine));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(1));

            controller.Cancel();

            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(expectedGain));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.Moonshine));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator MoonshineAtEighty_CompletesAtMaximumAfterCascade()
        {
            GameSessionState.UpdateDrinkingProgress(
                80,
                DrinkId.RedWine,
                3);
            int completionCount = 0;
            controller.Completed += () => completionCount++;
            Assert.That(controller.Open(interactor), Is.True);
            TinctureMatchSwap moonshine =
                FindMoonshineSwap(controller.Board);

            Assert.That(
                controller.TrySwap(
                    moonshine.First.Row,
                    moonshine.First.Column,
                    moonshine.Second.Row,
                    moonshine.Second.Column),
                Is.True);
            Assert.That(controller.IntoxicationLevel, Is.EqualTo(100));
            Assert.That(completionCount, Is.Zero);

            ResolvePresentation(controller);

            Assert.That(
                controller.PresentationPhase,
                Is.EqualTo(
                    TinctureMatchPresentationPhase.FinalResult));
            Assert.That(completionCount, Is.EqualTo(1));
            Assert.That(controller.CloseFinalResult(), Is.True);
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(100));
            yield return null;
        }

        [UnityTest]
        public IEnumerator FifteenNormalMoves_RaiseCompletedExactlyOnce()
        {
            int completionCount = 0;
            controller.Completed += () => completionCount++;
            Assert.That(controller.Open(interactor), Is.True);

            for (int move = 1;
                 move <= controller.Settings.MoveLimit;
                 move++)
            {
                TinctureMatchSwap swap =
                    GetFirstLegalNormalSwap(controller.Board);
                Assert.That(
                    controller.TrySwap(
                        swap.First.Row,
                        swap.First.Column,
                        swap.Second.Row,
                        swap.Second.Column),
                    Is.True,
                    $"Move {move}");
                Assert.That(
                    controller.TrySwap(
                        swap.First.Row,
                        swap.First.Column,
                        swap.Second.Row,
                        swap.Second.Column),
                    Is.False,
                    "Input must remain locked during resolution.");

                ResolvePresentation(controller);
                Assert.That(
                    controller.MovesCompleted,
                    Is.EqualTo(move));
                Assert.That(
                    completionCount,
                    Is.EqualTo(
                        move == controller.Settings.MoveLimit
                            ? 1
                            : 0));
            }

            Assert.That(
                controller.PresentationPhase,
                Is.EqualTo(
                    TinctureMatchPresentationPhase.FinalResult));
            Assert.That(controller.Score, Is.GreaterThan(0));
            Assert.That(controller.BestCascade, Is.GreaterThan(0));
            Assert.That(GameSessionState.IntoxicationLevel, Is.Zero);
            Assert.That(GameSessionState.DrinksConsumed, Is.Zero);

            controller.AdvancePresentation(100f);
            Assert.That(completionCount, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator CancelDuringFinalMove_StillRaisesCompletedOnce()
        {
            int completionCount = 0;
            controller.Completed += () => completionCount++;
            Assert.That(controller.Open(interactor), Is.True);

            for (int move = 1;
                 move < controller.Settings.MoveLimit;
                 move++)
            {
                TinctureMatchSwap swap =
                    GetFirstLegalNormalSwap(controller.Board);
                Assert.That(
                    controller.TrySwap(
                        swap.First.Row,
                        swap.First.Column,
                        swap.Second.Row,
                        swap.Second.Column),
                    Is.True,
                    $"Move {move}");
                ResolvePresentation(controller);
            }

            TinctureMatchSwap finalSwap =
                GetFirstLegalNormalSwap(controller.Board);
            Assert.That(
                controller.TrySwap(
                    finalSwap.First.Row,
                    finalSwap.First.Column,
                    finalSwap.Second.Row,
                    finalSwap.Second.Column),
                Is.True);
            Assert.That(
                controller.MovesCompleted,
                Is.EqualTo(controller.Settings.MoveLimit));
            Assert.That(completionCount, Is.Zero);

            controller.Cancel();

            Assert.That(controller.IsOpen, Is.False);
            Assert.That(completionCount, Is.EqualTo(1));
            controller.Cancel();
            Assert.That(completionCount, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PersistFalse_KeepsMoonshineProgressLocal()
        {
            GameSessionState.UpdateDrinkingProgress(
                64,
                DrinkId.RedWine,
                7);
            GameObject debugObject =
                new GameObject("Tincture Match Debug Test");
            debugObject.transform.SetParent(uiObject.transform, false);
            TinctureMatchMinigameView debugView =
                debugObject.AddComponent<TinctureMatchMinigameView>();
            TinctureMatchMinigameController debugController =
                debugObject.AddComponent<
                    TinctureMatchMinigameController>();
            debugController.Initialize(
                debugView,
                hud,
                playerRuntime,
                cameraFollow,
                false);

            Assert.That(debugController.Open(interactor), Is.True);
            TinctureMatchSwap moonshine =
                FindMoonshineSwap(debugController.Board);
            Assert.That(
                debugController.TrySwap(
                    moonshine.First.Row,
                    moonshine.First.Column,
                    moonshine.Second.Row,
                    moonshine.Second.Column),
                Is.True);
            Assert.That(
                debugController.IntoxicationLevel,
                Is.EqualTo(
                    DrinkRules.GetIntoxicationGain(
                        DrinkId.Moonshine)));
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(64));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.RedWine));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(7));

            debugController.Cancel();
            yield return null;
        }

        [UnityTest]
        public IEnumerator KeyboardGamepadAndMouse_PerformEquivalentSwaps()
        {
            yield return OpenAndUnlockInput();
            TinctureMatchSwap keyboardSwap =
                GetFirstLegalNormalSwap(controller.Board);
            yield return MoveKeyboardCursorTo(keyboardSwap.First);
            yield return PressAndRelease(keyboard.spaceKey);
            Assert.That(controller.HasSelection, Is.True);
            yield return MoveKeyboardCursorTo(keyboardSwap.Second);
            yield return PressAndRelease(keyboard.spaceKey);
            Assert.That(
                controller.PresentationPhase,
                Is.EqualTo(
                    TinctureMatchPresentationPhase.Swapping));
            ResolvePresentation(controller);
            Assert.That(controller.MovesCompleted, Is.EqualTo(1));
            controller.Cancel();
            yield return null;

            yield return OpenAndUnlockInput();
            TinctureMatchSwap gamepadSwap =
                GetFirstLegalNormalSwap(controller.Board);
            yield return MoveGamepadCursorTo(gamepadSwap.First);
            yield return PressAndRelease(gamepad.buttonSouth);
            Assert.That(controller.HasSelection, Is.True);
            yield return MoveGamepadCursorTo(gamepadSwap.Second);
            yield return PressAndRelease(gamepad.buttonSouth);
            Assert.That(
                controller.PresentationPhase,
                Is.EqualTo(
                    TinctureMatchPresentationPhase.Swapping));
            ResolvePresentation(controller);
            Assert.That(controller.MovesCompleted, Is.EqualTo(1));
            controller.Cancel();
            yield return null;

            yield return OpenAndUnlockInput();
            TinctureMatchSwap mouseSwap =
                GetFirstLegalNormalSwap(controller.Board);
            yield return ClickCell(mouseSwap.First);
            Assert.That(controller.HasSelection, Is.True);
            yield return ClickCell(mouseSwap.Second);
            Assert.That(
                controller.PresentationPhase,
                Is.EqualTo(
                    TinctureMatchPresentationPhase.Swapping));
            ResolvePresentation(controller);
            Assert.That(controller.MovesCompleted, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator MouseDrag_PerformsAdjacentSwap()
        {
            yield return OpenAndUnlockInput();
            TinctureMatchSwap swap =
                GetFirstLegalNormalSwap(controller.Board);

            yield return DragCells(swap.First, swap.Second);

            Assert.That(
                controller.PresentationPhase,
                Is.EqualTo(
                    TinctureMatchPresentationPhase.Swapping));
            ResolvePresentation(controller);
            Assert.That(controller.MovesCompleted, Is.EqualTo(1));
        }

        private IEnumerator OpenAndUnlockInput()
        {
            Assert.That(controller.Open(interactor), Is.True);
            yield return null;
            yield return null;
        }

        private IEnumerator MoveKeyboardCursorTo(
            TinctureMatchCell destination)
        {
            while (controller.CursorRow > destination.Row)
            {
                yield return PressAndRelease(keyboard.wKey);
            }

            while (controller.CursorRow < destination.Row)
            {
                yield return PressAndRelease(keyboard.sKey);
            }

            while (controller.CursorColumn > destination.Column)
            {
                yield return PressAndRelease(keyboard.aKey);
            }

            while (controller.CursorColumn < destination.Column)
            {
                yield return PressAndRelease(keyboard.dKey);
            }
        }

        private IEnumerator MoveGamepadCursorTo(
            TinctureMatchCell destination)
        {
            while (controller.CursorRow > destination.Row)
            {
                yield return PressAndRelease(gamepad.dpad.up);
            }

            while (controller.CursorRow < destination.Row)
            {
                yield return PressAndRelease(gamepad.dpad.down);
            }

            while (controller.CursorColumn > destination.Column)
            {
                yield return PressAndRelease(gamepad.dpad.left);
            }

            while (controller.CursorColumn < destination.Column)
            {
                yield return PressAndRelease(gamepad.dpad.right);
            }
        }

        private IEnumerator PressAndRelease(ButtonControl button)
        {
            inputFixture.Press(button, queueEventOnly: true);
            yield return null;
            inputFixture.Release(button, queueEventOnly: true);
            yield return null;
        }

        private IEnumerator ClickCell(TinctureMatchCell cell)
        {
            Vector2 screen = GetCellScreenPosition(cell);
            inputFixture.Set(
                mouse.position,
                screen,
                queueEventOnly: true);
            yield return null;
            yield return PressAndRelease(mouse.leftButton);
        }

        private IEnumerator DragCells(
            TinctureMatchCell from,
            TinctureMatchCell to)
        {
            inputFixture.Set(
                mouse.position,
                GetCellScreenPosition(from),
                queueEventOnly: true);
            yield return null;
            inputFixture.Press(
                mouse.leftButton,
                queueEventOnly: true);
            yield return null;
            inputFixture.Set(
                mouse.position,
                GetCellScreenPosition(to),
                queueEventOnly: true);
            yield return null;
            inputFixture.Release(
                mouse.leftButton,
                queueEventOnly: true);
            yield return null;
        }

        private static Vector2 GetCellScreenPosition(
            TinctureMatchCell cell)
        {
            Rect board =
                TinctureMatchMinigameController.BoardRect;
            Vector2 logical = new Vector2(
                board.x +
                (cell.Column + 0.5f) *
                TinctureMatchMinigameController.LogicalCellSize,
                board.y +
                (cell.Row + 0.5f) *
                TinctureMatchMinigameController.LogicalCellSize);
            RetroUiCanvas canvas =
                RetroUiTheme.CalculateCanvas(
                    Screen.width,
                    Screen.height);
            Vector2 screen = canvas.LogicalToScreen(logical);
            screen.y = Screen.height - screen.y;
            return screen;
        }

        private static TinctureMatchSwap
            GetFirstLegalNormalSwap(TinctureMatchBoard board)
        {
            var swaps =
                TinctureMatchResolver.GetLegalNormalSwaps(board);
            Assert.That(swaps, Is.Not.Empty);
            return swaps[0];
        }

        private static TinctureMatchSwap FindInvalidNormalSwap(
            TinctureMatchBoard board)
        {
            for (int row = 0; row < board.Rows; row++)
            {
                for (int column = 0;
                     column < board.Columns;
                     column++)
                {
                    var first =
                        new TinctureMatchCell(row, column);
                    if (!TinctureMatchResolver.IsNormalTile(
                            board[first.Row, first.Column]))
                    {
                        continue;
                    }

                    TinctureMatchCell[] candidates =
                    {
                        new TinctureMatchCell(row, column + 1),
                        new TinctureMatchCell(row + 1, column)
                    };
                    foreach (TinctureMatchCell second in candidates)
                    {
                        if (!board.Contains(second) ||
                            !TinctureMatchResolver.IsNormalTile(
                                board[second.Row, second.Column]) ||
                            TinctureMatchResolver.IsLegalNormalSwap(
                                board,
                                first,
                                second))
                        {
                            continue;
                        }

                        return new TinctureMatchSwap(first, second);
                    }
                }
            }

            Assert.Fail("Generated board has no invalid normal swap.");
            return default;
        }

        private static TinctureMatchSwap FindMoonshineSwap(
            TinctureMatchBoard board)
        {
            for (int row = 0; row < board.Rows; row++)
            {
                for (int column = 0;
                     column < board.Columns;
                     column++)
                {
                    if (board[row, column] !=
                        TinctureTileKind.Moonshine)
                    {
                        continue;
                    }

                    var moonshine =
                        new TinctureMatchCell(row, column);
                    TinctureMatchCell[] neighbors =
                    {
                        new TinctureMatchCell(row - 1, column),
                        new TinctureMatchCell(row, column + 1),
                        new TinctureMatchCell(row + 1, column),
                        new TinctureMatchCell(row, column - 1)
                    };
                    foreach (TinctureMatchCell neighbor in neighbors)
                    {
                        if (board.Contains(neighbor) &&
                            TinctureMatchResolver.IsNormalTile(
                                board[
                                    neighbor.Row,
                                    neighbor.Column]))
                        {
                            return new TinctureMatchSwap(
                                moonshine,
                                neighbor);
                        }
                    }
                }
            }

            Assert.Fail("Generated board has no usable Moonshine tile.");
            return default;
        }

        private static void ResolvePresentation(
            TinctureMatchMinigameController minigame)
        {
            minigame.AdvancePresentation(100f);
            Assert.That(
                minigame.PresentationPhase,
                Is.EqualTo(
                    minigame.MovesRemaining == 0 ||
                    minigame.IntoxicationLevel >= 100
                        ? TinctureMatchPresentationPhase.FinalResult
                        : TinctureMatchPresentationPhase.AwaitingInput));
        }

        private static void CloseExistingModalOwners()
        {
            foreach (MinigameDebugWindow window in
                     Object.FindObjectsByType<MinigameDebugWindow>(
                         FindObjectsInactive.Include))
            {
                window.Close();
            }

            foreach (CocktailMinigameController minigame in
                     Object.FindObjectsByType<
                         CocktailMinigameController>(
                         FindObjectsInactive.Include))
            {
                minigame.Cancel();
            }

            foreach (BeerPongMinigameController minigame in
                     Object.FindObjectsByType<
                         BeerPongMinigameController>(
                         FindObjectsInactive.Include))
            {
                minigame.Cancel();
            }

            foreach (SplitTheGMinigameController minigame in
                     Object.FindObjectsByType<
                         SplitTheGMinigameController>(
                         FindObjectsInactive.Include))
            {
                minigame.Cancel();
            }

            foreach (TinctureMatchMinigameController minigame in
                     Object.FindObjectsByType<
                         TinctureMatchMinigameController>(
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
                Object.Destroy(gameObject);
            }
        }
    }
}
