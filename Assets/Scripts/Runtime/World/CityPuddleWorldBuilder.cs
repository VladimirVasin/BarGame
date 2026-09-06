using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Batches the gutter puddles as one sheet of standing water.
    ///
    /// Each planner patch becomes a 3x3 grid whose TEXCOORD0.x carries
    /// the rim mask — 0 on the patch edge, 1 at its centre — which is
    /// what lets the water shader's edge noise gnaw the rectangle into
    /// an irregular shore and dry it toward the middle. All patches
    /// combine into a single mesh under the shared
    /// <see cref="CityPuddleWaterResources"/> material, so the whole
    /// city's puddles stay one draw and follow one wetness, one night
    /// factor and one rain intensity. No collider, as before: a
    /// three-millimetre film must never trip a walker.
    /// </summary>
    internal static class CityPuddleWorldBuilder
    {
        // Over the road surface the mirror probe stands at street-lamp
        // eye height: the cube it renders is what every puddle in the
        // city reflects, Morrowind-style — an environment map has no
        // parallax for a second probe to correct.
        private const float MirrorProbeHeight = 1.6f;

        public static GameObject Build(
            Transform parent,
            IReadOnlyList<RuntimeOrientedBox> patches)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (patches == null)
            {
                throw new ArgumentNullException(nameof(patches));
            }

            if (patches.Count == 0)
            {
                return null;
            }

            var result = new GameObject("Gutter Puddle Water");
            result.transform.SetParent(parent, false);
            Mesh mesh = CreateCombinedPatchMesh(patches);
            result.AddComponent<MeshFilter>().sharedMesh = mesh;
            result.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            var renderer = result.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CityPuddleWaterResources.Material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;

            var mirror = new GameObject("Puddle Reflection Mirror");
            mirror.transform.SetParent(result.transform, false);
            mirror.transform.position =
                AveragePatchCenter(patches) +
                Vector3.up * MirrorProbeHeight;
            mirror.AddComponent<CityFountainReflectionController>()
                .Initialize(CityPuddleWaterResources.Material);
            return result;
        }

        private static Vector3 AveragePatchCenter(
            IReadOnlyList<RuntimeOrientedBox> patches)
        {
            Vector3 sum = Vector3.zero;
            for (int index = 0; index < patches.Count; index++)
            {
                sum += patches[index].Center;
            }

            return sum / patches.Count;
        }

        private static Mesh CreateCombinedPatchMesh(
            IReadOnlyList<RuntimeOrientedBox> patches)
        {
            var vertices = new List<Vector3>(patches.Count * 9);
            var normals = new List<Vector3>(patches.Count * 9);
            var uvs = new List<Vector2>(patches.Count * 9);
            var triangles = new List<int>(patches.Count * 24);
            for (int index = 0; index < patches.Count; index++)
            {
                AppendPatch(
                    patches[index],
                    vertices,
                    normals,
                    uvs,
                    triangles);
            }

            var mesh = new Mesh
            {
                name = "City Puddle Water Sheet",
                hideFlags = HideFlags.HideAndDontSave
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            // Headroom for the film's few millimetres of breathing and
            // the shader's vertex displacement.
            Bounds bounds = mesh.bounds;
            bounds.Expand(new Vector3(0f, 0.2f, 0f));
            mesh.bounds = bounds;
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static void AppendPatch(
            RuntimeOrientedBox patch,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles)
        {
            int baseIndex = vertices.Count;
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    var local = new Vector3(
                        (column - 1) * 0.5f * patch.Size.x,
                        0f,
                        (row - 1) * 0.5f * patch.Size.z);
                    vertices.Add(
                        patch.Center + (patch.Rotation * local));
                    normals.Add(Vector3.up);

                    // The rim mask: 1 only at the centre vertex, 0 on
                    // the whole rim. Bilinear interpolation turns it
                    // into a pyramid the shader erodes from the edge
                    // inward.
                    bool centre = row == 1 && column == 1;
                    uvs.Add(new Vector2(centre ? 1f : 0f, 0f));
                }
            }

            for (int row = 0; row < 2; row++)
            {
                for (int column = 0; column < 2; column++)
                {
                    int corner = baseIndex + (row * 3) + column;
                    triangles.Add(corner);
                    triangles.Add(corner + 3);
                    triangles.Add(corner + 4);
                    triangles.Add(corner);
                    triangles.Add(corner + 4);
                    triangles.Add(corner + 1);
                }
            }
        }
    }
}
