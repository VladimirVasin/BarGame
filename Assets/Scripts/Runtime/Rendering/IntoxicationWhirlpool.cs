using UnityEngine;

namespace BarPromenade.Rendering
{
    /// <summary>
    /// The geometry of the drunk vertigo whirlpool, and the CPU mirror of the
    /// composite shader that draws it. <see cref="Warp"/> is the same
    /// arithmetic as <c>ApplyVertigoWhirlpool</c> in
    /// <c>Assets/Resources/Shaders/Ps1Composite.shader</c>, kept here so the
    /// contract can be tested without a GPU; the shader keeps its own copy of
    /// the formula and a contract test pins the literals in both.
    ///
    /// Everything works in TARGET UV space — the unit square over the frame
    /// the player actually sees, which in 4:3 mode is the cropped window, so
    /// the water follows the visible picture exactly as the wave and the
    /// vignette do. Radii are measured after the aspect correction, in
    /// fractions of the frame's HEIGHT: half a height is 0.5, and the
    /// farthest corner of a 16:9 frame is 1.02 away from its middle.
    /// </summary>
    public static class IntoxicationWhirlpool
    {
        /// <summary>
        /// The calm disc over the hero. 0.28 of a frame height is 0.72 m at
        /// the exterior arm of 2.6 m under the follow lens, which covers his
        /// torso and head; his feet sit just outside it and take under two
        /// degrees. It needs no field-of-view term because the dolly zoom
        /// holds his apparent size invariant across its whole swing.
        /// </summary>
        public const float InnerRadius = 0.28f;

        /// <summary>
        /// How much the twisted frame is also drawn inward at the corners, so
        /// the picture reads as a funnel rather than as a flat shear.
        /// </summary>
        public const float RadialPull = 0.08f;

        /// <summary>
        /// The sampled point is kept this far inside the frame. The whirlpool
        /// shortens its own radius to that boundary instead of letting the
        /// clamped sampler smear an axis-aligned edge: the degeneracy then
        /// points into the vortex, which is the look that was asked for.
        /// </summary>
        public const float BoundaryMargin = 0.001f;

        /// <summary>
        /// The eye keeps full strength while it is this near the middle of
        /// the frame and fades out by the frame's edge, so a hero leaving the
        /// picture takes the water with him instead of snapping it off.
        /// </summary>
        public const float EyeFadeStart = 0.42f;
        public const float EyeFadeEnd = 0.5f;

        /// <summary>
        /// Turns the whirlpool's world-space eye into the two material
        /// vectors the composite takes. <paramref name="viewportPoint"/> is
        /// the eye through <c>Camera.WorldToViewportPoint</c>;
        /// <paramref name="aspectFraction"/> is the composite's 4:3 crop, and
        /// it is inverted here because the shader works in target space while
        /// the camera answers in source space. <paramref name="windowWidth"/>
        /// and <paramref name="windowHeight"/> are the visible window in
        /// output pixels, which is what makes the swirl circular rather than
        /// elliptical. Returns <c>false</c> when there is nothing to draw.
        /// </summary>
        public static bool TryResolve(
            Vector3 viewportPoint,
            float aspectFraction,
            int windowWidth,
            int windowHeight,
            float twistRadians,
            Vector2 corePixels,
            out Vector4 vertigo,
            out Vector4 shape)
        {
            float fraction = Mathf.Clamp(aspectFraction, 0.01f, 1f);
            float aspect = windowHeight > 0
                ? Mathf.Max(0.01f, windowWidth / (float)windowHeight)
                : 1f;
            Vector2 centre = new Vector2(
                0.5f + (viewportPoint.x - 0.5f) / fraction,
                viewportPoint.y);
            float fade = EyeFade(centre);
            if (viewportPoint.z <= 0f)
            {
                // Behind the lens: there is no on-screen eye to wind around.
                fade = 0f;
            }

            centre = new Vector2(
                Mathf.Clamp(centre.x, BoundaryMargin, 1f - BoundaryMargin),
                Mathf.Clamp(centre.y, BoundaryMargin, 1f - BoundaryMargin));
            float twist = twistRadians * fade;
            Vector2 core = corePixels * fade;
            vertigo = new Vector4(centre.x, centre.y, aspect, twist);
            shape = new Vector4(
                InnerRadius,
                RadialPull,
                core.x,
                core.y);
            return twist != 0f || core.sqrMagnitude > 0f;
        }

        /// <summary>
        /// 1 while the eye is well inside the frame, 0 by its edge.
        /// </summary>
        public static float EyeFade(Vector2 centre)
        {
            float distance = Mathf.Max(
                Mathf.Abs(centre.x - 0.5f),
                Mathf.Abs(centre.y - 0.5f));
            return 1f - Mathf.InverseLerp(
                EyeFadeStart,
                EyeFadeEnd,
                distance);
        }

        /// <summary>
        /// The radius of the frame corner farthest from the eye. The profile
        /// is normalised on it so 16:9, 4:3 and an off-centre eye all reach
        /// the full twist exactly at their own corner — normalising on the
        /// inscribed circle instead would push the whole outer ring off the
        /// source image.
        /// </summary>
        public static float FarthestCornerRadius(Vector2 centre, float aspect)
        {
            Vector2 far = new Vector2(
                Mathf.Max(centre.x, 1f - centre.x) * aspect,
                Mathf.Max(centre.y, 1f - centre.y));
            return Mathf.Max(far.magnitude, InnerRadius + 0.001f);
        }

        /// <summary>
        /// How much of the twist a point at <paramref name="radius"/> takes:
        /// exactly nothing on the hero's disc, all of it at the corner, with
        /// zero slope at the disc's rim so no ring shows.
        /// </summary>
        public static float Profile(float radius, float outerRadius)
        {
            float t = Mathf.Clamp01(
                (radius - InnerRadius) /
                Mathf.Max(0.001f, outerRadius - InnerRadius));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// The mirror of the shader's <c>ApplyVertigoWhirlpool</c>: where the
        /// composite reads the frame for the target pixel at
        /// <paramref name="uv"/>. Still water returns the input untouched,
        /// which is what keeps every sober frame bit-exact.
        /// </summary>
        public static Vector2 Warp(
            Vector2 uv,
            Vector4 vertigo,
            Vector4 shape,
            Vector2 internalTexelSize)
        {
            float twist = vertigo.w;
            Vector2 core = new Vector2(shape.z, shape.w);
            if (twist == 0f && core.sqrMagnitude == 0f)
            {
                return uv;
            }

            Vector2 centre = new Vector2(vertigo.x, vertigo.y);
            float aspect = vertigo.z;
            float inner = shape.x;
            float pull = shape.y;

            Vector2 p = new Vector2(
                (uv.x - centre.x) * aspect,
                uv.y - centre.y);
            float radius = p.magnitude;
            float outer = FarthestCornerRadius(centre, aspect);
            float profile = Profile(radius, outer);

            float angle = twist * profile;
            float sine = Mathf.Sin(angle);
            float cosine = Mathf.Cos(angle);
            Vector2 rotated = new Vector2(
                p.x * cosine - p.y * sine,
                p.x * sine + p.y * cosine);
            rotated *= 1f - pull * profile;

            Vector2 delta = new Vector2(rotated.x / aspect, rotated.y);
            float coreMask = Mathf.Clamp01(1f - radius / inner);
            delta += new Vector2(
                core.x * internalTexelSize.x,
                core.y * internalTexelSize.y) * coreMask;

            float wallX = delta.x >= 0f
                ? 1f - BoundaryMargin - centre.x
                : centre.x - BoundaryMargin;
            float wallY = delta.y >= 0f
                ? 1f - BoundaryMargin - centre.y
                : centre.y - BoundaryMargin;
            float scale = Mathf.Min(
                1f,
                Mathf.Min(
                    wallX / (Mathf.Abs(delta.x) + 1e-6f),
                    wallY / (Mathf.Abs(delta.y) + 1e-6f)));
            return centre + delta * scale;
        }
    }
}
