using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class GameLogRepeatGateTests
    {
        [Test]
        public void Burst_EmitsFirstThreeThenSparseSummaries()
        {
            var gate = new GameLogRepeatGate(10000L, 8);

            AssertDecision(
                gate.Observe("same", 0L),
                GameLogRepeatAction.EmitOriginal,
                1);
            AssertDecision(
                gate.Observe("same", 1L),
                GameLogRepeatAction.EmitOriginal,
                2);
            AssertDecision(
                gate.Observe("same", 2L),
                GameLogRepeatAction.EmitOriginal,
                3);
            AssertDecision(
                gate.Observe("same", 3L),
                GameLogRepeatAction.EmitSummary,
                4);
            AssertDecision(
                gate.Observe("same", 4L),
                GameLogRepeatAction.Suppress,
                5);
            gate.Observe("same", 5L);
            gate.Observe("same", 6L);
            AssertDecision(
                gate.Observe("same", 7L),
                GameLogRepeatAction.EmitSummary,
                8);
        }

        [Test]
        public void QuietWindowAndReset_StartANewBurst()
        {
            var gate = new GameLogRepeatGate(100L, 8);
            gate.Observe("same", 10L);
            gate.Observe("same", 20L);

            AssertDecision(
                gate.Observe("same", 120L),
                GameLogRepeatAction.EmitOriginal,
                1);

            gate.Reset();
            AssertDecision(
                gate.Observe("same", 121L),
                GameLogRepeatAction.EmitOriginal,
                1);
        }

        [Test]
        public void RepeatGate_OutOfOrderTimestampDoesNotResetBurst()
        {
            var gate = new GameLogRepeatGate(100L, 8);

            gate.Observe("same", 50L);
            gate.Observe("same", 75L);
            gate.Observe("same", 25L);

            AssertDecision(
                gate.Observe("same", 150L),
                GameLogRepeatAction.EmitSummary,
                4);
        }

        [Test]
        public void UnitySignature_IsStableAndIncludesStackAndType()
        {
            string baseline =
                GameLogRuntime.CreateUnityLogSignature(
                    "message",
                    "stack-a",
                    LogType.Warning);

            Assert.That(
                GameLogRuntime.CreateUnityLogSignature(
                    "message",
                    "stack-a",
                    LogType.Warning),
                Is.EqualTo(baseline));
            Assert.That(
                GameLogRuntime.CreateUnityLogSignature(
                    "message",
                    "stack-b",
                    LogType.Warning),
                Is.Not.EqualTo(baseline));
            Assert.That(
                GameLogRuntime.CreateUnityLogSignature(
                    "message",
                    "stack-a",
                    LogType.Error),
                Is.Not.EqualTo(baseline));

            string sharedPrefix = new string(
                'x',
                GameLogFormatter.MaxStringCharacters);
            Assert.That(
                GameLogRuntime.CreateUnityLogSignature(
                    "message",
                    sharedPrefix + "a",
                    LogType.Warning),
                Is.Not.EqualTo(
                    GameLogRuntime.CreateUnityLogSignature(
                        "message",
                        sharedPrefix + "longer-tail",
                        LogType.Warning)));
        }

        [Test]
        public void RateGate_CapsMessagesAndReportsDropsSparsely()
        {
            var gate = new GameLogRateGate(100L, 3);

            AssertRateDecision(gate.Observe(0L), true, false, 0);
            AssertRateDecision(gate.Observe(1L), true, false, 0);
            AssertRateDecision(gate.Observe(2L), true, false, 0);
            AssertRateDecision(gate.Observe(3L), false, true, 1);
            AssertRateDecision(gate.Observe(4L), false, true, 2);
            AssertRateDecision(gate.Observe(5L), false, false, 3);
            AssertRateDecision(gate.Observe(6L), false, true, 4);
        }

        [Test]
        public void RateGate_NewWindowReportsFinalDropCountAndAllowsMessage()
        {
            var gate = new GameLogRateGate(100L, 1);

            gate.Observe(0L);
            gate.Observe(1L);
            gate.Observe(2L);
            GameLogRateDecision decision = gate.Observe(100L);

            AssertRateDecision(decision, true, false, 2);

            gate.Observe(101L);
            gate.Observe(102L);
            gate.Observe(103L);
            AssertRateDecision(
                gate.Observe(200L),
                true,
                true,
                3);
        }

        [Test]
        public void RateGate_ResetRestoresFullBudget()
        {
            var gate = new GameLogRateGate(100L, 1);
            gate.Observe(0L);
            Assert.That(gate.Observe(1L).AllowMessage, Is.False);

            gate.Reset();

            AssertRateDecision(gate.Observe(2L), true, false, 0);
        }

        [Test]
        public void RateGate_OutOfOrderTimestampDoesNotResetBudget()
        {
            var gate = new GameLogRateGate(100L, 1);

            AssertRateDecision(gate.Observe(50L), true, false, 0);
            AssertRateDecision(gate.Observe(75L), false, true, 1);
            AssertRateDecision(gate.Observe(25L), false, true, 2);
            AssertRateDecision(gate.Observe(150L), true, false, 2);
        }

        private static void AssertDecision(
            GameLogRepeatDecision decision,
            GameLogRepeatAction expectedAction,
            int expectedCount)
        {
            Assert.That(decision.Action, Is.EqualTo(expectedAction));
            Assert.That(
                decision.OccurrenceCount,
                Is.EqualTo(expectedCount));
        }

        private static void AssertRateDecision(
            GameLogRateDecision decision,
            bool allowMessage,
            bool emitSummary,
            int droppedCount)
        {
            Assert.That(
                decision.AllowMessage,
                Is.EqualTo(allowMessage));
            Assert.That(
                decision.EmitSummary,
                Is.EqualTo(emitSummary));
            Assert.That(
                decision.DroppedCount,
                Is.EqualTo(droppedCount));
        }
    }
}
