using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The drunk hero holding it down: the clock that decides when a bout
    /// comes, the gauge that runs it, the body's side of it, the geometry
    /// of the instrument beside him, the stomach under it, the key it
    /// borrows — and the proof that none of it reaches for the mutter or
    /// a citizen.
    /// </summary>
    public sealed class HeroNauseaTests
    {
        private const float Step = 1f / 60f;
        private const int Seed = 4131;

        [Serializable]
        private sealed class Catalog
        {
            public CatalogEntry[] entries;
        }

        [Serializable]
        private sealed class CatalogEntry
        {
            public string key;
            public string value;
        }

        // ---- the clock -----------------------------------------------

        [Test]
        public void Clock_SameSeedReplaysAndRestsShortenWithPace()
        {
            var first = new HeroNauseaClock(Seed);
            var second = new HeroNauseaClock(Seed);

            Assert.That(
                first.RestDuration,
                Is.EqualTo(HeroNauseaClock.InitialRestSeconds));
            RunUntilCue(first);
            RunUntilCue(second);

            first.ArmRest(0f);
            second.ArmRest(0f);
            Assert.That(first.RestDuration, Is.EqualTo(second.RestDuration));
            Assert.That(
                first.RestDuration,
                Is.InRange(
                    HeroNauseaClock.SlowRestMinimumSeconds,
                    HeroNauseaClock.SlowRestMaximumSeconds));

            RunUntilCue(first);
            first.ArmRest(1f);
            Assert.That(
                first.RestDuration,
                Is.InRange(
                    HeroNauseaClock.FastRestMinimumSeconds,
                    HeroNauseaClock.FastRestMaximumSeconds));

            HeroNauseaClock.ResolveRestRange(0f, out float slowMin, out float slowMax);
            HeroNauseaClock.ResolveRestRange(1f, out float fastMin, out float fastMax);
            Assert.That(slowMin, Is.EqualTo(15f));
            Assert.That(slowMax, Is.EqualTo(25f));
            Assert.That(fastMin, Is.EqualTo(8f));
            Assert.That(fastMax, Is.EqualTo(14f));
            Assert.That(
                fastMax,
                Is.GreaterThan(HeroNauseaGaugeModel.ExpectedDurationSeconds(1f)),
                "Even the shortest rest outlasts a bout: the gauge is never up twice at once.");
        }

        [Test]
        public void Clock_AClosedGateRearmsTheFullRest()
        {
            var clock = new HeroNauseaClock(Seed);
            Run(clock, 15f, true);
            Assert.That(clock.RestElapsed, Is.GreaterThan(14f));

            clock.Advance(Step, false);
            Assert.That(clock.RestElapsed, Is.EqualTo(0f));
            Assert.That(
                clock.RestDuration,
                Is.EqualTo(HeroNauseaClock.InitialRestSeconds));

            Run(clock, 19f, true);
            Assert.That(clock.ConsumeBoutCue(), Is.False, "Nineteen seconds is not twenty.");
            Run(clock, 1.5f, true);
            Assert.That(clock.ConsumeBoutCue(), Is.True);
            Assert.That(clock.ConsumeBoutCue(), Is.False, "A cue is consumed once.");
        }

        [Test]
        public void Clock_HugeStepDoesNotSkipTheRestAndABoutHoldsIt()
        {
            var clock = new HeroNauseaClock(Seed);
            clock.Advance(10f, true);
            Assert.That(clock.ConsumeBoutCue(), Is.False);
            Assert.That(
                clock.RestElapsed,
                Is.LessThanOrEqualTo(HeroNauseaClock.MaximumStepSeconds));

            RunUntilCue(clock);
            Assert.That(clock.IsInBout, Is.True);
            clock.Advance(100f, true);
            clock.Advance(100f, false);
            Assert.That(clock.ConsumeBoutCue(), Is.False, "The gauge owns the time during a bout.");
            Assert.That(clock.IsInBout, Is.True);
        }

        [Test]
        public void Clock_PaceAndStageAreTheLastStagesOwn()
        {
            Assert.That(HeroNauseaClock.FirstLevel, Is.EqualTo(81));
            Assert.That(HeroNauseaClock.ResolvePace(81), Is.EqualTo(0f));
            Assert.That(HeroNauseaClock.ResolvePace(100), Is.EqualTo(1f));
            Assert.That(HeroNauseaClock.ResolvePace(60), Is.EqualTo(0f));
            Assert.That(HeroNauseaClock.ResolvePace(90), Is.InRange(0.45f, 0.5f));

            Assert.That(HeroNauseaClock.IsNauseaStage(0), Is.False);
            Assert.That(HeroNauseaClock.IsNauseaStage(60), Is.False);
            Assert.That(HeroNauseaClock.IsNauseaStage(80), Is.False);
            Assert.That(HeroNauseaClock.IsNauseaStage(81), Is.True);
            Assert.That(HeroNauseaClock.IsNauseaStage(100), Is.True);
        }

        // ---- the gauge -----------------------------------------------

        [Test]
        public void Gauge_TheHeldKeyLiftsTheMarkerAndGravityDropsIt()
        {
            var gauge = new HeroNauseaGaugeModel();
            gauge.Begin(0f, Seed);
            Assert.That(gauge.IsRunning, Is.True);
            Assert.That(gauge.Marker, Is.EqualTo(HeroNauseaGaugeModel.ZoneStart));
            Assert.That(gauge.IsInside, Is.True, "The bout opens with him inside the band.");

            gauge.Advance(0.5f, true);
            float raised = gauge.Marker;
            Assert.That(raised, Is.GreaterThan(HeroNauseaGaugeModel.ZoneStart));
            Assert.That(
                gauge.MarkerVelocity,
                Is.EqualTo(HeroNauseaGaugeModel.MaximumRiseSpeed).Within(0.001f),
                "Half a second of lift reaches the speed cap.");

            // Let go: the marker coasts up on its momentum and gravity
            // takes exactly the cap's worth of speed back in half a second.
            gauge.Advance(0.5f, false);
            Assert.That(gauge.Marker, Is.GreaterThan(raised), "It does not stop dead when the key is released.");
            Assert.That(gauge.MarkerVelocity, Is.LessThanOrEqualTo(0.001f));
            for (int index = 0; index < 180 && gauge.IsRunning; index++)
            {
                gauge.Advance(Step, false);
            }

            Assert.That(gauge.Marker, Is.EqualTo(0f), "Released, the marker ends on the floor of the track.");
        }

        [Test]
        public void Gauge_APlayerWhoKeepsUpWinsInTime()
        {
            for (int pace = 0; pace <= 1; pace++)
            {
                var gauge = new HeroNauseaGaugeModel();
                gauge.Begin(pace, Seed + pace);
                float worstStrain = 0f;
                int guard = 0;
                while (gauge.IsRunning && guard++ < 60 * 30)
                {
                    gauge.Advance(Step, gauge.Marker < gauge.ZoneCenter);
                    worstStrain = Mathf.Max(worstStrain, gauge.Strain);
                }

                Assert.That(gauge.Outcome, Is.EqualTo(HeroNauseaOutcome.Success), $"pace {pace}");
                Assert.That(gauge.Elapsed, Is.InRange(5f, 9f), $"pace {pace}");
                Assert.That(worstStrain, Is.LessThan(0.5f), $"pace {pace}: keeping up never gets close to losing it.");
                Assert.That(gauge.ZoneCenter, Is.EqualTo(HeroNauseaGaugeModel.ZoneEnd));
            }

            Assert.That(
                HeroNauseaGaugeModel.ExpectedDurationSeconds(0f),
                Is.GreaterThan(HeroNauseaGaugeModel.ExpectedDurationSeconds(1f)),
                "The drunker, the faster the band climbs.");
        }

        [Test]
        public void Gauge_NeverHoldingLosesItBeforeTheBandArrives()
        {
            var gauge = new HeroNauseaGaugeModel();
            gauge.Begin(0f, Seed);
            int guard = 0;
            while (gauge.IsRunning && guard++ < 60 * 30)
            {
                gauge.Advance(Step, false);
            }

            Assert.That(gauge.Outcome, Is.EqualTo(HeroNauseaOutcome.Fail));
            Assert.That(gauge.Strain, Is.EqualTo(1f));
            Assert.That(
                gauge.Elapsed,
                Is.LessThan(HeroNauseaGaugeModel.ExpectedDurationSeconds(0f) * 0.85f));
        }

        [Test]
        public void Gauge_TheSameSeedAndKeysReplayExactly()
        {
            var first = new HeroNauseaGaugeModel();
            var second = new HeroNauseaGaugeModel();
            first.Begin(0.5f, Seed);
            second.Begin(0.5f, Seed);
            Assert.That(first.ZoneSpeed, Is.EqualTo(second.ZoneSpeed));

            for (int frame = 0; frame < 60 * 6 && first.IsRunning; frame++)
            {
                bool held = (frame / 20) % 3 != 0;
                first.Advance(Step, held);
                second.Advance(Step, held);
                Assert.That(second.Marker, Is.EqualTo(first.Marker));
                Assert.That(second.Strain, Is.EqualTo(first.Strain));
            }

            Assert.That(second.Outcome, Is.EqualTo(first.Outcome));

            var other = new HeroNauseaGaugeModel();
            other.Begin(0.5f, Seed + 1);
            Assert.That(
                other.ZoneSpeed,
                Is.Not.EqualTo(first.ZoneSpeed),
                "Another seed climbs at another speed.");
        }

        [Test]
        public void Gauge_EdgesHitchesAndCancel()
        {
            // Exactly representable edges: a quarter either side of a half.
            Assert.That(HeroNauseaGaugeModel.IsInsideZone(0.5f, 0.5f, 0.25f), Is.True);
            Assert.That(HeroNauseaGaugeModel.IsInsideZone(0.75f, 0.5f, 0.25f), Is.True);
            Assert.That(HeroNauseaGaugeModel.IsInsideZone(0.25f, 0.5f, 0.25f), Is.True);
            Assert.That(HeroNauseaGaugeModel.IsInsideZone(0.76f, 0.5f, 0.25f), Is.False);
            Assert.That(HeroNauseaGaugeModel.IsInsideZone(0.24f, 0.5f, 0.25f), Is.False);

            var gauge = new HeroNauseaGaugeModel();
            gauge.Begin(0f, Seed);
            gauge.Advance(10f, false);
            Assert.That(
                gauge.Elapsed,
                Is.LessThanOrEqualTo(HeroNauseaGaugeModel.MaximumAdvanceSeconds + 0.001f),
                "A frozen frame cannot decide the bout.");
            Assert.That(gauge.IsRunning, Is.True);

            gauge.Advance(Step, true);
            gauge.Cancel();
            Assert.That(gauge.IsRunning, Is.False);
            Assert.That(gauge.Outcome, Is.EqualTo(HeroNauseaOutcome.None));
            gauge.Advance(1f, true);
            Assert.That(gauge.IsRunning, Is.False, "A cancelled bout does not resume.");
        }

        // ---- the body ------------------------------------------------

        [Test]
        public void Pose_TheHandComesUpAndGoesDownOnItsOwnClocks()
        {
            var model = new PlayerNauseaModel(Seed);
            Assert.That(model.IsInert, Is.True);
            Assert.That(model.Pose.IsNone, Is.True);

            Run(model, PlayerNauseaRules.HandBlendInSeconds + Step, true);
            Assert.That(model.HandWeight, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(model.Pose.Active, Is.True);
            Assert.That(model.IsInert, Is.False);

            Run(model, PlayerNauseaRules.HandBlendInSeconds * 0.5f, false);
            Assert.That(model.HandWeight, Is.GreaterThan(0f), "It comes down slower than it went up.");
            Run(model, PlayerNauseaRules.HandBlendOutSeconds, false);
            Assert.That(model.HandWeight, Is.EqualTo(0f));
            Assert.That(model.Pose.Active, Is.False);

            model.Advance(Step, true, 0.4f);
            Assert.That(model.Pose.Strain, Is.EqualTo(0.4f).Within(0.0001f));
            model.Advance(Step, false, 0.4f);
            Assert.That(model.Pose.Strain, Is.EqualTo(0f), "Strain belongs to a running bout.");
        }

        [Test]
        public void Pose_HiccupsComeOnSeededIntervalsAndTheFirstAnnouncesTheBout()
        {
            var model = new PlayerNauseaModel(Seed);
            var replay = new PlayerNauseaModel(Seed);
            var cues = new List<float>();
            float time = 0f;
            float peak = 0f;
            for (int frame = 0; frame < 60 * 40; frame++)
            {
                model.Advance(Step, true, 0f);
                replay.Advance(Step, true, 0f);
                time += Step;
                peak = Mathf.Max(peak, model.HiccupAmount);
                bool cue = model.ConsumeHiccupCue();
                Assert.That(replay.ConsumeHiccupCue(), Is.EqualTo(cue), "The same seed hiccups on the same frames.");
                if (cue)
                {
                    cues.Add(time);
                }
            }

            Assert.That(cues.Count, Is.GreaterThanOrEqualTo(8));
            Assert.That(cues[0], Is.LessThanOrEqualTo(PlayerNauseaRules.FirstHiccupDelaySeconds + Step * 1.5f));
            for (int index = 1; index < cues.Count; index++)
            {
                float interval = cues[index] - cues[index - 1];
                Assert.That(
                    interval,
                    Is.InRange(
                        PlayerNauseaRules.HiccupIntervalMinimumSeconds - Step,
                        PlayerNauseaRules.HiccupIntervalMaximumSeconds + Step * 1.5f));
            }

            Assert.That(peak, Is.LessThanOrEqualTo(1f));
            Assert.That(peak, Is.GreaterThan(0.9f));
            Assert.That(PlayerNauseaRules.HiccupShape(0f), Is.EqualTo(0f));
            Assert.That(PlayerNauseaRules.HiccupShape(PlayerNauseaRules.HiccupRiseSeconds), Is.EqualTo(1f));
            Assert.That(PlayerNauseaRules.HiccupShape(PlayerNauseaRules.HiccupSeconds), Is.EqualTo(0f));

            model.Advance(Step, false, 0f);
            model.ConsumeHiccupCue();
            for (int frame = 0; frame < 60 * 10; frame++)
            {
                model.Advance(Step, false, 0f);
                Assert.That(model.ConsumeHiccupCue(), Is.False, "No hiccups outside a bout.");
            }

            Assert.That(model.IsInert, Is.True);
        }

        // ---- the instrument ------------------------------------------

        [Test]
        public void Layout_StaysInsideTheCanvasAndToTheRightOfHim()
        {
            var anchors = new[]
            {
                new Vector2(320f, 180f),
                new Vector2(0f, 0f),
                new Vector2(640f, 360f),
                new Vector2(10f, 350f),
                new Vector2(630f, 5f),
                new Vector2(-50f, 400f)
            };
            var canvas = new Rect(0f, 0f, RetroUiTheme.LogicalWidth, RetroUiTheme.LogicalHeight);

            for (int index = 0; index < anchors.Length; index++)
            {
                Rect track = IntoxicationNauseaGaugeView.ResolveTrackRect(anchors[index]);
                Rect icon = IntoxicationNauseaGaugeView.ResolveIconRect(track);
                Rect verdict = IntoxicationNauseaGaugeView.ResolveVerdictRect(icon);
                AssertInside(track, canvas, $"track for {anchors[index]}");
                AssertInside(icon, canvas, $"icon for {anchors[index]}");
                AssertInside(verdict, canvas, $"verdict for {anchors[index]}");
                Assert.That(icon.y, Is.GreaterThanOrEqualTo(track.yMax));
                Assert.That(verdict.y, Is.GreaterThanOrEqualTo(icon.yMax));
                Assert.That(track.width, Is.EqualTo(IntoxicationNauseaGaugeView.TrackWidth));
                Assert.That(track.height, Is.EqualTo(IntoxicationNauseaGaugeView.TrackHeight));
                Assert.That(Mathf.Abs(icon.center.x - track.center.x), Is.LessThanOrEqualTo(0.5f));
            }

            Rect centred = IntoxicationNauseaGaugeView.ResolveTrackRect(new Vector2(320f, 180f));
            Assert.That(
                centred.x,
                Is.EqualTo(320f + IntoxicationNauseaGaugeView.AnchorClearance),
                "With room, the track starts a clearance to his right.");
            Assert.That(centred.center.y, Is.EqualTo(180f).Within(0.5f));
        }

        [Test]
        public void Layout_TheTrackReadsUpward()
        {
            var inner = new Rect(100f, 50f, 8f, 100f);
            Assert.That(
                IntoxicationNauseaGaugeView.MapToTrackY(inner, 1f),
                Is.LessThan(IntoxicationNauseaGaugeView.MapToTrackY(inner, 0f)));
            Assert.That(IntoxicationNauseaGaugeView.MapToTrackY(inner, 0f), Is.EqualTo(inner.yMax));
            Assert.That(IntoxicationNauseaGaugeView.MapToTrackY(inner, 1f), Is.EqualTo(inner.y));

            Rect zone = IntoxicationNauseaGaugeView.ResolveZoneRect(inner, 0.5f, 0.1f);
            Assert.That(zone.height, Is.EqualTo(20f).Within(0.001f));
            Assert.That(zone.center.y, Is.EqualTo(100f).Within(0.001f));
            AssertInside(zone, inner, "zone");

            Rect clipped = IntoxicationNauseaGaugeView.ResolveZoneRect(inner, 0.95f, 0.1f);
            AssertInside(clipped, inner, "clipped zone");
            Assert.That(clipped.y, Is.EqualTo(inner.y));

            Assert.That(
                IntoxicationNauseaGaugeView.VerdictKey(HeroNauseaOutcome.Success),
                Is.EqualTo("hero.nausea.result.success"));
            Assert.That(
                IntoxicationNauseaGaugeView.VerdictKey(HeroNauseaOutcome.Fail),
                Is.EqualTo("hero.nausea.result.fail"));
        }

        [TestCase("Localization/ru")]
        [TestCase("Localization/en")]
        public void Verdict_WordsResolveInBothCatalogs(string resourcePath)
        {
            Dictionary<string, string> values = LoadCatalog(resourcePath);
            string[] keys =
            {
                IntoxicationNauseaGaugeView.VerdictKey(HeroNauseaOutcome.Success),
                IntoxicationNauseaGaugeView.VerdictKey(HeroNauseaOutcome.Fail),
                "debug.nausea.trigger"
            };
            for (int index = 0; index < keys.Length; index++)
            {
                Assert.That(values.ContainsKey(keys[index]), Is.True, $"{resourcePath} is missing '{keys[index]}'.");
                Assert.That(values[keys[index]], Is.Not.Empty);
                Assert.That(values[keys[index]].Contains("!"), Is.False, "§21: no exclamation marks.");
            }
        }

        [Test]
        public void Icon_IsSixteenPixelsOfDarkGreen()
        {
            Texture2D icon = IntoxicationNauseaIconLibrary.GetStomachIcon();
            Assert.That(icon, Is.Not.Null);
            Assert.That(icon.width, Is.EqualTo(IntoxicationNauseaIconLibrary.IconSize));
            Assert.That(icon.height, Is.EqualTo(IntoxicationNauseaIconLibrary.IconSize));
            Assert.That(icon.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(IntoxicationNauseaIconLibrary.StomachRows.Length, Is.EqualTo(16));
            for (int row = 0; row < IntoxicationNauseaIconLibrary.StomachRows.Length; row++)
            {
                Assert.That(
                    IntoxicationNauseaIconLibrary.StomachRows[row].Length,
                    Is.EqualTo(16),
                    $"row {row}");
            }

            Color32[] pixels = icon.GetPixels32();
            int opaque = 0;
            for (int index = 0; index < pixels.Length; index++)
            {
                Color32 pixel = pixels[index];
                if (pixel.a == 0)
                {
                    continue;
                }

                opaque++;
                Assert.That(pixel.g, Is.GreaterThan(pixel.r), $"pixel {index} is not green-led");
                Assert.That(pixel.g, Is.GreaterThan(pixel.b), $"pixel {index} is not green-led");
                Assert.That(Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)), Is.LessThan(140), $"pixel {index} is not dark");
            }

            Assert.That(opaque, Is.GreaterThan(60));
            Assert.That(opaque, Is.LessThan(200));
            Assert.That(IntoxicationNauseaIconLibrary.GetStomachIcon(), Is.SameAs(icon), "The icon is drawn once.");
        }

        // ---- the key -------------------------------------------------

        [Test]
        public void Interactor_ClaimingTheKeyLeavesTheInteractorEnabled()
        {
            var gameObject = new GameObject("Nausea Interactor Test");
            try
            {
                PlayerInteractor interactor = gameObject.AddComponent<PlayerInteractor>();
                Assert.That(interactor.InputEnabled, Is.True);
                Assert.That(interactor.InteractKeyClaimed, Is.False);

                interactor.SetInteractKeyClaimed(true);
                Assert.That(interactor.InteractKeyClaimed, Is.True);
                Assert.That(
                    interactor.InputEnabled,
                    Is.True,
                    "The balance model and the fall gate read InputEnabled; the claim must not touch it.");
                Assert.That(interactor.ActiveInteractable, Is.Null);

                interactor.SetInteractKeyClaimed(false);
                Assert.That(interactor.InteractKeyClaimed, Is.False);
                Assert.That(interactor.InputEnabled, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        // ---- the hand-off --------------------------------------------

        /// <summary>
        /// A lost bout raises the fail cue once, after the gauge has
        /// resolved and not before, and the key is his again by then —
        /// the vomiting claims it afresh in the same Update.
        /// </summary>
        [Test]
        public void Fail_RaisesTheFailCueExactlyOnce()
        {
            using (var rig = new ControllerRig())
            {
                IntoxicationNauseaController controller = rig.Controller;
                Assert.That(controller.ConsumeFailCue(), Is.False, "Nothing is due before a bout.");
                Assert.That(controller.DebugForceBout(), Is.True);
                Assert.That(controller.IsBoutActive, Is.True);
                Assert.That(rig.Interactor.InteractKeyClaimed, Is.True);

                // Nobody holds the key in EditMode: the gauge is lost.
                int guard = 0;
                while (guard++ < 60 * 30)
                {
                    controller.Tick(Step, false, false);
                    if (!controller.IsBoutActive)
                    {
                        break;
                    }

                    Assert.That(controller.ConsumeFailCue(), Is.False, "No cue while the bout still runs.");
                }

                Assert.That(controller.IsBoutActive, Is.False, "The bout resolved.");
                Assert.That(controller.Verdict, Is.EqualTo(HeroNauseaOutcome.Fail));
                Assert.That(controller.Fails, Is.EqualTo(1));
                Assert.That(rig.Interactor.InteractKeyClaimed, Is.False, "The key is released on resolve.");
                Assert.That(controller.ConsumeFailCue(), Is.True, "The fail is handed on once.");
                Assert.That(controller.ConsumeFailCue(), Is.False, "And only once.");

                controller.Tick(Step, false, false);
                Assert.That(controller.ConsumeFailCue(), Is.False, "A later frame does not raise it again.");
            }
        }

        /// <summary>
        /// A second lost bout raises the cue again, and a shutdown drops
        /// one nobody has read.
        /// </summary>
        [Test]
        public void Fail_TheCueIsPerBoutAndShutdownDropsIt()
        {
            using (var rig = new ControllerRig())
            {
                IntoxicationNauseaController controller = rig.Controller;
                LoseABout(controller);
                Assert.That(controller.ConsumeFailCue(), Is.True);

                LoseABout(controller);
                Assert.That(controller.Fails, Is.EqualTo(2));
                controller.Shutdown();
                Assert.That(controller.ConsumeFailCue(), Is.False, "Shutdown drops an unread cue.");
                Assert.That(rig.Interactor.InteractKeyClaimed, Is.False);
            }
        }

        /// <summary>
        /// The suspension the vomiting asks for: the clock's gate closes
        /// without touching anything else, and a closed gate rearms the
        /// full twenty seconds — after being sick he is left alone.
        /// </summary>
        [Test]
        public void Clock_ASuspendedGateRearmsTheFullRest()
        {
            using (var rig = new ControllerRig())
            {
                GameSessionState.UpdateDrinkingProgress(90, DrinkId.Vodka, 6);
                try
                {
                    IntoxicationNauseaController controller = rig.Controller;
                    Assert.That(controller.CanBegin(false, false), Is.True, "The last stage, on his feet: a bout may come.");

                    Run(controller, 15f, false);
                    Assert.That(controller.Clock.RestElapsed, Is.GreaterThan(14f));
                    Assert.That(controller.BoutsBegun, Is.EqualTo(0), "Fifteen seconds is not twenty.");

                    controller.Tick(Step, false, false, boutsSuspended: true);
                    Assert.That(controller.Clock.RestElapsed, Is.EqualTo(0f));
                    Assert.That(
                        controller.Clock.RestDuration,
                        Is.EqualTo(HeroNauseaClock.InitialRestSeconds));

                    Run(controller, 25f, true);
                    Assert.That(controller.BoutsBegun, Is.EqualTo(0), "Suspended, the clock never cues.");
                    Assert.That(controller.IsBoutActive, Is.False);
                    Assert.That(controller.Clock.RestElapsed, Is.EqualTo(0f));

                    Run(controller, 19f, false);
                    Assert.That(controller.BoutsBegun, Is.EqualTo(0), "Released, the full rest runs again.");
                    Run(controller, 1.5f, false);
                    Assert.That(controller.BoutsBegun, Is.EqualTo(1));
                    Assert.That(controller.IsBoutActive, Is.True);
                }
                finally
                {
                    GameSessionState.ResetDrinkingState();
                }
            }
        }

        // ---- the scope -----------------------------------------------

        /// <summary>
        /// §16.2 stays literally true: the nausea reads the hero and
        /// nothing about the street, and it does not reach into the
        /// mutter either — two channels, two systems.
        /// </summary>
        [Test]
        public void Scope_TheNauseaNamesNoMutterAndNoCitizen()
        {
            string[] forbiddenPrefixes = { "HeroMutter", "IntoxicationMutter", "CityPedestrian" };
            const BindingFlags all =
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly;
            Type[] types = typeof(HeroNauseaClock).Assembly.GetTypes();
            int inspected = 0;
            for (int index = 0; index < types.Length; index++)
            {
                Type type = types[index];
                if (!type.Name.Contains("Nausea"))
                {
                    continue;
                }

                inspected++;
                foreach (FieldInfo field in type.GetFields(all))
                {
                    AssertNotForbidden(field.FieldType, forbiddenPrefixes, $"{type.Name}.{field.Name}");
                }

                foreach (PropertyInfo property in type.GetProperties(all))
                {
                    AssertNotForbidden(property.PropertyType, forbiddenPrefixes, $"{type.Name}.{property.Name}");
                }

                foreach (MethodInfo method in type.GetMethods(all))
                {
                    AssertNotForbidden(method.ReturnType, forbiddenPrefixes, $"{type.Name}.{method.Name}");
                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        AssertNotForbidden(parameter.ParameterType, forbiddenPrefixes, $"{type.Name}.{method.Name}({parameter.Name})");
                    }
                }
            }

            Assert.That(inspected, Is.GreaterThanOrEqualTo(6));
        }

        // ---- helpers -------------------------------------------------

        /// <summary>
        /// The controller over a bare hero: a motor and an interactor on
        /// one object, no presentation and no ragdoll. Enough for the
        /// gates (<see cref="IntoxicationNauseaController.CanRun"/> wants a
        /// live motor and interactor) and for the key.
        /// </summary>
        private sealed class ControllerRig : IDisposable
        {
            private readonly GameObject root;

            public ControllerRig()
            {
                root = new GameObject("Nausea Controller Test");
                Motor = root.AddComponent<PlayerMotor>();
                Interactor = root.AddComponent<PlayerInteractor>();
                var player = new PlayerRuntime(root, Motor, Interactor, null);
                Controller = new IntoxicationNauseaController(player, Seed);
            }

            public PlayerMotor Motor { get; }
            public PlayerInteractor Interactor { get; }
            public IntoxicationNauseaController Controller { get; }

            public void Dispose()
            {
                Controller.Shutdown();
                Object.DestroyImmediate(root);
            }
        }

        private static void LoseABout(IntoxicationNauseaController controller)
        {
            Assert.That(controller.DebugForceBout(), Is.True);
            int guard = 0;
            while (controller.IsBoutActive && guard++ < 60 * 30)
            {
                controller.Tick(Step, false, false);
            }

            Assert.That(controller.Verdict, Is.EqualTo(HeroNauseaOutcome.Fail));
        }

        private static void Run(
            IntoxicationNauseaController controller,
            float seconds,
            bool boutsSuspended)
        {
            int frames = Mathf.CeilToInt(seconds / Step);
            for (int frame = 0; frame < frames; frame++)
            {
                controller.Tick(Step, false, false, boutsSuspended);
            }
        }

        private static void AssertNotForbidden(Type type, string[] prefixes, string where)
        {
            for (int index = 0; index < prefixes.Length; index++)
            {
                Assert.That(
                    type.Name.StartsWith(prefixes[index], StringComparison.Ordinal),
                    Is.False,
                    $"{where} names {type.Name}.");
            }
        }

        private static void AssertInside(Rect inner, Rect outer, string what)
        {
            Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin - 0.001f), what);
            Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin - 0.001f), what);
            Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax + 0.001f), what);
            Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax + 0.001f), what);
        }

        private static void Run(HeroNauseaClock clock, float seconds, bool allowed)
        {
            int frames = Mathf.CeilToInt(seconds / Step);
            for (int frame = 0; frame < frames; frame++)
            {
                clock.Advance(Step, allowed);
            }
        }

        private static void Run(PlayerNauseaModel model, float seconds, bool active)
        {
            int frames = Mathf.CeilToInt(seconds / Step);
            for (int frame = 0; frame < frames; frame++)
            {
                model.Advance(Step, active, 0f);
            }
        }

        private static void RunUntilCue(HeroNauseaClock clock)
        {
            int guard = 0;
            while (guard++ < 60 * 120)
            {
                clock.Advance(Step, true);
                if (clock.ConsumeBoutCue())
                {
                    return;
                }
            }

            Assert.Fail("The clock never cued a bout.");
        }

        private static Dictionary<string, string> LoadCatalog(string resourcePath)
        {
            TextAsset asset = Resources.Load<TextAsset>(resourcePath);
            Assert.That(asset, Is.Not.Null, $"Missing catalog {resourcePath}.");
            Catalog catalog = JsonUtility.FromJson<Catalog>(asset.text);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < catalog.entries.Length; index++)
            {
                values[catalog.entries[index].key] = catalog.entries[index].value;
            }

            return values;
        }
    }
}
