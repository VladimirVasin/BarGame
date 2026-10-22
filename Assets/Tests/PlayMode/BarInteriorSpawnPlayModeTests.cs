using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// Watches the first two seconds of the bar through the camera the
    /// player actually gets.
    ///
    /// This exists because the room passed every measurement it had -
    /// anchors, sizes, tints, renderer bounds, a folder of rendered
    /// frames - while the hero fell through its floor forever and the
    /// chase camera sat inside his skull. None of those checks could see
    /// it: `BarModelContractTests` measures geometry and never a
    /// collider, and `AreaCaptureFixture` photographs the room from
    /// invented camera poses with the hero's renderers switched off, so
    /// it can neither see him fall nor see where his camera ended up.
    ///
    /// So this fixture asserts the two things a player notices in his
    /// first second: he stands on the floor, and he can see himself.
    /// </summary>
    [PrebuildSetup(typeof(CompactBarAssetsSetup))]
    public sealed partial class BarInteriorSpawnPlayModeTests
    {
        private const float TimeoutSeconds = 20f;
        private float previousCaptureDeltaTime;

        //  Long enough to outlast the arrival shot (1.35 s), which owns
        //  the camera while it plays and hands it back to the follow.
        private const float SettleSeconds = 2f;

        //  The chase camera wants 1.9 m indoors and gives that up to
        //  geometry behind the hero. Under a metre it is inside him.
        private const float MinimumCameraDistance = 1f;

        //  Keep the seated lens visibly above the 1.02 m counter silhouette.
        //  This is deliberately smaller than ordinary seated eye clearance,
        //  leaving authoring room without letting the timber bury the view.
        private const float MinimumEyeAboveCounter = 0.18f;

        //  Below this cosine the page is less than 17.5 degrees above an
        //  edge-on view. Text may still own valid meshes and screen bounds,
        //  but its glyphs collapse into an unreadable strip.
        private const float MinimumReadablePageFacing = 0.72f;
        private const float MinimumOverheadSurfaceFacing = 0.97f;
        private const float MaximumCameraPlanarOffset = 0.14f;
        private const float MaximumWipePenetration = 0.015f;
        private const float MaximumWipeGap = 0.03f;
        private const float MinimumVisibleWipeTravel = 0.04f;
        private const float MaximumWipeReturnError = 0.04f;
        private const float WipeSurfaceMargin = 0.02f;

        //  Keep the physical booklet clear of the shared bottom hint. The
        //  hint occupies BottomMargin + Height logical pixels; four pixels of
        //  breathing room prevent the paper from reading as part of the HUD.
        private static readonly float MenuSafeViewportBottom =
            (CounterMenuHintView.BottomMargin +
             CounterMenuHintView.Height + 4f) /
            RetroUiTheme.LogicalHeight;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousCaptureDeltaTime = Time.captureDeltaTime;
            // Short compact-room routes still need samples through the full
            // contact interval, independent of cold shader/render stalls.
            Time.captureDeltaTime = 1f / 60f;
            GameSessionState.EnterBar("bar-spawn-test", BarActivityKind.SplitTheG,
                CityDistrictKind.Residential);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return AreaCaptureFixture.ReleaseBarService();
            Scene bar = SceneManager.GetSceneByName(SceneIds.BarInterior);
            if (bar.IsValid() && bar.isLoaded)
            {
                Scene cleanup = SceneManager.CreateScene(
                    "Bar Spawn Test Cleanup");
                SceneManager.SetActiveScene(cleanup);
                AsyncOperation unload = SceneManager.UnloadSceneAsync(bar);
                while (unload != null && !unload.isDone)
                {
                    yield return null;
                }
            }

            GameSessionState.ResetEconomyState();
            GameSessionState.ResetDrinkingState();
            Time.captureDeltaTime = previousCaptureDeltaTime;
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            ArrivingHero_KeepsHisFeetOnTheFloorAndHisCameraBehindHim()
        {
            BarInteriorRoot bar = null;
            yield return LoadBar(result => bar = result);

            Transform hero = bar.Player.GameObject.transform;
            float spawnHeight = bar.Layout.PlayerSpawn.y;

            float deadline = Time.realtimeSinceStartup + SettleSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                Assert.That(
                    hero.position.y,
                    Is.GreaterThan(spawnHeight - 0.5f),
                    $"the hero is falling through the bar's floor: he " +
                    $"spawned at y={spawnHeight:F2} and is now at " +
                    $"{hero.position}. The room's collision is not where " +
                    "its geometry is.");
                yield return null;
            }

            CharacterController controller =
                bar.Player.GameObject.GetComponent<CharacterController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(
                controller.isGrounded,
                Is.True,
                "the hero never found the floor of the bar");
        }

        [UnityTest]
        public IEnumerator ArrivingHero_CameraReturnsInsideEntranceAndClearOfHero()
        {
            BarInteriorRoot bar = null;
            yield return LoadBar(result => bar = result);

            float deadline = Time.realtimeSinceStartup + SettleSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null, "the bar has no main camera");
            Assert.That(
                bar.ArrivalPresentation.IsPlaying,
                Is.False,
                "the arrival shot never handed the camera back");

            Transform hero = bar.Player.GameObject.transform;
            Vector3 head = hero.position + Vector3.up * 1.3f;
            float distance = Vector3.Distance(
                camera.transform.position,
                head);
            Assert.That(
                distance,
                Is.GreaterThan(MinimumCameraDistance),
                $"the camera sits {distance:F2} m from the hero's head - " +
                "inside him. Its collision probe is starting inside a " +
                "collider, so the chase distance collapsed to nothing.");

            Vector3 cameraInRoom = bar.Room.InverseTransformPoint(
                camera.transform.position);
            float minimumInteriorCameraZ =
                bar.Layout.RoomBounds.yMin +
                bar.Layout.WallThickness * 0.5f +
                camera.nearClipPlane;
            Assert.That(
                cameraInRoom.z,
                Is.GreaterThan(minimumInteriorCameraZ),
                $"the camera sits behind the closed entrance door at " +
                $"room z={cameraInRoom.z:F2}; its lens must stay inside " +
                $"the door plane beyond z={minimumInteriorCameraZ:F2}.");
        }

        [UnityTest]
        public IEnumerator Bartender_WipeTouchesAndTravelsAcrossCounter()
        {
            BarInteriorRoot bar = null;
            yield return LoadBar(result => bar = result);
            bar.ArrivalPresentation.Skip();
            yield return null;

            BarBartenderPresentation bartender = bar.Bartender;
            Assert.That(bartender, Is.Not.Null);
            Assert.That(bartender.UsesOrdinaryRig, Is.True);
            Assert.That(
                bartender.CurrentClipKind,
                Is.EqualTo(BarBartenderClipKind.Wipe));
            Assert.That(
                bar.transform.InverseTransformPoint(
                    bartender.transform.position).y,
                Is.Zero.Within(0.001f),
                "The ordinary bartender must stand at the same ground level " +
                "for which CafeAttendantWipe was authored.");

            Transform counterTop = bar.Room.Find("Counter Top");
            Assert.That(counterTop, Is.Not.Null);
            Renderer counterRenderer = counterTop.GetComponent<Renderer>();
            Assert.That(counterRenderer, Is.Not.Null);

            CityPedestrianHandPropRegistry towel =
                bartender.Registry.ServiceTowel;
            Assert.That(towel, Is.Not.Null);
            Assert.That(towel.IsVisible, Is.True);
            Assert.That(
                bartender.Registry.TryGetClip(
                    BarBartenderClipKind.Wipe,
                    out AnimationClip wipeClip,
                    out bool wipeLoops),
                Is.True);
            Assert.That(wipeLoops, Is.True);
            AdvanceWipeToPhase(bartender, wipeClip.length, 0.12f);
            Vector3 first = MeasureWipeTowel(towel, counterRenderer.bounds);
            AdvanceWipeToPhase(bartender, wipeClip.length, 0.28f);
            Vector3 second = MeasureWipeTowel(towel, counterRenderer.bounds);
            AdvanceWipeToPhase(bartender, wipeClip.length, 0.44f);
            Vector3 third = MeasureWipeTowel(towel, counterRenderer.bounds);

            first.y = 0f;
            second.y = 0f;
            third.y = 0f;
            Assert.That(
                Vector3.Distance(first, second),
                Is.GreaterThanOrEqualTo(MinimumVisibleWipeTravel),
                "The first Wipe stroke is not visible across the top.");
            Assert.That(
                Vector3.Distance(second, third),
                Is.GreaterThanOrEqualTo(MinimumVisibleWipeTravel),
                "The return Wipe stroke is not visible across the top.");
            Assert.That(
                Vector3.Distance(first, third),
                Is.LessThanOrEqualTo(MaximumWipeReturnError),
                "CafeAttendantWipe does not return across the same patch.");
        }

        [UnityTest]
        public IEnumerator
            BeerService_BartenderWalksAndPlacesMugWithoutThrowingIt()
            => VerifyBeerService(verifyCompletionOnly: false);

        [UnityTest]
        public IEnumerator EmptyVesselCleanup_StepsBackAndBracesBeforeHiding()
            => VerifyBeerService(verifyCompletionOnly: true);

        private IEnumerator VerifyBeerService(bool verifyCompletionOnly)
        {
            GameSessionState.ResetEconomyState();
            GameSessionState.ResetDrinkingState();
            BarInteriorRoot bar = null;
            yield return LoadBar(result => bar = result);
            var towelHandoff = bar.gameObject.AddComponent<BarTowelHandoffProbe>();
            towelHandoff.Initialize(bar);
            bar.ArrivalPresentation.Skip();
            yield return new WaitForFixedUpdate();

            BarBartenderPresentation bartender = bar.Bartender;
            BarDrinkShopController shop = bar.DrinkShop;
            BarDrinkServiceView service = bar.DrinkServiceView;
            BarCounterStation station = bar.CounterStations[
                bar.CounterStations.Count - 1];
            Assert.That(bartender, Is.Not.Null);
            Assert.That(shop, Is.Not.Null);
            Assert.That(service, Is.Not.Null);
            Assert.That(station, Is.Not.Null);
            Assert.That(
                bartender.GetComponent<
                    BarBartenderServiceChoreography>(),
                Is.Not.Null.And.Property("IsInitialized").True,
                "The production bar must own the real choreography.");
            bartender.Registry.Animator.cullingMode =
                AnimatorCullingMode.AlwaysAnimate;

            Transform leftFoot = FindRequiredDescendant(
                bartender.Registry.ModelRoot,
                "foot.L");
            Transform rightFoot = FindRequiredDescendant(
                bartender.Registry.ModelRoot,
                "foot.R");

            CounterSeatPlan seatPlan = station.Seat.Plan;
            bar.Player.Motor.Teleport(seatPlan.EntryPose.RootPosition);
            bar.Player.GameObject.transform.rotation =
                seatPlan.EntryPose.RootRotation;
            Physics.SyncTransforms();
            station.Interact(bar.Player.Interactor);

            float timeout = Time.realtimeSinceStartup + 5f;
            while (!station.Seat.IsSeated &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(station.Seat.IsSeated, Is.True);
            Assert.That(shop.ActiveServiceMirrored, Is.True,
                "The production regression must exercise the mirrored " +
                "rightmost service route.");
            timeout = Time.realtimeSinceStartup + 6f;
            while (!shop.IsBrowsing &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return new WaitForFixedUpdate();
            }

            var towelTerminal = bar.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
            yield return AtFinalBarPose(towelTerminal, () =>
            {
                if (!shop.IsBrowsing) BarTowelHandoffProbe.Capture(bartender.TowelPresentation, "22-towel-terminal");
                Assert.That(shop.IsBrowsing, Is.True,
                    "The real bartender never completed menu delivery. " + DescribeTowelHandoff(bartender, shop));
            });
            Object.Destroy(towelTerminal);
            Assert.That(
                shop.Select(FindOfferIndex(shop, DrinkId.LightBeer)),
                Is.True);
            station.Interact(bar.Player.Interactor);
            Assert.That(
                shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.BeerWalkToTap));

            var travel = new BartenderTravelProbe(
                bartender,
                leftFoot,
                rightFoot, bar);
            timeout = Time.realtimeSinceStartup + 8f;
            while (shop.Phase ==
                       BarDrinkServicePhase.BeerWalkToTap &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return new WaitForFixedUpdate();
                travel.Observe("walk to beer tap");
            }

            Assert.That(
                shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.BeerGlassPickup),
                "The choreography never reported its real tap arrival.");
            timeout = Time.realtimeSinceStartup + 5f;
            while (shop.Phase !=
                       BarDrinkServicePhase.BeerCarryToGuest &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return new WaitForFixedUpdate();
                travel.Observe("beer pickup and pour");
            }

            Assert.That(
                shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.BeerCarryToGuest));
            BarDrinkVesselView vessel = service.ActiveVessel;
            Assert.That(vessel, Is.Not.Null);
            Pose counterPose = shop.ResolveBeerCounterWorldPose(vessel);
            int carriedFrames = 0;
            int carryTranslationFrames = 0;
            Vector3 carryStartPosition = bartender.transform.position;
            timeout = Time.realtimeSinceStartup + 8f;
            while (shop.Phase ==
                       BarDrinkServicePhase.BeerCarryToGuest &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return new WaitForFixedUpdate();
                bool translated = travel.Observe("carry beer to guest");
                if (shop.Phase !=
                    BarDrinkServicePhase.BeerCarryToGuest)
                {
                    break;
                }

                Assert.That(
                    service.IsBeerTapVesselCarriedByBartender,
                    Is.True,
                    "The bartender released the mug during guest travel.");
                carriedFrames++;
                if (translated)
                {
                    carryTranslationFrames++;
                }

                Assert.That(
                    service.ResolveActiveVesselGripError(
                        bartender.Registry.VesselGripAnchor),
                    Is.LessThan(0.01f),
                    "The walking hand released the mug before the counter.");
                Assert.That(
                    Vector3.Angle(
                        vessel.OpeningDirection,
                        service.transform.up),
                    Is.LessThan(2f),
                    "The filled mug rolled with the walking wrist.");
            }

            Assert.That(carriedFrames, Is.GreaterThan(2));
            Assert.That(carryTranslationFrames, Is.GreaterThan(2));
            Vector3 guestTravel =
                bartender.transform.position - carryStartPosition;
            guestTravel.y = 0f;
            Assert.That(
                guestTravel.magnitude,
                Is.GreaterThan(0.5f),
                "The mug route reached placement without a real approach " +
                "to the selected guest.");
            Assert.That(
                shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.BeerGlassPlacement),
                "The choreography never reported its real guest arrival.");

            Vector3 previousVesselPosition = vessel.transform.position;
            int placementContactFrames = 0;
            timeout = Time.realtimeSinceStartup + 3f;
            while (shop.Phase ==
                       BarDrinkServicePhase.BeerGlassPlacement &&
                   Time.realtimeSinceStartup < timeout)
            {
                // Compare the vessel and hand from the same completed
                // LateUpdate pose, before the next service Update moves it.
                yield return new WaitForFixedUpdate();
                travel.Observe("place beer on counter");
                if (shop.Phase !=
                    BarDrinkServicePhase.BeerGlassPlacement)
                {
                    break;
                }

                float counterDistance = Vector3.Distance(
                    vessel.transform.position,
                    counterPose.position);
                Assert.That(Vector3.Distance(vessel.transform.position, previousVesselPosition),
                    Is.LessThan(0.10f), $"The mug jumped during placement at {shop.Timeline.PhaseProgress:F3}; " +
                    $"grip={service.ResolveActiveVesselGripError(bartender.Registry.VesselGripAnchor):F4}, " +
                    $"from={previousVesselPosition:F3}, to={vessel.transform.position:F3}.");
                Assert.That(
                    Vector3.Angle(
                        vessel.OpeningDirection,
                        service.transform.up),
                    Is.LessThan(2f),
                    "The mug flipped while the bartender set it down.");
                if (counterDistance > 0.03f)
                {
                    placementContactFrames++;
                    Assert.That(
                        service.ResolveActiveVesselGripError(
                            bartender.Registry.VesselGripAnchor),
                        Is.LessThan(0.04f),
                        "The hand left the mug before it reached the top " +
                        $"at progress " +
                        $"{shop.Timeline.CurrentFrame.PhaseProgress:F2}. " +
                        DescribeBartenderReach(bartender, vessel));
                }

                previousVesselPosition = vessel.transform.position;
                AssertBartenderClearsCounter(bar);
            }

            Assert.That(
                shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.AwaitingDrink));
            Assert.That(
                Vector3.Distance(
                    vessel.transform.position,
                    counterPose.position),
                Is.LessThan(0.005f));
            Assert.That(
                Quaternion.Angle(
                    vessel.transform.rotation,
                    counterPose.rotation),
                Is.LessThan(1f),
                "The served mug did not finish upright on the counter.");
            Assert.That(placementContactFrames, Is.GreaterThan(2));
            AssertVesselRestsOnPhysicalCounter(bar, service, vessel);
            AssertBartenderClearsCounter(bar);
            Assert.That(
                travel.TranslationFrameCount,
                Is.GreaterThan(10),
                "The fixture never exercised real bartender travel.");
            Assert.That(
                travel.MaximumLocalFootTravel,
                Is.GreaterThan(0.05f),
                "Walk was selected, but the bartender's local foot pose " +
                "stayed static while his root translated.");
            Assert.That(travel.MatchedMovementFrames, Is.GreaterThan(5));
            AreaCaptureFixture.CaptureCurrentCamera(
                Camera.main, SceneIds.BarInterior, "11-served-beer");
            Camera.main.transform.rotation = Quaternion.LookRotation(
                vessel.GlassRenderer.bounds.center - Camera.main.transform.position, Vector3.up);
            shop.RefreshServedDrinkAffordance();
            Assert.That(shop.BeginServedDrink(), Is.True);
            if (verifyCompletionOnly)
                yield return VerifyHeroDrinkCompletion(bar, vessel);
            else
                yield return VerifyHeroDrinkContacts(bar, vessel, DrinkId.LightBeer);
            yield return VerifyEmptyVesselCleanup(bar, DrinkId.Vodka);
            towelHandoff.AssertComplete();
        }

        [UnityTest]
        public IEnumerator
            RedWineService_FetchesPoursCarriesAndWaitsForExplicitDrink()
            => VerifyBottleService(DrinkId.RedWine, BarDrinkVesselKind.WineGlass);

        [UnityTest]
        public IEnumerator CognacService_KeepsSnifterClearOfCounterAndMenu()
            => VerifyBottleService(DrinkId.CognacVs, BarDrinkVesselKind.Snifter);

        [UnityTest]
        public IEnumerator VodkaService_KeepsShotClearOfCounterAndMenu()
            => VerifyBottleService(DrinkId.Vodka, BarDrinkVesselKind.ShotGlass);

        private IEnumerator VerifyBottleService(DrinkId drinkId, BarDrinkVesselKind vesselKind,
            bool verifyCompletionOnly = false)
        {
            GameSessionState.ResetEconomyState();
            GameSessionState.ResetDrinkingState();
            GameSessionState.UpdateDrinkingProgress(80, DrinkId.None, 0);
            BarInteriorRoot bar = null;
            yield return LoadBar(result => bar = result);
            bar.ArrivalPresentation.Skip();
            yield return new WaitForFixedUpdate();

            BarBartenderPresentation bartender = bar.Bartender;
            BarDrinkShopController shop = bar.DrinkShop;
            BarDrinkServiceView service = bar.DrinkServiceView;
            BarCounterStation station = bar.CounterStations[
                bar.CounterStations.Count - 1];
            bartender.Registry.Animator.cullingMode =
                AnimatorCullingMode.AlwaysAnimate;
            Transform leftFoot = FindRequiredDescendant(
                bartender.Registry.ModelRoot, "foot.L");
            Transform rightFoot = FindRequiredDescendant(
                bartender.Registry.ModelRoot, "foot.R");
            Assert.That(
                service.TryGetBottle(
                    drinkId,
                    out BarDrinkBottleView shelfBottle),
                Is.True);
            Transform restingShelfParent = shelfBottle.transform.parent;
            Vector3 restingShelfPosition =
                shelfBottle.transform.localPosition;
            Quaternion restingShelfRotation =
                shelfBottle.transform.localRotation;
            Vector3 restingShelfScale = shelfBottle.transform.localScale;

            CounterSeatPlan seatPlan = station.Seat.Plan;
            bar.Player.Motor.Teleport(seatPlan.EntryPose.RootPosition);
            bar.Player.GameObject.transform.rotation =
                seatPlan.EntryPose.RootRotation;
            Physics.SyncTransforms();
            station.Interact(bar.Player.Interactor);
            float timeout = Time.realtimeSinceStartup + 5f;
            while (!station.Seat.IsSeated &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return new WaitForFixedUpdate();
            }

            timeout = Time.realtimeSinceStartup + 6f;
            while (!shop.IsBrowsing &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(shop.IsBrowsing, Is.True, DescribeTowelHandoff(bartender, shop));
            Assert.That(Time.timeScale, Is.InRange(0.94f, 0.96f),
                "The complete production route must retain hand contact in slow motion.");
            Assert.That(
                shop.Select(FindOfferIndex(shop, drinkId)),
                Is.True);
            Transform shelfParent = shelfBottle.transform.parent;
            Vector3 shelfPosition = shelfBottle.transform.localPosition;
            Quaternion shelfRotation = shelfBottle.transform.localRotation;
            Vector3 shelfScale = shelfBottle.transform.localScale;
            AssertBottleShelfState(
                shelfBottle,
                shelfParent,
                shelfPosition,
                shelfRotation,
                shelfScale,
                true);
            int cashBefore = GameSessionState.CashBalance;
            int intoxicationBefore = GameSessionState.IntoxicationLevel;
            int drinksBefore = GameSessionState.DrinksConsumed;
            station.Interact(bar.Player.Interactor);
            Assert.That(shop.SelectedOffer.DrinkId, Is.EqualTo(drinkId));
            Assert.That(shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.BottleWalkToShelf));
            for (int frame = 0; frame < 240 && !shop.IsBartenderServiceReady; frame++) yield return null;
            Assert.That(shop.IsBartenderServiceReady, Is.True, DescribeTowelHandoff(bartender, shop));
            Assert.That(service.SelectedBottle, Is.SameAs(shelfBottle));
            Assert.That(service.ActiveVessel.Kind,
                Is.EqualTo(vesselKind));
            Assert.That(service.ActiveVessel.gameObject.activeSelf, Is.False);
            Assert.That(service.IsCarriedBottleVisible, Is.False);
            Assert.That(GameSessionState.CashBalance,
                Is.EqualTo(cashBefore - shop.SelectedOffer.Price));
            Assert.That(GameSessionState.IntoxicationLevel,
                Is.EqualTo(intoxicationBefore));
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(drinksBefore));

            var travel = new BartenderTravelProbe(
                bartender, leftFoot, rightFoot, bar);
            int shelfTravelFrames = 0;
            timeout = Time.realtimeSinceStartup + 8f;
            while (shop.Phase ==
                       BarDrinkServicePhase.BottleWalkToShelf &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return new WaitForFixedUpdate();
                if (travel.Observe("walk to selected shelf"))
                {
                    shelfTravelFrames++;
                }

                if (shop.Phase ==
                    BarDrinkServicePhase.BottleWalkToShelf)
                {
                    AssertBottleShelfState(
                        shelfBottle,
                        shelfParent,
                        shelfPosition,
                        shelfRotation,
                        shelfScale,
                        true);
                    Assert.That(service.IsCarriedBottleVisible, Is.False);
                }
            }

            Assert.That(shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.BottlePickup));
            Assert.That(shelfTravelFrames, Is.GreaterThan(2));
            AssertBottleShelfState(
                shelfBottle,
                shelfParent,
                shelfPosition,
                shelfRotation,
                shelfScale,
                false);
            Assert.That(service.IsCarriedBottleVisible, Is.True);
            bool sawAttachedBottle = false;
            // Record the shelf position before the phase-changing Update can
            // already take the first step toward the nearby preparation point.
            Vector3 pourTravelStart = bartender.transform.position;
            float expectedPourTravel = Vector3.ProjectOnPlane(
                shop.ResolveBottlePreparationServerWorldPose().position -
                pourTravelStart, Vector3.up).magnitude;
            Vector3 previousBottlePosition =
                service.CarriedBottleRoot.position;
            timeout = Time.realtimeSinceStartup + 3f;
            while (shop.Phase == BarDrinkServicePhase.BottlePickup &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return new WaitForFixedUpdate();
                travel.Observe("pick up selected bottle");
                if (shop.Phase == BarDrinkServicePhase.BottlePickup &&
                    shop.IsBottleGripAttached)
                {
                    if (!sawAttachedBottle)
                    {
                        Assert.That(
                            Vector3.Distance(
                                previousBottlePosition,
                                service.CarriedBottleRoot.position),
                            Is.LessThan(0.04f),
                            "The carried copy must meet the hand at the shelf " +
                            "without a visible pickup jump.");
                    }

                    sawAttachedBottle = true;
                    Assert.That(shop.BottleGripError, Is.LessThan(0.015f));
                }

                previousBottlePosition =
                    service.CarriedBottleRoot.position;
            }

            Assert.That(sawAttachedBottle, Is.True);
            Assert.That(shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.BottleCarryToPour));
            int pourTravelFrames = 0;
            timeout = Time.realtimeSinceStartup + 8f;
            while (shop.Phase ==
                       BarDrinkServicePhase.BottleCarryToPour &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return new WaitForFixedUpdate();
                if (travel.Observe("carry selected bottle to pour point"))
                {
                    pourTravelFrames++;
                }

                if (shop.Phase ==
                    BarDrinkServicePhase.BottleCarryToPour)
                {
                    Assert.That(service.IsCarriedBottleVisible, Is.True);
                    Assert.That(shop.BottleGripError, Is.LessThan(0.015f));
                }
            }

            // The compact shelf-to-preparation route is a short real step;
            // sampled frame count depends on the rendering cadence.
            Assert.That(pourTravelFrames, Is.GreaterThan(0));
            Assert.That(expectedPourTravel, Is.GreaterThan(0.01f));
            Assert.That(
                Vector3.ProjectOnPlane(
                    bartender.transform.position - pourTravelStart,
                    Vector3.up).magnitude,
                Is.EqualTo(expectedPourTravel).Within(0.002f),
                "The bartender must carry the bottle from the shelf to " +
                "the preparation point with the full Walk clip.");
            Assert.That(shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.VesselPlacement));
            timeout = Time.realtimeSinceStartup + 4f;
            while ((shop.Phase == BarDrinkServicePhase.VesselPlacement ||
                    (shop.Phase == BarDrinkServicePhase.Pouring &&
                     shop.Timeline.PhaseProgress < 0.40f)) &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return new WaitForFixedUpdate();
                travel.Observe("place vessel and begin pour");
            }

            // The bottle is seated on the anatomical hand by a post-IK
            // LateUpdate pass. FixedUpdate resumes before the next service
            // Update, so this samples the pose that was actually rendered.
            yield return new WaitForFixedUpdate();
            BarDrinkVesselView vessel = service.ActiveVessel;
            Assert.That(shop.Phase, Is.EqualTo(BarDrinkServicePhase.Pouring));
            Assert.That(vessel.Kind, Is.EqualTo(vesselKind));
            Assert.That(vessel.FillProgress, Is.InRange(0.05f, 0.95f));
            Assert.That(service.IsStreamVisible, Is.True);
            var handProof = bar.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
            yield return AtFinalBarPose(handProof, () =>
            {
                CaptureBarServiceDetail(vessel.transform.position, $"13-pouring-{drinkId}");
                AssertHandWrap(bartender.Registry.HandPose, false,
                    service.CarriedBottleGripCenterWorldPosition,
                    shop.ActiveBottleWorldRotation * Vector3.up, "Bartender bottle");
                BarDrinkServiceWorldBuilderPlayModeTests.AssertBottleDoesNotIntersectHand(
                    bartender.Registry.HandPose, false, service.CarriedBottleRoot, "Bartender bottle");
                AssertHandWrap(bartender.Registry.HandPose, true,
                    vessel.GripWorldPosition, vessel.OpeningDirection, "Bartender vessel");
                BarDrinkServiceWorldBuilderPlayModeTests.AssertBottleDoesNotIntersectHand(
                    bartender.Registry.HandPose, true, vessel.transform, "Bartender vessel");
            });
            Object.Destroy(handProof);
            Vector3 pourDelta =
                service.CarriedBottleMouthWorldPosition -
                vessel.PourTargetWorldPosition;
            Assert.That(
                Vector3.ProjectOnPlane(
                    pourDelta,
                    service.transform.up).magnitude,
                Is.LessThan(0.025f),
                "The selected bottle mouth must be directly above the " +
                "matching vessel, not connected by a diagonal stream. " +
                $"Reach correction: {shop.ActiveBottleReachCorrection}; " +
                $"grip error: {shop.BottleGripError:0.0000}.");
            Assert.That(
                Vector3.Dot(pourDelta, service.transform.up),
                Is.InRange(0.035f, 0.16f));
            Assert.That(
                Vector3.Distance(
                    service.StreamRoot.TransformPoint(Vector3.down),
                    service.CarriedBottleMouthWorldPosition),
                Is.LessThan(0.003f));
            Assert.That(
                Vector3.Distance(
                    service.StreamRoot.TransformPoint(Vector3.up),
                    vessel.PourTargetWorldPosition),
                Is.LessThan(0.003f));

            timeout = Time.realtimeSinceStartup + 4f;
            while (shop.Phase == BarDrinkServicePhase.Pouring &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return new WaitForFixedUpdate();
                travel.Observe("pour selected drink");
            }

            Assert.That(shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.BottleCarryToGuest));
            Assert.That(vessel.FillProgress, Is.EqualTo(1f));
            Pose counterPose = shop.ResolveServedCounterWorldPose(vessel);
            int guestTravelFrames = 0;
            timeout = Time.realtimeSinceStartup + 8f;
            while (shop.Phase ==
                       BarDrinkServicePhase.BottleCarryToGuest &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return new WaitForFixedUpdate();
                if (travel.Observe("carry full vessel to guest"))
                {
                    guestTravelFrames++;
                }

                if (shop.Phase !=
                    BarDrinkServicePhase.BottleCarryToGuest)
                {
                    continue;
                }

                Assert.That(service.IsActiveVesselCarriedByBartender, Is.True);
                Assert.That(
                    service.ResolveActiveVesselGripError(
                        bartender.Registry.VesselGripAnchor),
                    Is.LessThan(0.01f));
                Assert.That(
                    Vector3.Distance(
                        vessel.GripWorldPosition,
                        bartender.Registry.LeftHand.position),
                    Is.LessThan(Vector3.Distance(
                        vessel.GripWorldPosition,
                        bartender.Registry.RightHand.position)),
                    "The full vessel must travel in the left hand.");
                Assert.That(Vector3.Angle(
                        vessel.OpeningDirection,
                        service.transform.up),
                    Is.LessThan(2f));
            }

            Assert.That(guestTravelFrames, Is.GreaterThan(2));
            Assert.That(shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.BottleVesselPlacement));
            Vector3 previousVesselPosition = vessel.transform.position;
            int placementContactFrames = 0;
            timeout = Time.realtimeSinceStartup + 3f;
            while (shop.Phase ==
                       BarDrinkServicePhase.BottleVesselPlacement &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return new WaitForFixedUpdate();
                travel.Observe("place vessel on counter");
                if (shop.Phase !=
                    BarDrinkServicePhase.BottleVesselPlacement)
                {
                    break;
                }

                float distance = Vector3.Distance(
                    vessel.transform.position, counterPose.position);
                Assert.That(Vector3.Distance(vessel.transform.position, previousVesselPosition),
                    Is.LessThan(0.10f), $"The vessel jumped during placement at {shop.Timeline.PhaseProgress:F3}; " +
                    $"grip={service.ResolveActiveVesselGripError(bartender.Registry.VesselGripAnchor):F4}.");
                Assert.That(Vector3.Angle(
                        vessel.OpeningDirection,
                        service.transform.up),
                    Is.LessThan(2f));
                if (distance > 0.03f)
                {
                    placementContactFrames++;
                    Assert.That(service.ResolveActiveVesselGripError(
                            bartender.Registry.VesselGripAnchor),
                        Is.LessThan(0.04f), DescribeBartenderReach(bartender, vessel));
                }

                previousVesselPosition = vessel.transform.position;
                AssertBartenderClearsCounter(bar);
            }

            Assert.That(placementContactFrames, Is.GreaterThan(2));
            Assert.That(shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.BottleWalkToShelfReturn));
            Assert.That(Vector3.Distance(
                    vessel.transform.position, counterPose.position),
                Is.LessThan(0.005f));
            AssertVesselRestsOnPhysicalCounter(bar, service, vessel);
            int returnTravelFrames = 0;
            timeout = Time.realtimeSinceStartup + 8f;
            while (shop.Phase ==
                       BarDrinkServicePhase.BottleWalkToShelfReturn &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return new WaitForFixedUpdate();
                if (travel.Observe("return selected bottle to shelf"))
                {
                    returnTravelFrames++;
                }

                if (shop.Phase ==
                    BarDrinkServicePhase.BottleWalkToShelfReturn)
                {
                    Assert.That(service.IsCarriedBottleVisible, Is.True);
                    Assert.That(shop.BottleGripError, Is.LessThan(0.015f));
                    Assert.That(vessel.FillProgress, Is.EqualTo(1f));
                    Assert.That(Vector3.Distance(
                            vessel.transform.position,
                            counterPose.position),
                        Is.LessThan(0.005f));
                }
            }

            Assert.That(returnTravelFrames, Is.GreaterThan(2));
            Assert.That(shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.BottleReturn));
            timeout = Time.realtimeSinceStartup + 3f;
            while (shop.Phase == BarDrinkServicePhase.BottleReturn &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return new WaitForFixedUpdate();
                travel.Observe("set selected bottle back on shelf");
            }

            Assert.That(shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.AwaitingDrink));
            Assert.That(service.IsCarriedBottleVisible, Is.False);
            AssertBottleShelfState(
                shelfBottle,
                restingShelfParent,
                restingShelfPosition,
                restingShelfRotation,
                restingShelfScale,
                true);
            Assert.That(travel.TranslationFrameCount, Is.GreaterThan(10));
            Assert.That(travel.MaximumLocalFootTravel, Is.GreaterThan(0.05f));
            Assert.That(travel.MatchedMovementFrames, Is.GreaterThan(5));
            Assert.That(GameSessionState.IntoxicationLevel,
                Is.LessThanOrEqualTo(intoxicationBefore),
                "Waiting for service may sober the hero; delivery must not apply the drink.");
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(drinksBefore));
            Assert.That(GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.None));
            shop.AdvancePresentation(30f);
            Assert.That(shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.AwaitingDrink));
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(drinksBefore));

            AreaCaptureFixture.CaptureCurrentCamera(
                Camera.main, SceneIds.BarInterior, $"12-served-{drinkId}");

            Camera camera = Camera.main;
            Vector3 lookDirection =
                vessel.GlassRenderer.bounds.center - camera.transform.position;
            camera.transform.rotation = Quaternion.LookRotation(
                lookDirection.normalized, Vector3.up);
            shop.RefreshServedDrinkAffordance();
            Assert.That(shop.IsLookingAtServedVessel, Is.True);
            int intoxicationBeforeDrink = GameSessionState.IntoxicationLevel;
            Assert.That(shop.BeginServedDrink(), Is.True);
            Assert.That(GameSessionState.IntoxicationLevel,
                Is.EqualTo(intoxicationBeforeDrink));
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(drinksBefore));
            float drinkingStartedAt = Time.unscaledTime;
            if (verifyCompletionOnly)
                yield return VerifyHeroDrinkCompletion(bar, vessel);
            else
                yield return VerifyHeroDrinkContacts(bar, vessel, drinkId);

            Assert.That(shop.IsServing, Is.False);
            Assert.That(shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.EmptyOnCounter));
            int projectedIntoxication = Mathf.Min(IntoxicationStageRules.MaximumLevel,
                intoxicationBeforeDrink + DrinkRules.GetIntoxicationGain(drinkId));
            // The real-time reach/sip/return keeps natural recovery running.
            int maximumRecovery = Mathf.CeilToInt((Time.unscaledTime - drinkingStartedAt) /
                IntoxicationStageRules.RecoverySecondsPerPointAtMinimumLevel);
            Assert.That(GameSessionState.IntoxicationLevel, Is.InRange(
                Mathf.Max(0, projectedIntoxication - maximumRecovery), projectedIntoxication));
            Assert.That(GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksBefore + 1));
            Assert.That(GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(drinkId));
            int appliedIntoxication = GameSessionState.IntoxicationLevel;
            Assert.That(shop.BeginServedDrink(), Is.False);
            shop.AdvancePresentation(30f);
            yield return new WaitForFixedUpdate();
            Assert.That(GameSessionState.IntoxicationLevel,
                Is.LessThanOrEqualTo(appliedIntoxication));
            Assert.That(GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksBefore + 1));
            if (drinkId == DrinkId.RedWine)
                yield return VerifyEmptyVesselCleanup(bar, DrinkId.Vodka);
        }

        private static IEnumerator AtFinalBarPose(WarehouseRefrigeratorPresentationProbe probe,
            System.Action sample)
        {
            probe.Schedule(sample);
            for (int frame = 0; frame < 60 && !probe.Completed; frame++) yield return null;
            Assert.That(probe.Completed, Is.True, "The final bar presentation did not run.");
            if (probe.Failure != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(probe.Failure).Throw();
        }

        private static string DescribeTowelHandoff(BarBartenderPresentation bartender, BarDrinkShopController shop)
        {
            BarBartenderTowelPresentation towel = bartender.TowelPresentation;
            return $"phase={shop.Phase}, menu={shop.MenuState}, towel={towel.State} p={towel.Progress:F3}, " +
                $"ready={towel.ServiceReady}, visible={towel.IsTowelVisible}, behindBack={towel.HandBehindBack}, " +
                $"grip={towel.LastGripError:F4}, hand={towel.HandWorldPosition:F3}, " +
                $"target={towel.TargetHandWorldPosition:F3}, pocket={towel.PocketWorldPosition:F3}.";
        }

        private static IEnumerator VerifyHeroDrinkContacts(BarInteriorRoot bar,
            BarDrinkVesselView vessel, DrinkId drinkId)
        {
            var probe = bar.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
            var clearance = new CounterSeatSkinClearance(bar);
            BarPlayerDrinkReach reach = bar.Player.GameObject.GetComponent<BarPlayerDrinkReach>();
            Assert.That(reach, Is.Not.Null);
            NpcHandPose hand = bar.Player.GameObject.GetComponentInChildren<NpcHandPose>(true);
            Pose counter = new Pose(vessel.transform.position, vessel.transform.rotation);
            Vector3 previous = counter.position;
            Transform chest = ((Player3DCharacterPresentation)bar.Player.Visual).Registry.Anchors.Chest;
            Vector3 initialChest = chest.position;
            float forwardLean = 0f;
            bool sawApproach = false, sawPickup = false, sawSip = false, sawReturn = false;
            try
            {
                for (int frame = 0; frame < 900 && bar.DrinkShop.IsServing; frame++)
                    yield return AtFinalBarPose(probe, () =>
                    {
                        BarDrinkServicePhase phase = bar.DrinkShop.Phase;
                        float progress = bar.DrinkShop.Timeline.PhaseProgress;
                        if (phase == BarDrinkServicePhase.PlayerPickup ||
                            phase == BarDrinkServicePhase.PlayerDrinking ||
                            phase == BarDrinkServicePhase.PlayerVesselReturn)
                        {
                            clearance.AssertClear("Hero drink " + phase + " " + progress.ToString("F3"));
                            Assert.That(reach.LastPelvisContactError, Is.LessThan(.001f),
                                "The seated pelvis left its support during the reach.");
                            Assert.That(reach.LastFootContactError, Is.LessThan(.002f),
                                "The hip hinge displaced a planted foot.");
                            Assert.That(reach.LastFootRotationError, Is.LessThan(.2f),
                                "The hip hinge rotated a planted foot.");
                        }
                        if (phase == BarDrinkServicePhase.PlayerPickup && !reach.PickupContactObserved)
                        {
                            Assert.That(Vector3.Distance(vessel.transform.position, counter.position),
                                Is.LessThan(.001f), "The vessel moved before the hero's hand reached it.");
                            forwardLean = Mathf.Max(forwardLean,
                                Vector3.Dot(chest.position - initialChest, bar.Player.GameObject.transform.forward));
                            if (!sawApproach && progress > .20f)
                            {
                                sawApproach = true;
                                CaptureBarServiceDetail(vessel.GripWorldPosition, $"14-hero-reaching-{drinkId}");
                            }
                        }
                        if (!sawPickup && reach.PickupContactObserved)
                        {
                            sawPickup = true;
                            Assert.That(reach.LastPickupContactError, Is.LessThanOrEqualTo(BarPlayerDrinkReach.ContactTolerance));
                            CaptureBarServiceDetail(vessel.GripWorldPosition, $"15-hero-contact-{drinkId}");
                            AssertHandWrap(hand, false, vessel.GripWorldPosition, vessel.OpeningDirection, "Hero pickup");
                            BarDrinkServiceWorldBuilderPlayModeTests.AssertBottleDoesNotIntersectHand(
                                hand, false, vessel.transform, "Hero pickup");
                        }
                        if (reach.IsAttached)
                            Assert.That(reach.VesselFollowsHandError, Is.LessThan(.012f), "The vessel left the physical palm.");
                        if (!sawSip && phase == BarDrinkServicePhase.PlayerDrinking && progress > .3f)
                        {
                            sawSip = true;
                            AssertHandWrap(hand, false, vessel.GripWorldPosition, vessel.OpeningDirection, "Hero sip");
                            BarDrinkServiceWorldBuilderPlayModeTests.AssertBottleDoesNotIntersectHand(
                                hand, false, vessel.transform, "Hero sip");
                            AreaCaptureFixture.CaptureCurrentCamera(Camera.main, SceneIds.BarInterior, $"16-hero-sip-{drinkId}");
                        }
                        if (!sawReturn && phase == BarDrinkServicePhase.PlayerVesselReturn && progress > .75f)
                        {
                            sawReturn = true;
                            CaptureBarServiceDetail(vessel.GripWorldPosition, $"17-hero-return-{drinkId}");
                        }
                        Assert.That(Vector3.Distance(previous, vessel.transform.position), Is.LessThan(.075f),
                            "The hero's vessel jumped between rendered frames.");
                        previous = vessel.transform.position;
                    });
                yield return AtFinalBarPose(probe, () =>
                {
                    if (bar.DrinkShop.Phase != BarDrinkServicePhase.EmptyOnCounter)
                        CaptureBarServiceDetail(vessel.GripWorldPosition, $"14-terminal-reach-{drinkId}");
                    Assert.That(bar.DrinkShop.Phase, Is.EqualTo(BarDrinkServicePhase.EmptyOnCounter),
                    $"grip={reach.GripError:F4}, pickup={reach.LastPickupContactError:F4}, " +
                    $"palm={reach.PalmFacingAlignment:F3}, lean={reach.TorsoLeanDegrees:F1}, " +
                    $"contact={reach.PickupContactObserved}, carry={reach.CarryProgress:F3}, " +
                    $"hand={reach.PhysicalGripWorldPosition:F3}, target={vessel.GripWorldPosition:F3}, " +
                    $"shoulder={reach.ShoulderWorldPosition:F3}, wrist={reach.WristWorldPosition:F3}, " +
                    $"wristTarget={reach.WristTargetPosition:F3}, span={reach.ArmLength:F3}, required={reach.RequiredArmReach:F3}");
                });
                Assert.That(sawApproach && sawPickup && sawSip && sawReturn, Is.True,
                    "The complete approach, physical contact, sip and return were not observed.");
                Assert.That(forwardLean, Is.GreaterThan(.04f), "The hero did not move his torso toward the supported vessel.");
                Assert.That(reach.ReturnContactObserved, Is.True, "The hero released the vessel without counter contact.");
                Assert.That(reach.LastReturnContactError, Is.LessThanOrEqualTo(.003f));
                Assert.That(Vector3.Distance(vessel.transform.position, counter.position), Is.LessThan(.003f));
                AssertVesselRestsOnPhysicalCounter(bar, bar.DrinkServiceView, vessel);
            }
            finally { probe.CancelPending(); Object.Destroy(probe); clearance.Dispose(); }
        }

        private static IEnumerator VerifyEmptyVesselCleanup(BarInteriorRoot bar, DrinkId nextDrink)
        {
            BarDrinkShopController shop = bar.DrinkShop;
            BarDrinkVesselView empty = bar.DrinkServiceView.ActiveVessel;
            Pose original = new Pose(empty.transform.position, empty.transform.rotation);
            Bounds backbar = bar.Room.Find("Backbar Cabinet").GetComponent<Renderer>().bounds;
            Bounds counterTop = bar.Room.Find("Counter Top").GetComponent<Renderer>().bounds;
            int cash = GameSessionState.CashBalance, consumed = GameSessionState.DrinksConsumed;
            int cleared = shop.ClearedEmptyVesselCount;
            // Reopening the same physical booklet precedes the next order.
            var aim = Camera.main.gameObject.AddComponent<BarMenuTestCameraAimDriver>();
            aim.Target = shop.MenuPresentation.Page.RestingWorldCenter;
            aim.IsAiming = true;
            aim.LookAway = true;
            yield return null;
            aim.LookAway = false;
            for (int frame = 0; frame < 120 && !shop.IsLookingAtRestingMenu; frame++) yield return null;
            Assert.That(shop.ReopenPhysicalMenu(), Is.True);
            Object.Destroy(aim);
            for (int frame = 0; frame < 360 &&
                !bar.CounterStations[bar.CounterStations.Count - 1].SeatView.IsMenuFocusComplete; frame++) yield return null;
            yield return null;
            Assert.That(shop.Select(FindOfferIndex(shop, nextDrink)), Is.True);
            int price = shop.SelectedOffer.Price;
            bar.CounterStations[bar.CounterStations.Count - 1].Interact(bar.Player.Interactor);
            Assert.That(shop.Phase, Is.EqualTo(BarDrinkServicePhase.EmptyVesselWalkToGuest));
            Assert.That(bar.DrinkServiceView.ActiveVessel, Is.SameAs(empty));
            Assert.That(empty.gameObject.activeSelf, Is.True);
            var probe = bar.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
            bool sawContact = false, sawCarry = false, sawStow = false, sawHidden = false;
            bool sawBackStep = false, sawSupport = false;
            Pose pickupServer = default;
            Vector3 stowStart = default, supportTarget = default;
            float previousBackStep = 0f;
            Vector3 previous = empty.transform.position;
            float cleanupDeadline = Time.realtimeSinceStartup + 15f;
            try
            {
                while (shop.IsClearingEmptyVessel && Time.realtimeSinceStartup < cleanupDeadline)
                    yield return AtFinalBarPose(probe, () =>
                    {
                        if (!shop.IsClearingEmptyVessel) return;
                        if (shop.IsBartenderServiceReady || shop.Timeline.EmptyVesselGripConfirmed)
                            Assert.That(bar.Bartender.Registry.ServiceTowel.IsVisible, Is.False,
                                "The wiping towel overlaps the hand collecting a vessel.");
                        Assert.That(bar.DrinkServiceView.ActiveVessel, Is.SameAs(empty));
                        Assert.That(empty.gameObject.activeSelf, Is.True);
                        Bounds shell = empty.GlassRenderer.bounds;
                        Assert.That(shell.Intersects(backbar), Is.False,
                            $"The empty vessel crosses the backbar during {shop.Phase}: {shell}.");
                        if (shell.max.x > counterTop.min.x && shell.min.x < counterTop.max.x &&
                            shell.max.z > counterTop.min.z && shell.min.z < counterTop.max.z)
                            Assert.That(shell.min.y, Is.GreaterThanOrEqualTo(counterTop.max.y - .005f),
                                "The old vessel crosses the counter during cleanup.");
                        Assert.That(empty.FillProgress, Is.Zero);
                        Assert.That(bar.DrinkServiceView.IsCarriedBottleVisible, Is.False,
                            "The next bottle appeared before empty-vessel cleanup.");
                        if (!shop.Timeline.EmptyVesselGripConfirmed)
                            Assert.That(Vector3.Distance(empty.transform.position, original.position), Is.LessThan(.001f));
                        else if (!sawContact)
                        {
                            sawContact = true;
                            pickupServer = new Pose(bar.Bartender.transform.position, bar.Bartender.transform.rotation);
                            Assert.That(Vector3.Distance(empty.GripWorldPosition, bar.Bartender.Registry.VesselGripAnchor.position),
                                Is.LessThan(.015f), "The empty vessel detached before physical hand contact.");
                            AssertHandWrap(bar.Bartender.Registry.HandPose, true,
                                empty.GripWorldPosition, empty.OpeningDirection, "Bartender clearing empty");
                            BarDrinkServiceWorldBuilderPlayModeTests.AssertBottleDoesNotIntersectHand(
                                bar.Bartender.Registry.HandPose, true, empty.transform, "Bartender clearing empty");
                            CaptureBarServiceDetail(empty.GripWorldPosition, "18-empty-pickup");
                        }
                        if (shop.Phase == BarDrinkServicePhase.EmptyVesselCarryToStorage ||
                            shop.Phase == BarDrinkServicePhase.EmptyVesselStow)
                        {
                            Vector3 delta = bar.Bartender.transform.position - pickupServer.position;
                            float backStep = -Vector3.Dot(delta, pickupServer.rotation * Vector3.forward);
                            Assert.That(Quaternion.Angle(bar.Bartender.transform.rotation, pickupServer.rotation),
                                Is.LessThan(.2f), "The bartender turned while clearing the empty vessel.");
                            Assert.That(Mathf.Abs(Vector3.Dot(delta, pickupServer.rotation * Vector3.right)),
                                Is.LessThan(.002f), "The backward step drifted sideways.");
                            Assert.That(backStep, Is.InRange(previousBackStep - .002f,
                                BarDrinkShopController.EmptyVesselBackStepDistance + .002f));
                            previousBackStep = backStep;
                            Assert.That(Vector3.Distance(empty.GripWorldPosition, bar.Bartender.Registry.VesselGripAnchor.position),
                                Is.LessThan(.015f), "The empty vessel left the hand during the backward step/lowering.");
                            BarBartenderAssetRegistry rig = bar.Bartender.Registry;
                            if (rig.LeftForearm.position.y >= rig.LeftUpperArm.position.y - .06f)
                                CaptureBarServiceDetail(empty.GripWorldPosition, "Test empty vessel elbow direction");
                            Assert.That(rig.LeftForearm.position.y, Is.LessThan(rig.LeftUpperArm.position.y - .06f),
                                $"The carrying elbow must stay below the shoulder: phase={shop.Phase}, " +
                                $"shoulder={rig.LeftUpperArm.position:F3}, elbow={rig.LeftForearm.position:F3}, " +
                                $"wrist={rig.LeftHand.position:F3}, vessel={empty.transform.position:F3}.");
                            Assert.That(Vector3.Angle(rig.LeftUpperArm.position - rig.LeftForearm.position,
                                rig.LeftHand.position - rig.LeftForearm.position), Is.GreaterThan(45f),
                                "The carrying arm folds back unnaturally against the body.");
                            if (!sawBackStep && backStep > .08f)
                            {
                                sawBackStep = true;
                                CaptureBarServiceDetail(empty.GripWorldPosition, "Test empty vessel backward step");
                            }
                        }
                        if (shop.Phase == BarDrinkServicePhase.EmptyVesselCarryToStorage) sawCarry = true;
                        if (shop.Phase == BarDrinkServicePhase.EmptyVesselStow)
                        {
                            if (!sawStow) stowStart = empty.transform.position;
                            sawStow = true;
                            Assert.That(previousBackStep, Is.EqualTo(BarDrinkShopController.EmptyVesselBackStepDistance).Within(.002f));
                            Assert.That(Vector2.Distance(new Vector2(empty.transform.position.x, empty.transform.position.z),
                                new Vector2(stowStart.x, stowStart.z)), Is.LessThan(.015f), "The vessel must lower vertically.");
                            if (shop.Timeline.PhaseProgress > .36f)
                            {
                                BarBartenderPresentation bartender = bar.Bartender;
                                Assert.That(bartender.CounterHandSupportWeight, Is.GreaterThan(.99f));
                                Assert.That(bartender.CounterHandSupportContactError, Is.LessThan(.004f),
                                    "The free palm left its counter support while lowering the vessel.");
                                Assert.That(Vector3.Dot(bartender.CounterHandSupportPalmNormal, Vector3.down), Is.GreaterThan(.99f));
                                Assert.That(Vector3.Dot(bartender.CounterHandSupportFingerDirection, bartender.transform.forward), Is.GreaterThan(.99f));
                                Assert.That(bartender.CounterHandSupportPalmContact.y,
                                    Is.EqualTo(counterTop.max.y + BarBartenderPresentation.CounterHandSupportClearance).Within(.004f));
                                if (!sawSupport)
                                {
                                    supportTarget = bartender.CounterHandSupportTarget;
                                    CaptureBarServiceDetail(empty.GripWorldPosition, "Test empty vessel counter palm support");
                                }
                                Assert.That(Vector3.Distance(supportTarget, bartender.CounterHandSupportTarget), Is.LessThan(.001f));
                                sawSupport = true;
                            }
                            if (!sawHidden && empty.GlassRenderer.bounds.max.y < bar.DrinkServiceView.CounterSurfaceWorldHeight)
                            {
                                sawHidden = true;
                                CaptureBarServiceDetail(empty.GripWorldPosition, "19-empty-under-counter");
                            }
                        }
                        Assert.That(Vector3.Distance(previous, empty.transform.position), Is.LessThan(.075f),
                            $"The empty vessel jumped during {shop.Phase} p={shop.Timeline.PhaseProgress:F3}; " +
                            $"root={bar.Bartender.transform.position:F3}, yaw={bar.Bartender.transform.eulerAngles.y:F1}, " +
                            $"from={previous:F3}, to={empty.transform.position:F3}, " +
                            $"grip={bar.Bartender.Registry.VesselGripAnchor.position:F3}, dt={Time.deltaTime:F3}.");
                        previous = empty.transform.position;
                        Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash - price));
                        Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(consumed));
                    });
                Assert.That(sawContact && sawCarry && sawStow && sawHidden && sawBackStep && sawSupport, Is.True,
                    $"The old vessel was not visibly picked up, carried and lowered beneath the counter: " +
                    $"phase={shop.Phase} p={shop.Timeline.PhaseProgress:F3}, " +
                    $"contact={sawContact}, carry={sawCarry}, stow={sawStow}, hidden={sawHidden}; " +
                    $"root={bar.Bartender.transform.position:F3}, vessel={empty.transform.position:F3}, " +
                    $"grip={bar.Bartender.Registry.VesselGripAnchor.position:F3}, " +
                    $"retraction={shop.EmptyVesselRetractionError:F4}, " +
                    $"storageFacing={shop.EmptyVesselStorageFacingError:F2}.");
                Assert.That(shop.ClearedEmptyVesselCount, Is.EqualTo(cleared + 1),
                    $"phase={shop.Phase} p={shop.Timeline.PhaseProgress:F3}, " +
                    $"root={bar.Bartender.transform.position:F3}, vessel={empty.transform.position:F3}, " +
                    $"bounds={empty.GlassRenderer.bounds}, camera={Camera.main.transform.position:F3}, " +
                    $"grip={bar.Bartender.Registry.VesselGripAnchor.position:F3}, " +
                    $"supportError={bar.Bartender.CounterHandSupportContactError:F5}");
                Assert.That(empty.gameObject.activeSelf, Is.False);
                Assert.That(shop.Phase, Is.EqualTo(BarDrinkServicePhase.BottleWalkToShelf));
                Assert.That(bar.DrinkServiceView.ActiveVessel, Is.Not.SameAs(empty));
                Assert.That(bar.DrinkServiceView.ActiveVessel.gameObject.activeSelf, Is.False);
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash - price));
                Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(consumed));
            }
            finally { probe.CancelPending(); Object.Destroy(probe); }
        }

        [UnityTest]
        public IEnumerator
            SeatedCounterMenu_ClearsCounterAndFitsReadableViewport()
        {
            BarInteriorRoot bar = null;
            yield return LoadBar(result => bar = result);

            Assert.That(bar.ArrivalPresentation, Is.Not.Null);
            bar.ArrivalPresentation.Skip();

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null, "the bar has no main camera");
            camera.aspect = 16f / 9f;

            BarCounterStation station = bar.CounterStation;
            Assert.That(station, Is.Not.Null);
            Assert.That(bar.CounterStations, Has.Count.EqualTo(3));
            Assert.That(station.Seat, Is.Not.Null);
            Assert.That(station.SeatView, Is.Not.Null);
            CounterSeatPlan seatPlan = station.Seat.Plan;
            Assert.That(seatPlan, Is.Not.Null);

            for (int index = 0; index < bar.CounterStations.Count; index++)
            {
                BarCounterStation available = bar.CounterStations[index];
                Assert.That(
                    available.Seat.Plan.ApproachWaypointCount,
                    Is.GreaterThan(0));
                bar.Player.Motor.Teleport(
                    available.Seat.Plan.ApproachWaypoints[0]);
                bar.Player.GameObject.transform.rotation =
                    available.Seat.Plan.EntryPose.RootRotation;
                Physics.SyncTransforms();
                float focusDeadline = Time.realtimeSinceStartup + 1f;
                while (Time.realtimeSinceStartup < focusDeadline &&
                       !ReferenceEquals(
                           bar.Player.Interactor.ActiveInteractable,
                           available))
                {
                    yield return null;
                }

                Assert.That(
                    bar.Player.Interactor.ActiveInteractable,
                    Is.SameAs(available),
                    $"free counter stool {index + 1} cannot be targeted " +
                    "from its authored approach lane");
                available.Interact(bar.Player.Interactor);

                PlayerAnimatedInteractionController interaction =
                    available.Seat.Controller;
                float positioningDeadline =
                    Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < positioningDeadline &&
                       interaction.Phase ==
                           PlayerAnimatedInteractionPhase.Positioning)
                {
                    yield return null;
                }

                Assert.That(
                    interaction.Phase,
                    Is.EqualTo(PlayerAnimatedInteractionPhase.Entering),
                    $"free counter stool {index + 1} aborted before its " +
                    "seating animation");
                Assert.That(
                    bar.DrinkShop.IsOpen,
                    Is.False,
                    "the menu must wait until the hero is actually seated");
                Assert.That(available.Seat.Cancel(), Is.True);
                yield return null;
                Assert.That(
                    interaction.Phase,
                    Is.EqualTo(PlayerAnimatedInteractionPhase.Idle));
                Assert.That(bar.DrinkShop.IsOpen, Is.False);
            }

            Vector3 reportedBlockedPosition = seatPlan.ApproachWaypoints[0];
            reportedBlockedPosition.y = seatPlan.EntryPose.RootPosition.y;
            bar.Player.Motor.Teleport(reportedBlockedPosition);
            bar.Player.GameObject.transform.rotation =
                seatPlan.EntryPose.RootRotation;
            Physics.SyncTransforms();
            Assert.That(
                station.CanInteract(bar.Player.Interactor),
                Is.True,
                "the reported blocked position cannot start the counter seat");
            station.Interact(bar.Player.Interactor);

            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline &&
                   (!station.Seat.IsSeated ||
                    bar.DrinkShop.MenuState !=
                    BarPromenade.Runtime.World.CounterMenuState.Open ||
                    !station.SeatView.IsMenuFocusComplete))
            {
                yield return null;
            }

            Assert.That(station.Seat.IsSeated, Is.True);
            Assert.That(
                bar.Player.Motor.InteractionPoseMoveStalled,
                Is.False,
                "the safe entry dock still collides with the stool");
            Assert.That(
                bar.DrinkShop.MenuState,
                Is.EqualTo(
                    BarPromenade.Runtime.World.CounterMenuState.Open));
            Assert.That(station.SeatView.IsMenuFocusComplete, Is.True);
            // WaitForEndOfFrame is never pumped by Unity's headless runner;
            // one ordinary update is sufficient because TMP is forced below.
            yield return null;

            Transform counterTop = bar.Room.Find("Counter Top");
            Assert.That(
                counterTop,
                Is.Not.Null,
                "the authored room has no main counter-top renderer");
            Renderer counterTopRenderer =
                counterTop.GetComponent<Renderer>();
            Assert.That(counterTopRenderer, Is.Not.Null);
            float counterSurfaceY = counterTopRenderer.bounds.max.y;

            Vector3 plannedSeatedCamera =
                seatPlan.ActionHipPosition +
                seatPlan.CameraOffsetFromActionHip;
            float plannedEyeClearance =
                plannedSeatedCamera.y - counterSurfaceY;
            float focusedEyeClearance =
                camera.transform.position.y - counterSurfaceY;

            BarDrinkMenuPresentation menu =
                bar.DrinkShop.MenuPresentation;
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.IsPlaced, Is.True);
            Assert.That(menu.IsTextVisible, Is.True);
            Assert.That(
                menu.ItemLines.Count,
                Is.EqualTo(BarServicePropFactory.MenuItemCount));

            Vector3 menuInRoom = bar.Room.InverseTransformPoint(
                menu.PropRoot.position);
            Vector3 heroInRoom = bar.Room.InverseTransformPoint(
                seatPlan.ActionHipPosition);
            Assert.That(
                menuInRoom.x,
                Is.EqualTo(heroInRoom.x).Within(0.01f),
                "the bartender did not put the menu directly before the " +
                "seated hero");

            BarServicePropInstance prop =
                menu.PropRoot.GetComponent<BarServicePropInstance>();
            Assert.That(prop, Is.Not.Null);
            Assert.That(
                prop.TryGetAnchor(
                    BarServicePropFactory.MenuPageOriginRole,
                    out Transform pageOrigin),
                Is.True);
            Assert.That(
                prop.TryGetAnchor(
                    BarServicePropFactory.MenuPageNormalRole,
                    out Transform pageNormal),
                Is.True);
            Assert.That(
                prop.TryGetAnchor(
                    BarServicePropFactory.MenuTextItemRole(0),
                    out Transform firstRow),
                Is.True);
            Vector3 authoredPageNormal =
                (pageNormal.position - pageOrigin.position).normalized;
            Vector3 cameraFromPage =
                camera.transform.position - pageOrigin.position;
            float overheadSurfaceFacing = Vector3.Dot(
                authoredPageNormal,
                cameraFromPage.normalized);
            float cameraPlanarOffset = Vector3.ProjectOnPlane(
                cameraFromPage,
                authoredPageNormal).magnitude;
            float closestMenuDistance = float.PositiveInfinity;
            for (int index = 0; index < prop.Renderers.Count; index++)
            {
                Renderer renderer = prop.Renderers[index];
                Assert.That(renderer, Is.Not.Null);
                closestMenuDistance = Mathf.Min(
                    closestMenuDistance,
                    Mathf.Sqrt(renderer.bounds.SqrDistance(
                        camera.transform.position)));
            }

            float minimumPageFacing = 1f;
            float minimumRenderedFontSize = float.PositiveInfinity;
            Vector3 firstPageOutward = Vector3.zero;
            Vector3 firstTowardCamera = Vector3.zero;
            for (int index = 0; index < menu.ItemLines.Count; index++)
            {
                TMPro.TMP_Text line = menu.ItemLines[index];
                Assert.That(line, Is.Not.Null);
                line.ForceMeshUpdate();
                Assert.That(
                    line.isTextOverflowing,
                    Is.False,
                    $"bar menu row {index + 1} is truncated");
                Assert.That(
                    line.enableAutoSizing,
                    Is.False,
                    $"bar menu row {index + 1} may not shrink independently");
                minimumRenderedFontSize = Mathf.Min(
                    minimumRenderedFontSize,
                    line.fontSize);
                Vector3 outward = -line.transform.forward.normalized;
                Vector3 towardCamera =
                    (camera.transform.position -
                     line.transform.position).normalized;
                if (index == 0)
                {
                    firstPageOutward = outward;
                    firstTowardCamera = towardCamera;
                }

                minimumPageFacing = Mathf.Min(
                    minimumPageFacing,
                    Vector3.Dot(outward, towardCamera));
            }

            ViewportEnvelope viewport = MeasureMenuViewport(
                camera,
                prop.Renderers,
                menu.ItemLines,
                menu.SelectionMarker);
            Debug.Log(
                $"Bar counter-menu visual contract: counterTopY=" +
                $"{counterSurfaceY:F3}, seatedClearance=" +
                $"{plannedEyeClearance:F3}, focusedClearance=" +
                $"{focusedEyeClearance:F3}, closestMenu=" +
                $"{closestMenuDistance:F3}, pageFacing=" +
                $"{minimumPageFacing:F3}, firstOutward=" +
                $"{firstPageOutward:F3}, firstTowardCamera=" +
                $"{firstTowardCamera:F3}, minFont=" +
                $"{minimumRenderedFontSize:F3}, camera=" +
                $"{camera.transform.position:F3}, authoredPageNormal=" +
                $"{authoredPageNormal:F3}, overheadFacing=" +
                $"{overheadSurfaceFacing:F3}, planarOffset=" +
                $"{cameraPlanarOffset:F3}, rowAxes=" +
                $"{firstRow.right:F3}/{firstRow.up:F3}/" +
                $"{firstRow.forward:F3}, viewport={viewport}.");

            Assert.That(
                plannedEyeClearance,
                Is.GreaterThanOrEqualTo(MinimumEyeAboveCounter),
                "the authored seated lens is below/inside the visual " +
                "counter edge");
            Assert.That(
                focusedEyeClearance,
                Is.GreaterThanOrEqualTo(MinimumEyeAboveCounter),
                "menu focus lowers the real camera into the counter");
            Assert.That(
                closestMenuDistance,
                Is.GreaterThanOrEqualTo(MinimumEyeAboveCounter),
                "one edge of the booklet is too close to the lens");
            Assert.That(
                minimumPageFacing,
                Is.GreaterThanOrEqualTo(MinimumReadablePageFacing),
                "the camera sees the menu text almost edge-on");
            Assert.That(
                minimumRenderedFontSize,
                Is.EqualTo(CounterMenuPageStyle.Bar.ItemFontSize)
                    .Within(0.0001f),
                "the descriptive bar menu does not use one fixed type size");
            Assert.That(
                overheadSurfaceFacing,
                Is.GreaterThanOrEqualTo(MinimumOverheadSurfaceFacing),
                "the camera does not hang steeply enough over the menu");
            Assert.That(
                cameraPlanarOffset,
                Is.LessThanOrEqualTo(MaximumCameraPlanarOffset),
                "the camera projection lands beyond the menu footprint");

            Volume cinematicVolume = FindCinematicDepthOfFieldVolume();
            Assert.That(cinematicVolume, Is.Not.Null);
            Assert.That(
                cinematicVolume.profile.TryGet(
                    out DepthOfField cinematicDepthOfField),
                Is.True);
            Assert.That(
                cinematicDepthOfField.focusDistance.value,
                Is.EqualTo(Vector3.Distance(
                        camera.transform.position,
                        menu.CameraFocusWorldPosition))
                    .Within(0.01f),
                "the close-up DOF is focused behind the menu");
            Assert.That(
                cinematicDepthOfField.aperture.value,
                Is.EqualTo(
                    BarDrinkShopController.CounterMenuDepthOfFieldAperture),
                "the menu close-up uses the restrained indoor aperture");
            Assert.That(
                cinematicDepthOfField.focalLength.value,
                Is.EqualTo(
                    BarDrinkShopController.CounterMenuDepthOfFieldFocalLength),
                "the menu close-up uses the restrained indoor focal length");
            Assert.That(viewport.HasPoints, Is.True);
            Assert.That(
                viewport.MinimumDepth,
                Is.GreaterThan(camera.nearClipPlane + 0.02f),
                "part of the menu lies on the near clip plane");
            Assert.That(
                viewport.MinimumX,
                Is.GreaterThanOrEqualTo(0.03f),
                "the menu is cropped at the left edge");
            Assert.That(
                viewport.MaximumX,
                Is.LessThanOrEqualTo(0.97f),
                "the menu is cropped at the right edge");
            Assert.That(
                viewport.MinimumY,
                Is.GreaterThanOrEqualTo(MenuSafeViewportBottom),
                "the menu runs under the bottom controls/status hint");
            Assert.That(
                viewport.MaximumY,
                Is.LessThanOrEqualTo(0.95f),
                "the menu is cropped at the top edge");

            PlayerCameraFollow cameraFollow =
                station.SeatView.CameraFollow;
            Assert.That(cameraFollow, Is.Not.Null);
            var heroPresentation =
                bar.Player.Visual as Player3DCharacterPresentation;
            Assert.That(heroPresentation, Is.Not.Null);
            Assert.That(heroPresentation.Registry, Is.Not.Null);
            seatPlan.EvaluateCamera(
                heroPresentation.Registry.Anchors.Pelvis.position,
                0f,
                0f,
                out Vector3 returnedPosition,
                out Quaternion returnedRotation);
            Assert.That(
                Vector3.Distance(
                    cameraFollow.FixedBasePosition,
                    returnedPosition),
                Is.GreaterThan(0.15f),
                "The menu close-up is not visibly closer than the seated " +
                "view.");
            Assert.That(
                cameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(BarDrinkMenuPresentation.CameraFocusFieldOfView)
                    .Within(0.001f));

            Assert.That(bar.DrinkShop.RestPhysicalMenuAtCounter(), Is.True);
            deadline = Time.realtimeSinceStartup + 2f;
            while (station.SeatView.IsMenuFocusLocked &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                station.SeatView.IsMenuFocusLocked,
                Is.False,
                "Closing the menu never released its camera focus.");
            Assert.That(
                station.SeatView.MenuFocusWeight,
                Is.Zero.Within(0.0001f));
            Assert.That(
                Vector3.Distance(
                    cameraFollow.FixedBasePosition,
                    returnedPosition),
                Is.LessThan(0.002f),
                "The camera did not return to the seated pose.");
            Assert.That(
                Quaternion.Angle(
                    cameraFollow.FixedBaseRotation,
                    returnedRotation),
                Is.LessThan(0.1f));
            Assert.That(
                cameraFollow.FixedBaseFieldOfView,
                Is.EqualTo(seatPlan.CameraFieldOfView).Within(0.001f));
        }

        private static ViewportEnvelope MeasureMenuViewport(
            Camera camera,
            IReadOnlyList<Renderer> physicalRenderers,
            IReadOnlyList<TMPro.TMP_Text> lines,
            TMPro.TMP_Text marker)
        {
            var envelope = new ViewportEnvelope();
            for (int index = 0; index < physicalRenderers.Count; index++)
            {
                Renderer renderer = physicalRenderers[index];
                IncludeLocalBounds(
                    camera,
                    envelope,
                    renderer.transform,
                    renderer.localBounds);
            }

            for (int index = 0; index < lines.Count; index++)
            {
                TMPro.TMP_Text line = lines[index];
                IncludeLocalBounds(
                    camera,
                    envelope,
                    line.transform,
                    line.textBounds);
            }

            Assert.That(marker, Is.Not.Null);
            marker.ForceMeshUpdate();
            IncludeLocalBounds(
                camera,
                envelope,
                marker.transform,
                marker.textBounds);
            return envelope;
        }

        private static Vector3 MeasureWipeTowel(
            CityPedestrianHandPropRegistry towel,
            Bounds counter)
        {
            Assert.That(towel.Renderers, Is.Not.Empty);
            Bounds visible = towel.Renderers[0].bounds;
            for (int index = 1; index < towel.Renderers.Count; index++)
            {
                visible.Encapsulate(towel.Renderers[index].bounds);
            }
            Vector3 centre = visible.center;
            float gap = visible.min.y - counter.max.y;
            Assert.That(
                gap,
                Is.InRange(-MaximumWipePenetration, MaximumWipeGap),
                $"The towel is wiping air ({gap:F3} m from the counter). ");
            Assert.That(
                centre.x,
                Is.InRange(
                    counter.min.x - WipeSurfaceMargin,
                    counter.max.x + WipeSurfaceMargin),
                "The towel left the counter width.");
            Assert.That(
                centre.z,
                Is.InRange(
                    counter.min.z - WipeSurfaceMargin,
                    counter.max.z + WipeSurfaceMargin),
                "The towel left the counter depth.");
            return centre;
        }

        private static int FindOfferIndex(
            BarDrinkShopController shop,
            DrinkId drinkId)
        {
            for (int index = 0; index < shop.Offers.Count; index++)
            {
                if (shop.Offers[index].DrinkId == drinkId)
                {
                    return index;
                }
            }

            Assert.Fail($"Missing retail offer for {drinkId}.");
            return -1;
        }

        private static Transform FindRequiredDescendant(
            Transform root,
            string objectName)
        {
            Transform[] descendants =
                root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < descendants.Length; index++)
            {
                if (descendants[index].name == objectName)
                {
                    return descendants[index];
                }
            }

            Assert.Fail($"Missing bartender transform '{objectName}'.");
            return null;
        }

        private static string DescribeBartenderReach(
            BarBartenderPresentation bartender, BarDrinkVesselView vessel)
        {
            BarBartenderAssetRegistry rig = bartender.Registry;
            return $"root={bartender.transform.position:F3}, " +
                $"shoulder={rig.LeftUpperArm.position:F3}, elbow={rig.LeftForearm.position:F3}, " +
                $"hand={rig.LeftHand.position:F3}, grip={rig.VesselGripAnchor.position:F3}, " +
                $"target={vessel.GripWorldPosition:F3}, head={rig.Head.position:F3}, " +
                $"torsoUp={Vector3.Dot((rig.Chest.position - rig.Spine.position).normalized, bartender.transform.up):F3}";
        }

        // Inspect the visible skinned digits, independently of the contact
        // solver/socket frame. Flat fingers or a back-of-hand attachment cannot
        // cross the far side of the physical cylinder from the neutral palm.
        internal static void AssertHandWrap(NpcHandPose pose, bool isLeft,
            Vector3 cylinderCentre, Vector3 cylinderAxis, string label)
        {
            Assert.That(pose, Is.Not.Null, label);
            NpcHandPose.HandBinding binding = null;
            foreach (NpcHandPose.HandBinding candidate in pose.Hands)
                if (candidate.IsLeft == isLeft) binding = candidate;
            Assert.That(binding, Is.Not.Null, label);
            Vector3 palm = default;
            bool foundPalm = false;
            foreach (SkinnedMeshRenderer renderer in binding.Renderers)
                if (renderer.enabled && renderer.gameObject.activeInHierarchy &&
                    (renderer.name.Contains("HandPalm.") || renderer.name.Contains("GlovePalm.") ||
                     renderer.name.StartsWith("GEO_Hand.")))
                {
                    palm = NeutralHandMeshCentre(renderer, binding.Hand);
                    foundPalm = true;
                }
            Assert.That(foundPalm, Is.True, label + " has no visible palm");
            Vector3 distal = default, thumb = default, indexFinger = default;
            int fingerCount = 0;
            foreach (SkinnedMeshRenderer renderer in binding.Renderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Vector3 neutral = NeutralHandMeshCentre(renderer, binding.Hand);
                if (renderer.name.Contains("Thumb.")) thumb = neutral;
                if (renderer.name.Contains("Finger")) { distal += neutral; fingerCount++; }
                if (renderer.name.Contains("Finger0.")) indexFinger = neutral;
            }
            Assert.That(fingerCount, Is.EqualTo(4), label);
            distal = (distal / fingerCount - palm).normalized;
            Vector3 across = Vector3.ProjectOnPlane(thumb - indexFinger, distal).normalized;
            Vector3 towardObject = Vector3.Cross(distal, across).normalized * (isLeft ? -1f : 1f);
            Assert.That(Vector3.Dot(towardObject, cylinderCentre - palm), Is.GreaterThan(.015f),
                label + " presents the back of its palm to the object");
            int wrappedDigits = 0;
            foreach (SkinnedMeshRenderer renderer in binding.Renderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    (!renderer.name.Contains("Finger") && !renderer.name.Contains("Thumb."))) continue;
                Vector3[] vertices = BarDrinkServiceWorldBuilderPlayModeTests.ReadVerifiedHandWorldVertices(
                    renderer, binding.Hand, label);
                float farSide = float.NegativeInfinity;
                Vector3[] neutral = renderer.sharedMesh.vertices;
                int bone = System.Array.IndexOf(renderer.bones, binding.Hand);
                Matrix4x4 neutralToWorld = binding.Hand.localToWorldMatrix * renderer.sharedMesh.bindposes[bone];
                float digitTravel = 0f;
                for (int index = 0; index < vertices.Length; index++)
                {
                    Vector3 world = vertices[index];
                    digitTravel = Mathf.Max(digitTravel,
                        Vector3.Distance(world, neutralToWorld.MultiplyPoint3x4(neutral[index])));
                    farSide = Mathf.Max(farSide, Vector3.Dot(world - cylinderCentre, towardObject));
                }
                if (renderer.name.Contains("Finger"))
                    Assert.That(BarDrinkServiceWorldBuilderPlayModeTests.MeasureFingerCurlDegrees(
                        renderer, binding.Hand, vertices), Is.GreaterThanOrEqualTo(
                        BarDrinkServiceWorldBuilderPlayModeTests.MinimumFingerCurlDegrees(pose.GetGripKind(isLeft))),
                        label + "/" + renderer.name + " remained flat");
                else
                    Assert.That(digitTravel, Is.GreaterThan(.01f),
                        label + "/" + renderer.name + " did not oppose the fingers");
                if (pose.GetGripKind(isLeft) == NpcHandGripKind.Cylindrical)
                    // This plane comes from the actual palm mesh. Its normal
                    // differs from the source bone frame: a valid hero index
                    // crosses it by 1.24 mm, versus 4.84 mm in the source plane.
                    // Crossing the far half, digit curl and the independent
                    // solid-intersection proof together establish a real wrap.
                    Assert.That(farSide, Is.GreaterThan(0f),
                        label + "/" + renderer.name + " does not wrap the actual cylinder; far=" + farSide);
                wrappedDigits++;
            }
            Assert.That(wrappedDigits, Is.EqualTo(5), label);
        }

        private static Vector3 NeutralHandMeshCentre(SkinnedMeshRenderer renderer, Transform hand)
        {
            int bone = System.Array.IndexOf(renderer.bones, hand);
            Assert.That(bone, Is.GreaterThanOrEqualTo(0), renderer.name);
            Mesh mesh = renderer.sharedMesh;
            Matrix4x4 toWorld = hand.localToWorldMatrix * mesh.bindposes[bone];
            Vector3 centre = default;
            Vector3[] vertices = mesh.vertices;
            foreach (Vector3 vertex in vertices) centre += toWorld.MultiplyPoint3x4(vertex);
            return centre / vertices.Length;
        }

        private static void CaptureBarServiceDetail(Vector3 target, string name)
        {
            Camera camera = Camera.main;
            Pose previous = new Pose(camera.transform.position, camera.transform.rotation);
            float fieldOfView = camera.fieldOfView;
            try
            {
                Vector3 from = target + new Vector3(0.8f, 0.5f, -0.7f);
                camera.transform.SetPositionAndRotation(from,
                    Quaternion.LookRotation(target + Vector3.up * 0.10f - from, Vector3.up));
                camera.fieldOfView = 58f;
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.BarInterior, name);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(previous.position, previous.rotation);
                camera.fieldOfView = fieldOfView;
            }
        }

        private static void AssertBartenderClearsCounter(BarInteriorRoot bar)
        {
            BarBartenderAssetRegistry rig = bar.Bartender.Registry;
            Bounds counter = bar.Room.Find("Counter Top")
                .GetComponent<Renderer>().bounds;
            Assert.That(rig.Head.position.y,
                Is.GreaterThan(counter.max.y + 0.25f),
                "The bartender's head folds into the counter during service.");
            Assert.That(Vector3.Dot(
                    (rig.Chest.position - rig.Spine.position).normalized,
                    bar.Bartender.transform.up),
                Is.GreaterThan(0.70f),
                "The bartender must reach with the arm without collapsing " +
                "the torso onto the counter.");
        }

        private static void AssertVesselRestsOnPhysicalCounter(
            BarInteriorRoot bar,
            BarDrinkServiceView service,
            BarDrinkVesselView vessel)
        {
            Bounds top = bar.Room.Find("Counter Top").GetComponent<Renderer>().bounds;
            Bounds shell = vessel.GlassRenderer.bounds;
            Assert.That(shell.min.x, Is.GreaterThanOrEqualTo(top.min.x));
            Assert.That(shell.max.x, Is.LessThanOrEqualTo(top.max.x));
            Assert.That(shell.min.z, Is.GreaterThanOrEqualTo(top.min.z));
            Assert.That(shell.max.z, Is.LessThanOrEqualTo(top.max.z));
            Assert.That(shell.min.y, Is.EqualTo(top.max.y).Within(.005f),
                "The final vessel must rest on the measured visible counter top.");
        }

        private static void AssertBottleShelfState(
            BarDrinkBottleView bottle,
            Transform parent,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale,
            bool renderersEnabled)
        {
            Assert.That(bottle.transform.parent, Is.SameAs(parent));
            Assert.That(bottle.transform.localPosition,
                Is.EqualTo(localPosition));
            Assert.That(Quaternion.Angle(
                    bottle.transform.localRotation, localRotation),
                Is.LessThan(0.001f));
            Assert.That(bottle.transform.localScale,
                Is.EqualTo(localScale));
            for (int index = 0; index < bottle.Renderers.Count; index++)
            {
                Assert.That(bottle.Renderers[index].enabled,
                    Is.EqualTo(renderersEnabled));
            }
        }

        private static void AdvanceWipeToPhase(
            BarBartenderPresentation bartender,
            float clipLength,
            float normalizedPhase)
        {
            float current = Mathf.Repeat(
                bartender.CurrentClipTimeSeconds,
                clipLength);
            float target = normalizedPhase * clipLength;
            float delta = target - current;
            if (delta < 0f)
            {
                delta += clipLength;
            }

            bartender.Advance(delta);
        }

        private static void IncludeLocalBounds(
            Camera camera,
            ViewportEnvelope envelope,
            Transform transform,
            Bounds bounds)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 local = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        envelope.Include(
                            camera.WorldToViewportPoint(
                                transform.TransformPoint(local)));
                    }
                }
            }
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

        private sealed class ViewportEnvelope
        {
            public bool HasPoints { get; private set; }
            public float MinimumX { get; private set; } =
                float.PositiveInfinity;
            public float MaximumX { get; private set; } =
                float.NegativeInfinity;
            public float MinimumY { get; private set; } =
                float.PositiveInfinity;
            public float MaximumY { get; private set; } =
                float.NegativeInfinity;
            public float MinimumDepth { get; private set; } =
                float.PositiveInfinity;

            public void Include(Vector3 viewportPoint)
            {
                HasPoints = true;
                MinimumX = Mathf.Min(MinimumX, viewportPoint.x);
                MaximumX = Mathf.Max(MaximumX, viewportPoint.x);
                MinimumY = Mathf.Min(MinimumY, viewportPoint.y);
                MaximumY = Mathf.Max(MaximumY, viewportPoint.y);
                MinimumDepth = Mathf.Min(
                    MinimumDepth,
                    viewportPoint.z);
            }

            public override string ToString()
            {
                return $"x={MinimumX:F3}..{MaximumX:F3}, " +
                       $"y={MinimumY:F3}..{MaximumY:F3}, " +
                       $"zMin={MinimumDepth:F3}";
            }
        }

        private sealed class BartenderTravelProbe
        {
            private const float PositionToleranceSquared = 0.000001f;

            private readonly BarBartenderPresentation bartender;
            private readonly Transform leftFoot;
            private readonly Transform rightFoot;
            private readonly BarInteriorRoot bar;
            private readonly Renderer counterSurface;
            private readonly Renderer[] menuParts;
            private readonly Renderer[] bottleParts;
            private Vector3 previousPosition;
            private Vector3 firstLeftFootLocalPosition;
            private Vector3 firstRightFootLocalPosition;
            private bool hasFootSample;
            private int previousSampleFrame;

            public BartenderTravelProbe(
                BarBartenderPresentation bartenderPresentation,
                Transform leftFootTransform,
                Transform rightFootTransform,
                BarInteriorRoot barRoot)
            {
                bartender = bartenderPresentation;
                leftFoot = leftFootTransform;
                rightFoot = rightFootTransform;
                bar = barRoot;
                counterSurface = bar.Room.Find("Counter Top").GetComponent<Renderer>();
                menuParts = bar.DrinkShop.MenuPresentation.PropRoot.GetComponentsInChildren<Renderer>(true);
                bottleParts = bar.DrinkServiceView.CarriedBottleRoot != null
                    ? bar.DrinkServiceView.CarriedBottleRoot.GetComponentsInChildren<Renderer>(true)
                    : System.Array.Empty<Renderer>();
                BarDrinkVesselView vessel = bar.DrinkServiceView.ActiveVessel;
                float height = vessel.LocalVisualBounds.size.y * vessel.transform.lossyScale.y;
                switch (vessel.Kind)
                {
                    case BarDrinkVesselKind.Pint: Assert.That(height, Is.EqualTo(0.15515f).Within(0.001f)); break;
                    case BarDrinkVesselKind.WineGlass: Assert.That(height, Is.InRange(0.17f, 0.20f)); break;
                    case BarDrinkVesselKind.Snifter: Assert.That(height, Is.InRange(0.11f, 0.14f)); break;
                    case BarDrinkVesselKind.ShotGlass: Assert.That(height, Is.InRange(0.05f, 0.07f)); break;
                }
                previousPosition = bartender.transform.position;
                previousSampleFrame = Time.frameCount;
            }

            public int TranslationFrameCount { get; private set; }
            public int MatchedMovementFrames { get; private set; }
            public float MaximumLocalFootTravel { get; private set; }

            public bool Observe(string step)
            {
                ObserveServiceClearance(step);
                Vector3 movement =
                    bartender.transform.position - previousPosition;
                movement.y = 0f;
                bool translated =
                    movement.sqrMagnitude > PositionToleranceSquared;
                bool fullWalkSelected =
                    bartender.CurrentClipKind ==
                    BarBartenderClipKind.Walk;
                // A fixed-step observation can span two rendered frames,
                // including a final walking frame followed by stationary
                // arrival. Only a consecutive render-frame sample can pair
                // this root delta with this one selected clip.
                if (Time.frameCount == previousSampleFrame + 1)
                {
                    Assert.That(fullWalkSelected, Is.EqualTo(translated),
                        $"During {step}, root translation and the full Walk clip diverged.");
                    if (translated) MatchedMovementFrames++;
                }
                if (translated)
                {
                    TranslationFrameCount++;
                    Vector3 leftLocal =
                        bartender.transform.InverseTransformPoint(
                            leftFoot.position);
                    Vector3 rightLocal =
                        bartender.transform.InverseTransformPoint(
                            rightFoot.position);
                    if (!hasFootSample)
                    {
                        firstLeftFootLocalPosition = leftLocal;
                        firstRightFootLocalPosition = rightLocal;
                        hasFootSample = true;
                    }

                    MaximumLocalFootTravel = Mathf.Max(
                        MaximumLocalFootTravel,
                        Vector3.Distance(
                            firstLeftFootLocalPosition,
                            leftLocal) +
                        Vector3.Distance(
                            firstRightFootLocalPosition,
                            rightLocal));
                }

                previousPosition = bartender.transform.position;
                previousSampleFrame = Time.frameCount;
                return translated;
            }

            private void ObserveServiceClearance(string step)
            {
                BarDrinkServiceView service = bar.DrinkServiceView;
                Bounds menu = default;
                bool hasMenu = false;
                if (bar.DrinkShop.MenuPresentation.IsRestingOnCounter)
                {
                    foreach (Renderer part in menuParts)
                    {
                        if (!part.enabled || !part.gameObject.activeInHierarchy) continue;
                        if (hasMenu) menu.Encapsulate(part.bounds);
                        else { menu = part.bounds; hasMenu = true; }
                    }
                }
                BarDrinkVesselView vessel = service.ActiveVessel;
                if (vessel != null && vessel.gameObject.activeInHierarchy)
                    AssertClear(vessel.GlassRenderer.bounds, "vessel");
                if (service.IsCarriedBottleVisible)
                    foreach (Renderer part in bottleParts)
                        if (part.enabled && part.gameObject.activeInHierarchy)
                            AssertClear(part.bounds, "bottle");

                void AssertClear(Bounds item, string name)
                {
                    Bounds top = counterSurface.bounds;
                    bool overCounter = item.max.x > top.min.x && item.min.x < top.max.x &&
                        item.max.z > top.min.z && item.min.z < top.max.z;
                    if (overCounter)
                        Assert.That(item.min.y, Is.GreaterThanOrEqualTo(top.max.y - 0.005f),
                            $"During {step}, {name} crosses the counter: {item} vs {top}.");
                    if (hasMenu)
                        Assert.That(item.Intersects(menu), Is.False,
                            $"During {step}, {name} crosses the resting menu: {item} vs {menu}.");
                }
            }
        }

        private static IEnumerator LoadBar(
            System.Action<BarInteriorRoot> onReady)
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.BarInterior,
                LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
            {
                yield return null;
            }

            BarInteriorRoot bar = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                bar = Object.FindAnyObjectByType<BarInteriorRoot>();
                if (bar != null && bar.IsInitialized)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(
                bar,
                Is.Not.Null,
                "the bar interior never built its root");
            Assert.That(bar.IsInitialized, Is.True);
            Assert.That(bar.Player, Is.Not.Null);
            onReady(bar);
        }
    }

    [DefaultExecutionOrder(2200)]
    internal sealed class BarTowelHandoffProbe : MonoBehaviour
    {
        private BarInteriorRoot bar;
        private CityPedestrianHandPropRegistry original;
        private BarBartenderTowelState previousState;
        private Vector3 previousHand;
        private bool hasSample, sawStow, sawPocket, sawDraw, sawReturn;
        private System.Exception failure;

        public void Initialize(BarInteriorRoot root)
        {
            bar = root;
            original = root.Bartender.Registry.ServiceTowel;
        }

        private void LateUpdate()
        {
            if (bar == null || failure != null) return;
            try
            {
                BarBartenderTowelPresentation towel = bar.Bartender.TowelPresentation;
                Assert.That(towel, Is.Not.Null);
                Assert.That(towel.Towel, Is.SameAs(original), "The towel was replaced during handoff.");
                if (towel.IsTransitioning)
                {
                    Assert.That(towel.LastGripError, Is.LessThan(.04f),
                        $"Towel hand contact failed during {towel.State} p={towel.Progress:F3}.");
                    if (hasSample && previousState == towel.State)
                        Assert.That(Vector3.Distance(previousHand, towel.HandWorldPosition), Is.LessThan(.10f),
                            "The pocket hand jumped between rendered frames.");
                }
                if (towel.State == BarBartenderTowelState.Stowing && !sawStow && towel.Progress > .35f)
                {
                    sawStow = true;
                    Capture(towel, "20-towel-to-pocket");
                }
                if (sawStow && towel.State == BarBartenderTowelState.Stowed)
                {
                    if (!sawPocket)
                        Assert.That(towel.HandBehindBack, Is.True, "The towel vanished before the hand reached behind the back.");
                    Assert.That(towel.IsTowelVisible, Is.False);
                    sawPocket = true;
                }
                if (sawPocket && towel.State == BarBartenderTowelState.Drawing && !sawDraw && towel.Progress > .35f)
                {
                    sawDraw = true;
                    Assert.That(towel.IsTowelVisible, Is.True);
                    Capture(towel, "21-towel-from-pocket");
                }
                if (sawDraw && towel.State == BarBartenderTowelState.Wiping)
                {
                    Assert.That(towel.IsTowelVisible, Is.True);
                    Assert.That(towel.WipeReady, Is.True);
                    sawReturn = true;
                }
                previousState = towel.State;
                previousHand = towel.HandWorldPosition;
                hasSample = true;
            }
            catch (System.Exception exception) { failure = exception; }
        }

        public void AssertComplete()
        {
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            Assert.That(sawStow && sawPocket && sawDraw && sawReturn, Is.True,
                $"Missing towel handoff: stow={sawStow}, pocket={sawPocket}, draw={sawDraw}, wipe={sawReturn}.");
        }

        internal static void Capture(BarBartenderTowelPresentation towel, string name)
        {
            Camera camera = Camera.main;
            Pose originalPose = new Pose(camera.transform.position, camera.transform.rotation);
            float originalFov = camera.fieldOfView;
            try
            {
                Vector3 target = towel.PocketWorldPosition + new Vector3(0f, .28f, -.25f);
                Vector3 from = target + new Vector3(1.15f, .4f, -.35f);
                camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                camera.fieldOfView = 65f;
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.BarInterior, name);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(originalPose.position, originalPose.rotation);
                camera.fieldOfView = originalFov;
            }
        }
    }
}
