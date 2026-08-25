using System;
using UnityEngine;

namespace BarPromenade
{
    public static class MountainRoadBridgeValidator
    {
        public const int MaximumRendererCount = 7;
        public const int MaximumActiveColliderCount = 6;

        private const float DirectionTolerance = 0.01f;

        public static void ValidateOrThrow(
            MountainRoadBridgeDescriptor bridge)
        {
            if (bridge == null)
            {
                throw new ArgumentNullException(nameof(bridge));
            }

            RequireFinite(bridge.StartDistance, "Bridge start distance");
            RequireFinite(bridge.EndDistance, "Bridge end distance");
            RequireFinite(bridge.Start, "Bridge start");
            RequireFinite(bridge.End, "Bridge end");
            RequireFinite(bridge.Center, "Bridge center");
            RequireFinite(bridge.Forward, "Bridge forward");
            RequireFinite(bridge.Right, "Bridge right");
            RequireFinite(bridge.ClearWidth, "Bridge clear width");
            RequireFinite(bridge.DeckWidth, "Bridge deck width");
            RequireFinite(bridge.DeckThickness, "Bridge deck thickness");
            RequireFinite(bridge.RailHeight, "Bridge rail height");
            RequireFinite(bridge.GorgeFloorY, "Bridge gorge floor");
            RequireFinite(bridge.GorgeHalfWidth, "Bridge gorge half width");
            RequireFinite(
                bridge.AbutmentBlendLength,
                "Bridge abutment blend length");

            Vector3 span = bridge.End - bridge.Start;
            Vector3 planarSpan = new Vector3(span.x, 0f, span.z);
            float drop = Mathf.Min(bridge.Start.y, bridge.End.y) -
                         bridge.GorgeFloorY;
            if (string.IsNullOrWhiteSpace(bridge.StableId) ||
                bridge.Length < 20f ||
                bridge.Length > 70f ||
                planarSpan.magnitude < 20f ||
                bridge.ClearWidth <= 0f ||
                bridge.DeckWidth < bridge.ClearWidth + 0.5f ||
                bridge.DeckThickness < 0.5f ||
                bridge.RailHeight < 1f ||
                drop < 25f ||
                bridge.GorgeHalfWidth <= bridge.DeckWidth * 0.5f + 1f ||
                bridge.AbutmentBlendLength < 1f ||
                bridge.AbutmentBlendLength * 2f >= bridge.Length)
            {
                throw new InvalidOperationException(
                    "Mountain bridge descriptor cannot produce a safe " +
                    "high-gorge structure.");
            }

            RequireNormalized(bridge.Forward, "Bridge forward");
            RequireNormalized(bridge.Right, "Bridge right");
            if (Mathf.Abs(bridge.Forward.y) > DirectionTolerance ||
                Mathf.Abs(bridge.Right.y) > DirectionTolerance ||
                Mathf.Abs(Vector3.Dot(
                    bridge.Forward,
                    bridge.Right)) > DirectionTolerance ||
                Vector3.Dot(planarSpan.normalized, bridge.Forward) < 0.999f ||
                Vector3.Distance(
                    bridge.Center,
                    (bridge.Start + bridge.End) * 0.5f) > 0.001f)
            {
                throw new InvalidOperationException(
                    "Mountain bridge axes or centre do not match its span.");
            }
        }

        internal static void ValidateBuiltWorldOrThrow(
            MountainRoadBridgeWorldResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (result.Piers.Count != 2 ||
                result.Rails.Count != 2 ||
                result.RailColliders.Count != 2)
            {
                throw new InvalidOperationException(
                    "Mountain bridge must own two piers and two rails.");
            }

            for (int index = 0;
                 index < result.RailColliders.Count;
                 index++)
            {
                if (result.RailColliders[index] == null ||
                    !result.RailColliders[index].enabled)
                {
                    throw new InvalidOperationException(
                        "Mountain bridge rail collision is not continuous.");
                }
            }

            if (result.RendererCount > MaximumRendererCount ||
                result.ActiveColliderCount != MaximumActiveColliderCount ||
                result.PhysicalColliders.Count !=
                MaximumActiveColliderCount)
            {
                throw new InvalidOperationException(
                    "Mountain bridge exceeded its bounded presentation " +
                    "budget.");
            }

            if (result.Root.GetComponentsInChildren<Light>(true).Length != 0 ||
                result.Root.GetComponentsInChildren<AudioSource>(true)
                    .Length != 0)
            {
                throw new InvalidOperationException(
                    "Mountain bridge cannot create lights or audio sources.");
            }
        }

        private static void RequireNormalized(Vector3 value, string label)
        {
            if (Mathf.Abs(value.sqrMagnitude - 1f) > DirectionTolerance)
            {
                throw new InvalidOperationException(label +
                    " must be normalized.");
            }
        }

        private static void RequireFinite(Vector3 value, string label)
        {
            RequireFinite(value.x, label);
            RequireFinite(value.y, label);
            RequireFinite(value.z, label);
        }

        private static void RequireFinite(float value, string label)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new InvalidOperationException(label +
                    " must be finite.");
            }
        }
    }
}
