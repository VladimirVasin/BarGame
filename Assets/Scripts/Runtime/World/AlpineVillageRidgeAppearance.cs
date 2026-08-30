using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one material the village's enclosing wall wears, and the numbers
    /// that keep it a present mass through the storm wave.
    ///
    /// The rise is the second submesh of the same ground mesh the hero walks
    /// on, but it cannot share the floor's material: on plain Exp2 fog a
    /// wall `85 m` away is at `12 %` between gusts and `5e-8` at a crest, so
    /// the bowl that was built to loom would vanish exactly when the storm
    /// is at its strongest. `Shaders/CityMountainPhysical` is the game's
    /// answer to that already - native Exp2 close in, a visibility floor
    /// beyond `NativeFogFar`, an ordered dither handing the mass to the haze
    /// just inside the far plane - and the City hard-codes its haze into
    /// `CityMountainSurfaceAppearance`, so the village needs its own owner
    /// with its own colour, its own breathing density and its own band.
    ///
    /// Two things follow from the shader and are recorded here rather than
    /// "fixed". It has no ShadowCaster pass, so the rise casts no shadow
    /// while the floor next to it still does; a `50 m` wall can therefore
    /// never shade the lane at low sun. And it does not snap vertices to the
    /// composite grid the way `Ps1Lit` does, so the toe seam is buried by
    /// the mesh builder instead of relying on the two sides landing on the
    /// same pixel.
    /// </summary>
    internal static class AlpineVillageRidgeAppearance
    {
        /// <summary>
        /// What the wall keeps of itself however thick the haze. `0.10` is
        /// the City's, where a painted shell stands behind the rock and
        /// carries the silhouette; here nothing stands behind the wall, so
        /// the wall itself has to be the mass. The first capture at `0.12`
        /// showed a band nobody would call a mountain - a shade on the haze
        /// at the edge of noticing - so the floor is what it takes to read
        /// as a looming wall on a pale amber haze at `640x360`, through the
        /// crest of a gust as well as between them.
        /// </summary>
        public const float VisibilityFloor = 0.30f;

        /// <summary>Inside this the wall is on native Exp2, like the ground
        /// it meets; the floor blends in over the next three metres.</summary>
        public const float NativeFogNearDistance = 9f;

        public const float NativeFogFarDistance = 12f;

        /// <summary>
        /// The dither band, in horizontal metres, that hands the wall to the
        /// haze before the `110 m` plane can cut it. It starts beyond every
        /// crest the plan produces (the farthest, mid-lane toward the far
        /// side, stands at `88 m`; the platform's sideways crest at `99 m`),
        /// so the silhouette edge that makes the bowl loom is never inside
        /// the dither - the City learned that a band across the crest
        /// dissolves it mid-air. At `96 m` and beyond the wall is at its
        /// floor, so the dots are haze-coloured and read as haze.
        /// </summary>
        public const float HandoffNearDistance = 96f;

        public const float HandoffFarDistance = 108f;

        /// <summary>
        /// The one sheet the whole village ground wears, on both submeshes:
        /// §10g raises no new surface family, and one sheet across the toe
        /// is what makes the seam a change of slope rather than a change of
        /// material.
        /// </summary>
        public const MountainRoadSurfaceKind Surface =
            MountainRoadSurfaceKind.WindSnow;

        /// <summary>
        /// Snow in its own shadow: colder and darker than the amber haze
        /// `(0.575, 0.545, 0.495)` and the floor's `(0.695, 0.685, 0.655)`,
        /// two steps below the road's far snowy ring `(0.47, 0.52, 0.525)`.
        /// A wall lighter than the haze is a hole in the sky; a wall only a
        /// little darker is a smudge (the first pass at `(0.40, 0.44, 0.48)`
        /// was). This reads as a mass. Compensated for the sheet's mean
        /// before it is written.
        /// </summary>
        public static readonly Color SnowShadowTint =
            new Color(0.31f, 0.35f, 0.41f, 1f);

        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId =
            Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");
        private static readonly int HazeColorId =
            Shader.PropertyToID("_HazeColor");
        private static readonly int FogDensityId =
            Shader.PropertyToID("_FogDensity");
        private static readonly int VisibilityFloorId =
            Shader.PropertyToID("_VisibilityFloor");
        private static readonly int NativeFogNearId =
            Shader.PropertyToID("_NativeFogNear");
        private static readonly int NativeFogFarId =
            Shader.PropertyToID("_NativeFogFar");
        private static readonly int HandoffNearId =
            Shader.PropertyToID("_HandoffNear");
        private static readonly int HandoffFarId =
            Shader.PropertyToID("_HandoffFar");

        private static Material ridgeMaterial;

        /// <summary>
        /// The shared wall material. Created once, never instanced per
        /// renderer; the haze colour and density on it are the village's,
        /// and <see cref="SetHaze"/> keeps the density moving with the
        /// storm wave.
        /// </summary>
        internal static Material RidgeMaterial
        {
            get
            {
                if (ridgeMaterial == null)
                {
                    Shader shader = Resources.Load<Shader>(
                        CityMountainSurfaceAppearance
                            .PhysicalShaderResourcePath);
                    if (shader == null || !shader.isSupported)
                    {
                        throw new InvalidOperationException(
                            "Missing or unsupported physical mountain " +
                            "shader '" +
                            CityMountainSurfaceAppearance
                                .PhysicalShaderResourcePath +
                            "'.");
                    }

                    ridgeMaterial = new Material(shader)
                    {
                        name = "Alpine Village Ridge (Shared)",
                        hideFlags = HideFlags.HideAndDontSave,
                        enableInstancing = true
                    };
                    ridgeMaterial.SetColor(
                        HazeColorId,
                        RuntimeSceneSetup.AlpineVillageFogColor);
                    ridgeMaterial.SetFloat(
                        FogDensityId,
                        RuntimeSceneSetup.AlpineVillageFogDensity);
                    ridgeMaterial.SetFloat(
                        VisibilityFloorId,
                        VisibilityFloor);
                    ridgeMaterial.SetFloat(
                        NativeFogNearId,
                        NativeFogNearDistance);
                    ridgeMaterial.SetFloat(
                        NativeFogFarId,
                        NativeFogFarDistance);
                    ridgeMaterial.SetFloat(
                        HandoffNearId,
                        HandoffNearDistance);
                    ridgeMaterial.SetFloat(
                        HandoffFarId,
                        HandoffFarDistance);
                }

                return ridgeMaterial;
            }
        }

        /// <summary>
        /// Writes the wall's sheet, tint and response into the property
        /// block of one submesh. <paramref name="baseMapTransform"/> is the
        /// SAME `_BaseMap_ST` the floor received from its own apply - read
        /// back from the floor's block by the caller - so the sheet is
        /// continuous across the toe. The wall never invents its own pitch:
        /// the floor's is what the shipped ground looks like, and a second
        /// pitch at the seam would be the seam.
        /// </summary>
        public static void Apply(
            Renderer renderer,
            int materialIndex,
            Vector4 baseMapTransform)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            if (materialIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(materialIndex),
                    materialIndex,
                    "The ridge is always one indexed submesh.");
            }

            HomeSurfaceRecipe recipe =
                MountainRoadSurfaceAppearance.GetRecipe(Surface);
            Color displayTint = MountainRoadSurfaceAppearance
                .CreateDisplayTint(SnowShadowTint, Surface);
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties, materialIndex);
            properties.SetTexture(
                BaseMapId,
                MountainRoadSurfaceAppearance.GetTexture(Surface));
            properties.SetVector(BaseMapTransformId, baseMapTransform);
            properties.SetColor(BaseColorId, displayTint);
            properties.SetColor(ColorId, displayTint);
            properties.SetFloat(SmoothnessId, recipe.Smoothness);
            properties.SetFloat(MetallicId, recipe.Metallic);
            renderer.SetPropertyBlock(properties, materialIndex);
        }

        /// <summary>
        /// The per-frame half. The village root calls this right after it
        /// has written `RenderSettings`, with what it wrote, so the wall's
        /// own fog term is the scene's fog term on the same frame; a wall
        /// hazed on last minute's density against this frame's sky is a
        /// silhouette that comes and goes on its own.
        /// </summary>
        public static void SetHaze(Color hazeColor, float fogDensity)
        {
            Material material = RidgeMaterial;
            material.SetColor(HazeColorId, hazeColor);
            material.SetFloat(FogDensityId, Mathf.Max(0f, fogDensity));
        }

        /// <summary>
        /// Mirror of the shader's visibility term, for the tests: native
        /// Exp2 inside <see cref="NativeFogNearDistance"/>, never below
        /// <see cref="VisibilityFloor"/> past <see cref="NativeFogFarDistance"/>.
        /// </summary>
        internal static float EvaluateRidgeVisibility(
            float cameraDistance,
            float fogDensity)
        {
            float distance = Mathf.Max(0f, cameraDistance);
            float fogTerm = Mathf.Max(0f, fogDensity) * distance;
            float nativeVisibility = Mathf.Exp(-fogTerm * fogTerm);
            float blend = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    NativeFogNearDistance,
                    NativeFogFarDistance,
                    distance));
            return Mathf.Lerp(
                nativeVisibility,
                Mathf.Max(nativeVisibility, VisibilityFloor),
                blend);
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources()
        {
            if (ridgeMaterial != null)
            {
                UnityEngine.Object.Destroy(ridgeMaterial);
                ridgeMaterial = null;
            }
        }
    }
}
