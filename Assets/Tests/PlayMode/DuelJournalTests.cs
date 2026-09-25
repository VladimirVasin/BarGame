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
