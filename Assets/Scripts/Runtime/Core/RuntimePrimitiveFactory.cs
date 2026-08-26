using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// How a combined mesh bakes its world-scale UVs.
    ///
    /// `XZPlanar` projects every vertex straight down, which is right
    /// for ground, roads and paths but collapses a vertical face to a
    /// single stretched line of the sheet. `BoxProjected` picks the
    /// projection plane per face from its normal, so an upright hedge
    /// run, a trunk or a bench leg tiles at the same metre pitch as
    /// the ground it stands on.
    /// </summary>
    public enum RuntimeWorldUvMode
    {
        XZPlanar,
        BoxProjected
    }

    public readonly struct RuntimeOrientedBox
    {
        public RuntimeOrientedBox(
            Vector3 center,
            Quaternion rotation,
            Vector3 size)
        {
            Center = center;
            Rotation = rotation;
            Size = size;
        }

        public Vector3 Center { get; }
        public Quaternion Rotation { get; }
        public Vector3 Size { get; }

        /// <summary>
        /// Samples the world height of this box's upper face under a
        /// world position. False when the position falls outside the
        /// face's footprint or the box lies on its side.
        /// </summary>
        public bool TrySampleTop(Vector3 worldPosition, out float topY)
        {
            const float tolerance = 0.0001f;
            Vector3 normal = Rotation * Vector3.up;
            if (Mathf.Abs(normal.y) <= tolerance)
            {
                topY = 0f;
                return false;
            }

            Vector3 planePoint = Center + normal * (Size.y * 0.5f);
            topY = planePoint.y -
                ((normal.x * (worldPosition.x - planePoint.x) +
                  normal.z * (worldPosition.z - planePoint.z)) /
                 normal.y);
            Vector3 local = Quaternion.Inverse(Rotation) *
                (new Vector3(worldPosition.x, topY, worldPosition.z) -
                 Center);
            return Mathf.Abs(local.x) <=
                       Size.x * 0.5f + tolerance &&
                   Mathf.Abs(local.z) <=
                       Size.z * 0.5f + tolerance;
        }
    }

    /// <summary>
    /// One authored mesh placed into a combined batch. The box helpers
    /// below cover everything this city is made of except the handful of
    /// props whose shape a box cannot state — a knight is the first of
    /// them — and those arrive as imported meshes that still have to
    /// reach the batcher as one draw call.
    /// </summary>
    public readonly struct RuntimeMeshPlacement
    {
        public RuntimeMeshPlacement(
            Mesh mesh,
            Vector3 center,
            Quaternion rotation)
            : this(mesh, center, rotation, Vector3.one)
        {
        }

        public RuntimeMeshPlacement(
            Mesh mesh,
            Vector3 center,
            Quaternion rotation,
            Vector3 scale)
        {
            Mesh = mesh;
            Center = center;
            Rotation = rotation;
            Scale = scale;
        }

        public Mesh Mesh { get; }
        public Vector3 Center { get; }
        public Quaternion Rotation { get; }
        public Vector3 Scale { get; }
    }

    public static class RuntimePrimitiveFactory
    {
        private const int LowPolyCylinderSides = 8;
        public const string DefaultMaterialResourcePath =
            "Materials/RuntimePrimitiveLit";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static Mesh lowPolyCylinderMesh;
        private static Material defaultMaterial;

        public static Material DefaultMaterial
        {
            get
            {
                if (defaultMaterial == null)
                {
                    defaultMaterial = Resources.Load<Material>(
                        DefaultMaterialResourcePath);
                }

                if (defaultMaterial == null ||
                    defaultMaterial.shader == null ||
                    !defaultMaterial.shader.isSupported)
                {
                    throw new InvalidOperationException(
                        "Missing or unsupported runtime primitive material " +
                        $"'{DefaultMaterialResourcePath}'.");
                }

                return defaultMaterial;
            }
        }

        public static GameObject CreateBox(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Color color,
            bool collider = true)
        {
            return CreatePrimitive(
                PrimitiveType.Cube,
                name,
                parent,
                localPosition,
                size,
                color,
                collider,
                null);
        }

        public static GameObject CreateBox(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Color color,
            Material sharedMaterial,
            bool collider = true)
        {
            return CreatePrimitive(
                PrimitiveType.Cube,
                name,
                parent,
                localPosition,
                size,
                color,
                collider,
                sharedMaterial);
        }

        /// <summary>
        /// A box that leaves colour entirely to its shared material: no
        /// property block is written, so a later material-wide colour
        /// change (for example the day-night window dim) reaches it.
        /// </summary>
        public static GameObject CreateMaterialBox(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Material sharedMaterial,
            bool collider = true)
        {
            if (sharedMaterial == null)
            {
                throw new ArgumentNullException(nameof(sharedMaterial));
            }

            return CreatePrimitive(
                PrimitiveType.Cube,
                name,
                parent,
                localPosition,
                size,
                Color.white,
                collider,
                sharedMaterial,
                false);
        }

        public static GameObject CreateCylinder(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Color color,
            bool collider = true)
        {
            return CreatePrimitive(
                PrimitiveType.Cylinder,
                name,
                parent,
                localPosition,
                size,
                color,
                collider,
                null);
        }

        public static GameObject CreateCylinder(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Color color,
            Material sharedMaterial,
            bool collider = true)
        {
            return CreatePrimitive(
                PrimitiveType.Cylinder,
                name,
                parent,
                localPosition,
                size,
                color,
                collider,
                sharedMaterial);
        }

        // Shared scratch block: GetPropertyBlock overwrites it with the
        // renderer's current state, so per-frame callers (colour pulses)
        // reuse it instead of allocating one per call.
        private static MaterialPropertyBlock sharedPropertyBlock;

        public static void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            MaterialPropertyBlock properties = sharedPropertyBlock ??=
                new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetColor(BaseColorId, color);
            properties.SetColor(ColorId, color);
            renderer.SetPropertyBlock(properties);
        }

        public static GameObject CreateCombinedBoxes(
            string name,
            Transform parent,
            IReadOnlyList<Bounds> boxes,
            Color color)
        {
            return CreateCombinedBoxes(
                name,
                parent,
                boxes,
                color,
                null,
                false);
        }

        public static GameObject CreateCombinedBoxes(
            string name,
            Transform parent,
            IReadOnlyList<Bounds> boxes,
            Color color,
            bool collider)
        {
            return CreateCombinedBoxes(
                name,
                parent,
                boxes,
                color,
                null,
                collider);
        }

        public static GameObject CreateCombinedBoxes(
            string name,
            Transform parent,
            IReadOnlyList<Bounds> boxes,
            Color color,
            Material sharedMaterial)
        {
            return CreateCombinedBoxes(
                name,
                parent,
                boxes,
                color,
                sharedMaterial,
                false);
        }

        public static GameObject CreateCombinedBoxes(
            string name,
            Transform parent,
            IReadOnlyList<Bounds> boxes,
            Color color,
            Material sharedMaterial,
            bool collider)
        {
            return CreateCombinedBoxes(
                name,
                parent,
                boxes,
                color,
                sharedMaterial,
                collider,
                null);
        }

        public static GameObject CreateCombinedBoxes(
            string name,
            Transform parent,
            IReadOnlyList<Bounds> boxes,
            Color color,
            bool collider,
            float worldUvTileSize,
            RuntimeWorldUvMode uvMode = RuntimeWorldUvMode.XZPlanar,
            Vector3 worldUvOrigin = default)
        {
            if (!IsPositiveFinite(worldUvTileSize))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(worldUvTileSize),
                    "World UV tile size must be finite and positive.");
            }

            return CreateCombinedBoxes(
                name,
                parent,
                boxes,
                color,
                null,
                collider,
                worldUvTileSize,
                uvMode,
                worldUvOrigin);
        }

        public static GameObject CreateCombinedOrientedBoxes(
            string name,
            Transform parent,
            IReadOnlyList<RuntimeOrientedBox> boxes,
            Color color,
            bool collider = false,
            float? worldUvTileSize = null,
            RuntimeWorldUvMode uvMode = RuntimeWorldUvMode.XZPlanar)
        {
            if (boxes == null)
            {
                throw new ArgumentNullException(nameof(boxes));
            }

            if (worldUvTileSize.HasValue &&
                !IsPositiveFinite(worldUvTileSize.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(worldUvTileSize));
            }

            var transforms = new Matrix4x4[boxes.Count];
            for (int index = 0; index < boxes.Count; index++)
            {
                RuntimeOrientedBox box = boxes[index];
                if (!IsPositiveFinite(box.Size) ||
                    !IsFinite(box.Center) ||
                    !IsFinite(box.Rotation))
                {
                    throw new ArgumentException(
                        "Combined oriented boxes require finite transforms " +
                        "and positive dimensions.",
                        nameof(boxes));
                }

                transforms[index] = Matrix4x4.TRS(
                    box.Center,
                    box.Rotation,
                    box.Size);
            }

            return CreateCombinedBoxTransforms(
                name,
                parent,
                transforms,
                color,
                null,
                collider,
                worldUvTileSize,
                uvMode);
        }

        /// <summary>
        /// Bakes a set of imported meshes into one mesh under one
        /// renderer, on the same world-UV contract the box batches use.
        /// The sources are shared assets and are never modified.
        /// </summary>
        public static GameObject CreateCombinedMeshes(
            string name,
            Transform parent,
            IReadOnlyList<RuntimeMeshPlacement> placements,
            Color color,
            bool collider = false,
            float? worldUvTileSize = null,
            RuntimeWorldUvMode uvMode = RuntimeWorldUvMode.XZPlanar,
            Vector3 worldUvOrigin = default)
        {
            if (placements == null)
            {
                throw new ArgumentNullException(nameof(placements));
            }

            if (placements.Count == 0)
            {
                throw new ArgumentException(
                    "At least one mesh placement is required.",
                    nameof(placements));
            }

            if (worldUvTileSize.HasValue &&
                !IsPositiveFinite(worldUvTileSize.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(worldUvTileSize));
            }

            var combine = new CombineInstance[placements.Count];
            long vertexBudget = 0;
            for (int index = 0; index < placements.Count; index++)
            {
                RuntimeMeshPlacement placement = placements[index];
                if (placement.Mesh == null)
                {
                    throw new ArgumentException(
                        "A mesh placement has no source mesh.",
                        nameof(placements));
                }

                if (!IsFinite(placement.Center) ||
                    !IsFinite(placement.Rotation) ||
                    !IsPositiveFinite(placement.Scale))
                {
                    throw new ArgumentException(
                        "Combined meshes require finite transforms and " +
                        "positive scales.",
                        nameof(placements));
                }

                vertexBudget += placement.Mesh.vertexCount;
                combine[index] = new CombineInstance
                {
                    mesh = placement.Mesh,
                    transform = Matrix4x4.TRS(
                        placement.Center,
                        placement.Rotation,
                        placement.Scale)
                };
            }

            GameObject result = CreatePrimitive(
                PrimitiveType.Cube,
                name,
                parent,
                Vector3.zero,
                Vector3.one,
                color,
                false,
                null);
            MeshFilter meshFilter = result.GetComponent<MeshFilter>();
            var combinedMesh = new Mesh
            {
                name = $"{name} Combined Mesh",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = vertexBudget > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            combinedMesh.CombineMeshes(
                combine,
                true,
                true,
                false);
            if (worldUvTileSize.HasValue)
            {
                ApplyWorldUvs(
                    combinedMesh,
                    worldUvTileSize.Value,
                    uvMode,
                    worldUvOrigin);
            }

            combinedMesh.RecalculateBounds();
            meshFilter.sharedMesh = combinedMesh;
            result.AddComponent<RuntimeGeneratedMeshOwner>()
                .Initialize(combinedMesh);
            if (collider)
            {
                result.AddComponent<MeshCollider>().sharedMesh =
                    combinedMesh;
            }

            combinedMesh.UploadMeshData(!collider);
            return result;
        }

        private static GameObject CreateCombinedBoxes(
            string name,
            Transform parent,
            IReadOnlyList<Bounds> boxes,
            Color color,
            Material sharedMaterial,
            bool collider,
            float? worldUvTileSize,
            RuntimeWorldUvMode uvMode = RuntimeWorldUvMode.XZPlanar,
            Vector3 worldUvOrigin = default)
        {
            if (boxes == null)
            {
                throw new ArgumentNullException(nameof(boxes));
            }

            if (boxes.Count == 0)
            {
                throw new ArgumentException(
                    "At least one box is required.",
                    nameof(boxes));
            }

            var transforms = new Matrix4x4[boxes.Count];
            for (int index = 0; index < boxes.Count; index++)
            {
                Bounds box = boxes[index];
                Vector3 size = box.size;
                if (!IsPositiveFinite(size))
                {
                    throw new ArgumentException(
                        "Combined boxes require finite positive dimensions.",
                        nameof(boxes));
                }

                transforms[index] = Matrix4x4.TRS(
                    box.center,
                    Quaternion.identity,
                    size);
            }

            return CreateCombinedBoxTransforms(
                name,
                parent,
                transforms,
                color,
                sharedMaterial,
                collider,
                worldUvTileSize,
                uvMode,
                worldUvOrigin);
        }

        private static GameObject CreateCombinedBoxTransforms(
            string name,
            Transform parent,
            IReadOnlyList<Matrix4x4> transforms,
            Color color,
            Material sharedMaterial,
            bool collider,
            float? worldUvTileSize,
            RuntimeWorldUvMode uvMode = RuntimeWorldUvMode.XZPlanar,
            Vector3 worldUvOrigin = default)
        {
            if (transforms == null)
            {
                throw new ArgumentNullException(nameof(transforms));
            }

            if (transforms.Count == 0)
            {
                throw new ArgumentException(
                    "At least one box transform is required.",
                    nameof(transforms));
            }

            GameObject result = CreatePrimitive(
                PrimitiveType.Cube,
                name,
                parent,
                Vector3.zero,
                Vector3.one,
                color,
                false,
                sharedMaterial);
            MeshFilter meshFilter = result.GetComponent<MeshFilter>();
            Mesh sourceMesh = meshFilter.sharedMesh;
            var combine = new CombineInstance[transforms.Count];
            for (int index = 0; index < transforms.Count; index++)
            {
                combine[index] = new CombineInstance
                {
                    mesh = sourceMesh,
                    transform = transforms[index]
                };
            }

            var combinedMesh = new Mesh
            {
                name = $"{name} Combined Mesh",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = transforms.Count * sourceMesh.vertexCount >
                              ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            combinedMesh.CombineMeshes(
                combine,
                true,
                true,
                false);
            if (worldUvTileSize.HasValue)
            {
                ApplyWorldUvs(
                    combinedMesh,
                    worldUvTileSize.Value,
                    uvMode,
                    worldUvOrigin);
            }

            combinedMesh.RecalculateBounds();
            meshFilter.sharedMesh = combinedMesh;
            result.AddComponent<RuntimeGeneratedMeshOwner>()
                .Initialize(combinedMesh);
            if (collider)
            {
                MeshCollider surfaceCollider =
                    result.AddComponent<MeshCollider>();
                surfaceCollider.sharedMesh = combinedMesh;
            }

            combinedMesh.UploadMeshData(!collider);
            return result;
        }

        private static void ApplyWorldUvs(
            Mesh mesh,
            float tileSize,
            RuntimeWorldUvMode uvMode,
            Vector3 worldUvOrigin)
        {
            Vector3[] vertices = mesh.vertices;
            var uvs = new Vector2[vertices.Length];
            float tilesPerMeter = 1f / tileSize;
            Vector3[] normals =
                uvMode == RuntimeWorldUvMode.BoxProjected
                    ? mesh.normals
                    : null;
            bool boxProjected =
                normals != null && normals.Length == vertices.Length;
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 vertex = vertices[index] + worldUvOrigin;
                uvs[index] = boxProjected
                    ? ProjectBoxUv(vertex, normals[index]) * tilesPerMeter
                    : new Vector2(
                        vertex.x * tilesPerMeter,
                        vertex.z * tilesPerMeter);
            }

            mesh.uv = uvs;
        }

        /// <summary>
        /// Chooses the projection plane from the face normal's dominant
        /// axis, so every face of a box tiles at true metre scale: tops
        /// take XZ, faces looking along X take ZY and faces looking
        /// along Z take XY. Neighbouring boxes still share world
        /// coordinates, so a batched run reads as one continuous
        /// surface rather than a repeated per-box stamp.
        /// </summary>
        internal static Vector2 ProjectBoxUv(
            Vector3 vertex,
            Vector3 normal)
        {
            float absoluteX = Mathf.Abs(normal.x);
            float absoluteY = Mathf.Abs(normal.y);
            float absoluteZ = Mathf.Abs(normal.z);
            if (absoluteY >= absoluteX && absoluteY >= absoluteZ)
            {
                return new Vector2(vertex.x, vertex.z);
            }

            return absoluteX >= absoluteZ
                ? new Vector2(vertex.z, vertex.y)
                : new Vector2(vertex.x, vertex.y);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static GameObject CreatePrimitive(
            PrimitiveType type,
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Color color,
            bool collider,
            Material sharedMaterial,
            bool applyColor = true)
        {
            GameObject result = GameObject.CreatePrimitive(type);
            result.name = name;
            result.transform.SetParent(parent, false);
            result.transform.localPosition = localPosition;
            result.transform.localScale = size;
            Renderer renderer = result.GetComponent<Renderer>();
            renderer.sharedMaterial =
                sharedMaterial != null
                    ? sharedMaterial
                    : DefaultMaterial;

            if (type == PrimitiveType.Cylinder)
            {
                MeshFilter meshFilter = result.GetComponent<MeshFilter>();
                if (meshFilter != null)
                {
                    meshFilter.sharedMesh = GetLowPolyCylinderMesh();
                }
            }

            if (applyColor)
            {
                SetColor(renderer, color);
            }

            if (!collider)
            {
                Collider primitiveCollider = result.GetComponent<Collider>();
                if (primitiveCollider != null)
                {
                    if (Application.isPlaying)
                    {
                        primitiveCollider.enabled = false;
                        Object.Destroy(primitiveCollider);
                    }
                    else
                    {
                        Object.DestroyImmediate(primitiveCollider);
                    }
                }
            }

            return result;
        }

        private static Mesh GetLowPolyCylinderMesh()
        {
            if (lowPolyCylinderMesh != null)
            {
                return lowPolyCylinderMesh;
            }

            int sideVertexCount = LowPolyCylinderSides * 4;
            int capRingVertexCount = LowPolyCylinderSides * 2;
            int vertexCount = sideVertexCount + capRingVertexCount + 2;
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            var triangles = new int[LowPolyCylinderSides * 12];

            int triangleIndex = 0;
            for (int side = 0; side < LowPolyCylinderSides; side++)
            {
                float angleA = side * Mathf.PI * 2f / LowPolyCylinderSides;
                float angleB =
                    (side + 1) * Mathf.PI * 2f / LowPolyCylinderSides;
                Vector3 bottomA = new Vector3(
                    Mathf.Cos(angleA) * 0.5f,
                    -1f,
                    Mathf.Sin(angleA) * 0.5f);
                Vector3 bottomB = new Vector3(
                    Mathf.Cos(angleB) * 0.5f,
                    -1f,
                    Mathf.Sin(angleB) * 0.5f);
                Vector3 topA = new Vector3(bottomA.x, 1f, bottomA.z);
                Vector3 topB = new Vector3(bottomB.x, 1f, bottomB.z);
                Vector3 faceNormal =
                    Vector3.Cross(topA - bottomA, bottomB - bottomA).normalized;

                int vertex = side * 4;
                vertices[vertex] = bottomA;
                vertices[vertex + 1] = topA;
                vertices[vertex + 2] = topB;
                vertices[vertex + 3] = bottomB;
                for (int offset = 0; offset < 4; offset++)
                {
                    normals[vertex + offset] = faceNormal;
                }

                float uA = side / (float)LowPolyCylinderSides;
                float uB = (side + 1f) / LowPolyCylinderSides;
                uvs[vertex] = new Vector2(uA, 0f);
                uvs[vertex + 1] = new Vector2(uA, 1f);
                uvs[vertex + 2] = new Vector2(uB, 1f);
                uvs[vertex + 3] = new Vector2(uB, 0f);

                triangles[triangleIndex++] = vertex;
                triangles[triangleIndex++] = vertex + 1;
                triangles[triangleIndex++] = vertex + 2;
                triangles[triangleIndex++] = vertex;
                triangles[triangleIndex++] = vertex + 2;
                triangles[triangleIndex++] = vertex + 3;
            }

            int topRingStart = sideVertexCount;
            int bottomRingStart = topRingStart + LowPolyCylinderSides;
            int topCenter = bottomRingStart + LowPolyCylinderSides;
            int bottomCenter = topCenter + 1;
            vertices[topCenter] = Vector3.up;
            normals[topCenter] = Vector3.up;
            uvs[topCenter] = new Vector2(0.5f, 0.5f);
            vertices[bottomCenter] = Vector3.down;
            normals[bottomCenter] = Vector3.down;
            uvs[bottomCenter] = new Vector2(0.5f, 0.5f);

            for (int side = 0; side < LowPolyCylinderSides; side++)
            {
                float angle = side * Mathf.PI * 2f / LowPolyCylinderSides;
                float x = Mathf.Cos(angle) * 0.5f;
                float z = Mathf.Sin(angle) * 0.5f;
                int top = topRingStart + side;
                int bottom = bottomRingStart + side;
                vertices[top] = new Vector3(x, 1f, z);
                normals[top] = Vector3.up;
                uvs[top] = new Vector2(x + 0.5f, z + 0.5f);
                vertices[bottom] = new Vector3(x, -1f, z);
                normals[bottom] = Vector3.down;
                uvs[bottom] = new Vector2(x + 0.5f, z + 0.5f);

                int next = (side + 1) % LowPolyCylinderSides;
                triangles[triangleIndex++] = topCenter;
                triangles[triangleIndex++] = topRingStart + next;
                triangles[triangleIndex++] = top;
                triangles[triangleIndex++] = bottomCenter;
                triangles[triangleIndex++] = bottom;
                triangles[triangleIndex++] = bottomRingStart + next;
            }

            lowPolyCylinderMesh = new Mesh
            {
                name = "Shared PS1 Eight-Sided Cylinder",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = vertices,
                normals = normals,
                uv = uvs,
                triangles = triangles
            };
            lowPolyCylinderMesh.RecalculateBounds();
            lowPolyCylinderMesh.UploadMeshData(false);
            return lowPolyCylinderMesh;
        }

        private static bool IsPositiveFinite(Vector3 size)
        {
            return IsPositiveFinite(size.x) &&
                   IsPositiveFinite(size.y) &&
                   IsPositiveFinite(size.z);
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f &&
                   !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }

    internal sealed class RuntimeGeneratedMeshOwner : MonoBehaviour
    {
        private Mesh ownedMesh;

        public void Initialize(Mesh mesh)
        {
            ownedMesh = mesh;
        }

        private void OnDestroy()
        {
            if (ownedMesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(ownedMesh);
            }
            else
            {
                Object.DestroyImmediate(ownedMesh);
            }

            ownedMesh = null;
        }
    }
}
