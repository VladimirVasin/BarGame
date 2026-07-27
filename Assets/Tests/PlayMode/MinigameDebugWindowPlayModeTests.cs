using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class MinigameDebugWindowPlayModeTests
    {
        private GameObject playerObject;
        private GameObject cameraObject;
        private GameObject uiObject;
        private PlayerMotor motor;
        private PlayerInteractor interactor;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView hud;
        private MinigameDebugWindow window;
        private InputTestFixture inputFixture;
        private Keyboard keyboard;
        private readonly List<MinigameDebugWindow> suspendedWindows =
            new List<MinigameDebugWindow>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            inputFixture = new InputTestFixture();
            inputFixture.Setup();
            suspendedWindows.Clear();
            MinigameDebugWindow[] existingWindows =
                UnityEngine.Object.FindObjectsByType<MinigameDebugWindow>(
                    FindObjectsInactive.Exclude);
            for (int index = 0;
                 index < existingWindows.Length;
                 index++)
            {
                MinigameDebugWindow existing = existingWindows[index];
                suspendedWindows.Add(existing);
                existing.enabled = false;
            }

            ResetSession();
            GameSessionState.UpdateDrinkingProgress(
                64,
                DrinkId.RedWine,
                7);

            playerObject = new GameObject("Debug Player");
            playerObject.AddComponent<CharacterController>();
            motor = playerObject.AddComponent<PlayerMotor>();
            interactor =
                playerObject.AddComponent<PlayerInteractor>();

            cameraObject = new GameObject("Debug Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraFollow =
                cameraObject.AddComponent<PlayerCameraFollow>();
            cameraFollow.Initialize(
                camera,
                playerObject.transform,
                false);
            motor.Initialize(camera, null, null);

            uiObject = new GameObject("Debug Runtime UI");
            hud = uiObject.AddComponent<IntoxicationHudView>();
            window = uiObject.AddComponent<MinigameDebugWindow>();
            var player = new PlayerRuntime(
                playerObject,
                motor,
                interactor,
                null);
            window.Initialize(player, cameraFollow, hud);
            keyboard = InputSystem.AddDevice<Keyboard>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            window?.ActiveDebugMinigame?.Cancel();
            if (keyboard != null && keyboard.added)
            {
                InputSystem.RemoveDevice(keyboard);
            }

            Destroy(uiObject);
            Destroy(cameraObject);
            Destroy(playerObject);
            ResetSession();
            for (int index = 0;
                 index < suspendedWindows.Count;
                 index++)
            {
                MinigameDebugWindow suspended =
                    suspendedWindows[index];
                if (suspended != null)
                {
                    suspended.enabled = true;
                }
            }

            suspendedWindows.Clear();
            inputFixture?.TearDown();
            inputFixture = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator F9_TogglesWindowAndRestoresModalState()
        {
            inputFixture.Press(
                keyboard.f9Key,
                queueEventOnly: true);
            yield return null;

            Assert.That(window.IsOpen, Is.True);
            Assert.That(motor.InputEnabled, Is.False);
            Assert.That(interactor.InputEnabled, Is.False);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.False);

            inputFixture.Release(
                keyboard.f9Key,
                queueEventOnly: true);
            yield return null;
            inputFixture.Press(
                keyboard.f9Key,
                queueEventOnly: true);
            yield return null;

            Assert.That(window.IsOpen, Is.False);
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(hud.Visible, Is.True);
        }

        [UnityTest]
        public IEnumerator Window_LaunchesEveryBuiltInWithFreshIsolatedState()
        {
            Assert.That(window.Open(), Is.True);
            Assert.That(window.IsOpen, Is.True);
            Assert.That(motor.InputEnabled, Is.False);
            Assert.That(interactor.InputEnabled, Is.False);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.False);
            Assert.That(hud.Visible, Is.False);

            Assert.That(
                window.TryLaunch(BarMinigameCatalog.CocktailId),
                Is.True);
            Assert.That(window.IsOpen, Is.False);
            var cocktail = window.ActiveDebugMinigame as
                CocktailMinigameController;
            Assert.That(cocktail, Is.Not.Null);
            Assert.That(cocktail.IsOpen, Is.True);
            Assert.That(
                cocktail.PresentationPhase,
                Is.EqualTo(
                    CocktailPresentationPhase.ChoosingBase));
            Assert.That(cocktail.IntoxicationLevel, Is.Zero);
            Assert.That(cocktail.ChooseBase(0), Is.True);
            cocktail.AdvancePresentation(
                CocktailMinigameController.BasePourDuration);
            int badIngredient = FindCocktailOffer(
                cocktail,
                id => !CocktailRules.AreCompatible(
                    cocktail.CurrentBase,
                    id));
            int goodIngredient = FindCocktailOffer(
                cocktail,
                id => CocktailRules.AreCompatible(
                    cocktail.CurrentBase,
                    id));
            Assert.That(badIngredient, Is.GreaterThanOrEqualTo(0));
            Assert.That(goodIngredient, Is.GreaterThanOrEqualTo(0));
            Assert.That(cocktail.AddIngredient(badIngredient), Is.True);
            cocktail.AdvancePresentation(
                CocktailMinigameController.IngredientPourDuration);
            Assert.That(cocktail.AddIngredient(goodIngredient), Is.True);
            cocktail.AdvancePresentation(
                CocktailMinigameController.IngredientPourDuration);
            Assert.That(cocktail.ServeCocktail(), Is.True);
            Assert.That(cocktail.IntoxicationLevel, Is.GreaterThan(0));
            AssertSessionUnchanged();
            Assert.That(motor.InputEnabled, Is.False);
            Assert.That(interactor.InputEnabled, Is.False);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.False);

            cocktail.Cancel();
            AssertSessionUnchanged();
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(hud.Visible, Is.True);

            Assert.That(window.Open(), Is.True);
            Assert.That(
                window.TryLaunch(BarMinigameCatalog.BeerPongId),
                Is.True);
            var beerPong = window.ActiveDebugMinigame as
                BeerPongMinigameController;
            Assert.That(beerPong, Is.Not.Null);
            Assert.That(beerPong.IsOpen, Is.True);
            Assert.That(beerPong.IntoxicationLevel, Is.Zero);
            Assert.That(beerPong.BeginCharging(), Is.True);
            Assert.That(beerPong.ReleaseThrow(), Is.True);
            Assert.That(
                beerPong.ResolveFlightForTests(
                    BeerPongFlightResult.CreateMiss(
                        BeerPongMissReason.OutOfBounds)),
                Is.True);
            Assert.That(
                beerPong.IntoxicationLevel,
                Is.EqualTo(
                    BeerPongSession.MissIntoxicationGain));
            AssertSessionUnchanged();

            beerPong.Cancel();
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(hud.Visible, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReopeningWindow_CancelsRunningDebugGameBeforeLocking()
        {
            Assert.That(
                window.TryLaunch(BarMinigameCatalog.CocktailId),
                Is.True);
            IBarMinigame running = window.ActiveDebugMinigame;
            Assert.That(running.IsOpen, Is.True);

            Assert.That(window.Open(), Is.True);

            Assert.That(running.IsOpen, Is.False);
            Assert.That(window.IsOpen, Is.True);
            Assert.That(motor.InputEnabled, Is.False);
            Assert.That(interactor.InputEnabled, Is.False);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.False);

            Assert.That(window.Close(), Is.True);
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(hud.Visible, Is.True);
            yield return null;
        }

        private void AssertSessionUnchanged()
        {
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
        }

        private static int FindCocktailOffer(
            CocktailMinigameController cocktail,
            Predicate<CocktailIngredientId> predicate)
        {
            for (int index = 0;
                 index < cocktail.OfferCount;
                 index++)
            {
                if (predicate(cocktail.GetOfferId(index)))
                {
                    return index;
                }
            }

            return -1;
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
