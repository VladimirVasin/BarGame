using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Focused runtime mesh and oriented-segment helpers for the five-sided
    /// cafe. Generated meshes keep the same lifetime owner as other runtime
    /// world geometry and never create a material instance.
    /// </summary>
    internal static class MountainRoadCafeGeometry
    {
        internal static GameObject CreateSegmentBox(
            string name,
            Transform parent,
            Vector3 first,
            Vector3 second,
            float centerY,
            float height,
            float depth,
            Color color,
            Material sharedMaterial,
            bool collider)
        {
            Vector3 segment = second - first;
            segment.y = 0f;
            float length = segment.magnitude;
            if (length <= 0.001f)
            {
                throw new ArgumentException(
                    "A cafe segment requires two distinct endpoints.");
            }

            Vector3 direction = segment / length;
            Vector3 center = (first + second) * 0.5f;
            center.y = centerY;
            GameObject box = sharedMaterial == null
                ? RuntimePrimitiveFactory.CreateBox(
                    name,
                    parent,
                    center,
                    new Vector3(length, height, depth),
                    color,
                    collider)
                : RuntimePrimitiveFactory.CreateBox(
                    name,
                    parent,
                    center,
                    new Vector3(length, height, depth),
                    color,
                    sharedMaterial,
                    collider);
            box.transform.rotation = Quaternion.LookRotation(
                Vector3.Cross(direction, Vector3.up),
                Vector3.up);
            return box;
        }

        internal static GameObject CreatePolygonSurface(
            string name,
            Transform parent,
            IReadOnlyList<Vector2> footprint,
            float y,
            Color color)
        {
            int count = footprint.Count;
            var vertices = new Vector3[count];
            var normals = new Vector3[count];
            var uvs = new Vector2[count];
            for (int index = 0; index < count; index++)
            {
                Vector2 point = footprint[index];
                vertices[index] = new Vector3(point.x, y, point.y);
                normals[index] = Vector3.up;
                uvs[index] = point * 0.42f;
            }

            var triangles = new int[(count - 2) * 3];
            int triangle = 0;
            for (int index = 1; index < count - 1; index++)
            {
                triangles[triangle++] = 0;
                triangles[triangle++] = index + 1;
                triangles[triangle++] = index;
            }

            var mesh = new Mesh
            {
                name = name + " Runtime Mesh",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = vertices,
                normals = normals,
                uv = uvs,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            return CreateMeshObject(name, parent, mesh, color, false);
        }

        internal static GameObject CreatePrism(
            string name,
            Transform parent,
            IReadOnlyList<Vector2> footprint,
            float bottom,
            float top,
            Color color,
            bool collider)
        {
            int count = footprint.Count;
            var vertices = new Vector3[count * 2];
            var uvs = new Vector2[count * 2];
            for (int index = 0; index < count; index++)
            {
                Vector2 point = footprint[index];
                vertices[index] = new Vector3(point.x, bottom, point.y);
                vertices[index + count] =
                    new Vector3(point.x, top, point.y);
                uvs[index] = point * 0.36f;
                uvs[index + count] = point * 0.36f;
            }

            var triangles = new int[(count - 2) * 6 + count * 6];
            int triangle = 0;
            for (int index = 1; index < count - 1; index++)
            {
                triangles[triangle++] = 0;
                triangles[triangle++] = index;
                triangles[triangle++] = index + 1;
                triangles[triangle++] = count;
                triangles[triangle++] = count + index + 1;
                triangles[triangle++] = count + index;
            }

            for (int index = 0; index < count; index++)
            {
                int next = (index + 1) % count;
                triangles[triangle++] = index;
                triangles[triangle++] = next + count;
                triangles[triangle++] = next;
                triangles[triangle++] = index;
                triangles[triangle++] = index + count;
                triangles[triangle++] = next + count;
            }

            var mesh = new Mesh
            {
                name = name + " Runtime Mesh",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = vertices,
                uv = uvs,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return CreateMeshObject(name, parent, mesh, color, collider);
        }

        private static GameObject CreateMeshObject(
            string name,
            Transform parent,
            Mesh mesh,
            Color color,
            bool collider)
        {
            var result = new GameObject(name);
            result.transform.SetParent(parent, false);
            MeshFilter filter = result.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = result.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            RuntimePrimitiveFactory.SetColor(renderer, color);
            if (collider)
            {
                result.AddComponent<MeshCollider>().sharedMesh = mesh;
            }

            result.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            mesh.UploadMeshData(!collider);
            return result;
        }
    }
}
