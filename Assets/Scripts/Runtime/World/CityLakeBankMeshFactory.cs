using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Builds the two lake surfaces that are neither boxes nor water:
    /// the bank ring a person walks down, and the bed cap the water is
    /// seen through.
    ///
    /// This is a separate factory from
    /// <see cref="CityWaterSurfaceFactory"/> because the two have
    /// opposite obligations. A water sheet must never carry a collider;
    /// the bank must, because it is ground, and because it is the thing
    /// that replaces the generic guard rail the shore used to get. The
    /// bed must not, because nothing may stand on the bottom of a pond.
    ///
    /// Both are laid with world-planar XZ UVs at the bank sheet's metre
    /// pitch, the same rule the combined box batches follow, so the
    /// bank, the bed and the flat shore ring beyond them read as one
    /// continuous ground rather than as three surfaces that happen to
    /// meet.
    /// </summary>
    internal static class CityLakeBankMeshFactory
    {
        /// <summary>
        /// The bank ring: three loops around the octagon - the outer
        /// edge at shore level, the slope crest at shore level, and the
        /// toe just above the water - triangulated into a band that
        /// falls to the waterline.
        ///
        /// The outer loop follows the water cells' own rectangle rather
        /// than the octagon, so the ring meets the flat shore slabs on a
        /// straight line with no gap, and the corners fill in as the
        /// band widens toward the cut.
        /// </summary>
        internal static GameObject CreateBankRing(
            string name,
            Transform parent,
            in CityLakeBasin basin,
            Color tint)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            Vector2[] waterline = SubdivideForCorners(
                basin.CreateWaterlinePolygon());
            if (waterline.Length < 3)
            {
                throw new ArgumentException(
                    "A bank ring needs a closed waterline.",
                    nameof(basin));
            }

            Rect cells = basin.WaterCellBounds;
            Vector2 centre = cells.center;
            float toeY = basin.WaterTopY + 0.06f;
            float crestInset = basin.BankSlopeWidth;

            int ring = waterline.Length;
            var vertices = new List<Vector3>(ring * 3);
            var normals = new List<Vector3>(ring * 3);
            var uvs = new List<Vector2>(ring * 3);
            var triangles = new List<int>(ring * 12);

            // Loop 0: the outer edge, pushed back out to the cell
            // rectangle so the ring closes against the shore slabs.
            // Loop 1: the crest, where the ground starts to fall.
            // Loop 2: the toe, at the waterline.
            for (int index = 0; index < ring; index++)
            {
                Vector2 toe = waterline[index];
                Vector2 outward = (toe - centre).normalized;
                Vector2 crest = toe + outward * crestInset;
                Vector2 outer = ProjectToRect(toe, centre, cells);

                AddVertex(vertices, normals, uvs, outer, basin.BankTopY);
                AddVertex(vertices, normals, uvs, crest, basin.BankTopY);
                AddVertex(vertices, normals, uvs, toe, toeY);
            }

            for (int index = 0; index < ring; index++)
            {
                int here = index * 3;
                int next = ((index + 1) % ring) * 3;
                for (int band = 0; band < 2; band++)
                {
                    int a = here + band;
                    int b = here + band + 1;
                    int c = next + band;
                    int d = next + band + 1;

                    // Wound so the band faces the sky: the outer loop
                    // runs counter-clockwise in XZ, so the inner edge of
                    // each quad leads.
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(d);
                }
            }

            var mesh = new Mesh
            {
                name = $"{name} Mesh",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var result = new GameObject(name);
            result.transform.SetParent(parent, false);
            result.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = result.AddComponent<MeshRenderer>();

            // A real collider, because this is the ground the player
            // walks down to the water on. It is also what makes the
            // authored revetment - rather than a generic rail - the
            // thing that stops them.
            MeshCollider collider = result.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            result.AddComponent<RuntimeGeneratedMeshOwner>()
                .Initialize(mesh);
            CityLakeSurfaceAppearance.ApplyCombined(
                renderer,
                CityLakeSurfaceKind.Bank,
                tint);
            mesh.UploadMeshData(false);
            return result;
        }

        /// <summary>
        /// The bed: the waterline octagon, grown a little so the
        /// revetment laps it, laid flat at the bed datum.
        ///
        /// Colliderless on purpose. Nothing may stand on the bottom of
        /// the pond; this exists so the transparent water has something
        /// behind it. Without it the shader's absorption blends toward
        /// the skybox and the pond reads as a hole in the world - which
        /// is what the river discovered the first time it was built.
        /// </summary>
        internal static GameObject CreateBedCap(
            string name,
            Transform parent,
            in CityLakeBasin basin,
            Color tint)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            Vector2[] waterline = basin.CreateWaterlinePolygon();
            Vector2 centre = basin.WaterlineBounds.center;
            const float lap = 0.34f;

            var vertices = new List<Vector3>(waterline.Length + 1);
            var normals = new List<Vector3>(waterline.Length + 1);
            var uvs = new List<Vector2>(waterline.Length + 1);
            var triangles = new List<int>(waterline.Length * 3);

            AddVertex(vertices, normals, uvs, centre, basin.BedTopY);
            for (int index = 0; index < waterline.Length; index++)
            {
                Vector2 outward = (waterline[index] - centre).normalized;
                AddVertex(
                    vertices,
                    normals,
                    uvs,
                    waterline[index] + outward * lap,
                    basin.BedTopY);
            }

            for (int index = 0; index < waterline.Length; index++)
            {
                triangles.Add(0);
                triangles.Add(1 + index);
                triangles.Add(1 + (index + 1) % waterline.Length);
            }

            var mesh = new Mesh
            {
                name = $"{name} Mesh",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var result = new GameObject(name);
            result.transform.SetParent(parent, false);
            result.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = result.AddComponent<MeshRenderer>();
            result.AddComponent<RuntimeGeneratedMeshOwner>()
                .Initialize(mesh);
            CityLakeSurfaceAppearance.ApplyCombined(
                renderer,
                CityLakeSurfaceKind.Bank,
                tint);
            mesh.UploadMeshData(false);
            return result;
        }

        /// <summary>
        /// Splits every waterline edge at its midpoint.
        ///
        /// The outer loop is cast outward from the basin's centre, so
        /// each inner vertex needs an outer partner in the same
        /// direction. With only the octagon's own eight vertices the
        /// four rectangle corners have no partner and the band cuts
        /// them off, leaving a hole where the bank should be. The
        /// midpoint of each cut corner points at 45 degrees, straight
        /// at the rectangle corner, which is exactly the partner those
        /// corners were missing.
        /// </summary>
        private static Vector2[] SubdivideForCorners(Vector2[] polygon)
        {
            var result = new Vector2[polygon.Length * 2];
            for (int index = 0; index < polygon.Length; index++)
            {
                Vector2 from = polygon[index];
                Vector2 to = polygon[(index + 1) % polygon.Length];
                result[index * 2] = from;
                result[index * 2 + 1] = (from + to) * 0.5f;
            }

            return result;
        }

        private static void AddVertex(
            ICollection<Vector3> vertices,
            ICollection<Vector3> normals,
            ICollection<Vector2> uvs,
            Vector2 xz,
            float y)
        {
            vertices.Add(new Vector3(xz.x, y, xz.y));
            normals.Add(Vector3.up);

            // World-planar UVs at the bank sheet's pitch, the same rule
            // RuntimePrimitiveFactory bakes into the combined batches.
            float pitch = CityLakeSurfaceAppearance
                .GetRecipe(CityLakeSurfaceKind.Bank)
                .MetersPerTile;
            uvs.Add(new Vector2(xz.x / pitch, xz.y / pitch));
        }

        /// <summary>
        /// Casts a ray from the basin's centre through a waterline point
        /// out to the enclosing cell rectangle. This is what puts the
        /// ring's outer loop on a straight cell edge while its inner
        /// loop follows the cut octagon.
        /// </summary>
        private static Vector2 ProjectToRect(
            Vector2 point,
            Vector2 centre,
            Rect rect)
        {
            Vector2 direction = point - centre;
            if (direction.sqrMagnitude <= 1e-6f)
            {
                return point;
            }

            float scale = float.PositiveInfinity;
            if (Mathf.Abs(direction.x) > 1e-4f)
            {
                float edge = direction.x > 0f ? rect.xMax : rect.xMin;
                scale = Mathf.Min(scale, (edge - centre.x) / direction.x);
            }

            if (Mathf.Abs(direction.y) > 1e-4f)
            {
                float edge = direction.y > 0f ? rect.yMax : rect.yMin;
                scale = Mathf.Min(scale, (edge - centre.y) / direction.y);
            }

            if (float.IsInfinity(scale) || scale <= 0f)
            {
                return point;
            }

            return centre + direction * scale;
        }
    }
}
