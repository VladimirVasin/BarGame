using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityTerrainSurfaceWorldBuilderTests
    {
        private const float Tolerance = 0.001f;

        [Test]
        [Category("CityTraversal")]
        public void Build_DefaultContinuousTerrain_MatchesPlanAndOwnsCollider()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                20260727);
            var root = new GameObject("Terrain Test Root");

            try
            {
                GameObject activeLand =
                    CityTerrainSurfaceWorldBuilder.Build(
                        "Active Land",
                        root.transform,
                        layout,
                        CitySurfaceKind.BuildableGround,
                        Color.white,
                        true);
                GameObject parkLawn =
                    CityTerrainSurfaceWorldBuilder.Build(
                        "Park Lawn",
                        root.transform,
                        layout,
                        CitySurfaceKind.ParkGround,
                        Color.green,
                        false);
                GameObject yardGround =
                    CityTerrainSurfaceWorldBuilder.Build(
                        "Yard Ground",
                        root.transform,
                        layout,
                        CitySurfaceKind.OpenGround,
                        Color.gray,
                        false);
                GameObject beach =
                    CityTerrainSurfaceWorldBuilder.Build(
                        "Beach",
                        root.transform,
                        layout,
                        CitySurfaceKind.Beach,
                        Color.yellow,
                        false);

                AssertContinuousMesh(
                    layout,
                    activeLand,
                    CitySurfaceKind.BuildableGround);
                AssertContinuousMesh(
                    layout,
                    parkLawn,
                    CitySurfaceKind.ParkGround);
                AssertContinuousMesh(
                    layout,
                    yardGround,
                    CitySurfaceKind.OpenGround);
                AssertContinuousMesh(
                    layout,
                    beach,
                    CitySurfaceKind.Beach);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        [Category("CityTraversal")]
        public void DefaultPlazasAndPublicPads_ClearContinuousTerrain()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                20260727);
            CityRoadGroundBoundaryPlan boundaries =
                CityRoadGroundBoundaryPlanner.Create(layout);
            RoadWalkableArea walkable = RoadWalkableArea.FromLayout(layout);
            var root = new GameObject("Terrain Feature Test Root");

            try
            {
                for (int regionIndex = 0;
                     regionIndex < layout.Park.Regions.Count;
                     regionIndex++)
                {
                    CityParkRegionPlan region =
                        layout.Park.Regions[regionIndex];
                    GameObject plaza =
                        CityTerrainSurfaceWorldBuilder.BuildConformingDisc(
                            $"Park Plaza {regionIndex + 1}",
                            root.transform,
                            layout,
                            region.PlazaPosition,
                            4.25f,
                            0.10f,
                            0.16f,
                            Color.gray);
                    AssertConformingPlaza(layout, plaza);
                }

                for (int descriptorIndex = 0;
                     descriptorIndex <
                     layout.DistrictPointsOfInterest.Count;
                     descriptorIndex++)
                {
                    CityDistrictPointOfInterestDescriptor descriptor =
                        layout.DistrictPointsOfInterest[descriptorIndex];
                    CitySurfaceDescriptor source = layout.Surfaces.Single(
                        surface => surface.Cell == descriptor.Cell);
                    float expectedTerrainTop = descriptor.Center.y +
                        CityElevationPlan.GroundTopOffset;
                    float publicGroundTop = descriptor.Center.y + 0.06f;
                    for (int z = 0; z <= 16; z++)
                    {
                        for (int x = 0; x <= 16; x++)
                        {
                            var point = new Vector2(
                                Mathf.Lerp(
                                    descriptor.PublicBounds.xMin,
                                    descriptor.PublicBounds.xMax,
                                    x / 16f),
                                Mathf.Lerp(
                                    descriptor.PublicBounds.yMin,
                                    descriptor.PublicBounds.yMax,
                                    z / 16f));
                            float terrainTop =
                                CityTerrainSurfacePlan.SampleTop(
                                    layout,
                                    source,
                                    point);
                            Assert.That(
                                terrainTop,
                                Is.EqualTo(expectedTerrainTop)
                                    .Within(Tolerance),
                                $"{descriptor.Kind} pad at {point}");
                            Assert.That(
                                publicGroundTop - terrainTop,
                                Is.EqualTo(0.14f).Within(Tolerance),
                                $"{descriptor.Kind} slab clearance");
                        }
                    }

                    foreach (
                        CityDistrictPointOfInterestAccessDescriptor access in
                        descriptor.Accesses)
                    {
                        Assert.That(
                            boundaries.SafeConnections.Any(span =>
                                span.Surface.Cell == descriptor.Cell &&
                                span.Edge == access.FrontageEdge),
                            Is.True,
                            $"{descriptor.Kind} authored road access");
                        Assert.That(
                            walkable.Contains(access.Center, 0.28f),
                            Is.True,
                            $"{descriptor.Kind} walkable access");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void AssertConformingPlaza(
            CityLayout layout,
            GameObject plaza)
        {
            MeshFilter filter = plaza.GetComponent<MeshFilter>();
            MeshCollider collider = plaza.GetComponent<MeshCollider>();
            Assert.That(filter, Is.Not.Null);
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.sharedMesh, Is.SameAs(filter.sharedMesh));
            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int topVertexCount = 0;
            for (int index = 0; index < vertices.Length; index++)
            {
                if (normals[index].y <= 0.5f)
                {
                    continue;
                }

                topVertexCount++;
                Vector3 vertex = vertices[index];
                var worldXZ = new Vector2(vertex.x, vertex.z);
                float terrainTop = SamplePhysicalTop(layout, worldXZ);
                Assert.That(
                    vertex.y - terrainTop,
                    Is.EqualTo(0.10f).Within(Tolerance),
                    $"{plaza.name} top at {worldXZ}");
            }

            Assert.That(topVertexCount, Is.GreaterThan(16));
        }

        private static float SamplePhysicalTop(
            CityLayout layout,
            Vector2 worldXZ)
        {
            bool found = CityTerrainSurfacePlan.TrySampleGroundTop(
                layout,
                worldXZ,
                out float result,
                out _);
            if (layout.ElevationPlan.TrySampleSurface(
                    worldXZ,
                    CitySurfaceRole.RoadTop,
                    out float roadTop,
                    out _))
            {
                result = found ? Mathf.Max(result, roadTop) : roadTop;
                found = true;
            }

            Assert.That(found, Is.True, worldXZ.ToString());
            return result;
        }

        private static void AssertContinuousMesh(
            CityLayout layout,
            GameObject surfaceObject,
            CitySurfaceKind kind)
        {
            Assert.That(surfaceObject, Is.Not.Null);
            MeshFilter filter = surfaceObject.GetComponent<MeshFilter>();
            MeshCollider collider =
                surfaceObject.GetComponent<MeshCollider>();
            Assert.That(filter, Is.Not.Null);
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.sharedMesh, Is.SameAs(filter.sharedMesh));
            Assert.That(
                surfaceObject.GetComponent<BoxCollider>(),
                Is.Null,
                "Continuous terrain must not restore a deep terrace box.");

            Mesh mesh = filter.sharedMesh;
            Assert.That(mesh.vertexCount, Is.GreaterThan(4));
            Assert.That(mesh.triangles, Is.Not.Empty);
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uvs = mesh.uv;
            CitySurfaceDescriptor[] sourceSurfaces = layout.Surfaces
                .Where(surface => surface.Kind == kind)
                .ToArray();
            Assert.That(sourceSurfaces, Is.Not.Empty);

            float minimumY = float.PositiveInfinity;
            float maximumY = float.NegativeInfinity;
            for (int vertexIndex = 0;
                 vertexIndex < vertices.Length;
                 vertexIndex++)
            {
                Vector3 vertex = vertices[vertexIndex];
                minimumY = Mathf.Min(minimumY, vertex.y);
                maximumY = Mathf.Max(maximumY, vertex.y);
                Assert.That(
                    normals[vertexIndex].y,
                    Is.GreaterThan(0.94f),
                    $"gently walkable upward terrain normal at vertex " +
                    vertexIndex);
                Assert.That(
                    uvs[vertexIndex].x,
                    Is.EqualTo(
                        vertex.x /
                        CityExteriorAppearance.GroundTextureTileSize)
                        .Within(Tolerance));
                Assert.That(
                    uvs[vertexIndex].y,
                    Is.EqualTo(
                        vertex.z /
                        CityExteriorAppearance.GroundTextureTileSize)
                        .Within(Tolerance));

                var worldXZ = new Vector2(vertex.x, vertex.z);
                bool matchesPlan = sourceSurfaces.Any(surface =>
                    Contains(surface.WorldBounds, worldXZ) &&
                    Mathf.Abs(
                        CityTerrainSurfacePlan.SampleTop(
                            layout,
                            surface,
                            worldXZ) -
                        vertex.y) <= Tolerance);
                Assert.That(
                    matchesPlan,
                    Is.True,
                    $"vertex {vertexIndex} at {vertex} must use the " +
                    "authoritative terrain sampler");
            }

            Assert.That(
                maximumY - minimumY,
                Is.GreaterThan(0.5f),
                "The production fixture must prove the mesh is not flat.");
        }

        private static bool Contains(Rect bounds, Vector2 point)
        {
            return point.x >= bounds.xMin - Tolerance &&
                   point.x <= bounds.xMax + Tolerance &&
                   point.y >= bounds.yMin - Tolerance &&
                   point.y <= bounds.yMax + Tolerance;
        }
    }
}
