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
    public sealed class BarInteriorSpawnPlayModeTests
    {
        private const float TimeoutSeconds = 20f;

        //  Long enough to outlast the arrival shot (1.35 s), which owns
        //  the camera while it plays and hands it back to the follow.
        private const float SettleSeconds = 2f;

        //  The chase camera wants 2.2 m indoors and gives that up to
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
            GameSessionState.EnterBar("bar-spawn-test");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
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
            GameSessionState.ClearRoute();
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

            SkinnedMeshRenderer towel = null;
            for (int index = 0;
                 index < bartender.Registry.RendererBindings.Count;
                 index++)
            {
                BarBartenderRendererBinding binding =
                    bartender.Registry.RendererBindings[index];
                if (binding != null &&
                    binding.RendererName == "ACC_ServiceTowel")
                {
                    towel = binding.Renderer as SkinnedMeshRenderer;
                    break;
                }
            }

            Assert.That(towel, Is.Not.Null);
            Assert.That(towel.enabled, Is.True);
            Assert.That(
                bartender.Registry.TryGetClip(
                    BarBartenderClipKind.Wipe,
                    out AnimationClip wipeClip,
                    out bool wipeLoops),
                Is.True);
            Assert.That(wipeLoops, Is.True);
            var scratch = new Mesh();
            try
            {
                AdvanceWipeToPhase(bartender, wipeClip.length, 0.12f);
                Vector3 first = MeasureWipeTowel(
                    towel,
                    scratch,
                    counterRenderer.bounds);
                AdvanceWipeToPhase(bartender, wipeClip.length, 0.28f);
                Vector3 second = MeasureWipeTowel(
                    towel,
                    scratch,
                    counterRenderer.bounds);
                AdvanceWipeToPhase(bartender, wipeClip.length, 0.44f);
                Vector3 third = MeasureWipeTowel(
                    towel,
                    scratch,
                    counterRenderer.bounds);

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
            finally
            {
                Object.DestroyImmediate(scratch);
            }
        }

        [UnityTest]
        public IEnumerator
            BeerService_BartenderWalksAndPlacesMugWithoutThrowingIt()
        {
            GameSessionState.ResetEconomyState();
            GameSessionState.ResetDrinkingState();
            BarInteriorRoot bar = null;
            yield return LoadBar(result => bar = result);
            bar.ArrivalPresentation.Skip();
            yield return null;

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
                yield return null;
            }

            Assert.That(station.Seat.IsSeated, Is.True);
            Assert.That(shop.ActiveServiceMirrored, Is.True,
                "The production regression must exercise the mirrored " +
                "rightmost service route.");
            timeout = Time.realtimeSinceStartup + 6f;
            while (!shop.IsBrowsing &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            Assert.That(shop.IsBrowsing, Is.True,
                "The real bartender never completed menu delivery.");
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
                rightFoot);
            timeout = Time.realtimeSinceStartup + 8f;
            while (shop.Phase ==
                       BarDrinkServicePhase.BeerWalkToTap &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
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
                yield return null;
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
                yield return null;
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

            float previousCounterDistance = Vector3.Distance(
                vessel.transform.position,
                counterPose.position);
            int placementContactFrames = 0;
            timeout = Time.realtimeSinceStartup + 3f;
            while (shop.Phase ==
                       BarDrinkServicePhase.BeerGlassPlacement &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
                travel.Observe("place beer on counter");
                if (shop.Phase !=
                    BarDrinkServicePhase.BeerGlassPlacement)
                {
                    break;
                }

                float counterDistance = Vector3.Distance(
                    vessel.transform.position,
                    counterPose.position);
                Assert.That(
                    counterDistance,
                    Is.LessThanOrEqualTo(
                        previousCounterDistance + 0.002f),
                    "The mug moved away from the counter like a throw.");
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
                        $"{shop.Timeline.CurrentFrame.PhaseProgress:F2}.");
                }

                previousCounterDistance = counterDistance;
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
            Assert.That(
                travel.TranslationFrameCount,
                Is.GreaterThan(10),
                "The fixture never exercised real bartender travel.");
            Assert.That(
                travel.MaximumLocalFootTravel,
                Is.GreaterThan(0.05f),
                "Walk was selected, but the bartender's local foot pose " +
                "stayed static while his root translated.");
        }

        [UnityTest]
        public IEnumerator
            RedWineService_FetchesPoursCarriesAndWaitsForExplicitDrink()
        {
            GameSessionState.ResetEconomyState();
            GameSessionState.ResetDrinkingState();
            GameSessionState.UpdateDrinkingProgress(80, DrinkId.None, 0);
            BarInteriorRoot bar = null;
            yield return LoadBar(result => bar = result);
            bar.ArrivalPresentation.Skip();
            yield return null;

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
                    DrinkId.RedWine,
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
                yield return null;
            }

            timeout = Time.realtimeSinceStartup + 6f;
            while (!shop.IsBrowsing &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            Assert.That(shop.IsBrowsing, Is.True);
            Assert.That(Time.timeScale, Is.InRange(0.94f, 0.96f),
                "The complete production route must retain hand contact in slow motion.");
            Assert.That(
                shop.Select(FindOfferIndex(shop, DrinkId.RedWine)),
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
            Assert.That(shop.SelectedOffer.DrinkId, Is.EqualTo(DrinkId.RedWine));
            Assert.That(shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.BottleWalkToShelf));
            Assert.That(service.SelectedBottle, Is.SameAs(shelfBottle));
            Assert.That(service.ActiveVessel.Kind,
                Is.EqualTo(BarDrinkVesselKind.WineGlass));
            Assert.That(service.ActiveVessel.gameObject.activeSelf, Is.False);
            Assert.That(service.IsCarriedBottleVisible, Is.False);
            Assert.That(GameSessionState.CashBalance,
                Is.EqualTo(cashBefore - shop.SelectedOffer.Price));
            Assert.That(GameSessionState.IntoxicationLevel,
                Is.EqualTo(intoxicationBefore));
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(drinksBefore));

            var travel = new BartenderTravelProbe(
                bartender, leftFoot, rightFoot);
            int shelfTravelFrames = 0;
            timeout = Time.realtimeSinceStartup + 8f;
            while (shop.Phase ==
                       BarDrinkServicePhase.BottleWalkToShelf &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
                if (travel.Observe("walk to selected wine shelf"))
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
            Vector3 previousBottlePosition =
                service.CarriedBottleRoot.position;
            timeout = Time.realtimeSinceStartup + 3f;
            while (shop.Phase == BarDrinkServicePhase.BottlePickup &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
                travel.Observe("pick up selected wine bottle");
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
                yield return null;
                if (travel.Observe("carry wine bottle to pour point"))
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

            Assert.That(pourTravelFrames, Is.GreaterThan(2));
            Assert.That(shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.VesselPlacement));
            timeout = Time.realtimeSinceStartup + 4f;
            while ((shop.Phase == BarDrinkServicePhase.VesselPlacement ||
                    (shop.Phase == BarDrinkServicePhase.Pouring &&
                     shop.Timeline.PhaseProgress < 0.40f)) &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
                travel.Observe("place wine glass and begin pour");
            }

            // The bottle is seated on the anatomical hand by a post-IK
            // LateUpdate pass. FixedUpdate resumes before the next service
            // Update, so this samples the pose that was actually rendered.
            yield return new WaitForFixedUpdate();
            BarDrinkVesselView vessel = service.ActiveVessel;
            Assert.That(shop.Phase, Is.EqualTo(BarDrinkServicePhase.Pouring));
            Assert.That(vessel.Kind, Is.EqualTo(BarDrinkVesselKind.WineGlass));
            Assert.That(vessel.FillProgress, Is.InRange(0.05f, 0.95f));
            Assert.That(service.IsStreamVisible, Is.True);
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
                yield return null;
                travel.Observe("pour red wine");
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
                yield return null;
                if (travel.Observe("carry full wine glass to guest"))
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
                    "The full wine glass must travel in the left hand.");
                Assert.That(Vector3.Angle(
                        vessel.OpeningDirection,
                        service.transform.up),
                    Is.LessThan(2f));
            }

            Assert.That(guestTravelFrames, Is.GreaterThan(2));
            Assert.That(shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.BottleVesselPlacement));
            float previousDistance = Vector3.Distance(
                vessel.transform.position, counterPose.position);
            int placementContactFrames = 0;
            timeout = Time.realtimeSinceStartup + 3f;
            while (shop.Phase ==
                       BarDrinkServicePhase.BottleVesselPlacement &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
                travel.Observe("place wine glass on counter");
                if (shop.Phase !=
                    BarDrinkServicePhase.BottleVesselPlacement)
                {
                    break;
                }

                float distance = Vector3.Distance(
                    vessel.transform.position, counterPose.position);
                Assert.That(distance,
                    Is.LessThanOrEqualTo(previousDistance + 0.002f));
                Assert.That(Vector3.Angle(
                        vessel.OpeningDirection,
                        service.transform.up),
                    Is.LessThan(2f));
                if (distance > 0.03f)
                {
                    placementContactFrames++;
                    Assert.That(service.ResolveActiveVesselGripError(
                            bartender.Registry.VesselGripAnchor),
                        Is.LessThan(0.04f));
                }

                previousDistance = distance;
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
                yield return null;
                if (travel.Observe("return wine bottle to shelf"))
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
                yield return null;
                travel.Observe("set wine bottle back on shelf");
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
            float previousTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 20f;
                timeout = Time.realtimeSinceStartup + 4f;
                while (shop.IsServing &&
                       Time.realtimeSinceStartup < timeout)
                {
                    yield return null;
                }
            }
            finally
            {
                Time.timeScale = previousTimeScale;
            }

            Assert.That(shop.IsServing, Is.False);
            Assert.That(shop.Phase,
                Is.EqualTo(BarDrinkServicePhase.EmptyOnCounter));
            Assert.That(GameSessionState.IntoxicationLevel,
                Is.EqualTo(intoxicationBeforeDrink +
                    DrinkRules.GetIntoxicationGain(DrinkId.RedWine)));
            Assert.That(GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksBefore + 1));
            Assert.That(GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.RedWine));
            int appliedIntoxication = GameSessionState.IntoxicationLevel;
            Assert.That(shop.BeginServedDrink(), Is.False);
            shop.AdvancePresentation(30f);
            yield return null;
            Assert.That(GameSessionState.IntoxicationLevel,
                Is.LessThanOrEqualTo(appliedIntoxication));
            Assert.That(GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksBefore + 1));
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
            Assert.That(bar.CounterStations, Has.Count.EqualTo(4));
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

            Vector3 reportedBlockedPosition = bar.Room.TransformPoint(
                new Vector3(-1.15f, 0f, 3.93f));
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
            SkinnedMeshRenderer towel,
            Mesh scratch,
            Bounds counter)
        {
            scratch.Clear();
            towel.BakeMesh(scratch, true);
            Vector3[] vertices = scratch.vertices;
            Assert.That(vertices, Is.Not.Empty);
            Matrix4x4 localToWorld = towel.localToWorldMatrix;
            float lowest = float.PositiveInfinity;
            Vector3 centre = Vector3.zero;
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 world = localToWorld.MultiplyPoint3x4(vertices[index]);
                lowest = Mathf.Min(lowest, world.y);
                centre += world;
            }

            centre /= vertices.Length;
            float gap = lowest - counter.max.y;
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

        private static void AssertVesselRestsOnPhysicalCounter(
            BarInteriorRoot bar,
            BarDrinkServiceView service,
            BarDrinkVesselView vessel)
        {
            Assert.That(
                bar.Layout.TryGetFurniture(
                    BarInteriorFurnitureKind.Counter,
                    out BarInteriorFurnitureFootprint counter),
                Is.True);
            Bounds worldBounds = vessel.GlassRenderer.bounds;
            Vector3 localMinimum = bar.Room.InverseTransformPoint(
                worldBounds.min);
            Vector3 localMaximum = bar.Room.InverseTransformPoint(
                worldBounds.max);
            Assert.That(localMinimum.x,
                Is.GreaterThanOrEqualTo(counter.Bounds.xMin));
            Assert.That(localMaximum.x,
                Is.LessThanOrEqualTo(counter.Bounds.xMax));
            Assert.That(localMinimum.z,
                Is.GreaterThanOrEqualTo(counter.Bounds.yMin));
            Assert.That(localMaximum.z,
                Is.LessThanOrEqualTo(counter.Bounds.yMax));

            float counterTop = bar.Layout.CounterPosition.y +
                counter.Height * 0.5f +
                BarPatronWorldBuilder.CounterTopBuildUp;
            float vesselBottom = service.transform.InverseTransformPoint(
                worldBounds.min).y;
            Assert.That(
                vesselBottom,
                Is.EqualTo(counterTop).Within(0.02f),
                "The final mug pose must rest on the physical counter top.");
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
            private Vector3 previousPosition;
            private Vector3 firstLeftFootLocalPosition;
            private Vector3 firstRightFootLocalPosition;
            private bool hasFootSample;

            public BartenderTravelProbe(
                BarBartenderPresentation bartenderPresentation,
                Transform leftFootTransform,
                Transform rightFootTransform)
            {
                bartender = bartenderPresentation;
                leftFoot = leftFootTransform;
                rightFoot = rightFootTransform;
                previousPosition = bartender.transform.position;
            }

            public int TranslationFrameCount { get; private set; }
            public float MaximumLocalFootTravel { get; private set; }

            public bool Observe(string step)
            {
                Vector3 movement =
                    bartender.transform.position - previousPosition;
                movement.y = 0f;
                bool translated =
                    movement.sqrMagnitude > PositionToleranceSquared;
                bool fullWalkSelected =
                    bartender.CurrentClipKind ==
                    BarBartenderClipKind.Walk;
                Assert.That(
                    fullWalkSelected,
                    Is.EqualTo(translated),
                    $"During {step}, root translation and the full Walk " +
                    "clip diverged.");
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
                return translated;
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
}
