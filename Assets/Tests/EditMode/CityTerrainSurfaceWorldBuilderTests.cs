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
        // The plan's normal is a central difference with 0.1 m taps, so a
        // vertex that far outside a deliberate bank still sees its slope.

        [TestCase(CitySurfaceKind.BuildableGround)]
        [TestCase(CitySurfaceKind.ParkGround)]
        [TestCase(CitySurfaceKind.OpenGround)]
        public void SharedStreetBoundary_TerrainMeshAndSamplerMeetPavingWithoutGap(CitySurfaceKind kind)
        {
            CityLayout layout = CityLayoutGenerator.Generate(CityBlueprintCatalog.Default,
                CityGenerationSettings.Default, 20260727);
            if (kind == CitySurfaceKind.BuildableGround) AssertOverlappingBoundaryBandsAreContinuous(layout);
            int checkedSegments = 0;
            foreach (CitySurfaceDescriptor surface in layout.Surfaces)
            {
                if (surface.Kind != kind || surface.AreaId == "yard-east" || surface.AreaId == "yard-north-east") continue;
                CityTerrainStreetBoundaryPlan.SurfaceBoundaryContext context =
                    CityTerrainStreetBoundaryPlan.ForSurface(layout, surface);
                if (context == null) continue;
                foreach (CityTerrainStreetBoundaryPlan.Segment segment in context.Segments)
                {
                    Vector3 middle = (segment.Start + segment.End) * .5f;
                    Vector2 point = new Vector2(middle.x, middle.z);
                    if (!surface.WorldBounds.Contains(point) || Vector3.Distance(segment.Start, segment.End) < .1f) continue;
                    if (layout.DistrictPointsOfInterest.Any(poi => poi.PublicBounds.Contains(point))) continue;
                    if (!CityStreetSupportPlan.ForLayout(layout).TrySampleTop(point, out float streetTop, out _)) continue;
                    if (Mathf.Abs(streetTop - middle.y) > .001f) continue;
                    // Test the whole selected sub-edge, including positions
                    // between original grid vertices. Endpoint-only tests miss
                    // the former single-plane pavement/graded-road mismatch.
                    Rect retained = Rect.MinMaxRect(Mathf.Min(segment.Start.x, segment.End.x) - .1f,
                        Mathf.Min(segment.Start.z, segment.End.z) - .1f,
                        Mathf.Max(segment.Start.x, segment.End.x) + .1f,
                        Mathf.Max(segment.Start.z, segment.End.z) + .1f);
                    CityTerrainMeshSource source = CityTerrainSurfaceWorldBuilder.CreateMeshSource(layout, kind,
                        null, null, null, false, CityBeachSandPlan.MeshPitch, retained);
                    for (int sample = 1; sample <= 5; sample++)
                    {
                        Vector3 edge = Vector3.Lerp(segment.Start, segment.End, sample / 6f);
                        Vector2 xz = new Vector2(edge.x, edge.z);
                        if (!surface.WorldBounds.Contains(xz) ||
                            layout.DistrictPointsOfInterest.Any(poi => poi.PublicBounds.Contains(xz))) continue;
                        float top = CityTerrainSurfacePlan.SampleTop(layout, surface, xz);
                        Assert.That(top, Is.EqualTo(edge.y).Within(.001f), $"{kind} sampler seam at {xz}");
                        bool found = false;
                        for (int triangle = 0; triangle < source.Triangles.Count; triangle += 3)
                        {
                            var vertices = new[] { source.Vertices[source.Triangles[triangle]],
                                source.Vertices[source.Triangles[triangle + 1]], source.Vertices[source.Triangles[triangle + 2]] };
                            if (!CityStreetSupportPatch.TrySampleTop(vertices, xz, out float meshTop)) continue;
                            Assert.That(meshTop, Is.EqualTo(edge.y).Within(.001f),
                                $"{kind} triangulated seam at {xz}; segment {segment.Start:F5}->{segment.End:F5}; " +
                                $"triangle {vertices[0]:F5}, {vertices[1]:F5}, {vertices[2]:F5}; " +
                                $"scalar at triangle vertices " + string.Join(", ", vertices.Select(vertex =>
                                    CityTerrainSurfacePlan.SampleTop(layout, surface, new Vector2(vertex.x, vertex.z)).ToString("F6"))));
                            found = true;
                        }
                        Assert.That(found, Is.True, $"{kind} terrain must reach its paving seam at {xz}");
                    }
                    checkedSegments++;
                    if (checkedSegments >= 3) break;
                }
                if (checkedSegments >= 3) break;
            }
            Assert.That(checkedSegments, Is.GreaterThan(0), $"{kind} needs a real retained paving/earth seam");
        }

        private static void AssertOverlappingBoundaryBandsAreContinuous(CityLayout layout)
        {
            // The service fringe can leave only four metres of earth between
            // street edges, so their four-metre transition bands overlap.
            // Put this pure field outside the real city to retain production
            // support lookup without introducing a test-only null contract.
            const float origin = 10000f;
            var left = new CityTerrainStreetBoundaryPlan.Segment(
                new Vector3(origin, 0f, 0f), new Vector3(origin, 0f, 10f), Vector2.right);
            var right = new CityTerrainStreetBoundaryPlan.Segment(
                new Vector3(origin + 4f, 1f, 0f), new Vector3(origin + 4f, 1f, 10f), Vector2.left);
            CityStreetSupportPlan support = CityStreetSupportPlan.ForLayout(layout);
            var both = new CityTerrainStreetBoundaryPlan.SurfaceBoundaryContext(new[] { left, right }, support);
            float before = both.Apply(new Vector2(origin + 1.99f, 5f), .5f);
            float after = both.Apply(new Vector2(origin + 2.01f, 5f), .5f);
            Assert.That(Mathf.Abs(after - before), Is.LessThan(.03f),
                "Opposing street bands must blend across their bisector rather than change the nearest target abruptly.");
            Assert.That(both.Apply(new Vector2(origin, 5f), .5f), Is.EqualTo(0f).Within(.0001f));
            Assert.That(both.Apply(new Vector2(origin + 4f, 5f), .5f), Is.EqualTo(1f).Within(.0001f));
            Assert.That(both.Apply(new Vector2(origin - 5f, 5f), .5f), Is.EqualTo(.5f).Within(.0001f));
            var single = new CityTerrainStreetBoundaryPlan.SurfaceBoundaryContext(new[] { left }, support);
            Assert.That(single.Apply(new Vector2(origin + 2f, 5f), .5f), Is.EqualTo(.25f).Within(.0001f),
                "One boundary retains its original bounded smoothstep transition.");
        }

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

        internal static bool MatchesSourceVertex(CityLayout layout, CitySurfaceDescriptor surface, Vector3 vertex)
        {
            var point = new Vector2(vertex.x, vertex.z);
            if (!ContainsGround(layout, surface, point))
                return false;
            if (Mathf.Abs(CityTerrainSurfacePlan.SampleTop(layout, surface, point) - vertex.y) <= Tolerance)
                return true;

            var boundary = CityTerrainStreetBoundaryPlan.ForSurface(layout, surface);
            if (boundary == null)
                return false;
            // At a closing step the mesh can carry both the ground and the
            // adjoining street edge's height at the same projected corner.
            foreach (CityTerrainStreetBoundaryPlan.Segment segment in boundary.Segments)
            {
                var start = new Vector2(segment.Start.x, segment.Start.z);
                var end = new Vector2(segment.End.x, segment.End.z);
                Vector2 delta = end - start;
                float amount = Mathf.Clamp01(Vector2.Dot(point - start, delta) / delta.sqrMagnitude);
                if ((start + delta * amount - point).sqrMagnitude <= Tolerance * Tolerance &&
                    Mathf.Abs(Mathf.Lerp(segment.Start.y, segment.End.y, amount) - vertex.y) <= Tolerance)
                    return true;
            }
            return false;
        }

        internal static bool ContainsGround(CityLayout layout, CitySurfaceDescriptor surface, Vector2 point)
        {
            if (!layout.RoadGeometry.HasReplannedGround(surface.Cell))
                return Contains(surface.WorldBounds, point);
            return Contains(layout.GetCellWorldBounds(surface.Cell), point) &&
                layout.RoadGeometry.GetGroundPolygons(surface.Cell)
                    .Any(polygon => CityRoadPolygon.Contains(polygon, point));
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

            float minimumY = float.PositiveInfinity;
            float maximumY = float.NegativeInfinity;
            for (int vertexIndex = 0;
                 vertexIndex < vertices.Length;
                 vertexIndex++)
            {
                Vector3 vertex = vertices[vertexIndex];
                minimumY = Mathf.Min(minimumY, vertex.y);
                maximumY = Mathf.Max(maximumY, vertex.y);
                Assert.That(normals[vertexIndex].y, Is.GreaterThanOrEqualTo(-Tolerance),
                    $"Terrain or a closing edge faces down at vertex {vertexIndex} ({kind} {vertex}).");
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

                bool matchesPlan = sourceSurfaces.Any(surface => MatchesSourceVertex(layout, surface, vertex));
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
        /// A foot on it stands within the accepted tolerance of the drawn
        /// surface: compare the drawn vertices with the collider triangles.
        /// </summary>
        private static void AssertCoarseBeachCollider(
            Mesh visual,
            Mesh collision)
        {
            Assert.That(collision, Is.Not.Null);
            Assert.That(collision, Is.Not.SameAs(visual));
            Assert.That(
                collision.vertexCount,
                Is.LessThan(visual.vertexCount / 2),
                "The sand collider must be coarser than the drawn skin.");
            Vector3[] colliderVertices = collision.vertices;

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
            var referenced = new bool[drawnVertices.Length];
            foreach (int index in visual.triangles)
                referenced[index] = true;
            int drawnCount = 0;
            int covered = 0;
            float worst = 0f;
            Vector3 worstAt = default;
            for (int index = 0; index < drawnVertices.Length; index++)
            {
                if (!referenced[index]) continue;
                drawnCount++;
                Vector3 drawn = drawnVertices[index];
                var key = new Vector2Int(
                    Mathf.FloorToInt(drawn.x / bucketSize),
                    Mathf.FloorToInt(drawn.z / bucketSize));
                if (!buckets.TryGetValue(key, out List<int> list))
                {
                    continue;
                }

                float closest = float.PositiveInfinity;
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

                    // Several faces can own the same XZ along a curb or a step.
                    closest = Mathf.Min(closest, Mathf.Abs(height - drawn.y));
                }

                if (float.IsPositiveInfinity(closest))
                {
                    continue;
                }

                covered++;
                if (closest > worst)
                {
                    worst = closest;
                    worstAt = drawn;
                }
            }

            Assert.That(
                covered,
                Is.GreaterThanOrEqualTo(drawnCount * 99 / 100),
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
