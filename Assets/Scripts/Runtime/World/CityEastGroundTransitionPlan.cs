using UnityEngine;

namespace BarPromenade
{
    /// <summary>One material boundary shared by the garden and the eastern yard.
    /// Its outer envelope is wider than the irregular 3–5 m worn transition.
    /// Coordinates are local to the seam, so different city sizes keep both
    /// adjoining textures in phase without a different baked asset.</summary>
    public sealed class CityEastGroundTransitionPlan
    {
        public const float HalfDepth = 4f;
        public const float WorldRepeat = 24f;
        public const string TextureResource = "Textures/CityEastGroundTransitionAlbedo";
        private CityEastGroundTransitionPlan(CityEastExitPlan exit)
        {
            IsEnabled = exit.IsEnabled;
            if (!IsEnabled) return;
            SeamZ = exit.YardBounds.yMin;
            Bounds = Rect.MinMaxRect(exit.YardBounds.xMin, SeamZ - HalfDepth,
                exit.YardBounds.xMax, SeamZ + HalfDepth);
        }
        public bool IsEnabled { get; }
        public float SeamZ { get; }
        public Rect Bounds { get; }
        // Same periodic outline as build-city-east-ground-texture.py. Used by
        // the existing footstep overlay channel, not by rendering each frame.
        public float SoilWeight(Vector2 point)
        {
            float z = point.y - SeamZ;
            float phase = point.x * (2f * Mathf.PI / WorldRepeat);
            float center = -.55f + .43f * Mathf.Sin(phase) + .24f * Mathf.Sin(3f * phase + .7f);
            float width = 3.7f + .45f * Mathf.Sin(2f * phase - .6f);
            float irregular = .20f * Mathf.Sin(5f * phase + z * 2.1f) + .12f * Mathf.Sin(9f * phase - z * 3.4f);
            float t = Mathf.Clamp01((z - center + irregular) / width + .5f);
            return t * t * (3f - 2f * t);
        }
        public static CityEastGroundTransitionPlan Create(CityLayout layout) =>
            new CityEastGroundTransitionPlan(CityEastExitPlanner.Create(layout));
    }
}
