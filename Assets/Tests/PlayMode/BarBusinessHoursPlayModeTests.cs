using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using BarPromenade.Runtime.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    [PrebuildSetup(typeof(CompactBarAssetsSetup))]
    public sealed class BarBusinessHoursPlayModeTests
    {
        private BarInteriorRoot bar;
        private PlayerAnimatedInteractionController playerAction;
        private IDisposable heroHide;
        private float previousCaptureDelta;

        [SetUp]
        public void SetUp()
        {
            previousCaptureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            GameSessionState.BeginNewGame("bar-hours-test");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            heroHide?.Dispose();
            heroHide = null;
            if (playerAction != null) playerAction.ReleaseExternalClock(this);
            if (bar != null)
                foreach (BarCounterStation station in bar.GetComponentsInChildren<BarCounterStation>(true)) station.Seat?.Cancel();
            yield return AreaCaptureFixture.ReleaseBarService();
            Scene scene = SceneManager.GetSceneByName(SceneIds.BarInterior);
            if (scene.IsValid() && scene.isLoaded)
            {
                Scene cleanup = SceneManager.CreateScene("Bar Hours Cleanup");
                SceneManager.SetActiveScene(cleanup);
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            GameSessionState.BeginNewGame("bar-hours-cleanup");
            Time.captureDeltaTime = previousCaptureDelta;
            bar = null;
            playerAction = null;
            yield return null;
        }

        [Test]
        public void Schedule_BoundariesStableAttendanceAndTimeBeforeLocation()
        {
            Assert.That(BarBusinessHours.IsOpen(119), Is.True);
            Assert.That(BarBusinessHours.IsOpen(120), Is.False);
            Assert.That(BarBusinessHours.IsOpen(719), Is.False);
            Assert.That(BarBusinessHours.IsOpen(720), Is.True);
            Assert.That(BarBusinessHours.IsOpen(1439), Is.True);
            Assert.That(BarBusinessHours.IsOpen(-1) || BarBusinessHours.IsOpen(1440), Is.False);
            Assert.That(BarBusinessHours.TargetGuestCount(0, 120, 17), Is.Zero);
            Assert.That(BarBusinessHours.TargetGuestCount(0, 719, 17), Is.Zero);
            int[,] periods = { { 12, 15, 3, 4 }, { 16, 18, 4, 6 }, { 19, 22, 6, 8 },
                { 23, 23, 4, 6 }, { 0, 0, 4, 6 }, { 1, 1, 3, 4 } };
            for (int period = 0; period < periods.GetLength(0); period++)
            for (int hour = periods[period, 0]; hour <= periods[period, 1]; hour++)
            for (int seed = 0; seed < 24; seed++)
            {
                int count = BarBusinessHours.TargetGuestCount(2, hour * 60, seed);
                Assert.That(count, Is.InRange(periods[period, 2], periods[period, 3]));
                Assert.That(BarBusinessHours.TargetGuestCount(2, hour * 60 + 59, seed), Is.EqualTo(count),
                    "A quick return in the same hour retains the attendance target.");
            }

            var menu = new StartMenuModel();
            menu.Open();
            Assert.That(menu.OpenDebugMenu(), Is.True);
            Assert.That(menu.Confirm(), Is.EqualTo(StartMenuAction.ChooseStartTime));
            Assert.That(menu.IsChoosingStartTime, Is.True);
            Assert.That(menu.IsChoosingLocation, Is.False);
            Assert.That(menu.SelectedStartMinuteOfDay, Is.EqualTo(7 * 60 + 40));
            Assert.That(menu.SelectStartTime(120), Is.True);
            menu.SelectStartTimeOption(StartTimeOption.Continue);
            Assert.That(menu.Confirm(), Is.EqualTo(StartMenuAction.ChooseLocation));
            Assert.That(menu.SelectLocation(NewGameLocation.Bar), Is.True);
            Assert.That(menu.Confirm(), Is.EqualTo(StartMenuAction.LocationUnavailable));
            Assert.That(menu.IsCommitted, Is.False);
            Assert.That(menu.AvailabilityMessageKey, Is.EqualTo("opening.location.bar_closed"));
            Assert.That(NewGameLocationCatalog.IsAvailableAt(NewGameLocation.Bar, 120), Is.False);
            string activeBar = GameSessionState.ActiveBarId;
            double initialTime = GameSessionState.GameTimeOfDayMinutes;
            Assert.That(NewGameStartService.TryStart(NewGameLocation.Bar, 120), Is.False);
            Assert.That(GameSessionState.ActiveBarId, Is.EqualTo(activeBar));
            Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(initialTime));
            Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
            Assert.That(menu.ReturnToStartTime(), Is.True);
            Assert.That(menu.SelectedStartMinuteOfDay, Is.EqualTo(120));
            Assert.That(menu.SelectStartTime(18 * 60), Is.True);
            menu.SelectStartTimeOption(StartTimeOption.Continue);
            Assert.That(menu.Confirm(), Is.EqualTo(StartMenuAction.ChooseLocation));
            menu.SelectLocation(NewGameLocation.Bar);
            Assert.That(NewGameLocationCatalog.IsAvailableAt(NewGameLocation.Bar, 18 * 60), Is.True);
            Assert.That(menu.Confirm(), Is.EqualTo(StartMenuAction.NewGame));
            Assert.That(menu.IsCommitted, Is.True);
        }

        [UnityTest]
        public IEnumerator ActualBar_FillsAndEmptiesWithClockAndFinishesAcceptedServiceAfterClosing()
        {
            Assert.That(Application.isBatchMode, Is.False,
                "This focused scenario captures the actual Game view; run without -batchmode or -nographics.");
            FocusGameView();
            MainMenuRoot.ClearLegacyOpeningRequest();
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            StartMenuRoot menu = Object.FindAnyObjectByType<StartMenuRoot>();
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.OpenDebugMenu(), Is.True);
            Assert.That(menu.ConfirmSelection(), Is.True);
            Assert.That(menu.IsChoosingStartTime, Is.True);
            Assert.That(menu.IsChoosingLocation, Is.False);
            Assert.That(menu.SelectedStartMinuteOfDay, Is.EqualTo(7 * 60 + 40));
            Assert.That(menu.SelectStartTime(120), Is.True);
            yield return Capture("bar-hours-start-time.png");
            menu.SelectStartTimeOption(StartTimeOption.Continue);
            Assert.That(menu.ConfirmSelection(), Is.True);
            Assert.That(menu.SelectLocation(NewGameLocation.Bar), Is.True);
            string sessionBarBefore = GameSessionState.ActiveBarId;
            double sessionTimeBefore = GameSessionState.GameTimeOfDayMinutes;
            int sessionCashBefore = GameSessionState.CashBalance;
            Assert.That(menu.ConfirmSelection(), Is.False);
            Assert.That(menu.AvailabilityMessageKey, Is.EqualTo("opening.location.bar_closed"));
            Assert.That(menu.IsChoosingLocation, Is.True);
            Assert.That(menu.IsStartingNewGame, Is.False);
            Assert.That(NewGameStartService.TryStart(NewGameLocation.Bar, 120), Is.False);
            Assert.That(GameSessionState.ActiveBarId, Is.EqualTo(sessionBarBefore));
            Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(sessionTimeBefore));
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(sessionCashBefore));
            Assert.That(GameSessionState.IsGameTimeRunning, Is.False);
            Assert.That(SceneTransitionService.IsTransitioning, Is.False);
            yield return Capture("bar-hours-location-closed.png");
            Assert.That(menu.ReturnToStartTime(), Is.True);
            Assert.That(menu.SelectedStartMinuteOfDay, Is.EqualTo(120));
            Assert.That(menu.SelectStartTime(12 * 60), Is.True);
            menu.SelectStartTimeOption(StartTimeOption.Continue);
            Assert.That(menu.ConfirmSelection(), Is.True);
            menu.SelectLocation(NewGameLocation.Bar);
            Assert.That(menu.ConfirmSelection(), Is.True, "The actual selected noon starts a real available bar.");
            float deadline = Time.realtimeSinceStartup + 20f;
            do
            {
                bar = Object.FindAnyObjectByType<BarInteriorRoot>();
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The actual bar did not initialize.");
                if (bar == null || !bar.IsInitialized || SceneTransitionService.IsTransitioning) yield return null;
            } while (bar == null || !bar.IsInitialized || SceneTransitionService.IsTransitioning);
            Assert.That(GameSessionState.GameMinuteOfDay, Is.EqualTo(12 * 60));
            bar.ArrivalPresentation?.Skip();
            bar.Closing.AutoAdvance = false;
            Assert.That(bar.Crowd, Is.Not.Null.And.Property("IsInitialized").True);
            bar.Crowd.AutoAdvance = false;
            bar.Conversation.AutoAdvance = false;
            // Freeze unrelated sip/voice selection. Crowd movement still drives each real rig itself.
            foreach (BarPatronAnimator animator in bar.GetComponentsInChildren<BarPatronAnimator>(true)) animator.enabled = false;
            bar.Conversation.enabled = false;
            bar.Conversation.enabled = true;
            Camera camera = Camera.main;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            follow.enabled = false;
            ParkHeroForCrowd();
            heroHide = bar.Player.PresentationVisibility.AcquireHidden(this);
            StageRoomCamera(camera);
            yield return null;
            Assert.That(bar.Patrons.Count, Is.InRange(3, 4));
            AssertSettledCast();
            yield return Capture("bar-hours-afternoon.png");

            AdvanceClockTo(19 * 60);
            bar.Crowd.Advance(0f);
            int eveningTarget = bar.Crowd.TargetCount;
            Assert.That(eveningTarget, Is.InRange(6, 8));
            int beforeArrival = bar.Patrons.Count;
            bar.Crowd.Advance(BarCrowdController.ChangeIntervalSeconds + .1f);
            Assert.That(bar.Crowd.HasGuestInTransit, Is.True);
            Assert.That(bar.Patrons.Count, Is.EqualTo(beforeArrival), "The walker cannot speak or reserve a settled guest's cup yet.");
            yield return AdvanceCrowdFrames(55);
            yield return Capture("bar-hours-arrival.png");
            yield return DriveCrowdUntil(() => bar.Patrons.Count == eveningTarget && !bar.Crowd.HasGuestInTransit,
                "The evening arrivals did not physically reach their places.");
            AssertSettledCast();
            yield return Capture("bar-hours-evening.png");

            // A real complete pair is available, but closing cannot reserve its rest for another exchange.
            foreach (BarPatron patron in bar.Patrons)
            {
                BarPatronDrinkTimeline clock = patron.Drinking.Timeline;
                for (int phase = 0; phase < 4 && clock.Phase != BarPatronDrinkPhase.Rest; phase++)
                    clock.Advance(Mathf.Max(.001f, clock.PhaseDuration - clock.PhaseElapsed + .0001f));
                Assert.That(patron.Drinking.CanConverse, Is.True);
            }
            bool hasPair = false;
            for (int group = 0; group < BarConversationCatalog.ActiveGroupCount; group++)
                hasPair |= bar.Conversation.Speaker(group * 2) != null && bar.Conversation.Speaker(group * 2 + 1) != null;
            Assert.That(hasPair, Is.True, "The peak cast must include a complete eligible pair.");
            AdvanceClockTo(2 * 60);
            int linesBeforeClosing = bar.Conversation.StartedLineCount;
            for (int step = 0; step < 200; step++)
            {
                bar.Conversation.Advance(.1f);
                for (int group = 0; group < BarConversationCatalog.ActiveGroupCount; group++)
                    Assert.That(bar.Conversation.GetSchedule((BarConversationKind)group).Current.HasExchange, Is.False,
                        "An idle pair cannot begin another exchange after closing and hold up departure.");
            }
            Assert.That(bar.Conversation.StartedLineCount, Is.EqualTo(linesBeforeClosing));

            AdvanceClockTo(60);
            bar.Crowd.Advance(0f);
            int lateTarget = bar.Crowd.TargetCount;
            Assert.That(lateTarget, Is.InRange(3, 4));
            // Match production Update ordering: the shared graph has written
            // its base before the drinker's final LateUpdate contacts.
            foreach (BarPatron patron in bar.Patrons) patron.Presentation.Advance(0f, false, true);
            bar.Crowd.Advance(BarCrowdController.ChangeIntervalSeconds + .1f);
            Assert.That(bar.Crowd.HasGuestInTransit, Is.True);
            Assert.That(bar.Patrons.Count, Is.EqualTo(eveningTarget - 1));
            yield return AdvanceCrowdFrames(25);
            Assert.That(bar.Crowd.TryGetAbandonedDrink(bar.Crowd.TransitPatronId, out BarAbandonedDrink leftDrink),
                Is.True, "A departing guest leaves the actual drink at the reserved place.");
            Assert.That(leftDrink.DrinkProp.Root, Is.Not.Null);
            Assert.That(leftDrink.DrinkProp.Root.IsChildOf(bar.Room), Is.True);
            Assert.That(leftDrink.DrinkProp.Root.gameObject.activeInHierarchy, Is.True);
            yield return Capture("bar-hours-departure.png");
            yield return DriveCrowdUntil(() => bar.Patrons.Count == lateTarget && !bar.Crowd.HasGuestInTransit,
                "The late guests did not leave through the door.");
            AssertSettledCast();

            // A normal public order enters the real shared gesture and service queue.
            heroHide.Dispose();
            heroHide = null;
            BarDrinkShopController shop = bar.DrinkShop;
            BarCounterStation station = bar.CounterStations[bar.CounterStations.Count - 1];
            bar.Player.Motor.Teleport(station.Seat.Plan.EntryPose.RootPosition);
            bar.Player.GameObject.transform.rotation = station.Seat.Plan.EntryPose.RootRotation;
            Physics.SyncTransforms();
            follow.enabled = true;
            playerAction = station.Seat.Controller;
            Assert.That(playerAction.BeginExternalClock(this), Is.True);
            Assert.That(station.CanInteract(bar.Player.Interactor), Is.True);
            station.Interact(bar.Player.Interactor);
            yield return DriveServiceUntil(() => station.Seat.IsSeated && shop.IsBrowsing,
                "The real seat and menu did not become ready.");
            Assert.That(shop.Select(3), Is.True);
            int cashBefore = GameSessionState.CashBalance;
            int price = shop.SelectedOffer.Price;
            Assert.That(shop.ConfirmSelection(), Is.True);
            yield return DriveServiceUntil(() => shop.PurchaseCommitted, "The public order gesture did not accept its order.");
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(cashBefore - price));
            int cashAfter = GameSessionState.CashBalance;
            AdvanceClockTo(2 * 60);
            Assert.That(shop.IsAcceptingOrders, Is.False);
            Assert.That(shop.ConfirmSelection(), Is.False);
            Assert.That(shop.BeginRepeatDrink(), Is.False);
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(cashAfter), "Closing rejects requests before payment.");
            yield return DriveServiceUntil(() => shop.CanDrinkServedVessel,
                "An already accepted order must still be delivered after closing.");
            Assert.That(shop.HeroServiceVessel, Is.Not.Null);
            Assert.That(shop.HeroServiceVessel.FillProgress, Is.GreaterThan(.99f));
            Assert.That(shop.PendingServiceOrderCount, Is.Zero);
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(cashAfter));
            shop.Close();
            station.Seat.Cancel();
            playerAction.ReleaseExternalClock(this);
            playerAction = null;
            yield return null;
            follow.enabled = false;
            ParkHeroForCrowd();
            heroHide = bar.Player.PresentationVisibility.AcquireHidden(this);
            StageRoomCamera(camera);

            // Actual NPCs leave; no closed bar is recreated or teleported away.
            bar.Crowd.Advance(0f);
            Assert.That(bar.Crowd.TargetCount, Is.Zero);
            foreach (BarPatron patron in bar.Patrons)
            {
                if (patron.Refill == null) continue;
                patron.Refill.Advance(2000f);
                Assert.That(shop.QueuePatronRefill(patron.Refill), Is.False);
            }
            yield return DriveCrowdUntil(() => bar.Patrons.Count == 0 && !bar.Crowd.HasGuestInTransit,
                "Closing must let all actual guests finish and leave.");
            AssertSettledCast();
            Assert.That(bar.Conversation.ActiveLineCount, Is.Zero);
            yield return Capture("bar-hours-closed.png");

            BarExit exit = bar.GetComponentInChildren<BarExit>(true);
            Assert.That(exit, Is.Not.Null);
            Assert.That(exit.CanInteract(bar.Player.Interactor), Is.True, "The exit remains available after daily closing.");
            // Reuse the production door target: only its entrance adapter is temporary.
            BarEntrance entrance = exit.gameObject.AddComponent<BarEntrance>();
            entrance.Configure("closed-entrance-test", BarActivityKind.Cocktail, CityDistrictKind.Residential, Vector3.zero);
            string activeBar = GameSessionState.ActiveBarId;
            bool inputBefore = bar.Player.Motor.InputEnabled;
            Assert.That(entrance.CanInteract(bar.Player.Interactor), Is.True);
            Assert.That(entrance.PromptKey, Is.EqualTo(BarEntrance.ClosedFeedbackKey));
            entrance.Interact(bar.Player.Interactor);
            yield return null;
            Assert.That(bar.Player.Interactor.PromptView.IsFeedbackVisible, Is.True);
            Assert.That(SceneTransitionService.IsTransitioning, Is.False);
            Assert.That(GameSessionState.ActiveBarId, Is.EqualTo(activeBar));
            Assert.That(bar.Player.Motor.InputEnabled, Is.EqualTo(inputBefore));
            Assert.That(bar.Player.GameObject.GetComponent<PlayerDoorActionController>().IsPlaying, Is.False);
            Assert.That(shop.Open(bar.Player.Interactor), Is.False);
            Assert.That(shop.FeedbackKey, Is.EqualTo(BarEntrance.ClosedFeedbackKey));
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(cashAfter));
            Assert.That(exit.CanInteract(bar.Player.Interactor), Is.True);
            yield return AssertFacadeHoursRestoreActualPanes();
        }

        [UnityTest]
        public IEnumerator LastGuest_ClosingPreservesMenuSipAndWalksToDoor()
        {
            Assert.That(Application.isBatchMode, Is.False,
                "This focused closing scenario reviews the actual Game view.");
            FocusGameView();
            Assert.That(GameSessionState.TryStartGameTimeAt(60), Is.True);
            GameSessionState.EnterBar("last-guest-closing", BarActivityKind.Cocktail);
            yield return SceneManager.LoadSceneAsync(SceneIds.BarInterior, LoadSceneMode.Single);
            float deadline = Time.realtimeSinceStartup + 20f;
            do
            {
                bar = Object.FindAnyObjectByType<BarInteriorRoot>();
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The real bar did not initialize.");
                if (bar == null || !bar.IsInitialized) yield return null;
            } while (bar == null || !bar.IsInitialized);
            bar.ArrivalPresentation?.Skip();
            bar.Closing.AutoAdvance = false;
            bar.Crowd.AutoAdvance = false;
            bar.Conversation.AutoAdvance = false;
            foreach (BarPatronAnimator animator in bar.GetComponentsInChildren<BarPatronAnimator>(true))
                animator.enabled = false;
            Camera camera = Camera.main;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            BarDrinkShopController shop = bar.DrinkShop;
            BarCounterStation station = bar.CounterStations[bar.CounterStations.Count - 1];
            bar.Player.Motor.Teleport(station.Seat.Plan.EntryPose.RootPosition);
            bar.Player.GameObject.transform.rotation = station.Seat.Plan.EntryPose.RootRotation;
            Physics.SyncTransforms();
            playerAction = station.Seat.Controller;
            Assert.That(playerAction.BeginExternalClock(this), Is.True);
            station.Interact(bar.Player.Interactor);
            yield return DriveServiceUntil(() => station.Seat.IsSeated && shop.IsBrowsing,
                "The ordinary physical seat and menu must become ready.");
            Assert.That(shop.Select(1), Is.True);
            Assert.That(shop.SelectedOffer.DrinkId, Is.EqualTo(DrinkId.RedWine));
            deadline = Time.realtimeSinceStartup + 3f;
            while (!station.SeatView.IsMenuFocusComplete)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                yield return null;
            }
            Pose menuCamera = new Pose(camera.transform.position, camera.transform.rotation);
            float menuFov = camera.fieldOfView;
            int menuSelection = shop.SelectedIndex;
            CounterMenuState menuState = shop.MenuState;
            AdvanceClockTo(90);
            yield return DriveClosingUntil(() => bar.Closing.WarningIssued,
                "The bartender must warn at 01:30 even while the physical menu is open.");
            BarClosingCameraFocus focus = bar.Closing.GetComponent<BarClosingCameraFocus>();
            Assert.That(focus, Is.Not.Null);
            Assert.That(focus.IsActive, Is.True);
            Assert.That(bar.Bartender.GetComponent<BarBartenderServiceChoreography>().IsReadyForClosingAnnouncement,
                Is.True, "Speech and camera wait for the dedicated hall-address pose.");
            deadline = Time.realtimeSinceStartup + 10f;
            while (bar.Conversation.LastLineKey != BarConversationController.ClosingWarningKey)
            {
                bar.Conversation.Advance(1f / 60f);
                bar.Closing.Advance(1f / 60f);
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The shared warning voice did not reveal its line.");
                yield return null;
            }
            for (int frame = 0; frame < 30; frame++)
            {
                bar.Conversation.Advance(1f / 60f);
                bar.Closing.Advance(1f / 60f);
                yield return null;
            }
            Assert.That(focus.IsActive, Is.True);
            Vector3 head = bar.Conversation.Head(BarConversationController.BartenderRole).position;
            Vector3 inFrame = camera.WorldToViewportPoint(head);
            Assert.That(inFrame.z, Is.GreaterThan(0f));
            Assert.That(inFrame.x, Is.InRange(.15f, .85f));
            Assert.That(inFrame.y, Is.InRange(.15f, .85f));
            yield return Capture("bar-hours-warning-bartender.png");
            yield return AssertClosingReadingHold(BarConversationController.ClosingWarningKey, focus);
            yield return DriveClosingUntil(() => !focus.IsActive, "The warning must return the borrowed menu camera.");
            Assert.That(Vector3.Distance(camera.transform.position, menuCamera.position), Is.LessThan(.002f));
            Assert.That(Quaternion.Angle(camera.transform.rotation, menuCamera.rotation), Is.LessThan(.05f));
            Assert.That(camera.fieldOfView, Is.EqualTo(menuFov).Within(.01f));
            Assert.That(shop.SelectedIndex, Is.EqualTo(menuSelection));
            Assert.That(shop.MenuState, Is.EqualTo(menuState));
            Assert.That(bar.Closing.FinalRequestIssued, Is.False);
            Assert.That(bar.Closing.IsDepartureRequested, Is.False);

            int cashBefore = GameSessionState.CashBalance;
            int price = shop.SelectedOffer.Price;
            Assert.That(shop.ConfirmSelection(), Is.True);
            yield return DriveServiceUntil(() => shop.PurchaseCommitted, "The visible gesture must accept the paid wine order.");
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(cashBefore - price));
            int cashAfter = GameSessionState.CashBalance;
            AdvanceClockTo(120);
            bar.Closing.Advance(0f);
            Assert.That(shop.IsAcceptingOrders, Is.False);
            bar.Closing.Advance(61f);
            Assert.That(bar.Closing.FinalRequestIssued, Is.False,
                "An accepted order must be delivered before the time to drink it can expire.");
            yield return DriveServiceUntil(() => shop.CanDrinkServedVessel, "The paid wine must still be served after closing.");
            BarDrinkVesselView vessel = shop.HeroServiceVessel;
            Assert.That(vessel.FillProgress, Is.GreaterThan(.99f));
            Assert.That(bar.Closing.GraceRemainingSeconds, Is.EqualTo(60f).Within(.1f));
            using (GameTimeScaleRuntime.AcquirePause())
            {
                float grace = bar.Closing.GraceRemainingSeconds;
                bar.Closing.Advance(120f);
                Assert.That(bar.Closing.GraceRemainingSeconds, Is.EqualTo(grace));
            }
            bar.Closing.Advance(bar.Closing.GraceRemainingSeconds - .25f);
            Assert.That(bar.Closing.FinalRequestIssued, Is.False);
            Assert.That(vessel.FillProgress, Is.GreaterThan(.99f), "Closing cannot consume the wine automatically.");
            yield return DriveClosingUntil(() => bar.Closing.FinalRequestIssued, "The final ordinary request must be spoken.");
            yield return AssertClosingReadingHold(BarConversationController.ClosingRequestKey, focus);
            yield return DriveClosingUntil(() => !bar.Conversation.IsBartenderSpeaking && !focus.IsActive,
                "The final request must return the camera before the last chance to leave.");
            camera.transform.rotation = Quaternion.LookRotation(
                vessel.GlassRenderer.bounds.center - camera.transform.position, Vector3.up);
            shop.RefreshServedDrinkAffordance();
            Assert.That(shop.BeginServedDrink(), Is.True);
            Assert.That(playerAction.IsNestedLoopActionActive, Is.True);
            bar.Closing.Advance(8.1f);
            Assert.That(bar.Closing.IsDepartureRequested, Is.True);
            Assert.That(station.Seat.IsSeated, Is.True);
            Assert.That(playerAction.IsNestedLoopActionActive, Is.True,
                "The deadline cannot interrupt a live hand grip and sip.");
            yield return DriveServiceUntil(() => !playerAction.IsNestedLoopActionActive && shop.Timeline.CompletedDrinkSteps == 1,
                "The current sip must return the actual vessel normally.");
            Assert.That(vessel.FillProgress, Is.EqualTo(2f / 3f).Within(.02f));
            Assert.That(shop.BeginServedDrink(), Is.False, "Starting another sip cannot postpone an overdue departure.");
            int drinksAfterSip = GameSessionState.DrinksConsumed;
            yield return DriveClosingUntil(() => !playerAction.IsActive,
                "The last guest must use the normal visible stand-up, without cancelling the seat.", true);
            Assert.That(vessel, Is.Not.Null);
            Assert.That(vessel.gameObject.activeInHierarchy, Is.True);
            Assert.That(vessel.FillProgress, Is.EqualTo(2f / 3f).Within(.02f));
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(drinksAfterSip));
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(cashAfter));
            bar.Closing.enabled = false;
            yield return DriveServiceUntil(() => !shop.IsOpen,
                "The bartender must collect the real remaining wine and menu through normal storage.");
            playerAction.ReleaseExternalClock(this);
            playerAction = null;
            // Stage the opposite side of the real hall; only fixture setup may place the player.
            ParkHeroForCrowd();
            follow.enabled = true;
            Physics.SyncTransforms();
            bar.Closing.enabled = true;
            Vector3 routeStart = bar.Player.GameObject.transform.position;
            Vector3 previousPosition = routeStart;
            deadline = Time.realtimeSinceStartup + 20f;
            while (!(bar.Closing.IsWalkingToExit && Vector3.Distance(previousPosition, routeStart) > .4f))
            {
                bar.Closing.Advance(.05f);
                Vector3 position = bar.Player.GameObject.transform.position;
                Assert.That(Vector3.Distance(position, previousPosition), Is.LessThan(.5f));
                previousPosition = position;
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The constrained exit walk did not begin.");
                yield return null;
            }
            yield return Capture("bar-hours-last-guest-walking.png");
            using (GameTimeScaleRuntime.AcquirePause())
            {
                Vector3 paused = bar.Player.GameObject.transform.position;
                bar.Closing.Advance(2f);
                yield return null;
                Assert.That(bar.Player.GameObject.transform.position, Is.EqualTo(paused));
            }
            PlayerAnimatedInteractionController exitAction = bar.Player.GameObject.GetComponent<PlayerAnimatedInteractionController>();
            BarExit exit = bar.GetComponentInChildren<BarExit>(true);
            Vector3 doorDock = exit.GetComponent<PlayerDoorActionTarget>().Plan.EntryPose.RootPosition;
            deadline = Time.realtimeSinceStartup + 20f;
            while (exitAction.Phase != PlayerAnimatedInteractionPhase.Entering)
            {
                bar.Closing.Advance(.05f);
                Vector3 position = bar.Player.GameObject.transform.position;
                Assert.That(Vector3.Distance(position, previousPosition), Is.LessThan(.5f),
                    "Every waypoint must retain visible constrained locomotion.");
                previousPosition = position;
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The full route did not reach the authored door dock.");
                yield return null;
            }
            Vector3 offset = bar.Player.GameObject.transform.position - doorDock;
            offset.y = 0f;
            Assert.That(offset.magnitude, Is.LessThan(.05f));
            bar.Closing.enabled = false;
            Assert.That(bar.Player.Motor.InputEnabled, Is.True);
            Assert.That(SceneTransitionService.IsTransitioning, Is.False,
                "The test stops the ordinary door action before its terminal scene load.");
        }

        private IEnumerator AssertClosingReadingHold(string key, BarClosingCameraFocus focus)
        {
            string text = LocalizationService.Get(key);
            UnityEngine.Object speaker = bar.Conversation.Speaker(BarConversationController.BartenderRole);
            yield return DriveClosingUntil(() => bar.Conversation.BartenderBubbles.RevealedTextOf(speaker) == text,
                "The complete announcement must appear before its reading hold.");
            BarBartenderAssetRegistry rig = bar.Bartender.Registry;
            Bounds handTravel = new Bounds(rig.RightHand.position, Vector3.zero);
            using (GameTimeScaleRuntime.AcquirePause())
            {
                double life = bar.Conversation.LifeSeconds;
                bar.Conversation.Advance(10f);
                bar.Closing.Advance(10f);
                yield return null;
                Assert.That(bar.Conversation.LifeSeconds, Is.EqualTo(life));
                Assert.That(focus.IsActive, Is.True);
            }
            // Existing reading time plus the user's additional second, all with the full line visible.
            for (int step = 0; step < 70; step++)
            {
                Assert.That(bar.Conversation.BartenderBubbles.RevealedTextOf(speaker), Is.EqualTo(text));
                Assert.That(focus.IsActive && !focus.IsReturning, Is.True);
                Assert.That(bar.Closing.IsDepartureRequested, Is.False);
                Assert.That(rig.RightForearm.position.y, Is.LessThan(rig.RightUpperArm.position.y - .12f),
                    "The hall-address elbow stays naturally below its shoulder.");
                Assert.That(rig.RightHand.position.y, Is.LessThan(rig.Head.position.y - .16f));
                handTravel.Encapsulate(rig.RightHand.position);
                if (step == 20)
                    yield return Capture(key == BarConversationController.ClosingWarningKey
                        ? "bar-hours-warning-full-text.png" : "bar-hours-request-full-text.png");
                bar.Conversation.Advance(.05f);
                bar.Closing.Advance(.05f);
                yield return null;
            }
            Assert.That(handTravel.size.magnitude, Is.GreaterThan(.025f),
                "The open hand must gesture while the full announcement is held.");
        }

        private IEnumerator DriveClosingUntil(Func<bool> complete, string message, bool advanceService = false)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            BarBartenderServiceChoreography choreography =
                bar.Bartender.GetComponent<BarBartenderServiceChoreography>();
            for (int step = 0; step < 1600 && !complete(); step++)
            {
                if (advanceService)
                {
                    playerAction.AdvanceExternal(this, .1f);
                    bar.Bartender.Advance(.1f);
                    choreography.Advance(.1f);
                    bar.DrinkShop.AdvancePresentation(.1f);
                }
                bar.Conversation.Advance(.1f);
                bar.Closing.Advance(.1f);
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), message +
                    " Last line: " + bar.Conversation.LastLineKey + "; visible: " +
                    bar.Conversation.BartenderBubbles.RevealedTextOf(
                        bar.Conversation.Speaker(BarConversationController.BartenderRole)) +
                    "; speaking: " + bar.Conversation.IsBartenderSpeaking);
                yield return null;
            }
            Assert.That(complete(), Is.True, message);
        }

        private void ParkHeroForCrowd()
        {
            // The doorway is the actual arrival route; a staged review cannot park the hero in its way.
            bar.Player.Motor.Teleport(bar.Room.TransformPoint(new Vector3(4f, PlayerFactory.GroundedRootOffset, 0f)));
            Physics.SyncTransforms();
        }

        private static IEnumerator AssertFacadeHoursRestoreActualPanes()
        {
            var stage = new GameObject("Test actual bar facade hours");
            stage.transform.position = Vector3.right * 10000f;
            Camera camera = Camera.main;
            Pose oldCamera = new Pose(camera.transform.position, camera.transform.rotation);
            float oldFov = camera.fieldOfView;
            AmbientMode oldAmbientMode = RenderSettings.ambientMode;
            Color oldAmbient = RenderSettings.ambientLight;
            bool oldFog = RenderSettings.fog;
            try
            {
                BuildingLot lot = null;
                foreach (BuildingLot candidate in HomeExteriorContextPlanner.Generate(GameSessionState.CitySeed).Layout.BuildingLots)
                    if (candidate.IsBar) { lot = candidate; break; }
                Assert.That(lot, Is.Not.Null);
                CityBarFacadeWorldBuilder.BuildCity(stage.transform, lot);
                BarFacadeHoursPresentation hours = stage.GetComponentInChildren<BarFacadeHoursPresentation>();
                Assert.That(hours, Is.Not.Null);
                GameObject prefab = Resources.Load<GameObject>("Bar/BarFacade3D");
                Assert.That(prefab, Is.Not.Null);
                Renderer lower = FindPane("exterior_window_ground");
                Renderer upper = FindPane("exterior_window_upper_warm");
                Assert.That(lower, Is.Not.Null);
                Assert.That(upper, Is.Not.Null);
                var lightObject = new GameObject("Test facade review fixture");
                lightObject.transform.SetParent(stage.transform, false);
                lightObject.transform.rotation = Quaternion.Euler(38f, 28f, 0f);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = .8f;
                light.color = new Color(1f, .96f, .9f);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.35f, .37f, .40f);
                RenderSettings.fog = false;
                Vector3 outward = new Vector3(lot.FrontageDirection.x, 0f, lot.FrontageDirection.y).normalized;
                Vector3 target = stage.transform.TransformPoint(lot.DoorPosition) + Vector3.up * 3.4f;
                Vector3 position = target + outward * 14f + Vector3.Cross(outward, Vector3.up) * 1.8f;
                camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
                camera.fieldOfView = 55f;
                // The Editor's asynchronous loading shader is cyan. Photograph the real forward pass.
                yield return null;
                yield return null;
                yield return CompileForwardPass(lower.sharedMaterial);
                yield return CompileForwardPass(upper.sharedMaterial);
                int baseColor = Shader.PropertyToID("_BaseColor");
                int emission = Shader.PropertyToID("_EmissionColor");
                var lowerOpen = new MaterialPropertyBlock();
                var upperOpen = new MaterialPropertyBlock();
                var actual = new MaterialPropertyBlock();
                AdvanceClockTo(12 * 60);
                hours.Refresh();
                Assert.That(hours.IsBusinessOpen, Is.True);
                lower.GetPropertyBlock(lowerOpen);
                upper.GetPropertyBlock(upperOpen);
                yield return Capture("bar-hours-facade-open.png");
                Color openPixels = ReadPanePixels("bar-hours-facade-open.png", lower, camera);
                Assert.That(openPixels.r, Is.GreaterThan(openPixels.b + .04f),
                    "The actual open glazing must render warm, without the cyan compilation fallback.");
                AdvanceClockTo(2 * 60);
                hours.Refresh();
                Assert.That(hours.IsBusinessOpen, Is.False);
                lower.GetPropertyBlock(actual);
                Assert.That(actual.GetColor(emission), Is.EqualTo(lower.sharedMaterial.GetColor(emission) * .08f));
                Assert.That(actual.GetColor(baseColor), Is.Not.EqualTo(lowerOpen.GetColor(baseColor)));
                upper.GetPropertyBlock(actual);
                Assert.That(actual.GetColor(baseColor), Is.EqualTo(upperOpen.GetColor(baseColor)));
                Assert.That(actual.GetColor(emission), Is.EqualTo(upperOpen.GetColor(emission)));
                yield return Capture("bar-hours-facade-closed.png");
                Color closedPixels = ReadPanePixels("bar-hours-facade-closed.png", lower, camera);
                Assert.That(closedPixels.grayscale, Is.LessThan(openPixels.grayscale * .8f),
                    "The actual photographed ground glazing must visibly dim after closing.");
                AdvanceClockTo(12 * 60);
                hours.Refresh();
                lower.GetPropertyBlock(actual);
                Assert.That(actual.GetColor(baseColor), Is.EqualTo(lowerOpen.GetColor(baseColor)));
                Assert.That(actual.GetColor(emission), Is.EqualTo(lowerOpen.GetColor(emission)));

                Renderer FindPane(string role)
                {
                    foreach (BarPartBinding part in prefab.GetComponent<BarAssetRegistry>().Parts)
                    {
                        if (part.Role != role || part.Renderer == null) continue;
                        foreach (Renderer placed in stage.GetComponentsInChildren<Renderer>())
                            if (placed.name == part.Renderer.name) return placed;
                    }
                    return null;
                }

            }
            finally
            {
                Object.DestroyImmediate(stage);
                camera.transform.SetPositionAndRotation(oldCamera.position, oldCamera.rotation);
                camera.fieldOfView = oldFov;
                RenderSettings.ambientMode = oldAmbientMode;
                RenderSettings.ambientLight = oldAmbient;
                RenderSettings.fog = oldFog;
            }
        }

        private static IEnumerator CompileForwardPass(Material material)
        {
#if UNITY_EDITOR
            int pass = material.FindPass("ForwardLit");
            Assert.That(pass, Is.GreaterThanOrEqualTo(0));
            if (!UnityEditor.ShaderUtil.IsPassCompiled(material, pass))
                UnityEditor.ShaderUtil.CompilePass(material, pass);
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!UnityEditor.ShaderUtil.IsPassCompiled(material, pass))
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The actual glazing's forward pass did not compile.");
                yield return null;
            }
#endif
            yield return null;
            yield return null;
        }

        private static Color ReadPanePixels(string filename, Renderer pane, Camera camera)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", SceneIds.BarInterior, filename));
                Assert.That(texture.LoadImage(File.ReadAllBytes(path)), Is.True);
                Bounds bounds = pane.bounds;
                Vector2 minimum = Vector2.one;
                Vector2 maximum = Vector2.zero;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 offset = new Vector3((corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Vector3 point = camera.WorldToViewportPoint(bounds.center + Vector3.Scale(bounds.extents, offset));
                    Assert.That(point.z, Is.GreaterThan(0f));
                    minimum = Vector2.Min(minimum, point);
                    maximum = Vector2.Max(maximum, point);
                }
                int xMin = Mathf.Clamp(Mathf.FloorToInt(minimum.x * texture.width), 0, texture.width - 1);
                int xMax = Mathf.Clamp(Mathf.CeilToInt(maximum.x * texture.width), xMin + 1, texture.width);
                int yMin = Mathf.Clamp(Mathf.FloorToInt(minimum.y * texture.height), 0, texture.height - 1);
                int yMax = Mathf.Clamp(Mathf.CeilToInt(maximum.y * texture.height), yMin + 1, texture.height);
                Color total = Color.clear;
                int count = 0;
                for (int y = yMin; y < yMax; y += 4)
                for (int x = xMin; x < xMax; x += 4)
                {
                    total += texture.GetPixel(x, y);
                    count++;
                }
                Assert.That(count, Is.GreaterThan(0));
                return total / count;
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private IEnumerator DriveCrowdUntil(Func<bool> complete, string message)
        {
            float deadline = Time.realtimeSinceStartup + 25f;
            for (int step = 0; step < 8000 && !complete(); step++)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), message + CrowdDiagnostic());
                bool contactPhase = CrowdNeedsContactFrame();
                bar.Crowd.Advance(bar.Crowd.HasGuestInTransit ? .1f : BarCrowdController.ChangeIntervalSeconds + .1f);
                // Each sit/stand sample reaches LateUpdate before advancing again.
                if (contactPhase || CrowdNeedsContactFrame() || step % 3 == 0) yield return null;
            }
            Assert.That(complete(), Is.True, message + CrowdDiagnostic());
        }

        private IEnumerator AdvanceCrowdFrames(int count)
        {
            for (int step = 0; step < count; step++)
            {
                bool contactPhase = CrowdNeedsContactFrame();
                bar.Crowd.Advance(.1f);
                if (contactPhase || CrowdNeedsContactFrame() || step % 3 == 0) yield return null;
            }
            yield return null;
        }

        private bool CrowdNeedsContactFrame() => bar.Crowd.TransitPhaseName == "Sitting" || bar.Crowd.TransitPhaseName == "Standing";

        private string CrowdDiagnostic() => $" Phase={bar.Crowd.TransitPhaseName}, guest={bar.Crowd.TransitPatronId}," +
            $" position={bar.Crowd.TransitPosition}, target={bar.Crowd.TransitTarget}, blocked={bar.Crowd.TransitBlockedBy}," +
            $" foot={bar.Crowd.TransitFootContactError}, grip={bar.Crowd.TransitVesselGripError}.";

        private IEnumerator DriveServiceUntil(Func<bool> complete, string message)
        {
            BarBartenderServiceChoreography choreography = bar.Bartender.GetComponent<BarBartenderServiceChoreography>();
            float deadline = Time.realtimeSinceStartup + 20f;
            for (int step = 0; step < 1400 && !complete(); step++)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), message + " Phase: " + bar.DrinkShop.Phase);
                playerAction.AdvanceExternal(this, .1f);
                bar.Bartender.Advance(.1f);
                choreography.Advance(.1f);
                bar.DrinkShop.AdvancePresentation(.1f);
                yield return null;
            }
            Assert.That(complete(), Is.True, message + " Phase: " + bar.DrinkShop.Phase);
        }

        private void AssertSettledCast()
        {
            Assert.That(bar.Crowd.HasGuestInTransit, Is.False);
            int occupiedCounterPlaces = 0;
            foreach (BarNpcAnchor anchor in bar.Layout.NpcAnchors)
                if (anchor.Role == BarNpcRole.CounterPatron && bar.Crowd.IsAnchorOccupied(anchor.Id))
                    occupiedCounterPlaces++;
            for (int role = 0; role < BarConversationCatalog.RoleCount; role++)
            {
                BarPatron present = null;
                foreach (BarPatron patron in bar.Patrons)
                    if (patron.Anchor.Id == BarConversationCatalog.RoleId(role)) present = patron;
                Assert.That(bar.Conversation.Speaker(role), Is.SameAs(present?.Presentation));
                Assert.That(bar.Conversation.Head(role), Is.SameAs(present?.Registry.HeadAnchor));
                if (present == null) continue;
                Assert.That(present.Registry.gameObject.activeInHierarchy, Is.True);
                Assert.That(present.Drinking, Is.Not.Null.And.Property("enabled").True);
                Assert.That(bar.Conversation.GuestBubbles.IsDeclared(present.Presentation),
                    Is.EqualTo(BarConversationCatalog.CanSpeak(role)));
            }
            Assert.That(bar.CounterStations.Count, Is.EqualTo(5 - occupiedCounterPlaces));
        }

        private static void AdvanceClockTo(int minute)
        {
            double delta = minute - GameSessionState.GameTimeOfDayMinutes;
            if (delta < 0d) delta += 24 * 60;
            GameSessionState.AdvanceGameTime((float)(delta / GameTimeState.GameMinutesPerRealSecond + .002d));
            Assert.That(GameSessionState.GameMinuteOfDay, Is.EqualTo(minute));
        }

        private void StageRoomCamera(Camera camera)
        {
            Vector3 position = bar.Room.TransformPoint(new Vector3(0f, 2.35f, -3.75f));
            camera.transform.SetPositionAndRotation(position,
                Quaternion.LookRotation(bar.Room.TransformPoint(new Vector3(0f, 1.25f, 1.4f)) - position));
            camera.fieldOfView = 76f;
        }

        private static IEnumerator Capture(string filename)
        {
            FocusGameView();
            yield return null;
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", SceneIds.BarInterior, filename));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            DateTime before = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 3f;
            do { yield return null; }
            while ((!File.Exists(path) || File.GetLastWriteTimeUtc(path) <= before) && Time.realtimeSinceStartup < deadline);
            Assert.That(File.Exists(path) && File.GetLastWriteTimeUtc(path) > before, Is.True, filename);
        }

        private static void FocusGameView()
        {
#if UNITY_EDITOR
            Type type = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            Assert.That(type, Is.Not.Null);
            UnityEditor.EditorWindow view = UnityEditor.EditorWindow.GetWindow(type);
            view.Show();
            view.Focus();
            view.Repaint();
#endif
        }
    }
}
