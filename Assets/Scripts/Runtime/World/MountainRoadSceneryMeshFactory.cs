using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    internal static class MountainRoadSceneryMeshFactory
    {
        internal static Mesh CreateConiferCrowns(
            string name,
            IReadOnlyList<MountainRoadForestDescriptor> trees)
        {
            if (trees == null)
            {
                throw new ArgumentNullException(nameof(trees));
            }

            var vertices = new List<Vector3>(trees.Count * 96);
            var triangles = new List<int>(trees.Count * 96);
            for (int index = 0; index < trees.Count; index++)
            {
                MountainRoadForestDescriptor tree = trees[index];
                Quaternion rotation = Quaternion.Euler(
                    0f,
                    tree.YawDegrees,
                    0f);
                AppendCone(
                    tree.Position + Vector3.up * (tree.Height * 0.18f),
                    rotation,
                    tree.CrownRadius,
                    tree.Height * 0.56f,
                    7,
                    vertices,
                    triangles);
                AppendCone(
                    tree.Position + Vector3.up * (tree.Height * 0.43f),
                    rotation,
                    tree.CrownRadius * 0.73f,
                    tree.Height * 0.57f,
                    7,
                    vertices,
                    triangles);
            }

            return CreateMesh(name, vertices, triangles);
        }

        internal static Mesh CreateBoulders(
            IReadOnlyList<MountainRoadMiscDescriptor> boulders)
        {
            if (boulders == null)
            {
                throw new ArgumentNullException(nameof(boulders));
            }

            var vertices = new List<Vector3>(boulders.Count * 14);
            var triangles = new List<int>(boulders.Count * 36);
            for (int index = 0; index < boulders.Count; index++)
            {
                MountainRoadMiscDescriptor item = boulders[index];
                AppendBoulder(item, index, vertices, triangles);
            }

            return CreateMesh("Mountain Road Boulders", vertices, triangles);
        }

        internal static Mesh CreateRidges(
            string name,
            IReadOnlyList<MountainRoadRidgeDescriptor> ridges)
        {
            if (ridges == null)
            {
                throw new ArgumentNullException(nameof(ridges));
            }

            var vertices = new List<Vector3>(ridges.Count * 40);
            var triangles = new List<int>(ridges.Count * 96);
            for (int index = 0; index < ridges.Count; index++)
            {
                AppendRidge(ridges[index], vertices, triangles);
            }

            return CreateMesh(name, vertices, triangles);
        }

        private static void AppendCone(
            Vector3 baseCenter,
            Quaternion rotation,
            float radius,
            float height,
            int sides,
            ICollection<Vector3> vertices,
            IList<int> triangles)
        {
            Vector3 apex = baseCenter + Vector3.up * height;
            for (int side = 0; side < sides; side++)
            {
                float firstAngle = side / (float)sides * Mathf.PI * 2f;
                float secondAngle = (side + 1) / (float)sides * Mathf.PI * 2f;
                Vector3 first = baseCenter + rotation * new Vector3(
                    Mathf.Cos(firstAngle) * radius,
                    0f,
                    Mathf.Sin(firstAngle) * radius);
                Vector3 second = baseCenter + rotation * new Vector3(
                    Mathf.Cos(secondAngle) * radius,
                    0f,
                    Mathf.Sin(secondAngle) * radius);
                int firstIndex = vertices.Count;
                vertices.Add(first);
                vertices.Add(apex);
                vertices.Add(second);
                triangles.Add(firstIndex);
                triangles.Add(firstIndex + 1);
                triangles.Add(firstIndex + 2);
            }
        }

        private static void AppendBoulder(
            MountainRoadMiscDescriptor item,
            int ordinal,
            ICollection<Vector3> vertices,
            IList<int> triangles)
        {
            const int ringCount = 6;
            int first = vertices.Count;
            Vector3 half = item.Size * 0.5f;
            vertices.Add(item.Position + item.Rotation * new Vector3(
                0f,
                half.y,
                0f));
            vertices.Add(item.Position + item.Rotation * new Vector3(
                0f,
                -half.y,
                0f));
            for (int ring = 0; ring < ringCount; ring++)
            {
                float angle = ring / (float)ringCount * Mathf.PI * 2f;
                float jitter = 0.86f + ((ordinal * 17 + ring * 13) % 9) * 0.025f;
                vertices.Add(item.Position + item.Rotation * new Vector3(
                    Mathf.Cos(angle) * half.x * jitter,
                    Mathf.Sin(angle * 2f + ordinal) * half.y * 0.16f,
                    Mathf.Sin(angle) * half.z * (1.04f - (jitter - 0.86f))));
            }

            for (int ring = 0; ring < ringCount; ring++)
            {
                int next = (ring + 1) % ringCount;
                triangles.Add(first);
                triangles.Add(first + 2 + ring);
                triangles.Add(first + 2 + next);
                triangles.Add(first + 1);
                triangles.Add(first + 2 + next);
                triangles.Add(first + 2 + ring);
            }
        }

        private static void AppendRidge(
            MountainRoadRidgeDescriptor ridge,
            ICollection<Vector3> vertices,
            IList<int> triangles)
        {
            const int stations = 6;
            Quaternion rotation = Quaternion.Euler(0f, ridge.YawDegrees, 0f);
            int firstIndex = vertices.Count;
            for (int depth = 0; depth < 2; depth++)
            {
                float z = (depth == 0 ? -0.5f : 0.5f) * ridge.Size.z;
                for (int station = 0; station < stations; station++)
                {
                    float t = station / (float)(stations - 1);
                    float x = Mathf.Lerp(-0.5f, 0.5f, t) * ridge.Size.x;
                    float edge = Mathf.Sin(t * Mathf.PI);
                    float variation = 0.84f +
                        ((ridge.Seed + station * 37) & 7) * 0.025f;
                    float peakY = -ridge.Size.y * 0.5f +
                                  ridge.Size.y * edge * variation;
                    vertices.Add(ridge.Center + rotation * new Vector3(
                        x,
                        -ridge.Size.y * 0.5f,
                        z));
                    vertices.Add(ridge.Center + rotation * new Vector3(
                        x,
                        peakY,
                        z));
                }
            }

            int back = firstIndex + stations * 2;
            for (int station = 0; station < stations - 1; station++)
            {
                int frontBottom = firstIndex + station * 2;
                int frontTop = frontBottom + 1;
                int nextFrontBottom = frontBottom + 2;
                int nextFrontTop = frontBottom + 3;
                AddQuad(
                    frontBottom,
                    frontTop,
                    nextFrontBottom,
                    nextFrontTop,
                    triangles);

                int backBottom = back + station * 2;
                int backTop = backBottom + 1;
                int nextBackBottom = backBottom + 2;
                int nextBackTop = backBottom + 3;
                AddQuad(
                    nextBackBottom,
                    nextBackTop,
                    backBottom,
                    backTop,
                    triangles);
                AddQuad(
                    frontTop,
                    backTop,
                    nextFrontTop,
                    nextBackTop,
                    triangles);
                AddQuad(
                    backBottom,
                    frontBottom,
                    nextBackBottom,
                    nextFrontBottom,
                    triangles);
            }

            AddQuad(
                firstIndex,
                back,
                firstIndex + 1,
                back + 1,
                triangles);
            int lastFront = firstIndex + (stations - 1) * 2;
            int lastBack = back + (stations - 1) * 2;
            AddQuad(
                lastFront + 1,
                lastBack + 1,
                lastFront,
                lastBack,
                triangles);
        }

        private static void AddQuad(
            int a,
            int b,
            int c,
            int d,
            IList<int> triangles)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(c);
            triangles.Add(b);
            triangles.Add(d);
        }

        private static Mesh CreateMesh(
            string name,
            List<Vector3> vertices,
            List<int> triangles)
        {
            var mesh = new Mesh
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (vertices.Count > ushort.MaxValue)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
