using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Profiling;
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
        [Test]
        public void ReusableFormatterPreservesSchemaAndReducesSerializationAllocations()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
                var timestamp = new DateTimeOffset(2026, 9, 26, 14, 5, 6, 789, TimeSpan.FromHours(4));
                var values = new List<GameLogField> {
                    GameLog.Field("round", 2), GameLog.Field("frame", 71), GameLog.Field("tick", 128L),
                    GameLog.Field("duel_seconds", 1.2345678901234567), GameLog.Field("actor", 1),
                    GameLog.Field("name", "root"), GameLog.Field("x", .0123456789f), GameLog.Field("y", -1.25f),
                    GameLog.Field("z", 100000000f), GameLog.Field("qx", 0f), GameLog.Field("qy", .70710677f),
                    GameLog.Field("qz", 0f), GameLog.Field("qw", .70710677f), GameLog.Field("valid", true) };
                var buffer = new GameLogFormatter.Buffer(16384, 16384);
                var utf8 = new UTF8Encoding(false);
                // Validate the schema before any profiler-dependent assertion.
                var measuredValues = new List<GameLogField>(values);
                Assert.That(FormatReusable(buffer, timestamp, values), Is.True);
                string actual = new string(buffer.Characters, 0, buffer.Count);
                Assert.That(actual, Is.EqualTo(SerializeSnapshot(timestamp, values)));
                StringAssert.StartsWith("{\"schema_version\":1,\"utc\":\"2026-09-26T10:05:06.789Z\",\"mono_ms\":8123,\"seq\":7,\"level\":\"info\",", actual);
                StringAssert.Contains("\"x\":" + .0123456789f.ToString("R", CultureInfo.InvariantCulture), actual);

                values.Clear();
                values.Add(GameLog.Field("escaped\"key", "\"\\\b\f\n\r\t\0\u001f\u2028\u2029\ud83d\ude00\ud800Ж"));
                values.Add(GameLog.Field("missing", (string)null));
                values.Add(GameLog.Field("nan", float.NaN));
                values.Add(GameLog.Field("positive", double.PositiveInfinity));
                values.Add(GameLog.Field("negative", float.NegativeInfinity));
                values.Add(GameLog.Field("min", long.MinValue));
                values.Add(GameLog.Field("max", long.MaxValue));
                values.Add(GameLog.Field("tiny", double.Epsilon));
                values.Add(GameLog.Field("huge", double.MaxValue));
                values.Add(GameLog.Field("false", false));
                values.Add(default);
                Assert.That(GameLogFormatter.TryFormat(buffer, timestamp, 0, 1, (GameLogLevel)999,
                    null, null, null, null, int.MinValue, values), Is.True);
                actual = new string(buffer.Characters, 0, buffer.Count);
                string expected = GameLogFormatter.Format(new GameLogEvent(timestamp, 0, 1, (GameLogLevel)999,
                    null, null, null, null, int.MinValue, values.ToArray()));
                Assert.That(actual, Is.EqualTo(expected));
                StringAssert.Contains("\"category\":\"\",\"event\":\"\",\"session_id\":\"\",\"scene\":\"\",\"city_seed\":-2147483648", actual);
                StringAssert.Contains("\"escaped\\\"key\":\"\\\"\\\\\\b\\f\\n\\r\\t\\u0000\\u001f\\u2028\\u2029\\ud83d\\ude00\\ud800Ж\"", actual);
                StringAssert.Contains("\"missing\":null,\"nan\":null,\"positive\":null,\"negative\":null", actual);
                StringAssert.Contains("\"min\":-9223372036854775808,\"max\":9223372036854775807", actual);
                StringAssert.Contains("\"tiny\":" + double.Epsilon.ToString("R", CultureInfo.InvariantCulture), actual);
                StringAssert.Contains("\"huge\":" + double.MaxValue.ToString("R", CultureInfo.InvariantCulture), actual);
                StringAssert.EndsWith("\"false\":false,\"\":null}}", actual);
                Assert.That(actual, Does.Not.Contain("\n").And.Not.Contain("\r"));

                values.Clear();
                values.Add(GameLog.Field("text", new string('x', GameLogFormatter.MaxStringCharacters) + "discarded"));
                Assert.That(FormatReusable(buffer, timestamp, values), Is.False, "A capped record must not expose a successful partial line.");
                var growable = new GameLogFormatter.Buffer(256);
                Assert.That(FormatReusable(growable, timestamp, values), Is.True);
                actual = new string(growable.Characters, 0, growable.Count);
                Assert.That(actual, Is.EqualTo(SerializeSnapshot(timestamp, values)));
                StringAssert.EndsWith("...[truncated]\"}}", actual);
                Assert.That(actual, Does.Not.Contain("discarded"));
                values.Clear();
                values.Add(GameLog.Field("text", "reused after overflow"));
                Assert.That(FormatReusable(buffer, timestamp, values), Is.True);
                Assert.That(new string(buffer.Characters, 0, buffer.Count), Is.EqualTo(SerializeSnapshot(timestamp, values)));

                // Unity Mono can return zero from GetAllocatedBytesForCurrentThread.
                // Use the same current-thread GC.Alloc recorder as Unity's own
                // performance package, and prove it observes a known allocation.
                using var recorder = new ProfilerRecorder(ProfilerCategory.Memory, "GC.Alloc", 1,
                    ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
                    ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                Assert.That(recorder.Valid, Is.True, "GC.Alloc must be available to prove the allocation reduction.");
                for (int i = 0; i < 16; i++)
                {
                    SerializeSnapshot(timestamp, measuredValues);
                    FormatReusable(buffer, timestamp, measuredValues);
                    utf8.GetByteCount(buffer.Characters, 0, buffer.Count);
                }
                recorder.Start();
                GC.KeepAlive(new byte[16]);
                recorder.Stop();
                RecorderSample(recorder);
                recorder.Reset();
                recorder.Start();
                var positiveControl = new byte[16384];
                recorder.Stop();
                var control = RecorderSample(recorder);
                GC.KeepAlive(positiveControl);
                Assert.That(control.Count, Is.EqualTo(1), "The recorder must observe exactly the known array allocation.");

                const int records = 256;
                recorder.Reset(); recorder.Start();
                long previousBytes = 0;
                for (int i = 0; i < records; i++) previousBytes += utf8.GetByteCount(SerializeSnapshot(timestamp, measuredValues));
                recorder.Stop();
                var snapshot = RecorderSample(recorder);
                recorder.Reset(); recorder.Start();
                long reusableBytes = 0;
                bool allFit = true;
                for (int i = 0; i < records; i++)
                {
                    allFit &= FormatReusable(buffer, timestamp, measuredValues);
                    reusableBytes += utf8.GetByteCount(buffer.Characters, 0, buffer.Count);
                }
                recorder.Stop();
                var reusable = RecorderSample(recorder);
                Assert.That(allFit, Is.True);
                Assert.That(reusableBytes, Is.EqualTo(previousBytes));
                Assert.That(snapshot.Count, Is.GreaterThan(0));
                TestContext.Out.WriteLine($"Duel serializer: snapshot={(double)snapshot.Count / records:F3} allocations/record; reusable={(double)reusable.Count / records:F3} allocations/record; control={control.Count} allocation. GC.Alloc sample count measures allocations, not bytes.");
                // Framework TryFormat may still allocate internally on Mono.
                // Require a material reduction, not zero framework allocations.
                Assert.That(reusable.Count, Is.LessThanOrEqualTo(snapshot.Count * .5d), "Reusing record storage must at least halve serialization allocations after warmup.");
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        private static string SerializeSnapshot(DateTimeOffset timestamp, List<GameLogField> values) =>
            GameLogFormatter.Format(new GameLogEvent(timestamp, 8123, 7, GameLogLevel.Info,
                "duel", "pose", "session", "CombatTest", null, values.ToArray()));

        private static bool FormatReusable(GameLogFormatter.Buffer buffer, DateTimeOffset timestamp, List<GameLogField> values) =>
            GameLogFormatter.TryFormat(buffer, timestamp, 8123, 7, GameLogLevel.Info,
                "duel", "pose", "session", "CombatTest", null, values);

        private static ProfilerRecorderSample RecorderSample(ProfilerRecorder recorder) =>
            recorder.Count > 0 ? recorder.GetSample(0) : default;

    }
}
