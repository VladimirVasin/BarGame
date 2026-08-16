using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Builds runtime cloth panels: a skinned vertical grid pinned
    /// along its top row that PhysX cloth simulates below. The mesh
    /// follows the terrain-builder idiom and is owned through
    /// RuntimeGeneratedMeshOwner. The simulated topology is strictly
    /// single-sided — cloth recomputes particle normals from every
    /// triangle, and a same-vertex reversed duplicate cancels them
    /// into glinting garbage — so the back face comes from one shared
    /// cull-off clone of the primitive material instead, with
    /// specular zeroed per renderer. The factory does not register
    /// the cloth for wind — the calling builder decides which
    /// registry, if any, drives it.
    /// </summary>
    internal static class ClothPanelFactory
    {
        public const int DefaultColumns = 5;
        public const int DefaultRows = 7;

        /// <summary>Top-row vertices at or above this local height
        /// stay pinned to the anchor transform.</summary>
        public const float PinnedTopThreshold = -0.02f;

        /// <summary>Free particles may drift at most this fraction of
        /// the panel height from their skinned pose — the explosion
        /// clamp for runaway accelerations.</summary>
        public const float FreeTravelHeightFraction = 0.35f;

        /// <summary>Largest fraction of the panel height a torn hem
        /// column can lose.</summary>
        public const float TornHemHeightFraction = 0.35f;

        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");
        private static readonly int CullId =
            Shader.PropertyToID("_Cull");

        private static Material twoSidedMaterial;

        /// <summary>
        /// One shared cull-off clone of the primitive material for
        /// every cloth panel; per-panel colour still rides the MPB.
        /// </summary>
        public static Material TwoSidedMaterial
        {
            get
            {
                if (twoSidedMaterial == null)
                {
                    twoSidedMaterial = new Material(
                        RuntimePrimitiveFactory.DefaultMaterial)
                    {
                        name = "Cloth Panel Two-Sided",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    twoSidedMaterial.SetFloat(
                        CullId,
                        (float)CullMode.Off);
                }

                return twoSidedMaterial;
            }
        }

        /// <summary>
        /// Creates a hanging cloth rag. The returned object's pivot is
        /// the top-center of the panel and the cloth hangs down -Y;
        /// yaw turns the panel's face (+Z normal) around the pivot.
        /// </summary>
        public static GameObject CreateHangingRag(
            string name,
            Transform parent,
            Vector3 localPosition,
            float yawDegrees,
            float width,
            float height,
            Color color,
            int tornVariant = 0,
            int columns = DefaultColumns,
            int rows = DefaultRows)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (width <= 0f || height <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    width <= 0f ? nameof(width) : nameof(height),
                    "Cloth panels need a positive size.");
            }

            if (columns < 1 || rows < 2)
            {
                throw new ArgumentOutOfRangeException(
                    columns < 1 ? nameof(columns) : nameof(rows),
                    "Cloth panels need at least a 1x2 grid.");
            }

            Mesh mesh = BuildPanelMesh(
                name,
                width,
                height,
                columns,
                rows,
                tornVariant);

            var panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            panel.transform.localPosition = localPosition;
            panel.transform.localRotation =
                Quaternion.Euler(0f, yawDegrees, 0f);

            var renderer = panel.AddComponent<SkinnedMeshRenderer>();
            renderer.bones = new[] { panel.transform };
            renderer.rootBone = panel.transform;
            renderer.sharedMesh = mesh;
            renderer.sharedMaterial = TwoSidedMaterial;
            renderer.updateWhenOffscreen = false;
            renderer.localBounds = new Bounds(
                new Vector3(0f, height * -0.5f, 0f),
                new Vector3(width + height, height * 2f, height));
            RuntimePrimitiveFactory.SetColor(renderer, color);

            // Matte rags: any specular on live cloth normals reads
            // as sparkle under the street lights.
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetFloat(SmoothnessId, 0f);
            properties.SetFloat(MetallicId, 0f);
            renderer.SetPropertyBlock(properties);

            panel.AddComponent<RuntimeGeneratedMeshOwner>()
                .Initialize(mesh);

            // The cloth snapshots the skinned mesh when it is added,
            // so it must come last; coefficients index the welded
            // particles reported by cloth.vertices, not raw vertices.
            var cloth = panel.AddComponent<Cloth>();
            Vector3[] particles = cloth.vertices;
            var coefficients =
                new ClothSkinningCoefficient[particles.Length];
            float freeTravel = height * FreeTravelHeightFraction;
            for (int index = 0; index < particles.Length; index++)
            {
                coefficients[index].maxDistance =
                    particles[index].y > PinnedTopThreshold
                        ? 0f
                        : freeTravel;
                coefficients[index].collisionSphereDistance =
                    float.MaxValue;
            }

            cloth.coefficients = coefficients;
            cloth.stretchingStiffness = 0.9f;
            cloth.bendingStiffness = 0.3f;
            cloth.damping = 0.4f;
            cloth.friction = 0.25f;
            cloth.useGravity = true;
            cloth.worldVelocityScale = 0f;
            cloth.worldAccelerationScale = 0f;
            cloth.externalAcceleration = Vector3.zero;
            cloth.randomAcceleration = Vector3.zero;
            return panel;
        }

        private static Mesh BuildPanelMesh(
            string name,
            float width,
            float height,
            int columns,
            int rows,
            int tornVariant)
        {
            int vertexColumns = columns + 1;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (int row = 0; row <= rows; row++)
            {
                for (int column = 0; column <= columns; column++)
                {
                    float u = column / (float)columns;
                    float y = height * -row / rows;
                    if (tornVariant > 0 && row == rows)
                    {
                        y += height *
                             TornHemHeightFraction *
                             Hash01(tornVariant, column);
                    }

                    vertices.Add(new Vector3(
                        (u - 0.5f) * width,
                        y,
                        0f));
                    normals.Add(Vector3.forward);
                    uvs.Add(new Vector2(u, 1f - (row / (float)rows)));
                }
            }

            int notchColumn = tornVariant > 0
                ? (int)(HashValue(tornVariant, 71) % (uint)columns)
                : -1;
            var triangles = new List<int>();
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    bool notch =
                        row == rows - 1 &&
                        column == notchColumn &&
                        HashValue(tornVariant, 137) % 3u == 0u;
                    if (notch)
                    {
                        continue;
                    }

                    int a = (row * vertexColumns) + column;
                    int b = a + 1;
                    int c = a + vertexColumns;
                    int d = c + 1;

                    // Front faces (+Z) only — the cull-off material
                    // shows the back, and a reversed duplicate would
                    // cancel the cloth-computed normals.
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(d);
                    triangles.Add(a);
                    triangles.Add(d);
                    triangles.Add(b);
                }
            }

            var mesh = new Mesh
            {
                name = $"{name} Cloth Mesh",
                hideFlags = HideFlags.HideAndDontSave
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();

            var weights = new BoneWeight[vertices.Count];
            for (int index = 0; index < weights.Length; index++)
            {
                weights[index].boneIndex0 = 0;
                weights[index].weight0 = 1f;
            }

            mesh.boneWeights = weights;
            mesh.bindposes = new[] { Matrix4x4.identity };
            mesh.UploadMeshData(false);
            return mesh;
        }

        internal static float Hash01(int variant, int column)
        {
            return (HashValue(variant, column) & 0xFFFFFFu) /
                   16777215f;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources()
        {
            twoSidedMaterial = null;
        }

        private static uint HashValue(int variant, int column)
        {
            uint value =
                (unchecked((uint)variant) * 0x9E3779B9u) ^
                (unchecked((uint)column) * 0x85EBCA6Bu) ^
                0x434C4F54u;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }
    }
}
