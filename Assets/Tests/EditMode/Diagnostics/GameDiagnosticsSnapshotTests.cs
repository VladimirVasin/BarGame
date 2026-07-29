using System;
using System.IO;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class GameDiagnosticsSnapshotTests
    {
        private string directory;
        private string logPath;

        [SetUp]
        public void SetUp()
        {
            GameLog.Shutdown("snapshot_test_setup", false);
            directory = Path.Combine(
                Path.GetTempPath(),
                "BarPromenade-SnapshotTests-" +
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            logPath = Path.Combine(directory, "debug.log");
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.Shutdown("snapshot_test_teardown", false);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void Capture_WritesAndFlushesOneStructuredSnapshot()
        {
            GameLog.Initialize(
                new GameLogSettings(
                    GameLogProfile.Basic,
                    logPath),
                new SystemGameLogClock(),
                "snapshot-session",
                false);
            GameLog.SetScene("SnapshotTest");
            GameLog.SetCitySeed(2468);

            bool captured =
                GameDiagnosticsSnapshot.Capture("editmode_test");

            string[] lines = ReadLiveLines(logPath);
            Assert.That(captured, Is.True);
            Assert.That(lines, Has.Length.EqualTo(1));
            Assert.That(
                lines[0],
                Does.Contain("\"schema_version\":1"));
            Assert.That(
                lines[0],
                Does.Contain("\"category\":\"diagnostics\""));
            Assert.That(
                lines[0],
                Does.Contain("\"event\":\"snapshot\""));
            Assert.That(
                lines[0],
                Does.Contain("\"reason\":\"editmode_test\""));
            Assert.That(
                lines[0],
                Does.Contain("\"city_seed\":2468"));
            Assert.That(
                lines[0],
                Does.Contain("\"root_kind\":\"none\""));
        }

        [Test]
        public void OffProfile_DisablesSnapshotAndDirectoryCommand()
        {
            GameLog.Initialize(
                new GameLogSettings(
                    GameLogProfile.Off,
                    logPath),
                new SystemGameLogClock(),
                "snapshot-off",
                false);

            Assert.That(
                GameDiagnosticsSnapshot.Capture("ignored"),
                Is.False);
            Assert.That(
                GameDiagnosticsSnapshot.TryOpenLogDirectory(),
                Is.False);
            Assert.That(File.Exists(logPath), Is.False);
        }

        private static string[] ReadLiveLines(string path)
        {
            using (FileStream stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite))
            using (StreamReader reader = new StreamReader(stream))
            {
                return reader
                    .ReadToEnd()
                    .Split(
                        new[] { '\n' },
                        StringSplitOptions.RemoveEmptyEntries);
            }
        }
    }
}
