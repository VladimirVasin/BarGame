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
            float grain = 0.5f + 0.25f * Mathf.Sin(point.x * 0.83f + point.y * 0.37f) +
                          0.25f * Mathf.Sin(point.x * 1.31f - point.y * 0.69f);
            return envelope * Mathf.Lerp(0.025f, MaximumLooseDepth, grain);
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
