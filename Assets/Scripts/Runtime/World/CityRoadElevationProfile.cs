using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A height at a measured horizontal distance along one road path.</summary>
    public readonly struct CityRoadElevationStation
    {
        public CityRoadElevationStation(float horizontalDistance, float height)
        { HorizontalDistance = horizontalDistance; Height = height; }
        public float HorizontalDistance { get; }
        public float Height { get; }
    }

    public readonly struct CityRoadElevationSample
    {
        internal CityRoadElevationSample(float height, float signedGradePercent, int segmentIndex)
        { Height = height; SignedGradePercent = signedGradePercent; SegmentIndex = segmentIndex; }
        public float Height { get; }
        /// <summary>Positive rises in the direction of increasing path distance.</summary>
        public float SignedGradePercent { get; }
        public float GradePercent => Mathf.Abs(SignedGradePercent);
        public int SegmentIndex { get; }

        /// <summary>The planar forward must point towards increasing path distance.</summary>
        public Vector3 GetTangent(Vector2 planarForward)
        {
            Vector2 forward = Normalize(planarForward);
            return Unit(forward.x, SignedGradePercent * .01f, forward.y);
        }

        /// <summary>Upward surface normal, with no authored crossfall.</summary>
        public Vector3 GetNormal(Vector2 planarForward)
        {
            Vector2 forward = Normalize(planarForward);
            float slope = SignedGradePercent * .01f;
            return Unit(-forward.x * slope, 1f, -forward.y * slope);
        }

        private static Vector3 Unit(float x, float y, float z)
        {
            double inverseLength = 1d / Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
            return new Vector3((float)(x * inverseLength), (float)(y * inverseLength), (float)(z * inverseLength));
        }

        private static Vector2 Normalize(Vector2 forward)
        {
            if (!CityRoadElevationProfile.IsFinite(forward.x) || !CityRoadElevationProfile.IsFinite(forward.y) ||
                (forward.x == 0f && forward.y == 0f))
                throw new ArgumentException("A road frame requires a finite nonzero planar forward.", nameof(forward));
            double length = Math.Sqrt((double)forward.x * forward.x + (double)forward.y * forward.y);
            return new Vector2((float)(forward.x / length), (float)(forward.y / length));
        }
    }

    /// <summary>
    /// Immutable piecewise linear road height. Equal successive heights preserve
    /// internal turn/stop landings. Construction is bounded; queries allocate
    /// nothing and search the stations logarithmically. It does not reserve a
    /// corridor or prove vehicle clearance, and is not a live-road activation.
    /// </summary>
    public sealed class CityRoadElevationProfile
    {
        public const int MaximumStations = 256;
        private const float GradeTolerance = .001f;
        private readonly CityRoadElevationStation[] stations;
        private readonly float[] signedGrades;

        public CityRoadElevationProfile(IReadOnlyList<CityRoadElevationStation> source, float maximumGradePercent)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.Count < 2 || source.Count > MaximumStations)
                throw new ArgumentException("A road profile requires between two and 256 stations.", nameof(source));
            if (!IsFinite(maximumGradePercent) || maximumGradePercent < 0f)
                throw new ArgumentOutOfRangeException(nameof(maximumGradePercent));

            stations = new CityRoadElevationStation[source.Count];
            signedGrades = new float[source.Count - 1];
            AllowedGradePercent = maximumGradePercent;
            for (int i = 0; i < stations.Length; i++)
            {
                CityRoadElevationStation station = source[i];
                if (!IsFinite(station.HorizontalDistance) || !IsFinite(station.Height) ||
                    (i == 0 ? station.HorizontalDistance != 0f : station.HorizontalDistance <= stations[i - 1].HorizontalDistance))
                    throw new ArgumentException("Road stations require finite heights and strictly increasing distances starting at zero.", nameof(source));
                stations[i] = station;
                if (i == 0) continue;
                CityRoadElevationStation previous = stations[i - 1];
                double grade = ((double)station.Height - previous.Height) /
                    ((double)station.HorizontalDistance - previous.HorizontalDistance) * 100d;
                if (Math.Abs(grade) > (double)maximumGradePercent + GradeTolerance)
                    throw new ArgumentException($"Road profile segment {i - 1} exceeds its grade limit.", nameof(source));
                signedGrades[i - 1] = (float)grade;
                MaximumGradePercent = Mathf.Max(MaximumGradePercent, Mathf.Abs(signedGrades[i - 1]));
            }
            Stations = Array.AsReadOnly(stations);
            HorizontalLength = stations[stations.Length - 1].HorizontalDistance;
        }

        public IReadOnlyList<CityRoadElevationStation> Stations { get; }
        public float HorizontalLength { get; }
        public float AllowedGradePercent { get; }
        public float MaximumGradePercent { get; }

        /// <summary>
        /// Finite out-of-range distances clamp to the endpoints. An internal
        /// station owns its outgoing grade; the final station uses the incoming
        /// grade. Tangent/normal queries therefore share the height's segment.
        /// </summary>
        public CityRoadElevationSample SampleDistance(float horizontalDistance)
        {
            if (!IsFinite(horizontalDistance)) throw new ArgumentOutOfRangeException(nameof(horizontalDistance));
            float distance = Mathf.Clamp(horizontalDistance, 0f, HorizontalLength);
            int low = 0, high = stations.Length - 1;
            while (high - low > 1)
            {
                int middle = (low + high) / 2;
                if (stations[middle].HorizontalDistance <= distance) low = middle;
                else high = middle;
            }
            CityRoadElevationStation first = stations[low], second = stations[low + 1];
            double amount = ((double)distance - first.HorizontalDistance) /
                ((double)second.HorizontalDistance - first.HorizontalDistance);
            float height = (float)(first.Height + ((double)second.Height - first.Height) * amount);
            return new CityRoadElevationSample(height, signedGrades[low], low);
        }

        internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
