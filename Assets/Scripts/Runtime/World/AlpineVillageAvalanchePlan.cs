using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The visible old avalanche's closed foot, in expansion metres.
    /// The Blender manifest must carry the same polygon: neither forest clearance
    /// nor movement can silently turn its open surroundings into a barrier.</summary>
    public sealed class AlpineVillageAvalanchePlan
    {
        public const string RootName = "Old Avalanche";
        public static readonly Vector2 Origin = new Vector2(-158f, 110f);
        private static readonly Vector2[] outline =
        {
            new Vector2(-10f, 0f), new Vector2(-5f, -1f), new Vector2(1f, 0f),
            new Vector2(6f, -.5f), new Vector2(9.5f, 1.5f), new Vector2(9.5f, 5f),
            new Vector2(18f, 6.5f), new Vector2(19f, 11f), new Vector2(19f, 17f),
            new Vector2(19f, 25f), new Vector2(-19f, 25f), new Vector2(-19f, 14f),
            new Vector2(-18f, 8f), new Vector2(-12f, 5f)
        };
        public static IReadOnlyList<Vector2> Footprint { get; } = Array.AsReadOnly(outline);

        /// <summary>Positive radius expands the solid by the actor/crown radius.</summary>
        public bool ContainsLocal(Vector2 local, float radius = 0f) => SignedDistance(local) <= radius;

        public float SignedDistance(Vector2 local)
        {
            Vector2 point = local - Origin;
            bool inside = false;
            float distance = float.PositiveInfinity;
            for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
            {
                Vector2 a = outline[j], b = outline[i];
                distance = Mathf.Min(distance, (point - Nearest(point, a, b)).sqrMagnitude);
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return Mathf.Sqrt(distance) * (inside ? -1f : 1f);
        }

        public Vector2 ClosestOutside(Vector2 local, float radius)
        {
            if (!ContainsLocal(local, radius)) return local;
            Vector2 point = local - Origin, result = point;
            float best = float.PositiveInfinity;
            bool outside = SignedDistance(local) >= 0f;
            // CCW outline: the right-hand normal points outside. At concave
            // corners reject candidates which are still inside another edge.
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 a = outline[i], b = outline[(i + 1) % outline.Length];
                Vector2 edge = (b - a).normalized;
                Vector2 boundary = Nearest(point, a, b);
                Vector2 direction = new Vector2(edge.y, -edge.x);
                if (outside && (point - boundary).sqrMagnitude > .000001f)
                    direction = (point - boundary).normalized;
                Vector2 candidate = boundary + direction * (radius + .003f);
                Consider(candidate);
                // At an inward notch, either edge's offset alone still overlaps
                // its neighbour. Their offset-line intersection is the nearby
                // legal contact, rather than a jump to a distant polygon edge.
                Vector2 previous = (a - outline[(i + outline.Length - 1) % outline.Length]).normalized;
                Vector2 previousNormal = new Vector2(previous.y, -previous.x);
                Vector2 edgeNormal = new Vector2(edge.y, -edge.x);
                float denominator = 1f + Vector2.Dot(previousNormal, edgeNormal);
                if (denominator > .0001f)
                    Consider(a + (previousNormal + edgeNormal) * ((radius + .003f) / denominator));
            }
            return result + Origin;

            void Consider(Vector2 candidate)
            {
                float distance = (candidate - point).sqrMagnitude;
                if (distance >= best || ContainsLocal(candidate + Origin, radius)) return;
                best = distance;
                result = candidate;
            }
        }

        private static Vector2 Nearest(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 edge = b - a;
            return a + edge * Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude);
        }
    }
}
