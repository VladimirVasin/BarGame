using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityMapViewportTests
    {
        private static readonly Vector2 ViewportSize =
            new Vector2(423f, 311f);
        private static readonly Vector2 CellWorldSize =
            new Vector2(24f, 24f);

        [Test]
        public void Configure_EnablesOnlyAxesThatExceedReadableViewport()
        {
            var viewport = new CityMapViewport();

            viewport.Configure(
                new Rect(0f, 0f, 288f, 336f),
                CellWorldSize,
                ViewportSize,
                22f);
            Assert.That(viewport.CanScrollHorizontal, Is.False);
            Assert.That(viewport.CanScrollVertical, Is.False);

            viewport.Configure(
                new Rect(0f, 0f, 480f, 336f),
                CellWorldSize,
                ViewportSize,
                22f);
            Assert.That(viewport.CanScrollHorizontal, Is.True);
            Assert.That(viewport.CanScrollVertical, Is.False);

            viewport.Configure(
                new Rect(0f, 0f, 288f, 432f),
                CellWorldSize,
                ViewportSize,
                22f);
            Assert.That(viewport.CanScrollHorizontal, Is.False);
            Assert.That(viewport.CanScrollVertical, Is.True);
        }

        [Test]
        public void ScrollAndFocus_AreClampedOnBothOverflowingAxes()
        {
            var viewport = new CityMapViewport();
            viewport.Configure(
                new Rect(-240f, -216f, 480f, 432f),
                CellWorldSize,
                ViewportSize,
                22f);

            Assert.That(viewport.CanScrollHorizontal, Is.True);
            Assert.That(viewport.CanScrollVertical, Is.True);
            Assert.That(
                viewport.ContentSize.x,
                Is.EqualTo(440f).Within(0.001f));
            Assert.That(
                viewport.ContentSize.y,
                Is.EqualTo(396f).Within(0.001f));

            viewport.CenterOnWorld(Vector3.zero, new Rect(
                -240f,
                -216f,
                480f,
                432f));
            Assert.That(
                viewport.ScrollOffset,
                Is.EqualTo(new Vector2(8.5f, 42.5f)));

            viewport.ScrollBy(new Vector2(-1000f, 1000f));
            Assert.That(
                viewport.ScrollOffset,
                Is.EqualTo(new Vector2(0f, 85f)));
            Assert.That(
                viewport.ContentRect.position,
                Is.EqualTo(new Vector2(0f, -85f)));
        }
    }
}
