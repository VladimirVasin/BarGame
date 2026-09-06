using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The bout of vomiting as a score: three bursts with a breath
    /// between them, the cues in their order and at their instants, the
    /// relief split 7 / 7 / 6, the head that goes down first and comes up
    /// last, a stream that pumps and never exceeds one — and a clock that
    /// holds on a zero step, replays bitwise, and rests inert.
    /// </summary>
    public sealed class HeroVomitTests
    {
        private const float Step = 1f / 60f;

        private readonly struct ExpectedCue
        {
            public ExpectedCue(HeroVomitCueKind kind, int burstIndex, float atSeconds)
            {
                Kind = kind;
                BurstIndex = burstIndex;
                AtSeconds = atSeconds;
            }

            public HeroVomitCueKind Kind { get; }
            public int BurstIndex { get; }
            public float AtSeconds { get; }
        }

        private static readonly ExpectedCue[] Score =
        {
            new ExpectedCue(HeroVomitCueKind.Retch, 0, 0f),
            new ExpectedCue(HeroVomitCueKind.BurstBegin, 0, 0.4f),
            new ExpectedCue(HeroVomitCueKind.Gush, 0, 0.65f),
            new ExpectedCue(HeroVomitCueKind.Gush, 0, 1.55f),
            new ExpectedCue(HeroVomitCueKind.Gush, 0, 2.45f),
            new ExpectedCue(HeroVomitCueKind.BurstEnd, 0, 3.4f),
            new ExpectedCue(HeroVomitCueKind.Cough, 0, 3.4f),
            new ExpectedCue(HeroVomitCueKind.Relief, 0, 3.4f),
            new ExpectedCue(HeroVomitCueKind.Soil, 0, 3.4f),
            new ExpectedCue(HeroVomitCueKind.Retch, 1, 5.1f),
            new ExpectedCue(HeroVomitCueKind.BurstBegin, 1, 5.4f),
            new ExpectedCue(HeroVomitCueKind.Gush, 1, 5.65f),
            new ExpectedCue(HeroVomitCueKind.BurstEnd, 1, 6.4f),
            new ExpectedCue(HeroVomitCueKind.Cough, 1, 6.4f),
            new ExpectedCue(HeroVomitCueKind.Relief, 1, 6.4f),
            new ExpectedCue(HeroVomitCueKind.Retch, 2, 8.1f),
            new ExpectedCue(HeroVomitCueKind.BurstBegin, 2, 8.4f),
            new ExpectedCue(HeroVomitCueKind.Gush, 2, 8.65f),
            new ExpectedCue(HeroVomitCueKind.BurstEnd, 2, 9.4f),
            new ExpectedCue(HeroVomitCueKind.Cough, 2, 9.4f),
            new ExpectedCue(HeroVomitCueKind.Relief, 2, 9.4f),
            new ExpectedCue(HeroVomitCueKind.Finished, -1, 9.4f)
        };

        private static readonly float[] BurstEdges = { 0.4f, 3.4f, 5.4f, 6.4f, 8.4f, 9.4f };

        // ---- the schedule --------------------------------------------

        [Test]
        public void Schedule_BurstsAreThreeTwoOneTwoOne()
        {
            Assert.That(HeroVomitRules.BurstStart(0), Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(HeroVomitRules.BurstEnd(0), Is.EqualTo(3.4f).Within(0.0001f));
            Assert.That(HeroVomitRules.BurstStart(1), Is.EqualTo(5.4f).Within(0.0001f));
            Assert.That(HeroVomitRules.BurstEnd(1), Is.EqualTo(6.4f).Within(0.0001f));
            Assert.That(HeroVomitRules.BurstStart(2), Is.EqualTo(8.4f).Within(0.0001f));
            Assert.That(HeroVomitRules.BurstEnd(2), Is.EqualTo(9.4f).Within(0.0001f));
            Assert.That(HeroVomitRules.ScheduleEndSeconds, Is.EqualTo(9.4f).Within(0.0001f));
            // 9.4 s of score, then the 1.6 s wipe outlasts the 0.8 s head blend-out.
            Assert.That(HeroVomitRules.WipeSeconds, Is.EqualTo(1.6f).Within(0.0001f));
            Assert.That(HeroVomitRules.TotalSeconds, Is.EqualTo(11.0f).Within(0.0001f));

            var model = new HeroVomitModel();
            Assert.That(model.IsActive, Is.False);
            Assert.That(model.BurstIndex, Is.EqualTo(-1));

            model.Begin();
            Assert.That(model.IsActive, Is.True);
            float time = 0f;
            bool[] seenBurst = new bool[3];
            for (int frame = 0; frame < 60 * 11; frame++)
            {
                if (!NearAnEdge(time))
                {
                    int expected = ExpectedBurst(time);
                    Assert.That(model.BurstIndex, Is.EqualTo(expected), $"burst at {time:F3}");
                    Assert.That(model.IsVomiting, Is.EqualTo(expected >= 0), $"vomiting at {time:F3}");
                    float strength = expected < 0 ? 0f : HeroVomitRules.BurstStrengths[expected];
                    Assert.That(model.BurstStrength, Is.EqualTo(strength), $"strength at {time:F3}");
                    if (expected >= 0)
                    {
                        seenBurst[expected] = true;
                    }
                }

                // The two clocks add the same step, so the instant of
                // the deactivation is exact.
                Assert.That(
                    model.IsActive,
                    Is.EqualTo(time < HeroVomitRules.TotalSeconds),
                    $"active at {time:F3}");
                model.Advance(Step);
                time += Step;
            }

            Assert.That(seenBurst, Is.All.True);
            Assert.That(HeroVomitRules.BurstStrengths, Is.EqualTo(new[] { 1f, 0.55f, 0.55f }));
            Assert.That(HeroVomitRules.BurstSeconds, Is.EqualTo(new[] { 3f, 1f, 1f }));
            Assert.That(model.IsActive, Is.False, "Eleven seconds outlasts the bout.");
            Assert.That(model.Pose.IsNone, Is.True);
        }

        // ---- the cues ------------------------------------------------

        [Test]
        public void Cues_ComeInOrderAtTheirInstants()
        {
            var model = new HeroVomitModel();
            model.Begin();
            var cues = new List<HeroVomitCue>();
            var consumedAt = new List<float>();
            float time = 0f;
            Drain(model, time, cues, consumedAt);
            for (int frame = 0; frame < 60 * 11; frame++)
            {
                model.Advance(Step);
                time += Step;
                Drain(model, time, cues, consumedAt);
            }

            Assert.That(cues.Count, Is.EqualTo(Score.Length));
            for (int index = 0; index < Score.Length; index++)
            {
                ExpectedCue expected = Score[index];
                HeroVomitCue cue = cues[index];
                string where = $"cue {index} ({expected.Kind} of burst {expected.BurstIndex})";
                Assert.That(cue.Kind, Is.EqualTo(expected.Kind), where);
                Assert.That(cue.BurstIndex, Is.EqualTo(expected.BurstIndex), where);
                Assert.That(cue.AtSeconds, Is.EqualTo(expected.AtSeconds).Within(0.0001f), where);
                Assert.That(
                    consumedAt[index],
                    Is.InRange(expected.AtSeconds - 0.0001f, expected.AtSeconds + Step + 0.0001f),
                    $"{where} was consumed a frame late");
                if (cue.Kind == HeroVomitCueKind.BurstBegin || cue.Kind == HeroVomitCueKind.Gush)
                {
                    Assert.That(
                        cue.Strength,
                        Is.EqualTo(HeroVomitRules.BurstStrengths[expected.BurstIndex]),
                        where);
                }

                if (cue.Kind == HeroVomitCueKind.Relief)
                {
                    Assert.That(
                        cue.Points,
                        Is.EqualTo(HeroVomitRules.ReliefForBurst(expected.BurstIndex)),
                        where);
                }
            }

            Assert.That(cues[cues.Count - 1].Kind, Is.EqualTo(HeroVomitCueKind.Finished));

            // A dropped frame delivers everything it crossed, in order.
            var dropped = new HeroVomitModel();
            dropped.Begin();
            dropped.Advance(11f);
            int delivered = 0;
            while (dropped.TryConsumeCue(out HeroVomitCue cue))
            {
                Assert.That(cue.Kind, Is.EqualTo(Score[delivered].Kind), $"dropped-frame cue {delivered}");
                delivered++;
            }

            Assert.That(delivered, Is.EqualTo(Score.Length));
            Assert.That(dropped.IsActive, Is.False);
        }

        // ---- the relief ----------------------------------------------

        [Test]
        public void Relief_IsSevenSevenSixAndSumsToTwenty()
        {
            Assert.That(HeroVomitRules.ReliefForBurst(0), Is.EqualTo(7));
            Assert.That(HeroVomitRules.ReliefForBurst(1), Is.EqualTo(7));
            Assert.That(HeroVomitRules.ReliefForBurst(2), Is.EqualTo(6));
            Assert.That(HeroVomitRules.ReliefForBurst(3), Is.EqualTo(0));
            Assert.That(HeroVomitRules.ReliefForBurst(-1), Is.EqualTo(0));
            Assert.That(
                HeroVomitRules.ReliefForBurst(0) + HeroVomitRules.ReliefForBurst(1) + HeroVomitRules.ReliefForBurst(2),
                Is.EqualTo(HeroVomitRules.ReliefPointsPerBout));
            Assert.That(HeroVomitRules.ReliefPointsPerBout, Is.EqualTo(20));

            var model = new HeroVomitModel();
            model.Begin();
            int fromCues = 0;
            int reliefCues = 0;
            for (int frame = 0; frame < 60 * 11; frame++)
            {
                model.Advance(Step);
                while (model.TryConsumeCue(out HeroVomitCue cue))
                {
                    if (cue.Kind != HeroVomitCueKind.Relief)
                    {
                        continue;
                    }

                    reliefCues++;
                    fromCues += cue.Points;
                    Assert.That(model.ReliefGranted, Is.EqualTo(fromCues), "The count follows the cues.");
                }
            }

            Assert.That(reliefCues, Is.EqualTo(3));
            Assert.That(fromCues, Is.EqualTo(20));
            Assert.That(model.ReliefGranted, Is.EqualTo(20));
        }

        [Test]
        public void Cancel_AtFourSecondsGrantsOnlyTheFirstSeven()
        {
            var model = new HeroVomitModel();
            model.Begin();
            int granted = 0;
            float time = 0f;
            while (time < 4f - Step * 0.5f)
            {
                model.Advance(Step);
                time += Step;
                while (model.TryConsumeCue(out HeroVomitCue cue))
                {
                    if (cue.Kind == HeroVomitCueKind.Relief)
                    {
                        granted += cue.Points;
                    }
                }
            }

            Assert.That(granted, Is.EqualTo(7));
            Assert.That(model.ReliefGranted, Is.EqualTo(7));
            Assert.That(model.IsActive, Is.True);
            Assert.That(model.HeadDownDegrees, Is.GreaterThanOrEqualTo(13f));

            model.Cancel();
            Assert.That(model.IsActive, Is.False);
            Assert.That(model.IsVomiting, Is.False);
            Assert.That(model.Pose.IsNone, Is.True, "The pose is None at once; the presentation blends nothing.");
            Assert.That(model.HeadDownDegrees, Is.EqualTo(0f));
            Assert.That(model.Flow, Is.EqualTo(0f));
            Assert.That(model.TryConsumeCue(out _), Is.False);

            for (int frame = 0; frame < 60 * 10; frame++)
            {
                model.Advance(Step);
                Assert.That(model.TryConsumeCue(out _), Is.False, "A cancelled bout emits no further cues.");
            }

            Assert.That(model.ReliefGranted, Is.EqualTo(7), "What the first burst earned stays granted.");
            Assert.That(model.IsActive, Is.False, "A cancelled bout does not resume.");
            Assert.That(model.Time, Is.EqualTo(4f).Within(Step), "The clock stopped where it was cancelled.");
        }

        // ---- the head ------------------------------------------------

        [Test]
        public void HeadDown_RisesHoldsThroughPausesAndDecays()
        {
            var model = new HeroVomitModel();
            model.Begin();
            Assert.That(model.HeadDownDegrees, Is.LessThanOrEqualTo(0.01f), "Nothing yet at the first instant.");
            Assert.That(model.Spasm, Is.EqualTo(0f));

            float time = 0f;
            float peak = 0f;
            float peakSpasm = 0f;
            float atBlendIn = -1f;
            float atFour = -1f;
            float atSeven = -1f;
            float atTotal = -1f;
            bool spasmOutsideBursts = false;
            for (int frame = 0; frame < 60 * 11; frame++)
            {
                model.Advance(Step);
                time += Step;
                float head = model.HeadDownDegrees;
                peak = Mathf.Max(peak, head);
                peakSpasm = Mathf.Max(peakSpasm, model.Spasm);
                if (model.Spasm > 0f && ExpectedBurst(time) < 0 && !NearAnEdge(time))
                {
                    spasmOutsideBursts = true;
                }

                if (atBlendIn < 0f && time >= HeroVomitRules.HeadDownBlendInSeconds - Step * 0.01f)
                {
                    atBlendIn = head;
                }

                if (atFour < 0f && time >= 4f)
                {
                    atFour = head;
                }

                if (atSeven < 0f && time >= 7f)
                {
                    atSeven = head;
                }

                if (atTotal < 0f && time >= HeroVomitRules.TotalSeconds - Step * 0.01f)
                {
                    atTotal = head;
                }
            }

            Assert.That(atBlendIn, Is.GreaterThanOrEqualTo(HeroVomitRules.HeadDownDegrees - 1f), "Down by the end of the blend-in.");
            Assert.That(atFour, Is.GreaterThanOrEqualTo(HeroVomitRules.HeadDownDegrees - 1f), "Held through the first pause.");
            Assert.That(atSeven, Is.GreaterThanOrEqualTo(HeroVomitRules.HeadDownDegrees - 1f), "Held through the second pause.");
            Assert.That(atTotal, Is.LessThanOrEqualTo(0.5f), "Back up by the end of the bout.");
            Assert.That(
                peak,
                Is.LessThanOrEqualTo(
                    HeroVomitRules.HeadDownDegrees +
                    HeroVomitRules.HeadDownHeaveExtraDegrees +
                    HeroVomitRules.PumpHeadDegrees + 0.001f));
            Assert.That(peak, Is.GreaterThan(HeroVomitRules.HeadDownDegrees + 8f), "The heave adds visibly to the pitch.");
            Assert.That(HeroVomitRules.HeadDownDegrees, Is.EqualTo(24f), "A slightly raised head leaves room for the forward jet.");
            Assert.That(peakSpasm, Is.GreaterThan(0.9f));
            Assert.That(peakSpasm, Is.LessThanOrEqualTo(1f));
            Assert.That(spasmOutsideBursts, Is.False, "The heave belongs to the start of a burst.");
            Assert.That(model.HeadDownDegrees, Is.EqualTo(0f), "Nothing left once the bout is over.");

            Assert.That(HeroVomitRules.HeaveShape(0f), Is.EqualTo(0f));
            Assert.That(HeroVomitRules.HeaveShape(HeroVomitRules.HeadDownHeaveSeconds), Is.EqualTo(0f));
            Assert.That(HeroVomitRules.HeaveShape(0.08f), Is.EqualTo(1f).Within(0.0001f));
        }

        // ---- the body ------------------------------------------------

        [Test]
        public void Pose_DoublesOverBracesOnTheKneesConvulsesWithThePumpAndWipes()
        {
            var model = new HeroVomitModel();
            model.Begin();
            float time = 0f;
            float torsoAtFour = -1f;
            float torsoAtSeven = -1f;
            float braceAtSeven = -1f;
            float crouchAtSeven = -1f;
            float peakTorso = 0f;
            float peakPump = 0f;
            float peakWipe = 0f;
            float wipeAtNine = -1f;
            float wipeAtEnd = -1f;
            bool pumpOutsideBursts = false;
            bool torsoPumpsInFirstBurst = false;
            float previousTorso = 0f;
            int torsoCrestsInFirstBurst = 0;
            for (int frame = 0; frame < 60 * 12; frame++)
            {
                model.Advance(Step);
                time += Step;
                PlayerVomitPose pose = model.Pose;
                peakTorso = Mathf.Max(peakTorso, pose.TorsoPitchDegrees);
                peakPump = Mathf.Max(peakPump, pose.Pump);
                peakWipe = Mathf.Max(peakWipe, pose.WipeWeight);
                if (pose.Pump > 0f && ExpectedBurst(time) < 0 && !NearAnEdge(time))
                {
                    pumpOutsideBursts = true;
                }

                if (time > 1f && time < 3.2f)
                {
                    // Inside the first burst the torso rocks with the pump:
                    // count its crests.
                    if (pose.TorsoPitchDegrees < previousTorso && previousTorso > HeroVomitRules.TorsoPitchDegrees + 1f)
                    {
                        torsoCrestsInFirstBurst++;
                    }

                    torsoPumpsInFirstBurst |= pose.Pump > 0.9f;
                }

                previousTorso = pose.TorsoPitchDegrees;
                if (torsoAtFour < 0f && time >= 4f)
                {
                    torsoAtFour = pose.TorsoPitchDegrees;
                }

                if (torsoAtSeven < 0f && time >= 7f)
                {
                    torsoAtSeven = pose.TorsoPitchDegrees;
                    braceAtSeven = pose.BraceWeight;
                    crouchAtSeven = pose.CrouchMetres;
                }

                if (wipeAtNine < 0f && time >= 9f)
                {
                    wipeAtNine = pose.WipeWeight;
                }

                if (wipeAtEnd < 0f && time >= HeroVomitRules.TotalSeconds - Step * 0.01f)
                {
                    wipeAtEnd = pose.WipeWeight;
                }
            }

            Assert.That(torsoAtFour, Is.EqualTo(HeroVomitRules.TorsoPitchDegrees).Within(0.001f), "Doubled over through the first pause.");
            Assert.That(torsoAtSeven, Is.EqualTo(HeroVomitRules.TorsoPitchDegrees).Within(0.001f), "And the second.");
            Assert.That(braceAtSeven, Is.EqualTo(1f).Within(0.001f), "Hands on the knees between bursts.");
            Assert.That(crouchAtSeven, Is.EqualTo(HeroVomitRules.CrouchMetres).Within(0.0001f), "The knees give for the whole bout.");
            Assert.That(peakTorso, Is.GreaterThan(HeroVomitRules.TorsoPitchDegrees + HeroVomitRules.TorsoHeaveExtraDegrees * 0.8f), "The heave lurches the torso further.");
            Assert.That(peakPump, Is.GreaterThan(0.95f), "The first burst pumps at full strength.");
            Assert.That(torsoPumpsInFirstBurst, Is.True);
            Assert.That(torsoCrestsInFirstBurst, Is.GreaterThanOrEqualTo(5), "The torso rocks with every push of the pump.");
            Assert.That(pumpOutsideBursts, Is.False, "The pump beats only while the stream runs.");
            Assert.That(wipeAtNine, Is.EqualTo(0f), "No wipe while the last burst still runs.");
            Assert.That(peakWipe, Is.EqualTo(1f).Within(0.001f), "The hand reaches the mouth after the last burst.");
            Assert.That(wipeAtEnd, Is.LessThanOrEqualTo(0.02f), "And has fallen by the bout's end.");
            Assert.That(model.Pose.IsNone, Is.True, "Nothing left once the bout is over.");
            Assert.That(HeroVomitRules.PulseHertz, Is.EqualTo(HeroVomitStreamSound.PumpHertz), "The stream sound pumps to the flow's beat.");
        }

        // ---- the stream ----------------------------------------------

        [Test]
        public void Flow_PulsesAndNeverExceedsOne()
        {
            var model = new HeroVomitModel();
            model.Begin();
            Assert.That(model.Flow, Is.EqualTo(0f));

            var times = new List<float>();
            var flows = new List<float>();
            float time = 0f;
            float peak = 0f;
            for (int frame = 0; frame < 60 * 11; frame++)
            {
                model.Advance(Step);
                time += Step;
                float flow = model.Flow;
                times.Add(time);
                flows.Add(flow);
                peak = Mathf.Max(peak, flow);
                Assert.That(flow, Is.LessThanOrEqualTo(1f), $"flow at {time:F3}");
                Assert.That(model.Pose.Flow, Is.EqualTo(flow));
                if (NearAnEdge(time))
                {
                    continue;
                }

                int burst = ExpectedBurst(time);
                if (burst < 0)
                {
                    Assert.That(flow, Is.EqualTo(0f), $"dry at {time:F3}");
                }
                else
                {
                    Assert.That(flow, Is.GreaterThan(0f), $"running at {time:F3}");
                    Assert.That(
                        flow,
                        Is.LessThanOrEqualTo(HeroVomitRules.BurstStrengths[burst] + 0.0001f),
                        $"burst {burst} never exceeds its strength at {time:F3}");
                }
            }

            Assert.That(peak, Is.GreaterThan(0.95f), "The first burst reaches its full strength.");

            // Visibly pumping: every second of the first burst has a
            // crest and a trough. The pump's floor is flat for half a
            // period, so a trough is the first frame of the floor.
            for (int second = 0; second < 3; second++)
            {
                float from = HeroVomitRules.BurstStart(0) + second;
                float to = from + 1f;
                int crests = 0;
                int troughs = 0;
                for (int index = 1; index < flows.Count - 1; index++)
                {
                    if (times[index] < from || times[index] >= to)
                    {
                        continue;
                    }

                    if (flows[index] > flows[index - 1] && flows[index] >= flows[index + 1])
                    {
                        crests++;
                    }

                    if (flows[index] < flows[index - 1] && flows[index] <= flows[index + 1])
                    {
                        troughs++;
                    }
                }

                Assert.That(crests, Is.GreaterThanOrEqualTo(1), $"second {second} of the first burst has no crest");
                Assert.That(troughs, Is.GreaterThanOrEqualTo(1), $"second {second} of the first burst has no trough");
            }
        }

        // ---- the clock -----------------------------------------------

        [Test]
        public void Advance_ZeroStepIsAHold()
        {
            var model = new HeroVomitModel();
            model.Begin();
            Run(model, 1f);
            while (model.TryConsumeCue(out _))
            {
            }

            float time = model.Time;
            PlayerVomitPose before = model.Pose;
            Assert.That(before.Active, Is.True);
            Assert.That(before.Flow, Is.GreaterThan(0f));

            model.Advance(0f);
            model.Advance(-1f);
            model.Advance(float.NaN);
            Assert.That(model.Time, Is.EqualTo(time));
            PlayerVomitPose after = model.Pose;
            Assert.That(after.Active, Is.EqualTo(before.Active));
            Assert.That(after.HeadDownDegrees, Is.EqualTo(before.HeadDownDegrees));
            Assert.That(after.Flow, Is.EqualTo(before.Flow));
            Assert.That(after.Spasm, Is.EqualTo(before.Spasm));
            Assert.That(model.TryConsumeCue(out _), Is.False, "A hold crosses no instant.");
            Assert.That(model.BurstIndex, Is.EqualTo(0));
        }

        [Test]
        public void TwoModels_SameTrace()
        {
            var first = new HeroVomitModel();
            var second = new HeroVomitModel();
            first.Begin();
            second.Begin();
            int frame = 0;
            while (first.IsActive || second.IsActive)
            {
                // A ragged frame rate, the same for both.
                float step = (frame % 7 == 3) ? Step * 2.5f : Step;
                if (frame % 11 == 5)
                {
                    step = 0f;
                }

                first.Advance(step);
                second.Advance(step);
                Assert.That(second.Time, Is.EqualTo(first.Time));
                Assert.That(second.IsActive, Is.EqualTo(first.IsActive));
                Assert.That(second.BurstIndex, Is.EqualTo(first.BurstIndex));
                Assert.That(second.Flow, Is.EqualTo(first.Flow));
                Assert.That(second.HeadDownDegrees, Is.EqualTo(first.HeadDownDegrees));
                Assert.That(second.Spasm, Is.EqualTo(first.Spasm));
                Assert.That(second.TorsoPitchDegrees, Is.EqualTo(first.TorsoPitchDegrees));
                Assert.That(second.WipeWeight, Is.EqualTo(first.WipeWeight));
                Assert.That(second.Pump, Is.EqualTo(first.Pump));
                Assert.That(second.ReliefGranted, Is.EqualTo(first.ReliefGranted));

                while (first.TryConsumeCue(out HeroVomitCue cue))
                {
                    Assert.That(second.TryConsumeCue(out HeroVomitCue twin), Is.True);
                    Assert.That(twin.Kind, Is.EqualTo(cue.Kind));
                    Assert.That(twin.BurstIndex, Is.EqualTo(cue.BurstIndex));
                    Assert.That(twin.AtSeconds, Is.EqualTo(cue.AtSeconds));
                    Assert.That(twin.Points, Is.EqualTo(cue.Points));
                    Assert.That(twin.Strength, Is.EqualTo(cue.Strength));
                }

                Assert.That(second.TryConsumeCue(out _), Is.False);
                frame++;
                Assert.That(frame, Is.LessThan(60 * 30), "The bout ends.");
            }

            Assert.That(first.ReliefGranted, Is.EqualTo(HeroVomitRules.ReliefPointsPerBout));
        }

        [Test]
        public void Reset_IsInert()
        {
            var model = new HeroVomitModel();
            Assert.That(model.Pose.IsNone, Is.True);
            Assert.That(model.IsActive, Is.False);
            Assert.That(model.TryConsumeCue(out _), Is.False);
            model.Advance(1f);
            Assert.That(model.Time, Is.EqualTo(0f), "Nothing moves before Begin.");

            model.Begin();
            Run(model, 3.6f);
            Assert.That(model.ReliefGranted, Is.EqualTo(7));
            model.Reset();
            Assert.That(model.Pose.IsNone, Is.True);
            Assert.That(model.IsActive, Is.False);
            Assert.That(model.IsVomiting, Is.False);
            Assert.That(model.Time, Is.EqualTo(0f));
            Assert.That(model.ReliefGranted, Is.EqualTo(0));
            Assert.That(model.TryConsumeCue(out _), Is.False, "Reset empties the queue.");

            model.Advance(1f);
            Assert.That(model.Time, Is.EqualTo(0f));
            Assert.That(model.Pose.IsNone, Is.True);
            Assert.That(model.TryConsumeCue(out _), Is.False);

            model.Begin();
            Assert.That(model.IsActive, Is.True, "A reset model can begin again.");
            Assert.That(model.TryConsumeCue(out HeroVomitCue cue), Is.True);
            Assert.That(cue.Kind, Is.EqualTo(HeroVomitCueKind.Retch));
            Assert.That(cue.AtSeconds, Is.EqualTo(0f));

            Assert.That(PlayerVomitPose.None.IsNone, Is.True);
            Assert.That(new PlayerVomitPose(false, -3f, -1f, 2f).HeadDownDegrees, Is.EqualTo(0f));
            Assert.That(new PlayerVomitPose(false, -3f, -1f, 2f).Flow, Is.EqualTo(0f));
            Assert.That(new PlayerVomitPose(false, -3f, -1f, 2f).Spasm, Is.EqualTo(1f));
            Assert.That(new PlayerVomitPose(false, -3f, -1f, 2f).IsNone, Is.False, "A spasm is something to draw.");
        }

        // ---- helpers -------------------------------------------------

        private static int ExpectedBurst(float time)
        {
            for (int index = 0; index < 3; index++)
            {
                if (time >= BurstEdges[index * 2] && time < BurstEdges[index * 2 + 1])
                {
                    return index;
                }
            }

            return -1;
        }

        private static bool NearAnEdge(float time)
        {
            for (int index = 0; index < BurstEdges.Length; index++)
            {
                if (Mathf.Abs(time - BurstEdges[index]) < Step * 0.5f)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Run(HeroVomitModel model, float seconds)
        {
            int frames = Mathf.CeilToInt(seconds / Step);
            for (int frame = 0; frame < frames; frame++)
            {
                model.Advance(Step);
            }
        }

        private static void Drain(
            HeroVomitModel model,
            float time,
            List<HeroVomitCue> cues,
            List<float> consumedAt)
        {
            while (model.TryConsumeCue(out HeroVomitCue cue))
            {
                cues.Add(cue);
                consumedAt.Add(time);
            }
        }
    }
}
