using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class BeerPongPresentationTests
    {
        [Test]
        public void Projection_MapsTableBoundsIntoBackgroundPerspective()
        {
            BeerPongTableLayout layout =
                BeerPongTableLayout.Default;
            var projection = new BeerPongProjection(layout);

            Vector2 nearCenter = projection.ProjectSurface(
                0f,
                layout.TableNearZ);
            Vector2 farCenter = projection.ProjectSurface(
                0f,
                layout.TableFarZ);
            Vector2 nearLeft = projection.ProjectSurface(
                -layout.TableHalfWidth,
                layout.TableNearZ);
            Vector2 farRight = projection.ProjectSurface(
                layout.TableHalfWidth,
                layout.TableFarZ);

            Assert.That(
                nearCenter,
                Is.EqualTo(new Vector2(
                    BeerPongProjection.TableCenterX,
                    BeerPongProjection.NearSurfaceY)));
            Assert.That(
                farCenter,
                Is.EqualTo(new Vector2(
                    BeerPongProjection.TableCenterX,
                    BeerPongProjection.FarSurfaceY)));
            Assert.That(
                nearLeft.x,
                Is.EqualTo(
                    BeerPongProjection.TableCenterX -
                    BeerPongProjection.NearHalfWidth)
                    .Within(0.001f));
            Assert.That(
                farRight.x,
                Is.EqualTo(
                    BeerPongProjection.TableCenterX +
                    BeerPongProjection.FarHalfWidth)
                    .Within(0.001f));
            Assert.That(
                projection.GetProjectedScale(
                    layout.TableFarZ),
                Is.LessThan(
                    projection.GetProjectedScale(
                        layout.TableNearZ)));
        }

        [Test]
        public void Projection_AimLandingRespondsToYawAndHeight()
        {
            var projection = new BeerPongProjection();

            Vector3 left = projection.CalculateLandingPoint(
                -12f,
                38f,
                0.55f);
            Vector3 center = projection.CalculateLandingPoint(
                0f,
                38f,
                0.55f);
            Vector3 right = projection.CalculateLandingPoint(
                12f,
                38f,
                0.55f);
            Vector2 surface = projection.ProjectSurface(
                center.x,
                center.z);
            Vector2 airborne = projection.Project(
                projection.CalculateBallisticPoint(
                    0f,
                    38f,
                    0.55f,
                    0.35f));

            Assert.That(left.x, Is.LessThan(center.x));
            Assert.That(right.x, Is.GreaterThan(center.x));
            Assert.That(center.z, Is.GreaterThan(0f));
            Assert.That(airborne.y, Is.LessThan(surface.y));
        }

        [Test]
        public void SpriteAtlas_DefinesSixteenUniqueTopOrderedCells()
        {
            var cells = new HashSet<Rect>();
            for (int index = 0;
                 index <
                 BeerPongSpriteLibrary.AtlasColumns *
                 BeerPongSpriteLibrary.AtlasRows;
                 index++)
            {
                Rect uv = BeerPongSpriteLibrary.GetUv(
                    (BeerPongSpriteId)index);
                Assert.That(uv.width, Is.EqualTo(0.25f));
                Assert.That(uv.height, Is.EqualTo(0.25f));
                Assert.That(cells.Add(uv), Is.True);
            }

            Rect ball = BeerPongSpriteLibrary.GetUv(
                BeerPongSpriteId.Ball);
            Rect opponent = BeerPongSpriteLibrary.GetUv(
                BeerPongSpriteId.OpponentReact);
            Assert.That(ball.x, Is.Zero);
            Assert.That(ball.y, Is.EqualTo(0.75f));
            Assert.That(opponent.x, Is.EqualTo(0.75f));
            Assert.That(opponent.y, Is.Zero);
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => BeerPongSpriteLibrary.GetUv(
                    (BeerPongSpriteId)16));
        }
    }
}
