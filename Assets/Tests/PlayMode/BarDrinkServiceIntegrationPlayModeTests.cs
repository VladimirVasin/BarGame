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
            BarTestTime.StartOpen();
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

        [UnityTest]
        public IEnumerator DebugWindow_SetsOnlyDaysOneThroughSeven()
        {
            Assert.That(
                GameSessionState.TryStartGameTimeFromWake(),
                Is.True);
            GameSessionState.AdvanceGameTime(394.5f);
            double timeBeforeChange =
                GameSessionState.GameTimeOfDayMinutes;

            Assert.That(debugWindow.TrySetGameDayNumber(7), Is.False);
            Assert.That(debugWindow.Open(), Is.True);
            Assert.That(debugWindow.CurrentGameDayNumber, Is.EqualTo(1));
            Assert.That(debugWindow.TrySetGameDayNumber(7), Is.True);
            Assert.That(debugWindow.CurrentGameDayNumber, Is.EqualTo(7));
            Assert.That(GameSessionState.GameDayIndex, Is.EqualTo(6));
            Assert.That(
                GameSessionState.GameTimeOfDayMinutes,
                Is.EqualTo(timeBeforeChange));
            Assert.That(debugWindow.TrySetGameDayNumber(0), Is.False);
            Assert.That(debugWindow.TrySetGameDayNumber(8), Is.False);
            Assert.That(debugWindow.TrySetGameDayNumber(3), Is.True);
            Assert.That(debugWindow.CurrentGameDayNumber, Is.EqualTo(3));
            Assert.That(debugWindow.TrySetGameDayNumber(1), Is.True);
            Assert.That(debugWindow.CurrentGameDayNumber, Is.EqualTo(1));
            Assert.That(GameSessionState.GameTimeOfDayMinutes,
                Is.EqualTo(timeBeforeChange),
                "Backward previews must preserve the same time of day.");
            Assert.That(debugWindow.Close(), Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DebugWindow_SelectsLiveWeatherAndReturnsToSchedule()
        {
            // This window can produce lightning, but the default seed's
            // midday schedule is clear. A forced storm must use that window.
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60 + 3), Is.True);
            GameSessionState.AdvanceGameTime(.25f / (float)GameTimeState.GameMinutesPerRealSecond);
            double minute = GameSessionState.GameTimeOfDayMinutes;
            int seed = GameSessionState.CitySeed;
            WeatherVisualSample scheduled = GameWeatherRules.Evaluate(seed, minute);
            WindSample scheduledWind = GameWeatherRules.EvaluateWind(seed, minute);
            Assert.That(GameWeatherRules.EvaluateCurrentLightning().IsFlashing, Is.False);

            var rain = rootObject.AddComponent<CityRainField>();
            rain.Initialize(player.GameObject.transform, RuntimePrimitiveFactory.DefaultMaterial, seed);
            var weather = rootObject.AddComponent<CityWeatherController>();
            weather.Initialize(rain, null, null, null, player.GameObject.transform, null, new CityEternalRainShaper());

            Assert.That(debugWindow.TrySetWeather(WeatherKind.Thunderstorm), Is.False);
            Assert.That(debugWindow.Open(), Is.True);
            Assert.That(debugWindow.TrySetWeather(WeatherKind.Thunderstorm), Is.True);
            weather.ApplyCurrentWeather();
            Assert.That(rain.Intensity, Is.EqualTo(GameWeatherRules.ThunderstormIntensity));
            Assert.That(CityWaterResources.RainIntensity, Is.EqualTo(rain.Intensity));
            Assert.That(weather.CurrentWind.Strength01,
                Is.EqualTo(GameWeatherRules.ThunderstormWindStrength * GameWeatherRules.EvaluateCurrentGust()).Within(.0001f));
            Assert.That(GameWeatherRules.EvaluateCurrentLightning().IsFlashing, Is.True);
            Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(minute));

            Assert.That(debugWindow.TrySetWeather(WeatherKind.HeavyRain), Is.True);
            Assert.That(GameWeatherRules.EvaluateCurrentLightning().IsFlashing, Is.False);
            Assert.That(debugWindow.TrySetWeather(WeatherKind.Clear), Is.True);
            Assert.That(debugWindow.Close(), Is.True);
            weather.ApplyCurrentWeather();
            Assert.That(rain.Intensity, Is.EqualTo(CityEternalRainShaper.DrizzleIntensity));

            Assert.That(debugWindow.Open(), Is.True);
            Assert.That(debugWindow.TrySetWeather(null), Is.True);
            Assert.That(GameWeatherRules.EvaluateCurrent().IsVisuallyEquivalentTo(scheduled), Is.True);
            Assert.That(GameWeatherRules.EvaluateCurrentWind().Strength01, Is.EqualTo(scheduledWind.Strength01));
            Assert.That(debugWindow.TrySetWeather(WeatherKind.LightRain), Is.True);
            Assert.That(debugWindow.Close(), Is.True);
            GameSessionState.BeginNewGame("debug_weather_reset_test");
            Assert.That(GameSessionState.DebugWeatherOverride, Is.Null);
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
            GameSessionState.BeginNewGame();
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
