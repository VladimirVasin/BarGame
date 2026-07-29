using System;
using System.Globalization;
using System.Text;

namespace BarPromenade
{
    public static class GameLogFormatter
    {
        public const int SchemaVersion = 1;
        public const int MaxStringCharacters = 16384;
        private const string TruncationMarker = "...[truncated]";

        public static string Format(GameLogEvent entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(256);
            builder.Append("{\"schema_version\":");
            builder.Append(
                SchemaVersion.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"utc\":");
            AppendEscapedString(
                builder,
                entry.UtcTimestamp
                    .ToUniversalTime()
                    .ToString(
                        "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                        CultureInfo.InvariantCulture));
            AppendNumberProperty(
                builder,
                "mono_ms",
                entry.MonotonicMilliseconds);
            AppendNumberProperty(builder, "seq", entry.Sequence);
            AppendStringProperty(
                builder,
                "level",
                FormatLevel(entry.Level));
            AppendStringProperty(builder, "category", entry.Category);
            AppendStringProperty(builder, "event", entry.EventName);
            AppendStringProperty(builder, "session_id", entry.SessionId);
            AppendStringProperty(builder, "scene", entry.SceneName);
            if (entry.CitySeed.HasValue)
            {
                AppendNumberProperty(
                    builder,
                    "city_seed",
                    entry.CitySeed.Value);
            }

            builder.Append(",\"data\":{");
            for (int index = 0; index < entry.Fields.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                GameLogField field = entry.Fields[index];
                AppendEscapedString(builder, field.Name);
                builder.Append(':');
                AppendFieldValue(builder, field);
            }

            builder.Append("}}");
            return builder.ToString();
        }

        private static void AppendFieldValue(
            StringBuilder builder,
            GameLogField field)
        {
            switch (field.Type)
            {
                case GameLogFieldType.String:
                    if (field.StringValue == null)
                    {
                        builder.Append("null");
                    }
                    else
                    {
                        AppendEscapedString(builder, field.StringValue);
                    }

                    break;
                case GameLogFieldType.Int32:
                case GameLogFieldType.Int64:
                    builder.Append(
                        field.IntegerValue.ToString(
                            CultureInfo.InvariantCulture));
                    break;
                case GameLogFieldType.Single:
                    float single = (float)field.NumberValue;
                    if (float.IsNaN(single) ||
                        float.IsInfinity(single))
                    {
                        builder.Append("null");
                    }
                    else
                    {
                        builder.Append(
                            single.ToString(
                                "R",
                                CultureInfo.InvariantCulture));
                    }

                    break;
                case GameLogFieldType.Double:
                    if (double.IsNaN(field.NumberValue) ||
                        double.IsInfinity(field.NumberValue))
                    {
                        builder.Append("null");
                    }
                    else
                    {
                        builder.Append(
                            field.NumberValue.ToString(
                                "R",
                                CultureInfo.InvariantCulture));
                    }

                    break;
                case GameLogFieldType.Boolean:
                    builder.Append(
                        field.BooleanValue ? "true" : "false");
                    break;
                default:
                    builder.Append("null");
                    break;
            }
        }

        private static void AppendStringProperty(
            StringBuilder builder,
            string name,
            string value)
        {
            builder.Append(",\"");
            builder.Append(name);
            builder.Append("\":");
            AppendEscapedString(builder, value ?? string.Empty);
        }

        private static void AppendNumberProperty(
            StringBuilder builder,
            string name,
            long value)
        {
            builder.Append(",\"");
            builder.Append(name);
            builder.Append("\":");
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendEscapedString(
            StringBuilder builder,
            string value)
        {
            builder.Append('"');
            if (value != null)
            {
                int characterCount = Math.Min(
                    value.Length,
                    MaxStringCharacters);
                for (int index = 0;
                     index < characterCount;
                     index++)
                {
                    char character = value[index];
                    switch (character)
                    {
                        case '"':
                            builder.Append("\\\"");
                            break;
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '\b':
                            builder.Append("\\b");
                            break;
                        case '\f':
                            builder.Append("\\f");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        default:
                            if (character < ' ' ||
                                character == '\u2028' ||
                                character == '\u2029' ||
                                char.IsSurrogate(character))
                            {
                                builder.Append("\\u");
                                builder.Append(
                                    ((int)character).ToString(
                                        "x4",
                                        CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                builder.Append(character);
                            }

                            break;
                    }
                }

                if (characterCount < value.Length)
                {
                    builder.Append(TruncationMarker);
                }
            }

            builder.Append('"');
        }

        private static string FormatLevel(GameLogLevel level)
        {
            switch (level)
            {
                case GameLogLevel.Debug:
                    return "debug";
                case GameLogLevel.Info:
                    return "info";
                case GameLogLevel.Warning:
                    return "warning";
                case GameLogLevel.Error:
                    return "error";
                case GameLogLevel.Fatal:
                    return "fatal";
                default:
                    return "info";
            }
        }
    }
}
