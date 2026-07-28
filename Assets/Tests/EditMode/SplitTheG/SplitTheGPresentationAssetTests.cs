using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests
{
    public sealed class SplitTheGPresentationAssetTests
    {
        [Test]
        public void BackgroundAndAtlas_AreLoadablePixelArtResources()
        {
            Texture2D background = Resources.Load<Texture2D>(
                SplitTheGSpriteLibrary.BackgroundResourcePath);
            Texture2D atlas = Resources.Load<Texture2D>(
                SplitTheGSpriteLibrary.AtlasResourcePath);

            Assert.That(background, Is.Not.Null);
            Assert.That(background.width, Is.EqualTo(640));
            Assert.That(background.height, Is.EqualTo(360));
            Assert.That(
                background.filterMode,
                Is.EqualTo(FilterMode.Point));
            Assert.That(
                background.wrapMode,
                Is.EqualTo(TextureWrapMode.Clamp));

            Assert.That(atlas, Is.Not.Null);
            Assert.That(atlas.width, Is.EqualTo(512));
            Assert.That(atlas.height, Is.EqualTo(512));
            Assert.That(atlas.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(
                atlas.wrapMode,
                Is.EqualTo(TextureWrapMode.Clamp));
        }

        [Test]
        public void SpriteUvCells_CoverTheAtlasWithoutLeavingBounds()
        {
            var occupied = new bool[
                SplitTheGSpriteLibrary.AtlasColumns,
                SplitTheGSpriteLibrary.AtlasRows];

            foreach (SplitTheGSpriteId sprite in
                     Enum.GetValues(typeof(SplitTheGSpriteId)))
            {
                Rect uv = SplitTheGSpriteLibrary.GetUv(sprite);
                Assert.That(uv.xMin, Is.GreaterThanOrEqualTo(0f));
                Assert.That(uv.yMin, Is.GreaterThanOrEqualTo(0f));
                Assert.That(uv.xMax, Is.LessThanOrEqualTo(1f));
                Assert.That(uv.yMax, Is.LessThanOrEqualTo(1f));
                Assert.That(
                    uv.width,
                    Is.EqualTo(
                        1f / SplitTheGSpriteLibrary.AtlasColumns));
                Assert.That(
                    uv.height,
                    Is.EqualTo(
                        1f / SplitTheGSpriteLibrary.AtlasRows));

                int column = Mathf.RoundToInt(
                    uv.x *
                    SplitTheGSpriteLibrary.AtlasColumns);
                int bottomRow = Mathf.RoundToInt(
                    uv.y *
                    SplitTheGSpriteLibrary.AtlasRows);
                Assert.That(occupied[column, bottomRow], Is.False);
                occupied[column, bottomRow] = true;
            }

            for (int column = 0;
                 column < SplitTheGSpriteLibrary.AtlasColumns;
                 column++)
            {
                for (int row = 0;
                     row < SplitTheGSpriteLibrary.AtlasRows;
                     row++)
                {
                    Assert.That(occupied[column, row], Is.True);
                }
            }
        }

        [Test]
        public void InvalidSpriteId_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SplitTheGSpriteLibrary.GetUv(
                    (SplitTheGSpriteId)(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SplitTheGSpriteLibrary.GetUv(
                    (SplitTheGSpriteId)16));
        }
    }
}
