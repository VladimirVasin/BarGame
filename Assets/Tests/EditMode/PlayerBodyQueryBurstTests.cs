using System;
using NUnit.Framework;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class PlayerBodyQueryBurstTests
    {
        private static void Set<T>(NativeArray<T> array, int index, T value) where T : struct => array[index] = value;

        [TestCase(false)]
        [TestCase(true)]
        public void Support_CompilesForHostAndKeepsOrderedMaxima(bool useTree)
        {
            Assert.That(BurstCompiler.IsEnabled, Is.True, "This regression must compile the host ISA.");
            using var vertices = new NativeArray<Vector3>(new[]
            {
                Vector3.left, Vector3.right, Vector3.right, Vector3.up, Vector3.forward
            }, Allocator.TempJob);
            using var normals = new NativeArray<Vector3>(new[]
            {
                Vector3.right, Vector3.left, Vector3.up, Vector3.forward, new Vector3(1f, 1f, 1f).normalized
            }, Allocator.TempJob);
            using var order = new NativeArray<int>(new[] { 4, 3, 2, 1, 0 }, Allocator.TempJob);
            using var changed = new NativeArray<bool>(vertices.Length, Allocator.TempJob);
            using var nodes = new NativeArray<PlayerBodySupportJob.Node>(useTree ? 1 : 0, Allocator.TempJob);
            using var offsets = new NativeArray<float>(normals.Length, Allocator.TempJob);
            using var supports = new NativeArray<int>(normals.Length, Allocator.TempJob);
            using var stack = new NativeArray<int>(PlayerBodySupportJob.RequiredStackCapacity, Allocator.TempJob);
            using var bounds = new NativeArray<double>(stack.Length, Allocator.TempJob);
            using var counts = new NativeArray<int>(normals.Length, Allocator.TempJob);
            using var indices = new NativeArray<int>(normals.Length * PlayerBodySupportJob.CandidateCapacity, Allocator.TempJob);
            using var scores = new NativeArray<double>(PlayerBodySupportJob.CandidateCapacity, Allocator.TempJob);
            if (useTree) Set(nodes, 0, new PlayerBodySupportJob.Node { Count = vertices.Length, Left = -1, Right = -1 });
            var job = new PlayerBodySupportJob
            {
                Vertices = vertices, Normals = normals, Order = order, ChangedVertices = changed,
                Nodes = nodes, Offsets = offsets, SupportPoints = supports, Stack = stack, StackBounds = bounds,
                CandidateCounts = counts, CandidateIndices = indices, CandidateScores = scores,
                RefitTree = true, ExactArithmeticMode = 1
            };
            for (int pose = 0; pose < 2; pose++)
            {
                if (pose == 1) { Set(vertices, 4, new Vector3(2f, 0f, 1f)); Set(changed, 4, true); job.HasPreviousCloud = true; }
                PlayerBodySupportJob.RunSynchronous(ref job);
                for (int plane = 0; plane < normals.Length; plane++)
                {
                    float maximum = float.NegativeInfinity;
                    int winner = -1;
                    for (int vertex = 0; vertex < vertices.Length; vertex++)
                    {
                        Vector3 n = normals[plane], v = vertices[vertex];
                        float dot = (float)(((double)n.x * v.x + (double)n.y * v.y) + (double)n.z * v.z);
                        if (dot > maximum) { maximum = dot; winner = vertex; }
                    }
                    Assert.That(counts[plane], Is.EqualTo(PlayerBodySupportJob.ExactResult));
                    Assert.That(BitConverter.SingleToInt32Bits(offsets[plane]), Is.EqualTo(BitConverter.SingleToInt32Bits(maximum)));
                    Assert.That(supports[plane], Is.EqualTo(winner), "Equal maxima keep the first source vertex.");
                }
            }
        }

        [Test]
        public void Ray_CompilesForHostAndRetainsConservativeBoxWitnesses()
        {
            Assert.That(BurstCompiler.IsEnabled, Is.True);
            Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            using var planes = new NativeArray<Plane>(6, Allocator.TempJob);
            for (int i = 0; i < planes.Length; i++) Set(planes, i, new Plane(normals[i], -1f));
            using var ranges = new NativeArray<PlayerBodyRayJob.Range>(new[] { new PlayerBodyRayJob.Range { Count = 6 } }, Allocator.TempJob);
            using var trees = new NativeArray<PlayerBodyRayJob.Tree>(new[] { new PlayerBodyRayJob.Tree { Count = 1 } }, Allocator.TempJob);
            using var counts = new NativeArray<int>(1, Allocator.TempJob);
            using var indices = new NativeArray<int>(PlayerBodyRayJob.CandidateCapacity, Allocator.TempJob);
            using var points = new NativeArray<PlayerBodyRayJob.PointPlane>(6, Allocator.TempJob);
            using var versions = new NativeArray<uint>(1, Allocator.TempJob);
            using var crossings = new NativeArray<PlayerBodyRayJob.Crossing>(6, Allocator.TempJob);
            using var packRanges = new NativeArray<PlayerBodyRayJob.Range>(new[] { new PlayerBodyRayJob.Range { Count = 2 } }, Allocator.TempJob);
            using var packs = new NativeArray<PlayerBodyRayJob.PlanePack>(2, Allocator.TempJob);
            using var packPoints = new NativeArray<PlayerBodyRayJob.PointPack>(2, Allocator.TempJob);
            using var packVersions = new NativeArray<uint>(1, Allocator.TempJob);
            for (int group = 0; group < 2; group++)
            {
                Vector3 a = normals[group * 4], b = normals[group * 4 + 1];
                Vector3 c = group == 0 ? normals[2] : a, d = group == 0 ? normals[3] : b;
                Set(packs, group, new PlayerBodyRayJob.PlanePack
                {
                    X = new v128(a.x, b.x, c.x, d.x), Y = new v128(a.y, b.y, c.y, d.y),
                    Z = new v128(a.z, b.z, c.z, d.z), Distance = new v128(-1f)
                });
            }
            var job = new PlayerBodyRayJob
            {
                Planes = planes, Ranges = ranges, Trees = trees, CandidateCounts = counts, CandidateIndices = indices,
                PointPlanes = points, ProxyPointVersions = versions, Crossings = crossings,
                PackRanges = packRanges, PlanePacks = packs, PointPacks = packPoints, PackPointVersions = packVersions,
                UseMasks = true, EligibleMask = 1, InsideMask = 1, PointVersion = 1, Direction = Vector3.right
            };
            foreach (Vector3 direction in new[] { Vector3.right, Vector3.left, new Vector3(1f, .25f, -.5f) })
            {
                job.Direction = direction;
                PlayerBodyRayJob.RunSynchronous(ref job);
                Assert.That(job.ManagedExecution, Is.Zero, "The query must execute in Burst, not a managed fallback.");
                Assert.That(counts[0], Is.InRange(2, 6));
                bool entry = false, exit = false;
                for (int i = 0; i < counts[0]; i++)
                {
                    entry |= indices[i] == (direction.x > 0f ? 1 : 0);
                    exit |= indices[i] == (direction.x > 0f ? 0 : 1);
                }
                Assert.That(entry && exit, Is.True, "Conservative filtering must retain both limiting box faces.");
            }
        }
    }
}
