using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    public static class RuntimePrimitiveFactory
    {
        private const int LowPolyCylinderSides = 8;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static Mesh lowPolyCylinderMesh;

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

        public static void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            MaterialPropertyBlock properties = new MaterialPropertyBlock();
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

            GameObject result = CreatePrimitive(
                PrimitiveType.Cube,
                name,
                parent,
                Vector3.zero,
                Vector3.one,
                color,
                false,
                sharedMaterial);
            MeshFilter meshFilter =
                result.GetComponent<MeshFilter>();
            Mesh sourceMesh = meshFilter.sharedMesh;
            var combine = new CombineInstance[boxes.Count];
            for (int index = 0; index < boxes.Count; index++)
            {
                Bounds box = boxes[index];
                Vector3 size = box.size;
                if (!IsPositiveFinite(size))
                {
                    if (Application.isPlaying)
                    {
                        Object.Destroy(result);
                    }
                    else
                    {
                        Object.DestroyImmediate(result);
                    }

                    throw new ArgumentException(
                        "Combined boxes require finite positive dimensions.",
                        nameof(boxes));
                }

                combine[index] = new CombineInstance
                {
                    mesh = sourceMesh,
                    transform = Matrix4x4.TRS(
                        box.center,
                        Quaternion.identity,
                        size)
                };
            }

            var combinedMesh = new Mesh
            {
                name = $"{name} Combined Mesh",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = boxes.Count * sourceMesh.vertexCount >
                              ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            combinedMesh.CombineMeshes(
                combine,
                true,
                true,
                false);
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

        private static GameObject CreatePrimitive(
            PrimitiveType type,
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Color color,
            bool collider,
            Material sharedMaterial)
        {
            GameObject result = GameObject.CreatePrimitive(type);
            result.name = name;
            result.transform.SetParent(parent, false);
            result.transform.localPosition = localPosition;
            result.transform.localScale = size;
            Renderer renderer = result.GetComponent<Renderer>();
            if (sharedMaterial != null)
            {
                renderer.sharedMaterial = sharedMaterial;
            }

            if (type == PrimitiveType.Cylinder)
            {
                MeshFilter meshFilter = result.GetComponent<MeshFilter>();
                if (meshFilter != null)
                {
                    meshFilter.sharedMesh = GetLowPolyCylinderMesh();
                }
            }

            SetColor(renderer, color);

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
