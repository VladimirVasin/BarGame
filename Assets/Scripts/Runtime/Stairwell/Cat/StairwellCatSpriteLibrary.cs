using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// Slices the cat atlas once and shares its 32 point-filtered sprites.
    /// The first three rows are authored top-first as LookLeft, Center and
    /// LookRight. The bottom row contains the direction-independent groom.
    /// </summary>
    public sealed class StairwellCatSpriteLibrary : IDisposable
    {
        public const string DefaultResourcePath =
            "Stairwell/Cat/StairwellCatAtlas";
        public const int Columns = 8;
        public const int DirectionalRows = 3;
        public const int GroomRow = 3;
        public const int Rows = 4;
        public const int FrameWidth = 64;
        public const int FrameHeight = 64;
        public const int FrameCount = Columns * Rows;
        public const float PixelsPerUnit = 96f;
        public const float PivotXPixels = 32f;
        public const float PivotYPixels = 4f;

        private static StairwellCatSpriteLibrary defaultLibrary;

        private readonly List<Sprite> sprites;
        private bool disposed;

        private StairwellCatSpriteLibrary(
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

        public static StairwellCatSpriteLibrary Create(
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
                    "The stairwell cat atlas must be exactly " +
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
                            ((rowFromTop + 1) *
                             FrameHeight);
                    for (int column = 0;
                         column < Columns;
                         column++)
                    {
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
                        string rowName =
                            rowFromTop < DirectionalRows
                                ? ((StairwellCatLook)rowFromTop)
                                    .ToString()
                                : "Groom";
                        sprite.name =
                            $"StairwellCat_{rowName}_{column}";
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

            return new StairwellCatSpriteLibrary(
                atlas,
                generatedSprites);
        }

        public static bool TryLoadDefault(
            out StairwellCatSpriteLibrary library)
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

        public static StairwellCatSpriteLibrary LoadDefault()
        {
            if (TryLoadDefault(
                    out StairwellCatSpriteLibrary library))
            {
                return library;
            }

            throw new InvalidOperationException(
                "The stairwell cat atlas was not found at " +
                $"Resources/{DefaultResourcePath}.");
        }

        public Sprite GetSprite(
            StairwellCatLook look,
            int frame)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(StairwellCatSpriteLibrary));
            }

            int row = (int)look;
            if (row < 0 || row >= DirectionalRows)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(look),
                    look,
                    "Look must select one of the three atlas rows.");
            }

            if (frame < 0 || frame >= Columns)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frame),
                    frame,
                    $"Frame must be in 0..{Columns - 1}.");
            }

            return sprites[(row * Columns) + frame];
        }

        public Sprite GetGroomSprite(int frame)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(StairwellCatSpriteLibrary));
            }

            if (frame < 0 || frame >= Columns)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frame),
                    frame,
                    $"Frame must be in 0..{Columns - 1}.");
            }

            return sprites[(GroomRow * Columns) + frame];
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
