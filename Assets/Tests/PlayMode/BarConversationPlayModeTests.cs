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
            BarTestTime.StartFullAttendance("bar-conversation-test");
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
        public IEnumerator SingleTablePatrons_RemainSilentWhilePairsSpeak()
        {
            BarConversationController speech = bar.Conversation;
            Assert.That(speech, Is.Not.Null.And.Property("IsInitialized").True);
            speech.AutoAdvance = false;
            foreach (BarPatronAnimator animator in bar.GetComponentsInChildren<BarPatronAnimator>(true))
                animator.enabled = false;
            foreach (BarPatron patron in bar.Patrons) Rest(patron.Drinking);
            bar.Player.Motor.SetInputEnabled(false);
            bar.Player.Motor.Teleport(bar.Room.TransformPoint(new Vector3(0f, PlayerFactory.GroundedRootOffset, .4f)));
            Physics.SyncTransforms();
            for (int role = 0; role < BarConversationCatalog.HighTableLeft; role++)
                Assert.That(speech.GuestBubbles.IsDeclared(speech.Speaker(role)), Is.True, "Speaking pair role " + role);
            AssertSolitarySilent();
            BarConversationExchange dormant = BarConversationCatalog.Get(BarConversationKind.HighTables, 0);
            foreach (int role in new[] { BarConversationCatalog.HighTableLeft, BarConversationCatalog.HighTableRight })
                Assert.That(speech.GuestBubbles.ShowAt(speech.Speaker(role),
                    LocalizationService.Get(role == BarConversationCatalog.HighTableLeft ? dormant.FirstKey : dormant.SecondKey),
                    (float)speech.LifeSeconds), Is.False, "A solitary patron cannot be routed into shared speech.");

            int heardRoles = 0, lineEvents = 0;
            const int allPairRoles = (1 << BarConversationCatalog.HighTableLeft) - 1;
            speech.LineStarted += ObserveLine;
            Camera camera = Camera.main;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool previousFollow = follow.enabled;
            float previousFov = camera.fieldOfView;
            try
            {
                float deadline = Time.realtimeSinceStartup + 20f;
                double finalClock = speech.LifeSeconds + 30d;
                while (heardRoles != allPairRoles && speech.LifeSeconds < finalClock)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline),
                        "The three actual pairs did not acknowledge their lines within the focused deadline.");
                    speech.Advance(.05f);
                    Assert.That(speech.ActiveLineCount, Is.InRange(0, BarConversationController.MaximumSimultaneousLines));
                    AssertSolitarySilent();
                    // The shared vocalizer consumes worker results in Update.
                    // Every owner-clock step therefore yields a real frame.
                    yield return null;
                }
                Assert.That(heardRoles, Is.EqualTo(allPairRoles),
                    "Both counter guests and all four booth guests must actually acknowledge shared spoken lines.");
                Assert.That(lineEvents, Is.GreaterThanOrEqualTo(6));
                AssertSolitarySilent();

                follow.enabled = false;
                camera.transform.SetPositionAndRotation(bar.Room.TransformPoint(new Vector3(0f, 2f, 1f)),
                    Quaternion.LookRotation(bar.Room.TransformPoint(new Vector3(0f, 1.35f, -2.8f)) -
                        bar.Room.TransformPoint(new Vector3(0f, 2f, 1f))));
                camera.fieldOfView = 70f;
                FocusGameView();
                yield return null;
                yield return null;
                foreach (int role in new[] { BarConversationCatalog.HighTableLeft, BarConversationCatalog.HighTableRight })
                {
                    Vector3 point = camera.WorldToViewportPoint(speech.Head(role).position);
                    Assert.That(point.z, Is.GreaterThan(0f));
                    Assert.That(point.x, Is.InRange(.05f, .95f), "Both solitary patrons belong in the actual review frame.");
                    Assert.That(point.y, Is.InRange(.05f, .95f));
                }
                string capture = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", SceneIds.BarInterior,
                    "solitary-table-patrons-silent.png"));
                Directory.CreateDirectory(Path.GetDirectoryName(capture));
                DateTime previousWrite = File.Exists(capture) ? File.GetLastWriteTimeUtc(capture) : DateTime.MinValue;
                ScreenCapture.CaptureScreenshot(capture);
                float captureDeadline = Time.realtimeSinceStartup + 3f;
                do { yield return null; }
                while ((!File.Exists(capture) || File.GetLastWriteTimeUtc(capture) <= previousWrite) &&
                    Time.realtimeSinceStartup < captureDeadline);
                Assert.That(File.Exists(capture) && File.GetLastWriteTimeUtc(capture) > previousWrite, Is.True);
                AssertSolitarySilent();

                yield return CaptureIntoxicatedNpcScatter(speech, camera);
                AssertSolitarySilent();

                speech.enabled = false;
                Assert.That(speech.ActiveLineCount, Is.Zero);
                Assert.That(speech.PendingCommentCount, Is.Zero);
                AssertSolitarySilent();
                speech.enabled = true;
                for (int role = 0; role < BarConversationCatalog.HighTableLeft; role++)
                    Assert.That(speech.GuestBubbles.IsDeclared(speech.Speaker(role)), Is.True);
                Assert.That(speech.ActiveLineCount, Is.Zero, "Re-enabling starts from a clean conversation boundary.");
                int eventsBeforeReturn = lineEvents;
                for (int frame = 0; frame < 3; frame++)
                {
                    speech.Advance(.05f);
                    yield return null;
                    AssertSolitarySilent();
                }
                Assert.That(lineEvents, Is.EqualTo(eventsBeforeReturn), "No old line may return as a queued acknowledgement.");
                Assert.That(speech.PendingCommentCount, Is.Zero);
            }
            finally
            {
                speech.LineStarted -= ObserveLine;
                speech.enabled = true;
                camera.fieldOfView = previousFov;
                follow.enabled = previousFollow;
            }

            void ObserveLine(int role, string key)
            {
                lineEvents++;
                Assert.That(role == BarConversationController.BartenderRole || BarConversationCatalog.CanSpeak(role), Is.True,
                    "The solitary table patrons must never emit a line-start event.");
                if (role == BarConversationController.BartenderRole) return;
                heardRoles |= 1 << role;
                Assert.That(speech.GuestBubbles.RevealedTextOf(speech.Speaker(role)), Is.Not.Empty);
                Assert.That(speech.GuestBubbles.TryGetSpeechFaceSample(speech.Speaker(role), out SpeechFaceSample sample), Is.True);
                Assert.That(sample.RevealedCharacters, Is.GreaterThan(0), key);
            }

            void AssertSolitarySilent()
            {
                for (int role = BarConversationCatalog.HighTableLeft; role <= BarConversationCatalog.HighTableRight; role++)
                {
                    Assert.That(speech.Head(role), Is.Not.Null, "The solitary visitor remains present in the bar.");
                    Assert.That(speech.GuestBubbles.IsDeclared(speech.Speaker(role)), Is.False);
                    Assert.That(speech.GuestBubbles.IsShowing(speech.Speaker(role)), Is.False);
                    Assert.That(speech.GuestBubbles.TryGetSpeechFaceSample(speech.Speaker(role), out _), Is.False);
                }
                BarConversationSchedule highTables = speech.GetSchedule(BarConversationKind.HighTables);
                Assert.That(highTables.Current.HasExchange || highTables.Current.IsSpeaking, Is.False);
                Assert.That(highTables.StartedLineCount, Is.Zero);
            }
        }

        private IEnumerator CaptureIntoxicatedNpcScatter(BarConversationController speech, Camera camera)
        {
            NpcSpeechBubbleView view = speech.GuestBubbles;
            int previousLevel = GameSessionState.IntoxicationLevel;
            DrinkId previousDrink = GameSessionState.LastAlcoholicDrink;
            int previousDrinks = GameSessionState.DrinksConsumed;
            float previousPresentation = GameTimeScaleRuntime.SmoothedPresentationLevel;
            int[] roles = { BarConversationCatalog.FrontBoothNear, BarConversationCatalog.RearBoothNear };
            try
            {
                // Keep the actual speakers, room and shared view. Only their
                // frozen owner clock and an existing sentence break are staged.
                view.DismissAll();
                speech.BartenderBubbles.DismissAll();
                float startedAt = (float)speech.LifeSeconds;
                for (int index = 0; index < roles.Length; index++)
                {
                    string text = ExistingMultilineLine((BarConversationKind)(roles[index] / 2), roles[index]);
                    Assert.That(view.ShowAt(speech.Speaker(roles[index]), text, startedAt,
                        NpcSpeechBubbleView.VisibleSeconds), Is.True);
                }
                NpcSpeechVoice service = NpcSpeechVoice.Instance;
                Assert.That(service, Is.Not.Null);
                float readyDeadline = Time.realtimeSinceStartup + 3f;
                do
                {
                    view.AdvanceTo(startedAt);
                    yield return null;
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(readyDeadline),
                        "Both short NPC phrases must become ready without advancing their owner clock.");
                }
                while (service.RunningSynthesisCount != 0 || service.ActiveSynthesizedClipCount < roles.Length);
                view.AdvanceTo(startedAt);
                // Seek sober first: at peak the intentional tape slowdown
                // otherwise leaves too few revealed words to review both rows.
                view.AdvanceTo(startedAt + 2.8f);
                foreach (int role in roles)
                    Assert.That(view.RevealedTextOf(speech.Speaker(role)), Does.Contain("\n"));

                Vector3 position = bar.Room.TransformPoint(new Vector3(-1.4f, 1.9f, -1.55f));
                camera.transform.SetPositionAndRotation(position,
                    Quaternion.LookRotation(bar.Room.TransformPoint(new Vector3(-4.38f, 1.25f, -1.55f)) - position));
                camera.fieldOfView = 65f;
                FocusGameView();
                yield return null;
                yield return null;
                yield return CaptureSpeechReview("bar-translucent-npc-speech.png");
                GameSessionState.UpdateDrinkingProgress(100, previousDrink, previousDrinks);
                GameTimeScaleRuntime.SetIntoxicationLevel(100f);
                view.AdvanceTo(startedAt + 3.05f);
                FocusGameView();
                yield return null;
                yield return null;
                foreach (int role in roles)
                {
                    Assert.That(view.ScatterOf(speech.Speaker(role)), Is.EqualTo(1f));
                    Vector3 point = camera.WorldToViewportPoint(speech.Head(role).position);
                    Assert.That(point.z, Is.GreaterThan(0f));
                    Assert.That(point.x, Is.InRange(.05f, .95f), "Both scattered NPC bubbles belong in the review frame.");
                    Assert.That(point.y, Is.InRange(.05f, .95f));
                }
                Assert.That(view.LastRenderedScatteredBubbleCount, Is.EqualTo(roles.Length));
                Assert.That(view.LastRenderedMaximumScatterRows, Is.GreaterThanOrEqualTo(2));
                AssertScatterPivotStaysOnCanvas(view.LastRenderedPanelRect);
                int layoutBuilds = view.ScatterLayoutBuildCount;
                view.AdvanceTo(startedAt + 3.05f);
                yield return null;
                yield return null;
                Assert.That(view.ScatterLayoutBuildCount, Is.EqualTo(layoutBuilds),
                    "Simultaneous multiline bubbles must retain their own layouts across GUI events.");
                IntoxicationAudioDriver audio = IntoxicationAudioDriver.EnsureInstalled();
                Assert.That(audio.IsConfigured, Is.True);
                Assert.That(GameAudioMixer.Mixer.GetFloat(IntoxicationAudioDriver.IntensityParameter,
                    out float intensity), Is.True);
                Assert.That(intensity, Is.EqualTo(1f).Within(.001f),
                    "The actual world tape bus remains driven at maximum intoxication.");

                yield return CaptureSpeechReview("bar-intoxicated-npc-speech-scatter.png");
            }
            finally
            {
                GameSessionState.UpdateDrinkingProgress(previousLevel, previousDrink, previousDrinks);
                GameTimeScaleRuntime.SetIntoxicationLevel(previousPresentation);
            }
            foreach (int role in roles)
                Assert.That(view.ScatterOf(speech.Speaker(role)), Is.Zero,
                    "NPC scatter must recover with the shared presentation level.");
        }

        private static IEnumerator CaptureSpeechReview(string name)
        {
            string capture = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", SceneIds.BarInterior, name));
            Directory.CreateDirectory(Path.GetDirectoryName(capture));
            DateTime previousWrite = File.Exists(capture) ? File.GetLastWriteTimeUtc(capture) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(capture);
            float deadline = Time.realtimeSinceStartup + 3f;
            do { yield return null; }
            while ((!File.Exists(capture) || File.GetLastWriteTimeUtc(capture) <= previousWrite) &&
                Time.realtimeSinceStartup < deadline);
            Assert.That(File.Exists(capture) && File.GetLastWriteTimeUtc(capture) > previousWrite, Is.True);
        }

        private static string ExistingMultilineLine(BarConversationKind kind, int role)
        {
            string longest = string.Empty;
            for (int variant = 0; variant < BarConversationCatalog.Count(kind); variant++)
            {
                BarConversationExchange exchange = BarConversationCatalog.Get(kind, variant);
                string text = LocalizationService.Get(role == exchange.FirstRole ? exchange.FirstKey : exchange.SecondKey);
                if (text.Length > longest.Length && text.IndexOf(". ", StringComparison.Ordinal) >= 0) longest = text;
            }
            Assert.That(longest, Is.Not.Empty, "Use an existing two-sentence NPC line for the multiline review.");
            int boundary = longest.IndexOf(". ", StringComparison.Ordinal);
            return longest.Substring(0, boundary + 1) + "\n" + longest.Substring(boundary + 2);
        }

        private static void AssertScatterPivotStaysOnCanvas(Rect panel)
        {
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            Matrix4x4 canvasMatrix = Matrix4x4.TRS(
                new Vector3(canvas.ScreenOffset.x, canvas.ScreenOffset.y, 0f), Quaternion.identity,
                new Vector3(canvas.Scale, canvas.Scale, 1f));
            // Exercise the same helper and actual translated/scaled canvas as
            // the visible glyphs. Rotation may tilt a glyph, never move its pivot.
            foreach (Vector2 pivot in new[] { panel.position, panel.center, new Vector2(panel.xMax, panel.yMax) })
            foreach (float angle in new[] { -SpeechScatterLayout.MaximumRotationDegrees,
                0f, SpeechScatterLayout.MaximumRotationDegrees })
            {
                Matrix4x4 glyphMatrix = SpeechScatterLayout.ResolveGlyphMatrix(canvasMatrix, pivot, angle);
                Vector3 expected = canvasMatrix.MultiplyPoint3x4(pivot);
                Vector3 actual = glyphMatrix.MultiplyPoint3x4(pivot);
                Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.002f),
                    "Glyph rotation must preserve its screen pivot through the retro canvas transform.");
                Assert.That(glyphMatrix.MultiplyVector(Vector3.right).magnitude,
                    Is.EqualTo(canvas.Scale).Within(.001f));
            }
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
                Assert.That(View(role).IsDeclared(speech.Speaker(role)),
                    Is.EqualTo(role == BarConversationController.BartenderRole || BarConversationCatalog.CanSpeak(role)),
                    "Declaration for role " + role);
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

            int groups = 0, eventStarts = 0, bartenderLines = 0;
            bool checkExchanges = true, serviceOverlappedGuests = false;
            var completed = new int[BarConversationCatalog.KindCount];
            var awaitingReply = new string[BarConversationCatalog.KindCount];
            var variants = new HashSet<string>[BarConversationCatalog.KindCount];
            for (int group = 0; group < variants.Length; group++) variants[group] = new HashSet<string>();
            speech.LineStarted += ObserveLine;

            int firstOverlapStep = -1, densitySamples = 0;
            double activeLineSamples = 0d;
            for (int step = 0; step < 600 && (step < 450 || !EveryPairCompletedTwice()); step++)
            {
                Step(.1f);
                if (speech.ActiveLineCount >= 2 && firstOverlapStep < 0) firstOverlapStep = step;
                if (step >= 60)
                {
                    densitySamples++;
                    activeLineSamples += speech.ActiveLineCount;
                }
            }
            Assert.That(groups, Is.EqualTo(BarConversationCatalog.EnabledGroups));
            Assert.That(EveryPairCompletedTwice(), Is.True, "Every pair must retain and finish its own A→B exchanges.");
            Assert.That(firstOverlapStep, Is.InRange(0, 59), "The room must become conversational shortly after entry.");
            Assert.That(activeLineSamples / densitySamples, Is.GreaterThanOrEqualTo(2d),
                "A lively bar sustains overlapping voices instead of one room-wide turn queue.");
            checkExchanges = false;

            // Keep the actual room, furniture, faces and post-processing in
            // view. A camera render alone cannot contain the shared IMGUI UI.
            camera.transform.position = bar.Room.TransformPoint(new Vector3(3.2f, 1.8f, -2.7f));
            camera.transform.LookAt(bar.Room.TransformPoint(new Vector3(-2.5f, 1.45f, .25f)));
            camera.fieldOfView = 70f;
            int captureSteps = 0;
            while (VisibleGuestCount() < 2 && captureSteps++ < 200) Step(.1f);
            Assert.That(VisibleGuestCount(), Is.GreaterThanOrEqualTo(2),
                "The production room frame must contain at least two simultaneously speaking guests.");
            FocusGameView();
            yield return null;
            yield return new WaitForEndOfFrame();
            NpcSpeechBubbleView visible = speech.GuestBubbles;
            Assert.That(visible.HasRenderedLayout, Is.True);
            Assert.That(visible.LastRenderedBubbleCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(visible.LastRenderedText, Is.Not.Empty);
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
            var heldText = new string[BarConversationController.BartenderRole + 1];
            for (int role = 0; role < heldText.Length; role++) heldText[role] = View(role).RevealedTextOf(speech.Speaker(role));
            using (GameTimeScaleRuntime.AcquirePause())
            {
                Step(1f);
                yield return null;
                Assert.That(speech.LifeSeconds, Is.EqualTo(heldClock));
                for (int role = 0; role < heldText.Length; role++)
                    Assert.That(View(role).RevealedTextOf(speech.Speaker(role)), Is.EqualTo(heldText[role]));
                Assert.That(speech.GuestBubbles.RenderEnabled || speech.BartenderBubbles.RenderEnabled, Is.False);
            }
            yield return null;

            bar.Player.Motor.Teleport(bar.Room.TransformPoint(new Vector3(-3.85f, PlayerFactory.GroundedRootOffset, 6.35f)));
            Step(.1f);
            AssertQuiet("WC crossing cancels all channels and their delayed replies.");
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
            Assert.That(serviceOverlappedGuests, Is.True, "Service comments speak alongside independent visitor pairs.");

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
                Assert.That(View(role).IsDeclared(speech.Speaker(role)),
                    Is.EqualTo(role == BarConversationController.BartenderRole || BarConversationCatalog.CanSpeak(role)));
            Assert.That(speech.PendingCommentCount, Is.Zero);

            NpcSpeechBubbleView View(int role) => role == BarConversationController.BartenderRole ? speech.BartenderBubbles : speech.GuestBubbles;

            void ObserveLine(int role, string key)
            {
                eventStarts++;
                Assert.That(role == BarConversationController.BartenderRole || BarConversationCatalog.CanSpeak(role), Is.True);
                Assert.That(View(role).RevealedTextOf(speech.Speaker(role)), Is.Not.Empty,
                    "Only a revealed line spends a shuffle entry.");
                Assert.That(LocalizationService.Get(key), Is.Not.EqualTo(key));
                if (role == BarConversationController.BartenderRole)
                {
                    bartenderLines++;
                    return;
                }
                if (!checkExchanges) return;
                int group = role / 2;
                BarConversationTurn turn = speech.GetSchedule((BarConversationKind)group).Current;
                Assert.That(turn.IsSpeaking, Is.True);
                Assert.That(turn.SpeakerRole, Is.EqualTo(role));
                Assert.That(turn.LineKey, Is.EqualTo(key));
                if (awaitingReply[group] == null)
                {
                    Assert.That(key, Is.EqualTo(turn.Exchange.FirstKey));
                    Assert.That(variants[group].Add(key), Is.True,
                        "A pair repeated before its own shuffle pool was exhausted.");
                    awaitingReply[group] = turn.Exchange.SecondKey;
                }
                else
                {
                    Assert.That(key, Is.EqualTo(awaitingReply[group]),
                        "An overlapping conversation must retain its authored reply.");
                    groups |= 1 << group;
                    completed[group]++;
                    awaitingReply[group] = null;
                }
            }

            bool EveryPairCompletedTwice()
            {
                for (int group = 0; group < completed.Length; group++)
                    if (BarConversationCatalog.IsEnabled((BarConversationKind)group) && completed[group] < 2) return false;
                return true;
            }

            int VisibleGuestCount()
            {
                int count = 0;
                for (int role = 0; role < BarConversationController.BartenderRole; role++)
                {
                    if (!speech.GuestBubbles.IsShowing(speech.Speaker(role))) continue;
                    Vector3 point = camera.WorldToViewportPoint(speech.Head(role).position + Vector3.up * .2f);
                    if (point.z > 0f && point.x > .05f && point.x < .95f && point.y > .08f && point.y < .9f) count++;
                }
                return count;
            }

            void Step(float seconds)
            {
                int prior = speech.StartedLineCount, previousEvents = eventStarts;
                speech.Advance(seconds);
                Assert.That(speech.ActiveLineCount, Is.InRange(0, BarConversationController.MaximumSimultaneousLines));
                Assert.That(speech.PendingCommentCount, Is.LessThanOrEqualTo(BarConversationController.PendingCommentCapacity));
                int showing = 0, guestsShowing = 0;
                for (int role = 0; role <= BarConversationController.BartenderRole; role++)
                {
                    if (!View(role).IsShowing(speech.Speaker(role))) continue;
                    showing++;
                    if (role < BarConversationController.BartenderRole) guestsShowing++;
                }
                Assert.That(showing, Is.EqualTo(speech.ActiveLineCount));
                for (int group = 0; group < BarConversationCatalog.KindCount; group++)
                    Assert.That(speech.GuestBubbles.IsShowing(speech.Speaker(group * 2)) &&
                                speech.GuestBubbles.IsShowing(speech.Speaker(group * 2 + 1)), Is.False,
                        "Partners in one exchange cannot talk over their own answer.");
                Assert.That(speech.StartedLineCount - prior, Is.EqualTo(eventStarts - previousEvents),
                    "Every first-glyph acknowledgement must be observable even when several start in one step.");
                if (guestsShowing > 0 && speech.BartenderBubbles.IsShowing(speech.Speaker(BarConversationController.BartenderRole)))
                    serviceOverlappedGuests = true;
            }

            void AssertQuiet(string message)
            {
                Assert.That(speech.ActiveLineCount, Is.Zero, message);
                Assert.That(speech.PendingCommentCount, Is.Zero, message);
                for (int group = 0; group < BarConversationCatalog.KindCount; group++)
                    Assert.That(speech.GetSchedule((BarConversationKind)group).Current.HasExchange, Is.False, message);
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
                for (int step = 0; step < 400 && (bartenderLines < expected || speech.PendingCommentCount > 0 || speech.BartenderBubbles.IsShowing(speech.Speaker(BarConversationController.BartenderRole))); step++)
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
