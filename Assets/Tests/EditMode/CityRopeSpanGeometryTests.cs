using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityRopeSpanGeometryTests
    {
        private static readonly Vector3 Start =
            new Vector3(2f, 3.1f, -4f);

        private static readonly Vector3 End =
            new Vector3(6f, 2.7f, -1f);

        [Test]
        public void SamplePoint_HitsBothKnotsAndSagsMidSpan()
        {
            const float Sag = 0.26f;
            Assert.That(
                CityRopeSpanGeometry.SamplePoint(Start, End, Sag, 0f),
                Is.EqualTo(Start));
            Assert.That(
                CityRopeSpanGeometry.SamplePoint(Start, End, Sag, 1f),
                Is.EqualTo(End));

            Vector3 middle = CityRopeSpanGeometry.SamplePoint(
                Start,
                End,
                Sag,
                0.5f);
            Vector3 flatMiddle = Vector3.Lerp(Start, End, 0.5f);
            Assert.That(middle.x, Is.EqualTo(flatMiddle.x));
            Assert.That(middle.z, Is.EqualTo(flatMiddle.z));
            Assert.That(
                middle.y,
                Is.EqualTo(flatMiddle.y - Sag).Within(0.0001f));
        }

        [Test]
        public void AppendChordBoxes_ChainIsContinuousAndOnTheCurve()
        {
            const float Sag = 0.18f;
            const float Thickness = 0.03f;
            var boxes = new List<RuntimeOrientedBox>();
            CityRopeSpanGeometry.AppendChordBoxes(
                boxes,
                Start,
                End,
                Sag,
                Thickness);

            Assert.That(
                boxes.Count,
                Is.EqualTo(CityRopeSpanGeometry.DefaultSegments));

            Vector3 previous = Start;
            for (int index = 0; index < boxes.Count; index++)
            {
                RuntimeOrientedBox box = boxes[index];
                Vector3 axis = box.Rotation * Vector3.forward;
                Vector3 chordStart =
                    box.Center - axis * (box.Size.z * 0.5f);
                Vector3 chordEnd =
                    box.Center + axis * (box.Size.z * 0.5f);

                // Each chord picks up exactly where the last ended.
                Assert.That(
                    (chordStart - previous).magnitude,
                    Is.LessThan(0.0005f));
                Assert.That(box.Size.x, Is.EqualTo(Thickness));
                Assert.That(box.Size.y, Is.EqualTo(Thickness));

                Vector3 expectedEnd = CityRopeSpanGeometry.SamplePoint(
                    Start,
                    End,
                    Sag,
                    (index + 1) /
                    (float)CityRopeSpanGeometry.DefaultSegments);
                Assert.That(
                    (chordEnd - expectedEnd).magnitude,
                    Is.LessThan(0.0005f));
                previous = chordEnd;
            }

            Assert.That((previous - End).magnitude, Is.LessThan(0.0005f));
        }
    }
}
