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
                    RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                        "Safety Rails",
                        chunkRoot,
                        geometry.RailBoxes,
                        RailColor,
                        true);
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
            AddRails(chunks, descriptor);
            AddPosts(chunks, descriptor);
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

            float startAmount = Mathf.InverseLerp(
                descriptor.MinimumCoordinate,
                descriptor.MaximumCoordinate,
                minimum);
            float endAmount = Mathf.InverseLerp(
                descriptor.MinimumCoordinate,
                descriptor.MaximumCoordinate,
                maximum);
            Vector3 pieceStart = Vector3.Lerp(
                descriptor.Start,
                descriptor.End,
                startAmount);
            Vector3 pieceEnd = Vector3.Lerp(
                descriptor.Start,
                descriptor.End,
                endAmount);
            Vector3 center = (pieceStart + pieceEnd) * 0.5f;
            center +=
                descriptor.OutwardNormal * (FenceDepth * 0.5f);
            Vector3 railSize = descriptor.IsHorizontal
                ? new Vector3(
                    Vector3.Distance(pieceStart, pieceEnd),
                    RailHeight,
                    FenceDepth)
                : new Vector3(
                    FenceDepth,
                    RailHeight,
                    Vector3.Distance(pieceStart, pieceEnd));
            Vector3 pieceDirection = (pieceEnd - pieceStart).normalized;
            Quaternion rotation = descriptor.IsHorizontal
                ? Quaternion.FromToRotation(Vector3.right, pieceDirection)
                : Quaternion.FromToRotation(Vector3.forward, pieceDirection);
            AddOrientedBox(
                chunks,
                new RuntimeOrientedBox(
                    center +
                    (Vector3.up * (RoadSurfaceY + LowerRailY)),
                    rotation,
                    railSize));
            AddOrientedBox(
                chunks,
                new RuntimeOrientedBox(
                    center +
                    (Vector3.up * (RoadSurfaceY + UpperRailY)),
                    rotation,
                    railSize));
        }

        private static void AddPosts(
            IDictionary<ChunkCoordinate, FenceChunkGeometry> chunks,
            RoadFenceSegmentDescriptor descriptor)
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
                    descriptor.Center +
                    descriptor.OutwardNormal * (FenceDepth * 0.5f));
                return;
            }

            int intervalCount = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    usableLength / MaximumPostSpacing));
            float startAmount = endInset / length;
            float endAmount = 1f - startAmount;
            for (int post = 0; post <= intervalCount; post++)
            {
                float t = post / (float)intervalCount;
                Vector3 position = Vector3.Lerp(
                    descriptor.Start,
                    descriptor.End,
                    Mathf.Lerp(startAmount, endAmount, t));
                position += descriptor.OutwardNormal *
                            (FenceDepth * 0.5f);
                AddPost(
                    chunks,
                    position);
            }
        }

        private static void AddPost(
            IDictionary<ChunkCoordinate, FenceChunkGeometry> chunks,
            Vector3 groundPosition)
        {
            AddBox(
                chunks,
                new Bounds(
                    groundPosition + Vector3.up *
                    (RoadSurfaceY + PostHeight * 0.5f),
                    new Vector3(
                        PostWidth,
                        PostHeight,
                        PostWidth)));
        }

        private static void AddOrientedBox(
            IDictionary<ChunkCoordinate, FenceChunkGeometry> chunks,
            RuntimeOrientedBox box)
        {
            ChunkCoordinate coordinate =
                ChunkCoordinate.FromPosition(box.Center);
            if (!chunks.TryGetValue(
                    coordinate,
                    out FenceChunkGeometry geometry))
            {
                geometry = new FenceChunkGeometry();
                chunks.Add(coordinate, geometry);
            }

            geometry.RailBoxes.Add(new RuntimeOrientedBox(
                box.Center - coordinate.Origin,
                box.Rotation,
                box.Size));
        }

        private static void AddBox(
            IDictionary<ChunkCoordinate, FenceChunkGeometry> chunks,
            Bounds box)
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
            geometry.PostBoxes.Add(localBox);
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
            public List<RuntimeOrientedBox> RailBoxes { get; } =
                new List<RuntimeOrientedBox>();
            public List<Bounds> PostBoxes { get; } =
                new List<Bounds>();
        }
    }
}
