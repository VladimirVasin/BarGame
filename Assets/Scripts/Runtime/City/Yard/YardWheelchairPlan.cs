using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The circuit the yard rider follows: the ring the shared yard
    /// site contract authored around the dead tree. Nothing is drawn
    /// for it — the chair rides bare ground, and the same contract
    /// keeps the yard dressing and the leaning utilities off the lap.
    /// </summary>
    public readonly struct YardWheelchairPlan
    {
        public const string TreeTrunkId = "home-yard-tree-trunk";

        /// <summary>Height samples taken around one full circle.</summary>
        public const int GroundSampleCount = 64;

        private readonly float[] groundHeights;

        public YardWheelchairPlan(
            Vector3 center,
            float radius,
            float groundY,
            bool clockwise,
            float[] circuitGroundHeights = null)
        {
            Center = new Vector3(center.x, groundY, center.z);
            Radius = radius;
            GroundY = groundY;
            Clockwise = clockwise;
            groundHeights =
                circuitGroundHeights != null &&
                circuitGroundHeights.Length > 1
                    ? circuitGroundHeights
                    : null;
            IsPresent = radius > 0.5f;
        }

        public Vector3 Center { get; }
        public float Radius { get; }
        public float GroundY { get; }
        public bool Clockwise { get; }
        public bool IsPresent { get; }
        public float LapLength => Mathf.PI * 2f * Radius;

        /// <summary>
        /// The ground under the circuit at one ring angle. The yard can
        /// straddle two terraces, so the lap is not a flat plane: the
        /// sampled profile follows every datum change, interpolated so a
        /// terrace lip reads as a short ramp instead of a hover.
        /// </summary>
        public float SampleGroundHeight(float angle)
        {
            if (groundHeights == null)
            {
                return GroundY;
            }

            float turns = Mathf.Repeat(
                angle / (Mathf.PI * 2f),
                1f);
            float scaled = turns * groundHeights.Length;
            int first = Mathf.FloorToInt(scaled) % groundHeights.Length;
            int second = (first + 1) % groundHeights.Length;
            return Mathf.Lerp(
                groundHeights[first],
                groundHeights[second],
                scaled - Mathf.Floor(scaled));
        }

        /// <summary>
        /// Derives the circuit from the shared yard site contract: its
        /// authored ring centre and radius are the lap, its ground the
        /// height. The dead tree must still stand at the centre — the
        /// rider circles something, not an abstract point. With an
        /// elevation plan the lap also carries a sampled ground
        /// profile, so the chair rides the real terraces instead of one
        /// flat plane.
        /// </summary>
        public static YardWheelchairPlan Create(
            CityOpenAreaDecorationPlan decorations,
            CityElevationPlan elevation = null)
        {
            if (decorations == null)
            {
                throw new ArgumentNullException(nameof(decorations));
            }

            if (!decorations.HomeYardSite.HasValue)
            {
                return default;
            }

            bool hasTree = false;
            for (int index = 0;
                 index < decorations.Descriptors.Count;
                 index++)
            {
                if (string.Equals(
                        decorations.Descriptors[index].StableId,
                        TreeTrunkId,
                        StringComparison.Ordinal))
                {
                    hasTree = true;
                    break;
                }
            }

            if (!hasTree)
            {
                return default;
            }

            HomeYardSitePlan site = decorations.HomeYardSite.Value;
            return new YardWheelchairPlan(
                site.RingCenter,
                site.RingRadius,
                site.GroundY,
                true,
                SampleCircuitGround(site, elevation));
        }

        private static float[] SampleCircuitGround(
            HomeYardSitePlan site,
            CityElevationPlan elevation)
        {
            if (elevation == null)
            {
                return null;
            }

            var heights = new float[GroundSampleCount];
            for (int index = 0; index < GroundSampleCount; index++)
            {
                float angle = Mathf.PI * 2f * index /
                              GroundSampleCount;
                var probe = new Vector2(
                    site.RingCenter.x +
                    Mathf.Cos(angle) * site.RingRadius,
                    site.RingCenter.z +
                    Mathf.Sin(angle) * site.RingRadius);
                float fallbackDatum = elevation.TrySampleSurface(
                        probe,
                        CitySurfaceRole.GroundDatum,
                        out float datum,
                        out _)
                    ? datum
                    : site.GroundY - CityElevationPlan.GroundTopOffset;
                var cell = new Vector2Int(
                    Mathf.FloorToInt(
                        (probe.x - elevation.WorldOrigin.x) /
                        elevation.NodeSpacing.x),
                    Mathf.FloorToInt(
                        (probe.y - elevation.WorldOrigin.z) /
                        elevation.NodeSpacing.y));
                heights[index] =
                    CityTerrainSurfacePlan.SampleContinuousDatum(
                        elevation,
                        cell,
                        probe,
                        fallbackDatum) +
                    CityElevationPlan.GroundTopOffset;
            }

            return heights;
        }
    }
}
