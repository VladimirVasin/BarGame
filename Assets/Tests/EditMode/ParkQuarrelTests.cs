using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The argument at the park chess set: the turn, the repertoire, the
    /// blend that carries the shout over the brooding loop, and the
    /// placement and typing of the bubble it is read in. All of it is
    /// pure — the two men themselves are proved by their own fixtures,
    /// and the one thing that cannot be tested here is what the panel
    /// looks like, because batch mode has no game view for IMGUI.
    /// </summary>
    public sealed class ParkQuarrelTests
    {
        private const int Seed = 20260818;

        // -- The turn ---------------------------------------------------

        [Test]
        public void Timeline_OpensAfterTheShortFirstDelay()
        {
            var timeline = new ParkQuarrelTimeline(Seed);

            timeline.Advance(
                ParkQuarrelTimeline.FirstTauntDelaySeconds - 0.05f);
            Assert.That(
                timeline.ConsumeTauntCue(out _),
                Is.False,
                "Nobody shouts before the first delay is up.");

            timeline.Advance(0.1f);
            Assert.That(
                timeline.ConsumeTauntCue(
                    out ParkQuarrelSpeaker first),
                Is.True);
            Assert.That(first, Is.EqualTo(timeline.OpeningSpeaker));
        }

        [Test]
        public void Timeline_AlternatesEveryTenSeconds()
        {
            var timeline = new ParkQuarrelTimeline(Seed);
            var order = new List<ParkQuarrelSpeaker>();

            // Ordinary frames, deliberately not a divisor of the
            // interval: the carried remainder is what keeps the cadence
            // from drifting a frame later every turn.
            for (int step = 0; step < 2400; step++)
            {
                timeline.Advance(1f / 60f);
                if (timeline.ConsumeTauntCue(
                        out ParkQuarrelSpeaker speaker))
                {
                    order.Add(speaker);
                }
            }

            Assert.That(order.Count, Is.EqualTo(4),
                "1.2 s then one every 10 s over 40 s is four shouts.");
            for (int index = 1; index < order.Count; index++)
            {
                Assert.That(
                    order[index],
                    Is.Not.EqualTo(order[index - 1]),
                    "Neither man ever gets two in a row.");
            }
        }

        [Test]
        public void Timeline_AHitchForfeitsTheBacklogInsteadOfFiringIt()
        {
            var timeline = new ParkQuarrelTimeline(Seed);
            timeline.Advance(
                ParkQuarrelTimeline.FirstTauntDelaySeconds);
            Assert.That(timeline.ConsumeTauntCue(out _), Is.True);

            // Four turns' worth of stall in one step. Two men screaming
            // over each other on the frame the game unfreezes is not the
            // scene, so exactly one shout comes out of it.
            timeline.Advance(41f);
            Assert.That(timeline.ConsumeTauntCue(out _), Is.True);
            Assert.That(timeline.ConsumeTauntCue(out _), Is.False);
            Assert.That(
                timeline.SecondsUntilNextTaunt,
                Is.EqualTo(ParkQuarrelTimeline.TauntIntervalSeconds)
                    .Within(0.0001f));
        }

        [Test]
        public void Timeline_ResetPutsTheSeededOpenerBackOnTheTurn()
        {
            var timeline = new ParkQuarrelTimeline(Seed);
            timeline.Advance(9f);
            timeline.ConsumeTauntCue(out _);

            timeline.Reset();

            Assert.That(timeline.TauntCount, Is.Zero);
            Assert.That(
                timeline.NextSpeaker,
                Is.EqualTo(timeline.OpeningSpeaker));
            Assert.That(
                timeline.SecondsUntilNextTaunt,
                Is.EqualTo(ParkQuarrelTimeline.FirstTauntDelaySeconds)
                    .Within(0.0001f));
        }

        [Test]
        public void Timeline_TheOpenerIsSeededRatherThanFixed()
        {
            var openers = new HashSet<ParkQuarrelSpeaker>();
            for (int seed = 0; seed < 64; seed++)
            {
                openers.Add(
                    ParkQuarrelTimeline.ResolveOpeningSpeaker(seed));
            }

            Assert.That(openers.Count, Is.EqualTo(2),
                "The same man must not always start.");
        }

        // -- The repertoire ---------------------------------------------

        [Test]
        public void Taunts_TheTwoPoolsAreSeparateAndFullyLocalized()
        {
            string[] chess = ParkQuarrelTaunts.ChessLineKeys;
            string[] checkers = ParkQuarrelTaunts.CheckersLineKeys;

            Assert.That(chess, Is.Not.Empty);
            Assert.That(checkers, Is.Not.Empty);
            Assert.That(
                chess.Intersect(checkers),
                Is.Empty,
                "Neither man borrows the other's material.");

            // Both catalogs, not merely the active one: a line added to
            // Russian and forgotten in English is the failure that would
            // otherwise ship, and it would ship as a bubble with a dotted
            // key in it rather than as an exception.
            string russian = LoadCatalog("ru");
            string english = LoadCatalog("en");

            foreach (string key in chess.Concat(checkers))
            {
                string line = LocalizationService.Get(key);
                Assert.That(line, Is.Not.EqualTo(key),
                    $"'{key}' has no localized line.");
                Assert.That(
                    line.Length,
                    Is.LessThanOrEqualTo(
                        ParkQuarrelTaunts.MaximumLineLength),
                    $"'{key}' is too long for a two-row bubble.");
                Assert.That(russian.Contains($"\"{key}\""), Is.True,
                    $"'{key}' is missing from the Russian catalog.");
                Assert.That(english.Contains($"\"{key}\""), Is.True,
                    $"'{key}' is missing from the English catalog.");
            }
        }

        private static string LoadCatalog(string language)
        {
            var asset = Resources.Load<TextAsset>(
                $"Localization/{language}");
            Assert.That(asset, Is.Not.Null,
                $"The '{language}' catalog is missing.");
            return asset.text;
        }

        [Test]
        public void Taunts_NeitherManRepeatsHimselfBackToBack()
        {
            foreach (ParkQuarrelSpeaker speaker in new[]
                     {
                         ParkQuarrelSpeaker.Chess,
                         ParkQuarrelSpeaker.Checkers
                     })
            {
                uint state = ParkQuarrelTaunts.CreateState(Seed, speaker);
                var counts = new int[
                    ParkQuarrelTaunts.LineKeysFor(speaker).Length];
                int previous = -1;
                for (int draw = 0; draw < 4000; draw++)
                {
                    int index = ParkQuarrelTaunts.NextIndex(
                        speaker,
                        ref state,
                        previous);
                    Assert.That(index, Is.Not.EqualTo(previous));
                    counts[index]++;
                    previous = index;
                }

                Assert.That(counts.Min(), Is.GreaterThan(0),
                    "Every line has to come up.");
            }
        }

        [Test]
        public void Taunts_OneSeedDoesNotHandBothMenTheSameWalk()
        {
            uint chessState = ParkQuarrelTaunts.CreateState(
                Seed,
                ParkQuarrelSpeaker.Chess);
            uint checkersState = ParkQuarrelTaunts.CreateState(
                Seed,
                ParkQuarrelSpeaker.Checkers);
            Assert.That(chessState, Is.Not.EqualTo(checkersState));

            bool diverged = false;
            int chessPrevious = -1;
            int checkersPrevious = -1;
            for (int draw = 0; draw < 24 && !diverged; draw++)
            {
                chessPrevious = ParkQuarrelTaunts.NextIndex(
                    ParkQuarrelSpeaker.Chess,
                    ref chessState,
                    chessPrevious);
                checkersPrevious = ParkQuarrelTaunts.NextIndex(
                    ParkQuarrelSpeaker.Checkers,
                    ref checkersState,
                    checkersPrevious);
                diverged = chessPrevious != checkersPrevious;
            }

            Assert.That(diverged, Is.True,
                "Two men reciting the same numbered line in lockstep " +
                "would read as one man with an echo.");
        }

        // -- The shout blend --------------------------------------------

        [Test]
        public void TauntWeight_IsZeroAtBothEndsAndOneAcrossTheBeat()
        {
            const float length = 2f;

            Assert.That(
                ParkChessPlayerPresentation.ResolveTauntWeight(0f, length),
                Is.Zero);
            Assert.That(
                ParkChessPlayerPresentation.ResolveTauntWeight(
                    length +
                    ParkChessPlayerPresentation.TauntBlendOutSeconds,
                    length),
                Is.Zero);
            Assert.That(
                ParkChessPlayerPresentation.ResolveTauntWeight(
                    length * 0.5f,
                    length),
                Is.EqualTo(1f).Within(0.0001f),
                "The authored beat plays at full weight, not blended.");

            // The bubble opens on the throw, so the pose has to be fully
            // in by then rather than still crossing.
            Assert.That(
                ParkChessPlayerPresentation.ResolveTauntWeight(
                    length * CityParkQuarrelController.ShoutPhase,
                    length),
                Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void TauntWeight_RisesAndFallsWithoutOvershoot()
        {
            const float length = 2f;
            float total = length +
                ParkCheckersPlayerPresentation.TauntBlendOutSeconds;
            for (int step = 0; step <= 200; step++)
            {
                float elapsed = total * step / 200f;
                float weight = ParkCheckersPlayerPresentation
                    .ResolveTauntWeight(elapsed, length);
                Assert.That(weight, Is.InRange(0f, 1f));
            }

            Assert.That(
                ParkCheckersPlayerPresentation.ResolveTauntWeight(
                    -1f,
                    length),
                Is.Zero);
            Assert.That(
                ParkCheckersPlayerPresentation.ResolveTauntWeight(
                    0.5f,
                    0f),
                Is.Zero,
                "A design with no authored beat never blends one in.");
        }

        // -- The bubble --------------------------------------------------

        [Test]
        public void Bubble_StaysInsideTheCanvasWhereverTheSpeakerIs()
        {
            var size = new Vector2(180f, 30f);
            var anchors = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(-400f, -220f),
                new Vector2(
                    RetroUiTheme.LogicalWidth + 400f,
                    RetroUiTheme.LogicalHeight + 400f),
                new Vector2(320f, 180f),
                new Vector2(4f, 3f)
            };

            foreach (Vector2 anchor in anchors)
            {
                Rect panel = NpcSpeechBubbleView.ResolvePanelRect(
                    anchor,
                    size);
                Assert.That(panel.xMin, Is.GreaterThanOrEqualTo(0f));
                Assert.That(panel.yMin, Is.GreaterThanOrEqualTo(0f));
                Assert.That(
                    panel.xMax,
                    Is.LessThanOrEqualTo(RetroUiTheme.LogicalWidth));
                Assert.That(
                    panel.yMax,
                    Is.LessThanOrEqualTo(RetroUiTheme.LogicalHeight));
            }
        }

        [Test]
        public void Bubble_SitsAboveTheHeadWhenThereIsRoomForIt()
        {
            var size = new Vector2(120f, 24f);
            var anchor = new Vector2(320f, 200f);

            Rect panel = NpcSpeechBubbleView.ResolvePanelRect(
                anchor,
                size);

            Assert.That(
                panel.center.x,
                Is.EqualTo(anchor.x).Within(0.51f),
                "Centred over the speaker.");
            Assert.That(
                panel.yMax,
                Is.LessThanOrEqualTo(anchor.y),
                "And clear of his head, tail included.");
        }

        [Test]
        public void Bubble_TypesForwardAndStops()
        {
            const string line = "Конь ходит буквой Г. От слова «горе».";

            Assert.That(
                SpeechDelivery.ResolveRevealedCharacters(line, 0f),
                Is.Zero);
            Assert.That(
                SpeechDelivery.ResolveRevealedCharacters(line, -3f),
                Is.Zero);

            int previous = 0;
            for (int step = 0; step <= 120; step++)
            {
                int revealed =
                    SpeechDelivery.ResolveRevealedCharacters(
                        line,
                        step * 0.05f);
                Assert.That(revealed, Is.GreaterThanOrEqualTo(previous));
                Assert.That(
                    revealed,
                    Is.LessThanOrEqualTo(line.Length));
                previous = revealed;
            }

            Assert.That(previous, Is.EqualTo(line.Length));
            // The whole line has to be typed out well inside its visible
            // interval, or the panel takes itself down while the
            // player is still being handed the words a letter at a time.
            float typedIn = line.Length /
                SpeechDelivery.CharactersPerSecond;
            Assert.That(
                typedIn,
                Is.LessThan(NpcSpeechBubbleView.VisibleSeconds * 0.5f));
        }

        [TestCase(NpcSpeechBubbleView.VisibleSeconds)]
        [TestCase((float)CityPortConversationSchedule.LineSeconds)]
        [TestCase((float)CityPortConversationSchedule.DriverLineSeconds)]
        [TestCase((float)CityPortConversationSchedule.AccessWaitLineSeconds)]
        public void Bubble_TakesItselfDownAfterItsConfiguredLifetime(float duration)
        {
            var host = new GameObject("Bubble Lifetime Test Host");
            try
            {
                var view = host.AddComponent<NpcSpeechBubbleView>();
                Assert.That(view.LineDurationSeconds,
                    Is.EqualTo(NpcSpeechBubbleView.VisibleSeconds));
                view.LineDurationSeconds = duration;
                var speaker = new GameObject("Speaker");
                speaker.transform.SetParent(host.transform, false);
                const string line = "Шашки — это шахматы для уставших.";

                Assert.That(
                    view.DeclareSpeaker(
                        speaker,
                        speaker.transform,
                        NpcVoiceCatalog.ChessPlayerDesignId,
                        NpcEarshotProfile.Shout),
                    Is.True);
                Assert.That(
                    view.ShowAt(
                        speaker,
                        line,
                        100f),
                    Is.True);

                view.LineDurationSeconds = .05f;
                view.AdvanceTo(
                    100f + duration - 0.01f);
                Assert.That(view.IsShowing(speaker), Is.True,
                    "Changing the next line's duration cannot shorten an open line.");
                Assert.That(view.RevealedTextOf(speaker), Is.EqualTo(line),
                    "The whole line is readable before its owner closes it.");

                view.AdvanceTo(
                    100f + duration + 0.01f);
                Assert.That(view.IsShowing(speaker), Is.False,
                    "And gone by itself after that, unanswered.");

                // Which has to happen with plenty of quiet left before
                // the neighbour's turn comes round, or nothing was
                // actually taken down between lines.
                Assert.That(
                    duration,
                    Is.LessThan(
                        ParkQuarrelTimeline.TauntIntervalSeconds));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Bubble_RefusesALineFromAnUndeclaredSpeaker()
        {
            var host = new GameObject("Bubble Declaration Test Host");
            try
            {
                var view = host.AddComponent<NpcSpeechBubbleView>();
                var stranger = new GameObject("Stranger");
                stranger.transform.SetParent(host.transform, false);

                Assert.That(
                    view.ShowAt(stranger, "Никто.", 10f),
                    Is.False,
                    "A line from nobody has no head to hang over and " +
                    "no voice to say it in.");
                Assert.That(view.IsShowing(stranger), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Bubble_OneHeadCannotSpeakThroughTwoOwnersOrViews()
        {
            var host = new GameObject("Shared speech ownership");
            var otherHost = new GameObject("Interaction speech ownership");
            try
            {
                var ambient = host.AddComponent<NpcSpeechBubbleView>();
                var response = otherHost.AddComponent<NpcSpeechBubbleView>();
                var actor = new GameObject("Speaking actor");
                actor.transform.SetParent(host.transform, false);
                var alias = new GameObject("Talk adapter");
                alias.transform.SetParent(host.transform, false);
                var speaker = new NpcSpeaker(actor, actor.transform,
                    NpcVoiceCatalog.WatchmanDesignId, NpcEarshotProfile.Conversation);
                var sameHead = new NpcSpeaker(alias, actor.transform,
                    NpcVoiceCatalog.WatchmanDesignId, NpcEarshotProfile.Conversation);
                ambient.DeclareSpeaker(speaker);
                ambient.DeclareSpeaker(sameHead);
                response.DeclareSpeaker(speaker);
                response.DeclareSpeaker(sameHead);
                Assert.That(ambient.ShowAt(actor, "Старая строка.", 10f, 5f), Is.True);
                ambient.AdvanceTo(10.2f);
                string held = ambient.RevealedTextOf(actor);
                Assert.That(response.ShowAt(actor, "Ответ.", 0f), Is.False,
                    "An interaction cannot steal the ambient speaker across different clocks.");
                Assert.That(response.ShowAt(alias, "Ответ.", 0f), Is.False,
                    "Separate adapters using the same head still belong to one speaker.");
                Assert.That(ambient.ShowAt(alias, "Ответ.", 10.2f), Is.False);
                Assert.That(ambient.RevealedTextOf(actor), Is.EqualTo(held));
                ambient.AdvanceTo(15.01f);
                Assert.That(response.ShowAt(alias, "Ответ.", 0f), Is.True,
                    "Normal completion releases the head for an answer.");
                response.enabled = false;
                Assert.That(ambient.ShowAt(actor, "Снова.", 20f), Is.True,
                    "Disabling the other view releases its ownership.");
                // A presentation can replace its model before the old manual
                // view gets another tick. A dead head must not retain the owner.
                actor.SetActive(false);
                response.enabled = true;
                var replacement = new GameObject("Replacement head");
                replacement.transform.SetParent(host.transform, false);
                response.DeclareSpeaker(new NpcSpeaker(actor, replacement.transform,
                    NpcVoiceCatalog.WatchmanDesignId, NpcEarshotProfile.Conversation));
                Assert.That(response.ShowAt(actor, "Ответ.", 30f), Is.True);
                response.DismissAll();
                Object.DestroyImmediate(actor);
                ambient.AdvanceTo(20.1f);
                Assert.That(ambient.IsShowing(alias), Is.False);
                Assert.That(ambient.RevealedTextOf(actor), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(otherHost);
                Object.DestroyImmediate(host);
            }
        }

        // -- The distance fade -------------------------------------------

        [Test]
        public void Bubble_IsFaintAcrossTheParkAndSolidAtTheTables()
        {
            NpcEarshotProfile shout = NpcEarshotProfile.Shout;

            Assert.That(
                shout.ResolveOpacity(
                    NpcEarshotProfile.ShoutFaintRadiusMeters),
                Is.EqualTo(NpcEarshotProfile.DefaultFaintOpacity)
                    .Within(0.0001f),
                "At the edge of earshot it is barely there.");
            Assert.That(
                shout.ResolveOpacity(
                    NpcEarshotProfile.ShoutCullRadiusMeters),
                Is.EqualTo(NpcEarshotProfile.DefaultFaintOpacity)
                    .Within(0.0001f),
                "And no fainter out to the very last metre.");
            Assert.That(
                shout.ResolveOpacity(
                    CityParkQuarrelController.SolidRadiusMeters),
                Is.EqualTo(1f).Within(0.0001f),
                "At the tables it is solid.");
            Assert.That(
                shout.ResolveOpacity(0f),
                Is.EqualTo(1f).Within(0.0001f),
                "And stays solid all the way in.");

            // The whole band the two of them argue across has to stay
            // comfortably readable. Tying the fade's far edge to the
            // engage gate, as the first build did, put the words at
            // their faintest over the entire approach.
            Assert.That(
                NpcEarshotProfile.ShoutFaintRadiusMeters,
                Is.GreaterThan(
                    CityParkQuarrelController.SilenceRadiusMeters),
                "They stop arguing before their words start fading.");
            Assert.That(
                shout.ResolveOpacity(
                    CityParkQuarrelController.AudibleRadiusMeters),
                Is.GreaterThan(
                    NpcEarshotProfile.DefaultFaintOpacity + 0.05f),
                "A line thrown the moment they start is legible.");
        }

        [Test]
        public void Bubble_FirmsUpWithoutEverGoingBackwards()
        {
            NpcEarshotProfile shout = NpcEarshotProfile.Shout;
            float previous = 0f;
            for (int step = 0; step <= 200; step++)
            {
                float distance =
                    NpcEarshotProfile.ShoutCullRadiusMeters *
                    (1f - step / 200f);
                float opacity = shout.ResolveOpacity(distance);
                Assert.That(
                    opacity,
                    Is.InRange(
                        NpcEarshotProfile.DefaultFaintOpacity,
                        1f));
                Assert.That(
                    opacity,
                    Is.GreaterThanOrEqualTo(previous - 0.0001f),
                    "Walking in never makes a line fainter.");
                previous = opacity;
            }

            Assert.That(previous, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void Bubble_OpacityIsClampedAndSurvivesNonsense()
        {
            NpcEarshotProfile shout = NpcEarshotProfile.Shout;

            Assert.That(
                shout.ResolveOpacity(float.NaN),
                Is.EqualTo(NpcEarshotProfile.DefaultFaintOpacity),
                "A NaN must never leave the park invisible.");
            Assert.That(
                shout.ResolveOpacity(-2f),
                Is.EqualTo(1f),
                "Nonsense on the near side reads as standing on him.");
            Assert.That(
                shout.ResolveOpacity(float.PositiveInfinity),
                Is.Zero,
                "And nonsense on the far side is simply not there.");
            Assert.That(
                shout.ResolveOpacity(
                    NpcEarshotProfile.ShoutCullRadiusMeters + 0.01f),
                Is.Zero,
                "Past the cull radius a line is absent, not faint.");
        }

        /// <summary>
        /// The whole reason the fade moved off the view. Two men at two
        /// distances used to share one opacity, so this could not be
        /// asked at all.
        /// </summary>
        [Test]
        public void Bubble_FadesEachSpeakerOnHisOwnDistance()
        {
            var host = new GameObject("Bubble Per-Speaker Test Host");
            try
            {
                var view = host.AddComponent<NpcSpeechBubbleView>();
                var hero = new GameObject("Hero");
                var near = new GameObject("Near Speaker");
                var far = new GameObject("Far Speaker");
                var gone = new GameObject("Out Of Earshot Speaker");
                hero.transform.SetParent(host.transform, false);
                near.transform.SetParent(host.transform, false);
                far.transform.SetParent(host.transform, false);
                gone.transform.SetParent(host.transform, false);
                hero.transform.position = Vector3.zero;
                near.transform.position = new Vector3(
                    NpcEarshotProfile.ShoutSolidRadiusMeters * 0.5f,
                    0f,
                    0f);
                far.transform.position = new Vector3(
                    NpcEarshotProfile.ShoutFaintRadiusMeters,
                    0f,
                    0f);
                gone.transform.position = new Vector3(
                    NpcEarshotProfile.ShoutCullRadiusMeters + 5f,
                    0f,
                    0f);

                view.Initialize(null, hero.transform);
                view.DeclareSpeaker(
                    near,
                    near.transform,
                    NpcVoiceCatalog.ChessPlayerDesignId,
                    NpcEarshotProfile.Shout);
                view.DeclareSpeaker(
                    far,
                    far.transform,
                    NpcVoiceCatalog.CheckersPlayerDesignId,
                    NpcEarshotProfile.Shout);
                view.DeclareSpeaker(
                    gone,
                    gone.transform,
                    NpcVoiceCatalog.WatchmanDesignId,
                    NpcEarshotProfile.Shout);

                view.ShowAt(near, "Рядом.", 50f);
                view.ShowAt(far, "Далеко.", 50f);
                view.ShowAt(gone, "Не слышно.", 50f);
                view.AdvanceTo(50.1f);

                Assert.That(
                    view.OpacityOf(near),
                    Is.EqualTo(1f).Within(0.0001f),
                    "The near man is solid.");
                Assert.That(
                    view.OpacityOf(far),
                    Is.EqualTo(NpcEarshotProfile.DefaultFaintOpacity)
                        .Within(0.0001f),
                    "And the far one is faint at the same instant.");
                Assert.That(
                    view.OpacityOf(near),
                    Is.GreaterThan(view.OpacityOf(far)),
                    "Two speakers at two distances read differently.");
                Assert.That(
                    view.OpacityOf(gone),
                    Is.Zero,
                    "Past the cull radius there is nothing to read.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        // -- The typewriter and its keystroke ----------------------------

        /// <summary>
        /// The reveal is stepped once a frame and the keystroke rides
        /// that step, so a dropped frame can never turn three letters
        /// into three blips.
        /// </summary>
        [Test]
        public void Delivery_StepsOncePerFrameAndBlipsOnLettersOnly()
        {
            const string line = "Да, да... и ещё раз.";
            SpeechDelivery delivery = SpeechDelivery.Spoken(line, 0f);

            int blips = 0;
            int frames = 0;
            int previousRevealed = 0;
            float lastBlipAt = float.NegativeInfinity;
            for (float now = 0f; now <= 2f; now += 1f / 60f)
            {
                frames++;
                if (delivery.Step(now, out char blip))
                {
                    blips++;
                    Assert.That(
                        SpeechDelivery.IsSpeakableCharacter(blip),
                        Is.True,
                        "A space or a full stop never ticks.");
                    Assert.That(
                        now - lastBlipAt,
                        Is.GreaterThanOrEqualTo(
                            SpeechDelivery.MinimumBlipIntervalSeconds -
                            0.0001f),
                        "Two keystrokes are never inside the throttle.");
                    lastBlipAt = now;
                }

                Assert.That(
                    delivery.RevealedCharacters,
                    Is.GreaterThanOrEqualTo(previousRevealed),
                    "The reveal never runs backwards.");
                previousRevealed = delivery.RevealedCharacters;
            }

            Assert.That(
                delivery.RevealedCharacters,
                Is.EqualTo(line.Length));
            Assert.That(blips, Is.GreaterThan(0));
            Assert.That(
                blips,
                Is.LessThan(frames),
                "One blip per frame at the very most.");
            Assert.That(
                blips,
                Is.LessThan(line.Length),
                "The throttle and the punctuation both thin it out.");
        }

        [Test]
        public void Delivery_AHitchYieldsOneKeystrokeNotABurst()
        {
            SpeechDelivery delivery =
                SpeechDelivery.Spoken("абвгде", 0f);

            // One missed frame reveals three letters at once at any
            // configured cadence. The ear must get one stroke.
            float hitchTime = 3.1f / SpeechDelivery.CharactersPerSecond;
            Assert.That(delivery.Step(hitchTime, out char blip), Is.True);
            Assert.That(delivery.RevealedCharacters, Is.EqualTo(3));
            Assert.That(
                blip,
                Is.EqualTo('в'),
                "Pitched from the newest letter, the one the eye is on.");
            Assert.That(
                delivery.Step(hitchTime, out _),
                Is.False,
                "And the same frame asked twice adds nothing.");
        }

        [Test]
        public void Delivery_NarrationIsWholeAndSilent()
        {
            SpeechDelivery narration =
                SpeechDelivery.Instant("Дверь заперта.");

            Assert.That(narration.IsSilent, Is.True);
            Assert.That(narration.IsComplete, Is.True);
            Assert.That(
                narration.RevealedText,
                Is.EqualTo("Дверь заперта."));
            Assert.That(
                narration.Step(5f, out _),
                Is.False,
                "A description of a door is not somebody talking.");
        }

        [TestCase("ru")]
        [TestCase("en")]
        public void Delivery_SpokenDurationLeavesRoomToReadTheLongest(string language)
        {
            var catalog = JsonUtility.FromJson<SpeechCatalog>(LoadCatalog(language));
            Dictionary<string, string> lines = catalog.entries.ToDictionary(
                entry => entry.key, entry => entry.value);
            string longest = CemeteryWatchmanQuips.LineKeys.Select(key => lines[key])
                .OrderByDescending(line => line.Length).First();

            Assert.That(
                CemeteryWatchmanInteraction.ResolveResponseSeconds(
                    CemeteryWatchmanQuips.LineKeys[0]),
                Is.GreaterThanOrEqualTo(
                    CemeteryWatchmanInteraction
                        .ResponseDurationSeconds),
                "The old floor is still a floor.");

            float typedIn = longest.Length /
                            SpeechDelivery.CharactersPerSecond;
            float longestDuration =
                SpeechDelivery.ResolveSpokenDuration(
                    longest,
                    CemeteryWatchmanInteraction.ReadingTailSeconds);
            Assert.That(
                longestDuration - typedIn,
                Is.GreaterThanOrEqualTo(
                    CemeteryWatchmanInteraction.ReadingTailSeconds -
                    0.0001f),
                "Even his longest line keeps its whole reading tail.");
            Assert.That(new[]
                {
                    CemeteryWatchmanInteraction.ReadingTailSeconds,
                    SeacoastFishermanInteraction.ReadingTailSeconds,
                    LastRouteFerrymanTalkInteraction.ReadingTailSeconds,
                    MothersHouseMotherInteraction.ReadingTailSeconds,
                    InventoryTargetInteractionController.SpokenReadingTailSeconds,
                    LastRouteRideSpeechView.ReadingTailSeconds,
                    MothersHouseMotherSpeechController.ReadingTailSeconds
                }, Is.All.EqualTo(SpeechDelivery.ReadingTailSeconds),
                "Every length-derived spoken response keeps the same reading tail.");

            string[] ambientPrefixes =
            {
                "park.quarrel.", "park.board.", "mountain.cafe.",
                "city.pedestrian.insult.", "hero.mutter.", "village.life."
            };
            foreach (KeyValuePair<string, string> line in lines.Where(entry =>
                         ambientPrefixes.Any(prefix => entry.Key.StartsWith(prefix,
                             System.StringComparison.Ordinal))))
            {
                Assert.That(NpcSpeechBubbleView.VisibleSeconds -
                            line.Value.Length / SpeechDelivery.CharactersPerSecond,
                    Is.GreaterThanOrEqualTo(1f),
                    $"{language} '{line.Key}' must finish before the ambient bubble closes.");
            }
            Assert.That(HeroMutterModel.SpeakingSeconds -
                        HeroMutterSlur.MaximumWrappedSlurredLength / SpeechDelivery.CharactersPerSecond,
                Is.GreaterThanOrEqualTo(1f),
                "Even the longest permitted slur finishes inside its speaking phase.");

            // Port exchanges use a fixed schedule instead of a length-derived
            // prompt lifetime. Check both participants in every localized pair
            // so a slower typewriter cannot lose the end before the reply.
            foreach (CityPortConversationKind kind in System.Enum.GetValues(
                         typeof(CityPortConversationKind)))
            {
                for (int variant = 0; variant < CityPortConversationCatalog.Count(kind); variant++)
                {
                    CityPortConversationExchange exchange = CityPortConversationCatalog.Get(kind, variant);
                    float duration = (float)CityPortConversationSchedule.LineDuration(exchange);
                    foreach (string key in new[] { exchange.FirstKey, exchange.SecondKey })
                    {
                        string line = lines[key];
                        Assert.That(duration - line.Length / SpeechDelivery.CharactersPerSecond,
                            Is.GreaterThanOrEqualTo(1f),
                            $"{language} '{key}' needs a reading pause before its partner answers.");
                    }
                }
            }
        }

        [System.Serializable]
        private sealed class SpeechCatalog
        {
            public SpeechCatalogEntry[] entries = System.Array.Empty<SpeechCatalogEntry>();
        }

        [System.Serializable]
        private sealed class SpeechCatalogEntry
        {
            public string key = string.Empty;
            public string value = string.Empty;
        }
    }
}
