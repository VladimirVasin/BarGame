using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// Slices the dedicated one-shot feeding atlas. Logical frames are
    /// authored left-to-right in the top PNG row, then the bottom row.
    /// </summary>
    public sealed class StairwellCatFeedingSpriteLibrary : IDisposable
    {
        public const string DefaultResourcePath =
            "Stairwell/Cat/StairwellCatFeedingAtlas";
        public const int Columns = 8;
        public const int Rows = 2;
        public const int FrameWidth = 64;
        public const int FrameHeight = 64;
        public const int FrameCount = Columns * Rows;
        public const float PixelsPerUnit = 96f;
        public const float PivotXPixels = 32f;
        public const float PivotYPixels = 4f;

        private static StairwellCatFeedingSpriteLibrary defaultLibrary;

        private readonly List<Sprite> sprites;
        private bool disposed;

        private StairwellCatFeedingSpriteLibrary(
            Texture2D atlas,
            List<Sprite> generatedSprites)
        {
            Atlas = atlas;
            sprites = generatedSprites;
        }

        public Texture2D Atlas { get; }
        public IReadOnlyList<Sprite> Sprites => sprites;
        public bool IsDisposed => disposed;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDefaultLibrary()
        {
            defaultLibrary?.Dispose();
            defaultLibrary = null;
        }

        public static StairwellCatFeedingSpriteLibrary Create(
            Texture2D atlas)
        {
            if (atlas == null)
            {
                throw new ArgumentNullException(nameof(atlas));
            }

            if (atlas.width != Columns * FrameWidth ||
                atlas.height != Rows * FrameHeight)
            {
                throw new ArgumentException(
                    "The stairwell cat feeding atlas must be exactly " +
                    $"{Columns * FrameWidth}x" +
                    $"{Rows * FrameHeight} pixels.",
                    nameof(atlas));
            }

            atlas.filterMode = FilterMode.Point;
            atlas.wrapMode = TextureWrapMode.Clamp;
            atlas.anisoLevel = 0;

            var generatedSprites =
                new List<Sprite>(FrameCount);
            try
            {
                for (int rowFromTop = 0;
                     rowFromTop < Rows;
                     rowFromTop++)
                {
                    int y = atlas.height -
                            ((rowFromTop + 1) * FrameHeight);
                    for (int column = 0;
                         column < Columns;
                         column++)
                    {
                        int frame =
                            (rowFromTop * Columns) + column;
                        Sprite sprite = Sprite.Create(
                            atlas,
                            new Rect(
                                column * FrameWidth,
                                y,
                                FrameWidth,
                                FrameHeight),
                            new Vector2(
                                PivotXPixels / FrameWidth,
                                PivotYPixels / FrameHeight),
                            PixelsPerUnit,
                            0,
                            SpriteMeshType.FullRect);
                        sprite.name =
                            $"StairwellCat_Feeding_{frame}";
                        sprite.hideFlags = HideFlags.DontSave;
                        generatedSprites.Add(sprite);
                    }
                }
            }
            catch
            {
                DestroySprites(generatedSprites);
                throw;
            }

            return new StairwellCatFeedingSpriteLibrary(
                atlas,
                generatedSprites);
        }

        public static bool TryLoadDefault(
            out StairwellCatFeedingSpriteLibrary library)
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

        public static StairwellCatFeedingSpriteLibrary LoadDefault()
        {
            if (TryLoadDefault(
                    out StairwellCatFeedingSpriteLibrary library))
            {
                return library;
            }

            throw new InvalidOperationException(
                "The stairwell cat feeding atlas was not found at " +
                $"Resources/{DefaultResourcePath}.");
        }

        public Sprite GetSprite(int frame)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(StairwellCatFeedingSpriteLibrary));
            }

            if (frame < 0 || frame >= FrameCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frame),
                    frame,
                    $"Frame must be in 0..{FrameCount - 1}.");
            }

            return sprites[frame];
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
