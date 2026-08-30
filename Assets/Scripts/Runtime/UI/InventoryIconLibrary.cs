using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static class InventoryIconLibrary
    {
        private static readonly Dictionary<InventoryItemId, Texture2D> Icons =
            new Dictionary<InventoryItemId, Texture2D>();
        private static Texture2D heroPortrait;

        internal const string HeroPortraitResourcePath =
            "Player/Player3DV2Portrait";

        internal static readonly Rect HeroPortraitUv =
            new Rect(0f, 0f, 1f, 1f);

        public static Texture2D GetIcon(InventoryItemId itemId)
        {
            if (Icons.TryGetValue(itemId, out Texture2D texture) &&
                texture != null)
            {
                return texture;
            }

            texture = CreateItemIcon(itemId);
            Icons[itemId] = texture;
            return texture;
        }

        public static Texture2D GetHeroPortrait()
        {
            if (heroPortrait == null)
            {
                heroPortrait = Resources.Load<Texture2D>(
                    HeroPortraitResourcePath);
                if (heroPortrait == null)
                {
                    throw new System.InvalidOperationException(
                        "Missing dedicated 3D player portrait at Resources/" +
                        HeroPortraitResourcePath + ".");
                }
            }

            return heroPortrait;
        }

        private static Texture2D CreateItemIcon(InventoryItemId itemId)
        {
            var painter = new PixelPainter(32, 32);
            Color32 ink = new Color32(27, 25, 29, 255);
            Color32 pale = new Color32(224, 216, 190, 255);
            Color32 metal = new Color32(126, 142, 138, 255);
            Color32 amber = new Color32(190, 121, 48, 255);
            switch (itemId)
            {
                case InventoryItemId.ApartmentKeys:
                    painter.Ring(9, 18, 6, 3, metal);
                    painter.FillRect(13, 16, 13, 4, metal);
                    painter.FillRect(22, 12, 3, 5, metal);
                    painter.FillRect(26, 14, 3, 4, metal);
                    painter.OutlineRect(2, 7, 27, 18, ink);
                    break;
                case InventoryItemId.Lighter:
                    painter.FillRect(9, 6, 14, 17, amber);
                    painter.OutlineRect(8, 5, 16, 19, ink);
                    painter.FillRect(10, 21, 12, 4, metal);
                    painter.FillRect(17, 24, 5, 3, metal);
                    painter.FillRect(13, 25, 3, 2, pale);
                    painter.Set(15, 28, amber);
                    painter.Set(16, 27, amber);
                    painter.Set(16, 28, pale);
                    break;
                case InventoryItemId.VodkaBottle:
                    painter.FillRect(13, 22, 6, 5, metal);
                    painter.FillRect(11, 9, 10, 14, pale);
                    painter.OutlineRect(10, 8, 12, 16, ink);
                    painter.OutlineRect(12, 21, 8, 7, ink);
                    painter.FillRect(12, 12, 8, 5, amber);
                    break;
                case InventoryItemId.ChickenEgg:
                    painter.Ellipse(16, 16, 8, 11, pale);
                    painter.EllipseOutline(16, 16, 9, 12, ink);
                    painter.Set(12, 20, new Color32(246, 238, 211, 255));
                    break;
                case InventoryItemId.OpenStewCan:
                    painter.FillRect(8, 8, 16, 16, metal);
                    painter.OutlineRect(7, 7, 18, 18, ink);
                    painter.FillRect(9, 12, 14, 7, amber);
                    painter.FillRect(9, 23, 14, 2, pale);
                    painter.FillRect(9, 7, 14, 2, pale);
                    break;
                case InventoryItemId.ClosedStewCan:
                    painter.FillRect(8, 8, 16, 16, metal);
                    painter.OutlineRect(7, 7, 18, 18, ink);
                    painter.FillRect(9, 12, 14, 7, amber);
                    painter.FillRect(9, 23, 14, 2, pale);
                    painter.FillRect(9, 7, 14, 2, pale);
                    painter.OutlineRect(12, 20, 8, 3, ink);
                    break;
                case InventoryItemId.InstantNoodles:
                    painter.FillRect(5, 8, 22, 16, amber);
                    painter.OutlineRect(4, 7, 24, 18, ink);
                    painter.FillRect(8, 11, 16, 9, pale);
                    painter.FillRect(10, 14, 12, 2, ink);
                    painter.FillRect(6, 8, 2, 16, metal);
                    painter.FillRect(24, 8, 2, 16, metal);
                    break;
                case InventoryItemId.DayOldLoaf:
                    painter.FillRect(6, 9, 20, 14, amber);
                    painter.FillRect(8, 22, 16, 3, pale);
                    painter.OutlineRect(5, 8, 22, 16, ink);
                    painter.FillRect(11, 21, 2, 4, ink);
                    painter.FillRect(16, 21, 2, 4, ink);
                    painter.FillRect(21, 21, 2, 4, ink);
                    painter.FillRect(19, 9, 4, 14, metal);
                    break;
            }

            return painter.CreateTexture("Inventory Icon " + itemId);
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetResources()
        {
            foreach (Texture2D texture in Icons.Values)
            {
                DestroyTexture(texture);
            }

            Icons.Clear();
            heroPortrait = null;
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(texture);
            }
            else
            {
                Object.DestroyImmediate(texture);
            }
        }

        private sealed class PixelPainter
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

            public void Set(int x, int y, Color32 color)
            {
                if (x >= 0 && x < width && y >= 0 && y < height)
                {
                    pixels[y * width + x] = color;
                }
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
}
