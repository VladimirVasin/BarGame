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
            var railBoxes = new List<Bounds>(
                checked(plan.Segments.Count * 2));
            var postBoxes = new List<Bounds>();

            for (int index = 0;
                 index < plan.Segments.Count;
                 index++)
            {
                AddSegmentGeometry(
                    railBoxes,
                    postBoxes,
                    plan.Segments[index]);
            }

            if (railBoxes.Count > 0)
            {
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    "Safety Rails",
                    root,
                    railBoxes,
                    RailColor);
            }

            if (postBoxes.Count > 0)
            {
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    "Fence Posts",
                    root,
                    postBoxes,
                    PostColor);
            }

            return root.gameObject;
        }

        private static void AddSegmentGeometry(
            ICollection<Bounds> railBoxes,
            ICollection<Bounds> postBoxes,
            RoadFenceSegmentDescriptor descriptor)
        {
            Vector3 segmentCenter =
                descriptor.Center +
                (descriptor.OutwardNormal * (FenceDepth * 0.5f));

            Vector3 railSize = descriptor.IsHorizontal
                ? new Vector3(
                    descriptor.Length,
                    RailHeight,
                    FenceDepth)
                : new Vector3(
                    FenceDepth,
                    RailHeight,
                    descriptor.Length);
            railBoxes.Add(new Bounds(
                segmentCenter +
                (Vector3.up * (RoadSurfaceY + LowerRailY)),
                railSize));
            railBoxes.Add(new Bounds(
                segmentCenter +
                (Vector3.up * (RoadSurfaceY + UpperRailY)),
                railSize));

            AddPosts(postBoxes, descriptor, segmentCenter);
        }

        private static void AddPosts(
            ICollection<Bounds> postBoxes,
            RoadFenceSegmentDescriptor descriptor,
            Vector3 segmentCenter)
        {
            float length = descriptor.Length;
            float endInset =
                Mathf.Min(PostWidth * 0.5f, length * 0.5f);
            float usableLength =
                Mathf.Max(0f, length - (endInset * 2f));
            if (usableLength <= 0.0001f)
            {
                AddPost(
                    postBoxes,
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
                    postBoxes,
                    descriptor.IsHorizontal,
                    segmentCenter,
                    offset);
            }
        }

        private static void AddPost(
            ICollection<Bounds> postBoxes,
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
            postBoxes.Add(new Bounds(
                segmentCenter + localOffset,
                new Vector3(
                    PostWidth,
                    PostHeight,
                    PostWidth)));
        }
    }
}
