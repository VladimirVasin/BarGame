using System;
using System.Globalization;

namespace BarPromenade
{
    /// <summary>Explicit opt-in; these limits also bound memory and wait time.</summary>
    public sealed class PerformanceCaptureOptions
    {
        public const int MaximumSamples = 36000;
        public const double MaximumWaitSeconds = 300;
        public string Scene { get; }
        public string Label { get; }
        public double WarmupSeconds { get; }
        public double DurationSeconds { get; }
        public double TargetFramesPerSecond { get; }
        public int RequestedRenderWidth { get; }
        public int RequestedRenderHeight { get; }

        public PerformanceCaptureOptions(string scene, string label = "manual",
            double warmupSeconds = 5, double durationSeconds = 30,
            double targetFramesPerSecond = 60, int requestedRenderWidth = 0, int requestedRenderHeight = 0)
        {
            if (string.IsNullOrWhiteSpace(scene) || scene.Length > 64)
                throw new ArgumentException("A scene name of 1-64 characters is required.", nameof(scene));
            if (label == null || label.Length > 64)
                throw new ArgumentException("The capture label is limited to 64 characters.", nameof(label));
            ValidateRange(warmupSeconds, 0, 60, nameof(warmupSeconds));
            ValidateRange(durationSeconds, 1, 120, nameof(durationSeconds));
            ValidateRange(targetFramesPerSecond, 1, 360, nameof(targetFramesPerSecond));
            if ((requestedRenderWidth == 0) != (requestedRenderHeight == 0) ||
                requestedRenderWidth < 0 || requestedRenderHeight < 0 ||
                requestedRenderWidth > 16384 || requestedRenderHeight > 16384)
                throw new ArgumentOutOfRangeException(nameof(requestedRenderWidth),
                    "Request both render dimensions in 1-16384, or leave both unspecified.");
            Scene = scene;
            Label = label;
            WarmupSeconds = warmupSeconds;
            DurationSeconds = durationSeconds;
            TargetFramesPerSecond = targetFramesPerSecond;
            RequestedRenderWidth = requestedRenderWidth;
            RequestedRenderHeight = requestedRenderHeight;
        }

        public bool MatchesRenderResolution(int actualWidth, int actualHeight) =>
            actualWidth > 0 && actualHeight > 0 && (RequestedRenderWidth == 0 ||
                RequestedRenderWidth == actualWidth && RequestedRenderHeight == actualHeight);

        public static PerformanceCaptureOptions FromArguments(string[] arguments)
        {
            string scene = ReadArgument(arguments, "-bp-perf-scene");
            if (scene == null) return null;
            return new PerformanceCaptureOptions(scene,
                ReadArgument(arguments, "-bp-perf-label") ?? "command-line",
                ReadNumber(arguments, "-bp-perf-warmup", 5),
                ReadNumber(arguments, "-bp-perf-seconds", 30),
                ReadNumber(arguments, "-bp-perf-target-fps", 60),
                ReadDimension(arguments, "-bp-perf-width"), ReadDimension(arguments, "-bp-perf-height"));
        }

        private static int ReadDimension(string[] arguments, string key)
        {
            string value = ReadArgument(arguments, key);
            if (value == null) return 0;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dimension) || dimension <= 0)
                throw new ArgumentException("Invalid performance capture dimension: " + key);
            return dimension;
        }

        private static string ReadArgument(string[] arguments, string key)
        {
            if (arguments == null) return null;
            for (int index = 0; index < arguments.Length; index++)
            {
                string argument = arguments[index] ?? string.Empty;
                if (argument.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                    return argument.Substring(key.Length + 1);
                if (string.Equals(argument, key, StringComparison.OrdinalIgnoreCase))
                    return index + 1 < arguments.Length ? arguments[index + 1] : string.Empty;
            }
            return null;
        }

        private static double ReadNumber(string[] arguments, string key, double fallback)
        {
            string value = ReadArgument(arguments, key);
            if (value == null) return fallback;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                throw new ArgumentException("Invalid performance capture argument: " + key);
            return number;
        }

        private static void ValidateRange(double value, double minimum, double maximum, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(name, $"Expected {minimum}-{maximum}.");
        }
    }

    [Serializable]
    public struct PerformanceDistribution
    {
        public int sampleCount;
        public double mean;
        public double p50;
        public double p95;
        public double p99;
        public double maximum;
    }

    /// <summary>No allocation while collecting; unsupported values are omitted, not reported as zero.</summary>
    internal sealed class PerformanceCaptureSamples
    {
        internal const int MetricCount = 22;
        internal const int FrameInterval = 0;
        internal const int MainThread = 1;
        internal const int RenderThread = 2;
        internal const int Gpu = 3;
        internal const int GcBytes = 4;
        internal const int FootBake = 5;
        internal const int Reflection = 6;
        internal const int CombatTotal = 7;
        internal const int CombatPose = 8;
        internal const int CombatCapture = 9;
        internal const int CombatContact = 10;
        internal const int CombatPhysicsSync = 11;
        internal const int CombatFlight = 12;
        internal const int CombatEffects = 13;
        internal const int NpcAnimation = 14;
        internal const int NpcComposition = 15;
        internal const int NpcSnapshot = 16;
        internal const int EditorLoop = 17;
        internal const int WaitTargetFps = 18;
        internal const int WaitPresent = 19;
        internal const int WarehouseProbeSubmit = 20;
        internal const int WarehouseProbePoll = 21;
        private readonly double[][] values;
        private readonly int[] counts = new int[MetricCount];

        public PerformanceCaptureSamples(int capacity = PerformanceCaptureOptions.MaximumSamples)
        {
            if (capacity < 1 || capacity > PerformanceCaptureOptions.MaximumSamples)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            values = new double[MetricCount][];
            for (int metric = 0; metric < MetricCount; metric++)
                values[metric] = new double[capacity];
        }

        public int FrameCount => counts[FrameInterval];
        public bool IsFull => FrameCount == values[FrameInterval].Length;

        public void Add(int metric, double value)
        {
            if (value < 0 || double.IsNaN(value) || double.IsInfinity(value)) return;
            int count = counts[metric];
            if (count == values[metric].Length) return;
            values[metric][count] = value;
            counts[metric] = count + 1;
        }

        public PerformanceDistribution Summarize(int metric)
        {
            int count = counts[metric];
            if (count == 0) return default;
            var sorted = new double[count];
            Array.Copy(values[metric], sorted, count);
            Array.Sort(sorted);
            double sum = 0;
            for (int index = 0; index < count; index++) sum += sorted[index];
            return new PerformanceDistribution
            {
                sampleCount = count,
                mean = sum / count,
                p50 = Percentile(sorted, 0.50),
                p95 = Percentile(sorted, 0.95),
                p99 = Percentile(sorted, 0.99),
                maximum = sorted[count - 1]
            };
        }

        private static double Percentile(double[] sorted, double fraction)
        {
            // Nearest rank, so an actual observed frame is reported at every percentile.
            return sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * fraction) - 1)];
        }
    }
}
