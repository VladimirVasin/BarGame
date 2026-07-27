using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class RetroUiThemeTests
    {
        [Test]
        public void CalculateCanvas_At720P_UsesExactTwoTimesScale()
        {
            RetroUiCanvas canvas =
                RetroUiTheme.CalculateCanvas(1280, 720);

            Assert.That(canvas.Scale, Is.EqualTo(2f));
            Assert.That(canvas.UsesIntegerScale, Is.True);
            Assert.That(canvas.ScreenOffset, Is.EqualTo(Vector2.zero));
            Assert.That(
                canvas.ScreenRect,
                Is.EqualTo(new Rect(0f, 0f, 1280f, 720f)));
        }

        [Test]
        public void CalculateCanvas_At1080P_UsesExactThreeTimesScale()
        {
            RetroUiCanvas canvas =
                RetroUiTheme.CalculateCanvas(1920, 1080);

            Assert.That(canvas.Scale, Is.EqualTo(3f));
            Assert.That(canvas.UsesIntegerScale, Is.True);
            Assert.That(canvas.ScreenOffset, Is.EqualTo(Vector2.zero));
            Assert.That(
                canvas.LogicalToScreen(
                    new Rect(10f, 8f, 140f, 36f)),
                Is.EqualTo(
                    new Rect(30f, 24f, 420f, 108f)));
        }

        [Test]
        public void CalculateCanvas_OnUltrawide_CentersIntegerCanvas()
        {
            RetroUiCanvas canvas =
                RetroUiTheme.CalculateCanvas(2560, 1080);

            Assert.That(canvas.Scale, Is.EqualTo(3f));
            Assert.That(
                canvas.ScreenOffset,
                Is.EqualTo(new Vector2(320f, 0f)));
            Assert.That(
                canvas.ScreenRect,
                Is.EqualTo(new Rect(320f, 0f, 1920f, 1080f)));
        }

        [Test]
        public void CalculateCanvas_BelowBaseline_UsesFractionalFallback()
        {
            RetroUiCanvas canvas =
                RetroUiTheme.CalculateCanvas(480, 270);

            Assert.That(canvas.Scale, Is.EqualTo(0.75f));
            Assert.That(canvas.UsesIntegerScale, Is.False);
            Assert.That(canvas.ScreenOffset, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void CanvasCoordinateConversion_RoundTrips()
        {
            RetroUiCanvas canvas =
                RetroUiTheme.CalculateCanvas(1920, 1080);
            Vector2 logical = new Vector2(137f, 211f);

            Vector2 restored =
                canvas.ScreenToLogical(
                    canvas.LogicalToScreen(logical));

            Assert.That(restored.x, Is.EqualTo(logical.x).Within(0.001f));
            Assert.That(restored.y, Is.EqualTo(logical.y).Within(0.001f));
        }

        [TestCase(1280, 720, 2f)]
        [TestCase(1920, 1080, 3f)]
        public void LogicalButtonRect_MapsVisualAndPointerToSameSpace(
            int screenWidth,
            int screenHeight,
            float expectedScale)
        {
            RetroUiCanvas canvas =
                RetroUiTheme.CalculateCanvas(screenWidth, screenHeight);
            Rect logicalButton = new Rect(543f, 9f, 87f, 21f);
            Rect visualButton =
                canvas.LogicalToScreen(logicalButton);
            Vector2 physicalPointer = visualButton.center;

            Assert.That(canvas.Scale, Is.EqualTo(expectedScale));
            Assert.That(
                logicalButton.Contains(
                    canvas.ScreenToLogical(physicalPointer)),
                Is.True);
            Assert.That(visualButton.Contains(physicalPointer), Is.True);
        }

        [Test]
        public void SnapRect_RoundsEveryComponentToPixels()
        {
            Rect snapped = RetroUiTheme.SnapRect(
                new Rect(2.4f, 3.6f, 17.49f, 20.51f));

            Assert.That(
                snapped,
                Is.EqualTo(new Rect(2f, 4f, 17f, 21f)));
        }

        [Test]
        public void CityMapLine_ComposesInsideScaledLogicalCanvas()
        {
            MethodInfo createLineMatrix = typeof(CityMapView).GetMethod(
                "CreateLineMatrix",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(createLineMatrix, Is.Not.Null);

            RetroUiCanvas canvas =
                RetroUiTheme.CalculateCanvas(2560, 1080);
            Matrix4x4 canvasMatrix = Matrix4x4.TRS(
                new Vector3(
                    canvas.ScreenOffset.x,
                    canvas.ScreenOffset.y,
                    0f),
                Quaternion.identity,
                new Vector3(canvas.Scale, canvas.Scale, 1f));
            Vector2 logicalStart = new Vector2(80f, 70f);
            Vector2 logicalEnd = new Vector2(110f, 95f);
            float logicalLength =
                Vector2.Distance(logicalStart, logicalEnd);
            Matrix4x4 lineMatrix = (Matrix4x4)createLineMatrix.Invoke(
                null,
                new object[]
                {
                    canvasMatrix,
                    logicalStart,
                    logicalEnd
                });

            Vector2 transformedStart =
                lineMatrix.MultiplyPoint3x4(Vector3.zero);
            Vector2 transformedEnd =
                lineMatrix.MultiplyPoint3x4(
                    Vector3.right * logicalLength);

            Assert.That(
                transformedStart.x,
                Is.EqualTo(
                    canvas.LogicalToScreen(logicalStart).x)
                    .Within(0.001f));
            Assert.That(
                transformedStart.y,
                Is.EqualTo(
                    canvas.LogicalToScreen(logicalStart).y)
                    .Within(0.001f));
            Assert.That(
                transformedEnd.x,
                Is.EqualTo(
                    canvas.LogicalToScreen(logicalEnd).x)
                    .Within(0.001f));
            Assert.That(
                transformedEnd.y,
                Is.EqualTo(
                    canvas.LogicalToScreen(logicalEnd).y)
                    .Within(0.001f));
        }

        [TestCase(1280, 720, 2f)]
        [TestCase(1920, 1080, 3f)]
        public void CityMapMarker_HitboxMatchesScaledVisual(
            int screenWidth,
            int screenHeight,
            float expectedScale)
        {
            RetroUiCanvas canvas =
                RetroUiTheme.CalculateCanvas(screenWidth, screenHeight);
            Vector2 logicalCenter = new Vector2(247f, 163f);
            var logicalMarker = new Rect(
                logicalCenter.x - 8.5f,
                logicalCenter.y - 8.5f,
                17f,
                17f);
            Rect screenMarker =
                canvas.LogicalToScreen(logicalMarker);

            Assert.That(canvas.Scale, Is.EqualTo(expectedScale));
            Assert.That(
                screenMarker.size,
                Is.EqualTo(logicalMarker.size * expectedScale));
            Assert.That(
                logicalMarker.Contains(
                    canvas.ScreenToLogical(screenMarker.center)),
                Is.True);
        }
    }
}
