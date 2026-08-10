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
        public void StaticTextColor_DoesNotChangeForPointerStates()
        {
            var style = new GUIStyle();
            var expected = new Color(0.93f, 0.71f, 0.37f, 0.82f);

            RetroUiTheme.SetStaticTextColor(style, expected);

            GUIStyleState[] states =
            {
                style.normal,
                style.hover,
                style.active,
                style.focused,
                style.onNormal,
                style.onHover,
                style.onActive,
                style.onFocused
            };
            foreach (GUIStyleState state in states)
            {
                Assert.That(state.textColor, Is.EqualTo(expected));
            }
        }

        [Test]
        public void CityMapLine_ComposesInsideScaledLogicalGroup()
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
            Vector2 groupOffset = new Vector2(19f, 41f);
            float logicalLength =
                Vector2.Distance(logicalStart, logicalEnd);
            Matrix4x4 lineMatrix = (Matrix4x4)createLineMatrix.Invoke(
                null,
                new object[]
                {
                    canvasMatrix,
                    logicalStart,
                    logicalEnd,
                    groupOffset
                });

            Vector2 transformedStart =
                lineMatrix.MultiplyPoint3x4(groupOffset);
            Vector2 transformedEnd =
                lineMatrix.MultiplyPoint3x4(
                    groupOffset + Vector2.right * logicalLength);

            Assert.That(
                transformedStart.x,
                Is.EqualTo(
                    canvas.LogicalToScreen(
                        groupOffset + logicalStart).x)
                    .Within(0.001f));
            Assert.That(
                transformedStart.y,
                Is.EqualTo(
                    canvas.LogicalToScreen(
                        groupOffset + logicalStart).y)
                    .Within(0.001f));
            Assert.That(
                transformedEnd.x,
                Is.EqualTo(
                    canvas.LogicalToScreen(
                        groupOffset + logicalEnd).x)
                    .Within(0.001f));
            Assert.That(
                transformedEnd.y,
                Is.EqualTo(
                    canvas.LogicalToScreen(
                        groupOffset + logicalEnd).y)
                    .Within(0.001f));
        }

        [Test]
        public void CityMapLine_ClipsScrollableViewportOverflow()
        {
            var viewport = new Rect(0f, 0f, 423f, 311f);
            Vector2 verticalStart = new Vector2(200f, -50f);
            Vector2 verticalEnd = new Vector2(200f, 350f);
            Assert.That(
                CityMapView.TryClipLineToRect(
                    viewport,
                    8f,
                    ref verticalStart,
                    ref verticalEnd),
                Is.True);
            Assert.That(verticalStart, Is.EqualTo(new Vector2(200f, 0f)));
            Assert.That(verticalEnd, Is.EqualTo(new Vector2(200f, 311f)));

            Vector2 horizontalStart = new Vector2(-50f, 150f);
            Vector2 horizontalEnd = new Vector2(500f, 150f);
            Assert.That(
                CityMapView.TryClipLineToRect(
                    viewport,
                    8f,
                    ref horizontalStart,
                    ref horizontalEnd),
                Is.True);
            Assert.That(
                horizontalStart,
                Is.EqualTo(new Vector2(0f, 150f)));
            Assert.That(
                horizontalEnd,
                Is.EqualTo(new Vector2(423f, 150f)));

            Vector2 crossingStart = new Vector2(-40f, -40f);
            Vector2 crossingEnd = new Vector2(463f, 351f);

            bool crossingVisible = CityMapView.TryClipLineToRect(
                viewport,
                8f,
                ref crossingStart,
                ref crossingEnd);

            Assert.That(crossingVisible, Is.True);
            Vector2 direction = (crossingEnd - crossingStart).normalized;
            Vector2 halfWidthOffset = new Vector2(
                -direction.y,
                direction.x) * 4f;
            Vector2[] corners =
            {
                crossingStart + halfWidthOffset,
                crossingStart - halfWidthOffset,
                crossingEnd + halfWidthOffset,
                crossingEnd - halfWidthOffset
            };
            foreach (Vector2 corner in corners)
            {
                Assert.That(
                    corner.x,
                    Is.InRange(viewport.xMin - 0.001f,
                        viewport.xMax + 0.001f));
                Assert.That(
                    corner.y,
                    Is.InRange(viewport.yMin - 0.001f,
                        viewport.yMax + 0.001f));
            }

            Vector2 outsideStart = new Vector2(-20f, 20f);
            Vector2 outsideEnd = new Vector2(-20f, 290f);
            Assert.That(
                CityMapView.TryClipLineToRect(
                    viewport,
                    8f,
                    ref outsideStart,
                    ref outsideEnd),
                Is.False);

            Vector2 insideStart = new Vector2(20f, 20f);
            Vector2 insideEnd = new Vector2(400f, 290f);
            Vector2 expectedInsideStart = insideStart;
            Vector2 expectedInsideEnd = insideEnd;
            Assert.That(
                CityMapView.TryClipLineToRect(
                    viewport,
                    2f,
                    ref insideStart,
                    ref insideEnd),
                Is.True);
            Assert.That(insideStart, Is.EqualTo(expectedInsideStart));
            Assert.That(insideEnd, Is.EqualTo(expectedInsideEnd));
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
