using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class RoadFenceWorldBuilder
    {
        private const float RoadSurfaceY = 0.08f;
        private const float FenceDepth = 0.16f;
        private const float RailHeight = 0.14f;
        private const float LowerRailY = 0.52f;
        private const float UpperRailY = 1.00f;
        private const float PostWidth = 0.18f;
        private const float PostHeight = 1.18f;
        private const float MaximumPostSpacing = 2.80f;
        private const float SpatialChunkSize = 48f;
        private const float CoordinateEpsilon = 0.0001f;

        private static readonly Color PostColor =
            new Color(0.12f, 0.14f, 0.15f);
        private static readonly Color RailColor =
            new Color(0.82f, 0.57f, 0.18f);

        public static GameObject Build(
            Transform parent,
            RoadFencePlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            Transform root =
                new GameObject("Road Edge Fences").transform;
            root.SetParent(parent, false);
            var chunks =
                new Dictionary<ChunkCoordinate, FenceChunkGeometry>();

            for (int index = 0;
                 index < plan.Segments.Count;
                 index++)
            {
                AddSegmentGeometry(
                    chunks,
                    plan.Segments[index]);
            }

            var coordinates =
                new List<ChunkCoordinate>(chunks.Keys);
            coordinates.Sort(CompareChunks);
            for (int index = 0;
                 index < coordinates.Count;
                 index++)
            {
                ChunkCoordinate coordinate = coordinates[index];
                FenceChunkGeometry geometry = chunks[coordinate];
                Transform chunkRoot = new GameObject(
                    $"Fence Chunk {coordinate.X} {coordinate.Z}")
                    .transform;
                chunkRoot.SetParent(root, false);
                chunkRoot.localPosition = coordinate.Origin;

                if (geometry.RailBoxes.Count > 0)
                {
                    RuntimePrimitiveFactory.CreateCombinedBoxes(
                        "Safety Rails",
                        chunkRoot,
                        geometry.RailBoxes,
                        RailColor);
                }

                if (geometry.PostBoxes.Count > 0)
                {
                    RuntimePrimitiveFactory.CreateCombinedBoxes(
                        "Fence Posts",
                        chunkRoot,
                        geometry.PostBoxes,
                        PostColor);
                }
            }

            return root.gameObject;
        }

        private static void AddSegmentGeometry(
            IDictionary<ChunkCoordinate, FenceChunkGeometry> chunks,
            RoadFenceSegmentDescriptor descriptor)
        {
            Vector3 segmentCenter =
                descriptor.Center +
                (descriptor.OutwardNormal * (FenceDepth * 0.5f));
            AddRails(chunks, descriptor);
            AddPosts(chunks, descriptor, segmentCenter);
        }

        private static void AddRails(
            IDictionary<ChunkCoordinate, FenceChunkGeometry> chunks,
            RoadFenceSegmentDescriptor descriptor)
        {
            float pieceStart = descriptor.MinimumCoordinate;
            float maximum = descriptor.MaximumCoordinate;
            int boundaryIndex =
                Mathf.FloorToInt(pieceStart / SpatialChunkSize) + 1;
            float boundary = boundaryIndex * SpatialChunkSize;
            while (boundary < maximum - CoordinateEpsilon)
            {
                AddRailPiece(
                    chunks,
                    descriptor,
                    pieceStart,
                    boundary);
                pieceStart = boundary;
                boundary += SpatialChunkSize;
            }

            AddRailPiece(
                chunks,
                descriptor,
                pieceStart,
                maximum);
        }

        private static void AddRailPiece(
            IDictionary<ChunkCoordinate, FenceChunkGeometry> chunks,
            RoadFenceSegmentDescriptor descriptor,
            float minimum,
            float maximum)
        {
            float length = maximum - minimum;
            if (length <= CoordinateEpsilon)
            {
                return;
            }

            float centerCoordinate = (minimum + maximum) * 0.5f;
            Vector3 center = descriptor.IsHorizontal
                ? new Vector3(
                    centerCoordinate,
                    0f,
                    descriptor.FixedCoordinate)
                : new Vector3(
                    descriptor.FixedCoordinate,
                    0f,
                    centerCoordinate);
            center +=
                descriptor.OutwardNormal * (FenceDepth * 0.5f);
            Vector3 railSize = descriptor.IsHorizontal
                ? new Vector3(
                    length,
                    RailHeight,
                    FenceDepth)
                : new Vector3(
                    FenceDepth,
                    RailHeight,
                    length);
            AddBox(
                chunks,
                new Bounds(
                    center +
                    (Vector3.up * (RoadSurfaceY + LowerRailY)),
                    railSize),
                true);
            AddBox(
                chunks,
                new Bounds(
                    center +
                    (Vector3.up * (RoadSurfaceY + UpperRailY)),
                    railSize),
                true);
        }

        private static void AddPosts(
            IDictionary<ChunkCoordinate, FenceChunkGeometry> chunks,
            RoadFenceSegmentDescriptor descriptor,
            Vector3 segmentCenter)
        {
            float length = descriptor.Length;
            float endInset =
                Mathf.Min(PostWidth * 0.5f, length * 0.5f);
            float usableLength =
                Mathf.Max(0f, length - (endInset * 2f));
            if (usableLength <= CoordinateEpsilon)
            {
                AddPost(
                    chunks,
                    descriptor.IsHorizontal,
                    segmentCenter,
                    0f);
                return;
            }

            int intervalCount = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    usableLength / MaximumPostSpacing));
            for (int post = 0; post <= intervalCount; post++)
            {
                float t = post / (float)intervalCount;
                float offset =
                    Mathf.Lerp(
                        -usableLength * 0.5f,
                        usableLength * 0.5f,
                        t);
                AddPost(
                    chunks,
                    descriptor.IsHorizontal,
                    segmentCenter,
                    offset);
            }
        }

        private static void AddPost(
            IDictionary<ChunkCoordinate, FenceChunkGeometry> chunks,
            bool horizontal,
            Vector3 segmentCenter,
            float offset)
        {
            Vector3 localOffset = horizontal
                ? new Vector3(
                    offset,
                    RoadSurfaceY + (PostHeight * 0.5f),
                    0f)
                : new Vector3(
                    0f,
                    RoadSurfaceY + (PostHeight * 0.5f),
                    offset);
            AddBox(
                chunks,
                new Bounds(
                    segmentCenter + localOffset,
                    new Vector3(
                        PostWidth,
                        PostHeight,
                        PostWidth)),
                false);
        }

        private static void AddBox(
            IDictionary<ChunkCoordinate, FenceChunkGeometry> chunks,
            Bounds box,
            bool isRail)
        {
            ChunkCoordinate coordinate =
                ChunkCoordinate.FromPosition(box.center);
            if (!chunks.TryGetValue(
                    coordinate,
                    out FenceChunkGeometry geometry))
            {
                geometry = new FenceChunkGeometry();
                chunks.Add(coordinate, geometry);
            }

            Bounds localBox = box;
            localBox.center -= coordinate.Origin;
            if (isRail)
            {
                geometry.RailBoxes.Add(localBox);
            }
            else
            {
                geometry.PostBoxes.Add(localBox);
            }
        }

        private static int CompareChunks(
            ChunkCoordinate left,
            ChunkCoordinate right)
        {
            int zComparison = left.Z.CompareTo(right.Z);
            return zComparison != 0
                ? zComparison
                : left.X.CompareTo(right.X);
        }

        private readonly struct ChunkCoordinate :
            IEquatable<ChunkCoordinate>
        {
            public ChunkCoordinate(int x, int z)
            {
                X = x;
                Z = z;
            }

            public int X { get; }
            public int Z { get; }
            public Vector3 Origin =>
                new Vector3(
                    X * SpatialChunkSize,
                    0f,
                    Z * SpatialChunkSize);

            public bool Equals(ChunkCoordinate other)
            {
                return X == other.X && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is ChunkCoordinate other &&
                       Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (X * 397) ^ Z;
                }
            }

            public static ChunkCoordinate FromPosition(
                Vector3 position)
            {
                return new ChunkCoordinate(
                    Mathf.FloorToInt(
                        position.x / SpatialChunkSize),
                    Mathf.FloorToInt(
                        position.z / SpatialChunkSize));
            }
        }

        private sealed class FenceChunkGeometry
        {
            public List<Bounds> RailBoxes { get; } =
                new List<Bounds>();
            public List<Bounds> PostBoxes { get; } =
                new List<Bounds>();
        }
    }
}
