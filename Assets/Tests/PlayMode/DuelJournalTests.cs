using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>Pure queue/storage checks; no scene, animation, Unity clock or rendering.</summary>
    public sealed class DuelJournalTests
    {
        private string directory;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "BarPromenade-DuelJournalTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown()
        {
            string full = Path.GetFullPath(directory);
            string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            Assert.That(Path.GetDirectoryName(full), Is.EqualTo(parent));
            Assert.That(Path.GetFileName(full).StartsWith("BarPromenade-DuelJournalTests-"), Is.True);
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }

        [Test]
        public void RoundsPreserveCorrelationAndResetFooterWithoutAllocatingPerEvent()
        {
            using var journal = new DuelJournal(directory, "correlation");
            journal.SetClock(17, 31, .25);
            journal.BeginRound(1, GameLog.Field("seed", 123));
            Wait(journal.FlushAsync());
            journal.Record("state", f0: GameLog.Field("weight", .5f));
            Wait(journal.FlushAsync());
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 128; i++) journal.Record("state", f0: GameLog.Field("weight", .5f));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, "Sampling cannot allocate on the producer thread.");
            long requested = journal.Record("command_requested", 1, 2, 4, 8);
            long rejected = journal.Record("command_result", 1, 2, 4, 8,
                GameLog.Field("result", "rejected"), GameLog.Field("reason", "missing_support"));
            Assert.That(rejected, Is.GreaterThan(requested));
            for (int i = 0; i < 3; i++) journal.Record("command_result", 1, 2, 4, 9 + i,
                GameLog.Field("result", "rejected"), GameLog.Field("reason", "missing_support"));
            journal.Record("command_result", 1, 2, 4, 12,
                GameLog.Field("result", "accepted"), GameLog.Field("reason", "none"));
            journal.Record("frame", f0: GameLog.Field("frame_ms", 42.5));
            journal.Mark("visible problem");
            journal.BeginRound(2); // Must finalize the old round with a reset reason.
            journal.SetClock(18, 32, .5);
            journal.Record("impact", 2, 1, 5, 9);
            journal.EndRound("manual_stop");
            Wait(journal.FlushAsync());
            string first = Path.Combine(directory, "duel_correlation_000001");
            string second = Path.Combine(directory, "duel_correlation_000002");
            string trace = File.ReadAllText(Path.Combine(first, "duel.ndjson"));
            StringAssert.Contains("\"event\":\"round_begin\"", trace);
            StringAssert.Contains("\"frame\":17,\"tick\":31,\"duel_seconds\":0.25", trace);
            StringAssert.Contains("\"actor\":1,\"target\":2,\"action\":4,\"request\":8", trace);
            StringAssert.Contains("\"reason\":\"reset\"", trace);
            Assert.That(File.Exists(Path.Combine(first, ".marked")), Is.True);
            Assert.That(File.Exists(Path.Combine(first, ".active")), Is.False);
            string summary = File.ReadAllText(Path.Combine(first, "summary.txt"));
            StringAssert.Contains("missing_support: 4", summary);
            StringAssert.Contains("\"reject_series_count\":4", trace);
            StringAssert.Contains("\"summarized_rejections\":3", trace);
            StringAssert.Contains("17: 42.500", summary);
            StringAssert.Contains("command_result actor=1 action=4 request=8 reason=missing_support", summary);
            StringAssert.Contains("\"reason\":\"manual_stop\"", File.ReadAllText(Path.Combine(second, "duel.ndjson")));
            Assert.That(journal.DroppedRecords, Is.Zero);
        }

        [Test]
        public void ZeroStageSamplesPreserveNonzeroUnavailableAndCausalRecordsWithoutAllocation()
        {
            using var journal = new DuelJournal(directory, "Test sparse stages");
            journal.BeginRound(1);
            Wait(journal.FlushAsync());
            string[] stages = { "damage_stage_sample", "body_sync_sample", "blood_stage_sample",
                "head_stage_sample", "projectile_defeat_stage_sample" };
            long before = GC.GetAllocatedBytesForCurrentThread();
            long omittedSequences = 0;
            for (int i = 0; i < 128; i++)
                foreach (string stage in stages)
                    omittedSequences += journal.Record(stage, f0: GameLog.Field("first_ms", 0d),
                        f1: GameLog.Field("second_ms", 0f), f2: GameLog.Field("calls", 0), f3: GameLog.Field("steps", 0L));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, "Omitting zero measurements cannot allocate on the producer thread.");
            Assert.That(omittedSequences, Is.Zero, "Zero samples are omitted before queueing or assigning sequence IDs.");
            foreach (string stage in stages)
                journal.Record(stage, f0: GameLog.Field("measured_ms", .001d));
            journal.Record("head_stage_sample", f0: GameLog.Field("unavailable_ms", double.NaN));
            journal.Record("head_stage_sample", f0: GameLog.Field("unsupported_ms", (string)null));
            journal.Record("head_stage_sample");
            journal.Record("head_stage_sample", f0: GameLog.Field("available", false));
            journal.Record("unknown_stage_sample", f0: GameLog.Field("measured_ms", 0d));
            journal.Record("impact", 1, 2, 4, 8, f0: GameLog.Field("power", 0f));
            journal.Record("round_result", f0: GameLog.Field("hero_defeated", false));
            journal.EndRound("Test sparse stages ended");
            Wait(journal.FlushAsync());
            string folder = Directory.GetDirectories(directory).Single();
            string[] rows = File.ReadAllLines(Path.Combine(folder, "duel.ndjson"));
            StringAssert.Contains("\"duel_schema_version\":3", rows[0]);
            StringAssert.Contains("\"stage_sample_encoding\":\"absent_numeric_zero\"", rows[0]);
            StringAssert.Contains("\"zero_stage_events\":\"" + string.Join(",", stages) + "\"", rows[0]);
            foreach (string stage in stages)
                Assert.That(rows.Count(row => row.Contains("\"event\":\"" + stage + "\"")),
                    Is.EqualTo(stage == "head_stage_sample" ? 5 : 1));
            Assert.That(rows.Any(row => row.Contains("\"unavailable_ms\":null")), Is.True);
            Assert.That(rows.Any(row => row.Contains("\"unsupported_ms\":null")), Is.True);
            Assert.That(rows.Any(row => row.Contains("\"available\":false")), Is.True);
            Assert.That(rows.Any(row => row.Contains("\"event\":\"unknown_stage_sample\"")), Is.True);
            Assert.That(rows.Any(row => row.Contains("\"event\":\"impact\"") &&
                row.Contains("\"actor\":1,\"target\":2,\"action\":4,\"request\":8")), Is.True);
            Assert.That(rows.Any(row => row.Contains("\"event\":\"round_result\"")), Is.True);
            Assert.That(journal.DroppedRecords, Is.Zero, "Sparse zero measurements are represented, rather than lost.");
        }

        [Test]
        public void RuleRequestReasonsKeepTheirActionClockAndClearFailedStartWhenQueued()
        {
            var state = new MeleeCombatant(new MeleeCombatSettings(maxStamina: 15f));
            Assert.That(state.TryStartStep(), Is.True);
            state.CancelAction();
            Assert.That(state.RequestKick(), Is.False);
            Assert.That(state.LastCommandAction, Is.EqualTo(MeleeBufferedAction.Kick));
            Assert.That(state.LastCommandRejection, Is.EqualTo(MeleeCommandRejection.Stamina));
            Assert.That(state.CooldownRemaining(state.LastCommandAction), Is.Zero,
                "An unaffordable kick cannot inherit the interrupted step's cooldown.");

            state.Reset();
            Assert.That(state.TryStartAttack(), Is.True);
            Assert.That(state.RequestStep(), Is.False);
            Assert.That(state.LastCommandRejection, Is.EqualTo(MeleeCommandRejection.Phase));
            Assert.That(state.RequestCharge(), Is.True);
            Assert.That(state.HasBufferedCharge, Is.True);
            Assert.That(state.LastCommandRejection, Is.EqualTo(MeleeCommandRejection.None));

            state.Reset();
            Assert.That(state.TryStartAttack(), Is.True);
            state.CancelAction();
            Assert.That(state.RequestAttack(), Is.False);
            Assert.That(state.LastCommandAction, Is.EqualTo(MeleeBufferedAction.Attack));
            Assert.That(state.LastCommandRejection, Is.EqualTo(MeleeCommandRejection.Cooldown));
            Assert.That(state.CooldownRemaining(state.LastCommandAction), Is.GreaterThan(state.Settings.AttackBufferSeconds));
            state.Advance(state.CooldownRemaining(MeleeBufferedAction.Charge) - state.Settings.AttackBufferSeconds * .5f);
            Assert.That(state.RequestCharge(), Is.True, "A later press can queue inside the same cooldown's buffer window.");
            Assert.That(state.HasBufferedCharge, Is.True);
            Assert.That(state.LastCommandRejection, Is.EqualTo(MeleeCommandRejection.None),
                "A failed immediate charge start must not remain the reason of an accepted queue.");

            state.Reset();
            Assert.That(state.ReceiveShove(.7f), Is.True);
            Assert.That(state.RequestStep(), Is.False);
            Assert.That(state.LastCommandRejection, Is.EqualTo(MeleeCommandRejection.BufferWindow));
            state.Reset();
            state.BeginKnockdown();
            Assert.That(state.RequestAttack(), Is.False);
            Assert.That(state.LastCommandRejection, Is.EqualTo(MeleeCommandRejection.KnockedDown));
        }

        [Test]
        public void QueueAndFileLimitsReportLossesAndStillFinishTheRound()
        {
            using var journal = new DuelJournal(directory, "bounded", capacity: 8, maxRoundBytes: 4096);
            journal.BeginRound(1);
            Wait(journal.FlushAsync());
            Assert.That(journal.Record("too_large", f0: GameLog.Field("text", new string('x', 513))), Is.Zero);
            GameLogField payload = GameLog.Field("text", new string('x', 256));
            for (int i = 0; i < 3000; i++) journal.Record("state", f0: payload);
            journal.Mark("keep this failure");
            journal.EndRound("bounded_test");
            Wait(journal.FlushAsync());
            string folder = Path.Combine(directory, "duel_bounded_000001");
            string trace = File.ReadAllText(Path.Combine(folder, "duel.ndjson"));
            StringAssert.Contains("\"event\":\"file_limit\"", trace);
            StringAssert.Contains("\"event\":\"round_end\"", trace);
            Assert.That(journal.DroppedRecords, Is.GreaterThan(0));
            Assert.That(journal.MaximumQueued, Is.LessThanOrEqualTo(16));
            Assert.That(Directory.GetFiles(folder).Sum(path => new FileInfo(path).Length), Is.LessThanOrEqualTo(4096));
            StringAssert.Contains("file_limit: True", File.ReadAllText(Path.Combine(folder, "summary.txt")));
            Assert.That(File.Exists(Path.Combine(folder, ".marked")), Is.True);
        }

        [Test]
        public void TelemetryLimitKeepsLateCausalRecordsWithinTheRoundBudget()
        {
            const int budget = 131072;
            using var journal = new DuelJournal(directory, "Test causal priority", capacity: 32, maxRoundBytes: budget);
            journal.BeginRound(1);
            Wait(journal.FlushAsync());
            GameLogField payload = GameLog.Field("text", new string('x', 256));
            long thinnedDetail = 0;
            for (int batch = 0; batch < 20; batch++)
            {
                for (int sample = 0; sample < 8; sample++) journal.Record("pose", f0: payload);
                Wait(journal.FlushAsync()); // File policy, rather than a full producer queue, is under test.
                if (batch == 4)
                {
                    journal.SetClock(1004, 0, 0d);
                    journal.Record("pose", f0: GameLog.Field("bone", "pelvis"));
                    journal.SetClock(1005, 0, 0d);
                    thinnedDetail = journal.Record("pose", f0: payload);
                    journal.SetClock(0, 0, 0d);
                    Wait(journal.FlushAsync());
                }
            }
            journal.SetClock(1500, 9000, 75d);
            journal.Record("state", actor: 1, f0: GameLog.Field("phase", "Rising"));
            journal.Record("frame", f0: GameLog.Field("frame_ms", 55d));
            Wait(journal.FlushAsync());
            // Ordinary detail cannot exhaust the independent essential quota.
            // Force that quota to its own limit before the requested snapshots.
            long retainedSnapshot = 0;
            for (int batch = 0; batch < 20; batch++)
            {
                for (int sample = 0; sample < 8; sample++) journal.Record("state", f0: payload);
                Wait(journal.FlushAsync());
                if (batch == 1)
                {
                    // All these regular snapshots miss frame%4. Encounter-based
                    // sampling must retain one complete actor state group anyway.
                    for (int sample = 0; sample < 6; sample++)
                    {
                        journal.SetClock(1501 + sample * 64, 9000, 75d);
                        long snapshot = journal.Record("state", actor: 2, f0: GameLog.Field("phase", "Rising"));
                        if (sample == 2) retainedSnapshot = snapshot;
                    }
                    journal.SetClock(1500, 9000, 75d);
                    Wait(journal.FlushAsync());
                }
            }
            Assert.That(journal.DroppedRecords, Is.Zero, "Deliberate thinning is reported separately from data loss.");
            using (journal.ForceSnapshot("impact"))
            {
                journal.Record("state", actor: 1, f0: GameLog.Field("phase", "Defeated"));
                journal.Record("pose", actor: 1, f0: GameLog.Field("bone", "pelvis"));
            }
            // Automatic impact bursts also have a finite quota; they cannot
            // consume the reserved manual/stall or terminal state capacity.
            for (int batch = 0; batch < 20; batch++)
            {
                using (journal.ForceSnapshot("impact"))
                    for (int sample = 0; sample < 8; sample++) journal.Record("pose", f0: payload);
                Wait(journal.FlushAsync());
            }
            string[] reasons = { "recovery_stall", "marked", "round_ended" };
            long before = GC.GetAllocatedBytesForCurrentThread();
            foreach (string reason in reasons)
                using (journal.ForceSnapshot(reason))
                {
                    journal.Record("state", actor: 1, f0: GameLog.Field("phase", "Defeated"));
                    journal.Record("pose", actor: 1, f0: GameLog.Field("bone", "pelvis"));
                }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero,
                "A forced snapshot scope and its producer events cannot allocate.");
            long request = journal.Record("command_requested", 1, 2, 4, 11, GameLog.Field("command", "charge"));
            journal.Record("command_result", 1, 2, 4, 11, GameLog.Field("result", "accepted"));
            journal.Record("impact_applied", 1, 2, 4, 11, GameLog.Field("power", 12f));
            journal.Mark("Test late visible strike");
            var staleScope = journal.ForceSnapshot("impact");
            var staleNestedScope = journal.ForceSnapshot("marked");
            journal.EndRound("Test priority ended");
            Wait(journal.FlushAsync());
            string folder = Directory.GetDirectories(directory).Single();
            string trace = File.ReadAllText(Path.Combine(folder, "duel.ndjson"));
            StringAssert.Contains("\"cause\":\"telemetry_budget\"", trace);
            StringAssert.Contains("\"event\":\"command_requested\"", trace);
            StringAssert.Contains("\"seq\":" + request + ",", trace);
            StringAssert.Contains("\"frame\":1500,\"tick\":9000,\"duel_seconds\":75", trace);
            StringAssert.Contains("\"actor\":1,\"target\":2,\"action\":4,\"request\":11", trace);
            StringAssert.Contains("\"event\":\"impact_applied\"", trace);
            StringAssert.Contains("\"event\":\"mark\"", trace);
            StringAssert.Contains("\"causal_dropped_records\":0", trace);
            StringAssert.Contains("\"producer_dropped_records\":0", trace);
            StringAssert.Contains("\"telemetry_limit\":true", trace);
            StringAssert.Contains("\"causal_limit\":false", trace);
            StringAssert.Contains("\"phase\":\"Rising\"", trace);
            StringAssert.Contains("\"frame_ms\":55", trace);
            StringAssert.Contains("\"sample_group_stride\":4", trace);
            StringAssert.Contains("\"seq\":" + retainedSnapshot + ",", trace);
            Assert.That(trace.Contains("\"seq\":" + thinnedDetail + ","), Is.False,
                "The ordinary detail policy deliberately thins a frame before exhausting its quota.");
            foreach (string reason in reasons)
                Assert.That(File.ReadAllLines(Path.Combine(folder, "duel.ndjson")).Count(row =>
                    row.Contains("\"sample_priority\":\"forced\"") && row.Contains("\"snapshot_reason\":\"" + reason + "\"")),
                    Is.EqualTo(2), "A late requested state and pelvis pose survive both ordinary quotas.");
            Assert.That(File.ReadAllLines(Path.Combine(folder, "duel.ndjson")).Count(row =>
                row.Contains("\"snapshot_reason\":\"impact\"") && row.Contains("\"bone\":\"pelvis\"")), Is.EqualTo(1));
            Assert.That(journal.DroppedRecords, Is.GreaterThan(0), "Exhausted forced capacity is real, explicit loss.");
            Assert.That(trace.Contains("\"telemetry_dropped_records\":0"), Is.False);
            string summary = File.ReadAllText(Path.Combine(folder, "summary.txt"));
            StringAssert.Contains("Adaptive samples omitted (not losses): detail=", summary);
            Assert.That(trace.Contains("\"detail_sampled_out_records\":0"), Is.False);
            Assert.That(trace.Contains("\"essential_sampled_out_records\":0"), Is.False);
            Assert.That(Directory.GetFiles(folder).Sum(path => new FileInfo(path).Length), Is.LessThanOrEqualTo(budget));
            Assert.That(File.Exists(Path.Combine(folder, ".complete")), Is.True);
            journal.BeginRound(2);
            staleNestedScope.Dispose();
            staleScope.Dispose();
            journal.Record("pose", f0: GameLog.Field("bone", "pelvis"));
            journal.EndRound("Test fresh priority round");
            Wait(journal.FlushAsync());
            string reset = File.ReadAllText(Path.Combine(directory, "duel_Test-causal-priority_000002", "duel.ndjson"));
            StringAssert.Contains("\"detail_sampled_out_records\":0", reset);
            StringAssert.Contains("\"essential_sampled_out_records\":0", reset);
            Assert.That(reset.Contains("\"snapshot_reason\""), Is.False, "Forced scopes and budgets do not leak to another round.");
        }

        [Test]
        public void CausalTailPreservesLateActionsWhenFileBudgetIsFull()
        {
            const int budget = 32768;
            using var journal = new DuelJournal(directory, "Test causal tail", capacity: 32, maxRoundBytes: budget);
            journal.BeginRound(1);
            Wait(journal.FlushAsync());
            GameLogField payload = GameLog.Field("text", new string('x', 256));
            for (int batch = 0; batch < 20; batch++)
            {
                for (int sample = 0; sample < 8; sample++) journal.Record("weapon_constraint", f0: payload);
                Wait(journal.FlushAsync());
            }
            journal.SetClock(2001, 12001, 99.5d);
            long request = journal.Record("command_requested", 1, 2, 42, 9, GameLog.Field("command", "charge"));
            journal.Record("command_result", 1, 2, 42, 9, GameLog.Field("result", "accepted"));
            journal.Record("weapon_contact_rejected", 1, 2, 42, 9, GameLog.Field("reason", "world_blocked"));
            for (int batch = 0; batch < 10; batch++)
            {
                for (int sample = 0; sample < 8; sample++) journal.Record("time_discarded", f0: GameLog.Field("seconds", .01f));
                Wait(journal.FlushAsync());
            }
            string folder = Directory.GetDirectories(directory).Single();
            string liveSummary = File.ReadAllText(Path.Combine(folder, "summary.txt"));
            StringAssert.Contains("Status: live", liveSummary);
            StringAssert.Contains("\"reason\":\"world_blocked\"", liveSummary);
            Assert.That(File.Exists(Path.Combine(folder, ".active")), Is.True);
            Assert.That(File.Exists(Path.Combine(folder, ".complete")), Is.False);
            journal.EndRound("Test tail ended");
            Wait(journal.FlushAsync());
            string trace = File.ReadAllText(Path.Combine(folder, "duel.ndjson"));
            string summary = File.ReadAllText(Path.Combine(folder, "summary.txt"));
            StringAssert.Contains("\"cause\":\"round_budget\"", trace);
            StringAssert.Contains("\"causal_limit\":true", trace);
            Assert.That(trace.Contains("\"event\":\"command_requested\""), Is.False,
                "This action happened after the hard file cap, so its retained copy belongs to the summary.");
            StringAssert.Contains("\"event\":\"command_requested\"", summary);
            StringAssert.Contains("\"event\":\"weapon_contact_rejected\"", summary);
            StringAssert.Contains("\"reason\":\"world_blocked\"", summary);
            StringAssert.Contains("\"seq\":" + request + ",", summary);
            StringAssert.Contains("\"frame\":2001,\"tick\":12001,\"duel_seconds\":99.5", summary);
            StringAssert.Contains("\"actor\":1,\"target\":2,\"action\":42,\"request\":9", summary);
            StringAssert.Contains("\"causal_tail_copy\":true", summary);
            Assert.That(Directory.GetFiles(folder).Sum(path => new FileInfo(path).Length), Is.LessThanOrEqualTo(budget));
            Assert.That(File.Exists(Path.Combine(folder, ".complete")), Is.True);

            journal.BeginRound(2);
            journal.Mark("Test fresh round");
            journal.EndRound("Test reset ended");
            Wait(journal.FlushAsync());
            string reset = Path.Combine(directory, "duel_Test-causal-tail_000002", "summary.txt");
            Assert.That(File.ReadAllText(reset).Contains("world_blocked"), Is.False,
                "A new round releases every retained packet and cannot inherit the previous action tail.");
        }

        [Test]
        public void GeneralShutdownPublishesCachedSummaryBeforeEndAndIsolatesObservers()
        {
            string path = Path.Combine(directory, "Test final general log.ndjson");
            int calls = 0;
            Action broken = () => throw new InvalidOperationException("Test shutdown observer");
            Action cached = () =>
            {
                calls++;
                GameLog.Info("combat", "movement_summary", GameLog.Field("source_scene", "CombatTest"));
                GameLog.Shutdown("Test recursive shutdown", true);
            };
            try
            {
                GameLog.Initialize(new GameLogSettings(GameLogProfile.Verbose, path), new SystemGameLogClock(), "Test final session", false);
                GameLog.BeforeShutdown += broken;
                GameLog.BeforeShutdown += cached;
                GameLog.Shutdown("Test session ended", true);
                GameLog.Shutdown("Test repeated shutdown", true);
                string[] rows = File.ReadAllLines(path);
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(rows.Count(row => row.Contains("\"event\":\"shutdown_observer_failed\"")), Is.EqualTo(1));
                Assert.That(rows.Count(row => row.Contains("\"event\":\"movement_summary\"")), Is.EqualTo(1));
                Assert.That(rows.Count(row => row.Contains("\"event\":\"end\"")), Is.EqualTo(1));
                Assert.That(Array.FindIndex(rows, row => row.Contains("\"event\":\"movement_summary\"")),
                    Is.LessThan(Array.FindIndex(rows, row => row.Contains("\"event\":\"end\""))));
                StringAssert.Contains("\"reason\":\"Test session ended\"", rows.Last());
                Assert.That(GameLog.CurrentFilePath, Is.Empty);
            }
            finally
            {
                GameLog.BeforeShutdown -= broken;
                GameLog.BeforeShutdown -= cached;
                GameLog.Shutdown("Test cleanup", false);
            }
        }

        [Test]
        public void RetentionProtectsLiveAndForeignFilesAndPrefersMarkedRoundsWithinBudget()
        {
            string old = FakeRound(1, 300, marked: false);
            string marked = FakeRound(2, 300, marked: true);
            string newer = FakeRound(3, 300, marked: false);
            string live = FakeRound(4, 300, marked: false);
            string foreign = Path.Combine(directory, "duel_foreign_000001");
            Directory.CreateDirectory(foreign);
            File.WriteAllText(Path.Combine(foreign, "personal.txt"), "keep");
            string unrelated = Path.Combine(directory, "unrelated");
            Directory.CreateDirectory(unrelated);
            File.WriteAllText(Path.Combine(unrelated, "duel.ndjson"), "keep");
            using (var lease = new FileStream(Path.Combine(live, ".active"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
            {
                byte[] reservation = Encoding.ASCII.GetBytes("4096"); lease.Write(reservation, 0, reservation.Length); lease.Flush();
                DuelJournal.Prune(directory, maxCompleted: 2, maxBytes: 4600);
                Assert.That(Directory.Exists(old), Is.False);
                Assert.That(Directory.Exists(newer), Is.False);
                Assert.That(Directory.Exists(marked), Is.True);
                Assert.That(Directory.Exists(live), Is.True);
                Assert.That(File.Exists(Path.Combine(foreign, "personal.txt")), Is.True);
                Assert.That(File.Exists(Path.Combine(unrelated, "duel.ndjson")), Is.True);
                byte[] oversized = Encoding.ASCII.GetBytes(long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
                lease.Position = 0; lease.SetLength(0); lease.Write(oversized, 0, oversized.Length); lease.Flush();
                DuelJournal.Prune(directory, maxCompleted: 2, maxBytes: 20L * 1024L * 1024L + 512);
                Assert.That(Directory.Exists(marked), Is.True, "An oversized numeric reservation is clamped to 20 MiB.");
                byte[] foreignLease = Encoding.ASCII.GetBytes(new string('x', 4096));
                lease.Position = 0; lease.SetLength(0); lease.Write(foreignLease, 0, foreignLease.Length); lease.Flush();
                DuelJournal.Prune(directory, maxCompleted: 2, maxBytes: 20L * 1024L * 1024L + 512);
                Assert.That(Directory.Exists(marked), Is.True, "Long or foreign lease contents use the bounded conservative reservation.");
                Assert.That(lease.Length, Is.EqualTo(foreignLease.Length), "Cleanup must not rewrite live foreign lease contents.");
                DuelJournal.Prune(directory, maxCompleted: 0, maxBytes: 4096);
                Assert.That(Directory.Exists(marked), Is.False, "A mark raises priority; it cannot retain files forever.");
                Assert.That(Directory.Exists(live), Is.True);
            }
            DuelJournal.Prune(directory, maxCompleted: 0, maxBytes: 0);
            Assert.That(Directory.Exists(live), Is.False, "An unlocked crash-partial lease is reclaimable.");
        }

        [Test]
        public void IoFailureDisablesCollectionAndCompletesBarriers()
        {
            string file = Path.Combine(directory, "not-a-directory");
            File.WriteAllText(file, "keep");
            using var journal = new DuelJournal(file, "io");
            journal.BeginRound(1);
            Wait(journal.FlushAsync());
            Wait(journal.Completion);
            Assert.That(journal.Enabled, Is.False);
            StringAssert.StartsWith("io_error:", journal.LastError);
            Assert.That(journal.Record("ignored"), Is.Zero);
            Assert.That(File.ReadAllText(file), Is.EqualTo("keep"));
        }

        private string FakeRound(int round, int bytes, bool marked)
        {
            string path = Path.Combine(directory, "duel_test_" + round.ToString("D6"));
            Directory.CreateDirectory(path);
            File.WriteAllBytes(Path.Combine(path, "duel.ndjson"), new byte[bytes]);
            File.WriteAllText(Path.Combine(path, ".complete"), string.Empty);
            if (marked) File.WriteAllText(Path.Combine(path, ".marked"), string.Empty);
            Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-20 + round));
            return path;
        }

        private static void Wait(Task task) => Assert.That(task.Wait(TimeSpan.FromSeconds(5)), Is.True, "The finite journal queue must drain.");
    }
}
