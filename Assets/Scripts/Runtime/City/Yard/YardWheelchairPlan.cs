using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The circuit the yard rider follows: the worn ring the decoration
    /// pass already put in the ground, read back so the rider and the
    /// track can never disagree.
    /// </summary>
    public readonly struct YardWheelchairPlan
    {
        public const string TreeTrunkId = "home-yard-tree-trunk";
        public const string RingIdPrefix = "home-yard-ring-";

        public YardWheelchairPlan(
            Vector3 center,
            float radius,
            float groundY,
            bool clockwise)
        {
            Center = new Vector3(center.x, groundY, center.z);
            Radius = radius;
            GroundY = groundY;
            Clockwise = clockwise;
            IsPresent = radius > 0.5f;
        }

        public Vector3 Center { get; }
        public float Radius { get; }
        public float GroundY { get; }
        public bool Clockwise { get; }
        public bool IsPresent { get; }
        public float LapLength => Mathf.PI * 2f * Radius;

        /// <summary>
        /// Derives the circuit from the authored yard decoration. The dead
        /// tree gives the centre, the worn ring segments give the radius
        /// and the ground height.
        /// </summary>
        public static YardWheelchairPlan Create(
            CityOpenAreaDecorationPlan decorations)
        {
            if (decorations == null)
            {
                throw new ArgumentNullException(nameof(decorations));
            }

            var center = Vector3.zero;
            bool hasCenter = false;
            float radiusSum = 0f;
            int radiusCount = 0;
            float groundY = 0f;
            for (int index = 0;
                 index < decorations.Descriptors.Count;
                 index++)
            {
                CityOpenAreaDecorationDescriptor descriptor =
                    decorations.Descriptors[index];
                if (string.Equals(
                        descriptor.StableId,
                        TreeTrunkId,
                        StringComparison.Ordinal))
                {
                    center = descriptor.Bounds.center;
                    groundY = descriptor.Bounds.min.y;
                    hasCenter = true;
                }
            }

            if (!hasCenter)
            {
                return default;
            }

            for (int index = 0;
                 index < decorations.Descriptors.Count;
                 index++)
            {
                CityOpenAreaDecorationDescriptor descriptor =
                    decorations.Descriptors[index];
                if (!descriptor.StableId.StartsWith(
                        RingIdPrefix,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                Vector3 segment = descriptor.Bounds.center;
                radiusSum += new Vector2(
                    segment.x - center.x,
                    segment.z - center.z).magnitude;
                radiusCount++;
            }

            if (radiusCount == 0)
            {
                return default;
            }

            return new YardWheelchairPlan(
                center,
                radiusSum / radiusCount,
                groundY,
                true);
        }
    }
}
