using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class GroundSurfacePartitionerTests
    {
        [TestCase(.5f)]
        [TestCase(2f)]
        [TestCase(8f)]
        public void Partition_PriorityRemovesOverlapAndRetainsSourcePlaneAndCoordinates(float bucketSize)
        {
            var upper = new GroundSurfacePartitioner.Triangle(
                Vertex(.25f, .25f, 10f), Vertex(.25f, .75f, 10f), Vertex(.75f, .25f, 10f), 0);
            var lower = new GroundSurfacePartitioner.Triangle(
                Vertex(0f, 0f), Vertex(0f, 2f), Vertex(2f, 0f), 1);
            List<GroundSurfacePartitioner.Triangle> result = GroundSurfacePartitioner.Partition(
                new[] { upper, lower }, out GroundSurfacePartitioner.Statistics statistics, bucketSize);

            Assert.That(Area(result, 0), Is.EqualTo(.125f).Within(.00001f));
            Assert.That(Area(result, 1), Is.EqualTo(1.875f).Within(.00001f));
            Assert.That(statistics.InputTriangles, Is.EqualTo(2));
            Assert.That(statistics.OutputTriangles, Is.EqualTo(result.Count));
            Assert.That(statistics.ClippedPairs, Is.GreaterThan(0));
            Assert.That(statistics.BucketEntriesVisited, Is.GreaterThanOrEqualTo(statistics.CandidatePairs),
                "Raw bucket visits include each candidate and its repeated bucket entries.");
            foreach (GroundSurfacePartitioner.Triangle triangle in result)
            {
                if (triangle.Surface != 1) continue;
                foreach (GroundSurfacePartitioner.Vertex vertex in new[] { triangle.A, triangle.B, triangle.C })
                {
                    Vector3 point = vertex.Position;
                    Assert.That(point.y, Is.EqualTo(2f * point.x + 3f * point.z).Within(.00001f));
                    Assert.That(vertex.Uv0.x, Is.EqualTo(point.x / 4f).Within(.00001f));
                    Assert.That(vertex.Uv0.y, Is.EqualTo(point.z / 4f).Within(.00001f));
                    Assert.That(vertex.RoadCoordinates.x, Is.EqualTo(point.x).Within(.00001f));
                    Assert.That(vertex.RoadCoordinates.y, Is.EqualTo(point.z).Within(.00001f));
                    Assert.That(vertex.RoadCoordinates.z, Is.EqualTo(2f));
                    Assert.That(vertex.RoadCoordinates.w, Is.EqualTo(.4f));
                    Assert.That(vertex.Color.r, Is.EqualTo(point.x / 4f).Within(.00001f));
                    Assert.That(vertex.Color.g, Is.EqualTo(point.z / 4f).Within(.00001f));
                    Assert.That(vertex.Normal, Is.EqualTo(Vector3.up));
                }
                Vector3 centre = (triangle.A.Position + triangle.B.Position + triangle.C.Position) / 3f;
                Assert.That(centre.x > .25f && centre.z > .25f && centre.x + centre.z < 1f,
                    Is.False, "The lower surface still occupies the upper triangle's interior.");
            }
        }

        [Test]
        public void Partition_TouchingEdgesKeepBothOriginalFaces()
        {
            var source = new[]
            {
                new GroundSurfacePartitioner.Triangle(Vertex(0, 0), Vertex(0, 1), Vertex(1, 0), 0),
                new GroundSurfacePartitioner.Triangle(Vertex(1, 0), Vertex(0, 1), Vertex(1, 1), 1)
            };
            List<GroundSurfacePartitioner.Triangle> result = GroundSurfacePartitioner.Partition(
                source, out GroundSurfacePartitioner.Statistics statistics, .25f);
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(Area(result, 0) + Area(result, 1), Is.EqualTo(1f));
            Assert.That(statistics.ClippedPairs, Is.Zero);
            Assert.That(result[1].A.Position, Is.EqualTo(source[1].A.Position));
            Assert.That(result[1].B.Position, Is.EqualTo(source[1].B.Position));
            Assert.That(result[1].C.Position, Is.EqualTo(source[1].C.Position));
        }

        [Test]
        public void Partition_RejectsRoundoffWedgesButRetainsSmallerWellConditionedFaces()
        {
            var wedge = new GroundSurfacePartitioner.Triangle(
                Vertex(3.36363626f, .636363685f), Vertex(2.5999999f, 1.39999998f),
                Vertex(2.60000014f, 1.39999998f), 0);
            var small = new GroundSurfacePartitioner.Triangle(
                Vertex(0f, 0f), Vertex(0f, .00004f), Vertex(.00004f, 0f), 1);
            List<GroundSurfacePartitioner.Triangle> result = GroundSurfacePartitioner.Partition(new[] { wedge, small });
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].Surface, Is.EqualTo(1));
            Assert.That(Area(result, 0), Is.Zero);
            Assert.That(Area(result, 1), Is.EqualTo(8e-10f).Within(1e-12f),
                "Degeneracy depends on projected altitude, not a raised global area threshold.");
        }

        [Test]
        public void Partition_AcrossNegativeBucketsIsDeterministic()
        {
            var source = new[]
            {
                new GroundSurfacePartitioner.Triangle(Vertex(-2, -2), Vertex(-2, 1), Vertex(1, -2), 0),
                new GroundSurfacePartitioner.Triangle(Vertex(-1, -1), Vertex(-1, 2), Vertex(2, -1), 1),
                new GroundSurfacePartitioner.Triangle(Vertex(-3, -3), Vertex(-3, 3), Vertex(3, -3), 2)
            };
            List<GroundSurfacePartitioner.Triangle> first = GroundSurfacePartitioner.Partition(source, .75f);
            List<GroundSurfacePartitioner.Triangle> second = GroundSurfacePartitioner.Partition(source, .75f);
            Assert.That(first.Count, Is.EqualTo(second.Count));
            for (int index = 0; index < first.Count; index++)
            {
                Assert.That(first[index].Surface, Is.EqualTo(second[index].Surface));
                Assert.That(first[index].A.Position, Is.EqualTo(second[index].A.Position));
                Assert.That(first[index].B.Position, Is.EqualTo(second[index].B.Position));
                Assert.That(first[index].C.Position, Is.EqualTo(second[index].C.Position));
                Assert.That(first[index].A.RoadCoordinates, Is.EqualTo(second[index].A.RoadCoordinates));
            }
        }

        [Test]
        public void RectangleSplit_PartitionsDynamicSectorWithoutChangingAppearance()
        {
            var source = new GroundSurfacePartitioner.Triangle(Vertex(0, 0), Vertex(0, 2), Vertex(2, 0), 7);
            var bounds = new Rect(0f, 0f, 1f, 1f);
            List<GroundSurfacePartitioner.Triangle> inside = GroundSurfacePartitioner.ClipToRect(source, bounds);
            List<GroundSurfacePartitioner.Triangle> outside = GroundSurfacePartitioner.SubtractRect(source, bounds);
            Assert.That(Area(inside, 7), Is.EqualTo(1f).Within(.00001f));
            Assert.That(Area(outside, 7), Is.EqualTo(1f).Within(.00001f));
            foreach (List<GroundSurfacePartitioner.Triangle> portion in new[] { inside, outside })
                foreach (GroundSurfacePartitioner.Triangle triangle in portion)
                    foreach (GroundSurfacePartitioner.Vertex vertex in new[] { triangle.A, triangle.B, triangle.C })
                    {
                        Vector3 point = vertex.Position;
                        Assert.That(point.y, Is.EqualTo(2f * point.x + 3f * point.z).Within(.00001f));
                        Assert.That(vertex.Uv0, Is.EqualTo(new Vector2(point.x / 4f, point.z / 4f)));
                        Assert.That(vertex.RoadCoordinates.x, Is.EqualTo(point.x));
                        Assert.That(vertex.Color.g, Is.EqualTo(point.z / 4f));
                    }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Intersect_UsesPhysicalSupportContourWithEitherWinding(bool reverse)
        {
            var source = new GroundSurfacePartitioner.Triangle(Vertex(0, 0), Vertex(0, 2), Vertex(2, 0), 3);
            var support = new[] { new Vector2(.25f, .25f), new Vector2(.75f, .25f), new Vector2(.25f, .75f) };
            if (reverse) System.Array.Reverse(support);
            List<GroundSurfacePartitioner.Triangle> result = GroundSurfacePartitioner.Intersect(source, support);
            Assert.That(Area(result, 3), Is.EqualTo(.125f).Within(.00001f));
            foreach (GroundSurfacePartitioner.Triangle triangle in result)
                foreach (GroundSurfacePartitioner.Vertex vertex in new[] { triangle.A, triangle.B, triangle.C })
                {
                    Assert.That(vertex.Position.y,
                        Is.EqualTo(2f * vertex.Position.x + 3f * vertex.Position.z).Within(.00001f));
                    Assert.That(vertex.Position.x, Is.InRange(.25f, .75f));
                    Assert.That(vertex.Position.z, Is.InRange(.25f, .75f));
                }
        }

        [TestCase(0f, true)]
        [TestCase(.14f, false)]
        public void Conform_SharesEdgeSamplesOnlyAtMatchingHeight(float neighbourHeight, bool splits)
        {
            var source = new[]
            {
                new GroundSurfacePartitioner.Triangle(Vertex(0, 0, 0), Vertex(0, 2, 0), Vertex(2, 0, 0), 0),
                new GroundSurfacePartitioner.Triangle(Vertex(1, 0, neighbourHeight),
                    Vertex(2, -1, neighbourHeight), Vertex(1, -1, neighbourHeight), 1)
            };
            List<GroundSurfacePartitioner.Triangle> result = GroundSurfacePartitioner.Conform(source);
            Assert.That(Area(result, 0), Is.EqualTo(2f).Within(.00001f));
            int firstSurfaceCount = 0;
            bool hasRimSample = false;
            foreach (GroundSurfacePartitioner.Triangle triangle in result)
            {
                if (triangle.Surface != 0) continue;
                firstSurfaceCount++;
                foreach (GroundSurfacePartitioner.Vertex vertex in new[] { triangle.A, triangle.B, triangle.C })
                {
                    Assert.That(vertex.Position.y, Is.Zero);
                    if (vertex.Position == new Vector3(1f, 0f, 0f))
                    {
                        hasRimSample = true;
                        Assert.That(vertex.Uv0, Is.EqualTo(new Vector2(.25f, 0f)));
                        Assert.That(vertex.RoadCoordinates, Is.EqualTo(new Vector4(1f, 0f, 2f, .4f)));
                    }
                }
            }
            Assert.That(hasRimSample, Is.EqualTo(splits));
            Assert.That(firstSurfaceCount, Is.EqualTo(splits ? 4 : 1));
        }

        [Test]
        public void Conform_LongDiagonalPreservesToleranceSamplesAcrossBucketSizes()
        {
            var source = new List<GroundSurfacePartitioner.Triangle>
            {
                new GroundSurfacePartitioner.Triangle(Vertex(-12, -12), Vertex(12, 12), Vertex(-12, 12), 0)
            };
            float[] along = { -8f, -.00004f, 3.5f, 8f };
            float[] offsets = { 0f, .0001f, -.0001f, 0f };
            for (int index = 0; index < along.Length; index++)
            {
                float x = along[index], z = x + offsets[index];
                source.Add(new GroundSurfacePartitioner.Triangle(Vertex(x, z),
                    Vertex(x + .2f, z - .3f), Vertex(x + .3f, z - .2f), index + 1));
            }
            List<GroundSurfacePartitioner.Triangle> fine = GroundSurfacePartitioner.Conform(source, bucketSize: .5f);
            // The coarse buckets use the complete edge AABB, providing the
            // same geometric contract without diagonal bucket pruning.
            List<GroundSurfacePartitioner.Triangle> coarse = GroundSurfacePartitioner.Conform(source, bucketSize: 1000f);
            Assert.That(fine.Count, Is.EqualTo(coarse.Count));
            int refinedBase = 0;
            for (int index = 0; index < fine.Count; index++)
            {
                Assert.That(fine[index].Surface, Is.EqualTo(coarse[index].Surface));
                if (fine[index].Surface == 0) refinedBase++;
                GroundSurfacePartitioner.Vertex[] actual = { fine[index].A, fine[index].B, fine[index].C };
                GroundSurfacePartitioner.Vertex[] expected = { coarse[index].A, coarse[index].B, coarse[index].C };
                for (int vertex = 0; vertex < actual.Length; vertex++)
                {
                    Assert.That(actual[vertex].Position, Is.EqualTo(expected[vertex].Position));
                    Assert.That(actual[vertex].Uv0, Is.EqualTo(expected[vertex].Uv0));
                    Assert.That(actual[vertex].RoadCoordinates, Is.EqualTo(expected[vertex].RoadCoordinates));
                    Assert.That(actual[vertex].Color, Is.EqualTo(expected[vertex].Color));
                }
            }
            Assert.That(refinedBase, Is.GreaterThanOrEqualTo(7), "The long edge lost matching-height subdivisions.");
            Assert.That(Area(fine, 0), Is.EqualTo(288f).Within(.0001f));
        }

        private static GroundSurfacePartitioner.Vertex Vertex(float x, float z, float? height = null)
            => new GroundSurfacePartitioner.Vertex(new Vector3(x, height ?? 2f * x + 3f * z, z),
                Vector3.up, new Vector2(x / 4f, z / 4f), new Vector4(x, z, 2f, .4f),
                new Color(x / 4f, z / 4f, .5f, 1f));

        private static float Area(IReadOnlyList<GroundSurfacePartitioner.Triangle> triangles, int surface)
        {
            float result = 0f;
            foreach (GroundSurfacePartitioner.Triangle triangle in triangles)
            {
                if (triangle.Surface != surface) continue;
                Vector3 first = triangle.B.Position - triangle.A.Position;
                Vector3 second = triangle.C.Position - triangle.A.Position;
                result += Mathf.Abs(first.x * second.z - first.z * second.x) * .5f;
            }
            return result;
        }
    }
}
