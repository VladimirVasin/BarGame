using System;
using System.Globalization;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class GameLogFormatterTests
    {
        [Test]
        public void Format_ProducesStableInvariantNdjson()
        {
            CultureInfo previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture =
                    CultureInfo.GetCultureInfo("ru-RU");
                GameLogEvent entry = new GameLogEvent(
                    new DateTimeOffset(
                        2026,
                        7,
                        29,
                        18,
                        24,
                        17,
                        483,
                        TimeSpan.Zero),
                    8123L,
                    7L,
                    GameLogLevel.Info,
                    "city",
                    "layout.ready",
                    "session-a",
                    "City",
                    -481516,
                    new[]
                    {
                        GameLog.Field(
                            "text",
                            "Бар \"A\"\nстрока"),
                        GameLog.Field("int", 12),
                        GameLog.Field(
                            "long",
                            9007199254740991L),
                        GameLog.Field("float", 1.25f),
                        GameLog.Field("double", 2.5d),
                        GameLog.Field("bool", true)
                    });

                string line = GameLogFormatter.Format(entry);

                Assert.That(
                    line,
                    Is.EqualTo(
                        "{\"schema_version\":1," +
                        "\"utc\":\"2026-07-29T18:24:17.483Z\"," +
                        "\"mono_ms\":8123,\"seq\":7,\"level\":\"info\"," +
                        "\"category\":\"city\",\"event\":\"layout.ready\"," +
                        "\"session_id\":\"session-a\",\"scene\":\"City\"," +
                        "\"city_seed\":-481516,\"data\":{" +
                        "\"text\":\"Бар \\\"A\\\"\\nстрока\"," +
                        "\"int\":12,\"long\":9007199254740991," +
                        "\"float\":1.25,\"double\":2.5,\"bool\":true}}"));
                Assert.That(line, Does.Not.Contain("\n"));
                Assert.That(line, Does.Not.Contain("\r"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        [Test]
        public void Format_UsesNullForMissingOrNonFiniteValues()
        {
            GameLogEvent entry = new GameLogEvent(
                DateTimeOffset.UnixEpoch,
                0L,
                1L,
                GameLogLevel.Warning,
                null,
                null,
                null,
                null,
                null,
                new[]
                {
                    GameLog.Field("missing", (string)null),
                    GameLog.Field("nan", float.NaN),
                    GameLog.Field(
                        "infinite",
                        double.PositiveInfinity)
                });

            string line = GameLogFormatter.Format(entry);

            Assert.That(
                line,
                Does.EndWith(
                    "\"data\":{\"missing\":null,\"nan\":null," +
                    "\"infinite\":null}}"));
            Assert.That(line, Does.Not.Contain("NaN"));
            Assert.That(line, Does.Not.Contain("Infinity"));
            Assert.That(
                line,
                Does.StartWith("{\"schema_version\":1,"));
        }

        [Test]
        public void Event_TakesAnImmutableFieldSnapshot()
        {
            GameLogField[] source =
            {
                GameLog.Field("value", 4)
            };
            GameLogEvent entry = new GameLogEvent(
                DateTimeOffset.UnixEpoch,
                0L,
                1L,
                GameLogLevel.Info,
                "test",
                "snapshot",
                "session",
                "City",
                null,
                source);

            source[0] = GameLog.Field("value", 99);

            Assert.That(entry.Fields.Count, Is.EqualTo(1));
            Assert.That(
                entry.Fields[0].IntegerValue,
                Is.EqualTo(4L));
        }

        [Test]
        public void Format_TruncatesOversizedStringsInsideOneJsonLine()
        {
            string oversized =
                new string('x', GameLogFormatter.MaxStringCharacters) +
                "tail-that-must-not-be-written";
            GameLogEvent entry = new GameLogEvent(
                DateTimeOffset.UnixEpoch,
                0L,
                1L,
                GameLogLevel.Error,
                "unity",
                "exception",
                "session",
                "City",
                null,
                new[]
                {
                    GameLog.Field("stack_trace", oversized)
                });

            string line = GameLogFormatter.Format(entry);

            Assert.That(line, Does.Contain("...[truncated]"));
            Assert.That(
                line,
                Does.Not.Contain("tail-that-must-not-be-written"));
            Assert.That(line, Does.Not.Contain("\n"));
            Assert.That(line, Does.EndWith("\"}}"));
        }
    }
}
