using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [Test]
        public void Range_SweptTriangleContactAnalyticMatchesBoundariesAndOracle()
        {
            var triangle = new CombatHurtboxes.HeadTriangle(Vector3.zero, new Vector3(2f, 0f, 0f), new Vector3(0f, 2f, 0f));
            double reach = Math.Sqrt(.1d * .1d + CombatHurtboxes.HeadTriangle.ContactSkinSquared);
            CheckTriangleContact(triangle, new Vector3(.25f, .25f, 1f), new Vector3(0f, 0f, -2f), .1f,
                (1d - reach) / 2d, "face");
            CheckTriangleContact(triangle, new Vector3(1f, -1f, 0f), new Vector3(0f, 2f, 0f), .1f,
                (1d - reach) / 2d, "finite edge");
            CheckTriangleContact(triangle, new Vector3(-1f, -1f, 0f), new Vector3(2f, 2f, 0f), .1f,
                (Math.Sqrt(2d) - reach) / (2d * Math.Sqrt(2d)), "vertex");
            CheckTriangleContact(triangle, new Vector3(-1f, -.05f, 0f), new Vector3(3f, 0f, 0f), .1f,
                (1d - Math.Sqrt(reach * reach - .05d * .05d)) / 3d, "parallel edge entry through vertex");
            CheckTriangleContact(triangle, new Vector3(-1f, .05f, 0f), new Vector3(3f, 0f, 0f), .1f,
                (1d - reach) / 3d, "opposite finite edge precedes vertex");
            CheckTriangleContact(triangle, new Vector3(.25f, .25f, .05f), Vector3.zero, .1f, 0d, "initial overlap");
            CheckTriangleContact(triangle, new Vector3(.25f, .25f, 2f), new Vector3(0f, 0f, -1f), 1f,
                2d - Math.Sqrt(1d + CombatHurtboxes.HeadTriangle.ContactSkinSquared), "endpoint");
            CheckTriangleContact(triangle, new Vector3(.25f, .25f, 1f), new Vector3(0f, 0f, -2f), 0f,
                .49995d, "zero-radius contact skin");
            var reversed = new CombatHurtboxes.HeadTriangle(triangle.A, triangle.C, triangle.B);
            CheckTriangleContact(reversed, new Vector3(.25f, .25f, -1f), new Vector3(0f, 0f, 2f), .1f,
                (1d - reach) / 2d, "reversed face");
            var line = new CombatHurtboxes.HeadTriangle(Vector3.zero, new Vector3(2f, 0f, 0f), Vector3.right);
            CheckTriangleContact(line, new Vector3(1f, -1f, 0f), new Vector3(0f, 2f, 0f), .1f,
                (1d - reach) / 2d, "collinear triangle");
            var point = new CombatHurtboxes.HeadTriangle(Vector3.zero, Vector3.zero, Vector3.zero);
            CheckTriangleContact(point, new Vector3(-1f, 0f, 0f), new Vector3(2f, 0f, 0f), .1f,
                (1d - reach) / 2d, "point triangle");
            CheckTriangleContact(triangle, new Vector3(-2f, -.125f, 0f), new Vector3(4f, 0f, 0f), .125f,
                (2d - Math.Sqrt(CombatHurtboxes.HeadTriangle.ContactSkinSquared)) / 4d, "grazing vertex");
            CheckTriangleContact(triangle, new Vector3(-1000000f, -.125f, 0f), new Vector3(2000000f, 0f, 0f), .125f,
                .5d, "long grazing ray");
            Vector3 almostParallelFrom = new Vector3(1f, -.100005f, 0f), almostParallelDelta = new Vector3(.3f, .00001f, 0f);
            double exactReach = Math.Sqrt((double).1f * .1f + CombatHurtboxes.HeadTriangle.ContactSkinSquared);
            CheckTriangleContact(triangle, almostParallelFrom, almostParallelDelta, .1f,
                (-exactReach - almostParallelFrom.y) / almostParallelDelta.y, "almost parallel cylinder");
            Assert.That(triangle.FirstContact(new Vector3(-2f, -.1252f, 0f), new Vector3(4f, 0f, 0f), .125f, out _), Is.False);
            Assert.That(triangle.FirstContact(new Vector3(.25f, .25f, 1f), Vector3.zero, .1f, out _), Is.False);
            Assert.That(triangle.FirstContact(new Vector3(float.NaN, 0f, 0f), Vector3.right, .1f, out _), Is.False);
            Assert.That(CombatHurtboxes.IntersectsSegment(triangle.Bounds, new Vector3(-2f, -.125f, 0f),
                new Vector3(4f, 0f, 0f), .125f), Is.True, "Bounds pruning must retain the exact query's contact skin.");
            var finiteQuery = new CombatHurtboxes.SegmentQuery(new Vector3(.25f, .25f, 1f), new Vector3(0f, 0f, -2f), .1f);
            Assert.That(finiteQuery.TryIntersect(triangle.Bounds, .4f, out _), Is.False, "Prune after an earlier exact contact.");
            Assert.That(finiteQuery.TryIntersect(triangle.Bounds, .5f, out float boundsEntry), Is.True);
            Assert.That(boundsEntry, Is.EqualTo((float)((1d - reach) / 2d)).Within(.000002f));

            var random = new System.Random(1949);
            for (int sample = 0; sample < 240; sample++)
            {
                Quaternion rotation = Quaternion.Euler(NextTriangleSample(random, -180f, 180f),
                    NextTriangleSample(random, -180f, 180f), NextTriangleSample(random, -180f, 180f));
                Vector3 offset = new Vector3(NextTriangleSample(random, -2f, 2f), NextTriangleSample(random, -2f, 2f),
                    NextTriangleSample(random, -2f, 2f));
                float scale = NextTriangleSample(random, .1f, 2f);
                var varied = new CombatHurtboxes.HeadTriangle(offset, offset + rotation * new Vector3(2f * scale, 0f, 0f),
                    offset + rotation * new Vector3(0f, 2f * scale, 0f));
                Vector3 from = offset + rotation * new Vector3(NextTriangleSample(random, -.5f, 2.5f) * scale,
                    NextTriangleSample(random, -.5f, 2.5f) * scale, NextTriangleSample(random, .4f, 2f));
                Vector3 delta = rotation * new Vector3(NextTriangleSample(random, -.5f, .5f),
                    NextTriangleSample(random, -.5f, .5f), NextTriangleSample(random, -3f, -.2f));
                float radius = NextTriangleSample(random, .01f, .3f);
                bool expected = IterativeTriangleContact(varied, from, delta, radius, out float oldFraction);
                bool actual = varied.FirstContact(from, delta, radius, out float fraction);
                Assert.That(actual, Is.EqualTo(expected), "Iterative contact oracle, sample " + sample);
                if (actual) Assert.That(fraction, Is.EqualTo(oldFraction).Within(.00002f), "First entry, sample " + sample);
            }
        }

        private static void CheckTriangleContact(CombatHurtboxes.HeadTriangle triangle, Vector3 from, Vector3 delta,
            float radius, double expected, string description)
        {
            Assert.That(triangle.FirstContact(from, delta, radius, out float fraction), Is.True, description);
            Assert.That(fraction, Is.InRange(0f, 1f), description);
            Assert.That(fraction, Is.EqualTo((float)expected).Within(.000002f), description);
        }

        [Test]
        public void Range_SkinnedTriangleQueryMatchesExhaustiveAfterDeformation()
        {
            var fixture = new GameObject("Test skinned contact surface");
            var mesh = new Mesh { name = "Test deforming contact mesh" };
            try
            {
                const int triangleCount = 96;
                var points = new Vector3[triangleCount * 3];
                var weights = new BoneWeight[points.Length];
                var indices = new int[points.Length];
                var bones = new Transform[3];
                for (int bone = 0; bone < bones.Length; bone++)
                {
                    bones[bone] = new GameObject("Test contact bone " + (bone + 1)).transform;
                    bones[bone].SetParent(fixture.transform, false);
                }
                for (int triangle = 0; triangle < triangleCount; triangle++)
                {
                    Vector3 centre = new Vector3((triangle % 12) * .2f - 1.1f, (triangle / 12) * .2f - .7f, 0f);
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int vertex = triangle * 3 + corner;
                        points[vertex] = centre + (corner == 1 ? Vector3.right * .16f : corner == 2 ? Vector3.up * .16f : Vector3.zero);
                        int bone = (triangle / 24) % bones.Length;
                        weights[vertex] = corner == 0 ? new BoneWeight { boneIndex0 = bone, weight0 = 1f } :
                            new BoneWeight { boneIndex0 = bone, weight0 = .65f, boneIndex1 = (bone + 1) % bones.Length,
                                weight1 = corner == 1 ? .35f : .35001f };
                        indices[vertex] = vertex;
                    }
                }
                mesh.vertices = points; mesh.boneWeights = weights;
                mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity };
                mesh.triangles = indices;
                var renderer = fixture.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh; renderer.bones = bones;
                var surface = new CombatHurtboxes.HeadSurface(renderer, true, mutableTopology: true,
                    initialGeometryVersion: 0, initialTopologyVersion: 0);
                var random = new System.Random(8053);
                bool pruned = false;
                for (int state = 0; state < 3; state++)
                {
                    if (state == 1)
                    {
                        for (int vertex = 0; vertex < points.Length; vertex++)
                            points[vertex] += new Vector3(.03f * Mathf.Sin(vertex), 0f, .08f * Mathf.Cos(vertex));
                        mesh.vertices = points;
                        var surviving = new System.Collections.Generic.List<int>();
                        for (int triangle = 0; triangle < triangleCount; triangle++)
                            if (triangle % 5 != 0)
                                for (int corner = 0; corner < 3; corner++) surviving.Add(indices[triangle * 3 + corner]);
                        mesh.triangles = surviving.ToArray();
                    }
                    for (int bone = 0; bone < bones.Length; bone++)
                    {
                        bones[bone].localPosition = new Vector3(.07f * bone, -.02f * state, .1f * bone);
                        bones[bone].localRotation = Quaternion.Euler(9f * state * bone, 11f * state, 3f * bone);
                        bones[bone].localScale = new Vector3(1f + .07f * bone, 1f - .05f * state, 1f + .03f * state);
                    }
                    uint version = state == 0 ? 0u : 1u;
                    int topology = state == 0 ? 0 : 1;
                    surface.CapturePose(geometryVersion: version, topologyVersion: topology);
                    surface.EnsureGeometry();
                    var exhaustive = (CombatHurtboxes.HeadTriangle[])surface.Triangles.Clone();
                    surface.CapturePose(geometryVersion: version, topologyVersion: topology);
                    // Deferred triangles must use the captured pose, even if the
                    // live rig or mesh is touched before the next contact query.
                    bones[0].localPosition += Vector3.right * 100f;
                    if (state == 2)
                    {
                        var laterVertices = (Vector3[])points.Clone();
                        for (int vertex = 0; vertex < laterVertices.Length; vertex++) laterVertices[vertex] += Vector3.up * 100f;
                        mesh.vertices = laterVertices;
                    }
                    for (int queryIndex = 0; queryIndex < 80; queryIndex++)
                    {
                        Vector3 from = new Vector3(NextTriangleSample(random, -1.5f, 1.5f),
                            NextTriangleSample(random, -1f, 1f), 1.5f);
                        Vector3 delta = new Vector3(NextTriangleSample(random, -.2f, .2f),
                            NextTriangleSample(random, -.2f, .2f), -3f);
                        float radius = queryIndex % 3 == 0 ? 0f : .035f;
                        float maximum = queryIndex % 4 == 0 ? .35f : 1f;
                        var query = new CombatHurtboxes.SegmentQuery(from, delta, radius);
                        surface.PrepareQuery(query, maximum);
                        float expected = NearestSkinnedTriangleContact(exhaustive, exhaustive.Length, from, delta, radius, maximum);
                        float actual = NearestSkinnedTriangleContact(surface.Triangles, surface.QueryTriangleCount, from, delta, radius, maximum);
                        Assert.That(actual, Is.EqualTo(expected).Within(.000001f), "Frozen skinned contact after pose/topology change");
                        pruned |= surface.QueryTriangleCount < exhaustive.Length;
                    }
                }
                Assert.That(pruned, Is.True, "The exact query must exercise conservative leaf rejection.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static float NearestSkinnedTriangleContact(CombatHurtboxes.HeadTriangle[] triangles, int count,
            Vector3 from, Vector3 delta, float radius, float maximum)
        {
            float nearest = float.PositiveInfinity;
            for (int triangle = 0; triangle < count; triangle++)
                if (triangles[triangle].FirstContact(from, delta, radius, out float fraction) && fraction <= maximum)
                    nearest = Mathf.Min(nearest, fraction);
            return nearest;
        }

        private static float NextTriangleSample(System.Random random, float low, float high) => low + (high - low) * (float)random.NextDouble();

        // The former production solver remains only as an independent regression
        // oracle at ordinary scales; analytic cases above cover its long-ray limit.
        private static bool IterativeTriangleContact(CombatHurtboxes.HeadTriangle triangle, Vector3 from,
            Vector3 delta, float radius, out float fraction)
        {
            float threshold = radius * radius + .00000001f;
            fraction = 0f;
            if (TriangleOracleDistance(triangle, from) <= threshold) return true;
            if (delta.sqrMagnitude < .0000000001f) return false;
            float low = 0f, high = 1f;
            for (int pass = 0; pass < 32; pass++)
            {
                float left = (2f * low + high) / 3f, right = (low + 2f * high) / 3f;
                if (TriangleOracleDistance(triangle, from + delta * left) <= TriangleOracleDistance(triangle, from + delta * right)) high = right;
                else low = left;
            }
            float minimum = (low + high) * .5f;
            if (TriangleOracleDistance(triangle, from + delta) < TriangleOracleDistance(triangle, from + delta * minimum)) minimum = 1f;
            if (TriangleOracleDistance(triangle, from + delta * minimum) > threshold) return false;
            low = 0f; high = minimum;
            for (int pass = 0; pass < 24; pass++)
            {
                float middle = (low + high) * .5f;
                if (TriangleOracleDistance(triangle, from + delta * middle) <= threshold) high = middle; else low = middle;
            }
            fraction = high; return true;
        }

        private static float TriangleOracleDistance(CombatHurtboxes.HeadTriangle triangle, Vector3 point) =>
            (point - triangle.Closest(point)).sqrMagnitude;
    }
}
