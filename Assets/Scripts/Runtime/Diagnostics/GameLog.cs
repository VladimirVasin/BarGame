using System;
using System.IO;

namespace BarPromenade
{
    public static class GameLog
    {
        private static readonly object Sync = new object();

        private static GameLogProfile profile = GameLogProfile.Off;
        private static GameLogFileSink sink;
        private static IGameLogClock clock;
        private static string sessionId = string.Empty;
        private static string sceneName = string.Empty;
        private static string currentFilePath = string.Empty;
        private static string currentDirectoryPath = string.Empty;
        private static int? citySeed;
        private static long sequence;

        public static bool IsVerbose
        {
            get
            {
                lock (Sync)
                {
                    return profile == GameLogProfile.Verbose &&
                           sink != null &&
                           sink.IsOperational;
                }
            }
        }

        public static string CurrentFilePath
        {
            get
            {
                lock (Sync)
                {
                    return IsSinkAvailableNoLock()
                        ? currentFilePath
                        : string.Empty;
                }
            }
        }

        public static string CurrentDirectoryPath
        {
            get
            {
                lock (Sync)
                {
                    return IsSinkAvailableNoLock()
                        ? currentDirectoryPath
                        : string.Empty;
                }
            }
        }

        public static GameLogField Field(string name, string value)
        {
            return GameLogField.FromString(name, value);
        }

        public static GameLogField Field(string name, int value)
        {
            return GameLogField.FromInt32(name, value);
        }

        public static GameLogField Field(string name, long value)
        {
            return GameLogField.FromInt64(name, value);
        }

        public static GameLogField Field(string name, float value)
        {
            return GameLogField.FromSingle(name, value);
        }

        public static GameLogField Field(string name, double value)
        {
            return GameLogField.FromDouble(name, value);
        }

        public static GameLogField Field(string name, bool value)
        {
            return GameLogField.FromBoolean(name, value);
        }

        public static void Debug(
            string category,
            string eventName,
            params GameLogField[] fields)
        {
            Write(GameLogLevel.Debug, category, eventName, fields);
        }

        public static void Info(
            string category,
            string eventName,
            params GameLogField[] fields)
        {
            Write(GameLogLevel.Info, category, eventName, fields);
        }

        public static void Warning(
            string category,
            string eventName,
            params GameLogField[] fields)
        {
            Write(GameLogLevel.Warning, category, eventName, fields);
        }

        public static void Error(
            string category,
            string eventName,
            params GameLogField[] fields)
        {
            Write(GameLogLevel.Error, category, eventName, fields);
        }

        public static void Fatal(
            string category,
            string eventName,
            params GameLogField[] fields)
        {
            Write(GameLogLevel.Fatal, category, eventName, fields);
        }

        public static void SetCitySeed(int seed)
        {
            lock (Sync)
            {
                citySeed = seed;
            }
        }

        public static void Flush()
        {
            lock (Sync)
            {
                try
                {
                    sink?.Flush();
                    DisableIfFaultedNoLock();
                }
                catch
                {
                    DisableNoLock();
                }
            }
        }

        internal static GameLogProfile Profile
        {
            get
            {
                lock (Sync)
                {
                    return profile;
                }
            }
        }

        internal static void Initialize(
            GameLogSettings settings,
            IGameLogClock eventClock,
            string newSessionId,
            bool startFlushTimer = true)
        {
            lock (Sync)
            {
                DisableNoLock();
                profile = settings?.Profile ?? GameLogProfile.Off;
                clock = eventClock ?? new SystemGameLogClock();
                sessionId = newSessionId ?? string.Empty;
                sceneName = string.Empty;
                citySeed = null;
                sequence = 0L;

                if (profile == GameLogProfile.Off)
                {
                    return;
                }

                try
                {
                    sink = new GameLogFileSink(
                        settings,
                        startFlushTimer);
                    DisableIfFaultedNoLock();
                    if (sink != null)
                    {
                        string fullPath = Path.GetFullPath(
                            settings.FilePath);
                        currentFilePath = fullPath;
                        currentDirectoryPath =
                            Path.GetDirectoryName(fullPath) ??
                            string.Empty;
                    }
                }
                catch
                {
                    DisableNoLock();
                }
            }
        }

        internal static void SetScene(string value)
        {
            lock (Sync)
            {
                sceneName = value ?? string.Empty;
            }
        }

        internal static void Shutdown(
            string reason,
            bool emitSessionEnd)
        {
            lock (Sync)
            {
                if (emitSessionEnd &&
                    IsEnabledNoLock(GameLogLevel.Info))
                {
                    WriteNoLock(
                        GameLogLevel.Info,
                        "session",
                        "end",
                        new[]
                        {
                            Field("reason", reason ?? string.Empty)
                        });
                }

                DisableNoLock();
            }
        }

        internal static void Write(
            GameLogLevel level,
            string category,
            string eventName,
            GameLogField[] fields)
        {
            lock (Sync)
            {
                WriteNoLock(level, category, eventName, fields);
            }
        }

        private static void WriteNoLock(
            GameLogLevel level,
            string category,
            string eventName,
            GameLogField[] fields)
        {
            if (!IsEnabledNoLock(level))
            {
                return;
            }

            try
            {
                long nextSequence = sequence + 1L;
                GameLogEvent entry = new GameLogEvent(
                    clock.UtcNow,
                    Math.Max(0L, clock.ElapsedMilliseconds),
                    nextSequence,
                    level,
                    category,
                    eventName,
                    sessionId,
                    sceneName,
                    citySeed,
                    fields);
                string line = GameLogFormatter.Format(entry);
                sink.Write(
                    line,
                    level == GameLogLevel.Error ||
                    level == GameLogLevel.Fatal);
                if (sink.IsOperational)
                {
                    sequence = nextSequence;
                }
                else
                {
                    DisableNoLock();
                }
            }
            catch
            {
                DisableNoLock();
            }
        }

        private static bool IsEnabledNoLock(GameLogLevel level)
        {
            if (!IsSinkAvailableNoLock())
            {
                return false;
            }

            return level != GameLogLevel.Debug ||
                   profile == GameLogProfile.Verbose;
        }

        private static bool IsSinkAvailableNoLock()
        {
            return profile != GameLogProfile.Off &&
                   sink != null &&
                   sink.IsOperational;
        }

        private static void DisableIfFaultedNoLock()
        {
            if (sink != null && sink.IsOperational)
            {
                return;
            }

            DisableNoLock();
        }

        private static void DisableNoLock()
        {
            try
            {
                sink?.Dispose();
            }
            catch
            {
                // Diagnostics cannot be allowed to break the game.
            }

            sink = null;
            profile = GameLogProfile.Off;
            currentFilePath = string.Empty;
            currentDirectoryPath = string.Empty;

            //  Cleared here too, because `GameLogRuntime.ResetStatics`
            //  routes every play-mode entry through this method and domain
            //  reload no longer clears statics for us. Without this the
            //  second run of a session continues the first one's sequence
            //  under the first one's session id. The session-end record is
            //  written before this runs, so it still carries them.
            sessionId = string.Empty;
            sceneName = string.Empty;
            citySeed = null;
            sequence = 0;
        }
    }
}
