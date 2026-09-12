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
        /// The closed-form partial derivatives of
        /// <see cref="Field.SampleLooseDepth"/> at <paramref name="point"/>:
        /// <c>x</c> is dDepth/dx, <c>y</c> is dDepth/dz. One evaluation
        /// replaces the four finite-difference taps the loose-sand normal
        /// pass used to take around every vertex.
        /// </summary>
        internal static Vector2 SampleLooseDepthGradient(in Field field, Vector2 point)
        {
            return field.SampleLooseDepthGradient(point);
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

            /// <summary>
            /// (dDepth/dx, dDepth/dz) of <see cref="SampleLooseDepth"/>,
            /// differentiated term by term:
            /// <list type="bullet">
            /// <item>The envelope depends on z alone, so its x-derivative
            /// is zero. Each factor is <c>Mathf.SmoothStep(0, 1, t)</c> =
            /// 3t² − 2t³ on a clamped t: its slope in t is 6t(1 − t) on the
            /// ramp and exactly zero on both clamp plateaus, so the product
            /// rule below is right across the clamp edges too (the field is
            /// C¹ there; only the second derivative jumps).</item>
            /// <item>The grain lerp is affine in the grain — its clamp is
            /// inert because the grain is 0.5 ± 0.25 ± 0.25 and never
            /// leaves [0, 1] — so its slope is the constant
            /// <c>MaximumLooseDepth − 0.025</c> times the grain's
            /// sinusoid derivatives.</item>
            /// </list>
            /// Zero where the field does not apply, exactly as the depth is.
            /// </summary>
            internal Vector2 SampleLooseDepthGradient(Vector2 point)
            {
                if (!applies)
                    return Vector2.zero;
                const float shoreStart = 4f;
                const float shoreFull = 6f;
                const float inlandRun = 2.5f;
                const float shoreRun = shoreFull - shoreStart;
                float inlandT = (point.y - street) / inlandRun;
                float shoreT = (shoreZ - point.y - shoreStart) / shoreRun;
                float inland = Mathf.SmoothStep(0f, 1f, inlandT);
                float shore = Mathf.SmoothStep(0f, 1f, shoreT);
                // d/dz of each ramp: the chain rule pulls in dt/dz, which is
                // +1/run for the inland ramp and -1/run for the shore ramp.
                float inlandSlope = SmoothStepSlope(inlandT) / inlandRun;
                float shoreSlope = -SmoothStepSlope(shoreT) / shoreRun;
                float envelope = inland * shore;
                float envelopeSlopeZ = inlandSlope * shore + inland * shoreSlope;

                float phaseA = point.x * 0.83f + point.y * 0.37f;
                float phaseB = point.x * 1.31f - point.y * 0.69f;
                float grain = 0.5f + 0.25f * Mathf.Sin(phaseA) +
                              0.25f * Mathf.Sin(phaseB);
                float cosA = 0.25f * Mathf.Cos(phaseA);
                float cosB = 0.25f * Mathf.Cos(phaseB);
                float grainSlopeX = cosA * 0.83f + cosB * 1.31f;
                float grainSlopeZ = cosA * 0.37f - cosB * 0.69f;
                const float depthPerGrain = MaximumLooseDepth - 0.025f;
                float depthFactor = Mathf.Lerp(0.025f, MaximumLooseDepth, grain);
                return new Vector2(
                    envelope * depthPerGrain * grainSlopeX,
                    envelopeSlopeZ * depthFactor +
                    envelope * depthPerGrain * grainSlopeZ);
            }

            /// <summary>
            /// d/dt of <c>Mathf.SmoothStep(0f, 1f, t)</c>: 6t(1 − t) inside
            /// the ramp, zero on the clamped plateaus either side.
            /// </summary>
            private static float SmoothStepSlope(float t)
            {
                if (t <= 0f || t >= 1f)
                    return 0f;
                return 6f * t * (1f - t);
            }
        }

        private static bool IsFinite(Vector2 point)
        {
            return !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
                   !float.IsNaN(point.y) && !float.IsInfinity(point.y);
        }
    }
}
