using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Shared metre contract for the small residential life-scenes that sit
    /// in the strip between an apartment facade and its street. The imported
    /// assemblies, collision proxies and resident docks all use this frame:
    /// Forward points from the facade towards the street and Tangent is the
    /// assembly's local +X axis.
    /// </summary>
    public static class CityCourtyardPocketGeometry
    {
        public const int NardiVariant = 0;
        public const int BicycleVariant = 1;
        public const int BalconyBasketVariant = 2;
        public const int ChairRepairVariant = 3;
        public const int SweepingVariant = 4;
        public const int QuietVariant = 5;
        public const int VariantCount = 6;

        // The ordinary lot leaves only 1.25 m between its facade and the
        // road. Every scene remains a shallow wall-side composition rather
        // than turning the pavement into a furnished room.
        public const float MaximumDepth = 1.05f;

        public static float GetWidth(int variant)
        {
            switch (variant)
            {
                case NardiVariant:
                    return 3.40f;
                case BicycleVariant:
                    return 3.20f;
                case BalconyBasketVariant:
                    return 2.40f;
                case ChairRepairVariant:
                    return 3.00f;
                case SweepingVariant:
                    return 2.80f;
                case QuietVariant:
                    return 3.40f;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(variant),
                        variant,
                        "Unsupported residential courtyard-pocket variant.");
            }
        }

        public static float GetDepth(int variant)
        {
            switch (variant)
            {
                case NardiVariant:
                case BicycleVariant:
                case BalconyBasketVariant:
                case ChairRepairVariant:
                case SweepingVariant:
                case QuietVariant:
                    return MaximumDepth;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(variant),
                        variant,
                        "Unsupported residential courtyard-pocket variant.");
            }
        }

        public static Rect CreateFootprint(
            CityDecorationDescriptor descriptor)
        {
            ResolveFrame(descriptor, out _, out Vector3 forward);
            float width = GetWidth(descriptor.Variant);
            float depth = GetDepth(descriptor.Variant);
            float halfX = Mathf.Abs(forward.x) > 0.5f
                ? depth * 0.5f
                : width * 0.5f;
            float halfZ = Mathf.Abs(forward.x) > 0.5f
                ? width * 0.5f
                : depth * 0.5f;
            return Rect.MinMaxRect(
                descriptor.Position.x - halfX,
                descriptor.Position.z - halfZ,
                descriptor.Position.x + halfX,
                descriptor.Position.z + halfZ);
        }

        public static void ResolveFrame(
            CityDecorationDescriptor descriptor,
            out Vector3 tangent,
            out Vector3 forward)
        {
            forward = ResolveForward(descriptor.Forward);
            // Quaternion.LookRotation(forward) maps imported local +X to
            // this world-space right vector.
            tangent = new Vector3(forward.z, 0f, -forward.x);
        }

        public static Vector3 ToWorld(
            CityDecorationDescriptor descriptor,
            float localX,
            float localY,
            float localZ)
        {
            ResolveFrame(
                descriptor,
                out Vector3 tangent,
                out Vector3 forward);
            return descriptor.Position +
                   tangent * localX +
                   Vector3.up * localY +
                   forward * localZ;
        }

        internal static void AppendCollisionBounds(
            CityDecorationDescriptor descriptor,
            ICollection<Bounds> target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            switch (descriptor.Variant)
            {
                case NardiVariant:
                    Add(target, descriptor, 0f, -0.15f, 1.70f, 0.78f, 0.70f);
                    Add(target, descriptor, -0.75f, 0.36f, 0.46f, 0.45f, 0.30f);
                    Add(target, descriptor, 0.75f, 0.36f, 0.46f, 0.45f, 0.30f);
                    return;
                case BicycleVariant:
                    Add(target, descriptor, -0.20f, 0f, 2.12f, 1.25f, 0.44f);
                    Add(target, descriptor, 1.08f, -0.10f, 0.58f, 0.48f, 0.48f);
                    return;
                case BalconyBasketVariant:
                    Add(target, descriptor, -0.28f, 0f, 0.88f, 0.64f, 0.58f);
                    return;
                case ChairRepairVariant:
                    Add(target, descriptor, -0.68f, -0.10f, 0.66f, 1.30f, 0.54f);
                    Add(target, descriptor, 0.48f, -0.14f, 1.14f, 0.82f, 0.56f);
                    return;
                case SweepingVariant:
                    Add(target, descriptor, 0.92f, -0.12f, 0.52f, 0.68f, 0.48f);
                    return;
                case QuietVariant:
                    Add(target, descriptor, 0f, -0.16f, 2.08f, 1.04f, 0.46f);
                    Add(target, descriptor, -1.28f, -0.14f, 0.50f, 0.52f, 0.46f);
                    Add(target, descriptor, 1.28f, -0.14f, 0.50f, 0.52f, 0.46f);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(descriptor),
                        descriptor.Variant,
                        "Unsupported residential courtyard-pocket variant.");
            }
        }

        private static void Add(
            ICollection<Bounds> target,
            CityDecorationDescriptor descriptor,
            float localX,
            float localZ,
            float width,
            float height,
            float depth)
        {
            ResolveFrame(
                descriptor,
                out Vector3 tangent,
                out Vector3 forward);
            Vector3 center = descriptor.Position +
                             tangent * localX +
                             forward * localZ +
                             Vector3.up * (height * 0.5f);
            Vector3 size = Mathf.Abs(forward.x) > 0.5f
                ? new Vector3(depth, height, width)
                : new Vector3(width, height, depth);
            target.Add(new Bounds(center, size));
        }

        private static Vector3 ResolveForward(Vector3 candidate)
        {
            candidate.y = 0f;
            if (!IsFinite(candidate.x) ||
                !IsFinite(candidate.z) ||
                candidate.sqrMagnitude < 0.25f)
            {
                return Vector3.back;
            }

            return Mathf.Abs(candidate.x) > Mathf.Abs(candidate.z)
                ? new Vector3(Mathf.Sign(candidate.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(candidate.z));
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
