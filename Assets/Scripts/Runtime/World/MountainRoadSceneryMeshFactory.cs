using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    internal static class MountainRoadSceneryMeshFactory
    {
        /// <summary>
        /// Metres of slack added to the crown bounds so the wind cannot
        /// bend a tree out of its own culling volume. It is generous
        /// against the amplitude the tallest tree can reach, because the
        /// cost of being wrong is a popping stand and the cost of being
        /// generous is a few extra draws at the screen edge.
        /// </summary>
        internal const float WindCullingHeadroom = 2.5f;

        internal static Mesh CreateConiferCrowns(
            string name,
            IReadOnlyList<MountainRoadForestDescriptor> trees)
        {
            if (trees == null)
            {
                throw new ArgumentNullException(nameof(trees));
            }

            float tile = MountainRoadSurfaceAppearance.GetRecipe(
                MountainRoadSurfaceKind.ConiferNeedles).MetersPerTile;
            var vertices = new List<Vector3>(trees.Count * 96);
            var uvs = new List<Vector2>(trees.Count * 96);
            var triangles = new List<int>(trees.Count * 96);
            for (int index = 0; index < trees.Count; index++)
            {
                MountainRoadForestDescriptor tree = trees[index];
                Quaternion rotation = Quaternion.Euler(
                    0f,
                    tree.YawDegrees,
                    0f);

                // One phase per tree, taken from where it stands, so a
                // stand of crowns does not repeat the same needle patch at
                // the same place on every trunk. Altitude is folded into
                // the phase because V no longer carries it: V used to be
                // absolute world height and its fractional part is what
                // used to break the repeat VERTICALLY between neighbours.
                float phase = tree.Position.x +
                              tree.Position.z +
                              tree.Position.y * 1.37f;
                AppendCone(
                    tree.Position + Vector3.up * (tree.Height * 0.18f),
                    tree.Position.y,
                    rotation,
                    tree.CrownRadius,
                    tree.Height * 0.56f,
                    7,
                    phase,
                    tile,
                    vertices,
                    uvs,
                    triangles);
                AppendCone(
                    tree.Position + Vector3.up * (tree.Height * 0.43f),
                    tree.Position.y,
                    rotation,
                    tree.CrownRadius * 0.73f,
                    tree.Height * 0.57f,
                    7,
                    phase,
                    tile,
                    vertices,
                    uvs,
                    triangles);
            }

            Mesh mesh = CreateMesh(name, uvs, vertices, triangles);

            // The crowns bend in the shader, and Unity culls against the
            // bounds the CPU baked. Without this headroom a stand at the
            // edge of the frustum pops out the moment the wind leans it
            // back in, and the shadow pass drops it a frame before the
            // forward pass does.
            Bounds bounds = mesh.bounds;
            bounds.Expand(WindCullingHeadroom * 2f);
            mesh.bounds = bounds;
            return mesh;
        }

        internal static Mesh CreateBoulders(
            IReadOnlyList<MountainRoadMiscDescriptor> boulders,
            MountainRoadSurfaceKind surface)
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

            return CreateBoxProjectedMesh(
                "Mountain Road Boulders",
                surface,
                vertices,
                triangles);
        }

        internal static Mesh CreateRidges(
            string name,
            IReadOnlyList<MountainRoadRidgeDescriptor> ridges,
            MountainRoadSurfaceKind surface)
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

            return CreateBoxProjectedMesh(
                name,
                surface,
                vertices,
                triangles);
        }

        /// <summary>
        /// One skirt of a crown. Its UVs unroll the cone: U is the arc the
        /// vertex stands at, so a metre around the crown is a metre of
        /// sheet, and V is height above THIS TREE'S OWN FOOT, so needles
        /// never lie sideways and the two stacked skirts of one tree stay in
        /// register. A cone's facets own their vertices outright, which is
        /// why the wrap back to zero needs no seam column.
        ///
        /// V used to be absolute world height, which textures identically —
        /// the sheet only cares about the metre pitch and each tree's own
        /// phase already breaks the repeat. It is measured from the foot
        /// because <c>MountainWindSway.hlsl</c> reads the bend lever out of
        /// it, and it can only read UV0: the four passes that have to agree
        /// on the displacement (forward, shadow, depth, depth-normals) share
        /// no other vertex channel. See that file for the whole argument.
        /// </summary>
        private static void AppendCone(
            Vector3 baseCenter,
            float treeFootY,
            Quaternion rotation,
            float radius,
            float height,
            int sides,
            float phase,
            float metersPerTile,
            ICollection<Vector3> vertices,
            ICollection<Vector2> uvs,
            IList<int> triangles)
        {
            Vector3 apex = baseCenter + Vector3.up * height;
            float tilesPerMeter = 1f / metersPerTile;
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
                uvs.Add(new Vector2(
                    (phase + firstAngle * radius) * tilesPerMeter,
                    (first.y - treeFootY) * tilesPerMeter));
                uvs.Add(new Vector2(
                    (phase + (firstAngle + secondAngle) * 0.5f * radius) *
                    tilesPerMeter,
                    (apex.y - treeFootY) * tilesPerMeter));
                uvs.Add(new Vector2(
                    (phase + secondAngle * radius) * tilesPerMeter,
                    (second.y - treeFootY) * tilesPerMeter));
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

        /// <summary>
        /// Ridges and boulders have no natural unwrap, so they take the
        /// same faceted box projection the combined batches use — the plane
        /// comes from each vertex's own normal — baked at the pitch of the
        /// sheet the caller is about to put on them. The pitch is read from
        /// that kind rather than assumed, because these meshes do not all
        /// wear stone: the far ring is snow, and baking it at the stone
        /// pitch tiled it a fifth too coarsely for its own recipe. The
        /// normals are the ones the mesh already carries, so nothing about
        /// the lighting or the silhouette changes; only the UVs arrive.
        /// </summary>
        private static Mesh CreateBoxProjectedMesh(
            string name,
            MountainRoadSurfaceKind surface,
            List<Vector3> vertices,
            List<int> triangles)
        {
            Mesh mesh = CreateMesh(name, null, vertices, triangles);
            float tilesPerMeter = 1f /
                MountainRoadSurfaceAppearance.GetRecipe(
                    surface).MetersPerTile;
            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            var uvs = new Vector2[positions.Length];
            for (int index = 0; index < positions.Length; index++)
            {
                uvs[index] = RuntimePrimitiveFactory.ProjectBoxUv(
                    positions[index],
                    normals[index]) * tilesPerMeter;
            }

            mesh.uv = uvs;
            return mesh;
        }

        private static Mesh CreateMesh(
            string name,
            List<Vector2> uvs,
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
            if (uvs != null)
            {
                mesh.SetUVs(0, uvs);
            }

            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
