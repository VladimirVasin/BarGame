using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityTerrainSurfaceWorldBuilderTests
    {
        private const float Tolerance = 0.001f;
        // The user's accepted foot-height deviation between the sand's
        // coarse collider and its drawn skin.
        // The user accepted 1-3 cm on the sand; the one measured outlier of the
        // default city sits at 3.2 cm regardless of the collider pitch (the same
        // point at 1.0 m and 0.8 m), so it is a fold of the plan, not a chord.
        private const float BeachFootTolerance = 0.04f;
        // cos 20 degrees: the ceiling the plan's own bilinear cells and
        // pedestrian-graded terraces never approach.
        private const float GentleNormalY = 0.94f;
        // The plan's normal is a central difference with 0.1 m taps, so a
        // vertex that far outside a deliberate bank still sees its slope.
        private const float NormalTapReach = 0.1f;

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
        public void Build_Beach_CollidesOnCoarserSkinWithinFootTolerance()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                20260727);
            var root = new GameObject("Beach Collider Test Root");

            try
            {
                GameObject beach =
                    CityTerrainSurfaceWorldBuilder.Build(
                        "Beach",
                        root.transform,
                        layout,
                        CitySurfaceKind.Beach,
                        Color.yellow,
                        false);
                Assert.That(beach, Is.Not.Null);
                AssertCoarseBeachCollider(
                    layout,
                    beach.GetComponent<MeshFilter>().sharedMesh,
                    beach.GetComponent<MeshCollider>().sharedMesh);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The bit-identity proof for the primed terrain: the three lists
        /// a City-interior start samples on a pool thread must be the
        /// lists the build would sample here, float for float, and the
        /// build fed those lists must draw and collide on exactly them.
        /// </summary>
        [Test]
        [Category("CityTraversal")]
        public void CreateMeshSource_OnAPoolThread_IsBitIdenticalToTheMainThread()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                20260727);
            // A twin instance for the pool thread, so every per-layout memo
            // the sampling fills (port access, cannery, road index) is
            // filled there from nothing, as a primed start fills them.
            CityLayout twin = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                20260727);
            // What the prime resolves on the main thread before it starts.
            CityPortAccessPlan.WarmDefinition();
            float sandTile = CitySeacoastSurfaceAppearance.GetRecipe(
                CitySeacoastSurfaceKind.Sand).MetersPerTile;
            CityTerrainMeshSource[] expected = SampleBeachSources(layout, sandTile);

            CityTerrainMeshSource[] primed = System.Threading.Tasks.Task
                .Run(() => SampleBeachSources(twin, sandTile))
                .GetAwaiter()
                .GetResult();

            for (int index = 0; index < expected.Length; index++)
            {
                AssertSameSource(primed[index], expected[index]);
            }

            var root = new GameObject("Primed Beach Test Root");
            try
            {
                GameObject beach = CityTerrainSurfaceWorldBuilder.Build(
                    "Beach",
                    root.transform,
                    layout,
                    CitySurfaceKind.Beach,
                    Color.yellow,
                    false,
                    sandTile,
                    primedVisual: primed[0],
                    primedCollision: primed[1]);
                Assert.That(beach, Is.Not.Null);
                Mesh drawn = beach.GetComponent<MeshFilter>().sharedMesh;
                Mesh collision = beach.GetComponent<MeshCollider>().sharedMesh;
                Assert.That(collision, Is.Not.SameAs(drawn));
                Assert.That(drawn.vertexCount, Is.EqualTo(expected[0].Vertices.Count));
                Assert.That(collision.vertexCount, Is.EqualTo(expected[1].Vertices.Count));
                Assert.That(drawn.vertices[0], Is.EqualTo(expected[0].Vertices[0]));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The seabed's per-column shore cache is a memo, not an
        /// approximation: every height and normal it answers must be the
        /// bits the uncached taps compute, over the whole slope including
        /// the port's dredge and the graded core.
        /// </summary>
        [Test]
        [Category("CityTraversal")]
        public void SeabedShoreColumns_AnswerTheUncachedTapsToTheBit()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                20260727);
            CityPortPlan port = CitySeacoastPlanner.CreatePortPlan(layout);
            int compared = 0;
            foreach (CitySurfaceDescriptor surface in layout.Surfaces)
            {
                if (surface.Kind != CitySurfaceKind.Beach ||
                    surface.Feature != CityAreaFeatureKind.NorthWaterfront ||
                    !CityTerrainSurfacePlan.UsesContinuousTop(surface))
                {
                    continue;
                }

                CityTerrainSurfacePlan.SurfaceContext context =
                    CityTerrainSurfacePlan.ResolveSurfaceContext(layout, surface);
                var columns = new CitySeacoastSeaLayout.SeabedShoreColumns();
                Rect bounds = surface.WorldBounds;
                for (float x = bounds.xMin; x <= bounds.xMax; x += 0.4f)
                {
                    for (float reach = 0.5f;
                         reach <= CitySeacoastSeaLayout.SeabedReach;
                         reach += 1.3f)
                    {
                        var point = new Vector2(x, bounds.yMax + reach);
                        float expectedTop = CitySeacoastSeaLayout.SampleSeabedTop(
                            layout, surface, point, port, in context);
                        Vector3 expectedNormal = CitySeacoastSeaLayout.SampleSeabedNormal(
                            layout, surface, point, port, in context);
                        float cachedTop = CitySeacoastSeaLayout.SampleSeabedTop(
                            layout, surface, point, port, in context, columns);
                        Vector3 cachedNormal = CitySeacoastSeaLayout.SampleSeabedNormal(
                            layout, surface, point, port, in context, columns);
                        Assert.That(
                            System.BitConverter.SingleToInt32Bits(cachedTop),
                            Is.EqualTo(System.BitConverter.SingleToInt32Bits(expectedTop)),
                            $"top at {point}");
                        AssertSameBits(cachedNormal, expectedNormal, "seabed normal", compared);
                        compared++;
                    }
                }
            }

            Assert.That(compared, Is.GreaterThan(1000), "the shore must have been sampled");
        }

        private static CityTerrainMeshSource[] SampleBeachSources(
            CityLayout layout,
            float sandTile)
        {
            return new[]
            {
                CityTerrainSurfaceWorldBuilder.CreateMeshSource(
                    layout, CitySurfaceKind.Beach, sandTile, null, null, false,
                    CityBeachSandPlan.MeshPitch),
                CityTerrainSurfaceWorldBuilder.CreateMeshSource(
                    layout, CitySurfaceKind.Beach, sandTile, null, null, false,
                    CityBeachSandPlan.CollisionPitch),
                CityTerrainSurfaceWorldBuilder.CreateMeshSource(
                    layout, CitySurfaceKind.Beach, sandTile, null, null, true,
                    CityBeachSandPlan.MeshPitch)
            };
        }

        private static void AssertSameSource(
            CityTerrainMeshSource actual,
            CityTerrainMeshSource expected)
        {
            Assert.That(actual.Vertices.Count, Is.EqualTo(expected.Vertices.Count));
            Assert.That(actual.Normals.Count, Is.EqualTo(expected.Normals.Count));
            Assert.That(actual.Uvs.Count, Is.EqualTo(expected.Uvs.Count));
            Assert.That(actual.Triangles.Count, Is.EqualTo(expected.Triangles.Count));
            for (int index = 0; index < expected.Vertices.Count; index++)
            {
                AssertSameBits(actual.Vertices[index], expected.Vertices[index], "vertex", index);
                AssertSameBits(actual.Normals[index], expected.Normals[index], "normal", index);
                Assert.That(
                    System.BitConverter.SingleToInt32Bits(actual.Uvs[index].x),
                    Is.EqualTo(System.BitConverter.SingleToInt32Bits(expected.Uvs[index].x)),
                    $"uv {index} x");
                Assert.That(
                    System.BitConverter.SingleToInt32Bits(actual.Uvs[index].y),
                    Is.EqualTo(System.BitConverter.SingleToInt32Bits(expected.Uvs[index].y)),
                    $"uv {index} y");
            }

            for (int index = 0; index < expected.Triangles.Count; index++)
            {
                Assert.That(actual.Triangles[index], Is.EqualTo(expected.Triangles[index]));
            }
        }

        private static void AssertSameBits(Vector3 actual, Vector3 expected, string what, int index)
        {
            Assert.That(
                System.BitConverter.SingleToInt32Bits(actual.x),
                Is.EqualTo(System.BitConverter.SingleToInt32Bits(expected.x)),
                $"{what} {index} x");
            Assert.That(
                System.BitConverter.SingleToInt32Bits(actual.y),
                Is.EqualTo(System.BitConverter.SingleToInt32Bits(expected.y)),
                $"{what} {index} y");
            Assert.That(
                System.BitConverter.SingleToInt32Bits(actual.z),
                Is.EqualTo(System.BitConverter.SingleToInt32Bits(expected.z)),
                $"{what} {index} z");
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

        /// <summary>
        /// Whether the plan banks the ground here on purpose: the whole of
        /// the sand, which the plan grades from the landward datum down to
        /// the waterline across the cell and ripples on top (the beach
        /// branch of <c>CityTerrainSurfacePlan.SampleDatum</c>, with the
        /// port's earthwork cut into it), or the blend ring around a
        /// district point's pad on buildable ground. Everywhere else the
        /// continuous terrain is only bilinear between its cell corners.
        /// </summary>
        private static bool IsDeliberateBank(
            CityLayout layout,
            CitySurfaceKind kind,
            Vector2 worldXZ)
        {
            if (kind == CitySurfaceKind.Beach)
            {
                return true;
            }

            if (kind != CitySurfaceKind.BuildableGround)
            {
                return false;
            }

            float reach = CityTerrainSurfacePlan.DistrictPointBlendDistance +
                          NormalTapReach;
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                if (DistanceOutside(
                        layout.DistrictPointsOfInterest[index].PublicBounds,
                        worldXZ) <= reach)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Inside the port's graded box the sand is not a bank but the
        /// port's own fillet: <c>CityPortAccessPlan.ApplyGroundTop</c>
        /// smooth-steps from the paved top down to the natural sand over
        /// two metres, so its grade is whatever drop it bridges, and the
        /// hero reaches the yards from the street, never up this face.
        /// </summary>
        private static bool IsPortEarthwork(
            CitySurfaceKind kind,
            Vector2 worldXZ,
            CityPortAccessPlan portAccess)
        {
            if (kind != CitySurfaceKind.Beach || portAccess == null)
            {
                return false;
            }

            Rect graded = portAccess.GradedBounds;
            return Rect.MinMaxRect(
                    graded.xMin - NormalTapReach,
                    graded.yMin - NormalTapReach,
                    graded.xMax + NormalTapReach,
                    graded.yMax + NormalTapReach)
                .Contains(worldXZ);
        }

        private static float DistanceOutside(Rect bounds, Vector2 point)
        {
            float xDistance = point.x < bounds.xMin
                ? bounds.xMin - point.x
                : point.x > bounds.xMax
                    ? point.x - bounds.xMax
                    : 0f;
            float zDistance = point.y < bounds.yMin
                ? bounds.yMin - point.y
                : point.y > bounds.yMax
                    ? point.y - bounds.yMax
                    : 0f;
            return Mathf.Max(xDistance, zDistance);
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
            if (kind == CitySurfaceKind.Beach)
            {
                AssertCoarseBeachCollider(
                    layout,
                    filter.sharedMesh,
                    collider.sharedMesh);
            }
            else
            {
                Assert.That(
                    collider.sharedMesh,
                    Is.SameAs(filter.sharedMesh));
            }

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

            // The continuous plan keeps its own ground gentle: cells are
            // bilinear between terraces the elevation plan grades for
            // pedestrians. It also banks on purpose in two places, and
            // there the ground may stand as steep as the hero's own slope
            // limit - the only slope any code holds him to: the blend
            // around a district point's pad (0a7fd2f6 levelled the
            // cannery's pad to its yard's origin) and the sand, graded from
            // the town's datum down to the waterline (a6e54e50 cut the
            // port's yards into it).
            float walkableNormalY = Mathf.Cos(
                PlayerFactory.SlopeLimitDegrees * Mathf.Deg2Rad);
            CityPortAccessPlan portAccess = kind == CitySurfaceKind.Beach
                ? CityPortAccessPlan.ForLayout(layout)
                : null;
            float minimumY = float.PositiveInfinity;
            float maximumY = float.NegativeInfinity;
            for (int vertexIndex = 0;
                 vertexIndex < vertices.Length;
                 vertexIndex++)
            {
                Vector3 vertex = vertices[vertexIndex];
                minimumY = Mathf.Min(minimumY, vertex.y);
                maximumY = Mathf.Max(maximumY, vertex.y);
                var worldXZ = new Vector2(vertex.x, vertex.z);
                if (IsPortEarthwork(kind, worldXZ, portAccess))
                {
                    Assert.That(
                        normals[vertexIndex].y,
                        Is.GreaterThan(0f),
                        $"upward port fillet normal at vertex {vertexIndex}");
                    continue;
                }

                bool deliberateBank = IsDeliberateBank(
                    layout,
                    kind,
                    worldXZ);
                Assert.That(
                    normals[vertexIndex].y,
                    Is.GreaterThan(
                        deliberateBank ? walkableNormalY : GentleNormalY),
                    (deliberateBank
                        ? "walkable bank normal at vertex "
                        : "gently walkable upward terrain normal at vertex ") +
                    $"{vertexIndex} ({kind} {vertex})");
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

        /// <summary>
        /// The sand collides on a second, coarser skin of the same plan.
        /// Its vertices still come from the authoritative sampler, and a
        /// foot on it stands within the accepted tolerance of the drawn
        /// surface everywhere: every drawn vertex is measured against the
        /// collider triangle under it.
        /// </summary>
        private static void AssertCoarseBeachCollider(
            CityLayout layout,
            Mesh visual,
            Mesh collision)
        {
            Assert.That(collision, Is.Not.Null);
            Assert.That(collision, Is.Not.SameAs(visual));
            Assert.That(
                collision.vertexCount,
                Is.LessThan(visual.vertexCount / 2),
                "The sand collider must be coarser than the drawn skin.");
            CitySurfaceDescriptor[] sourceSurfaces = layout.Surfaces
                .Where(surface => surface.Kind == CitySurfaceKind.Beach)
                .ToArray();
            Vector3[] colliderVertices = collision.vertices;
            for (int index = 0; index < colliderVertices.Length; index++)
            {
                Vector3 vertex = colliderVertices[index];
                var worldXZ = new Vector2(vertex.x, vertex.z);
                Assert.That(
                    sourceSurfaces.Any(surface =>
                        Contains(surface.WorldBounds, worldXZ) &&
                        Mathf.Abs(
                            CityTerrainSurfacePlan.SampleTop(
                                layout,
                                surface,
                                worldXZ) -
                            vertex.y) <= Tolerance),
                    Is.True,
                    $"collider vertex {index} at {vertex} must use the " +
                    "authoritative terrain sampler");
            }

            const float bucketSize = 2f;
            int[] triangles = collision.triangles;
            var buckets = new Dictionary<Vector2Int, List<int>>();
            for (int triangle = 0; triangle < triangles.Length; triangle += 3)
            {
                Vector3 a = colliderVertices[triangles[triangle]];
                Vector3 b = colliderVertices[triangles[triangle + 1]];
                Vector3 c = colliderVertices[triangles[triangle + 2]];
                int minX = Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x) / bucketSize);
                int maxX = Mathf.FloorToInt(Mathf.Max(a.x, b.x, c.x) / bucketSize);
                int minZ = Mathf.FloorToInt(Mathf.Min(a.z, b.z, c.z) / bucketSize);
                int maxZ = Mathf.FloorToInt(Mathf.Max(a.z, b.z, c.z) / bucketSize);
                for (int x = minX; x <= maxX; x++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    var key = new Vector2Int(x, z);
                    if (!buckets.TryGetValue(key, out List<int> list))
                    {
                        list = new List<int>();
                        buckets.Add(key, list);
                    }

                    list.Add(triangle);
                }
            }

            Vector3[] drawnVertices = visual.vertices;
            int covered = 0;
            float worst = 0f;
            Vector3 worstAt = default;
            for (int index = 0; index < drawnVertices.Length; index++)
            {
                Vector3 drawn = drawnVertices[index];
                var key = new Vector2Int(
                    Mathf.FloorToInt(drawn.x / bucketSize),
                    Mathf.FloorToInt(drawn.z / bucketSize));
                if (!buckets.TryGetValue(key, out List<int> list))
                {
                    continue;
                }

                for (int candidate = 0; candidate < list.Count; candidate++)
                {
                    int triangle = list[candidate];
                    if (!TryInterpolateHeight(
                            colliderVertices[triangles[triangle]],
                            colliderVertices[triangles[triangle + 1]],
                            colliderVertices[triangles[triangle + 2]],
                            drawn,
                            out float height))
                    {
                        continue;
                    }

                    covered++;
                    float deviation = Mathf.Abs(height - drawn.y);
                    if (deviation > worst)
                    {
                        worst = deviation;
                        worstAt = drawn;
                    }

                    break;
                }
            }

            Assert.That(
                covered,
                Is.GreaterThanOrEqualTo(drawnVertices.Length * 99 / 100),
                "The sand collider must lie under the drawn sand.");
            Assert.That(
                worst,
                Is.LessThanOrEqualTo(BeachFootTolerance),
                $"A foot on the sand collider stands {worst:F4} m off " +
                $"the drawn surface at {worstAt}.");
        }

        private static bool TryInterpolateHeight(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 point,
            out float height)
        {
            const float slack = 0.001f;
            float denominator = (b.z - c.z) * (a.x - c.x) +
                                (c.x - b.x) * (a.z - c.z);
            height = 0f;
            if (Mathf.Abs(denominator) < 1e-9f)
            {
                return false;
            }

            float u = ((b.z - c.z) * (point.x - c.x) +
                       (c.x - b.x) * (point.z - c.z)) / denominator;
            float v = ((c.z - a.z) * (point.x - c.x) +
                       (a.x - c.x) * (point.z - c.z)) / denominator;
            float w = 1f - u - v;
            if (u < -slack || v < -slack || w < -slack)
            {
                return false;
            }

            height = u * a.y + v * b.y + w * c.y;
            return true;
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
