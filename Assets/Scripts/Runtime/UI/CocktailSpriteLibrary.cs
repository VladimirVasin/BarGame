using System;
using UnityEngine;

namespace BarPromenade
{
    public static class CocktailSpriteLibrary
    {
        public const string ResourcePath =
            "Cocktails/CocktailSpriteAtlas";

        private const int AtlasColumns = 4;
        private const int AtlasRows = 4;
        private static Texture2D atlas;

        public static Texture2D Atlas
        {
            get
            {
                if (atlas == null)
                {
                    atlas = Resources.Load<Texture2D>(ResourcePath);
                }

                return atlas;
            }
        }

        public static bool IsAvailable => Atlas != null;

        public static Rect GlassUv => CellUv(0);

        public static Rect GetIngredientUv(CocktailIngredientId ingredientId)
        {
            switch (ingredientId)
            {
                case CocktailIngredientId.Beer:
                    return CellUv(1);
                case CocktailIngredientId.Wine:
                    return CellUv(2);
                case CocktailIngredientId.Vodka:
                    return CellUv(3);
                case CocktailIngredientId.Cognac:
                    return CellUv(4);
                case CocktailIngredientId.Tonic:
                    return CellUv(5);
                case CocktailIngredientId.Soda:
                    return CellUv(6);
                case CocktailIngredientId.Cola:
                    return CellUv(7);
                case CocktailIngredientId.Orange:
                    return CellUv(8);
                case CocktailIngredientId.Lemon:
                    return CellUv(9);
                case CocktailIngredientId.GingerAle:
                    return CellUv(10);
                case CocktailIngredientId.Honey:
                    return CellUv(11);
                case CocktailIngredientId.Mint:
                    return CellUv(12);
                case CocktailIngredientId.Berries:
                    return CellUv(13);
                case CocktailIngredientId.Cherry:
                    return CellUv(14);
                case CocktailIngredientId.Ice:
                    return CellUv(15);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(ingredientId),
                        ingredientId,
                        null);
            }
        }

        public static void DrawGlass(Rect rect, Color tint)
        {
            DrawCell(rect, GlassUv, tint);
        }

        public static void DrawIngredient(
            Rect rect,
            CocktailIngredientId ingredientId,
            Color tint)
        {
            DrawCell(rect, GetIngredientUv(ingredientId), tint);
        }

        private static void DrawCell(Rect rect, Rect uv, Color tint)
        {
            Texture2D texture = Atlas;
            if (texture == null)
            {
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(rect, texture, uv, true);
            GUI.color = previousColor;
        }

        private static Rect CellUv(int topToBottomIndex)
        {
            int column = topToBottomIndex % AtlasColumns;
            int topRow = topToBottomIndex / AtlasColumns;
            float width = 1f / AtlasColumns;
            float height = 1f / AtlasRows;
            return new Rect(
                column * width,
                (AtlasRows - topRow - 1) * height,
                width,
                height);
        }
    }
}
