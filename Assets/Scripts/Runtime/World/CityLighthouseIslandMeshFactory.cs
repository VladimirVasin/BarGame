using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Bakes the island plan into meshes for the unlit silhouette
    /// material. The island cannot use the shared combined-box
    /// factory: that one hard-wires the lit default material, and at
    /// the island's distance the scene's Exp2 fog would erase it —
    /// so the boxes are baked here with their light already painted
    /// in as vertex colour, one fixed key from above and the south,
    /// the way the mountain backdrop paints its ridges.
    /// </summary>
    internal static class CityLighthouseIslandMeshFactory
    {
        // The baked key light: a face's whole lighting, as one
        // multiplier. Top-lit, south-facing bright (that is the shore
        // side), north in its own shadow.
        private const float TopShade = 1.08f;
        private const float BottomShade = 0.72f;
        private const float NorthShade = 0.94f;
        private const float SouthShade = 1.0f;
        private const float EastShade = 0.98f;
        private const float WestShade = 1.03f;

        private static readonly Vector3[] FaceNormals =
        {
            Vector3.up,
            Vector3.down,
            Vector3.forward,
            Vector3.back,
            Vector3.right,
            Vector3.left
        };

        private static readonly float[] FaceShades =
        {
            TopShade,
            BottomShade,
            NorthShade,
            SouthShade,
            EastShade,
            WestShade
        };

        /// <summary>
        /// One mesh for the whole island: 24 vertices per box (four
        /// per face, so every face keeps its own baked shade), vertex
        /// colours carrying kind colour × part shade × face shade.
        /// </summary>
        public static Mesh CreateIslandMesh(
            string name,
            IReadOnlyList<CityLighthouseIslandPartDescriptor> parts,
            IReadOnlyList<Color> partColors)
        {
            if (parts == null)
            {
                throw new ArgumentNullException(nameof(parts));
            }

            if (partColors == null || partColors.Count != parts.Count)
            {
                throw new ArgumentException(
                    "One colour per part.",
                    nameof(partColors));
            }

            var vertices = new List<Vector3>(parts.Count * 24);
            var colors = new List<Color>(parts.Count * 24);
            var triangles = new List<int>(parts.Count * 36);

            for (int index = 0; index < parts.Count; index++)
            {
                AppendBox(
                    parts[index],
                    partColors[index],
                    vertices,
                    colors,
                    triangles);
            }

            var mesh = new Mesh
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (vertices.Count > ushort.MaxValue)
            {
                mesh.indexFormat =
                    UnityEngine.Rendering.IndexFormat.UInt32;
            }

            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// An open cone shell for a light beam: apex ring at the
        /// local origin, mouth at +Z. uv.x runs 0 at the lantern to
        /// 1 at the mouth so the beam shader can fade the light out
        /// along its own length.
        /// </summary>
        public static Mesh CreateBeamCone(
            string name,
            float length,
            float startRadius,
            float endRadius,
            int segments)
        {
            if (segments < 3)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(segments),
                    "A cone needs at least three segments.");
            }

            int ringVertexCount = segments + 1;
            var vertices = new List<Vector3>(ringVertexCount * 2);
            var uvs = new List<Vector2>(ringVertexCount * 2);
            var triangles = new List<int>(segments * 6);

            for (int ring = 0; ring < 2; ring++)
            {
                float z = ring == 0 ? 0f : length;
                float radius = ring == 0 ? startRadius : endRadius;
                for (int step = 0; step <= segments; step++)
                {
                    float angle =
                        step / (float)segments * Mathf.PI * 2f;
                    vertices.Add(new Vector3(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius,
                        z));
                    uvs.Add(new Vector2(
                        ring,
                        step / (float)segments));
                }
            }

            for (int step = 0; step < segments; step++)
            {
                int near = step;
                int far = ringVertexCount + step;
                triangles.Add(near);
                triangles.Add(far);
                triangles.Add(near + 1);
                triangles.Add(near + 1);
                triangles.Add(far);
                triangles.Add(far + 1);
            }

            var mesh = new Mesh
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AppendBox(
            in CityLighthouseIslandPartDescriptor part,
            Color baseColor,
            ICollection<Vector3> vertices,
            ICollection<Color> colors,
            IList<int> triangles)
        {
            Vector3 half = part.Size * 0.5f;
            for (int face = 0; face < FaceNormals.Length; face++)
            {
                Vector3 normal = FaceNormals[face];
                Vector3 tangent = new Vector3(
                    normal.y,
                    normal.z,
                    normal.x);
                Vector3 bitangent = Vector3.Cross(normal, tangent);

                Vector3 faceCenter = Vector3.Scale(normal, half);
                Vector3 tangentReach = Vector3.Scale(
                    tangent,
                    half);
                Vector3 bitangentReach = Vector3.Scale(
                    bitangent,
                    half);

                float shade = part.Shade * FaceShades[face];
                var color = new Color(
                    Mathf.Max(0f, baseColor.r * shade),
                    Mathf.Max(0f, baseColor.g * shade),
                    Mathf.Max(0f, baseColor.b * shade),
                    1f);

                int firstIndex = vertices.Count;
                AppendCorner(
                    part, faceCenter - tangentReach - bitangentReach,
                    color, vertices, colors);
                AppendCorner(
                    part, faceCenter + tangentReach - bitangentReach,
                    color, vertices, colors);
                AppendCorner(
                    part, faceCenter + tangentReach + bitangentReach,
                    color, vertices, colors);
                AppendCorner(
                    part, faceCenter - tangentReach + bitangentReach,
                    color, vertices, colors);

                // The silhouette shader culls back faces, so the
                // winding must face outward: corners run −t−b, +t−b,
                // +t+b, −t+b, and Cross(t, b) ∝ the face normal, so
                // 0-1-2 / 0-2-3 winds front-out.
                triangles.Add(firstIndex);
                triangles.Add(firstIndex + 1);
                triangles.Add(firstIndex + 2);
                triangles.Add(firstIndex);
                triangles.Add(firstIndex + 2);
                triangles.Add(firstIndex + 3);
            }
        }

        private static void AppendCorner(
            in CityLighthouseIslandPartDescriptor part,
            Vector3 localPosition,
            Color color,
            ICollection<Vector3> vertices,
            ICollection<Color> colors)
        {
            vertices.Add(
                part.Center + part.Rotation * localPosition);
            colors.Add(color);
        }
    }
}
