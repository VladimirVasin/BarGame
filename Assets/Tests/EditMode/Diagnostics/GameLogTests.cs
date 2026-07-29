using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class GameLogTests
    {
        private string directory;
        private string logPath;
        private FakeClock clock;

        [SetUp]
        public void SetUp()
        {
            GameLog.Shutdown("test_setup", false);
            directory = Path.Combine(
                Path.GetTempPath(),
                "BarPromenade-GameLogFacadeTests-" +
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            logPath = Path.Combine(directory, "debug.log");
            clock = new FakeClock(
                new DateTimeOffset(
                    2026,
                    7,
                    29,
                    18,
                    0,
                    0,
                    TimeSpan.Zero),
                42L);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.Shutdown("test_teardown", false);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void BasicProfile_FiltersDebugAndFlushesErrorsImmediately()
        {
            GameLog.Initialize(
                new GameLogSettings(
                    GameLogProfile.Basic,
                    logPath),
                clock,
                "session-test",
                false);
            GameLog.SetScene("City");
            GameLog.SetCitySeed(7788);

            Assert.That(
                GameLog.CurrentFilePath,
                Is.EqualTo(Path.GetFullPath(logPath)));
            Assert.That(
                GameLog.CurrentDirectoryPath,
                Is.EqualTo(Path.GetFullPath(directory)));

            GameLog.Debug(
                "navigation",
                "sample",
                GameLog.Field("ignored", true));
            GameLog.Info(
                "city",
                "layout.ready",
                GameLog.Field("blocks", 144));
            clock.Advance(5L);
            GameLog.Error(
                "transition",
                "failed",
                GameLog.Field("reason", "missing scene"));

            string[] lines = ReadLiveLines(logPath);
            Assert.That(GameLog.IsVerbose, Is.False);
            Assert.That(lines.Length, Is.EqualTo(2));
            Assert.That(lines[0], Does.Contain("\"seq\":1"));
            Assert.That(lines[0], Does.Contain("\"mono_ms\":42"));
            Assert.That(lines[0], Does.Contain("\"scene\":\"City\""));
            Assert.That(lines[0], Does.Contain("\"city_seed\":7788"));
            Assert.That(lines[0], Does.Contain("\"blocks\":144"));
            Assert.That(lines[1], Does.Contain("\"seq\":2"));
            Assert.That(lines[1], Does.Contain("\"mono_ms\":47"));
            Assert.That(lines[1], Does.Contain("\"level\":\"error\""));
        }

        [Test]
        public void VerboseProfile_ExposesDebugAndSessionEnd()
        {
            GameLog.Initialize(
                new GameLogSettings(
                    GameLogProfile.Verbose,
                    logPath),
                clock,
                "verbose-session",
                false);

            Assert.That(GameLog.IsVerbose, Is.True);
            GameLog.Debug("map", "opened");
            GameLog.Shutdown("test_complete", true);

            string[] lines = File.ReadAllLines(logPath);
            Assert.That(GameLog.CurrentFilePath, Is.Empty);
            Assert.That(GameLog.CurrentDirectoryPath, Is.Empty);
            Assert.That(lines.Length, Is.EqualTo(2));
            Assert.That(
                lines[0],
                Does.Contain("\"level\":\"debug\""));
            Assert.That(
                lines[0],
                Does.Contain("\"event\":\"opened\""));
            Assert.That(
                lines[1],
                Does.Contain("\"category\":\"session\""));
            Assert.That(
                lines[1],
                Does.Contain("\"event\":\"end\""));
            Assert.That(
                lines[1],
                Does.Contain("\"reason\":\"test_complete\""));
        }

        [Test]
        public void OffProfile_DoesNotCreateAFile()
        {
            GameLog.Initialize(
                new GameLogSettings(
                    GameLogProfile.Off,
                    logPath),
                clock,
                "off-session",
                false);

            Assert.DoesNotThrow(
                () =>
                {
                    GameLog.SetCitySeed(17);
                    GameLog.Info("session", "ignored");
                    GameLog.Flush();
                });
            Assert.That(GameLog.IsVerbose, Is.False);
            Assert.That(GameLog.CurrentFilePath, Is.Empty);
            Assert.That(GameLog.CurrentDirectoryPath, Is.Empty);
            Assert.That(File.Exists(logPath), Is.False);
        }

        [Test]
        public void InvalidSink_FailsClosedWithoutThrowing()
        {
            string invalidPath = logPath + "\0invalid";

            Assert.DoesNotThrow(
                () =>
                {
                    GameLog.Initialize(
                        new GameLogSettings(
                            GameLogProfile.Verbose,
                            invalidPath),
                        clock,
                        "invalid-session",
                        false);
                    GameLog.Info("test", "ignored");
                    GameLog.Flush();
                });
            Assert.That(GameLog.IsVerbose, Is.False);
            Assert.That(GameLog.CurrentFilePath, Is.Empty);
            Assert.That(GameLog.CurrentDirectoryPath, Is.Empty);
        }

        [TestCase(LogType.Log, false)]
        [TestCase(LogType.Warning, true)]
        [TestCase(LogType.Error, true)]
        [TestCase(LogType.Assert, true)]
        [TestCase(LogType.Exception, true)]
        public void UnityLogCapture_RejectsOrdinaryMessages(
            LogType type,
            bool expected)
        {
            Assert.That(
                GameLogRuntime.ShouldCaptureUnityLog(type),
                Is.EqualTo(expected));
        }

        [Test]
        public void SessionStartFields_AddHardwareOnlyInVerboseProfile()
        {
            GameLogField[] basic =
                GameLogRuntime.BuildSessionStartFields(
                    GameLogProfile.Basic,
                    true);
            GameLogField[] verbose =
                GameLogRuntime.BuildSessionStartFields(
                    GameLogProfile.Verbose,
                    true);

            Assert.That(HasField(basic, "build_guid"), Is.True);
            Assert.That(HasField(basic, "quality"), Is.True);
            Assert.That(
                HasField(basic, "resolution_width"),
                Is.True);
            Assert.That(
                HasField(basic, "resolution_height"),
                Is.True);
            Assert.That(
                HasField(basic, "fullscreen_mode"),
                Is.True);
            Assert.That(
                HasField(basic, "operating_system"),
                Is.False);

            Assert.That(
                HasField(verbose, "operating_system"),
                Is.True);
            Assert.That(HasField(verbose, "processor"), Is.True);
            Assert.That(
                HasField(verbose, "processor_count"),
                Is.True);
            Assert.That(
                HasField(verbose, "system_memory_mb"),
                Is.True);
            Assert.That(
                HasField(verbose, "graphics_device"),
                Is.True);
            Assert.That(
                HasField(verbose, "graphics_memory_mb"),
                Is.True);
            Assert.That(
                HasField(verbose, "graphics_api"),
                Is.True);

            AssertNoSensitiveFieldNames(verbose);
        }

        [Test]
        public void PauseAndFocusLoss_FlushBufferedEvents()
        {
            GameLog.Initialize(
                new GameLogSettings(
                    GameLogProfile.Verbose,
                    logPath),
                clock,
                "lifecycle-session",
                false);
            GameLog.Info("lifecycle", "before_pause");

            GameLogRuntime.HandleApplicationPause(false);
            Assert.That(ReadLiveLines(logPath), Is.Empty);

            GameLogRuntime.HandleApplicationPause(true);
            Assert.That(ReadLiveLines(logPath).Length, Is.EqualTo(1));

            GameLog.Info("lifecycle", "before_focus_loss");
            GameLogRuntime.HandleApplicationFocus(true);
            Assert.That(ReadLiveLines(logPath).Length, Is.EqualTo(1));

            GameLogRuntime.HandleApplicationFocus(false);
            string[] lines = ReadLiveLines(logPath);
            Assert.That(lines.Length, Is.EqualTo(2));
            Assert.That(
                lines[1],
                Does.Contain("\"event\":\"before_focus_loss\""));
        }

        [TestCase(
            new[] { "-bp-debug-log", "off" },
            GameLogProfile.Verbose,
            GameLogProfile.Off)]
        [TestCase(
            new[] { "-bp-debug-log=basic" },
            GameLogProfile.Verbose,
            GameLogProfile.Basic)]
        [TestCase(
            new[] { "-BP-DEBUG-LOG", "VERBOSE" },
            GameLogProfile.Off,
            GameLogProfile.Verbose)]
        [TestCase(
            new[] { "-bp-debug-log", "unknown" },
            GameLogProfile.Basic,
            GameLogProfile.Basic)]
        public void CommandLineProfileOverride_IsDeterministic(
            string[] arguments,
            GameLogProfile fallback,
            GameLogProfile expected)
        {
            Assert.That(
                GameLogSettings.ResolveProfile(arguments, fallback),
                Is.EqualTo(expected));
        }

        [Test]
        public void RuntimeSettings_DefaultTestsToOff_ButAllowOverride()
        {
            string assetsPath = Path.Combine(directory, "Assets");
            string persistentPath =
                Path.Combine(directory, "Persistent");

            GameLogSettings batch = GameLogSettings.CreateRuntime(
                Array.Empty<string>(),
                true,
                true,
                true,
                assetsPath,
                persistentPath);
            GameLogSettings testRun = GameLogSettings.CreateRuntime(
                new[] { "-runTests" },
                false,
                true,
                true,
                assetsPath,
                persistentPath);
            GameLogSettings overridden = GameLogSettings.CreateRuntime(
                new[]
                {
                    "-runTests",
                    "-bp-debug-log",
                    "verbose"
                },
                true,
                true,
                true,
                assetsPath,
                persistentPath);

            Assert.That(batch.Profile, Is.EqualTo(GameLogProfile.Off));
            Assert.That(
                testRun.Profile,
                Is.EqualTo(GameLogProfile.Off));
            Assert.That(
                overridden.Profile,
                Is.EqualTo(GameLogProfile.Verbose));
            Assert.That(
                batch.FilePath,
                Is.EqualTo(
                    Path.GetFullPath(
                        Path.Combine(assetsPath, "..", "debug.log"))));
        }

        private sealed class FakeClock : IGameLogClock
        {
            internal FakeClock(
                DateTimeOffset utcNow,
                long elapsedMilliseconds)
            {
                UtcNow = utcNow;
                ElapsedMilliseconds = elapsedMilliseconds;
            }

            public DateTimeOffset UtcNow { get; private set; }
            public long ElapsedMilliseconds { get; private set; }

            internal void Advance(long milliseconds)
            {
                ElapsedMilliseconds += milliseconds;
                UtcNow = UtcNow.AddMilliseconds(milliseconds);
            }
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

        private static bool HasField(
            GameLogField[] fields,
            string expectedName)
        {
            for (int index = 0; index < fields.Length; index++)
            {
                if (string.Equals(
                        fields[index].Name,
                        expectedName,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertNoSensitiveFieldNames(
            GameLogField[] fields)
        {
            for (int index = 0; index < fields.Length; index++)
            {
                string normalized =
                    fields[index].Name.ToLowerInvariant();
                Assert.That(normalized, Does.Not.Contain("path"));
                Assert.That(normalized, Does.Not.Contain("username"));
                Assert.That(normalized, Does.Not.Contain("user_name"));
            }
        }
    }
}
