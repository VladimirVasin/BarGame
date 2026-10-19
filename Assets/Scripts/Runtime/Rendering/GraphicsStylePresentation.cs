using UnityEngine;

namespace BarPromenade.Rendering
{
    /// <summary>Resolved presentation only; the scene, lighting and effect preferences stay shared.</summary>
    public readonly struct GraphicsStyleParameters
    {
        internal GraphicsStyleParameters(Vector2Int resolution, float quantization,
            float dither, float scanlines, float vertexJitter, bool native)
        {
            Resolution = resolution;
            QuantizationStrength = quantization;
            DitherStrength = dither;
            ScanlineIntensity = scanlines;
            VertexJitterStrength = vertexJitter;
            Native = native;
        }

        public Vector2Int Resolution { get; }
        public float QuantizationStrength { get; }
        public float DitherStrength { get; }
        public float ScanlineIntensity { get; }
        public float VertexJitterStrength { get; }
        public bool Native { get; }
    }

    public static class GraphicsStylePresentation
    {
        public static GraphicsStyleParameters Resolve(GraphicsStyle style,
            Ps1PresentationSettings profile, int width, int height,
            bool dither, bool scanlines, bool vertexJitter)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            if (style == GraphicsStyle.CleanLowPoly)
                return new GraphicsStyleParameters(new Vector2Int(width, height),
                    0f, 0f, 0f, 0f, true);

            // Preserve the original profile and its independent opt-in flags exactly.
            return new GraphicsStyleParameters(profile.GetInternalResolution(width, height),
                profile.QuantizationStrength, dither ? profile.DitherStrength : 0f,
                scanlines ? profile.ScanlineIntensity : 0f,
                vertexJitter ? profile.VertexJitterStrength : 0f, false);
        }
    }
}
