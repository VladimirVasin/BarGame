using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    [Category("BarConversation")]
    public sealed class BarConversationScheduleTests
    {
        [TestCase(17)]
        [TestCase(20261023)]
        public void Schedule_KeepsSeededWholeExchangesFairAndCommitsOnlyActualSpeech(int seed)
        {
            VerifyDeckRoundsAndConditions(seed);
            VerifySeparateHighTablesStaySilent(seed);
            VerifyAuthoredPairsAndFairAdmission(seed);
            VerifyIndependentPairChannels(seed);
            VerifyFirstGlyphServiceAndCancellation(seed);
            VerifyCommentCapacityEnvelope();
        }

        private static void VerifySeparateHighTablesStaySilent(int seed)
        {
            Assert.That(BarConversationCatalog.KindCount, Is.EqualTo(4),
                "Dormant high-table keys retain their catalog topology.");
            Assert.That(BarConversationCatalog.ActiveGroupCount, Is.EqualTo(3));
            for (int group = 0; group < BarConversationCatalog.KindCount; group++)
                Assert.That(BarConversationCatalog.IsEnabled((BarConversationKind)group), Is.EqualTo(group < 3));
            for (int role = 0; role < BarConversationCatalog.RoleCount; role++)
                Assert.That(BarConversationCatalog.CanSpeak(role), Is.EqualTo(role < 6));
            Assert.That(BarConversationCatalog.CanSpeak(-1), Is.False);
            Assert.That(BarConversationCatalog.CanSpeak(BarConversationCatalog.RoleCount), Is.False);
            Assert.That(BarConversationCatalog.Count(BarConversationKind.HighTables), Is.EqualTo(24));
            BarConversationExchange dormant = BarConversationCatalog.Get(BarConversationKind.HighTables, 0);
            Assert.That(dormant.FirstKey, Is.EqualTo("bar.speech.ht01.a"));
            Assert.That(BarConversationCatalog.CanSpeak(dormant.FirstRole), Is.False);
            Assert.That(BarConversationCatalog.CanSpeak(dormant.SecondRole), Is.False);

            var fixedHighTables = new BarConversationSchedule(seed, BarConversationKind.HighTables);
            var highTablesOnly = new BarConversationSchedule(seed);
            for (int step = 0; step < 200; step++)
            {
                double now = step * .25d;
                Assert.That(fixedHighTables.Advance(now, BarConversationSchedule.AllGroups, true).HasExchange,
                    Is.False, "Caller eligibility cannot enable a prohibited fixed conversation.");
                Assert.That(highTablesOnly.Advance(now, 1 << (int)BarConversationKind.HighTables, true).HasExchange,
                    Is.False, "The general schedule cannot select the two lone visitors either.");
                Assert.That(fixedHighTables.StartLine(now, .75d), Is.False);
            }
            Assert.That(fixedHighTables.StartedLineCount, Is.Zero);
            Assert.That(highTablesOnly.StartedLineCount, Is.Zero);
        }

        private static void VerifyCommentCapacityEnvelope()
        {
            // Integer microseconds make every arrival/departure discontinuity
            // exact, including the instant immediately before a departure.
            const long scale = 1000000;
            // Retain the former serialized pair's blackout as conservative slack.
            // The independent bartender no longer waits for ambient speech.
            const long pairBlock = 11200000;
            const long commentLine = 4958334;
            long[] ownerPeriods = { 9410000, 35590000, 50800000 };
            double arrivalsPerTick = 0d;
            foreach (long period in ownerPeriods) arrivalsPerTick += 1d / period;
            double netDrainPerTick = 1d / commentLine - arrivalsPerTick;
            Assert.That(netDrainPerTick, Is.GreaterThan(0d), "The bounded speech debt drains faster than all service owners can refill it.");
            // A <= 3 + rate*t, S >= (t-block)/line - 1. Beyond
            // this horizon the conservative A-S curve stays below zero.
            long horizon = (long)Math.Ceiling((4d + (double)pairBlock / commentLine) / netDrainPerTick) + scale;
            var edges = new SortedSet<long> { 0, horizon };
            foreach (long period in ownerPeriods)
                for (long at = period; at <= horizon; at += period) edges.Add(at);
            for (long at = pairBlock + commentLine; at <= horizon; at += commentLine) edges.Add(at);
            long maximum = 0;
            foreach (long edge in edges)
            {
                maximum = Math.Max(maximum, PendingBound(edge));
                if (edge > 0) maximum = Math.Max(maximum, PendingBound(edge - 1));
            }
            Assert.That(maximum, Is.LessThanOrEqualTo(BarConversationController.PendingCommentCapacity),
                "Even the former pair blackout plus the fastest hero and both NPC refills fits the fixed comment queue.");
            Assert.That(4d + (double)pairBlock / commentLine - horizon * netDrainPerTick, Is.LessThan(0d));

            long PendingBound(long time)
            {
                long accepted = 3;
                foreach (long period in ownerPeriods) accepted += time / period;
                long serviced = time <= pairBlock ? 0 : (time - pairBlock) / commentLine;
                return accepted - serviced;
            }
        }

        private static void VerifyDeckRoundsAndConditions(int seed)
        {
            foreach (int count in new[] { 8, 12, 24, 32, 56 })
            {
                var deck = new BarSpeechVariantDeck(count, seed);
                var same = new BarSpeechVariantDeck(count, seed);
                var different = new BarSpeechVariantDeck(count, unchecked(seed + 1));
                bool differentOrder = false;
                int previous = -1;
                for (int round = 0; round < 3; round++)
                {
                    var seen = new HashSet<int>();
                    for (int i = 0; i < count; i++)
                    {
                        int variant = deck.Peek();
                        Assert.That(deck.Peek(), Is.EqualTo(variant), "Preparation cannot spend an unheard line.");
                        Assert.That(variant, Is.EqualTo(same.Peek()), "The seed determines a reproducible shuffle.");
                        int otherVariant = different.Peek();
                        differentOrder |= variant != otherVariant;
                        Assert.That(variant, Is.Not.EqualTo(previous), "The round boundary cannot repeat its last line.");
                        Assert.That(seen.Add(variant), Is.True, "Every available line precedes the next round.");
                        deck.Commit(variant);
                        same.Commit(variant);
                        different.Commit(otherVariant);
                        previous = variant;
                    }
                }
                Assert.That(deck.ConsumedCount, Is.EqualTo(count * 3));
                Assert.That(differentOrder, Is.True, "Different evenings can shuffle the authored lines differently.");
            }

            var conditional = new BarSpeechVariantDeck(12, seed);
            const int eligible = (1 << 0) | (1 << 4) | (1 << 11);
            int last = -1;
            for (int round = 0; round < 3; round++)
            {
                var heard = new HashSet<int>();
                for (int i = 0; i < 3; i++)
                {
                    Assert.That(conditional.TryPeek(eligible, out int variant), Is.True);
                    Assert.That(variant, Is.Not.EqualTo(last));
                    Assert.That(heard.Add(variant), Is.True,
                        "Ineligible foam/handle lines cannot stall or exhaust an eligible service pool.");
                    conditional.Commit(variant);
                    last = variant;
                }
            }
            Assert.That(conditional.TryPeek(0, out _), Is.False);
            Assert.That(conditional.TryPeek(1 << 2, out int newlyEligible), Is.True);
            Assert.That(newlyEligible, Is.EqualTo(2), "Unavailable entries remain unheard until their condition exists.");
            conditional.Commit(newlyEligible);
            Assert.Throws<ArgumentException>(() => conditional.Commit(newlyEligible), "One first glyph spends a variant once.");
        }

        private static void VerifyAuthoredPairsAndFairAdmission(int seed)
        {
            var schedule = new BarConversationSchedule(seed);
            var same = new BarConversationSchedule(seed);
            var variants = new List<int>[BarConversationSchedule.ActiveGroupCount];
            for (int i = 0; i < variants.Length; i++) variants[i] = new List<int>();
            double now = 0d;
            string pendingReply = null;
            int previousGroup = -1;
            int previousSpeaker = -1;
            int lines = 0;
            while (!AllPoolsHaveTwoRounds(variants) && now < 20000d)
            {
                now += .25d;
                BarConversationTurn turn = schedule.Advance(now, BarConversationSchedule.AllGroups, true);
                BarConversationTurn other = same.Advance(now, BarConversationSchedule.AllGroups, true);
                Assert.That(turn.NeedsLineStart, Is.EqualTo(other.NeedsLineStart));
                if (!turn.NeedsLineStart) continue;
                Assert.That(BarConversationCatalog.CanSpeak(turn.SpeakerRole), Is.True);
                Assert.That(BarConversationCatalog.CanSpeak(turn.PartnerRole), Is.True);
                Assert.That(turn.LineKey, Is.EqualTo(other.LineKey));
                if (pendingReply == null)
                {
                    Assert.That(turn.LineKey, Does.EndWith(".a"));
                    pendingReply = turn.Exchange.SecondKey;
                    int group = (int)turn.Exchange.Kind;
                    if (previousGroup >= 0) Assert.That(group, Is.EqualTo((previousGroup + 1) % variants.Length),
                        "Every available pair gets its turn before a neighbour gets another.");
                    previousGroup = group;
                    variants[group].Add(turn.Exchange.Variant);
                }
                else
                {
                    Assert.That(turn.LineKey, Is.EqualTo(pendingReply), "The answer belongs to its authored first line.");
                    Assert.That(turn.SpeakerRole, Is.Not.EqualTo(previousSpeaker));
                    pendingReply = null;
                }
                previousSpeaker = turn.SpeakerRole;
                Assert.That(schedule.StartLine(now, .75d), Is.True);
                Assert.That(same.StartLine(now, .75d), Is.True);
                Assert.That(schedule.StartLine(now, .75d), Is.False);
                Assert.That(schedule.Current.IsSpeaking, Is.True);
                lines++;
            }
            Assert.That(AllPoolsHaveTwoRounds(variants), Is.True);
            Assert.That(schedule.StartedLineCount, Is.EqualTo(lines));
            for (int group = 0; group < variants.Length; group++)
            {
                int count = BarConversationCatalog.Count((BarConversationKind)group);
                for (int round = 0; round < 2; round++)
                {
                    var seen = new HashSet<int>();
                    for (int i = 0; i < count; i++) Assert.That(seen.Add(variants[group][round * count + i]), Is.True);
                }
                Assert.That(variants[group][count], Is.Not.EqualTo(variants[group][count - 1]));
            }
        }

        private static bool AllPoolsHaveTwoRounds(IReadOnlyList<List<int>> variants)
        {
            for (int i = 0; i < variants.Count; i++)
                if (variants[i].Count < BarConversationCatalog.Count((BarConversationKind)i) * 2 + 1) return false;
            return true;
        }

        private static void VerifyIndependentPairChannels(int seed)
        {
            Assert.That(BarConversationSchedule.FirstAttemptMinimumSeconds, Is.EqualTo(.4d));
            Assert.That(BarConversationSchedule.FirstAttemptMaximumSeconds, Is.EqualTo(2d));
            Assert.That(BarConversationSchedule.ExchangeMinimumIntervalSeconds, Is.EqualTo(1.5d));
            Assert.That(BarConversationSchedule.ExchangeMaximumIntervalSeconds, Is.EqualTo(4d));
            Assert.That(BarConversationSchedule.ReplyMinimumGapSeconds, Is.EqualTo(.3d));
            Assert.That(BarConversationSchedule.ReplyMaximumGapSeconds, Is.EqualTo(.75d));
            var channels = new BarConversationSchedule[BarConversationSchedule.ActiveGroupCount];
            var variants = new List<int>[channels.Length];
            var pendingReplies = new string[channels.Length];
            var lineEnds = new double[channels.Length];
            var lastFirstLines = new double[channels.Length];
            var firstLines = new int[channels.Length];
            var heardThisRound = new HashSet<int>[channels.Length];
            var previousVariants = new int[channels.Length];
            for (int group = 0; group < channels.Length; group++)
            {
                channels[group] = new BarConversationSchedule(unchecked(seed + group * 3571),
                    (BarConversationKind)group);
                variants[group] = new List<int>();
                heardThisRound[group] = new HashSet<int>();
                previousVariants[group] = -1;
            }

            double now = 0d;
            int peakSpeakers = 0;
            int overlapSteps = 0;
            int steps = 0;
            while (!AllPoolsHaveTwoRounds(variants) && now < 2400d)
            {
                now += .1d;
                int speaking = 0;
                for (int group = 0; group < channels.Length; group++)
                {
                    BarConversationSchedule channel = channels[group];
                    BarConversationTurn turn = channel.Advance(now, BarConversationSchedule.AllGroups, true);
                    if (turn.NeedsLineStart)
                    {
                        Assert.That((int)turn.Exchange.Kind, Is.EqualTo(group),
                            "Each channel only admits its own pair, even while neighbours speak.");
                        if (pendingReplies[group] == null)
                        {
                            Assert.That(turn.LineKey, Is.EqualTo(turn.Exchange.FirstKey));
                            Assert.That(turn.SpeakerRole, Is.EqualTo(turn.Exchange.FirstRole));
                            pendingReplies[group] = turn.Exchange.SecondKey;
                            if (firstLines[group] == 0)
                                Assert.That(now, Is.LessThan(2.8d), "Every pair begins promptly after entry.");
                            else
                                Assert.That(now - lastFirstLines[group], Is.LessThan(18d),
                                    "A neighbour's speech never adds a room-wide quiet wait.");
                            lastFirstLines[group] = now;
                            firstLines[group]++;
                            int count = BarConversationCatalog.Count((BarConversationKind)group);
                            if (heardThisRound[group].Count == count) heardThisRound[group].Clear();
                            Assert.That(heardThisRound[group].Add(turn.Exchange.Variant), Is.True,
                                "Simultaneous pairs still exhaust their own pool before a repeat.");
                            Assert.That(turn.Exchange.Variant, Is.Not.EqualTo(previousVariants[group]),
                                "A shuffle boundary cannot repeat the last exchange.");
                            previousVariants[group] = turn.Exchange.Variant;
                            variants[group].Add(turn.Exchange.Variant);
                        }
                        else
                        {
                            Assert.That(turn.LineKey, Is.EqualTo(pendingReplies[group]),
                                "Overlapping conversations cannot borrow another pair's answer.");
                            Assert.That(turn.SpeakerRole, Is.EqualTo(turn.Exchange.SecondRole));
                            pendingReplies[group] = null;
                        }

                        // Actual bubble lifetimes, not the abbreviated fair-admission probe.
                        double duration = 4.4d + (channel.StartedLineCount + group) % 12 * .1d;
                        Assert.That(channel.StartLine(now, duration), Is.True);
                        lineEnds[group] = now + duration;
                    }
                    if (now < lineEnds[group]) speaking++;
                }
                peakSpeakers = Math.Max(peakSpeakers, speaking);
                if (speaking >= 2) overlapSteps++;
                steps++;
            }

            Assert.That(AllPoolsHaveTwoRounds(variants), Is.True,
                "Every independent pair keeps conversing through complete shuffled pools.");
            Assert.That(peakSpeakers, Is.EqualTo(BarConversationSchedule.ActiveGroupCount),
                "Counter and both booths can speak together, without a global service or guest lock.");
            Assert.That(overlapSteps, Is.GreaterThan(steps * .75d),
                "Multiple live conversations form the normal bar atmosphere.");
        }

        private static void VerifyFirstGlyphServiceAndCancellation(int seed)
        {
            var schedule = new BarConversationSchedule(seed);
            double now = 0d;
            const int counter = 1 << (int)BarConversationKind.Counter;
            BarConversationTurn ready = AwaitReady(schedule, ref now, counter);
            string unspent = ready.LineKey;
            Assert.That(schedule.HasCommittedExchange, Is.False);
            Assert.That(schedule.StartedLineCount, Is.Zero);
            Assert.That(schedule.Advance(now + .1d, counter, true, true).HasExchange, Is.False,
                "An order may interrupt anticipation before its first glyph.");
            now += .1d;
            for (int i = 0; i < 100; i++)
            {
                now += .25d;
                Assert.That(schedule.Advance(now, counter, true, true).HasExchange, Is.False,
                    "A pending order blocks fresh ambient admission.");
            }
            schedule.DeferAfterService(now);
            Assert.That(schedule.NextAttemptSeconds - now, Is.InRange(2d, 4d));
            ready = AwaitReady(schedule, ref now, counter);
            Assert.That(ready.LineKey, Is.EqualTo(unspent), "Unheard preparation never spends its exchange.");
            Assert.That(schedule.StartLine(now, 1.5d), Is.True);
            Assert.That(schedule.HasCommittedExchange, Is.True);
            double firstEnd = now + 1.5d;
            now += 1d;
            Assert.That(schedule.Advance(now, counter, true, true).IsSpeaking, Is.True,
                "The actual shared line duration governs the first speaker.");
            now = firstEnd;
            Assert.That(schedule.Advance(now, counter, true, true).NeedsLineStart, Is.False,
                "The answer waits after the first line finishes.");
            now += BarConversationSchedule.ReplyMinimumGapSeconds - .01d;
            Assert.That(schedule.Advance(now, counter, true, true).NeedsLineStart, Is.False);
            ready = AwaitReady(schedule, ref now, counter, true);
            Assert.That(ready.LineKey, Is.EqualTo(unspent.Substring(0, unspent.Length - 2) + ".b"),
                "Service debt cannot split an already begun A-to-B exchange.");
            schedule.StartLine(now, .75d);
            now += .8d;
            Assert.That(schedule.Advance(now, counter, true, true).HasExchange, Is.False);

            ready = AwaitReady(schedule, ref now, counter);
            string spent = ready.LineKey;
            schedule.StartLine(now, .75d);
            now += .1d;
            Assert.That(schedule.Advance(now, 0, true).HasExchange, Is.False);
            ready = AwaitReady(schedule, ref now, counter);
            Assert.That(ready.LineKey, Is.Not.EqualTo(spent), "Losing a partner cancels the reply but keeps the heard exchange spent.");
            schedule.StartLine(now, .75d);
            int started = schedule.StartedLineCount;
            now += 60d;
            Assert.That(schedule.Advance(now, counter, true).HasExchange, Is.False);
            Assert.That(schedule.StartedLineCount, Is.EqualTo(started), "Clock jumps never catch up missed lines.");
            now += .1d;
            Assert.That(schedule.Advance(now, counter, false).HasExchange, Is.False);
            Assert.That(schedule.Advance(now + .1d, counter, true).HasExchange, Is.False,
                "Returning to earshot leaves a normal pause.");
        }

        private static BarConversationTurn AwaitReady(BarConversationSchedule schedule, ref double now,
            int groups, bool reserveService = false)
        {
            double deadline = now + 100d;
            while (now < deadline)
            {
                now += .25d;
                BarConversationTurn turn = schedule.Advance(now, groups, true, reserveService);
                if (turn.NeedsLineStart) return turn;
            }
            Assert.Fail("The eligible bar pair did not get a speaking opportunity.");
            return default;
        }
    }
}
