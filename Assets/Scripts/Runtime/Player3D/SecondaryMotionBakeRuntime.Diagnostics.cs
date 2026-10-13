using System;
using System.Diagnostics;
using UnityEngine;

namespace BarPromenade
{
    internal static partial class SecondaryMotionBakeRuntime
    {
        // A sample uses the context's retained clip name. Native Object.name
        // reads and variant/name concatenation belong only to cold loading.
        private readonly struct ClipKey : IEquatable<ClipKey>
        {
            internal readonly string Variant, Name;
            internal ClipKey(string variant, string name) { Variant = variant; Name = name; }
            public bool Equals(ClipKey other) => string.Equals(Variant, other.Variant, StringComparison.Ordinal) &&
                string.Equals(Name, other.Name, StringComparison.Ordinal);
            public override bool Equals(object value) => value is ClipKey other && Equals(other);
            public override int GetHashCode() => unchecked(StringComparer.Ordinal.GetHashCode(Variant) * 397 ^
                StringComparer.Ordinal.GetHashCode(Name));
            public override string ToString() => Variant + "/" + Name;
        }

        internal readonly struct DiagnosticsSnapshot
        {
            internal readonly long CacheHits, CacheMisses, DecodeCount, LoadTicks, DecodeTicks,
                LargestDecodeTicks, DecodedValues, ResidentFloatValues, Evictions, GuardBuildCount;
            internal readonly int ResidentClips;
            internal readonly string LastDecodedKey;
            internal DiagnosticsSnapshot(long hits, long misses, long count, long load, long decode,
                long maximum, long values, long resident, long evictions, long guards, int clips, string key)
            {
                CacheHits = hits; CacheMisses = misses; DecodeCount = count; LoadTicks = load; DecodeTicks = decode;
                LargestDecodeTicks = maximum; DecodedValues = values; ResidentFloatValues = resident;
                Evictions = evictions; GuardBuildCount = guards; ResidentClips = clips; LastDecodedKey = key;
            }
        }

        private static readonly long[,] resultCounts = new long[3, Enum.GetValues(typeof(SecondaryMotionBakeResult)).Length];
        private static readonly string[] resultNames = Enum.GetNames(typeof(SecondaryMotionBakeResult));
        private static long cacheHits, cacheMisses, decodeCount, loadTicks, decodeTicks,
            largestDecodeTicks, decodedValues, evictions, guardBuildCount;
        private static string lastDecodedKey;
        internal static DiagnosticsSnapshot Diagnostics => new DiagnosticsSnapshot(cacheHits, cacheMisses,
            decodeCount, loadTicks, decodeTicks, largestDecodeTicks, decodedValues, ResidentValues(),
            evictions, guardBuildCount, clips.Count, lastDecodedKey);

        internal static string ResultName(SecondaryMotionBakeResult result) => resultNames[(int)result];

        internal static long ResultCount(SecondaryMotionBakePart part, SecondaryMotionBakeResult result) =>
            resultCounts[(int)part, (int)result];

        private static void RecordResult(SecondaryMotionBakePart part, SecondaryMotionBakeResult result) =>
            resultCounts[(int)part, (int)result]++;

        private static void ResetDiagnostics()
        {
            Array.Clear(resultCounts, 0, resultCounts.Length);
            cacheHits = cacheMisses = decodeCount = loadTicks = decodeTicks = largestDecodeTicks = decodedValues = evictions = guardBuildCount = 0;
            lastDecodedKey = null;
        }

        /// <summary>Prepare a selected recording on the Unity thread while the
        /// loading owner still blocks gameplay. It uses the normal bounded cache;
        /// sampling continues to validate every pose and physical contact guard.</summary>
        internal static bool Prewarm(string variant, string clipName)
        {
            if (string.IsNullOrEmpty(variant)) throw new ArgumentException("A recording variant is required.", nameof(variant));
            if (string.IsNullOrEmpty(clipName)) throw new ArgumentException("An animation name is required.", nameof(clipName));
            return GetOrLoadClip(new ClipKey(variant, clipName)) != null;
        }

        private static ClipData GetOrLoadClip(ClipKey key)
        {
            if (clips.TryGetValue(key, out ClipData clip)) { cacheHits++; return clip; }
            cacheMisses++;
            long started = Stopwatch.GetTimestamp();
            TextAsset asset = Resources.Load<TextAsset>(ResourceRoot + key);
            if (asset != null)
            {
                // TextAsset.bytes copies its payload. Read it once per cache miss,
                // not once for each hair/jacket/sling consumer of this recording.
                byte[] bytes = asset.bytes;
                long decodeStarted = Stopwatch.GetTimestamp();
                clip = Decode(bytes);
                long elapsed = Stopwatch.GetTimestamp() - decodeStarted;
                decodeCount++; decodeTicks += elapsed;
                largestDecodeTicks = Math.Max(largestDecodeTicks, elapsed);
                decodedValues += Values(clip);
                lastDecodedKey = key.ToString();
            }
            loadTicks += Stopwatch.GetTimestamp() - started;
            while (residentOrder.Count > 0 && (clips.Count >= MaximumResidentClips ||
                ResidentValues() + Values(clip) > 16000000))
            {
                clips.Remove(residentOrder.Dequeue()); evictions++;
            }
            clips.Add(key, clip); residentOrder.Enqueue(key);
            return clip;
        }
    }
}
