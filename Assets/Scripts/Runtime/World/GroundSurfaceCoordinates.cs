using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Road placement data, independent of repeating albedo UVs and collision.
    /// UV4 carries signed lateral metres, distance along the road, half width and seed.
    /// A zero half width denotes an open apron or junction, without edge/tyre bands.</summary>
    internal static class GroundSurfaceCoordinates
    {
        internal const int Channel = 3;
        private static readonly int EnabledId = Shader.PropertyToID("_GroundRoadCoordinates");

        internal readonly struct Segment
        {
            internal Segment(Vector3 start, Vector3 end, float halfWidth, float distance, float seed)
            { Start = start; End = end; HalfWidth = halfWidth; Distance = distance; PatchSeed = seed; }
            internal Vector3 Start { get; }
            internal Vector3 End { get; }
            internal float HalfWidth { get; }
            internal float Distance { get; }
            internal float PatchSeed { get; }
        }

        internal static void AssignSegments(Mesh mesh, IReadOnlyList<Segment> segments,
            IReadOnlyList<int> roadIndices)
        {
            if (segments.Count == 0) return;
            Vector3[] vertices = mesh.vertices;
            var coordinates = new Vector4[vertices.Length];
            var visited = new HashSet<int>();
            foreach (int index in roadIndices)
            {
                if (!visited.Add(index)) continue;
                Vector3 point = vertices[index];
                float nearest = float.PositiveInfinity;
                foreach (Segment segment in segments)
                {
                    Vector3 direction = segment.End - segment.Start;
                    direction.y = 0f;
                    float length = direction.magnitude;
                    if (length < .0001f) continue;
                    direction /= length;
                    Vector3 delta = point - segment.Start;
                    delta.y = 0f;
                    float along = Vector3.Dot(delta, direction);
                    float distance = (delta - direction * Mathf.Clamp(along, 0f, length)).sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance;
                    var right = new Vector3(direction.z, 0f, -direction.x);
                    coordinates[index] = new Vector4(Vector3.Dot(delta, right),
                        segment.Distance + along, segment.HalfWidth, segment.PatchSeed);
                }
            }
            mesh.SetUVs(Channel, new List<Vector4>(coordinates));
        }

        internal static void Enable(Renderer renderer, int materialIndex = -1)
        {
            if (renderer == null) return;
            var properties = new MaterialPropertyBlock();
            if (materialIndex < 0) renderer.GetPropertyBlock(properties);
            else renderer.GetPropertyBlock(properties, materialIndex);
            properties.SetFloat(EnabledId, 1f);
            if (materialIndex < 0) renderer.SetPropertyBlock(properties);
            else renderer.SetPropertyBlock(properties, materialIndex);
        }

        internal static float Seed(Vector3 anchor)
        {
            unchecked
            {
                uint value = (uint)Mathf.RoundToInt(anchor.x * 10f) * 73856093u ^
                    (uint)Mathf.RoundToInt(anchor.z * 10f) * 19349663u;
                value ^= value >> 16;
                value *= 2246822519u;
                return (value & 65535u) / 65535f;
            }
        }

        internal static void AssignBoxes(Mesh mesh,
            IReadOnlyList<RuntimeOrientedBox> boxes, float edgeInset = 0f,
            Func<Vector3, Vector3> toPlan = null)
        {
            if (mesh == null || boxes == null || boxes.Count == 0) return;
            Vector3[] vertices = mesh.vertices;
            var coordinates = new List<Vector4>(vertices.Length);
            // The combined boxes retain each cube's private vertices. Verify the
            // groups before using them, so every corner of a road piece keeps its
            // own frame even where it touches a square intersection.
            int perBox = toPlan == null && vertices.Length % boxes.Count == 0
                ? vertices.Length / boxes.Count : 0;
            bool grouped = perBox >= 8;
            if (grouped)
                for (int box = 0; box < boxes.Count; box++)
                {
                    Vector3 mean = Vector3.zero;
                    for (int vertex = 0; vertex < perBox; vertex++)
                        mean += vertices[box * perBox + vertex];
                    if ((mean / perBox - boxes[box].Center).sqrMagnitude > .0001f)
                    { grouped = false; break; }
                }
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 point = toPlan == null ? vertices[index] : toPlan(vertices[index]);
                RuntimeOrientedBox box = boxes[grouped ? index / perBox : ClosestBox(point, boxes)];
                Vector3 forward = box.Rotation * Vector3.forward;
                forward.y = 0f;
                forward.Normalize();
                Vector3 right = new Vector3(forward.z, 0f, -forward.x);
                float halfWidth = box.Size.z > box.Size.x * 1.25f
                    ? Mathf.Max(0f, box.Size.x * .5f - edgeInset) : 0f;
                // A world phase keeps adjoining collinear pieces continuous.
                Vector3 axisAnchor = box.Center - forward * Vector3.Dot(box.Center, forward);
                coordinates.Add(new Vector4(Vector3.Dot(point - box.Center, right),
                    Vector3.Dot(point, forward), halfWidth, Seed(axisAnchor)));
            }
            mesh.SetUVs(Channel, coordinates);
        }

        private static int ClosestBox(Vector3 point, IReadOnlyList<RuntimeOrientedBox> boxes)
        {
            int nearest = 0;
            float distance = float.PositiveInfinity;
            for (int index = 0; index < boxes.Count; index++)
            {
                RuntimeOrientedBox box = boxes[index];
                Vector3 local = Quaternion.Inverse(box.Rotation) * (point - box.Center);
                float dx = Mathf.Max(0f, Mathf.Abs(local.x) - box.Size.x * .5f);
                float dz = Mathf.Max(0f, Mathf.Abs(local.z) - box.Size.z * .5f);
                float candidate = dx * dx + dz * dz + Mathf.Abs(local.y) * .00001f;
                if (candidate >= distance) continue;
                distance = candidate;
                nearest = index;
            }
            return nearest;
        }
    }
}
