using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// Slices the shared 3x2 full-body NPC atlas once. Actors keep references
    /// to these six sprites and never allocate per-instance sprites/materials.
    /// </summary>
    public sealed class BarNpcSpriteLibrary : IDisposable
    {
        public const string DefaultResourcePath =
            "Bar/Npc/BarNpcAtlas";
        public const int Columns = 3;
        public const int Rows = 2;
        public const int VariantCount = Columns * Rows;
        public const float PixelsPerUnit = 256f;
        public const float FeetPivotPixels = 0f;
        public const float LowerRowFeetPivotPixels = 37f;
        private const float AuthoredFrameHeight = 512f;

        private static BarNpcSpriteLibrary defaultLibrary;

        private readonly List<Sprite> sprites;
        private bool disposed;

        private BarNpcSpriteLibrary(
            Texture2D atlas,
            Material sharedMaterial,
            List<Sprite> generatedSprites)
        {
            Atlas = atlas;
            SharedMaterial = sharedMaterial;
            sprites = generatedSprites;
        }

        public Texture2D Atlas { get; }
        public Material SharedMaterial { get; }
        public IReadOnlyList<Sprite> Sprites => sprites;
        public bool IsDisposed => disposed;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDefaultLibrary()
        {
            defaultLibrary?.Dispose();
            defaultLibrary = null;
        }

        public static BarNpcSpriteLibrary Create(
            Texture2D atlas,
            Material sharedMaterial = null)
        {
            if (atlas == null)
            {
                throw new ArgumentNullException(nameof(atlas));
            }

            if (atlas.width <= 0 ||
                atlas.height <= 0 ||
                atlas.width % Columns != 0 ||
                atlas.height % Rows != 0)
            {
                throw new ArgumentException(
                    "The bar NPC atlas must be a non-empty 3x2 grid.",
                    nameof(atlas));
            }

            int frameWidth = atlas.width / Columns;
            int frameHeight = atlas.height / Rows;
            var generatedSprites = new List<Sprite>(VariantCount);
            try
            {
                for (int index = 0; index < VariantCount; index++)
                {
                    int column = index % Columns;
                    int rowFromTop = index / Columns;
                    int y = atlas.height -
                            ((rowFromTop + 1) * frameHeight);
                    float feetPivotPixels =
                        rowFromTop == 0
                            ? FeetPivotPixels
                            : frameHeight *
                              (LowerRowFeetPivotPixels /
                               AuthoredFrameHeight);
                    float pivotY = Mathf.Clamp(
                        feetPivotPixels / frameHeight,
                        0f,
                        1f);
                    Sprite sprite = Sprite.Create(
                        atlas,
                        new Rect(
                            column * frameWidth,
                            y,
                            frameWidth,
                            frameHeight),
                        new Vector2(0.5f, pivotY),
                        PixelsPerUnit,
                        0,
                        SpriteMeshType.FullRect);
                    sprite.name = $"BarNpcVariant{index + 1}";
                    sprite.hideFlags = HideFlags.DontSave;
                    generatedSprites.Add(sprite);
                }
            }
            catch
            {
                DestroySprites(generatedSprites);
                throw;
            }

            return new BarNpcSpriteLibrary(
                atlas,
                sharedMaterial,
                generatedSprites);
        }

        public static bool TryLoadDefault(
            out BarNpcSpriteLibrary library)
        {
            if (defaultLibrary != null &&
                !defaultLibrary.disposed)
            {
                library = defaultLibrary;
                return true;
            }

            Texture2D atlas = Resources.Load<Texture2D>(
                DefaultResourcePath);
            if (atlas == null)
            {
                library = null;
                return false;
            }

            defaultLibrary = Create(atlas);
            library = defaultLibrary;
            return true;
        }

        public static BarNpcSpriteLibrary LoadDefault()
        {
            if (TryLoadDefault(out BarNpcSpriteLibrary library))
            {
                return library;
            }

            throw new InvalidOperationException(
                "The bar NPC atlas was not found at Resources/" +
                $"{DefaultResourcePath}.");
        }

        public Sprite GetSprite(int variant)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(BarNpcSpriteLibrary));
            }

            if (variant < 0 || variant >= sprites.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(variant),
                    variant,
                    $"Variant must be in 0..{sprites.Count - 1}.");
            }

            return sprites[variant];
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            DestroySprites(sprites);
            sprites.Clear();
            if (ReferenceEquals(defaultLibrary, this))
            {
                defaultLibrary = null;
            }
        }

        private static void DestroySprites(
            IReadOnlyList<Sprite> generatedSprites)
        {
            for (int index = 0;
                 index < generatedSprites.Count;
                 index++)
            {
                Sprite sprite = generatedSprites[index];
                if (sprite == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Object.Destroy(sprite);
                }
                else
                {
                    Object.DestroyImmediate(sprite);
                }
            }
        }
    }
}
