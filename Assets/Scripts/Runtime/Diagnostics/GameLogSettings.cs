using System;
using System.IO;

namespace BarPromenade
{
    public sealed class GameLogSettings
    {
        public const long DefaultMaxFileBytes = 5L * 1024L * 1024L;
        public const int DefaultRetainedArchives = 3;
        public const float DefaultFlushIntervalSeconds = 0.5f;

        public GameLogSettings(
            GameLogProfile profile,
            string filePath,
            long maxFileBytes = DefaultMaxFileBytes,
            int retainedArchives = DefaultRetainedArchives,
            float flushIntervalSeconds = DefaultFlushIntervalSeconds)
        {
            Profile = profile;
            FilePath = filePath ?? string.Empty;
            MaxFileBytes = Math.Max(1L, maxFileBytes);
            RetainedArchives = Math.Max(0, retainedArchives);
            FlushInterval = TimeSpan.FromSeconds(
                Math.Max(0.01f, flushIntervalSeconds));
        }

        public GameLogProfile Profile { get; }
        public string FilePath { get; }
        public long MaxFileBytes { get; }
        public int RetainedArchives { get; }
        public TimeSpan FlushInterval { get; }

        internal static GameLogSettings CreateRuntime(
            string[] commandLineArguments,
            bool isBatchMode,
            bool isEditor,
            bool isDevelopmentBuild,
            string applicationDataPath,
            string persistentDataPath)
        {
            bool isTestRun = HasArgument(
                commandLineArguments,
                "-runTests") ||
                HasArgument(commandLineArguments, "-testPlatform");
            GameLogProfile fallback = isBatchMode || isTestRun
                ? GameLogProfile.Off
                : isEditor || isDevelopmentBuild
                    ? GameLogProfile.Verbose
                    : GameLogProfile.Basic;
            GameLogProfile profile = ResolveProfile(
                commandLineArguments,
                fallback);

            string path = isEditor
                ? Path.GetFullPath(
                    Path.Combine(applicationDataPath, "..", "debug.log"))
                : Path.Combine(
                    persistentDataPath,
                    "Logs",
                    "debug.log");
            return new GameLogSettings(profile, path);
        }

        internal static GameLogProfile ResolveProfile(
            string[] arguments,
            GameLogProfile fallback)
        {
            if (arguments == null)
            {
                return fallback;
            }

            for (int index = 0; index < arguments.Length; index++)
            {
                string argument = arguments[index] ?? string.Empty;
                string value = null;
                if (string.Equals(
                        argument,
                        "-bp-debug-log",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 < arguments.Length)
                    {
                        value = arguments[index + 1];
                    }
                }
                else
                {
                    const string prefix = "-bp-debug-log=";
                    if (argument.StartsWith(
                            prefix,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        value = argument.Substring(prefix.Length);
                    }
                }

                if (TryParseProfile(value, out GameLogProfile profile))
                {
                    return profile;
                }
            }

            return fallback;
        }

        private static bool TryParseProfile(
            string value,
            out GameLogProfile profile)
        {
            if (string.Equals(
                    value,
                    "off",
                    StringComparison.OrdinalIgnoreCase))
            {
                profile = GameLogProfile.Off;
                return true;
            }

            if (string.Equals(
                    value,
                    "basic",
                    StringComparison.OrdinalIgnoreCase))
            {
                profile = GameLogProfile.Basic;
                return true;
            }

            if (string.Equals(
                    value,
                    "verbose",
                    StringComparison.OrdinalIgnoreCase))
            {
                profile = GameLogProfile.Verbose;
                return true;
            }

            profile = GameLogProfile.Off;
            return false;
        }

        private static bool HasArgument(string[] arguments, string expected)
        {
            if (arguments == null)
            {
                return false;
            }

            for (int index = 0; index < arguments.Length; index++)
            {
                if (string.Equals(
                        arguments[index],
                        expected,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
