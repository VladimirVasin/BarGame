using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A small pixel-art canvas: rectangles, rings and ellipses written
    /// into a <see cref="Color32"/> grid, then turned into a point-filtered
    /// texture. Row zero is the BOTTOM of the texture, as Unity stores it,
    /// so a painter's `y` grows upward. Shared by every procedural icon in
    /// the interface; a PNG per icon would be an asset per glyph where a
    /// dozen rectangles say the same thing.
    /// </summary>
    internal sealed class PixelPainter
    {
        private readonly int width;
        private readonly int height;
        private readonly Color32[] pixels;

        public PixelPainter(int newWidth, int newHeight)
        {
            width = newWidth;
            height = newHeight;
            pixels = new Color32[width * height];
        }

        public int Width => width;
        public int Height => height;

        public void Set(int x, int y, Color32 color)
        {
            if (x >= 0 && x < width && y >= 0 && y < height)
            {
                pixels[y * width + x] = color;
            }
        }

        public Color32 Get(int x, int y)
        {
            return x >= 0 && x < width && y >= 0 && y < height
                ? pixels[y * width + x]
                : default;
        }

        public void FillRect(int x, int y, int w, int h, Color32 color)
        {
            for (int py = y; py < y + h; py++)
            {
                for (int px = x; px < x + w; px++)
                {
                    Set(px, py, color);
                }
            }
        }

        public void OutlineRect(int x, int y, int w, int h, Color32 color)
        {
            FillRect(x, y, w, 1, color);
            FillRect(x, y + h - 1, w, 1, color);
            FillRect(x, y, 1, h, color);
            FillRect(x + w - 1, y, 1, h, color);
        }

        public void Ring(
            int centerX,
            int centerY,
            int outerRadius,
            int innerRadius,
            Color32 color)
        {
            int outerSquared = outerRadius * outerRadius;
            int innerSquared = innerRadius * innerRadius;
            for (int y = -outerRadius; y <= outerRadius; y++)
            {
                for (int x = -outerRadius; x <= outerRadius; x++)
                {
                    int distance = x * x + y * y;
                    if (distance <= outerSquared &&
                        distance >= innerSquared)
                    {
                        Set(centerX + x, centerY + y, color);
                    }
                }
            }
        }

        public void Ellipse(
            int centerX,
            int centerY,
            int radiusX,
            int radiusY,
            Color32 color)
        {
            for (int y = -radiusY; y <= radiusY; y++)
            {
                for (int x = -radiusX; x <= radiusX; x++)
                {
                    float normalized =
                        x * x / (float)(radiusX * radiusX) +
                        y * y / (float)(radiusY * radiusY);
                    if (normalized <= 1f)
                    {
                        Set(centerX + x, centerY + y, color);
                    }
                }
            }
        }

        public void EllipseOutline(
            int centerX,
            int centerY,
            int radiusX,
            int radiusY,
            Color32 color)
        {
            for (int y = -radiusY; y <= radiusY; y++)
            {
                for (int x = -radiusX; x <= radiusX; x++)
                {
                    float normalized =
                        x * x / (float)(radiusX * radiusX) +
                        y * y / (float)(radiusY * radiusY);
                    if (normalized >= 0.78f && normalized <= 1.12f)
                    {
                        Set(centerX + x, centerY + y, color);
                    }
                }
            }
        }

        public Texture2D CreateTexture(string textureName)
        {
            var texture = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                false)
            {
                name = textureName,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }
    }
}
