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
        /// <summary>
        /// One run of the cafe's shell. `surface` names the sheet its wide
        /// face carries; a segment with no sheet - the glazing - passes
        /// null and keeps both its flat colour and its own material, which
        /// the recipe path would otherwise replace with the shared lit one.
        /// </summary>
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
            bool collider,
            MountainRoadSurfaceKind? surface = null)
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
            if (surface.HasValue)
            {
                // A run's readable face is the one along its length and up
                // its height, whatever its depth happens to be.
                MountainRoadSurfaceAppearance.Apply(
                    box.GetComponent<Renderer>(),
                    surface.Value,
                    SurfaceProjection.BoxXY,
                    color);
            }

            return box;
        }

        internal static GameObject CreatePolygonSurface(
            string name,
            Transform parent,
            IReadOnlyList<Vector2> footprint,
            float y,
            float metersPerTile,
            MountainRoadSurfaceKind surface,
            Color color)
        {
            int count = footprint.Count;
            float tilesPerMeter = 1f / metersPerTile;
            var vertices = new Vector3[count];
            var normals = new Vector3[count];
            var uvs = new Vector2[count];
            for (int index = 0; index < count; index++)
            {
                Vector2 point = footprint[index];
                vertices[index] = new Vector3(point.x, y, point.y);
                normals[index] = Vector3.up;
                uvs[index] = point * tilesPerMeter;
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
            return CreateMeshObject(
                name,
                parent,
                mesh,
                surface,
                color,
                false);
        }

        /// <summary>
        /// An extruded footprint whose caps and sides are unwrapped apart.
        ///
        /// The caps take the world-XZ projection a flat slab wants. The
        /// sides cannot: sharing the caps' vertices gives the top and the
        /// bottom of every side face the same UV, so each face samples one
        /// horizontal line of the sheet and smears it down its whole height.
        /// The sides therefore carry their own vertices, unwrapped as
        /// distance around the perimeter against world height. Per-face
        /// vertices also give the slab the crisp arris a building edge has,
        /// instead of the bevel that averaging a cap normal into a side
        /// normal used to produce.
        /// </summary>
        internal static GameObject CreatePrism(
            string name,
            Transform parent,
            IReadOnlyList<Vector2> footprint,
            float bottom,
            float top,
            float metersPerTile,
            MountainRoadSurfaceKind surface,
            Color color,
            bool collider)
        {
            int count = footprint.Count;
            float tilesPerMeter = 1f / metersPerTile;
            var vertices = new Vector3[count * 2 + count * 4];
            var uvs = new Vector2[vertices.Length];
            for (int index = 0; index < count; index++)
            {
                Vector2 point = footprint[index];
                vertices[index] = new Vector3(point.x, bottom, point.y);
                vertices[index + count] =
                    new Vector3(point.x, top, point.y);
                uvs[index] = point * tilesPerMeter;
                uvs[index + count] = point * tilesPerMeter;
            }

            int wallStart = count * 2;
            float run = 0f;
            for (int index = 0; index < count; index++)
            {
                Vector2 near = footprint[index];
                Vector2 far = footprint[(index + 1) % count];
                float length = Vector2.Distance(near, far);

                // U keeps running around the perimeter across the unwelded
                // corners, so neighbouring faces still line up even though
                // they no longer share a vertex.
                float nearU = run * tilesPerMeter;
                float farU = (run + length) * tilesPerMeter;
                int first = wallStart + index * 4;
                vertices[first] = new Vector3(near.x, bottom, near.y);
                vertices[first + 1] = new Vector3(near.x, top, near.y);
                vertices[first + 2] = new Vector3(far.x, bottom, far.y);
                vertices[first + 3] = new Vector3(far.x, top, far.y);
                uvs[first] = new Vector2(nearU, bottom * tilesPerMeter);
                uvs[first + 1] = new Vector2(nearU, top * tilesPerMeter);
                uvs[first + 2] = new Vector2(farU, bottom * tilesPerMeter);
                uvs[first + 3] = new Vector2(farU, top * tilesPerMeter);
                run += length;
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
                int first = wallStart + index * 4;
                triangles[triangle++] = first;
                triangles[triangle++] = first + 3;
                triangles[triangle++] = first + 2;
                triangles[triangle++] = first;
                triangles[triangle++] = first + 1;
                triangles[triangle++] = first + 3;
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
            return CreateMeshObject(
                name,
                parent,
                mesh,
                surface,
                color,
                collider);
        }

        private static GameObject CreateMeshObject(
            string name,
            Transform parent,
            Mesh mesh,
            MountainRoadSurfaceKind surface,
            Color color,
            bool collider)
        {
            var result = new GameObject(name);
            result.transform.SetParent(parent, false);
            MeshFilter filter = result.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = result.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            RuntimePrimitiveFactory.SetColor(renderer, color);

            // Both meshes bake their UVs at the recipe's pitch, so they
            // take the combined path and add no _BaseMap_ST of their own.
            MountainRoadSurfaceAppearance.ApplyCombined(
                renderer,
                surface,
                color);
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
