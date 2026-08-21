using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Owns the packaged mountain-rock recipe, the one shared fog-safe ridge
    /// material and the ordinary shared primitive treatment retained by
    /// close tunnel pieces. The measured constants come from
    /// `tools/build-city-mountain-textures.py` and its manifest.
    ///
    /// Physical ridge meshes use distance/height UVs baked at the recipe
    /// pitch (and combined tunnel boxes use the shared box projection), while
    /// small rock primitives can use the per-renderer transform path. Every
    /// variant keeps texture, tint and surface response in a property block.
    /// </summary>
    internal static class CityMountainSurfaceAppearance
    {
        public const string RockTextureResourcePath =
            "Textures/CityMountainRockAlbedo";
        public const string PhysicalShaderResourcePath =
            "Shaders/CityMountainPhysical";
        public const float MetersPerTile = 6.0f;
        public const float PhysicalVisibilityFloor = 0.10f;
        public const float NativeFogNearDistance = 9f;
        public const float NativeFogFarDistance = 12f;
        public const float PhysicalHandoffNearDistance = 31f;
        public const float PhysicalHandoffFarDistance = 43f;
        private const float MinimumUvScale = 0.35f;
        private const int HashSalt = 7000;

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

        private static Texture2D cachedRockTexture;
        private static Material physicalRidgeMaterial;

        internal static Material PhysicalRidgeMaterial
        {
            get
            {
                if (physicalRidgeMaterial == null)
                {
                    Shader shader = Resources.Load<Shader>(
                        PhysicalShaderResourcePath);
                    if (shader == null || !shader.isSupported)
                    {
                        throw new InvalidOperationException(
                            "Missing or unsupported physical mountain " +
                            $"shader '{PhysicalShaderResourcePath}'.");
                    }

                    physicalRidgeMaterial = new Material(shader)
                    {
                        name = "City Mountain Physical (Shared)",
                        hideFlags = HideFlags.HideAndDontSave,
                        enableInstancing = true
                    };
                    physicalRidgeMaterial.SetColor(
                        HazeColorId,
                        RuntimeSceneSetup.CityFogColor);
                    physicalRidgeMaterial.SetFloat(
                        FogDensityId,
                        RuntimeSceneSetup.CityFogDensity);
                    physicalRidgeMaterial.SetFloat(
                        VisibilityFloorId,
                        PhysicalVisibilityFloor);
                    physicalRidgeMaterial.SetFloat(
                        NativeFogNearId,
                        NativeFogNearDistance);
                    physicalRidgeMaterial.SetFloat(
                        NativeFogFarId,
                        NativeFogFarDistance);
                    physicalRidgeMaterial.SetFloat(
                        HandoffNearId,
                        PhysicalHandoffNearDistance);
                    physicalRidgeMaterial.SetFloat(
                        HandoffFarId,
                        PhysicalHandoffFarDistance);
                }

                return physicalRidgeMaterial;
            }
        }

        public static HomeSurfaceRecipe GetRecipe()
        {
            return new HomeSurfaceRecipe(
                RockTextureResourcePath,
                MetersPerTile,
                0.025f,
                0f,
                1.4065f);
        }

        public static Texture2D GetTexture()
        {
            if (cachedRockTexture == null)
            {
                cachedRockTexture = Resources.Load<Texture2D>(
                    RockTextureResourcePath);
            }

            if (cachedRockTexture == null)
            {
                throw new InvalidOperationException(
                    "Missing mountain rock surface texture " +
                    $"'{RockTextureResourcePath}'.");
            }

            return cachedRockTexture;
        }

        /// <summary>
        /// Textures a primitive, choosing the box projection from its shape.
        /// </summary>
        public static void Apply(Renderer renderer, Color sourceTint)
        {
            if (renderer == null)
            {
                return;
            }

            Apply(
                renderer,
                SurfaceAppearanceCore.ResolveBoxProjection(renderer),
                sourceTint);
        }

        public static void Apply(
            Renderer renderer,
            SurfaceProjection projection,
            Color sourceTint)
        {
            if (renderer == null)
            {
                return;
            }

            HomeSurfaceRecipe recipe = GetRecipe();
            ApplySharedProperties(renderer, sourceTint, recipe);
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetVector(
                BaseMapTransformId,
                SurfaceAppearanceCore.CreateBaseMapTransform(
                    renderer,
                    recipe.MetersPerTile,
                    MinimumUvScale,
                    projection,
                    HashSalt));
            renderer.SetPropertyBlock(properties);
        }

        /// <summary>
        /// Textures a combined mesh whose metre-scale UVs were already baked
        /// at the recipe's pitch.
        /// </summary>
        public static void ApplyCombined(
            Renderer renderer,
            Color sourceTint)
        {
            if (renderer == null)
            {
                return;
            }

            ApplySharedProperties(renderer, sourceTint, GetRecipe());
        }

        /// <summary>
        /// Applies the fog-safe physical-ridge material. Its opaque dither
        /// hands the camera-relative silhouette to real geometry without a
        /// depth-buffer gap, while tunnel pieces retain the ordinary shared
        /// primitive material used by other close-range city props.
        /// </summary>
        public static void ApplyPhysicalRidge(
            Renderer renderer,
            Color sourceTint)
        {
            if (renderer == null)
            {
                return;
            }

            ApplySharedProperties(
                renderer,
                sourceTint,
                GetRecipe(),
                PhysicalRidgeMaterial);
        }

        internal static float EvaluatePhysicalFogVisibility(
            float cameraDistance)
        {
            float distance = Mathf.Max(0f, cameraDistance);
            float fogTerm = RuntimeSceneSetup.CityFogDensity * distance;
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
                Mathf.Max(nativeVisibility, PhysicalVisibilityFloor),
                blend);
        }

        internal static Color CreateDisplayTint(Color sourceTint)
        {
            return SurfaceAppearanceCore.CreateDisplayTint(
                sourceTint,
                GetRecipe().AlbedoCompensation);
        }

        private static void ApplySharedProperties(
            Renderer renderer,
            Color sourceTint,
            HomeSurfaceRecipe recipe,
            Material sharedMaterial = null)
        {
            renderer.sharedMaterial = sharedMaterial != null
                ? sharedMaterial
                : RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, GetTexture());
            Color displayTint = SurfaceAppearanceCore.CreateDisplayTint(
                sourceTint,
                recipe.AlbedoCompensation);
            properties.SetColor(BaseColorId, displayTint);
            properties.SetColor(ColorId, displayTint);
            properties.SetFloat(SmoothnessId, recipe.Smoothness);
            properties.SetFloat(MetallicId, recipe.Metallic);
            renderer.SetPropertyBlock(properties);
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources()
        {
            cachedRockTexture = null;
            if (physicalRidgeMaterial != null)
            {
                UnityEngine.Object.Destroy(physicalRidgeMaterial);
                physicalRidgeMaterial = null;
            }
        }
    }
}
