using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BarPromenade
{
    public enum GameLogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Fatal
    }

    public enum GameLogProfile
    {
        Off,
        Basic,
        Verbose
    }

    public enum GameLogFieldType
    {
        String,
        Int32,
        Int64,
        Single,
        Double,
        Boolean
    }

    public readonly struct GameLogField
    {
        private GameLogField(
            string name,
            GameLogFieldType type,
            string stringValue,
            long integerValue,
            double numberValue,
            bool booleanValue)
        {
            Name = name ?? string.Empty;
            Type = type;
            StringValue = stringValue;
            IntegerValue = integerValue;
            NumberValue = numberValue;
            BooleanValue = booleanValue;
        }

        public string Name { get; }
        public GameLogFieldType Type { get; }
        public string StringValue { get; }
        public long IntegerValue { get; }
        public double NumberValue { get; }
        public bool BooleanValue { get; }

        internal static GameLogField FromString(string name, string value)
        {
            return new GameLogField(
                name,
                GameLogFieldType.String,
                value,
                0L,
                0d,
                false);
        }

        internal static GameLogField FromInt32(string name, int value)
        {
            return new GameLogField(
                name,
                GameLogFieldType.Int32,
                null,
                value,
                0d,
                false);
        }

        internal static GameLogField FromInt64(string name, long value)
        {
            return new GameLogField(
                name,
                GameLogFieldType.Int64,
                null,
                value,
                0d,
                false);
        }

        internal static GameLogField FromSingle(string name, float value)
        {
            return new GameLogField(
                name,
                GameLogFieldType.Single,
                null,
                0L,
                value,
                false);
        }

        internal static GameLogField FromDouble(string name, double value)
        {
            return new GameLogField(
                name,
                GameLogFieldType.Double,
                null,
                0L,
                value,
                false);
        }

        internal static GameLogField FromBoolean(string name, bool value)
        {
            return new GameLogField(
                name,
                GameLogFieldType.Boolean,
                null,
                0L,
                0d,
                value);
        }
    }

    public sealed class GameLogEvent
    {
        private readonly ReadOnlyCollection<GameLogField> fields;

        internal GameLogEvent(
            DateTimeOffset utcTimestamp,
            long monotonicMilliseconds,
            long sequence,
            GameLogLevel level,
            string category,
            string eventName,
            string sessionId,
            string sceneName,
            int? citySeed,
            GameLogField[] eventFields)
        {
            UtcTimestamp = utcTimestamp;
            MonotonicMilliseconds = monotonicMilliseconds;
            Sequence = sequence;
            Level = level;
            Category = category ?? string.Empty;
            EventName = eventName ?? string.Empty;
            SessionId = sessionId ?? string.Empty;
            SceneName = sceneName ?? string.Empty;
            CitySeed = citySeed;

            GameLogField[] snapshot = eventFields == null
                ? Array.Empty<GameLogField>()
                : (GameLogField[])eventFields.Clone();
            fields = Array.AsReadOnly(snapshot);
        }

        public DateTimeOffset UtcTimestamp { get; }
        public long MonotonicMilliseconds { get; }
        public long Sequence { get; }
        public GameLogLevel Level { get; }
        public string Category { get; }
        public string EventName { get; }
        public string SessionId { get; }
        public string SceneName { get; }
        public int? CitySeed { get; }
        public IReadOnlyList<GameLogField> Fields => fields;
    }
}
