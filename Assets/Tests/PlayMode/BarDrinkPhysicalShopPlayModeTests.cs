using System.Collections;
using System.Collections.Generic;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class BarDrinkPhysicalShopPlayModeTests
    {
        private GameObject worldObject;
        private GameObject cameraObject;
        private GameObject uiObject;
        private PlayerRuntime player;
        private Camera camera;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView hud;
        private BarDrinkShopController controller;
        private BarDrinkServiceView serviceView;
        private BarDrinkServicePlan servicePlan;
        private Renderer visibleSceneMarker;
        private Renderer hiddenSceneMarker;
        private bool[] initialRendererStates;
        private bool initialContactShadowState;
        private bool previousDepthOfFieldEnabled;
        private InputTestFixture inputFixture;
        private Keyboard keyboard;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousDepthOfFieldEnabled =
                GraphicsEffectsSettings.DepthOfFieldEnabled;
            GraphicsEffectsSettings.DepthOfFieldEnabled = true;
            CloseExistingModalOwners();
            ResetSession();

            worldObject = new GameObject("Physical Drink Shop Test World");
            BarInteriorLayoutPlan layout =
                BarInteriorLayoutPlanner.Generate(
                    20260731,
                    "physical-drink-shop-test",
                    BarActivityKind.Cocktail);
            servicePlan = BarDrinkServicePlan.FromLayout(layout);
            serviceView = BarDrinkServiceWorldBuilder.Build(
                worldObject.transform,
                servicePlan);

            cameraObject = new GameObject("Physical Drink Shop Test Camera");
            camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            player = PlayerFactory.Create(
                worldObject.transform,
                Vector3.zero,
                camera,
                null,
                null);
            cameraFollow =
                cameraObject.AddComponent<PlayerCameraFollow>();
            cameraFollow.Initialize(
                camera,
                player.GameObject.transform,
                true);

            uiObject = new GameObject("Physical Drink Shop Test UI");
            hud = uiObject.AddComponent<IntoxicationHudView>();
            BarDrinkShopView shopView =
                uiObject.AddComponent<BarDrinkShopView>();
            controller =
                uiObject.AddComponent<BarDrinkShopController>();
            controller.Initialize(
                shopView,
                hud,
                cameraFollow,
                player,
                serviceView);
            visibleSceneMarker = CreateSceneMarker(
                "Visible Drink Point Marker",
                true);
            hiddenSceneMarker = CreateSceneMarker(
                "Initially Hidden Drink Sign Marker",
                false);
            controller.ConfigureSceneMarkers(
                visibleSceneMarker,
                hiddenSceneMarker);

            yield return null;

            IReadOnlyList<Renderer> renderers =
                player.Visual.Renderers;
            initialRendererStates = new bool[renderers.Count];
            for (int index = 0; index < renderers.Count; index++)
            {
                initialRendererStates[index] = renderers[index].enabled;
            }

            initialContactShadowState = player.ContactShadow.enabled;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            controller?.Close();
            if (keyboard != null && keyboard.added)
            {
                InputSystem.RemoveDevice(keyboard);
            }

            GraphicsEffectsSettings.DepthOfFieldEnabled =
                previousDepthOfFieldEnabled;
            DestroyObject(uiObject);
            if (player.GameObject != null)
            {
                Object.Destroy(player.GameObject);
            }

            DestroyObject(cameraObject);
            DestroyObject(worldObject);
            inputFixture?.TearDown();
            inputFixture = null;
            ResetSession();
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            PhysicalCounterSeat_OpensOnlyAfterSitAndRestoresOnShopExit()
        {
            CounterSeatPlan seatPlan = CounterSeatPlan.FromService(
                serviceView.transform,
                servicePlan);
            player.Motor.Teleport(seatPlan.EntryPose.RootPosition);
            player.GameObject.transform.rotation =
                seatPlan.EntryPose.RootRotation;
            Physics.SyncTransforms();

            var stationObject = new GameObject("Physical Counter Seat");
            stationObject.transform.SetParent(worldObject.transform, false);
            BarCounterStation station =
                stationObject.AddComponent<BarCounterStation>();
            station.ConfigureSeated(
                controller,
                player,
                seatPlan,
                cameraFollow);

            Assert.That(controller.IsOpen, Is.False);
            station.Interact(player.Interactor);
            Assert.That(controller.IsOpen, Is.False,
                "Accepting E may begin only the visible approach/entry.");
            Assert.That(station.Seat.IsSeated, Is.False);

            float timeout = Time.realtimeSinceStartup + 5f;
            while (!station.Seat.IsSeated &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            Assert.That(station.Seat.IsSeated, Is.True);
            Assert.That(station.SeatView.IsFirstPerson, Is.True);
            Assert.That(controller.IsOpen, Is.True,
                "The shop opens from SeatedChanged, after the entry clip.");
            Assert.That(controller.UsesCounterSeatView, Is.True);
            Assert.That(cameraFollow.FixedPoseActive, Is.True);
            Assert.That(
                station.SeatView.HiddenHeadRendererCount,
                Is.GreaterThan(0));
            Assert.That(
                player.PresentationVisibility.IsHidden,
                Is.False,
                "The seated shop must not hide the complete world hero.");

            controller.AdvancePresentation(
                BarDrinkServiceTimeline.CameraApproachDurationSeconds);
            Assert.That(
                controller.FirstPersonArms.IsVisible,
                Is.False,
                "The seated bar view must not draw legacy camera-local arms.");
            Assert.That(
                controller.FirstPersonArms.VisualsSuppressed,
                Is.True);
            Assert.That(
                controller.FirstPersonArms.VisibilityAmount,
                Is.Zero);
            Assert.That(
                controller.FirstPersonArms.PresentationRoot.gameObject.activeSelf,
                Is.True,
                "The hidden attachment rig must remain active for the vessel.");
            Assert.That(controller.RestPhysicalMenuAtCounter(), Is.True);
            Assert.That(station.Seat.IsSeated, Is.True,
                "Closing the menu must not also stand the hero up.");
            Assert.That(station.Seat.RequestExit(), Is.True);

            timeout = Time.realtimeSinceStartup + 5f;
            while (station.Seat.Controller.IsActive &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            Assert.That(station.Seat.Controller.IsActive, Is.False);
            Assert.That(
                controller.MenuState,
                Is.EqualTo(
                    BarPromenade.Runtime.World.CounterMenuState.Retrieving));
            controller.AdvancePresentation(
                BarDrinkServiceTimeline.CameraReturnDurationSeconds + 0.01f);
            yield return null;

            Assert.That(controller.IsOpen, Is.False);
            Assert.That(station.Seat.IsSeated, Is.False);
            Assert.That(station.SeatView.IsFirstPerson, Is.False);
            Assert.That(cameraFollow.FixedPoseActive, Is.False);
            Assert.That(cameraFollow.CinematicMotionEnabled, Is.True);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(player.Motor.InputEnabled, Is.True);
            Assert.That(player.Interactor.InputEnabled, Is.True);
            AssertPlayerVisualRestored();
            Assert.That(
                player.GameObject.transform.position,
                Is.EqualTo(seatPlan.ExitPose.RootPosition)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
        }

        [UnityTest]
        public IEnumerator
            BeerTapService_WaitsForGazeThenHeroReturnsEmptyPint()
        {
            CounterSeatPlan seatPlan = CounterSeatPlan.FromService(
                serviceView.transform,
                servicePlan);
            player.Motor.Teleport(seatPlan.EntryPose.RootPosition);
            player.GameObject.transform.rotation =
                seatPlan.EntryPose.RootRotation;
            Physics.SyncTransforms();

            var stationObject = new GameObject("Beer Tap Test Station");
            stationObject.transform.SetParent(worldObject.transform, false);
            BarCounterStation station =
                stationObject.AddComponent<BarCounterStation>();
            station.ConfigureSeated(
                controller,
                player,
                seatPlan,
                cameraFollow);
            station.Interact(player.Interactor);

            float timeout = Time.realtimeSinceStartup + 5f;
            while (!station.Seat.IsSeated &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            Assert.That(station.Seat.IsSeated, Is.True);
            controller.ReportCounterServerAtTarget(true);
            controller.AdvancePresentation(
                BarDrinkServiceTimeline.CameraApproachDurationSeconds);
            Assert.That(controller.IsBrowsing, Is.True);
            Assert.That(
                controller.Select(FindOfferIndex(DrinkId.LightBeer)),
                Is.True);

            int cashBefore = GameSessionState.CashBalance;
            int intoxicationBefore = GameSessionState.IntoxicationLevel;
            int drinksBefore = GameSessionState.DrinksConsumed;
            Assert.That(
                station.PromptKey,
                Is.EqualTo(BarCounterStation.OrderPromptKey),
                "The ordinary E prompt must confirm the open bar menu.");
            station.Interact(player.Interactor);
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.BeerWalkToTap));
            Assert.That(serviceView.HasBeerTapPresentation, Is.True);
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(cashBefore - controller.SelectedOffer.Price));
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(intoxicationBefore));
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(drinksBefore));

            Assert.That(controller.ReportBeerServerAtTap(true), Is.True);
            controller.AdvancePresentation(
                BarDrinkServiceTimeline.BeerGlassPickupDurationSeconds +
                BarDrinkServiceTimeline.BeerPouringDurationSeconds * 0.5f);
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.BeerPouring));
            Assert.That(serviceView.IsStreamVisible, Is.True);
            Assert.That(serviceView.BeerTapHandlePullAmount, Is.GreaterThan(0f));
            Assert.That(
                serviceView.ActiveVessel.FillProgress,
                Is.InRange(0.1f, 0.9f));

            controller.AdvancePresentation(
                BarDrinkServiceTimeline.BeerPouringDurationSeconds * 0.5f);
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.BeerCarryToGuest));
            Assert.That(controller.ReportBeerServerAtGuest(true), Is.True);
            controller.AdvancePresentation(
                BarDrinkServiceTimeline.BeerGlassPlacementDurationSeconds);

            Assert.That(controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.AwaitingDrink));
            Assert.That(controller.IsServing, Is.True);
            Assert.That(serviceView.ActiveVessel.FillProgress, Is.EqualTo(1f));
            controller.AdvancePresentation(30f);
            Assert.That(controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.AwaitingDrink),
                "A full pint must wait indefinitely for an explicit drink.");
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(drinksBefore));

            BarDrinkVesselView vessel = serviceView.ActiveVessel;
            Vector3 lookDirection =
                vessel.GlassRenderer.bounds.center - camera.transform.position;
            camera.transform.rotation = Quaternion.LookRotation(
                lookDirection.normalized,
                Vector3.up);
            controller.RefreshServedDrinkAffordance();
            Assert.That(controller.IsLookingAtServedVessel, Is.True);
            Assert.That(vessel.IsInteractionHighlighted, Is.True);
            Assert.That(
                station.PromptKey,
                Is.EqualTo(BarCounterStation.DrinkPromptKey));

            station.Interact(player.Interactor);
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.PlayerPickup));
            Assert.That(station.Seat.Controller.IsNestedLoopActionActive, Is.True);
            Assert.That(station.SeatView.IsActionLookLocked, Is.True);
            Assert.That(vessel.IsInteractionHighlighted, Is.False);

            float previousTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 20f;
                timeout = Time.realtimeSinceStartup + 3f;
                while (controller.Phase !=
                           BarDrinkServicePhase.PlayerDrinking &&
                       controller.IsServing &&
                       Time.realtimeSinceStartup < timeout)
                {
                    yield return null;
                }

                Assert.That(controller.Phase,
                    Is.EqualTo(BarDrinkServicePhase.PlayerDrinking));
                yield return null;
                Assert.That(
                    station.Seat.Controller.LeftVesselGripAnchor,
                    Is.Not.Null);
                Assert.That(
                    controller.PlayerVesselGripError,
                    Is.LessThan(0.015f),
                    "The pint must follow the real Hero V2 hand socket.");

                timeout = Time.realtimeSinceStartup + 3f;
                while (controller.IsServing &&
                       Time.realtimeSinceStartup < timeout)
                {
                    yield return null;
                }
            }
            finally
            {
                Time.timeScale = previousTimeScale;
            }

            Assert.That(controller.IsServing, Is.False);
            Assert.That(controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.EmptyOnCounter));
            Assert.That(vessel.gameObject.activeSelf, Is.True);
            Assert.That(vessel.FillProgress, Is.Zero);
            Assert.That(station.SeatView.IsActionLookLocked, Is.False);
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(
                    intoxicationBefore +
                    DrinkRules.GetIntoxicationGain(DrinkId.LightBeer)));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksBefore + 1));
        }

        [UnityTest]
        public IEnumerator
            PhysicalCounterMenu_ListsFourDescriptionsThenServesAndRestores()
        {
            inputFixture = new InputTestFixture();
            inputFixture.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();

            const int waterPurchasesToLeaveOneRouble = 499;
            for (int index = 0;
                 index < waterPurchasesToLeaveOneRouble;
                 index++)
            {
                Assert.That(
                    GameSessionState.TryPurchaseDrink(
                        DrinkId.Water).Succeeded,
                    Is.True);
            }

            Assert.That(GameSessionState.CashBalance, Is.EqualTo(1));

            Pose menuDockPose = new Pose(
                controller.MenuPresentation.PropRoot.position,
                controller.MenuPresentation.PropRoot.rotation);
            var carrierObject = new GameObject("Bartender Menu Hand Test");
            carrierObject.transform.SetParent(worldObject.transform, false);
            carrierObject.transform.SetPositionAndRotation(
                menuDockPose.position +
                menuDockPose.rotation * new Vector3(-0.75f, 0.15f, 0f),
                menuDockPose.rotation);
            controller.ConfigureMenuCarrier(carrierObject.transform);

            CounterSeatPlan seatPlan = CounterSeatPlan.FromService(
                serviceView.transform,
                servicePlan);
            player.Motor.Teleport(seatPlan.EntryPose.RootPosition);
            player.GameObject.transform.rotation =
                seatPlan.EntryPose.RootRotation;
            Physics.SyncTransforms();

            var stationObject = new GameObject(
                "Physical Counter Menu Station");
            stationObject.transform.SetParent(worldObject.transform, false);
            stationObject.transform.position = seatPlan.InteractionPosition;
            SphereCollider stationTrigger =
                stationObject.AddComponent<SphereCollider>();
            stationTrigger.radius = 0.35f;
            stationTrigger.isTrigger = true;
            BarCounterStation station =
                stationObject.AddComponent<BarCounterStation>();
            station.ConfigureSeated(
                controller,
                player,
                seatPlan,
                cameraFollow);
            station.Interact(player.Interactor);

            float timeout = Time.realtimeSinceStartup + 5f;
            while (!station.Seat.IsSeated &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            Assert.That(station.Seat.IsSeated, Is.True);
            Assert.That(controller.UsesPhysicalMenu, Is.True);
            Assert.That(
                controller.MenuState,
                Is.EqualTo(
                    BarPromenade.Runtime.World.CounterMenuState.Delivering));
            Assert.That(
                CinematicDepthOfField.IsActive,
                Is.False,
                "Seating and the bartender's delivery must keep nearby " +
                "people on the restrained room grade.");

            controller.ReportCounterServerAtTarget(true);
            controller.AdvancePresentation(
                BarDrinkServiceTimeline.CameraApproachDurationSeconds);

            CounterMenuPageView menuPage =
                controller.MenuPresentation.Page;
            Assert.That(menuPage, Is.Not.Null);
            menuPage.AdvanceFold(CounterMenuPageView.FoldDurationSeconds);
            Assert.That(menuPage.FoldAmount, Is.Zero.Within(0.0001f));
            Assert.That(menuPage.IsFoldTransitionActive, Is.False);
            Assert.That(menuPage.LeftFoldHinge, Is.Not.Null);
            Assert.That(menuPage.RightFoldHinge, Is.Not.Null);
            float openLeafAngle = menuPage.LeftLeafAngleDegrees;
            Quaternion openLeftHingeRotation =
                menuPage.LeftFoldHinge.localRotation;
            Quaternion openRightHingeRotation =
                menuPage.RightFoldHinge.localRotation;

            Assert.That(controller.IsBrowsing, Is.True);
            Assert.That(
                controller.MenuState,
                Is.EqualTo(
                    BarPromenade.Runtime.World.CounterMenuState.Open));
            Assert.That(
                CinematicDepthOfField.IsActive,
                Is.True,
                "Only the actual menu close-up engages cinematic DOF.");
            Volume cinematicVolume = FindCinematicDepthOfFieldVolume();
            Assert.That(cinematicVolume, Is.Not.Null);
            Assert.That(
                cinematicVolume.profile.TryGet(
                    out UnityEngine.Rendering.Universal.DepthOfField
                        menuDepthOfField),
                Is.True);
            Assert.That(
                menuDepthOfField.aperture.value,
                Is.EqualTo(
                    BarDrinkShopController
                        .CounterMenuDepthOfFieldAperture));
            Assert.That(
                menuDepthOfField.focalLength.value,
                Is.EqualTo(
                    BarDrinkShopController
                        .CounterMenuDepthOfFieldFocalLength));
            Assert.That(controller.MenuPresentation.IsPlaced, Is.True);
            Assert.That(controller.MenuPresentation.IsTextVisible, Is.True);
            Assert.That(
                controller.MenuPresentation.ItemLines.Count,
                Is.EqualTo(4));
            Assert.That(controller.Offers.Count, Is.EqualTo(4));
            Assert.That(
                controller.Offers[0].DrinkId,
                Is.EqualTo(DrinkId.LightBeer));
            Assert.That(controller.Offers[0].Price, Is.EqualTo(8));

            for (int index = 0; index < controller.Offers.Count; index++)
            {
                BarDrinkOffer offer = controller.Offers[index];
                string expectedPrice = string.Format(
                    LocalizationService.Get("drink_shop.price"),
                    offer.Price);
                string expectedDescription = LocalizationService.Get(
                    offer.DescriptionKey);
                TMPro.TMP_Text itemLine =
                    controller.MenuPresentation.ItemLines[index];
                Assert.That(
                    itemLine.text,
                    Is.EqualTo(
                        LocalizationService.Get(offer.NameKey) +
                        "\n" + expectedPrice + "\n" +
                        expectedDescription));
                itemLine.ForceMeshUpdate();
                Assert.That(
                    itemLine.isTextOverflowing,
                    Is.False,
                    $"The description for menu row {index + 1} is cut off.");
                Assert.That(
                    itemLine.enableAutoSizing,
                    Is.False,
                    $"The menu row {index + 1} may not shrink independently.");
                Assert.That(
                    itemLine.fontSize,
                    Is.EqualTo(CounterMenuPageStyle.Bar.ItemFontSize)
                        .Within(0.0001f),
                    $"The menu row {index + 1} does not use the shared size.");

                float localX = controller.MenuPresentation.PropRoot
                    .InverseTransformPoint(
                        itemLine.transform.position).x;
                Assert.That(
                    localX,
                    index < 2
                        ? Is.LessThan(-0.01f)
                        : Is.GreaterThan(0.01f),
                    "The bar booklet must keep two offers on each page.");
            }

            int expensiveOfferIndex = FindOfferIndex(DrinkId.CognacVs);
            Assert.That(controller.Select(expensiveOfferIndex), Is.True);
            int drinksBeforeFailure = GameSessionState.DrinksConsumed;
            Assert.That(controller.ConfirmSelection(), Is.False);
            Assert.That(controller.IsBrowsing, Is.True);
            Assert.That(
                controller.MenuState,
                Is.EqualTo(
                    BarPromenade.Runtime.World.CounterMenuState.Open));
            Assert.That(controller.MenuPresentation.IsPlaced, Is.True);
            Assert.That(controller.MenuPresentation.IsTextVisible, Is.True);
            Assert.That(
                controller.MenuPresentation.SelectionMarker.text,
                Is.EqualTo("\u2022"));
            Assert.That(
                controller.FeedbackKey,
                Is.EqualTo("drink_shop.failure.insufficient_funds"));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksBeforeFailure));

            int servedOfferIndex = FindOfferIndex(DrinkId.RedWine);
            BarDrinkOffer servedOffer = controller.Offers[servedOfferIndex];
            Assert.That(
                GameSessionState.TryEarnCash(
                    servedOffer.Price - GameSessionState.CashBalance,
                    "bar-menu-focused-test"),
                Is.True);
            Assert.That(controller.Select(servedOfferIndex), Is.True);
            Assert.That(controller.ConfirmSelection(), Is.True);
            Assert.That(controller.IsServing, Is.True);
            Assert.That(controller.PurchaseCommitted, Is.True);
            Assert.That(
                serviceView.SelectedDrinkId,
                Is.EqualTo(DrinkId.RedWine));
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.BottlePickup));
            Assert.That(
                controller.MenuState,
                Is.EqualTo(
                    BarPromenade.Runtime.World.CounterMenuState.Resting));
            Assert.That(controller.MenuPresentation.IsPlaced, Is.True);
            Assert.That(controller.MenuPresentation.IsVisible, Is.True);
            Assert.That(controller.MenuPresentation.IsTextVisible, Is.False);
            Assert.That(CinematicDepthOfField.IsActive, Is.False,
                "Service must not leave the bartender behind menu Bokeh.");
            Assert.That(cinematicVolume.weight, Is.Zero.Within(0.0001f));
            Assert.That(
                controller.MenuPresentation.IsRestingOnCounter,
                Is.True);
            Assert.That(
                menuPage.FoldAmount,
                Is.Zero.Within(0.0001f),
                "Closing must begin from the fully open spread.");
            Assert.That(menuPage.IsFoldTransitionActive, Is.True);
            Assert.That(
                Quaternion.Angle(
                    openLeftHingeRotation,
                    menuPage.LeftFoldHinge.localRotation),
                Is.LessThan(0.001f));

            menuPage.AdvanceFold(
                CounterMenuPageView.FoldDurationSeconds * 0.5f);
            float midpointLeafAngle = menuPage.LeftLeafAngleDegrees;
            Assert.That(
                menuPage.FoldAmount,
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(menuPage.IsFoldTransitionActive, Is.True);
            Assert.That(
                midpointLeafAngle,
                Is.LessThan(openLeafAngle - 30f),
                "The left leaf must rotate over, not under, its spine.");
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    menuPage.LeftFoldHinge.localRotation),
                Is.EqualTo(Mathf.Abs(midpointLeafAngle)).Within(0.01f),
                "Fold progress must drive the physical hinge rotation.");
            Assert.That(
                Vector3.Dot(
                    FindRestingRenderer(menuPage, "Left Opaque Pages")
                        .bounds.center -
                    menuPage.LeftFoldHinge.parent.position,
                    menuPage.LeftFoldHinge.parent.up),
                Is.GreaterThan(0.10f),
                "The moving leaf must arc above the counter.");

            menuPage.AdvanceFold(
                CounterMenuPageView.FoldDurationSeconds * 0.5f);
            Assert.That(menuPage.FoldAmount, Is.EqualTo(1f));
            Assert.That(menuPage.IsFoldTransitionActive, Is.False);
            Assert.That(
                menuPage.LeftLeafAngleDegrees,
                Is.LessThan(midpointLeafAngle - 30f));
            Assert.That(
                Quaternion.Angle(
                    openRightHingeRotation,
                    menuPage.RightFoldHinge.localRotation),
                Is.LessThan(0.001f),
                "The right leaf stays on the counter while the left closes.");
            AssertClosedMenuUsesOpaqueFoldingPanels(menuPage);

            controller.AdvancePresentation(
                BarDrinkServiceTimeline.ConfirmedPresentationDurationSeconds +
                0.01f);

            Assert.That(controller.IsBrowsing, Is.False);
            Assert.That(controller.PurchaseCommitted, Is.False);
            Assert.That(
                controller.MenuState,
                Is.EqualTo(
                    BarPromenade.Runtime.World.CounterMenuState.Resting));
            Assert.That(controller.MenuPresentation.IsPlaced, Is.True);
            Assert.That(controller.MenuPresentation.IsVisible, Is.True);
            Assert.That(controller.MenuPresentation.IsTextVisible, Is.False);
            Assert.That(menuPage.IsRestingHighlighted, Is.False,
                "Closing the menu must not leave an open-state outline.");
            controller.AdvancePresentation(
                BarDrinkServiceTimeline.CameraReturnDurationSeconds + 0.1f);
            Assert.That(
                controller.MenuState,
                Is.EqualTo(
                    BarPromenade.Runtime.World.CounterMenuState.Resting),
                "Staff must not retrieve a menu while the hero is seated.");

            Vector3 restingCenter = controller.MenuPresentation.Page
                .RestingWorldCenter;
            BarMenuTestCameraAimDriver aimDriver =
                camera.gameObject.AddComponent<BarMenuTestCameraAimDriver>();
            aimDriver.Target = restingCenter;
            aimDriver.LookAway = true;
            aimDriver.IsAiming = true;
            yield return null;
            Assert.That(menuPage.IsRestingHighlighted, Is.False,
                "Looking away must leave the closed booklet unoutlined.");
            aimDriver.LookAway = false;
            yield return null;
            Assert.That(controller.IsLookingAtRestingMenu, Is.True);
            Assert.That(
                station.PromptKey,
                Is.EqualTo(
                    MountainRoadCafeMenuController.OpenMenuPromptKey));
            AssertRestingMenuHighlight(menuPage);
            station.Interact(player.Interactor);
            Assert.That(controller.IsBrowsing, Is.True);
            Assert.That(menuPage.IsRestingHighlighted, Is.False,
                "Opening must disable the closed-menu outline immediately.");
            Object.Destroy(aimDriver);
            Assert.That(
                controller.MenuState,
                Is.EqualTo(
                    BarPromenade.Runtime.World.CounterMenuState.Open));
            Assert.That(controller.MenuPresentation.IsPlaced, Is.True);
            Assert.That(controller.MenuPresentation.IsTextVisible, Is.False,
                "Text must remain hidden while the booklet unfolds.");
            Assert.That(menuPage.FoldAmount, Is.EqualTo(1f));
            Assert.That(menuPage.IsFoldTransitionActive, Is.True);
            menuPage.AdvanceFold(CounterMenuPageView.FoldDurationSeconds);
            Assert.That(menuPage.FoldAmount, Is.Zero.Within(0.0001f));
            Assert.That(menuPage.IsFoldTransitionActive, Is.False);
            Assert.That(
                Quaternion.Angle(
                    openLeftHingeRotation,
                    menuPage.LeftFoldHinge.localRotation),
                Is.LessThan(0.001f));
            Assert.That(controller.MenuPresentation.IsTextVisible, Is.True,
                "Readable text appears only after the menu is open again.");
            Assert.That(
                Vector3.Distance(
                    controller.MenuPresentation.PropRoot.position,
                    menuDockPose.position),
                Is.LessThan(0.001f));

            Assert.That(player.Interactor.InputEnabled, Is.True,
                "The seated loop must restore the ordinary interactor.");
            Assert.That(player.Interactor.isActiveAndEnabled, Is.True);
            Assert.That(station.CanInteract(player.Interactor), Is.True);
            yield return null;
            yield return null;
            Assert.That(station.Seat.IsSeated, Is.True);
            Assert.That(controller.CanRestPhysicalMenu, Is.True);
            Assert.That(station.CanInteract(player.Interactor), Is.True);
            stationObject.transform.position =
                player.Interactor.transform.position + Vector3.up * 0.8f;
            Physics.SyncTransforms();
            timeout = Time.realtimeSinceStartup + 1f;
            while (!ReferenceEquals(
                       player.Interactor.ActiveInteractable,
                       station) &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            Assert.That(
                player.Interactor.ActiveInteractable,
                Is.SameAs(station),
                "The seated station must remain the active interaction.");
            Assert.That(
                station.PromptKey,
                Is.EqualTo(BarCounterStation.OrderPromptKey),
                "E must advertise ordering, never silently close the menu.");
            inputFixture.Press(keyboard.escapeKey, queueEventOnly: true);
            yield return null;
            inputFixture.Release(keyboard.escapeKey, queueEventOnly: true);
            Assert.That(
                controller.MenuState,
                Is.EqualTo(
                    BarPromenade.Runtime.World.CounterMenuState.Resting));
            Assert.That(station.Seat.IsSeated, Is.True,
                "Escape closes the menu and must not stand the hero.");
            Assert.That(controller.MenuPresentation.IsPlaced, Is.True);
            Assert.That(controller.MenuPresentation.IsTextVisible, Is.False);
            Assert.That(menuPage.IsRestingHighlighted, Is.False);
            Assert.That(controller.IsLookingAtRestingMenu, Is.False,
                "Closing the close-up must disarm immediate gaze reopen.");
            Assert.That(
                station.PromptKey,
                Is.EqualTo(CounterSeatInteraction.StandPromptKey),
                "The close frame must offer standing, not reopen the menu.");

            restingCenter = controller.MenuPresentation.Page
                .RestingWorldCenter;
            camera.transform.rotation = Quaternion.LookRotation(
                camera.transform.position - restingCenter,
                Vector3.up);
            yield return null;
            camera.transform.rotation = Quaternion.LookRotation(
                restingCenter - camera.transform.position,
                Vector3.up);
            Assert.That(
                station.PromptKey,
                Is.EqualTo(
                    MountainRoadCafeMenuController.OpenMenuPromptKey));
            station.Interact(player.Interactor);
            Assert.That(
                controller.MenuState,
                Is.EqualTo(
                    BarPromenade.Runtime.World.CounterMenuState.Open));
            Assert.That(station.Seat.IsSeated, Is.True);
            Assert.That(CinematicDepthOfField.IsActive, Is.True,
                "Reopening the close-up must reacquire page-focused DOF.");

            Assert.That(controller.RestPhysicalMenuAtCounter(), Is.True);
            Assert.That(controller.IsLookingAtRestingMenu, Is.False);
            Assert.That(
                station.PromptKey,
                Is.EqualTo(CounterSeatInteraction.StandPromptKey));
            camera.transform.rotation = Quaternion.LookRotation(
                camera.transform.position -
                controller.MenuPresentation.Page.RestingWorldCenter,
                Vector3.up);
            yield return null;
            Assert.That(controller.IsLookingAtRestingMenu, Is.False);
            Assert.That(
                station.PromptKey,
                Is.EqualTo(CounterSeatInteraction.StandPromptKey));
            Assert.That(
                CinematicDepthOfField.IsActive,
                Is.False,
                "The resting booklet must release close-up Bokeh.");
            Assert.That(cinematicVolume.weight, Is.Zero.Within(0.0001f));
            station.Interact(player.Interactor);
            Assert.That(
                station.Seat.Controller.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Exiting));
            Assert.That(
                CinematicDepthOfField.IsActive,
                Is.False,
                "Standing must release Bokeh before third-person returns.");
            Assert.That(
                cinematicVolume.weight,
                Is.Zero.Within(0.0001f),
                "The chase camera must not inherit a DOF blend-out tail.");
            Assert.That(
                controller.MenuState,
                Is.EqualTo(
                    BarPromenade.Runtime.World.CounterMenuState.Resting),
                "The booklet remains on the counter throughout stand-up.");

            timeout = Time.realtimeSinceStartup + 5f;
            while (station.Seat.Controller.IsActive &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            Assert.That(station.Seat.Controller.IsActive, Is.False);
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.CameraReturn));
            Assert.That(
                controller.MenuState,
                Is.EqualTo(
                    BarPromenade.Runtime.World.CounterMenuState.Retrieving),
                "Retrieval starts only after the stand-up completes.");

            controller.AdvancePresentation(
                BarDrinkServiceTimeline.CameraReturnDurationSeconds + 0.01f);
            yield return null;

            Assert.That(controller.IsReturningCounterMenuHome, Is.True);
            Assert.That(controller.IsOpen, Is.True,
                "The carried booklet keeps the shared service occupied " +
                "until the server reaches home.");
            Assert.That(controller.CompleteCounterMenuReturnHome(), Is.True);
            Assert.That(controller.IsOpen, Is.False);
            Assert.That(station.Seat.IsSeated, Is.False);
            Assert.That(station.SeatView.IsFirstPerson, Is.False);
            Assert.That(cameraFollow.FixedPoseActive, Is.False);

            Assert.That(player.Motor.InputEnabled, Is.True);
            Assert.That(player.Interactor.InputEnabled, Is.True);
            AssertPlayerVisualRestored();
            Assert.That(
                Vector3.Distance(
                    player.GameObject.transform.position,
                    seatPlan.ExitPose.RootPosition),
                Is.LessThan(0.001f),
                "The visible exit may accumulate only sub-millimetre " +
                "floating-point drift.");
        }

        private static void AssertClosedMenuUsesOpaqueFoldingPanels(
            CounterMenuPageView page)
        {
            Assert.That(page, Is.Not.Null);
            Assert.That(page.LeftFoldHinge, Is.Not.Null);
            Assert.That(page.RightFoldHinge, Is.Not.Null);
            Assert.That(page.RestingPropRenderers.Count, Is.EqualTo(5));

            int leftLeafPanels = 0;
            int rightLeafPanels = 0;
            int spinePanels = 0;
            int baseColorId = Shader.PropertyToID("_BaseColor");
            var properties = new MaterialPropertyBlock();
            for (int index = 0;
                 index < page.RestingPropRenderers.Count;
                 index++)
            {
                Renderer renderer = page.RestingPropRenderers[index];
                Assert.That(renderer, Is.Not.Null);
                Assert.That(renderer.enabled, Is.True);
                Assert.That(renderer.gameObject.activeInHierarchy, Is.True);
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetColor(baseColorId).a,
                    Is.EqualTo(1f).Within(0.0001f),
                    $"Fold panel '{renderer.name}' must be opaque.");

                if (renderer.transform.IsChildOf(page.LeftFoldHinge))
                {
                    leftLeafPanels++;
                }
                else if (renderer.transform.IsChildOf(page.RightFoldHinge))
                {
                    rightLeafPanels++;
                }
                else
                {
                    spinePanels++;
                }
            }

            Assert.That(leftLeafPanels, Is.EqualTo(2));
            Assert.That(rightLeafPanels, Is.EqualTo(2));
            Assert.That(spinePanels, Is.EqualTo(1));

            Renderer rightCover = FindRestingRenderer(
                page,
                "Right Opaque Cover");
            Renderer rightPages = FindRestingRenderer(
                page,
                "Right Opaque Pages");
            Renderer leftPages = FindRestingRenderer(
                page,
                "Left Opaque Pages");
            Renderer leftCover = FindRestingRenderer(
                page,
                "Left Opaque Cover");
            Vector3 stackAxis = page.RightFoldHinge.up;
            Assert.That(
                ProjectedMinimum(rightPages, stackAxis),
                Is.GreaterThan(ProjectedMaximum(rightCover, stackAxis)),
                "The stationary pages intersect their cover.");
            Assert.That(
                ProjectedMinimum(leftPages, stackAxis),
                Is.GreaterThan(ProjectedMaximum(rightPages, stackAxis)),
                "The folded pages intersect the stationary pages.");
            Assert.That(
                ProjectedMinimum(leftCover, stackAxis),
                Is.GreaterThan(ProjectedMaximum(leftPages, stackAxis)),
                "The closed cover intersects the folded pages.");

            Collider[] colliders = page.LeftFoldHinge.parent
                .GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                Assert.That(
                    colliders[index].enabled &&
                    colliders[index].gameObject.activeInHierarchy,
                    Is.False,
                    "The folded menu must not add an active collider.");
            }
        }

        private static void AssertRestingMenuHighlight(
            CounterMenuPageView page)
        {
            Assert.That(page.IsRestingHighlighted, Is.True,
                "The yellow outline must share the open-menu prompt state.");
            Assert.That(page.RestingHighlightRenderers, Has.Count.EqualTo(4));
            int baseColorId = Shader.PropertyToID("_BaseColor");
            var properties = new MaterialPropertyBlock();
            for (int index = 0;
                 index < page.RestingHighlightRenderers.Count;
                 index++)
            {
                Renderer renderer = page.RestingHighlightRenderers[index];
                Assert.That(renderer, Is.Not.Null);
                Assert.That(renderer.gameObject.activeInHierarchy, Is.True);
                Assert.That(renderer.shadowCastingMode,
                    Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off));
                renderer.GetPropertyBlock(properties);
                Color colour = properties.GetColor(baseColorId);
                Assert.That(colour.r, Is.GreaterThan(0.9f));
                Assert.That(colour.g, Is.InRange(0.6f, 0.85f));
                Assert.That(colour.b, Is.LessThan(0.15f));
                properties.Clear();
            }
        }

        private static Renderer FindRestingRenderer(
            CounterMenuPageView page,
            string objectName)
        {
            for (int index = 0;
                 index < page.RestingPropRenderers.Count;
                 index++)
            {
                Renderer renderer = page.RestingPropRenderers[index];
                if (renderer != null && renderer.name == objectName)
                {
                    return renderer;
                }
            }

            Assert.Fail($"The physical fold has no '{objectName}'.");
            return null;
        }

        private static float ProjectedMinimum(
            Renderer renderer,
            Vector3 axis)
        {
            return ProjectRenderer(renderer, axis, false);
        }

        private static float ProjectedMaximum(
            Renderer renderer,
            Vector3 axis)
        {
            return ProjectRenderer(renderer, axis, true);
        }

        private static float ProjectRenderer(
            Renderer renderer,
            Vector3 axis,
            bool maximum)
        {
            Bounds local = renderer.localBounds;
            float result = maximum
                ? float.NegativeInfinity
                : float.PositiveInfinity;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = local.center + Vector3.Scale(
                            local.extents,
                            new Vector3(x, y, z));
                        float projection = Vector3.Dot(
                            renderer.transform.TransformPoint(corner),
                            axis);
                        result = maximum
                            ? Mathf.Max(result, projection)
                            : Mathf.Min(result, projection);
                    }
                }
            }

            return result;
        }

        private static Volume FindCinematicDepthOfFieldVolume()
        {
            Volume[] volumes = Resources.FindObjectsOfTypeAll<Volume>();
            for (int index = 0; index < volumes.Length; index++)
            {
                if (volumes[index] != null &&
                    volumes[index].name == "Cinematic Depth Of Field")
                {
                    return volumes[index];
                }
            }

            return null;
        }

        [UnityTest]
        public IEnumerator
            OpenAndExplicitExit_UseFirstPersonLifecycleAndRestoreState()
        {
            Assert.That(cameraFollow.FixedPoseActive, Is.False);
            Assert.That(player.Motor.InputEnabled, Is.True);
            Assert.That(player.Interactor.InputEnabled, Is.True);
            Assert.That(hud.Visible, Is.True);
            Assert.That(visibleSceneMarker.enabled, Is.True);
            Assert.That(hiddenSceneMarker.enabled, Is.False);
            AssertPlayerVisualRestored();

            Assert.That(controller.Open(player.Interactor), Is.True);

            Assert.That(controller.IsOpen, Is.True);
            Assert.That(
                CinematicDepthOfField.IsActive,
                Is.True,
                "The counter shot must engage the cinematic bokeh.");
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.CameraApproach));
            Assert.That(player.Motor.InputEnabled, Is.False);
            Assert.That(player.Interactor.InputEnabled, Is.False);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.False);
            Assert.That(hud.Visible, Is.False);
            Assert.That(visibleSceneMarker.enabled, Is.False);
            Assert.That(hiddenSceneMarker.enabled, Is.False);
            Assert.That(cameraFollow.FixedPoseActive, Is.True);
            Assert.That(controller.FirstPersonArms.IsVisible, Is.False);
            Assert.That(
                serviceView.SelectedDrinkId,
                Is.EqualTo(controller.SelectedOffer.DrinkId));

            controller.AdvancePresentation(
                BarDrinkServiceTimeline.CameraApproachDurationSeconds);

            Assert.That(controller.IsBrowsing, Is.True);
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.Browsing));
            Assert.That(controller.FirstPersonArms.IsVisible, Is.True);
            Assert.That(visibleSceneMarker.enabled, Is.False);
            Assert.That(hiddenSceneMarker.enabled, Is.False);
            AssertPlayerVisualHidden();
            Assert.That(
                Vector3.Distance(
                    cameraFollow.FixedBasePosition,
                    serviceView.transform.TransformPoint(
                        servicePlan.CameraPosition)),
                Is.LessThan(0.001f));
            Assert.That(
                cameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(servicePlan.CameraFieldOfView)
                    .Within(0.001f));

            controller.Exit();

            Assert.That(controller.IsOpen, Is.True);
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.CameraReturn));
            Assert.That(controller.FirstPersonArms.IsVisible, Is.True);
            AssertPlayerVisualRestored();
            Assert.That(player.Motor.InputEnabled, Is.False);
            Assert.That(player.Interactor.InputEnabled, Is.False);

            controller.AdvancePresentation(
                BarDrinkServiceTimeline.CameraReturnDurationSeconds);

            AssertPhysicalPresentationClosed();
            Assert.That(
                CinematicDepthOfField.IsActive,
                Is.False,
                "Closing the counter shot must release the bokeh.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            ConfirmedPurchase_DrinksReturnsToBrowsingAndRepeatsOnce()
        {
            int offerIndex = FindOfferIndex(DrinkId.RedWine);
            BarDrinkOffer offer = controller.Offers[offerIndex];
            Assert.That(
                serviceView.TryGetBottle(
                    offer.DrinkId,
                    out BarDrinkBottleView bottle),
                Is.True);
            Transform originalParent = bottle.transform.parent;
            Vector3 originalLocalPosition = bottle.transform.localPosition;
            Quaternion originalLocalRotation = bottle.transform.localRotation;
            Vector3 originalLocalScale = bottle.transform.localScale;
            int cashBefore = GameSessionState.CashBalance;
            int intoxicationBefore = GameSessionState.IntoxicationLevel;
            int drinksBefore = GameSessionState.DrinksConsumed;

            OpenBrowsing();
            Assert.That(controller.Select(offerIndex), Is.True);
            Transform shelfParent = bottle.transform.parent;
            Vector3 shelfLocalPosition = bottle.transform.localPosition;
            Quaternion shelfLocalRotation = bottle.transform.localRotation;
            Vector3 shelfLocalScale = bottle.transform.localScale;
            Assert.That(controller.ConfirmSelection(), Is.True);

            Assert.That(controller.IsOpen, Is.True);
            Assert.That(controller.IsServing, Is.True);
            Assert.That(controller.PurchaseCommitted, Is.True);
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.BottlePickup));
            Assert.That(controller.ConfirmSelection(), Is.False);
            Assert.That(bottle.transform.parent, Is.SameAs(shelfParent));
            Assert.That(bottle.transform.localPosition,
                Is.EqualTo(shelfLocalPosition));
            Assert.That(
                Quaternion.Angle(
                    bottle.transform.localRotation,
                    shelfLocalRotation),
                Is.LessThan(0.001f));
            Assert.That(bottle.transform.localScale,
                Is.EqualTo(shelfLocalScale));
            Assert.That(serviceView.IsCarriedBottleVisible, Is.True);
            for (int rendererIndex = 0;
                 rendererIndex < bottle.Renderers.Count;
                 rendererIndex++)
            {
                Assert.That(
                    bottle.Renderers[rendererIndex].enabled,
                    Is.False,
                    "The shelf source stays fixed and hidden during carry.");
            }
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(cashBefore - offer.Price));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksBefore + 1));

            BarDrinkServicePhase phaseBeforeCancel = controller.Phase;
            controller.Cancel();
            Assert.That(controller.IsOpen, Is.True);
            Assert.That(controller.Phase, Is.EqualTo(phaseBeforeCancel));
            Assert.That(controller.IsServing, Is.True);
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksBefore + 1));

            controller.AdvancePresentation(
                BarDrinkServiceTimeline.BottlePickupDurationSeconds +
                BarDrinkServiceTimeline.VesselPlacementDurationSeconds +
                BarDrinkServiceTimeline.PouringDurationSeconds * 0.5f);

            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.Pouring));
            Assert.That(bottle.SolidCollider.enabled, Is.True);
            Assert.That(bottle.SelectionTrigger.enabled, Is.True);
            Assert.That(bottle.Body.isKinematic, Is.True);
            Assert.That(serviceView.IsCarriedBottleVisible, Is.True);
            Assert.That(
                serviceView.CarriedBottleRoot.GetComponentsInChildren<
                    Collider>(true),
                Is.Empty);
            Assert.That(
                serviceView.CarriedBottleRoot.GetComponentsInChildren<
                    Rigidbody>(true),
                Is.Empty);
            Assert.That(serviceView.ActiveVessel, Is.Not.Null);
            Assert.That(
                serviceView.ActiveVessel.Kind,
                Is.EqualTo(BarDrinkVesselKind.WineGlass));
            Assert.That(serviceView.ActiveVessel.gameObject.activeSelf, Is.True);
            Assert.That(
                serviceView.ActiveVessel.FillProgress,
                Is.InRange(0.01f, 0.99f));
            Assert.That(
                serviceView.ActiveVessel.DisplayedFill,
                Is.EqualTo(
                    BarDrinkPresentationCatalog
                        .Get(offer.DrinkId).TargetFill *
                    serviceView.ActiveVessel.FillProgress)
                    .Within(0.0001f));
            Assert.That(serviceView.IsStreamVisible, Is.True);
            Assert.That(
                serviceView.StreamRoot.localScale.y,
                Is.GreaterThan(0.0025f));
            Assert.That(controller.FirstPersonArms.IsVisible, Is.True);
            AssertPlayerVisualHidden();

            controller.Cancel();
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.Pouring));
            Assert.That(serviceView.IsStreamVisible, Is.True);
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(cashBefore - offer.Price));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksBefore + 1));

            controller.AdvancePresentation(
                BarDrinkServiceTimeline.PouringDurationSeconds * 0.5f +
                BarDrinkServiceTimeline.BottleReturnDurationSeconds +
                0.001f);

            Assert.That(
                BarDrinkServiceTimeline.DrinkingDurationSeconds,
                Is.EqualTo(3f));
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.Drinking));
            Assert.That(controller.IsServing, Is.True);
            Assert.That(controller.PurchaseCommitted, Is.True);
            Assert.That(serviceView.IsCarriedBottleVisible, Is.True);
            Assert.That(serviceView.ActiveVessel, Is.Not.Null);
            Assert.That(serviceView.ActiveVessel.gameObject.activeSelf, Is.True);
            Assert.That(controller.FirstPersonArms.IsVisible, Is.True);
            Assert.That(cameraFollow.FixedPoseActive, Is.True);
            Assert.That(visibleSceneMarker.enabled, Is.False);
            Assert.That(hiddenSceneMarker.enabled, Is.False);
            AssertPlayerVisualHidden();

            float drinkingElapsed =
                controller.Timeline.PhaseElapsedSeconds;
            controller.AdvancePresentation(
                BarDrinkServiceTimeline.DrinkingDurationSeconds -
                drinkingElapsed -
                0.001f);
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.Drinking),
                "Drinking must hold for the complete three seconds.");
            Assert.That(controller.Timeline.CurrentFrame.DrinkLift, Is.EqualTo(1f));

            controller.AdvancePresentation(0.002f);
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.VesselReturn));
            Assert.That(serviceView.ActiveVessel, Is.Not.Null);
            Assert.That(serviceView.ActiveVessel.FillProgress, Is.Zero);
            Assert.That(controller.FirstPersonArms.IsVisible, Is.True);
            Assert.That(cameraFollow.FixedPoseActive, Is.True);

            BarDrinkVesselView returningVessel = serviceView.ActiveVessel;
            Vector3 vesselCounterPosition =
                serviceView.transform.TransformPoint(
                    servicePlan.VesselCounterPose.Position);
            float returnStartDistance = Vector3.Distance(
                returningVessel.transform.position,
                vesselCounterPosition);
            Assert.That(returnStartDistance, Is.GreaterThan(0.05f));

            controller.AdvancePresentation(
                BarDrinkServiceTimeline.VesselReturnDurationSeconds * 0.5f);
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.VesselReturn));
            Assert.That(
                controller.Timeline.CurrentFrame.DrinkLift,
                Is.InRange(0.01f, 0.99f));
            Assert.That(serviceView.ActiveVessel.gameObject.activeSelf, Is.True);
            Assert.That(
                Vector3.Distance(
                    returningVessel.transform.position,
                    vesselCounterPosition),
                Is.LessThan(returnStartDistance));

            float vesselReturnElapsed =
                controller.Timeline.PhaseElapsedSeconds;
            controller.AdvancePresentation(
                BarDrinkServiceTimeline.VesselReturnDurationSeconds * 0.75f -
                vesselReturnElapsed);
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.VesselReturn));
            Assert.That(
                Vector3.Distance(
                    returningVessel.transform.position,
                    vesselCounterPosition),
                Is.LessThan(returnStartDistance * 0.2f));

            controller.AdvancePresentation(
                BarDrinkServiceTimeline.VesselReturnDurationSeconds * 0.25f +
                0.002f);

            AssertPersistentBrowsing(bottle);
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(cashBefore - offer.Price));
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(
                    intoxicationBefore +
                    DrinkRules.GetIntoxicationGain(offer.DrinkId)));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksBefore + 1));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(offer.DrinkId));

            int cashAfterFirst = GameSessionState.CashBalance;
            int intoxicationAfterFirst = GameSessionState.IntoxicationLevel;
            int drinksAfterFirst = GameSessionState.DrinksConsumed;
            Assert.That(controller.ConfirmSelection(), Is.True);
            Assert.That(controller.ConfirmSelection(), Is.False);
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(cashAfterFirst - offer.Price));
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(
                    intoxicationAfterFirst +
                    DrinkRules.GetIntoxicationGain(offer.DrinkId)));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksAfterFirst + 1));

            controller.AdvancePresentation(
                BarDrinkServiceTimeline.ConfirmedPresentationDurationSeconds +
                0.01f);
            AssertPersistentBrowsing(bottle);
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(cashBefore - offer.Price * 2));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksBefore + 2));

            controller.Exit();
            Assert.That(controller.IsOpen, Is.True);
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.CameraReturn));
            Assert.That(player.Motor.InputEnabled, Is.False);
            Assert.That(player.Interactor.InputEnabled, Is.False);
            Assert.That(visibleSceneMarker.enabled, Is.False);
            Assert.That(hiddenSceneMarker.enabled, Is.False);
            controller.AdvancePresentation(
                BarDrinkServiceTimeline.CameraReturnDurationSeconds + 0.01f);

            AssertPhysicalPresentationClosed();
            AssertBottleRestored(
                bottle,
                originalParent,
                originalLocalPosition,
                originalLocalRotation,
                originalLocalScale);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BottleGripClamp_LeavesNearTargetsAndBoundsFarTargets()
        {
            Vector3 shoulder = new Vector3(-0.2f, 1.8f, 6.4f);
            const float reach = 0.625f;
            Vector3 near = shoulder + new Vector3(0.1f, -0.1f, -0.2f);
            Vector3 far = shoulder + new Vector3(1f, -1f, -2f);

            Assert.That(
                BarDrinkShopController.ClampBottleGripToReach(
                    shoulder,
                    reach,
                    near),
                Is.EqualTo(near));
            Vector3 clamped =
                BarDrinkShopController.ClampBottleGripToReach(
                    shoulder,
                    reach,
                    far);
            Assert.That(
                Vector3.Distance(shoulder, clamped),
                Is.EqualTo(reach).Within(0.0001f));
            Assert.That(
                Vector3.Dot(
                    (far - shoulder).normalized,
                    (clamped - shoulder).normalized),
                Is.GreaterThan(0.9999f));

            yield return null;
        }

        [UnityTest]
        public IEnumerator
            ConfiguredBottleCarrier_UsesReachSafeSurfaceContact()
        {
            var holderObject = new GameObject("Bottle Holder Root");
            holderObject.transform.SetParent(worldObject.transform, false);
            var shoulderObject = new GameObject("Right Shoulder");
            shoulderObject.transform.SetParent(holderObject.transform, false);
            shoulderObject.transform.position =
                new Vector3(-0.55f, 1.84f, 6.50f);
            var elbowObject = new GameObject("Right Elbow");
            elbowObject.transform.SetParent(holderObject.transform, false);
            elbowObject.transform.position =
                shoulderObject.transform.position +
                new Vector3(0.22f, -0.08f, -0.08f);
            var wristObject = new GameObject("Right Wrist");
            wristObject.transform.SetParent(holderObject.transform, false);
            wristObject.transform.position =
                elbowObject.transform.position +
                new Vector3(0.20f, -0.08f, -0.10f);
            var gripObject = new GameObject("Right Bottle Grip");
            gripObject.transform.SetParent(holderObject.transform, false);
            gripObject.transform.position =
                wristObject.transform.position + Vector3.down * 0.08f;

            controller.ConfigureBottleCarrier(gripObject.transform);
            controller.ConfigureBottleReachChain(
                holderObject.transform,
                shoulderObject.transform,
                elbowObject.transform,
                wristObject.transform,
                gripObject.transform);
            OpenBrowsing();
            Assert.That(
                controller.Select(FindOfferIndex(DrinkId.RedWine)),
                Is.True);
            Assert.That(controller.ConfirmSelection(), Is.True);
            controller.AdvancePresentation(
                BarDrinkServiceTimeline.BottlePickupDurationSeconds +
                BarDrinkServiceTimeline.VesselPlacementDurationSeconds +
                BarDrinkServiceTimeline.PouringDurationSeconds * 0.5f);

            Assert.That(
                serviceView.CarriedBottleRoot.parent,
                Is.SameAs(serviceView.transform));
            Assert.That(controller.BottleGripError, Is.LessThan(0.0001f));
            Assert.That(
                controller.BottleHandRadialClearance,
                Is.GreaterThanOrEqualTo(
                    BarDrinkServiceView.MinimumBottleHandRadialClearance));
            Assert.That(
                Vector3.Distance(
                    shoulderObject.transform.position,
                    controller.ActiveBottleHandTarget),
                Is.LessThanOrEqualTo(
                    controller.BottleGripReachLimit + 0.0001f));
            Assert.That(
                controller.ActiveBottleReachCorrection.magnitude,
                Is.GreaterThan(0.05f),
                "The authored counter pour requires the reachable grip " +
                "choice instead of stretching this physical arm.");

            yield return null;
        }

        [UnityTest]
        public IEnumerator
            DisableDuringCommittedPour_CleansPresentationWithoutRefund()
        {
            int offerIndex = FindOfferIndex(DrinkId.Vodka);
            BarDrinkOffer offer = controller.Offers[offerIndex];
            Assert.That(
                serviceView.TryGetBottle(
                    offer.DrinkId,
                    out BarDrinkBottleView bottle),
                Is.True);
            Transform originalParent = bottle.transform.parent;
            Vector3 originalLocalPosition = bottle.transform.localPosition;
            Quaternion originalLocalRotation = bottle.transform.localRotation;
            Vector3 originalLocalScale = bottle.transform.localScale;
            int cashBefore = GameSessionState.CashBalance;
            int drinksBefore = GameSessionState.DrinksConsumed;

            OpenBrowsing();
            controller.Select(offerIndex);
            Assert.That(controller.ConfirmSelection(), Is.True);
            controller.AdvancePresentation(
                BarDrinkServiceTimeline.BottlePickupDurationSeconds +
                BarDrinkServiceTimeline.VesselPlacementDurationSeconds +
                BarDrinkServiceTimeline.PouringDurationSeconds * 0.5f);
            Assert.That(serviceView.IsStreamVisible, Is.True);
            Assert.That(controller.PurchaseCommitted, Is.True);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.True);

            controller.enabled = false;

            AssertPhysicalPresentationClosed();
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(cashBefore - offer.Price));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksBefore + 1));
            Assert.That(controller.ConfirmSelection(), Is.False);
            AssertBottleRestored(
                bottle,
                originalParent,
                originalLocalPosition,
                originalLocalRotation,
                originalLocalScale);
            yield return null;
        }

        private void OpenBrowsing()
        {
            Assert.That(controller.Open(player.Interactor), Is.True);
            controller.AdvancePresentation(
                BarDrinkServiceTimeline.CameraApproachDurationSeconds);
            Assert.That(controller.IsBrowsing, Is.True);
        }

        private int FindOfferIndex(DrinkId drinkId)
        {
            for (int index = 0; index < controller.Offers.Count; index++)
            {
                if (controller.Offers[index].DrinkId == drinkId)
                {
                    return index;
                }
            }

            Assert.Fail($"Missing retail offer for {drinkId}.");
            return -1;
        }

        private void AssertPhysicalPresentationClosed()
        {
            Assert.That(controller.IsOpen, Is.False);
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.Closed));
            Assert.That(controller.IsServing, Is.False);
            Assert.That(controller.PurchaseCommitted, Is.False);
            Assert.That(controller.FirstPersonArms.IsVisible, Is.False);
            Assert.That(controller.FirstPersonArms.VisibilityAmount, Is.Zero);
            Assert.That(serviceView.SelectedBottle, Is.Null);
            Assert.That(serviceView.ActiveVessel, Is.Null);
            Assert.That(serviceView.IsCarriedBottleVisible, Is.False);
            Assert.That(serviceView.CarriedBottleRoot, Is.Null);
            Assert.That(serviceView.IsStreamVisible, Is.False);
            for (int index = 0; index < serviceView.Vessels.Count; index++)
            {
                Assert.That(
                    serviceView.Vessels[index].gameObject.activeSelf,
                    Is.False);
                Assert.That(serviceView.Vessels[index].FillProgress, Is.Zero);
            }

            Assert.That(player.Motor.InputEnabled, Is.True);
            Assert.That(player.Interactor.InputEnabled, Is.True);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(
                cameraFollow.CinematicMotionEnabled,
                Is.True);
            Assert.That(cameraFollow.FixedPoseActive, Is.False);
            Assert.That(hud.Visible, Is.True);
            Assert.That(visibleSceneMarker.enabled, Is.True);
            Assert.That(hiddenSceneMarker.enabled, Is.False);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
            AssertPlayerVisualRestored();
        }

        private void AssertPersistentBrowsing(BarDrinkBottleView bottle)
        {
            Assert.That(controller.IsOpen, Is.True);
            Assert.That(controller.IsBrowsing, Is.True);
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.Browsing));
            Assert.That(controller.IsServing, Is.False);
            Assert.That(controller.PurchaseCommitted, Is.False);
            Assert.That(controller.FirstPersonArms.IsVisible, Is.True);
            Assert.That(cameraFollow.FixedPoseActive, Is.True);
            Assert.That(player.Motor.InputEnabled, Is.False);
            Assert.That(player.Interactor.InputEnabled, Is.False);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.False);
            Assert.That(hud.Visible, Is.False);
            Assert.That(visibleSceneMarker.enabled, Is.False);
            Assert.That(hiddenSceneMarker.enabled, Is.False);
            Assert.That(serviceView.ActiveVessel, Is.Null);
            Assert.That(serviceView.IsCarriedBottleVisible, Is.False);
            Assert.That(serviceView.CarriedBottleRoot, Is.Null);
            Assert.That(serviceView.IsStreamVisible, Is.False);
            Assert.That(serviceView.SelectedBottle, Is.SameAs(bottle));
            Assert.That(bottle.SolidCollider.enabled, Is.True);
            Assert.That(bottle.SelectionTrigger.enabled, Is.True);
            for (int index = 0; index < bottle.Renderers.Count; index++)
            {
                Assert.That(bottle.Renderers[index].enabled, Is.True);
            }
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.True);
            AssertPlayerVisualHidden();
        }

        private void AssertPlayerVisualHidden()
        {
            IReadOnlyList<Renderer> renderers =
                player.Visual.Renderers;
            for (int index = 0; index < renderers.Count; index++)
            {
                Assert.That(renderers[index].enabled, Is.False);
            }

            Assert.That(
                player.PresentationVisibility.RenderersHidden,
                Is.True);
            Assert.That(
                player.PresentationVisibility.ShadowsHidden,
                Is.True);
            Assert.That(player.ContactShadow.enabled, Is.False);
        }

        private void AssertPlayerVisualRestored()
        {
            IReadOnlyList<Renderer> renderers =
                player.Visual.Renderers;
            // Count, not Has.Count: the constraint reflects for a public
            // "Count" property on the RUNTIME type, and this list is backed
            // by a Renderer[], which offers Length. The interface says
            // IReadOnlyList either way, so the failure was an ArgumentException
            // out of NUnit rather than an assertion about the game.
            Assert.That(
                renderers.Count,
                Is.EqualTo(initialRendererStates.Length));
            for (int index = 0; index < renderers.Count; index++)
            {
                Assert.That(
                    renderers[index].enabled,
                    Is.EqualTo(initialRendererStates[index]));
            }

            Assert.That(
                player.PresentationVisibility.IsHidden,
                Is.False);
            Assert.That(
                player.ContactShadow.enabled,
                Is.EqualTo(initialContactShadowState));
        }

        private static void AssertBottleRestored(
            BarDrinkBottleView bottle,
            Transform originalParent,
            Vector3 originalLocalPosition,
            Quaternion originalLocalRotation,
            Vector3 originalLocalScale)
        {
            Assert.That(bottle.transform.parent, Is.SameAs(originalParent));
            Assert.That(
                bottle.transform.localPosition,
                Is.EqualTo(originalLocalPosition));
            Assert.That(
                Quaternion.Angle(
                    bottle.transform.localRotation,
                    originalLocalRotation),
                Is.LessThan(0.01f));
            Assert.That(
                bottle.transform.localScale,
                Is.EqualTo(originalLocalScale));
            Assert.That(bottle.SolidCollider.enabled, Is.True);
            Assert.That(bottle.SelectionTrigger.enabled, Is.True);
            Assert.That(bottle.SelectionTrigger.isTrigger, Is.True);
            Assert.That(bottle.Body.isKinematic, Is.True);
            Assert.That(bottle.Body.useGravity, Is.False);
            Assert.That(bottle.Body.detectCollisions, Is.True);
        }

        private static void CloseExistingModalOwners()
        {
            foreach (BarDrinkShopController shop in
                     Object.FindObjectsByType<BarDrinkShopController>(
                         FindObjectsInactive.Include))
            {
                shop.Close();
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

        private Renderer CreateSceneMarker(string markerName, bool enabled)
        {
            var markerObject = new GameObject(markerName);
            markerObject.transform.SetParent(worldObject.transform, false);
            MeshRenderer marker = markerObject.AddComponent<MeshRenderer>();
            marker.enabled = enabled;
            return marker;
        }

        private static void DestroyObject(GameObject value)
        {
            if (value != null)
            {
                Object.Destroy(value);
            }
        }
    }

    /// <summary>
    /// Keeps an explicit test gaze after the seated view applies its own
    /// LateUpdate pose. Runtime input normally supplies this last look offset;
    /// batchmode has neither a rendered end-of-frame yield nor a physical
    /// mouse, so focused gaze contracts use this test-only equivalent.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    internal sealed class BarMenuTestCameraAimDriver : MonoBehaviour
    {
        public Vector3 Target { get; set; }
        public bool LookAway { get; set; }
        public bool IsAiming { get; set; }

        private void LateUpdate()
        {
            if (!IsAiming)
            {
                return;
            }

            Vector3 towardTarget = Target - transform.position;
            Vector3 direction = LookAway ? -towardTarget : towardTarget;
            if (direction.sqrMagnitude > 0.000001f)
            {
                transform.rotation = Quaternion.LookRotation(
                    direction,
                    Vector3.up);
            }
        }
    }
}
