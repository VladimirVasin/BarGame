using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct CityMapMountainHatchSegment
    {
        internal CityMapMountainHatchSegment(Vector3 start, Vector3 end)
        {
            Start = start;
            End = end;
        }

        public Vector3 Start { get; }
        public Vector3 End { get; }
    }

    /// <summary>
    /// Immutable, presentation-only chart data for the mountain road. It is
    /// deliberately built from route samples instead of reading the live
    /// scene, so opening the tab never constructs or keeps the other area's
    /// world alive.
    /// </summary>
    public sealed class CityMapMountainRoadOverlay
    {
        private static readonly IReadOnlyList<Vector3> NoPoints =
            new ReadOnlyCollection<Vector3>(new List<Vector3>());
        private static readonly IReadOnlyList<
            CityMapMountainHatchSegment> NoHatches =
            new ReadOnlyCollection<CityMapMountainHatchSegment>(
                new List<CityMapMountainHatchSegment>());
        private static readonly IReadOnlyList<
            MountainRoadTerminalLandmark> NoTerminalLandmarks =
            new ReadOnlyCollection<MountainRoadTerminalLandmark>(
                new List<MountainRoadTerminalLandmark>());

        internal CityMapMountainRoadOverlay(
            IList<Vector3> routePoints,
            IList<Vector3> hairpinPositions,
            IList<CityMapMountainHatchSegment> mountainHatches,
            IList<MountainRoadTerminalLandmark> terminalLandmarks,
            Rect plateauBounds,
            Rect displayWorldXZBounds)
        {
            RoutePoints = new ReadOnlyCollection<Vector3>(
                new List<Vector3>(routePoints));
            HairpinPositions = new ReadOnlyCollection<Vector3>(
                new List<Vector3>(hairpinPositions));
            MountainHatches =
                new ReadOnlyCollection<CityMapMountainHatchSegment>(
                    new List<CityMapMountainHatchSegment>(mountainHatches));
            TerminalLandmarks =
                new ReadOnlyCollection<MountainRoadTerminalLandmark>(
                    new List<MountainRoadTerminalLandmark>(
                        terminalLandmarks));
            PlateauBounds = plateauBounds;
            DisplayWorldXZBounds = displayWorldXZBounds;
        }

        private CityMapMountainRoadOverlay()
        {
            RoutePoints = NoPoints;
            HairpinPositions = NoPoints;
            MountainHatches = NoHatches;
            TerminalLandmarks = NoTerminalLandmarks;
            PlateauBounds = Rect.zero;
            DisplayWorldXZBounds = new Rect(-1f, -1f, 2f, 2f);
        }

        public static CityMapMountainRoadOverlay Empty { get; } =
            new CityMapMountainRoadOverlay();

        public IReadOnlyList<Vector3> RoutePoints { get; }
        public IReadOnlyList<Vector3> HairpinPositions { get; }
        public IReadOnlyList<CityMapMountainHatchSegment> MountainHatches
        {
            get;
        }

        public IReadOnlyList<MountainRoadTerminalLandmark>
            TerminalLandmarks { get; }

        public Rect PlateauBounds { get; }
        public Rect DisplayWorldXZBounds { get; }
        public bool IsEmpty => RoutePoints.Count < 2;
        public Vector3 TunnelPosition =>
            IsEmpty ? Vector3.zero : RoutePoints[0];
        public Vector3 EndpointPosition =>
            IsEmpty ? Vector3.zero : RoutePoints[RoutePoints.Count - 1];
    }

    public static class CityMapMountainRoadOverlayBuilder
    {
        private const float DuplicateTolerance = 0.05f;
        private const float DisplayPadding = 9f;
        private const float MinimumHairpinRouteSeparation = 18f;
        private const int MountainHatchCount = 18;

        private readonly struct TurnCandidate
        {
            public TurnCandidate(int index, float score)
            {
                Index = index;
                Score = score;
            }

            public int Index { get; }
            public float Score { get; }
        }

        public static CityMapMountainRoadOverlay Create(
            IReadOnlyList<Vector3> routeSamples,
            Rect plateauBounds)
        {
            return Create(
                routeSamples,
                plateauBounds,
                Array.Empty<MountainRoadTerminalLandmark>());
        }

        public static CityMapMountainRoadOverlay Create(
            MountainRoadPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            IReadOnlyList<MountainRoadRouteSample> routeSamples =
                plan.Route.Samples;
            var positions = new List<Vector3>(routeSamples.Count);
            for (int index = 0; index < routeSamples.Count; index++)
            {
                positions.Add(routeSamples[index].Position);
            }

            return Create(
                positions,
                plan.Plateau.BoundsXZ,
                plan.Terminal.Landmarks);
        }

        public static CityMapMountainRoadOverlay Create(
            IReadOnlyList<Vector3> routeSamples,
            Rect plateauBounds,
            IReadOnlyList<MountainRoadTerminalLandmark> terminalLandmarks)
        {
            if (routeSamples == null)
            {
                throw new ArgumentNullException(nameof(routeSamples));
            }

            if (terminalLandmarks == null)
            {
                throw new ArgumentNullException(nameof(terminalLandmarks));
            }

            List<Vector3> route = CopyFiniteRoute(routeSamples);
            if (route.Count < 2)
            {
                return CityMapMountainRoadOverlay.Empty;
            }

            if (!IsFinite(plateauBounds) ||
                plateauBounds.width <= 0.01f ||
                plateauBounds.height <= 0.01f)
            {
                Vector3 endpoint = route[route.Count - 1];
                plateauBounds = new Rect(
                    endpoint.x - 6f,
                    endpoint.z - 5f,
                    12f,
                    10f);
            }

            List<MountainRoadTerminalLandmark> landmarks =
                CopyFiniteLandmarks(terminalLandmarks);
            Rect displayBounds = CreateDisplayBounds(
                route,
                plateauBounds,
                landmarks);
            List<Vector3> hairpins = FindHairpins(route);
            List<CityMapMountainHatchSegment> hatches =
                CreateMountainHatches(displayBounds);
            return new CityMapMountainRoadOverlay(
                route,
                hairpins,
                hatches,
                landmarks,
                plateauBounds,
                displayBounds);
        }

        private static List<MountainRoadTerminalLandmark>
            CopyFiniteLandmarks(
                IReadOnlyList<MountainRoadTerminalLandmark> source)
        {
            var landmarks = new List<MountainRoadTerminalLandmark>(
                source.Count);
            for (int index = 0; index < source.Count; index++)
            {
                MountainRoadTerminalLandmark landmark = source[index];
                if (!IsFinite(landmark.Position))
                {
                    throw new ArgumentException(
                        "Mountain-road terminal landmarks must be finite.",
                        nameof(source));
                }

                landmarks.Add(landmark);
            }

            return landmarks;
        }

        private static List<Vector3> CopyFiniteRoute(
            IReadOnlyList<Vector3> samples)
        {
            var route = new List<Vector3>(samples.Count);
            float duplicateSquared =
                DuplicateTolerance * DuplicateTolerance;
            for (int index = 0; index < samples.Count; index++)
            {
                Vector3 sample = samples[index];
                if (!IsFinite(sample))
                {
                    throw new ArgumentException(
                        "Mountain-road map samples must be finite.",
                        nameof(samples));
                }

                if (route.Count == 0 ||
                    PlanarDistanceSquared(
                        route[route.Count - 1],
                        sample) > duplicateSquared)
                {
                    route.Add(sample);
                }
            }

            return route;
        }

        private static Rect CreateDisplayBounds(
            IReadOnlyList<Vector3> route,
            Rect plateau,
            IReadOnlyList<MountainRoadTerminalLandmark> landmarks)
        {
            float minimumX = plateau.xMin;
            float maximumX = plateau.xMax;
            float minimumZ = plateau.yMin;
            float maximumZ = plateau.yMax;
            for (int index = 0; index < route.Count; index++)
            {
                minimumX = Mathf.Min(minimumX, route[index].x);
                maximumX = Mathf.Max(maximumX, route[index].x);
                minimumZ = Mathf.Min(minimumZ, route[index].z);
                maximumZ = Mathf.Max(maximumZ, route[index].z);
            }

            for (int index = 0; index < landmarks.Count; index++)
            {
                Vector3 position = landmarks[index].Position;
                minimumX = Mathf.Min(minimumX, position.x);
                maximumX = Mathf.Max(maximumX, position.x);
                minimumZ = Mathf.Min(minimumZ, position.z);
                maximumZ = Mathf.Max(maximumZ, position.z);
            }

            return Rect.MinMaxRect(
                minimumX - DisplayPadding,
                minimumZ - DisplayPadding,
                maximumX + DisplayPadding,
                maximumZ + DisplayPadding);
        }

        private static List<Vector3> FindHairpins(
            IReadOnlyList<Vector3> route)
        {
            var turns = new List<TurnCandidate>();
            var strengths = new float[route.Count];
            var distances = new float[route.Count];
            for (int index = 1; index < route.Count; index++)
            {
                distances[index] = distances[index - 1] +
                    Mathf.Sqrt(PlanarDistanceSquared(
                        route[index - 1],
                        route[index]));
            }

            for (int index = 1; index < route.Count - 1; index++)
            {
                Vector2 incoming = Planar(route[index] - route[index - 1]);
                Vector2 outgoing = Planar(route[index + 1] - route[index]);
                if (incoming.sqrMagnitude <= 0.0001f ||
                    outgoing.sqrMagnitude <= 0.0001f)
                {
                    continue;
                }

                strengths[index] = Mathf.Abs(
                    Vector2.SignedAngle(incoming, outgoing));
            }

            for (int index = 1; index < route.Count - 1; index++)
            {
                float score = 0f;
                for (int offset = -2; offset <= 2; offset++)
                {
                    int neighbour = index + offset;
                    if (neighbour > 0 && neighbour < route.Count - 1)
                    {
                        score += strengths[neighbour];
                    }
                }

                turns.Add(new TurnCandidate(index, score));
            }

            turns.Sort((left, right) =>
            {
                int score = right.Score.CompareTo(left.Score);
                return score != 0 ? score : left.Index.CompareTo(right.Index);
            });

            var selectedIndices = new List<int>(2);
            for (int index = 0;
                 index < turns.Count && selectedIndices.Count < 2;
                 index++)
            {
                int candidate = turns[index].Index;
                bool separated = true;
                for (int other = 0;
                     other < selectedIndices.Count;
                     other++)
                {
                    if (Mathf.Abs(
                            distances[candidate] -
                            distances[selectedIndices[other]]) <
                        MinimumHairpinRouteSeparation)
                    {
                        separated = false;
                        break;
                    }
                }

                if (separated)
                {
                    selectedIndices.Add(candidate);
                }
            }

            AddFallbackIndex(route, selectedIndices, route.Count / 3);
            AddFallbackIndex(route, selectedIndices, route.Count * 2 / 3);
            selectedIndices.Sort();

            var positions = new List<Vector3>(2);
            for (int index = 0;
                 index < selectedIndices.Count && index < 2;
                 index++)
            {
                positions.Add(route[selectedIndices[index]]);
            }

            return positions;
        }

        private static void AddFallbackIndex(
            IReadOnlyList<Vector3> route,
            ICollection<int> selectedIndices,
            int requestedIndex)
        {
            if (selectedIndices.Count >= 2 || route.Count < 3)
            {
                return;
            }

            int candidate = Mathf.Clamp(
                requestedIndex,
                1,
                route.Count - 2);
            if (!selectedIndices.Contains(candidate))
            {
                selectedIndices.Add(candidate);
            }
        }

        private static List<CityMapMountainHatchSegment>
            CreateMountainHatches(Rect bounds)
        {
            var result = new List<CityMapMountainHatchSegment>(
                MountainHatchCount);
            float step = bounds.width / (MountainHatchCount - 1f);
            float depth = Mathf.Min(5f, bounds.height * 0.12f);
            for (int index = 0; index < MountainHatchCount; index++)
            {
                float x = bounds.xMin + step * index;
                float lean = index % 2 == 0 ? 2.6f : -2.6f;
                result.Add(new CityMapMountainHatchSegment(
                    new Vector3(x, 0f, bounds.yMax - depth),
                    new Vector3(
                        Mathf.Clamp(x + lean, bounds.xMin, bounds.xMax),
                        0f,
                        bounds.yMax)));
            }

            return result;
        }

        private static Vector2 Planar(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }

        private static float PlanarDistanceSquared(
            Vector3 left,
            Vector3 right)
        {
            float x = left.x - right.x;
            float z = left.z - right.z;
            return x * x + z * z;
        }

        private static bool IsFinite(Rect value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.width) &&
                   IsFinite(value.height);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
