using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class BarDrinkServiceIntegrationPlayModeTests
    {
        private GameObject rootObject;
        private GameObject cameraObject;
        private GameObject uiObject;
        private PlayerRuntime player;
        private PlayerCameraFollow cameraFollow;
        private BarDrinkShopController shop;
        private MinigameDebugWindow debugWindow;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            CloseExistingModalOwners();
            ResetSession();

            rootObject = new GameObject("Drink Service Integration Root");
            cameraObject = new GameObject("Drink Service Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.transform.SetPositionAndRotation(
                new Vector3(0f, 3f, -5f),
                Quaternion.Euler(12f, 0f, 0f));

            uiObject = new GameObject("Drink Service Runtime UI");
            InteractionPromptView prompt =
                uiObject.AddComponent<InteractionPromptView>();
            IntoxicationHudView hud =
                uiObject.AddComponent<IntoxicationHudView>();
            BarDrinkShopView shopView =
                uiObject.AddComponent<BarDrinkShopView>();

            BarInteriorLayoutPlan layout =
                BarInteriorLayoutPlanner.Generate(
                    27183,
                    "drink-service-integration",
                    BarActivityKind.Cocktail);
            BarDrinkServicePlan servicePlan =
                BarDrinkServicePlan.FromLayout(layout);
            BarDrinkServiceView serviceView =
                BarDrinkServiceWorldBuilder.Build(
                    rootObject.transform,
                    servicePlan);

            player = PlayerFactory.Create(
                rootObject.transform,
                layout.PlayerSpawn,
                camera,
                null,
                prompt);
            cameraFollow =
                cameraObject.AddComponent<PlayerCameraFollow>();
            cameraFollow.Initialize(
                camera,
                player.GameObject.transform,
                true);

            shop = uiObject.AddComponent<BarDrinkShopController>();
            shop.Initialize(
                shopView,
                hud,
                cameraFollow,
                player,
                serviceView);
            debugWindow =
                uiObject.AddComponent<MinigameDebugWindow>();
            debugWindow.Initialize(
                player,
                cameraFollow,
                hud,
                null,
                null,
                shop);

            Assert.That(serviceView.Plan, Is.SameAs(servicePlan));
            Assert.That(
                serviceView.Bottles,
                Has.Count.EqualTo(
                    BarDrinkServicePlan.RequiredBottleCount));
            Assert.That(shop.ServiceView, Is.SameAs(serviceView));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            debugWindow?.ActiveDebugMinigame?.Cancel();
            debugWindow?.Close();
            shop?.Close();
            Destroy(uiObject);
            Destroy(cameraObject);
            Destroy(rootObject);
            ResetSession();
            yield return null;
        }

        [UnityTest]
        public IEnumerator DebugWindow_ReplacesPrecommitServiceButNotCommittedPour()
        {
            Assert.That(shop.Open(player.Interactor), Is.True);
            Assert.That(shop.IsServing, Is.False);

            Assert.That(debugWindow.Open(), Is.True);
            Assert.That(shop.IsOpen, Is.False);
            Assert.That(debugWindow.IsOpen, Is.True);
            Assert.That(debugWindow.Close(), Is.True);

            int cashBefore = GameSessionState.CashBalance;
            int drinksBefore = GameSessionState.DrinksConsumed;
            Assert.That(shop.Open(player.Interactor), Is.True);
            shop.AdvancePresentation(
                BarDrinkServiceTimeline.CameraApproachDurationSeconds);
            Assert.That(shop.IsBrowsing, Is.True);
            BarDrinkOffer selectedOffer = shop.SelectedOffer;
            Assert.That(shop.ConfirmSelection(), Is.True);
            Assert.That(shop.IsServing, Is.True);

            Assert.That(debugWindow.Open(), Is.False);
            Assert.That(
                debugWindow.TryLaunch(BarMinigameCatalog.CocktailId),
                Is.False);
            Assert.That(
                debugWindow.LastLaunchErrorKey,
                Is.EqualTo("debug.minigames.unavailable"));
            Assert.That(shop.IsOpen, Is.True);
            Assert.That(shop.IsServing, Is.True);
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(cashBefore - selectedOffer.Price));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksBefore + 1));

            shop.AdvancePresentation(
                BarDrinkServiceTimeline.ConfirmedPresentationDurationSeconds +
                0.01f);
            Assert.That(shop.IsOpen, Is.True);
            Assert.That(shop.IsBrowsing, Is.True);
            Assert.That(
                shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.Browsing));
            Assert.That(shop.IsServing, Is.False);
            Assert.That(shop.PurchaseCommitted, Is.False);
            Assert.That(shop.FirstPersonArms.IsVisible, Is.True);
            Assert.That(cameraFollow.FixedPoseActive, Is.True);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.True);
            Assert.That(player.Motor.InputEnabled, Is.False);
            Assert.That(player.Interactor.InputEnabled, Is.False);

            shop.Exit();
            Assert.That(shop.IsOpen, Is.True);
            Assert.That(
                shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.CameraReturn));
            shop.AdvancePresentation(
                BarDrinkServiceTimeline.CameraReturnDurationSeconds + 0.01f);
            Assert.That(shop.IsOpen, Is.False);
            Assert.That(
                shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.Closed));
            Assert.That(cameraFollow.FixedPoseActive, Is.False);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
            Assert.That(player.Motor.InputEnabled, Is.True);
            Assert.That(player.Interactor.InputEnabled, Is.True);
            yield return null;
        }

        private static void CloseExistingModalOwners()
        {
            foreach (BarDrinkShopController controller in
                     Object.FindObjectsByType<BarDrinkShopController>(
                         FindObjectsInactive.Include))
            {
                controller.Close();
            }

            foreach (MinigameDebugWindow window in
                     Object.FindObjectsByType<MinigameDebugWindow>(
                         FindObjectsInactive.Include))
            {
                window.Close();
            }
        }

        private static void ResetSession()
        {
            GameSessionState.ResetEconomyState();
            GameSessionState.ResetDrinkingState();
        }

        private static void Destroy(GameObject target)
        {
            if (target != null)
            {
                Object.Destroy(target);
            }
        }
    }
}
