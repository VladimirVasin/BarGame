using System;
using System.Collections.Generic;
using System.Globalization;

namespace BarPromenade
{
    public static class GameLogFormatter
    {
        public const int SchemaVersion = 1;
        public const int MaxStringCharacters = 16384;
        private const string TruncationMarker = "...[truncated]";
        private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

        public static string Format(GameLogEvent entry)
        {
            if (entry == null) return string.Empty;
            var buffer = new Buffer(256);
            TryFormat(buffer, entry.UtcTimestamp, entry.MonotonicMilliseconds, entry.Sequence,
                entry.Level, entry.Category, entry.EventName, entry.SessionId, entry.SceneName,
                entry.CitySeed, entry.Fields);
            return new string(buffer.Characters, 0, buffer.Count);
        }

        /// <summary>
        /// Formats into caller-owned storage without constructing an event, field
        /// snapshot or result string. A buffer belongs to one consumer/thread.
        /// False means the complete record did not fit; never write its prefix.
        /// </summary>
        internal static bool TryFormat(Buffer buffer, DateTimeOffset utcTimestamp, long monotonicMilliseconds,
            long sequence, GameLogLevel level, string category, string eventName, string sessionId,
            string sceneName, int? citySeed, IReadOnlyList<GameLogField> fields)
        {
            buffer.Reset();
            buffer.Append("{\"schema_version\":"); buffer.Append((long)SchemaVersion);
            buffer.Append(",\"utc\":\""); buffer.AppendUtc(utcTimestamp); buffer.Append('"');
            AppendNumberProperty(buffer, "mono_ms", monotonicMilliseconds);
            AppendNumberProperty(buffer, "seq", sequence);
            AppendStringProperty(buffer, "level", FormatLevel(level));
            AppendStringProperty(buffer, "category", category);
            AppendStringProperty(buffer, "event", eventName);
            AppendStringProperty(buffer, "session_id", sessionId);
            AppendStringProperty(buffer, "scene", sceneName);
            if (citySeed.HasValue) AppendNumberProperty(buffer, "city_seed", citySeed.Value);
            buffer.Append(",\"data\":{");
            if (fields != null)
                for (int index = 0; index < fields.Count && !buffer.Overflowed; index++)
                {
                    if (index > 0) buffer.Append(',');
                    GameLogField field = fields[index];
                    AppendEscapedString(buffer, field.Name); buffer.Append(':');
                    AppendFieldValue(buffer, field);
                }
            buffer.Append("}}");
            return !buffer.Overflowed;
        }

        /// <summary>Reusable bounded characters and numeric scratch; growth is cold, never per record.</summary>
        internal sealed class Buffer
        {
            private readonly int maximumCharacters;
            private readonly char[] scratch = new char[64];
            internal char[] Characters { get; private set; }
            internal int Count { get; private set; }
            internal bool Overflowed { get; private set; }

            internal Buffer(int initialCapacity, int maximumCharacters = int.MaxValue)
            {
                if (initialCapacity < 0 || maximumCharacters < initialCapacity)
                    throw new ArgumentOutOfRangeException(nameof(initialCapacity));
                Characters = new char[initialCapacity];
                this.maximumCharacters = maximumCharacters;
            }

            internal void Reset() { Count = 0; Overflowed = false; }
            internal void Append(char value)
            { if (Reserve(1)) Characters[Count++] = value; }
            internal void Append(string value) => Append(value.AsSpan());
            private void Append(ReadOnlySpan<char> value)
            {
                if (!Reserve(value.Length)) return;
                value.CopyTo(Characters.AsSpan(Count)); Count += value.Length;
            }

            internal void Append(long value)
            {
                value.TryFormat(scratch.AsSpan(), out int length, default, CultureInfo.InvariantCulture);
                Append(scratch.AsSpan(0, length));
            }
            internal void Append(float value)
            {
                value.TryFormat(scratch.AsSpan(), out int length, "R".AsSpan(), CultureInfo.InvariantCulture);
                Append(scratch.AsSpan(0, length));
            }
            internal void Append(double value)
            {
                value.TryFormat(scratch.AsSpan(), out int length, "R".AsSpan(), CultureInfo.InvariantCulture);
                Append(scratch.AsSpan(0, length));
            }
            internal void AppendUtc(DateTimeOffset value)
            {
                value.ToUniversalTime().TryFormat(scratch.AsSpan(), out int length,
                    UtcFormat.AsSpan(), CultureInfo.InvariantCulture);
                Append(scratch.AsSpan(0, length));
            }
            private bool Reserve(int additional)
            {
                if (Overflowed) return false;
                if (additional > maximumCharacters - Count) { Overflowed = true; return false; }
                int needed = Count + additional;
                if (needed <= Characters.Length) return true;
                int grown = (int)Math.Min(maximumCharacters, Math.Max((long)needed, Math.Max(256L, Characters.Length * 2L)));
                var replacement = new char[grown];
                Array.Copy(Characters, replacement, Count); Characters = replacement;
                return true;
            }
        }

        private static void AppendFieldValue(Buffer buffer, GameLogField field)
        {
            switch (field.Type)
            {
                case GameLogFieldType.String:
                    if (field.StringValue == null) buffer.Append("null");
                    else AppendEscapedString(buffer, field.StringValue);
                    break;
                case GameLogFieldType.Int32:
                case GameLogFieldType.Int64:
                    buffer.Append(field.IntegerValue); break;
                case GameLogFieldType.Single:
                    float single = (float)field.NumberValue;
                    if (float.IsNaN(single) || float.IsInfinity(single)) buffer.Append("null");
                    else buffer.Append(single);
                    break;
                case GameLogFieldType.Double:
                    if (double.IsNaN(field.NumberValue) || double.IsInfinity(field.NumberValue)) buffer.Append("null");
                    else buffer.Append(field.NumberValue);
                    break;
                case GameLogFieldType.Boolean:
                    buffer.Append(field.BooleanValue ? "true" : "false"); break;
                default:
                    buffer.Append("null"); break;
            }
        }

        private static void AppendStringProperty(Buffer buffer, string name, string value)
        {
            buffer.Append(",\""); buffer.Append(name); buffer.Append("\":");
            AppendEscapedString(buffer, value);
        }

        private static void AppendNumberProperty(Buffer buffer, string name, long value)
        {
            buffer.Append(",\""); buffer.Append(name); buffer.Append("\":"); buffer.Append(value);
        }

        private static void AppendEscapedString(Buffer buffer, string value)
        {
            buffer.Append('"');
            if (value != null)
            {
                int characterCount = Math.Min(value.Length, MaxStringCharacters);
                for (int index = 0; index < characterCount && !buffer.Overflowed; index++)
                {
                    char character = value[index];
                    switch (character)
                    {
                        case '"': buffer.Append("\\\""); break;
                        case '\\': buffer.Append("\\\\"); break;
                        case '\b': buffer.Append("\\b"); break;
                        case '\f': buffer.Append("\\f"); break;
                        case '\n': buffer.Append("\\n"); break;
                        case '\r': buffer.Append("\\r"); break;
                        case '\t': buffer.Append("\\t"); break;
                        default:
                            if (character < ' ' || character == '\u2028' || character == '\u2029' || char.IsSurrogate(character))
                            {
                                const string hex = "0123456789abcdef";
                                buffer.Append("\\u");
                                buffer.Append(hex[(character >> 12) & 15]); buffer.Append(hex[(character >> 8) & 15]);
                                buffer.Append(hex[(character >> 4) & 15]); buffer.Append(hex[character & 15]);
                            }
                            else buffer.Append(character);
                            break;
                    }
                }
                if (characterCount < value.Length) buffer.Append(TruncationMarker);
            }
            buffer.Append('"');
        }

        private static string FormatLevel(GameLogLevel level) => level switch
        {
            GameLogLevel.Debug => "debug", GameLogLevel.Info => "info", GameLogLevel.Warning => "warning",
            GameLogLevel.Error => "error", GameLogLevel.Fatal => "fatal", _ => "info"
        };
    }
}
