using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Builds the single-sided, subdivided sheet a water shader displaces.
    ///
    /// The river used to be a stretched <see cref="PrimitiveType.Cube"/>,
    /// which is eight vertices: there is nowhere for a wave to go. This
    /// emits a grid instead, at a metre pitch coarse enough to stay cheap
    /// and fine enough that a two-metre wave reads as a curve rather than
    /// a fold.
    ///
    /// Only the top face is emitted. The old box's sides and underside
    /// were never visible - the quay walls cover the sides and nothing
    /// ever looks up through a river - and a transparent surface that
    /// carries its own back faces sorts against itself for nothing.
    ///
    /// Vertices carry no UVs. The water shader derives them from world
    /// position, which is what keeps a segment boundary invisible: two
    /// adjacent sheets agree on the wave and on the ripple because both
    /// are functions of where they are, not of how they were cut. It is
    /// also why this factory serves the sea as well as the river,
    /// though their footprints are nothing alike.
    ///
    /// No property block is written, for the same reason
    /// <see cref="RuntimePrimitiveFactory.CreateMaterialBox"/> writes
    /// none: the material is shared and driven whole, by night factor and
    /// rain intensity, and a block would shadow that.
    /// </summary>
    internal static class CityWaterSurfaceFactory
    {
        /// <summary>
        /// The grid pitch, in metres. At the default `10 m` channel this
        /// is ten quads across - enough for the wave, and small enough
        /// that a segment costs a few hundred triangles rather than a few
        /// thousand. The `640x360` composite cannot resolve better.
        /// </summary>
        internal const float SurfacePitch = 1.0f;

        /// <summary>
        /// Headroom the renderer bounds reserve above and below the flat
        /// sheet for the shader's vertical displacement. Kept a good
        /// margin above the shader's authored wave height so tuning the
        /// wave never silently reintroduces culling pops.
        /// </summary>
        internal const float CrestAllowance = 0.25f;

        private const int MinimumSpans = 1;

        /// <summary>
        /// One sloped water sheet spanning <paramref name="bounds"/>,
        /// its top running from <paramref name="southTopY"/> at
        /// <c>bounds.yMin</c> to <paramref name="northTopY"/> at
        /// <c>bounds.yMax</c>.
        /// </summary>
        internal static GameObject CreateSlopedSurface(
            string name,
            Transform parent,
            Rect bounds,
            float southTopY,
            float northTopY,
            Material sharedMaterial)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (sharedMaterial == null)
            {
                throw new ArgumentNullException(nameof(sharedMaterial));
            }

            if (!(bounds.width > 0f) || !(bounds.height > 0f))
            {
                throw new ArgumentException(
                    "A water surface needs a positive footprint.",
                    nameof(bounds));
            }

            int columns = ResolveSpans(bounds.width);
            int rows = ResolveSpans(bounds.height);
            var vertices = new Vector3[(columns + 1) * (rows + 1)];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[columns * rows * 6];

            // Local space is centred on the footprint so the renderer's
            // bounds stay symmetric and the transform reads as the patch
            // it is. World position - which the shader wants - comes back
            // out through the object-to-world matrix.
            Vector3 origin = new Vector3(
                bounds.center.x,
                (southTopY + northTopY) * 0.5f,
                bounds.center.y);
            for (int row = 0; row <= rows; row++)
            {
                float alongZ = (float)row / rows;
                float z = Mathf.Lerp(bounds.yMin, bounds.yMax, alongZ);
                float topY = Mathf.Lerp(southTopY, northTopY, alongZ);
                for (int column = 0; column <= columns; column++)
                {
                    float x = Mathf.Lerp(
                        bounds.xMin,
                        bounds.xMax,
                        (float)column / columns);
                    int index = row * (columns + 1) + column;
                    vertices[index] = new Vector3(x, topY, z) - origin;

                    // Flat up, not the slope's true normal. The shader
                    // replaces this wholesale with the analytic wave
                    // normal; what matters here is only that the sheet
                    // faces the sky, and a fall of a couple of metres
                    // over the whole river is not a tilt anybody sees.
                    normals[index] = Vector3.up;
                }
            }

            int triangle = 0;
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int southWest = row * (columns + 1) + column;
                    int southEast = southWest + 1;
                    int northWest = southWest + columns + 1;
                    int northEast = northWest + 1;
                    triangles[triangle++] = southWest;
                    triangles[triangle++] = northWest;
                    triangles[triangle++] = southEast;
                    triangles[triangle++] = southEast;
                    triangles[triangle++] = northWest;
                    triangles[triangle++] = northEast;
                }
            }

            return CreateSheet(
                name,
                parent,
                origin,
                vertices,
                normals,
                triangles,
                sharedMaterial);
        }

        private static GameObject CreateSheet(
            string name,
            Transform parent,
            Vector3 origin,
            Vector3[] vertices,
            Vector3[] normals,
            int[] triangles,
            Material sharedMaterial)
        {
            var mesh = new Mesh
            {
                name = $"{name} Water Mesh",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = vertices.Length > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0, true);

            // The wave displaces vertices the bounds know nothing about,
            // so the sheet would cull itself at a grazing angle from the
            // promenade. Growing the bounds by the crest height is
            // cheaper than making the shader clamp.
            Bounds meshBounds = mesh.bounds;
            meshBounds.Expand(new Vector3(0f, CrestAllowance * 2f, 0f));
            mesh.bounds = meshBounds;

            var result = new GameObject(name);
            result.transform.SetParent(parent, false);
            result.transform.localPosition = origin;
            result.AddComponent<MeshFilter>().sharedMesh = mesh;
            result.AddComponent<MeshRenderer>().sharedMaterial =
                sharedMaterial;
            result.AddComponent<RuntimeGeneratedMeshOwner>()
                .Initialize(mesh);
            mesh.UploadMeshData(true);
            return result;
        }

        private static int ResolveSpans(float extent)
        {
            return Mathf.Max(
                MinimumSpans,
                Mathf.RoundToInt(extent / SurfacePitch));
        }
    }
}
