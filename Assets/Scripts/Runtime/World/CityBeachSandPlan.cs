using UnityEngine;

namespace BarPromenade
{
    /// <summary>Shallow wind-shaped sand and its compressible surface layer.</summary>
    internal static class CityBeachSandPlan
    {
        internal const float MeshPitch = 0.40f;
        /// <summary>
        /// The pitch of the sand's collision skin away from the port. The
        /// drawn skin resolves the 2.2-2.6 m ripples at 0.40 m; a 1 m
        /// triangulation of the same plan puts a foot at most about 2.4 cm
        /// off the drawn surface (0.025 m ripple amplitude, plus the long
        /// dunes), the deviation the user accepted. Inside the port's graded
        /// box the collider keeps <see cref="MeshPitch"/>: the two-metre
        /// earthwork blend there carries up to 0.8 m of grading, and a 1 m
        /// chord across it would miss the surface by some 15 cm.
        /// </summary>
        internal const float CollisionPitch = 1.0f;
        internal const float MaximumRelief = 0.15f;
        internal const float MaximumLooseDepth = 0.10f;

        internal static float SampleRelief(
            CityElevationPlan elevation, CitySurfaceDescriptor surface, Vector2 point)
        {
            float envelope = new Field(elevation, surface).Envelope(point, 0f, 2.6f);
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
            return new Field(elevation, surface).SampleLooseDepth(point);
        }

        /// <summary>
        /// What the envelope takes from the surface and the elevation plan,
        /// resolved once so a pass over a whole mesh does not redo it under
        /// every vertex and every gradient tap. The static samplers are this
        /// struct: there is one copy of the arithmetic.
        /// </summary>
        internal readonly struct Field
        {
            private readonly bool applies;
            private readonly float street;
            private readonly float shoreZ;

            internal Field(CityElevationPlan elevation, CitySurfaceDescriptor surface)
            {
                applies = surface.Kind == CitySurfaceKind.Beach &&
                          surface.Feature == CityAreaFeatureKind.NorthWaterfront;
                street = applies
                    ? elevation.WorldOrigin.z + surface.Cell.y * elevation.NodeSpacing.y +
                      elevation.RoadWidth * 0.5f
                    : 0f;
                shoreZ = surface.WorldBounds.yMax;
            }

            internal float Envelope(Vector2 point, float shoreStart, float shoreFull)
            {
                if (!applies)
                    return 0f;
                float inland = Mathf.SmoothStep(0f, 1f, (point.y - street) / 2.5f);
                float shore = Mathf.SmoothStep(0f, 1f,
                    (shoreZ - point.y - shoreStart) / (shoreFull - shoreStart));
                return inland * shore;
            }

            internal float SampleLooseDepth(Vector2 point)
            {
                // The surf band is already compacted and stays on the shared
                // shore surface. The looser inland sand can hold a foot groove.
                float envelope = Envelope(point, 4f, 6f);
                // The depth factor is a lerp between two positive constants, so
                // a +0f envelope always yields exactly +0f here.
                if (envelope == 0f && IsFinite(point))
                    return 0f;
                float grain = 0.5f + 0.25f * Mathf.Sin(point.x * 0.83f + point.y * 0.37f) +
                              0.25f * Mathf.Sin(point.x * 1.31f - point.y * 0.69f);
                return envelope * Mathf.Lerp(0.025f, MaximumLooseDepth, grain);
            }
        }

        private static bool IsFinite(Vector2 point)
        {
            return !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
                   !float.IsNaN(point.y) && !float.IsInfinity(point.y);
        }
    }
}
