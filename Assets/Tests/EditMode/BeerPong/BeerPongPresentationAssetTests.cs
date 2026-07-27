using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests
{
    public sealed class BeerPongPresentationAssetTests
    {
        [Test]
        public void BackgroundAndAtlas_AreLoadablePixelArtResources()
        {
            Texture2D background = Resources.Load<Texture2D>(
                "BeerPong/BeerPongBackground");
            Texture2D atlas = Resources.Load<Texture2D>(
                "BeerPong/BeerPongAtlas");

            Assert.That(background, Is.Not.Null);
            Assert.That(background.width, Is.EqualTo(640));
            Assert.That(background.height, Is.EqualTo(360));
            Assert.That(background.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(background.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));

            Assert.That(atlas, Is.Not.Null);
            Assert.That(atlas.width, Is.EqualTo(1024));
            Assert.That(atlas.height, Is.EqualTo(1024));
            Assert.That(atlas.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(atlas.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
        }
    }
}
