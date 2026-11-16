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
            GameSessionState.BeginNewGame();
            ResetSession();
            BarTestTime.StartOpen();

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
            yield return VerifyMenuOrderMouseLook(station);
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
            // Check the authored exit before this floorless fixture gives
            // ordinary motor gravity another frame during menu retrieval.
            Assert.That(
                player.GameObject.transform.position,
                Is.EqualTo(seatPlan.ExitPose.RootPosition)
                    .Using(new UnityEngine.TestTools.Utils
                        .Vector3EqualityComparer(0.01f)));
            Assert.That(
                controller.MenuState,
                Is.EqualTo(
                    BarPromenade.Runtime.World.CounterMenuState.Retrieving));
            // No bartender choreography exists in this isolated fixture;
            // report its arrival before advancing the physical retrieval clock.
            controller.ReportCounterServerAtTarget(true);
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
        }

        private IEnumerator VerifyMenuOrderMouseLook(BarCounterStation station)
        {
            // This isolated service fixture omits the room. Supply the real
            // planner's countertop footprint/height for its physical hand contact.
            BarInteriorLayoutPlan layout = BarInteriorLayoutPlanner.Generate(
                20260731, "physical-drink-shop-test", BarActivityKind.Cocktail);
            var countertop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            countertop.name = "Test menu order counter surface";
            countertop.transform.SetParent(worldObject.transform, false);
            countertop.transform.localPosition = layout.CounterPosition +
                Vector3.up * (layout.CounterSize.y * .5f + .08f);
            countertop.transform.localScale = new Vector3(
                layout.CounterSize.x + .45f, .16f, layout.CounterSize.z + .32f);
            Object.Destroy(countertop.GetComponent<Collider>());
            serviceView.ConfigureCounterSurface(countertop.GetComponent<Renderer>());
            CounterSeatView view = station.SeatView;
            float deadline = Time.realtimeSinceStartup + 3f;
            while ((!controller.IsBrowsing || !view.IsMenuFocusComplete ||
                    controller.MenuPresentation.Page.FoldAmount > .001f) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(controller.IsBrowsing && view.IsMenuFocusComplete, Is.True,
                "Confirm the real open menu before checking its order gesture.");

            inputFixture = new InputTestFixture();
            inputFixture.Setup();
            var mouse = InputSystem.AddDevice<Mouse>("Test menu order mouse look");
            bool previousFocus = GameCursor.ApplicationFocused;
            bool previousBackground = Application.runInBackground;
            InputSettings settings = InputSystem.settings;
            InputSettings.BackgroundBehavior previousBackgroundBehavior = settings.backgroundBehavior;
            InputSettings.EditorInputBehaviorInPlayMode previousEditorInput = settings.editorInputBehaviorInPlayMode;
            float previousTimeScale = Time.timeScale;
            void OwnMouse()
            {
                mouse.MakeCurrent();
                GameCursor.SetApplicationFocused(true);
            }
            try
            {
                Application.runInBackground = true;
                settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                settings.editorInputBehaviorInPlayMode =
                    InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.onAfterUpdate += OwnMouse;
                OwnMouse();
                Assert.That(controller.Select(FindOfferIndex(DrinkId.LightBeer)), Is.True);
                int cashBefore = GameSessionState.CashBalance;
                Assert.That(controller.ConfirmSelection(), Is.True);
                deadline = Time.realtimeSinceStartup + 3f;
                while (!controller.RepeatGesture.IsActive && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(controller.RepeatGesture.IsActive, Is.True,
                    "Menu confirmation must start the shared attention/tapping gesture. " +
                    controller.RepeatGesture.LastBeginDiagnostics);
                Assert.That(view.IsActionLimitedLookEnabled && cameraFollow.OrbitInputEnabled, Is.True);
                Assert.That(view.IsMenuFocusLocked, Is.False);
                Assert.That(GameCursor.GameplayMouseInputAllowed, Is.True,
                    "The order gesture must expose ordinary captured mouse look.");
                Time.timeScale = .1f;
                // Let the shared cursor handoff discard its stale mouse sample.
                yield return null;
                yield return null;
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.BarInterior,
                    "Test menu order mouse look start");
                float startYaw = view.ViewYaw, startPitch = view.ViewPitch;
                Quaternion initialRotation = camera.transform.rotation;
                inputFixture.Set(mouse.delta, new Vector2(160f, 0f), queueEventOnly: true);
                yield return null;
                yield return null;
                Assert.That(view.ActionLookYaw, Is.GreaterThan(20f),
                    "A normal mouse sweep must pass the former twelve-degree order limit.");
                Assert.That(Quaternion.Angle(initialRotation, camera.transform.rotation), Is.GreaterThan(20f),
                    "The rendered eye camera must follow the mouse, not just update look state.");
                inputFixture.Set(mouse.delta, new Vector2(10000f, 0f), queueEventOnly: true);
                yield return null;
                yield return null;
                Assert.That(startYaw + view.ActionLookYaw,
                    Is.EqualTo(CounterSeatView.MaximumYawOffsetDegrees).Within(.01f));
                inputFixture.Set(mouse.delta, new Vector2(-20000f, 0f), queueEventOnly: true);
                yield return null;
                yield return null;
                Assert.That(startYaw + view.ActionLookYaw,
                    Is.EqualTo(-CounterSeatView.MaximumYawOffsetDegrees).Within(.01f));
                inputFixture.Set(mouse.delta, new Vector2(0f, 10000f), queueEventOnly: true);
                yield return null;
                yield return null;
                Assert.That(startPitch + view.ActionLookPitch,
                    Is.EqualTo(CounterSeatView.MinimumPitchDegrees).Within(.01f));
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.BarInterior,
                    "Test menu order mouse look wide pan");
                inputFixture.Set(mouse.delta, new Vector2(0f, -20000f), queueEventOnly: true);
                yield return null;
                yield return null;
                Assert.That(startPitch + view.ActionLookPitch,
                    Is.EqualTo(CounterSeatView.MaximumPitchDegrees).Within(.01f));
                Assert.That(controller.RepeatGesture.IsActive, Is.True);
                float finalYaw = startYaw + view.ActionLookYaw;
                float finalPitch = startPitch + view.ActionLookPitch;
                controller.RepeatGesture.Cancel();
                yield return null;
                Assert.That(view.IsActionLookLocked, Is.False);
                Assert.That(view.ViewYaw, Is.EqualTo(finalYaw).Within(.01f));
                Assert.That(view.ViewPitch, Is.EqualTo(finalPitch).Within(.01f));
                Assert.That(controller.IsOrderRequestPending, Is.False);
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(cashBefore),
                    "Cancelling this unfinished camera check must not accept or charge an order.");
                Assert.That(controller.MenuState,
                    Is.EqualTo(BarPromenade.Runtime.World.CounterMenuState.Resting));
            }
            finally
            {
                Time.timeScale = previousTimeScale;
                InputSystem.onAfterUpdate -= OwnMouse;
                if (mouse.added) InputSystem.RemoveDevice(mouse);
                settings.backgroundBehavior = previousBackgroundBehavior;
                settings.editorInputBehaviorInPlayMode = previousEditorInput;
                Application.runInBackground = previousBackground;
                GameCursor.SetApplicationFocused(previousFocus);
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
                Vector3.Distance(serviceView.CarriedBottleHandContactWorldPosition,
                    serviceView.CarriedBottleGripCenterWorldPosition),
                Is.LessThan(0.0001f),
                "The reachable hand target must surround the actual bottle " +
                "axis instead of preserving the former radial socket gap.");
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
