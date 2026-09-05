using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The three hidden materials the hero's vomit is drawn with — the
    /// liquid, the lumps in it, and the film it leaves on whatever it hit —
    /// and the 32x32 slurry texture that film carries.
    ///
    /// All three sit on the project's own PS1 Lit, not on the unlit
    /// atmosphere material the smoke uses. A yellow-green stream that ignores
    /// the lights would glow like neon on a night street (the art bible bans
    /// glow) and would read at the wrong brightness under the black-and-white
    /// film mode. Lit and opaque, it is shaded by the same lamps as the floor
    /// it lands on, and needs no shader review of its own.
    /// </summary>
    public static class HeroVomitResources
    {
        public const string ShaderName = "Bar Promenade/PS1 Lit";
        public const int SlurryTextureSize = 32;
        public const float LiquidSmoothness = 0.75f;
        public const float ChunkSmoothness = 0.3f;
        public const float ResidueSmoothness = 0.65f;

        public static readonly Color LiquidColor =
            new Color(0.64f, 0.61f, 0.23f, 1f);
        public static readonly Color ChunkColor =
            new Color(0.24f, 0.20f, 0.09f, 1f);
        public static readonly Color PaleChunkColor =
            new Color(0.55f, 0.50f, 0.25f, 1f);

        private static readonly Color32 SlurryBase =
            new Color32(140, 128, 44, 255);
        private static readonly Color32 SlurryCell =
            new Color32(108, 98, 30, 255);
        private static readonly Color32 SlurryLump =
            new Color32(58, 48, 20, 255);
        private static readonly Color32 SlurryCrumb =
            new Color32(176, 166, 96, 255);

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");

        private static Material liquidMaterial;
        private static Material chunkMaterial;
        private static Material residueMaterial;
        private static Texture2D slurryTexture;

        /// <summary>The stream rods and the splash droplets.</summary>
        public static Material LiquidMaterial
        {
            get
            {
                if (liquidMaterial == null)
                {
                    liquidMaterial = CreateMaterial(
                        "Hero Vomit Liquid (Shared)",
                        LiquidColor,
                        LiquidSmoothness,
                        null);
                }

                return liquidMaterial;
            }
        }

        /// <summary>The dark lumps, in flight and on the ground.</summary>
        public static Material ChunkMaterial
        {
            get
            {
                if (chunkMaterial == null)
                {
                    chunkMaterial = CreateMaterial(
                        "Hero Vomit Chunk (Shared)",
                        ChunkColor,
                        ChunkSmoothness,
                        null);
                }

                return chunkMaterial;
            }
        }

        /// <summary>
        /// The film on the floor. White tint: every colour comes from the
        /// slurry texture, so the residue mesh needs no property block.
        /// </summary>
        public static Material ResidueMaterial
        {
            get
            {
                if (residueMaterial == null)
                {
                    residueMaterial = CreateMaterial(
                        "Hero Vomit Residue (Shared)",
                        Color.white,
                        ResidueSmoothness,
                        SlurryTexture);
                }

                return residueMaterial;
            }
        }

        public static Texture2D SlurryTexture
        {
            get
            {
                if (slurryTexture == null)
                {
                    slurryTexture = CreateSlurryTexture();
                }

                return slurryTexture;
            }
        }

        private static Material CreateMaterial(
            string name,
            Color baseColor,
            float smoothness,
            Texture2D baseMap)
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Missing lit shader '{ShaderName}' for the hero vomit.");
            }

            var material = new Material(shader)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = false
            };
            material.SetColor(BaseColorId, baseColor);
            material.SetFloat(SmoothnessId, smoothness);
            if (baseMap != null)
            {
                material.SetTexture(BaseMapId, baseMap);
            }

            return material;
        }

        /// <summary>
        /// Point-sampled, tiled every 0.18 m by the residue mesh: a mottled
        /// ochre with darker cells, a few 2x2 dark lumps and pale crumbs. The
        /// pattern is a fixed arithmetic hash of the texel, so the texture is
        /// the same in every session and every capture.
        /// </summary>
        private static Texture2D CreateSlurryTexture()
        {
            var texture = new Texture2D(
                SlurryTextureSize,
                SlurryTextureSize,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = "Hero Vomit Slurry Shared",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                anisoLevel = 0
            };
            var pixels = new Color32[SlurryTextureSize * SlurryTextureSize];
            for (int y = 0; y < SlurryTextureSize; y++)
            {
                for (int x = 0; x < SlurryTextureSize; x++)
                {
                    pixels[y * SlurryTextureSize + x] = SlurryTexel(x, y);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>
        /// Exposed so a test can count the classes without reading the
        /// texture back. The lumps are keyed by the 2x2 block a texel sits
        /// in, which is what makes them lumps rather than salt.
        /// </summary>
        public static Color32 SlurryTexel(int x, int y)
        {
            int blockX = x / 2;
            int blockY = y / 2;
            int blockHash = blockX * 7 + blockY * 13 + blockX * blockY;
            if (blockHash % 10 == 0)
            {
                return SlurryLump;
            }

            int hash = x * 7 + y * 13 + x * y;
            if ((hash + 5) % 50 < 3)
            {
                return SlurryCrumb;
            }

            if (hash % 10 < 3)
            {
                return SlurryCell;
            }

            return SlurryBase;
        }

        // Domain reload is disabled: without this reset each play session
        // would leak another set of hidden materials and a texture, and the
        // cached references would point at destroyed objects.
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            DestroyCached(liquidMaterial);
            DestroyCached(chunkMaterial);
            DestroyCached(residueMaterial);
            DestroyCached(slurryTexture);
            liquidMaterial = null;
            chunkMaterial = null;
            residueMaterial = null;
            slurryTexture = null;
        }

        private static void DestroyCached(UnityEngine.Object cached)
        {
            if (cached == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(cached);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(cached);
            }
        }
    }
}
