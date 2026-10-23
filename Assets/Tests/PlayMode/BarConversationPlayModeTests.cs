using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    [PrebuildSetup(typeof(CompactBarAssetsSetup))]
    public sealed class BarConversationPlayModeTests
    {
        private BarInteriorRoot bar;
        private float previousCaptureDelta;
        private PlayerAnimatedInteractionController playerAction;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousCaptureDelta = Time.captureDeltaTime;
            Assert.That(Application.isBatchMode, Is.False,
                "The actual shared speech UI needs a visible Game view; run this focused case without -batchmode or -nographics.");
            FocusGameView();
            Time.captureDeltaTime = 1f / 60f;
            GameSessionState.BeginNewGame("bar-conversation-test");
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.EnterBar("bar-conversation-test", BarActivityKind.SplitTheG,
                CityDistrictKind.Residential);
            yield return SceneManager.LoadSceneAsync(SceneIds.BarInterior, LoadSceneMode.Single);
            float deadline = Time.realtimeSinceStartup + 20f;
            do
            {
                bar = Object.FindAnyObjectByType<BarInteriorRoot>();
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The production bar did not initialize.");
                if (bar == null || !bar.IsInitialized) yield return null;
            }
            while (bar == null || !bar.IsInitialized);
            bar.ArrivalPresentation.Skip();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (playerAction != null) playerAction.ReleaseExternalClock(this);
            yield return AreaCaptureFixture.ReleaseBarService();
            Scene scene = SceneManager.GetSceneByName(SceneIds.BarInterior);
            if (scene.IsValid() && scene.isLoaded)
            {
                Scene cleanup = SceneManager.CreateScene("Bar Conversation Cleanup");
                SceneManager.SetActiveScene(cleanup);
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            GameSessionState.ResetEconomyState();
            GameSessionState.ResetDrinkingState();
            Time.captureDeltaTime = previousCaptureDelta;
            yield return null;
        }

        [UnityTest]
        [Explicit("Production bar speech and actual Game-view UI. Run the exact case without -batchmode.")]
        public IEnumerator BarsShareSpeechAndKeepService()
        {
            BarConversationController speech = bar.Conversation;
            Assert.That(speech, Is.Not.Null.And.Property("IsInitialized").True);
            speech.AutoAdvance = false;
            Assert.That(speech.GuestBubbles, Is.Not.SameAs(speech.BartenderBubbles));
            Assert.That(speech.GuestBubbles.UseManualClock && speech.BartenderBubbles.UseManualClock, Is.True);
            for (int role = 0; role <= BarConversationController.BartenderRole; role++)
            {
                Assert.That(speech.Head(role), Is.Not.Null);
                Assert.That(View(role).IsDeclared(speech.Speaker(role)), Is.True, "Declaration for role " + role);
            }

            // The production sip component still solves contacts. This fixture
            // owns only its cadence, so a long manually sampled conversation
            // does not advance drinking on a different, rendered-frame clock.
            foreach (BarPatronAnimator animator in bar.GetComponentsInChildren<BarPatronAnimator>(true)) animator.enabled = false;
            foreach (BarPatron patron in bar.Patrons) Rest(patron.Drinking);
            bar.Player.Motor.SetInputEnabled(false);
            bar.Player.Motor.Teleport(bar.Room.TransformPoint(new Vector3(0f, PlayerFactory.GroundedRootOffset, .4f)));
            var camera = Camera.main;
            var follow = camera.GetComponent<PlayerCameraFollow>();
            follow.enabled = false;
            camera.transform.SetPositionAndRotation(bar.Room.TransformPoint(new Vector3(0f, 1.6f, -1.5f)), Quaternion.identity);
            Physics.SyncTransforms();

            int observed = speech.StartedLineCount, groups = 0, completed = 0;
            int bartenderLines = 0;
            string awaitingReply = null;
            var variants = new HashSet<string>();
            for (int step = 0; step < 4000 && (groups != BarConversationSchedule.AllGroups || completed < 8); step++)
            {
                Step(.1f);
                if (speech.StartedLineCount == observed) continue;
                observed = speech.StartedLineCount;
                Assert.That(speech.LastSpeakerRole, Is.LessThan(BarConversationController.BartenderRole));
                BarConversationTurn turn = speech.Schedule.Current;
                Assert.That(turn.IsSpeaking, Is.True);
                if (awaitingReply == null)
                {
                    Assert.That(speech.LastLineKey, Is.EqualTo(turn.Exchange.FirstKey));
                    Assert.That(variants.Add(turn.Exchange.FirstKey), Is.True, "A pair repeated before its shuffle pool was exhausted.");
                    awaitingReply = turn.Exchange.SecondKey;
                }
                else
                {
                    Assert.That(speech.LastLineKey, Is.EqualTo(awaitingReply), "The randomized exchange must retain its authored reply.");
                    groups |= 1 << (int)turn.Exchange.Kind;
                    completed++;
                    awaitingReply = null;
                }
            }
            Assert.That(groups, Is.EqualTo(BarConversationSchedule.AllGroups));
            Assert.That(completed, Is.GreaterThanOrEqualTo(8));

            // A real IMGUI/Game-view frame is required: Camera.Render alone
            // does not contain the shared overhead speech UI.
            int capturedRole = speech.LastSpeakerRole;
            Transform head = speech.Head(capturedRole);
            Vector3 towardHall = Vector3.ProjectOnPlane(bar.Room.TransformPoint(new Vector3(0f, 0f, .5f)) - head.position, Vector3.up).normalized;
            camera.transform.position = head.position + towardHall * 3f + Vector3.up * .25f;
            camera.transform.LookAt(head.position + Vector3.up * .08f);
            camera.fieldOfView = 55f;
            for (int sample = 0; sample < 8; sample++) Step(.1f);
            FocusGameView();
            yield return null;
            yield return new WaitForEndOfFrame();
            NpcSpeechBubbleView visible = View(capturedRole);
            Assert.That(visible.HasRenderedLayout, Is.True);
            Assert.That(visible.LastRenderedBubbleCount, Is.EqualTo(1));
            Assert.That(visible.LastRenderedText, Is.EqualTo(LocalizationService.Get(speech.LastLineKey)));
            Assert.That(visible.LastRenderedRevealedText, Is.Not.Empty);
            string capture = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", SceneIds.BarInterior,
                "bar-dialogue-shared-bubble.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(capture));
            DateTime previousWrite = File.Exists(capture) ? File.GetLastWriteTimeUtc(capture) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(capture);
            float captureDeadline = Time.realtimeSinceStartup + 3f;
            do { yield return null; }
            while ((!File.Exists(capture) || File.GetLastWriteTimeUtc(capture) <= previousWrite) && Time.realtimeSinceStartup < captureDeadline);
            Assert.That(File.Exists(capture) && File.GetLastWriteTimeUtc(capture) > previousWrite, Is.True);

            double heldClock = speech.LifeSeconds;
            string heldText = visible.RevealedTextOf(speech.Speaker(capturedRole));
            using (GameTimeScaleRuntime.AcquirePause())
            {
                Step(1f);
                yield return null;
                Assert.That(speech.LifeSeconds, Is.EqualTo(heldClock));
                Assert.That(visible.RevealedTextOf(speech.Speaker(capturedRole)), Is.EqualTo(heldText));
                Assert.That(visible.RenderEnabled, Is.False);
            }
            yield return null;

            bar.Player.Motor.Teleport(bar.Room.TransformPoint(new Vector3(-3.85f, PlayerFactory.GroundedRootOffset, 6.35f)));
            Step(.1f);
            AssertQuiet("WC crossing cancels the line and its delayed reply.");
            bar.Player.Motor.Teleport(bar.Room.position + Vector3.forward * 100f);
            Step(.1f);
            AssertQuiet("Distance exit does not preserve a speech backlog.");

            // The service proof uses the actual composed shop and arrival
            // reports. Advancing the common interaction clock avoids replaying
            // the unrelated full hand/contact cinematics in this speech test.
            BarDrinkShopController shop = bar.DrinkShop;
            int accepted = 0, heroAccepted = 0, patronAccepted = 0, delivered = 0, cancelled = 0;
            long firstOrderId = 0, lastOrderId = 0;
            shop.ServiceOrderAccepted += order =>
            {
                accepted++;
                Assert.That(order.Id, Is.GreaterThan(lastOrderId));
                lastOrderId = order.Id;
                if (order.IsHero) heroAccepted++; else patronAccepted++;
                Assert.That(order.IsRepeat, Is.EqualTo(!order.IsHero || heroAccepted > 1));
                if (accepted == 1) firstOrderId = order.Id;
            };
            shop.ServiceOrderDelivered += order => { delivered++; Assert.That(order.Id, Is.GreaterThanOrEqualTo(firstOrderId)); };
            shop.ServiceOrderCancelled += _ => cancelled++;

            BarCounterStation station = bar.CounterStations[bar.CounterStations.Count - 1];
            bar.Player.Motor.Teleport(station.Seat.Plan.EntryPose.RootPosition);
            bar.Player.GameObject.transform.rotation = station.Seat.Plan.EntryPose.RootRotation;
            Physics.SyncTransforms();
            playerAction = station.Seat.Controller;
            Assert.That(playerAction.BeginExternalClock(this), Is.True);
            station.Interact(bar.Player.Interactor);
            yield return DriveUntil(() => station.Seat.IsSeated && shop.IsBrowsing, 400, "The real seat/menu cycle did not reach browsing.");
            Assert.That(accepted, Is.Zero, "Opening/reserving the menu is not an order.");

            MethodInfo accept = typeof(BarDrinkShopController).GetMethod("AcceptHeroOrder", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(accept, Is.Not.Null);
            Assert.That((bool)accept.Invoke(shop, new object[] { DrinkId.None, false }), Is.False);
            Assert.That((bool)accept.Invoke(shop, new object[] { DrinkId.None, true }), Is.False);
            Assert.That(accepted, Is.Zero, "Failed initial/refill requests do not create a comment.");
            Assert.That(speech.PendingCommentCount, Is.Zero);
            int cash = GameSessionState.CashBalance;
            Assert.That((bool)accept.Invoke(shop, new object[] { DrinkId.Vodka, false }), Is.True);
            Assert.That(accepted, Is.EqualTo(1));
            Assert.That(GameSessionState.CashBalance, Is.LessThan(cash));
            Assert.That(shop.ConfirmSelection(), Is.False, "Duplicate input cannot buy while the accepted order is active.");
            foreach (BarPatron patron in bar.Patrons)
            {
                if (patron.Refill == null) continue;
                patron.Refill.Advance(2000f);
                Assert.That(patron.Refill.IsWaitingForRefill, Is.True);
                patron.Refill.Advance(BarRepeatGesture.EnterSeconds + .01f);
                Assert.That(shop.QueuePatronRefill(patron.Refill), Is.True, "A duplicate queue request keeps its existing order.");
            }
            Assert.That(accepted, Is.EqualTo(3));
            Assert.That(patronAccepted, Is.EqualTo(2));
            Assert.That(shop.PendingServiceOrderCount, Is.EqualTo(3));
            yield return DriveUntil(() => delivered == 3 && !shop.IsPatronServiceActive, 1500,
                "The shared worker did not physically finish the hero and both counter guests.");
            Assert.That(shop.Timeline.IsAwaitingDrink, Is.True);
            Assert.That(shop.PendingServiceOrderCount, Is.Zero);
            yield return DrainComments(3);

            // Consume the pure drink timeline without repeating the existing
            // separate full-rig drinking regression, then exercise a true refill.
            CompleteDrink();
            Assert.That(shop.Timeline.HasEmptyVessel, Is.True);
            Assert.That((bool)accept.Invoke(shop, new object[] { DrinkId.Vodka, true }), Is.True);
            Assert.That(accepted, Is.EqualTo(4));
            yield return DriveUntil(() => shop.Timeline.IsAwaitingDrink, 500, "The repeat physical delivery did not complete.");
            Assert.That(delivered, Is.EqualTo(4));
            yield return DrainComments(4);
            Assert.That(cancelled, Is.Zero);

            CompleteDrink();
            Assert.That((bool)accept.Invoke(shop, new object[] { DrinkId.Vodka, true }), Is.True);
            Assert.That(accepted, Is.EqualTo(5));
            shop.Close();
            Assert.That(cancelled, Is.EqualTo(1));
            Assert.That(speech.PendingCommentCount, Is.Zero, "Teardown cancels the unspoken accepted comment.");

            speech.enabled = false;
            AssertQuiet("Disabling removes both shared view owners.");
            for (int role = 0; role <= BarConversationController.BartenderRole; role++)
                Assert.That(View(role).IsDeclared(speech.Speaker(role)), Is.False);
            speech.enabled = true;
            for (int role = 0; role <= BarConversationController.BartenderRole; role++)
                Assert.That(View(role).IsDeclared(speech.Speaker(role)), Is.True);
            Assert.That(speech.PendingCommentCount, Is.Zero);

            NpcSpeechBubbleView View(int role) => role == BarConversationController.BartenderRole ? speech.BartenderBubbles : speech.GuestBubbles;

            void Step(float seconds)
            {
                int prior = speech.StartedLineCount;
                speech.Advance(seconds);
                Assert.That(speech.ActiveLineCount, Is.InRange(0, 1));
                Assert.That(speech.PendingCommentCount, Is.LessThanOrEqualTo(BarConversationController.PendingCommentCapacity));
                int showing = 0;
                for (int role = 0; role <= BarConversationController.BartenderRole; role++)
                    if (View(role).IsShowing(speech.Speaker(role))) showing++;
                Assert.That(showing, Is.LessThanOrEqualTo(1), "The two shared views must never overlap speech.");
                if (speech.StartedLineCount > prior)
                {
                    Assert.That(speech.StartedLineCount, Is.EqualTo(prior + 1));
                    int role = speech.LastSpeakerRole;
                    Assert.That(View(role).RevealedTextOf(speech.Speaker(role)), Is.Not.Empty, "Only a revealed line spends a shuffle entry.");
                    Assert.That(LocalizationService.Get(speech.LastLineKey), Is.Not.EqualTo(speech.LastLineKey));
                    if (role == BarConversationController.BartenderRole) bartenderLines++;
                }
            }

            void AssertQuiet(string message)
            {
                Assert.That(speech.ActiveLineCount, Is.Zero, message);
                Assert.That(speech.PendingCommentCount, Is.Zero, message);
                Assert.That(speech.Schedule.Current.HasExchange, Is.False, message);
                for (int role = 0; role <= BarConversationController.BartenderRole; role++)
                    Assert.That(View(role).IsShowing(speech.Speaker(role)), Is.False, message);
            }

            void CompleteDrink()
            {
                Assert.That(shop.Timeline.BeginDrink(), Is.True);
                shop.Timeline.Advance(BarDrinkServiceTimeline.PlayerPickupDurationSeconds +
                    BarDrinkServiceTimeline.DrinkingDurationSeconds + BarDrinkServiceTimeline.PlayerVesselReturnDurationSeconds);
                int consumed = GameSessionState.DrinksConsumed;
                MethodInfo complete = typeof(BarDrinkShopController).GetMethod("CompleteOrderPresentation", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(complete, Is.Not.Null);
                complete.Invoke(shop, null);
                Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(consumed + 1));
                shop.AdvancePresentation(0f);
            }

            IEnumerator DriveUntil(Func<bool> complete, int maximumSteps, string message)
            {
                BarBartenderServiceChoreography choreography = bar.Bartender.GetComponent<BarBartenderServiceChoreography>();
                MethodInfo patrons = typeof(BarDrinkShopController).GetMethod("AdvancePatronService", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(patrons, Is.Not.Null);
                for (int step = 0; step < maximumSteps && !complete(); step++)
                {
                    playerAction.AdvanceExternal(this, .1f);
                    bar.Bartender.Advance(.1f);
                    patrons.Invoke(shop, new object[] { .1f });
                    choreography.Advance(.1f);
                    shop.AdvancePresentation(.1f);
                    Step(.1f);
                    if (step % 10 == 0) yield return null;
                }
                Assert.That(complete(), Is.True, message + " Current phase: " + shop.Phase);
            }

            IEnumerator DrainComments(int expected)
            {
                for (int step = 0; step < 400 && (bartenderLines < expected || speech.PendingCommentCount > 0 || speech.ActiveLineCount > 0); step++)
                {
                    Step(.1f);
                    if (step % 100 == 0) yield return null;
                }
                Assert.That(bartenderLines, Is.EqualTo(expected), "Every accepted order has exactly one bartender line across acceptance and delivery.");
                Assert.That(speech.PendingCommentCount, Is.Zero);
            }
        }

        private static void Rest(BarPatronDrinkingArmPose drinking)
        {
            Assert.That(drinking, Is.Not.Null);
            for (int phase = 0; phase < 4 && drinking.Timeline.Phase != BarPatronDrinkPhase.Rest; phase++)
                drinking.Timeline.Advance(Mathf.Max(.001f, drinking.Timeline.PhaseDuration - drinking.Timeline.PhaseElapsed + .0001f));
            Assert.That(drinking.Timeline.Phase, Is.EqualTo(BarPatronDrinkPhase.Rest));
        }

        private static void FocusGameView()
        {
#if UNITY_EDITOR
            Type viewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            Assert.That(viewType, Is.Not.Null);
            UnityEditor.EditorWindow view = UnityEditor.EditorWindow.GetWindow(viewType);
            view.Show();
            view.Focus();
            view.Repaint();
#endif
        }
    }
}
