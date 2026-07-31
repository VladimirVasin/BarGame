using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Shared eight-sided meshes for all generated vessels. The meshes are
    /// cached once like RuntimePrimitiveFactory's cylinder and never cloned per
    /// drink or per bar.
    /// </summary>
    internal static class BarDrinkServiceMeshLibrary
    {
        private const int SideCount = 8;

        private static readonly Dictionary<BarDrinkVesselKind, Mesh>
            glassMeshes = new Dictionary<BarDrinkVesselKind, Mesh>();
        private static readonly Dictionary<BarDrinkVesselKind, Mesh>
            liquidMeshes = new Dictionary<BarDrinkVesselKind, Mesh>();

        public static Mesh GetGlassMesh(BarDrinkVesselKind kind)
        {
            if (!glassMeshes.TryGetValue(kind, out Mesh mesh))
            {
                mesh = BuildProfileMesh(
                    $"Shared {kind} Glass Mesh",
                    GetGlassProfile(kind),
                    true,
                    false);
                glassMeshes.Add(kind, mesh);
            }

            return mesh;
        }

        public static Mesh GetLiquidMesh(BarDrinkVesselKind kind)
        {
            if (!liquidMeshes.TryGetValue(kind, out Mesh mesh))
            {
                mesh = BuildProfileMesh(
                    $"Shared {kind} Liquid Mesh",
                    GetLiquidProfile(kind),
                    true,
                    true);
                liquidMeshes.Add(kind, mesh);
            }

            return mesh;
        }

        public static float GetLiquidBaseHeight(BarDrinkVesselKind kind)
        {
            switch (kind)
            {
                case BarDrinkVesselKind.Tumbler:
                case BarDrinkVesselKind.Pint:
                    return 0.025f;
                case BarDrinkVesselKind.WineGlass:
                    return 0.165f;
                case BarDrinkVesselKind.ShotGlass:
                    return 0.018f;
                case BarDrinkVesselKind.Snifter:
                    return 0.115f;
                default:
                    throw InvalidKind(kind);
            }
        }

        public static float GetVesselHeight(BarDrinkVesselKind kind)
        {
            Vector2[] profile = GetGlassProfile(kind);
            return profile[profile.Length - 1].y;
        }

        private static Vector2[] GetGlassProfile(
            BarDrinkVesselKind kind)
        {
            switch (kind)
            {
                case BarDrinkVesselKind.Tumbler:
                    return new[]
                    {
                        new Vector2(0.13f, 0f),
                        new Vector2(0.14f, 0.27f),
                        new Vector2(0.15f, 0.30f)
                    };
                case BarDrinkVesselKind.Pint:
                    return new[]
                    {
                        new Vector2(0.13f, 0f),
                        new Vector2(0.16f, 0.34f),
                        new Vector2(0.18f, 0.39f)
                    };
                case BarDrinkVesselKind.WineGlass:
                    return new[]
                    {
                        new Vector2(0.16f, 0f),
                        new Vector2(0.16f, 0.018f),
                        new Vector2(0.025f, 0.025f),
                        new Vector2(0.025f, 0.15f),
                        new Vector2(0.07f, 0.16f),
                        new Vector2(0.145f, 0.25f),
                        new Vector2(0.13f, 0.37f)
                    };
                case BarDrinkVesselKind.ShotGlass:
                    return new[]
                    {
                        new Vector2(0.065f, 0f),
                        new Vector2(0.09f, 0.145f)
                    };
                case BarDrinkVesselKind.Snifter:
                    return new[]
                    {
                        new Vector2(0.14f, 0f),
                        new Vector2(0.14f, 0.018f),
                        new Vector2(0.027f, 0.025f),
                        new Vector2(0.027f, 0.10f),
                        new Vector2(0.07f, 0.11f),
                        new Vector2(0.16f, 0.18f),
                        new Vector2(0.15f, 0.25f),
                        new Vector2(0.105f, 0.32f)
                    };
                default:
                    throw InvalidKind(kind);
            }
        }

        private static Vector2[] GetLiquidProfile(
            BarDrinkVesselKind kind)
        {
            switch (kind)
            {
                case BarDrinkVesselKind.Tumbler:
                    return new[]
                    {
                        new Vector2(0.115f, 0f),
                        new Vector2(0.134f, 0.245f)
                    };
                case BarDrinkVesselKind.Pint:
                    return new[]
                    {
                        new Vector2(0.115f, 0f),
                        new Vector2(0.162f, 0.335f)
                    };
                case BarDrinkVesselKind.WineGlass:
                    return new[]
                    {
                        new Vector2(0.058f, 0f),
                        new Vector2(0.132f, 0.085f),
                        new Vector2(0.116f, 0.172f)
                    };
                case BarDrinkVesselKind.ShotGlass:
                    return new[]
                    {
                        new Vector2(0.052f, 0f),
                        new Vector2(0.076f, 0.112f)
                    };
                case BarDrinkVesselKind.Snifter:
                    return new[]
                    {
                        new Vector2(0.058f, 0f),
                        new Vector2(0.145f, 0.075f),
                        new Vector2(0.098f, 0.175f)
                    };
                default:
                    throw InvalidKind(kind);
            }
        }

        private static Mesh BuildProfileMesh(
            string name,
            IReadOnlyList<Vector2> profile,
            bool capBottom,
            bool capTop)
        {
            if (profile == null || profile.Count < 2)
            {
                throw new ArgumentException(
                    "A vessel profile requires at least two rings.",
                    nameof(profile));
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float maximumHeight = profile[profile.Count - 1].y;
            for (int ring = 0; ring < profile.Count - 1; ring++)
            {
                Vector2 lower = profile[ring];
                Vector2 upper = profile[ring + 1];
                for (int side = 0; side < SideCount; side++)
                {
                    float angleA = side * Mathf.PI * 2f / SideCount;
                    float angleB = (side + 1) * Mathf.PI * 2f / SideCount;
                    Vector3 lowerA = Point(lower, angleA);
                    Vector3 lowerB = Point(lower, angleB);
                    Vector3 upperA = Point(upper, angleA);
                    Vector3 upperB = Point(upper, angleB);
                    Vector3 normal = Vector3.Cross(
                        upperA - lowerA,
                        lowerB - lowerA).normalized;
                    int first = vertices.Count;
                    vertices.Add(lowerA);
                    vertices.Add(upperA);
                    vertices.Add(upperB);
                    vertices.Add(lowerB);
                    normals.Add(normal);
                    normals.Add(normal);
                    normals.Add(normal);
                    normals.Add(normal);
                    float uA = side / (float)SideCount;
                    float uB = (side + 1f) / SideCount;
                    uvs.Add(new Vector2(uA, lower.y / maximumHeight));
                    uvs.Add(new Vector2(uA, upper.y / maximumHeight));
                    uvs.Add(new Vector2(uB, upper.y / maximumHeight));
                    uvs.Add(new Vector2(uB, lower.y / maximumHeight));
                    triangles.Add(first);
                    triangles.Add(first + 1);
                    triangles.Add(first + 2);
                    triangles.Add(first);
                    triangles.Add(first + 2);
                    triangles.Add(first + 3);
                }
            }

            if (capBottom)
            {
                AddCap(
                    profile[0],
                    false,
                    vertices,
                    normals,
                    uvs,
                    triangles);
            }

            if (capTop)
            {
                AddCap(
                    profile[profile.Count - 1],
                    true,
                    vertices,
                    normals,
                    uvs,
                    triangles);
            }

            var mesh = new Mesh
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }

        private static void AddCap(
            Vector2 ring,
            bool top,
            ICollection<Vector3> vertices,
            ICollection<Vector3> normals,
            ICollection<Vector2> uvs,
            ICollection<int> triangles)
        {
            int center = vertices.Count;
            Vector3 normal = top ? Vector3.up : Vector3.down;
            vertices.Add(new Vector3(0f, ring.y, 0f));
            normals.Add(normal);
            uvs.Add(new Vector2(0.5f, 0.5f));
            int ringStart = vertices.Count;
            for (int side = 0; side < SideCount; side++)
            {
                float angle = side * Mathf.PI * 2f / SideCount;
                vertices.Add(Point(ring, angle));
                normals.Add(normal);
                uvs.Add(new Vector2(
                    Mathf.Cos(angle) * 0.5f + 0.5f,
                    Mathf.Sin(angle) * 0.5f + 0.5f));
            }

            for (int side = 0; side < SideCount; side++)
            {
                int next = (side + 1) % SideCount;
                triangles.Add(center);
                triangles.Add(ringStart + (top ? next : side));
                triangles.Add(ringStart + (top ? side : next));
            }
        }

        private static Vector3 Point(Vector2 ring, float angle)
        {
            return new Vector3(
                Mathf.Cos(angle) * ring.x,
                ring.y,
                Mathf.Sin(angle) * ring.x);
        }

        private static ArgumentOutOfRangeException InvalidKind(
            BarDrinkVesselKind kind)
        {
            return new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unsupported bar drink vessel kind.");
        }
    }
}
