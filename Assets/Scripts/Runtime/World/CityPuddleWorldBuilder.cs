using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// Batches puddles as top-only quads. Their dry recipe is identical to the
    /// road beneath, while the weather registry turns the same world-aligned
    /// asphalt sample into a darker, glossier film under rain.
    /// </summary>
    internal static class CityPuddleWorldBuilder
    {
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

            Mesh source = CreatePatchMesh();
            var placements = new List<RuntimeMeshPlacement>(patches.Count);
            for (int index = 0; index < patches.Count; index++)
            {
                RuntimeOrientedBox patch = patches[index];
                placements.Add(
                    new RuntimeMeshPlacement(
                        source,
                        patch.Center,
                        patch.Rotation,
                        new Vector3(patch.Size.x, 1f, patch.Size.z)));
            }

            GameObject result = RuntimePrimitiveFactory.CreateCombinedMeshes(
                "Gutter Puddle Patches",
                parent,
                placements,
                Color.white,
                false,
                CityExteriorAppearance.RoadTextureTileSize);
            Object.Destroy(source);
            var renderer = result.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            CityExteriorAppearance.ApplyPuddleSurface(renderer);
            return result;
        }

        private static Mesh CreatePatchMesh()
        {
            var mesh = new Mesh
            {
                name = "City Puddle Source Quad",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[]
                {
                    new Vector3(-0.5f, 0f, -0.5f),
                    new Vector3(-0.5f, 0f, 0.5f),
                    new Vector3(0.5f, 0f, 0.5f),
                    new Vector3(0.5f, 0f, -0.5f)
                },
                normals = new[]
                {
                    Vector3.up,
                    Vector3.up,
                    Vector3.up,
                    Vector3.up
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
