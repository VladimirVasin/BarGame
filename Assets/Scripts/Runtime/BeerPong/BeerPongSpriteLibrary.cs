using System;
using UnityEngine;

namespace BarPromenade
{
    public enum BeerPongSpriteId
    {
        Ball = 0,
        BallImpact = 1,
        BallShadow = 2,
        HandHolding = 3,
        HandRelease = 4,
        HandGesture = 5,
        Cup = 6,
        CupWobble = 7,
        CupSplash = 8,
        CupEmpty = 9,
        Aim = 10,
        Dust = 11,
        RimSpark = 12,
        Impact = 13,
        OpponentIdle = 14,
        OpponentReact = 15
    }

    public static class BeerPongSpriteLibrary
    {
        public const string BackgroundResourcePath =
            "BeerPong/BeerPongBackground";
        public const string AtlasResourcePath =
            "BeerPong/BeerPongAtlas";
        public const int AtlasColumns = 4;
        public const int AtlasRows = 4;

        private static Texture2D background;
        private static Texture2D atlas;

        public static Texture2D Background
        {
            get
            {
                if (background == null)
                {
                    background = LoadPointTexture(
                        BackgroundResourcePath);
                }

                return background;
            }
        }

        public static Texture2D Atlas
        {
            get
            {
                if (atlas == null)
                {
                    atlas = LoadPointTexture(AtlasResourcePath);
                }

                return atlas;
            }
        }

        public static bool IsAvailable =>
            Background != null && Atlas != null;

        public static Rect GetUv(BeerPongSpriteId sprite)
        {
            int index = (int)sprite;
            int cellCount = AtlasColumns * AtlasRows;
            if (index < 0 || index >= cellCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sprite),
                    sprite,
                    null);
            }

            int column = index % AtlasColumns;
            int topRow = index / AtlasColumns;
            float width = 1f / AtlasColumns;
            float height = 1f / AtlasRows;
            return new Rect(
                column * width,
                (AtlasRows - topRow - 1) * height,
                width,
                height);
        }

        public static void DrawBackground(Rect rect, Color tint)
        {
            Texture2D texture = Background;
            if (texture == null)
            {
                return;
            }

            DrawTexture(rect, texture, tint);
        }

        public static void Draw(
            Rect rect,
            BeerPongSpriteId sprite,
            Color tint)
        {
            Texture2D texture = Atlas;
            if (texture == null)
            {
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(
                RetroUiTheme.SnapRect(rect),
                texture,
                GetUv(sprite),
                true);
            GUI.color = previousColor;
        }

        private static Texture2D LoadPointTexture(string resourcePath)
        {
            Texture2D texture =
                Resources.Load<Texture2D>(resourcePath);
            if (texture != null)
            {
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.anisoLevel = 0;
            }

            return texture;
        }

        private static void DrawTexture(
            Rect rect,
            Texture texture,
            Color tint)
        {
            Color previousColor = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(
                RetroUiTheme.SnapRect(rect),
                texture,
                ScaleMode.StretchToFill,
                true);
            GUI.color = previousColor;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedReferences()
        {
            background = null;
            atlas = null;
        }
    }
}
