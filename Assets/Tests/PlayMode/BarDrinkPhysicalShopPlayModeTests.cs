using System.Collections;
using System.Collections.Generic;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
            GraphicsEffectsSettings.DepthOfFieldEnabled =
                previousDepthOfFieldEnabled;
            DestroyObject(uiObject);
            if (player.GameObject != null)
            {
                Object.Destroy(player.GameObject);
            }

            DestroyObject(cameraObject);
            DestroyObject(worldObject);
            ResetSession();
            yield return null;
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
            Assert.That(controller.ConfirmSelection(), Is.True);

            Assert.That(controller.IsOpen, Is.True);
            Assert.That(controller.IsServing, Is.True);
            Assert.That(controller.PurchaseCommitted, Is.True);
            Assert.That(
                controller.Phase,
                Is.EqualTo(BarDrinkServicePhase.BottlePickup));
            Assert.That(controller.ConfirmSelection(), Is.False);
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
            Assert.That(bottle.SolidCollider.enabled, Is.False);
            Assert.That(bottle.SelectionTrigger.enabled, Is.False);
            Assert.That(bottle.Body.isKinematic, Is.True);
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
            Assert.That(serviceView.IsStreamVisible, Is.False);
            Assert.That(serviceView.SelectedBottle, Is.SameAs(bottle));
            Assert.That(bottle.SolidCollider.enabled, Is.True);
            Assert.That(bottle.SelectionTrigger.enabled, Is.True);
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
}
