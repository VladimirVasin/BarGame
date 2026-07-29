using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class DrinkShopPlayModeTests
    {
        private GameObject playerObject;
        private GameObject cameraObject;
        private GameObject uiObject;
        private PlayerMotor motor;
        private PlayerInteractor interactor;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView hud;
        private BarDrinkShopView view;
        private BarDrinkShopController controller;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            CloseExistingModalOwners();
            ResetSession();

            playerObject = new GameObject("Drink Shop Test Player");
            playerObject.AddComponent<CharacterController>();
            motor = playerObject.AddComponent<PlayerMotor>();
            interactor =
                playerObject.AddComponent<PlayerInteractor>();

            cameraObject = new GameObject("Drink Shop Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraFollow =
                cameraObject.AddComponent<PlayerCameraFollow>();
            cameraFollow.Initialize(
                camera,
                playerObject.transform,
                true);
            motor.Initialize(camera, null, null);

            uiObject = new GameObject("Drink Shop Test UI");
            hud = uiObject.AddComponent<IntoxicationHudView>();
            view = uiObject.AddComponent<BarDrinkShopView>();
            controller =
                uiObject.AddComponent<BarDrinkShopController>();
            controller.Initialize(view, hud, cameraFollow);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            controller?.Close();
            Destroy(uiObject);
            Destroy(cameraObject);
            Destroy(playerObject);
            ResetSession();
            yield return null;
        }

        [UnityTest]
        public IEnumerator OpenAndCancel_LocksAndRestoresModalState()
        {
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(
                cameraFollow.CinematicMotionEnabled,
                Is.True);
            Assert.That(hud.Visible, Is.True);

            Assert.That(controller.Open(interactor), Is.True);

            Assert.That(controller.IsOpen, Is.True);
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
            Assert.That(
                cameraFollow.CinematicMotionEnabled,
                Is.True);
            Assert.That(hud.Visible, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            SuccessfulPurchase_CommitsExactlyOnceAndCloses()
        {
            Assert.That(
                controller.Offers.Count,
                Is.EqualTo(BarDrinkShopView.RowCount));
            int offerIndex = FindFirstAlcoholicOffer();
            BarDrinkOffer offer = controller.Offers[offerIndex];
            int cashBefore = GameSessionState.CashBalance;
            int intoxicationBefore =
                GameSessionState.IntoxicationLevel;
            int drinksBefore = GameSessionState.DrinksConsumed;

            Assert.That(controller.Open(interactor), Is.True);
            Assert.That(controller.Select(offerIndex), Is.True);
            Assert.That(
                controller.ConfirmSelection(),
                Is.True);

            Assert.That(controller.IsOpen, Is.False);
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(cashBefore - offer.Price));
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(
                    Mathf.Min(
                        100,
                        intoxicationBefore +
                        DrinkRules.GetIntoxicationGain(
                            offer.DrinkId))));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksBefore + 1));

            Assert.That(
                controller.ConfirmSelection(),
                Is.False);
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(cashBefore - offer.Price));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksBefore + 1));
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            MaximumIntoxicationFailure_LeavesOpenAndDoesNotDebit()
        {
            GameSessionState.UpdateDrinkingProgress(
                100,
                DrinkId.RedWine,
                3);
            int offerIndex = FindFirstAlcoholicOffer();
            int cashBefore = GameSessionState.CashBalance;
            int drinksBefore = GameSessionState.DrinksConsumed;

            Assert.That(controller.Open(interactor), Is.True);
            Assert.That(controller.Select(offerIndex), Is.True);
            Assert.That(
                controller.ConfirmSelection(),
                Is.False);

            Assert.That(controller.IsOpen, Is.True);
            Assert.That(
                controller.FeedbackKey,
                Is.EqualTo(
                    "drink_shop.failure.maximum_intoxication"));
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(cashBefore));
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(100));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksBefore));
            Assert.That(motor.InputEnabled, Is.False);
            Assert.That(interactor.InputEnabled, Is.False);

            controller.Cancel();
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            InsufficientFundsFailure_NeverMakesBalanceNegative()
        {
            const int waterPurchases = 499;
            for (int index = 0; index < waterPurchases; index++)
            {
                Assert.That(
                    GameSessionState.TryPurchaseDrink(
                        DrinkId.Water).Succeeded,
                    Is.True);
            }

            Assert.That(GameSessionState.CashBalance, Is.EqualTo(1));
            int offerIndex = FindFirstAlcoholicOffer();
            int drinksBefore = GameSessionState.DrinksConsumed;

            Assert.That(controller.Open(interactor), Is.True);
            Assert.That(controller.Select(offerIndex), Is.True);
            Assert.That(
                controller.PreviewSelection().CashAfter,
                Is.EqualTo(1));
            Assert.That(
                controller.ConfirmSelection(),
                Is.False);

            Assert.That(controller.IsOpen, Is.True);
            Assert.That(
                controller.FeedbackKey,
                Is.EqualTo(
                    "drink_shop.failure.insufficient_funds"));
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(1));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksBefore));

            controller.Cancel();
            yield return null;
        }

        private int FindFirstAlcoholicOffer()
        {
            for (int index = 0;
                 index < controller.Offers.Count;
                 index++)
            {
                if (DrinkRules.IsAlcoholic(
                        controller.Offers[index].DrinkId))
                {
                    return index;
                }
            }

            Assert.Fail("Expected at least one alcoholic bar offer.");
            return -1;
        }

        private static void CloseExistingModalOwners()
        {
            foreach (BarDrinkShopController shop in
                     Object.FindObjectsByType<
                         BarDrinkShopController>(
                         FindObjectsInactive.Include))
            {
                shop.Close();
            }

            foreach (MinigameDebugWindow window in
                     Object.FindObjectsByType<
                         MinigameDebugWindow>(
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
        }

        private static void ResetSession()
        {
            GameSessionState.ResetEconomyState();
            GameSessionState.ResetDrinkingState();
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
