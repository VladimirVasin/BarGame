using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Pure contract for the cafe pair's turn, two repertoires and additive
    /// head look. Runtime composition is already exercised by the focused
    /// cafe cast fixture; this file deliberately does not grow its 1500-line
    /// PlayMode counterpart.
    /// </summary>
    public sealed class MountainRoadCafeConversationTests
    {
        private const int Seed = 20260902;

        [Test]
        public void Timeline_OpensQuicklyThenAlternatesAtOneCadence()
        {
            var timeline = new MountainRoadCafeConversationTimeline(Seed);
            var speakers = new List<
                MountainRoadCafeConversationSpeaker>();

            timeline.Advance(
                MountainRoadCafeConversationTimeline
                    .FirstLineDelaySeconds - 0.01f);
            Assert.That(timeline.ConsumeLineCue(out _), Is.False);

            timeline.Advance(0.02f);
            Assert.That(
                timeline.ConsumeLineCue(
                    out MountainRoadCafeConversationSpeaker first),
                Is.True);
            speakers.Add(first);

            for (int line = 0; line < 5; line++)
            {
                timeline.Advance(
                    MountainRoadCafeConversationTimeline
                        .LineIntervalSeconds);
                Assert.That(
                    timeline.ConsumeLineCue(
                        out MountainRoadCafeConversationSpeaker speaker),
                    Is.True);
                speakers.Add(speaker);
            }

            Assert.That(speakers[0], Is.EqualTo(timeline.OpeningSpeaker));
            for (int index = 1; index < speakers.Count; index++)
            {
                Assert.That(
                    speakers[index],
                    Is.EqualTo(
                        MountainRoadCafeConversationTimeline.Opposite(
                            speakers[index - 1])));
            }
        }

        [Test]
        public void Timeline_AHitchForfeitsBacklogAndResetRestoresOpener()
        {
            var timeline = new MountainRoadCafeConversationTimeline(Seed);
            timeline.Advance(100f);
            Assert.That(timeline.ConsumeLineCue(out _), Is.True);
            Assert.That(timeline.ConsumeLineCue(out _), Is.False);
            Assert.That(
                timeline.SecondsUntilNextLine,
                Is.EqualTo(
                    MountainRoadCafeConversationTimeline
                        .LineIntervalSeconds).Within(0.0001f));

            timeline.Reset();
            Assert.That(timeline.LineCount, Is.Zero);
            Assert.That(
                timeline.NextSpeaker,
                Is.EqualTo(timeline.OpeningSpeaker));
        }

        [Test]
        public void Timeline_DueCueSurvivesLongBlockedWindowInAuthoredOrder()
        {
            var timeline = new MountainRoadCafeConversationTimeline(Seed);
            var order = new MountainRoadCafeConversationOrder();

            // The due moment lands inside Drink. It is intentionally left
            // latched in the clock while the prohibited window continues.
            timeline.Advance(
                MountainRoadCafeConversationTimeline
                    .FirstLineDelaySeconds);
            string blockedKey = order.PeekKey(
                MountainRoadCafeConversationSpeaker.PairMan);
            MountainRoadCafeConversationSpeaker nextAfterBlocked =
                timeline.NextSpeaker;
            timeline.Advance(100f);
            timeline.Advance(100f);

            Assert.That(timeline.LineCount, Is.EqualTo(1));
            Assert.That(
                timeline.NextSpeaker,
                Is.EqualTo(nextAfterBlocked));
            Assert.That(
                order.PeekKey(
                    MountainRoadCafeConversationSpeaker.PairMan),
                Is.EqualTo(blockedKey));
            Assert.That(
                timeline.ConsumeLineCue(
                    out MountainRoadCafeConversationSpeaker unblocked),
                Is.True);
            Assert.That(
                unblocked,
                Is.EqualTo(MountainRoadCafeConversationSpeaker.PairMan));
            Assert.That(
                order.ConsumeKey(unblocked),
                Is.EqualTo("mountain.cafe.pair.man.line.01"));

            timeline.Advance(
                MountainRoadCafeConversationTimeline
                    .LineIntervalSeconds);
            Assert.That(
                timeline.ConsumeLineCue(
                    out MountainRoadCafeConversationSpeaker following),
                Is.True);
            Assert.That(
                following,
                Is.EqualTo(MountainRoadCafeConversationSpeaker.PairWoman));
            Assert.That(
                order.ConsumeKey(following),
                Is.EqualTo("mountain.cafe.pair.woman.line.01"));
        }

        [Test]
        public void Timeline_RejectsInvalidTime()
        {
            var timeline = new MountainRoadCafeConversationTimeline(Seed);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Advance(-0.01f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Advance(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => timeline.Advance(float.PositiveInfinity));
        }

        [Test]
        public void Lines_OwnTenSeparateStableKeysPerSpeaker()
        {
            string[] man =
                MountainRoadCafeConversationLines.PairManLineKeys;
            string[] woman =
                MountainRoadCafeConversationLines.PairWomanLineKeys;

            Assert.That(
                man.Length,
                Is.EqualTo(
                    MountainRoadCafeConversationLines.LinesPerSpeaker));
            Assert.That(
                woman.Length,
                Is.EqualTo(
                    MountainRoadCafeConversationLines.LinesPerSpeaker));
            Assert.That(man.Distinct().Count(), Is.EqualTo(man.Length));
            Assert.That(woman.Distinct().Count(), Is.EqualTo(woman.Length));
            Assert.That(man.Intersect(woman), Is.Empty);
            Assert.That(
                man.All(key => key.StartsWith(
                    "mountain.cafe.pair.man.line.",
                    StringComparison.Ordinal)),
                Is.True);
            Assert.That(
                woman.All(key => key.StartsWith(
                    "mountain.cafe.pair.woman.line.",
                    StringComparison.Ordinal)),
                Is.True);
        }

        [Test]
        public void LoneLines_OwnFourStableKeysOutsideThePairPool()
        {
            string[] lone =
                MountainRoadCafeConversationLines.LonePatronLineKeys;
            string[] pair = MountainRoadCafeConversationLines
                .PairManLineKeys
                .Concat(MountainRoadCafeConversationLines.PairWomanLineKeys)
                .ToArray();

            Assert.That(
                lone.Length,
                Is.EqualTo(
                    MountainRoadCafeConversationLines.LonePatronLineCount));
            CollectionAssert.AreEqual(
                new[]
                {
                    "mountain.cafe.lone.line.01",
                    "mountain.cafe.lone.line.02",
                    "mountain.cafe.lone.line.03",
                    "mountain.cafe.lone.line.04"
                },
                lone);
            Assert.That(lone.Distinct().Count(), Is.EqualTo(lone.Length));
            Assert.That(lone.Intersect(pair), Is.Empty);
            Assert.That(
                lone.All(key => key.StartsWith(
                    "mountain.cafe.lone.line.",
                    StringComparison.Ordinal)),
                Is.True);
        }

        [Test]
        public void Lines_ResolveInBothCatalogsAndKeepTextRegister()
        {
            string[] resourcePaths =
            {
                "Localization/ru",
                "Localization/en"
            };
            string[] keys = MountainRoadCafeConversationLines
                .PairManLineKeys
                .Concat(MountainRoadCafeConversationLines.PairWomanLineKeys)
                .Concat(MountainRoadCafeConversationLines.LonePatronLineKeys)
                .ToArray();

            Assert.That(
                keys.Length,
                Is.EqualTo(
                    MountainRoadCafeConversationLines.LinesPerSpeaker * 2 +
                    MountainRoadCafeConversationLines.LonePatronLineCount));

            for (int catalogIndex = 0;
                 catalogIndex < resourcePaths.Length;
                 catalogIndex++)
            {
                string resourcePath = resourcePaths[catalogIndex];
                Dictionary<string, string> values =
                    LoadCatalog(resourcePath);

                for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                {
                    string key = keys[keyIndex];
                    Assert.That(
                        values.ContainsKey(key),
                        Is.True,
                        $"{resourcePath} is missing '{key}'.");

                    string line = values[key];
                    Assert.That(
                        line,
                        Is.Not.Null.And.Not.Empty,
                        $"{resourcePath} leaves '{key}' blank.");
                    Assert.That(
                        line.Length,
                        Is.LessThanOrEqualTo(
                            MountainRoadCafeConversationLines
                                .MaximumLineLength),
                        $"{resourcePath} '{key}' is too long: " +
                        $"{line.Length} characters.");
                    Assert.That(
                        line.EndsWith(".", StringComparison.Ordinal),
                        Is.True,
                        $"{resourcePath} '{key}' must end as a statement.");
                    Assert.That(
                        line.Contains("!"),
                        Is.False,
                        $"{resourcePath} '{key}' raises its voice.");
                    Assert.That(
                        line.Contains("?"),
                        Is.False,
                        $"{resourcePath} '{key}' cannot become a question.");
                    Assert.That(
                        line.Count(character => character == '.'),
                        Is.InRange(1, 2),
                        $"{resourcePath} '{key}' must stay within one or " +
                        "two short sentences.");
                }
            }
        }

        [Test]
        public void ConversationSpeaker_RemainsTheMutualPairOnly()
        {
            var speakers =
                (MountainRoadCafeConversationSpeaker[])Enum.GetValues(
                    typeof(MountainRoadCafeConversationSpeaker));

            CollectionAssert.AreEquivalent(
                new[]
                {
                    MountainRoadCafeConversationSpeaker.PairMan,
                    MountainRoadCafeConversationSpeaker.PairWoman
                },
                speakers);
            Assert.That(
                Enum.IsDefined(
                    typeof(MountainRoadCafeConversationSpeaker),
                    "LonePatron"),
                Is.False,
                "The pair never treats the lone patron as a reply target.");
        }

        [Test]
        public void LoneSchedule_FiresEveryThirdCompletedExchangeAcrossLoop()
        {
            var schedule =
                new MountainRoadCafeLonePatronInterjectionSchedule();
            var firedExchanges = new List<int>();

            Assert.That(
                schedule.RecordCompletedLine(
                    MountainRoadCafeConversationSpeaker.PairWoman),
                Is.False,
                "A woman line without its preceding completed man line is " +
                "not a completed exchange.");
            Assert.That(schedule.CompletedPairExchanges, Is.Zero);

            for (int exchange = 1; exchange <= 12; exchange++)
            {
                Assert.That(
                    schedule.RecordCompletedLine(
                        MountainRoadCafeConversationSpeaker.PairMan),
                    Is.False);
                if (schedule.RecordCompletedLine(
                        MountainRoadCafeConversationSpeaker.PairWoman))
                {
                    firedExchanges.Add(exchange);
                }

                Assert.That(
                    schedule.CompletedPairExchanges,
                    Is.EqualTo(exchange));
            }

            CollectionAssert.AreEqual(
                new[] { 3, 6, 9, 12 },
                firedExchanges,
                "Cadence continues through the ten-pair authored wrap.");
            Assert.That(schedule.NextLineIndex, Is.Zero);

            string[] lone =
                MountainRoadCafeConversationLines.LonePatronLineKeys;
            string[] consumed = new string[lone.Length + 1];
            for (int index = 0; index < consumed.Length; index++)
            {
                consumed[index] = schedule.ConsumeLonePatronLineKey();
            }

            CollectionAssert.AreEqual(
                lone.Concat(new[] { lone[0] }).ToArray(),
                consumed,
                "The four lone lines keep authored order and wrap once.");
            Assert.That(schedule.NextLineIndex, Is.EqualTo(1));

            schedule.RecordCompletedLine(
                MountainRoadCafeConversationSpeaker.PairMan);
            schedule.Reset();
            Assert.That(schedule.CompletedPairExchanges, Is.Zero);
            Assert.That(schedule.NextLineIndex, Is.Zero);
            Assert.That(
                schedule.RecordCompletedLine(
                    MountainRoadCafeConversationSpeaker.PairWoman),
                Is.False,
                "Leaving or disabling the cafe clears an unfinished " +
                "half-exchange.");
            Assert.That(schedule.CompletedPairExchanges, Is.Zero);
        }

        [Test]
        [Category("MountainRoad")]
        public void ThirdExchange_InterjectsThenResumesWithMan04WithoutPairReaction()
        {
            var root = new GameObject("Cafe Interjection Contract Test");
            var player = new GameObject("Cafe Interjection Test Player");
            var cameraObject = new GameObject("Cafe Interjection Test Camera");
            player.transform.SetParent(root.transform, false);
            cameraObject.transform.SetParent(root.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            try
            {
                MountainRoadCafePlan cafePlan = MountainRoadPlanner.Create(
                    Seed).Terminal.Cafe;
                MountainRoadCafeWorldResult cafe =
                    MountainRoadCafeWorldBuilder.Build(
                        root.transform,
                        cafePlan);
                player.transform.position = cafePlan.Center;
                MountainRoadCafeConversationController controller =
                    MountainRoadCafeConversationController.Create(
                        root.transform,
                        cafePlan,
                        cafe.Cast,
                        player.transform,
                        camera,
                        Seed);

                Assert.That(controller, Is.Not.Null);
                Transform loneRoot = cafe.Cast.GetPresentationRoot(
                    MountainRoadCafeCastRole.LonePatron);
                MountainRoadCafeCastPresentation lonePresentation = loneRoot
                    .GetComponent<MountainRoadCafeCastPresentation>();
                Transform manRoot = cafe.Cast.GetPresentationRoot(
                    MountainRoadCafeCastRole.PairMan);
                Transform womanRoot = cafe.Cast.GetPresentationRoot(
                    MountainRoadCafeCastRole.PairWoman);
                MountainRoadCafeCastPresentation manPresentation = manRoot
                    .GetComponent<MountainRoadCafeCastPresentation>();
                MountainRoadCafeCastPresentation womanPresentation = womanRoot
                    .GetComponent<MountainRoadCafeCastPresentation>();
                Transform manHead = manPresentation.Registry
                    .FindModelTransform("head");
                Transform womanHead = womanPresentation.Registry
                    .FindModelTransform("head");

                Assert.That(
                    loneRoot.GetComponent<MountainRoadCafeConversationLook>(),
                    Is.Null,
                    "The husband never receives the pair's look/reply state.");
                Assert.That(
                    controller.ManLook.TargetHead,
                    Is.SameAs(womanHead));
                Assert.That(
                    controller.WomanLook.TargetHead,
                    Is.SameAs(manHead));
                Assert.That(controller.ManLook.IsSpeaking, Is.False);
                Assert.That(controller.WomanLook.IsSpeaking, Is.False);

                Assert.That(
                    cafe.Cast.BindActivationObserver(
                        player.transform,
                        cafePlan.Center),
                    Is.True);
                const float stepSeconds = 0.1f;
                for (int step = 0;
                     step < 3600 &&
                     !controller.IsLonePatronInterjecting;
                     step++)
                {
                    cafe.Cast.Advance(stepSeconds);
                    controller.Advance(stepSeconds);
                }

                Assert.That(
                    controller.IsLonePatronInterjecting,
                    Is.True,
                    "The actual controller must reach the husband's beat " +
                    "after three exchanges, not merely arm a pure clock. " +
                    $"Last={controller.LastLineKey}, " +
                    $"exchanges={controller.CompletedPairExchanges}, " +
                    $"pending={controller.HasPendingLine}, " +
                    $"active={controller.HasActiveLine}, " +
                    $"timeline={controller.Timeline.LineCount}, " +
                    $"reservation={cafe.Cast.IsPairConversationReserved}, " +
                    $"manClip={manPresentation.CurrentClipKind}, " +
                    $"womanClip={womanPresentation.CurrentClipKind}.");
                Assert.That(
                    controller.CompletedPairExchanges,
                    Is.EqualTo(3));
                Assert.That(
                    controller.LastLineKey,
                    Is.EqualTo("mountain.cafe.pair.woman.line.03"));
                Assert.That(controller.ManLook.IsSpeaking, Is.False);
                Assert.That(controller.WomanLook.IsSpeaking, Is.False);

                cafe.Cast.Advance(60f);
                controller.Advance(60f);
                Assert.That(
                    cafe.Cast.LonePatronInterjectionElapsedSeconds,
                    Is.EqualTo(
                        MountainRoadCafeConversationController
                            .MaximumLonePatronVisualStepSeconds)
                        .Within(0.0001f),
                    "A hitch cannot jump over the delayed speech cue and " +
                    "complete the whole visible beat offscreen.");
                Assert.That(
                    controller.LastLonePatronLineKey,
                    Is.Empty);

                for (int step = 0;
                     step < 120 &&
                     string.IsNullOrEmpty(
                         controller.LastLonePatronLineKey);
                     step++)
                {
                    cafe.Cast.Advance(stepSeconds);
                    controller.Advance(stepSeconds);
                }

                Assert.That(
                    controller.LastLonePatronLineKey,
                    Is.EqualTo("mountain.cafe.lone.line.01"));
                Assert.That(
                    controller.Bubbles.IsShowing(lonePresentation),
                    Is.True,
                    "The husband's localized line uses his own visible " +
                    "over-head bubble.");

                for (int step = 0;
                     step < 120 &&
                     controller.IsLonePatronInterjecting;
                     step++)
                {
                    cafe.Cast.Advance(stepSeconds);
                    controller.Advance(stepSeconds);
                }

                Assert.That(controller.IsLonePatronInterjecting, Is.False);
                for (int step = 0;
                     step < 1200 &&
                     !string.Equals(
                         controller.LastLineKey,
                         "mountain.cafe.pair.man.line.04",
                         StringComparison.Ordinal);
                     step++)
                {
                    cafe.Cast.Advance(stepSeconds);
                    controller.Advance(stepSeconds);
                }

                Assert.That(
                    controller.LastLineKey,
                    Is.EqualTo("mountain.cafe.pair.man.line.04"),
                    "The ignored interruption cannot consume or rewind the " +
                    "pair's next authored message.");

                // The husband's own pool does not mutate either pair cursor
                // and there is still no look target other than the partner.
                Assert.That(
                    controller.ManLook.TargetHead,
                    Is.SameAs(womanHead));
                Assert.That(
                    controller.WomanLook.TargetHead,
                    Is.SameAs(manHead));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Lines_BlockedCueKeepsExactAuthoredPairAndLoopsAfterTen()
        {
            var order = new MountainRoadCafeConversationOrder();
            Assert.That(
                order.PeekKey(
                    MountainRoadCafeConversationSpeaker.PairMan),
                Is.EqualTo("mountain.cafe.pair.man.line.01"));
            Assert.That(
                order.PeekKey(
                    MountainRoadCafeConversationSpeaker.PairMan),
                Is.EqualTo("mountain.cafe.pair.man.line.01"),
                "Waiting through Drink or smoke must not consume a cue.");

            for (int pair = 0;
                 pair < MountainRoadCafeConversationLines.LinesPerSpeaker;
                 pair++)
            {
                string suffix = (pair + 1).ToString("00");
                Assert.That(
                    order.ConsumeKey(
                        MountainRoadCafeConversationSpeaker.PairMan),
                    Is.EqualTo(
                        "mountain.cafe.pair.man.line." + suffix));
                Assert.That(
                    order.ConsumeKey(
                        MountainRoadCafeConversationSpeaker.PairWoman),
                    Is.EqualTo(
                        "mountain.cafe.pair.woman.line." + suffix));
            }

            Assert.That(
                order.PeekKey(
                    MountainRoadCafeConversationSpeaker.PairMan),
                Is.EqualTo("mountain.cafe.pair.man.line.01"));
            Assert.That(
                order.PeekKey(
                    MountainRoadCafeConversationSpeaker.PairWoman),
                Is.EqualTo("mountain.cafe.pair.woman.line.01"));
        }

        [Test]
        public void LookWeight_EntersAndReturnsAtSeparateSmoothDurations()
        {
            float entered = MountainRoadCafeConversationLook.ResolveWeight(
                0f,
                true,
                MountainRoadCafeConversationLook.TurnInSeconds * 0.5f);
            Assert.That(entered, Is.EqualTo(0.5f).Within(0.0001f));
            entered = MountainRoadCafeConversationLook.ResolveWeight(
                entered,
                true,
                MountainRoadCafeConversationLook.TurnInSeconds * 0.5f);
            Assert.That(entered, Is.EqualTo(1f).Within(0.0001f));

            float returned = MountainRoadCafeConversationLook.ResolveWeight(
                entered,
                false,
                MountainRoadCafeConversationLook.TurnOutSeconds * 0.5f);
            Assert.That(returned, Is.EqualTo(0.5f).Within(0.0001f));
            returned = MountainRoadCafeConversationLook.ResolveWeight(
                returned,
                false,
                MountainRoadCafeConversationLook.TurnOutSeconds * 0.5f);
            Assert.That(returned, Is.Zero.Within(0.0001f));
        }

        [Test]
        public void LookYaw_TurnsTowardEitherNeighbourButStopsAtOldNeckLimit()
        {
            float right = MountainRoadCafeConversationLook.ResolveYawDegrees(
                Vector3.forward,
                Vector3.right);
            float left = MountainRoadCafeConversationLook.ResolveYawDegrees(
                Vector3.forward,
                Vector3.left);

            Assert.That(
                right,
                Is.EqualTo(
                    MountainRoadCafeConversationLook.MaximumYawDegrees)
                    .Within(0.0001f));
            Assert.That(
                left,
                Is.EqualTo(
                    -MountainRoadCafeConversationLook.MaximumYawDegrees)
                    .Within(0.0001f));
            Assert.That(
                MountainRoadCafeConversationLook.ResolveYawDegrees(
                    Vector3.zero,
                    Vector3.right),
                Is.Zero);
        }

        [Test]
        public void WomanLineWindow_FitsBetweenPlumeAndNextCigaretteLift()
        {
            const float idleLengthSeconds = 11f;
            Assert.That(
                MountainRoadCafeConversationController.CanBeginWomanLine(
                    0.68f,
                    idleLengthSeconds),
                Is.True,
                "The settled post-exhale window accepts a whole line.");
            Assert.That(
                MountainRoadCafeConversationController.CanBeginWomanLine(
                    0.72f,
                    idleLengthSeconds),
                Is.True,
                "The last safe start still leaves the authored margin.");
            Assert.That(
                MountainRoadCafeConversationController.CanBeginWomanLine(
                    0.73f,
                    idleLengthSeconds),
                Is.False,
                "A later start would let the return reach the next lift.");
            Assert.That(
                MountainRoadCafeConversationController.CanBeginWomanLine(
                    0.40f,
                    idleLengthSeconds),
                Is.False,
                "She never talks through the drag or plume.");
        }

        [Test]
        public void ClipGate_BlocksEitherDrinkButAllowsMansIdleTapping()
        {
            Assert.That(
                MountainRoadCafeConversationController
                    .ArePairClipsAvailable(
                        MountainRoadCafeCastClipKind.Idle,
                        MountainRoadCafeCastClipKind.Idle),
                Is.True,
                "CafeManIdle contains tapping; it remains a legal overlap.");
            Assert.That(
                MountainRoadCafeConversationController
                    .ArePairClipsAvailable(
                        MountainRoadCafeCastClipKind.Drink,
                        MountainRoadCafeCastClipKind.Idle),
                Is.False);
            Assert.That(
                MountainRoadCafeConversationController
                    .ArePairClipsAvailable(
                        MountainRoadCafeCastClipKind.Idle,
                        MountainRoadCafeCastClipKind.Drink),
                Is.False);
        }

        [Test]
        public void CastReservation_DefersNextDrinkThroughCompleteSpeechBeat()
        {
            var parent = new GameObject("Cafe Conversation Gate Test");
            try
            {
                MountainRoadCafeWorldResult cafe =
                    MountainRoadCafeWorldBuilder.Build(
                        parent.transform,
                        MountainRoadPlanner.Create(Seed).Terminal.Cafe);
                MountainRoadCafeCastController cast = cafe.Cast;

                Assert.That(cast.TryRequestHeroNotice(), Is.True,
                    "The existing attendant action arms the service clock.");
                Assert.That(cast.TryReservePairConversation(), Is.True);
                Assert.That(cast.IsPairConversationReserved, Is.True);

                cast.Advance(
                    MountainRoadCafeServiceTimeline.NoticeSeconds + 100f);
                Assert.That(
                    cast.ServiceFrame.Phase,
                    Is.EqualTo(MountainRoadCafeServicePhase.Wiping),
                    "An attendant beat may finish, then the gate stops at " +
                    "Wiping before CoupleDrink.");
                float heldWipeElapsed =
                    cast.ServiceFrame.PhaseElapsedSeconds;
                cast.Advance(100f);
                Assert.That(
                    cast.ServiceFrame.Phase,
                    Is.EqualTo(MountainRoadCafeServicePhase.Wiping));
                Assert.That(
                    cast.ServiceFrame.PhaseElapsedSeconds,
                    Is.EqualTo(heldWipeElapsed).Within(0.0001f));
                Assert.That(
                    cast.TryRequestEpisode(
                        MountainRoadCafeCastEpisode.Couple),
                    Is.False,
                    "Manual Drink requests obey the same reservation.");

                Assert.That(cast.ReleasePairConversation(), Is.True);
                cast.Advance(cast.ServiceFrame.PhaseDurationSeconds + 0.1f);
                Assert.That(
                    cast.ServiceFrame.Phase,
                    Is.EqualTo(
                        MountainRoadCafeServicePhase.CoupleDrink));
                Assert.That(
                    cast.ServiceFrame.IsDrinking(
                        MountainRoadCafeCastRole.PairMan),
                    Is.True,
                    "Drink resumes only after the complete return releases " +
                    "the gate.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        private static Dictionary<string, string> LoadCatalog(
            string resourcePath)
        {
            TextAsset asset = Resources.Load<TextAsset>(resourcePath);
            Assert.That(
                asset,
                Is.Not.Null,
                $"Expected a TextAsset at Resources/{resourcePath}.json.");

            Catalog catalog = JsonUtility.FromJson<Catalog>(asset.text);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.entries, Is.Not.Null);

            var values =
                new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < catalog.entries.Length; index++)
            {
                CatalogEntry entry = catalog.entries[index];
                Assert.That(
                    entry,
                    Is.Not.Null,
                    $"{resourcePath} contains a null entry.");
                Assert.That(
                    values.ContainsKey(entry.key),
                    Is.False,
                    $"{resourcePath} contains duplicate key " +
                    $"'{entry.key}'.");
                values.Add(entry.key, entry.value);
            }

            return values;
        }

        [Serializable]
        private sealed class Catalog
        {
            public CatalogEntry[] entries = Array.Empty<CatalogEntry>();
        }

        [Serializable]
        private sealed class CatalogEntry
        {
            public string key = string.Empty;
            public string value = string.Empty;
        }
    }
}
