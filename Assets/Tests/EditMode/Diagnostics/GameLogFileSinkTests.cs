using System;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class GameLogFileSinkTests
    {
        private string directory;
        private string logPath;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(
                Path.GetTempPath(),
                "BarPromenade-GameLogSinkTests-" +
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            logPath = Path.Combine(directory, "debug.log");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void Sink_WritesUtf8WithoutBom_AndAllowsLiveReading()
        {
            GameLogSettings settings = new GameLogSettings(
                GameLogProfile.Verbose,
                logPath);
            using (GameLogFileSink sink =
                   new GameLogFileSink(settings, false))
            {
                sink.Write("{\"message\":\"Бар\"}", true);

                using (FileStream reader = new FileStream(
                           logPath,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.ReadWrite))
                {
                    byte[] bytes = new byte[reader.Length];
                    int read = reader.Read(bytes, 0, bytes.Length);
                    string text = Encoding.UTF8.GetString(
                        bytes,
                        0,
                        read);

                    Assert.That(read, Is.GreaterThan(0));
                    Assert.That(bytes[0], Is.EqualTo((byte)'{'));
                    Assert.That(
                        text,
                        Is.EqualTo("{\"message\":\"Бар\"}\n"));
                }
            }
        }

        [Test]
        public void Sink_RotatesAtSizeAndRetainsOnlyThreeArchives()
        {
            GameLogSettings settings = new GameLogSettings(
                GameLogProfile.Verbose,
                logPath,
                12L,
                3);
            using (GameLogFileSink sink =
                   new GameLogFileSink(settings, false))
            {
                sink.Write("one", false);
                sink.Write("two", false);
                sink.Write("three", false);
                sink.Write("four", false);
                sink.Write("five", false);
                sink.Write("six", false);
                sink.Write("seven", false);
                sink.Write("eight", false);
                sink.Write("nine", true);
            }

            Assert.That(
                File.ReadAllText(logPath),
                Is.EqualTo("nine\n"));
            Assert.That(
                File.ReadAllText(ArchivePath(1)),
                Is.EqualTo("seven\neight\n"));
            Assert.That(
                File.ReadAllText(ArchivePath(2)),
                Is.EqualTo("five\nsix\n"));
            Assert.That(
                File.ReadAllText(ArchivePath(3)),
                Is.EqualTo("three\nfour\n"));
            Assert.That(File.Exists(ArchivePath(4)), Is.False);

            using (GameLogFileSink ignored =
                   new GameLogFileSink(settings, false))
            {
                Assert.That(
                    File.ReadAllText(ArchivePath(1)),
                    Is.EqualTo("nine\n"));
                Assert.That(
                    File.ReadAllText(ArchivePath(2)),
                    Is.EqualTo("seven\neight\n"));
                Assert.That(
                    File.ReadAllText(ArchivePath(3)),
                    Is.EqualTo("five\nsix\n"));
                Assert.That(File.Exists(ArchivePath(4)), Is.False);
            }
        }

        [Test]
        public void Sink_RotatesWhileTheActiveFileHasALiveReader()
        {
            GameLogSettings settings = new GameLogSettings(
                GameLogProfile.Verbose,
                logPath,
                8L,
                3);
            using (GameLogFileSink sink =
                   new GameLogFileSink(settings, false))
            {
                sink.Write("123456", true);
                using (FileStream reader = new FileStream(
                           logPath,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.ReadWrite))
                {
                    sink.Write("next", true);
                    Assert.That(sink.IsOperational, Is.True);
                }
            }

            Assert.That(
                File.ReadAllText(logPath),
                Is.EqualTo("next\n"));
            Assert.That(
                File.ReadAllText(ArchivePath(1)),
                Is.EqualTo("123456\n"));
        }

        [Test]
        public void Sink_FlushesOnConfiguredInterval()
        {
            GameLogSettings settings = new GameLogSettings(
                GameLogProfile.Verbose,
                logPath,
                GameLogSettings.DefaultMaxFileBytes,
                3,
                0.02f);
            using (GameLogFileSink sink =
                   new GameLogFileSink(settings))
            {
                sink.Write("timer-flush", false);

                bool observed = SpinWait.SpinUntil(
                    () => TryReadContains(logPath, "timer-flush"),
                    TimeSpan.FromSeconds(2d));

                Assert.That(
                    observed,
                    Is.True,
                    "The periodic flush must make buffered lines readable.");
            }
        }

        [Test]
        public void Sink_DisablesItselfWhenPathIsInvalid()
        {
            string invalidPath = logPath + "\0invalid";
            GameLogSettings settings = new GameLogSettings(
                GameLogProfile.Verbose,
                invalidPath);

            Assert.DoesNotThrow(
                () =>
                {
                    using (GameLogFileSink sink =
                           new GameLogFileSink(settings, false))
                    {
                        Assert.That(sink.IsOperational, Is.False);
                        sink.Write("ignored", true);
                        sink.Flush();
                    }
                });
        }

        private string ArchivePath(int index)
        {
            return Path.Combine(
                directory,
                "debug." + index + ".log");
        }

        private static bool TryReadContains(
            string path,
            string expected)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                using (FileStream stream = new FileStream(
                           path,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd().Contains(expected);
                }
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
