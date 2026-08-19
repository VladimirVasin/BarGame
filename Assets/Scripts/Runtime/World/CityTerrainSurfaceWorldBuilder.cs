using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Materializes the continuous city-terrain contract as one static mesh.
    /// The mesh owns only its upward-facing terrain skin: authored retaining
    /// walls and safety rails remain responsible for deliberate exposed drops.
    /// </summary>
    internal static class CityTerrainSurfaceWorldBuilder
    {
        private const float MaximumTriangleSpan = 3f;
        private const float MinimumPatchSize = 0.001f;
        private const float CoordinateTolerance = 0.0001f;
        private const int DiscSegments = 16;
        private const int DiscRings = 4;

        internal static GameObject Build(
            string name,
            Transform parent,
            CityLayout layout,
            CitySurfaceKind kind,
            Color color,
            bool applyGroundAppearance,
            float? worldUvTileSize = null,
            IReadOnlyList<Rect> excavations = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "A terrain surface name is required.",
                    nameof(name));
            }

            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            Mesh mesh = CreateMesh(
                name,
                layout,
                kind,
                worldUvTileSize,
                excavations);
            if (mesh == null)
            {
                return null;
            }

            var result = new GameObject(name);
            result.transform.SetParent(parent, false);
            MeshFilter filter = result.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = result.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            RuntimePrimitiveFactory.SetColor(renderer, color);
            if (applyGroundAppearance)
            {
                CityExteriorAppearance.ApplyGroundSurface(renderer);
            }

            MeshCollider terrainCollider =
                result.AddComponent<MeshCollider>();
            terrainCollider.sharedMesh = mesh;
            result.AddComponent<RuntimeGeneratedMeshOwner>()
                .Initialize(mesh);
            mesh.UploadMeshData(false);
            return result;
        }

        /// <summary>
        /// Re-skins a terrain surface built earlier with a different
        /// set of excavations cut out of it. The renderer, its
        /// property block and the place in the hierarchy all survive:
        /// only the mesh under the filter and the collider is
        /// replaced, and the one it replaces dies with it.
        ///
        /// This is what digging a grave does to the ground. The hole
        /// is a rectangle missing from the skin, not a decal laid over
        /// it.
        /// </summary>
        internal static bool TryRebuild(
            GameObject target,
            CityLayout layout,
            CitySurfaceKind kind,
            float? worldUvTileSize,
            IReadOnlyList<Rect> excavations)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            MeshFilter filter = target.GetComponent<MeshFilter>();
            if (filter == null)
            {
                return false;
            }

            Mesh mesh = CreateMesh(
                target.name,
                layout,
                kind,
                worldUvTileSize,
                excavations);
            if (mesh == null)
            {
                return false;
            }

            Mesh previous = filter.sharedMesh;
            filter.sharedMesh = mesh;
            MeshCollider collider = target.GetComponent<MeshCollider>();
            if (collider != null)
            {
                collider.sharedMesh = mesh;
            }

            var owner =
                target.GetComponent<RuntimeGeneratedMeshOwner>();
            if (owner == null)
            {
                owner =
                    target.AddComponent<RuntimeGeneratedMeshOwner>();
            }

            owner.Initialize(mesh);
            mesh.UploadMeshData(false);
            if (previous != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(previous);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(previous);
                }
            }

            return true;
        }

        /// <summary>
        /// The upward-facing skin of one surface kind, with the
        /// authored stair and river cuts and any runtime excavations
        /// subtracted. Null when the kind covers no ground at all.
        /// </summary>
        private static Mesh CreateMesh(
            string name,
            CityLayout layout,
            CitySurfaceKind kind,
            float? worldUvTileSize,
            IReadOnlyList<Rect> excavations)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            float tileSize = ResolveTileSize(worldUvTileSize);
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int surfaceIndex = 0;
                 surfaceIndex < layout.Surfaces.Count;
                 surfaceIndex++)
            {
                CitySurfaceDescriptor surface =
                    layout.Surfaces[surfaceIndex];
                if (surface.Kind != kind ||
                    !CityTerrainSurfacePlan.UsesContinuousTop(surface))
                {
                    continue;
                }

                List<Rect> patches = CreateSurfacePatches(
                    layout,
                    surface,
                    excavations);
                for (int patchIndex = 0;
                     patchIndex < patches.Count;
                     patchIndex++)
                {
                    AppendPatch(
                        layout,
                        surface,
                        patches[patchIndex],
                        tileSize,
                        vertices,
                        normals,
                        uvs,
                        triangles);
                }
            }

            if (triangles.Count == 0)
            {
                return null;
            }

            var mesh = new Mesh
            {
                name = $"{name} Continuous Terrain Mesh",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = vertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The metre pitch this terrain mesh bakes into its UVs. Most
        /// surfaces share the city ground sheet's pitch; a surface with
        /// its own packaged sheet - the park lawn, its plazas - passes
        /// the pitch that sheet was measured at.
        /// </summary>
        private static float ResolveTileSize(float? worldUvTileSize)
        {
            if (!worldUvTileSize.HasValue)
            {
                return CityExteriorAppearance.GroundTextureTileSize;
            }

            float tileSize = worldUvTileSize.Value;
            if (tileSize <= 0f ||
                float.IsNaN(tileSize) ||
                float.IsInfinity(tileSize))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(worldUvTileSize),
                    "A terrain UV tile size must be finite and positive.");
            }

            return tileSize;
        }

        internal static List<Rect> CreateSurfacePatches(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            IReadOnlyList<Rect> excavations = null)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var patches = new List<Rect> { surface.WorldBounds };
            // Runtime holes come out first. A dug grave is a rectangle
            // missing from the skin exactly like an authored stair cut,
            // and it has to survive everything the authored cuts do to
            // the patch list afterwards.
            if (excavations != null)
            {
                for (int index = 0; index < excavations.Count; index++)
                {
                    SubtractFromPatches(
                        patches,
                        excavations[index],
                        surface.WorldBounds);
                }
            }

            for (int stairIndex = 0;
                 stairIndex < layout.ElevationPlan.SignatureStairs.Count;
                 stairIndex++)
            {
                CityElevationStairPlacement placement =
                    CityElevationStairPlacementPlanner.Create(
                        layout,
                        layout.ElevationPlan.SignatureStairs[stairIndex]);
                SubtractFromPatches(
                    patches,
                    placement.GroundCutFootprint,
                    surface.WorldBounds);
            }

            if (surface.Kind == CitySurfaceKind.RiverWater ||
                !layout.River.IsEnabled)
            {
                return patches;
            }

            for (int segmentIndex = 0;
                 segmentIndex < layout.River.Segments.Count;
                 segmentIndex++)
            {
                SubtractFromPatches(
                    patches,
                    layout.River.Segments[segmentIndex].WaterBounds,
                    surface.WorldBounds);
            }

            return patches;
        }

        internal static GameObject BuildConformingDisc(
            string name,
            Transform parent,
            CityLayout layout,
            Vector3 center,
            float radius,
            float topOffset,
            float thickness,
            Color color,
            float? worldUvTileSize = null)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (radius <= 0f || thickness <= topOffset)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(radius),
                    "A conforming disc requires positive radius and a " +
                    "thickness deeper than its top offset.");
            }

            int surfaceVertexCount = 1 + DiscSegments * DiscRings;
            var vertices = new List<Vector3>(
                surfaceVertexCount * 2 + DiscSegments * 2);
            var normals = new List<Vector3>(vertices.Capacity);
            var uvs = new List<Vector2>(vertices.Capacity);
            var triangles = new List<int>(DiscSegments * DiscRings * 12);
            float tilesPerMeter = 1f / ResolveTileSize(worldUvTileSize);

            AppendDiscSurfaceVertices(
                layout,
                center,
                radius,
                topOffset,
                false,
                tilesPerMeter,
                vertices,
                normals,
                uvs);
            AppendDiscSurfaceVertices(
                layout,
                center,
                radius,
                topOffset - thickness,
                true,
                tilesPerMeter,
                vertices,
                normals,
                uvs);
            AppendDiscSurfaceTriangles(0, false, triangles);
            AppendDiscSurfaceTriangles(
                surfaceVertexCount,
                true,
                triangles);
            AppendDiscSides(
                layout,
                center,
                radius,
                topOffset,
                thickness,
                tilesPerMeter,
                vertices,
                normals,
                uvs,
                triangles);

            var mesh = new Mesh
            {
                name = $"{name} Conforming Mesh",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = vertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();

            var result = new GameObject(name);
            result.transform.SetParent(parent, false);
            MeshFilter filter = result.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = result.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            RuntimePrimitiveFactory.SetColor(renderer, color);
            MeshCollider collider = result.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            result.AddComponent<RuntimeGeneratedMeshOwner>()
                .Initialize(mesh);
            mesh.UploadMeshData(false);
            return result;
        }

        private static void AppendDiscSurfaceVertices(
            CityLayout layout,
            Vector3 center,
            float radius,
            float offset,
            bool underside,
            float tilesPerMeter,
            ICollection<Vector3> vertices,
            ICollection<Vector3> normals,
            ICollection<Vector2> uvs)
        {
            var centerXZ = new Vector2(center.x, center.z);
            vertices.Add(new Vector3(
                center.x,
                SamplePhysicalTop(layout, centerXZ) + offset,
                center.z));
            Vector3 centerNormal = SamplePhysicalNormal(layout, centerXZ);
            normals.Add(underside ? -centerNormal : centerNormal);
            uvs.Add(centerXZ * tilesPerMeter);
            for (int ring = 1; ring <= DiscRings; ring++)
            {
                float ringRadius = radius * ring / DiscRings;
                for (int segment = 0;
                     segment < DiscSegments;
                     segment++)
                {
                    float angle = segment * Mathf.PI * 2f / DiscSegments;
                    var xz = centerXZ + new Vector2(
                        Mathf.Cos(angle),
                        Mathf.Sin(angle)) * ringRadius;
                    vertices.Add(new Vector3(
                        xz.x,
                        SamplePhysicalTop(layout, xz) + offset,
                        xz.y));
                    Vector3 normal = SamplePhysicalNormal(layout, xz);
                    normals.Add(underside ? -normal : normal);
                    uvs.Add(xz * tilesPerMeter);
                }
            }
        }

        private static void AppendDiscSurfaceTriangles(
            int firstVertex,
            bool reverse,
            ICollection<int> triangles)
        {
            for (int segment = 0;
                 segment < DiscSegments;
                 segment++)
            {
                int current = firstVertex + 1 + segment;
                int next = firstVertex + 1 +
                           (segment + 1) % DiscSegments;
                AddTriangle(
                    triangles,
                    firstVertex,
                    next,
                    current,
                    reverse);
            }

            for (int ring = 1; ring < DiscRings; ring++)
            {
                int innerStart = firstVertex +
                                 1 +
                                 (ring - 1) * DiscSegments;
                int outerStart = innerStart + DiscSegments;
                for (int segment = 0;
                     segment < DiscSegments;
                     segment++)
                {
                    int next = (segment + 1) % DiscSegments;
                    AddTriangle(
                        triangles,
                        innerStart + segment,
                        innerStart + next,
                        outerStart + segment,
                        reverse);
                    AddTriangle(
                        triangles,
                        innerStart + next,
                        outerStart + next,
                        outerStart + segment,
                        reverse);
                }
            }
        }

        private static void AppendDiscSides(
            CityLayout layout,
            Vector3 center,
            float radius,
            float topOffset,
            float thickness,
            float tilesPerMeter,
            ICollection<Vector3> vertices,
            ICollection<Vector3> normals,
            ICollection<Vector2> uvs,
            ICollection<int> triangles)
        {
            var centerXZ = new Vector2(center.x, center.z);
            int firstSideVertex = vertices.Count;
            for (int segment = 0; segment < DiscSegments; segment++)
            {
                float angle = segment * Mathf.PI * 2f / DiscSegments;
                var outward = new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle));
                Vector2 xz = centerXZ + outward * radius;
                float terrainTop = SamplePhysicalTop(layout, xz);
                vertices.Add(new Vector3(
                    xz.x,
                    terrainTop + topOffset,
                    xz.y));
                vertices.Add(new Vector3(
                    xz.x,
                    terrainTop + topOffset - thickness,
                    xz.y));
                var sideNormal = new Vector3(
                    outward.x,
                    0f,
                    outward.y);
                normals.Add(sideNormal);
                normals.Add(sideNormal);
                uvs.Add(xz * tilesPerMeter);
                uvs.Add(xz * tilesPerMeter);
            }

            for (int segment = 0; segment < DiscSegments; segment++)
            {
                int next = (segment + 1) % DiscSegments;
                int topCurrent = firstSideVertex + segment * 2;
                int bottomCurrent = topCurrent + 1;
                int topNext = firstSideVertex + next * 2;
                int bottomNext = topNext + 1;
                triangles.Add(topCurrent);
                triangles.Add(topNext);
                triangles.Add(bottomCurrent);
                triangles.Add(topNext);
                triangles.Add(bottomNext);
                triangles.Add(bottomCurrent);
            }
        }

        private static void AddTriangle(
            ICollection<int> triangles,
            int first,
            int second,
            int third,
            bool reverse)
        {
            triangles.Add(first);
            triangles.Add(reverse ? third : second);
            triangles.Add(reverse ? second : third);
        }

        private static float SamplePhysicalTop(
            CityLayout layout,
            Vector2 worldXZ)
        {
            bool found = CityTerrainSurfacePlan.TrySampleGroundTop(
                layout,
                worldXZ,
                out float top,
                out _);
            if (layout.ElevationPlan.TrySampleSurface(
                    worldXZ,
                    CitySurfaceRole.RoadTop,
                    out float roadTop,
                    out _))
            {
                top = found ? Mathf.Max(top, roadTop) : roadTop;
                found = true;
            }

            if (!found)
            {
                throw new InvalidOperationException(
                    $"No physical terrain below {worldXZ}.");
            }

            return top;
        }

        private static Vector3 SamplePhysicalNormal(
            CityLayout layout,
            Vector2 worldXZ)
        {
            const float offset = 0.10f;
            float west = SamplePhysicalTop(
                layout,
                worldXZ + Vector2.left * offset);
            float east = SamplePhysicalTop(
                layout,
                worldXZ + Vector2.right * offset);
            float south = SamplePhysicalTop(
                layout,
                worldXZ + Vector2.down * offset);
            float north = SamplePhysicalTop(
                layout,
                worldXZ + Vector2.up * offset);
            var tangentX = new Vector3(offset * 2f, east - west, 0f);
            var tangentZ = new Vector3(0f, north - south, offset * 2f);
            return Vector3.Cross(tangentZ, tangentX).normalized;
        }

        private static void AppendPatch(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            Rect patch,
            float worldUvTileSize,
            ICollection<Vector3> vertices,
            ICollection<Vector3> normals,
            ICollection<Vector2> uvs,
            ICollection<int> triangles)
        {
            float cellMinimumX = layout.ElevationPlan.WorldOrigin.x +
                                 surface.Cell.x *
                                 layout.ElevationPlan.NodeSpacing.x;
            float cellMinimumZ = layout.ElevationPlan.WorldOrigin.z +
                                 surface.Cell.y *
                                 layout.ElevationPlan.NodeSpacing.y;
            float halfRoad = layout.ElevationPlan.RoadWidth * 0.5f;
            var xAnchors = new List<float>();
            var zAnchors = new List<float>();
            AppendDistrictPointAnchors(
                layout,
                surface,
                patch,
                xAnchors,
                zAnchors);
            List<float> xCoordinates = CreateAxisCoordinates(
                patch.xMin,
                patch.xMax,
                cellMinimumX + halfRoad,
                cellMinimumX +
                layout.ElevationPlan.NodeSpacing.x -
                halfRoad,
                xAnchors);
            List<float> zCoordinates = CreateAxisCoordinates(
                patch.yMin,
                patch.yMax,
                cellMinimumZ + halfRoad,
                cellMinimumZ +
                layout.ElevationPlan.NodeSpacing.y -
                halfRoad,
                zAnchors);

            int firstVertex = vertices.Count;
            float tilesPerMeter = 1f / worldUvTileSize;
            for (int zIndex = 0;
                 zIndex < zCoordinates.Count;
                 zIndex++)
            {
                for (int xIndex = 0;
                     xIndex < xCoordinates.Count;
                     xIndex++)
                {
                    var worldXZ = new Vector2(
                        xCoordinates[xIndex],
                        zCoordinates[zIndex]);
                    vertices.Add(new Vector3(
                        worldXZ.x,
                        CityTerrainSurfacePlan.SampleTop(
                            layout,
                            surface,
                            worldXZ),
                        worldXZ.y));
                    normals.Add(CityTerrainSurfacePlan.SampleNormal(
                        layout,
                        surface,
                        worldXZ));
                    uvs.Add(worldXZ * tilesPerMeter);
                }
            }

            int rowLength = xCoordinates.Count;
            for (int zIndex = 0;
                 zIndex < zCoordinates.Count - 1;
                 zIndex++)
            {
                for (int xIndex = 0;
                     xIndex < xCoordinates.Count - 1;
                     xIndex++)
                {
                    int southWest = firstVertex +
                                    zIndex * rowLength +
                                    xIndex;
                    int southEast = southWest + 1;
                    int northWest = southWest + rowLength;
                    int northEast = northWest + 1;
                    triangles.Add(southWest);
                    triangles.Add(northWest);
                    triangles.Add(southEast);
                    triangles.Add(southEast);
                    triangles.Add(northWest);
                    triangles.Add(northEast);
                }
            }
        }

        private static List<float> CreateAxisCoordinates(
            float minimum,
            float maximum,
            float firstSlopeBoundary,
            float secondSlopeBoundary,
            IReadOnlyList<float> additionalAnchors)
        {
            var anchors = new List<float> { minimum, maximum };
            AddInteriorCoordinate(
                anchors,
                firstSlopeBoundary,
                minimum,
                maximum);
            AddInteriorCoordinate(
                anchors,
                secondSlopeBoundary,
                minimum,
                maximum);
            for (int index = 0;
                 index < additionalAnchors.Count;
                 index++)
            {
                AddInteriorCoordinate(
                    anchors,
                    additionalAnchors[index],
                    minimum,
                    maximum);
            }

            anchors.Sort();

            var result = new List<float>();
            for (int anchorIndex = 0;
                 anchorIndex < anchors.Count - 1;
                 anchorIndex++)
            {
                float start = anchors[anchorIndex];
                float end = anchors[anchorIndex + 1];
                int segmentCount = Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        (end - start) / MaximumTriangleSpan));
                if (result.Count == 0)
                {
                    result.Add(start);
                }

                for (int segment = 1;
                     segment <= segmentCount;
                     segment++)
                {
                    result.Add(Mathf.Lerp(
                        start,
                        end,
                        segment / (float)segmentCount));
                }
            }

            return result;
        }

        private static void AppendDistrictPointAnchors(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            Rect patch,
            ICollection<float> xAnchors,
            ICollection<float> zAnchors)
        {
            if (surface.Kind != CitySurfaceKind.BuildableGround)
            {
                return;
            }

            float blend =
                CityTerrainSurfacePlan.DistrictPointBlendDistance;
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                Rect bounds = layout.DistrictPointsOfInterest[index]
                    .PublicBounds;
                Rect blendBounds = Rect.MinMaxRect(
                    bounds.xMin - blend,
                    bounds.yMin - blend,
                    bounds.xMax + blend,
                    bounds.yMax + blend);
                if (!patch.Overlaps(blendBounds))
                {
                    continue;
                }

                xAnchors.Add(bounds.xMin);
                xAnchors.Add(bounds.xMax);
                xAnchors.Add(blendBounds.xMin);
                xAnchors.Add(blendBounds.xMax);
                zAnchors.Add(bounds.yMin);
                zAnchors.Add(bounds.yMax);
                zAnchors.Add(blendBounds.yMin);
                zAnchors.Add(blendBounds.yMax);
            }
        }

        private static void AddInteriorCoordinate(
            ICollection<float> coordinates,
            float value,
            float minimum,
            float maximum)
        {
            if (value <= minimum + CoordinateTolerance ||
                value >= maximum - CoordinateTolerance)
            {
                return;
            }

            foreach (float coordinate in coordinates)
            {
                if (Mathf.Abs(coordinate - value) <= CoordinateTolerance)
                {
                    return;
                }
            }

            coordinates.Add(value);
        }

        private static void SubtractFromPatches(
            List<Rect> patches,
            Rect cut,
            Rect surfaceBounds)
        {
            if (!surfaceBounds.Overlaps(cut))
            {
                return;
            }

            var next = new List<Rect>();
            for (int patchIndex = 0;
                 patchIndex < patches.Count;
                 patchIndex++)
            {
                SubtractRectangle(patches[patchIndex], cut, next);
            }

            patches.Clear();
            patches.AddRange(next);
        }

        private static void SubtractRectangle(
            Rect source,
            Rect cut,
            ICollection<Rect> destination)
        {
            float xMin = Mathf.Max(source.xMin, cut.xMin);
            float xMax = Mathf.Min(source.xMax, cut.xMax);
            float zMin = Mathf.Max(source.yMin, cut.yMin);
            float zMax = Mathf.Min(source.yMax, cut.yMax);
            if (xMax <= xMin || zMax <= zMin)
            {
                destination.Add(source);
                return;
            }

            AddRectIfPositive(
                destination,
                source.xMin,
                source.yMin,
                xMin,
                source.yMax);
            AddRectIfPositive(
                destination,
                xMax,
                source.yMin,
                source.xMax,
                source.yMax);
            AddRectIfPositive(
                destination,
                xMin,
                source.yMin,
                xMax,
                zMin);
            AddRectIfPositive(
                destination,
                xMin,
                zMax,
                xMax,
                source.yMax);
        }

        private static void AddRectIfPositive(
            ICollection<Rect> destination,
            float xMin,
            float zMin,
            float xMax,
            float zMax)
        {
            if (xMax - xMin <= MinimumPatchSize ||
                zMax - zMin <= MinimumPatchSize)
            {
                return;
            }

            destination.Add(Rect.MinMaxRect(
                xMin,
                zMin,
                xMax,
                zMax));
        }
    }
}
