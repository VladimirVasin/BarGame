using UnityEngine;

namespace BarPromenade
{
    /// <summary>Shallow wind-shaped sand and its compressible surface layer.</summary>
    internal static class CityBeachSandPlan
    {
        internal const float MeshPitch = 0.40f;
        internal const float MaximumRelief = 0.15f;
        internal const float MaximumLooseDepth = 0.10f;

        internal static float SampleRelief(
            CityElevationPlan elevation, CitySurfaceDescriptor surface, Vector2 point)
        {
            float envelope = Envelope(elevation, surface, point, 0f, 2.6f);
            // Every continuous ground kind asks for relief, and only the
            // waterfront band gets any. Off the band the envelope is +0f and
            // the product below is a signed zero whose sign the sole caller
            // (the datum sum, then "+ GroundTopOffset") cannot observe; a
            // non-finite point would still turn 0f * NaN into NaN, so it is
            // left to the full path.
            if (envelope == 0f && IsFinite(point))
                return 0f;
            float dunes = Mathf.Sin(point.x * 0.41f + Mathf.Sin(point.y * 0.19f)) * 0.65f +
                          Mathf.Sin(point.x * 0.19f - point.y * 0.36f) * 0.35f;
            float ripples = Mathf.Sin(point.x * 1.6f + point.y * 2.1f +
                                     Mathf.Sin(point.x * 0.37f));
            return envelope * (dunes * 0.125f + ripples * 0.025f);
        }

        internal static float SampleLooseDepth(
            CityElevationPlan elevation, CitySurfaceDescriptor surface, Vector2 point)
        {
            // The surf band is already compacted and stays on the shared
            // shore surface. The looser inland sand can hold a foot groove.
            float envelope = Envelope(elevation, surface, point, 4f, 6f);
            // The depth factor is a lerp between two positive constants, so
            // a +0f envelope always yields exactly +0f here.
            if (envelope == 0f && IsFinite(point))
                return 0f;
            float grain = 0.5f + 0.25f * Mathf.Sin(point.x * 0.83f + point.y * 0.37f) +
                          0.25f * Mathf.Sin(point.x * 1.31f - point.y * 0.69f);
            return envelope * Mathf.Lerp(0.025f, MaximumLooseDepth, grain);
        }

        private static bool IsFinite(Vector2 point)
        {
            return !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
                   !float.IsNaN(point.y) && !float.IsInfinity(point.y);
        }

        private static float Envelope(
            CityElevationPlan elevation, CitySurfaceDescriptor surface, Vector2 point,
            float shoreStart, float shoreFull)
        {
            if (surface.Kind != CitySurfaceKind.Beach ||
                surface.Feature != CityAreaFeatureKind.NorthWaterfront)
                return 0f;
            float street = elevation.WorldOrigin.z + surface.Cell.y * elevation.NodeSpacing.y +
                           elevation.RoadWidth * 0.5f;
            float inland = Mathf.SmoothStep(0f, 1f, (point.y - street) / 2.5f);
            float shore = Mathf.SmoothStep(0f, 1f,
                (surface.WorldBounds.yMax - point.y - shoreStart) / (shoreFull - shoreStart));
            return inland * shore;
        }
    }
}
