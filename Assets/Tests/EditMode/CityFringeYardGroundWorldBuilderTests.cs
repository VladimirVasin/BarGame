using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityFringeYardGroundWorldBuilderTests
    {
        private const float Tolerance = 0.001f;

        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId =
            Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");

        [Test]
        [Category("CityFringeYard")]
        public void DefaultPlan_SplitsOnlyMountainAreasIntoForefieldTerrain()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityFringeYardPlan fringePlan =
                CityFringeYardPlanner.Create(
                    layout,
                    CityMountainBoundaryPlanner.Create(layout));
            var expectedMountain = new HashSet<string>(
                fringePlan.Yards
                    .Where(yard =>
                        yard.Kind != CityFringeYardKind.EastUtilityEdge)
                    .Select(yard => yard.AreaId));
            var allOpenGround = new HashSet<string>(
                layout.Surfaces
                    .Where(surface =>
                        surface.Kind == CitySurfaceKind.OpenGround)
                    .Select(surface => surface.AreaId));
            HashSet<string> expectedGeneric =
                new HashSet<string>(allOpenGround);
            expectedGeneric.ExceptWith(expectedMountain);
            var host = new GameObject("Fringe Ground Test Host");

            try
            {
                CityFringeYardGroundWorldResult result =
                    CityFringeYardGroundWorldBuilder.Build(
                        host.transform,
                        layout,
                        fringePlan);

                Assert.That(expectedMountain, Has.Count.EqualTo(4));
                Assert.That(expectedGeneric, Does.Contain("yard-east"));
                Assert.That(result.GenericGround, Is.Not.Null);
                Assert.That(result.MountainGround, Is.Not.Null);
                Assert.That(
                    result.MountainGround.name,
                    Is.EqualTo(
                        CityFringeYardGroundWorldBuilder
                            .MountainGroundObjectName));

                HomeSurfaceRecipe recipe =
                    CityFringeYardSurfaceAppearance.GetRecipe(
                        CityFringeYardSurfaceKind.ForefieldGround);
                HashSet<string> genericAreas = AssertTerrainSkin(
                    layout,
                    result.GenericGround,
                    expectedGeneric,
                    recipe.MetersPerTile);
                HashSet<string> mountainAreas = AssertTerrainSkin(
                    layout,
                    result.MountainGround,
                    expectedMountain,
                    recipe.MetersPerTile);

                Assert.That(genericAreas, Is.EquivalentTo(expectedGeneric));
                Assert.That(
                    mountainAreas,
                    Is.EquivalentTo(expectedMountain));
                Assert.That(
                    genericAreas.Overlaps(mountainAreas),
                    Is.False,
                    "One source area must never own two terrain colliders.");
                var combined = new HashSet<string>(genericAreas);
                combined.UnionWith(mountainAreas);
                Assert.That(combined, Is.EquivalentTo(allOpenGround));

                Renderer genericRenderer =
                    result.GenericGround.GetComponent<Renderer>();
                var genericProperties = new MaterialPropertyBlock();
                genericRenderer.GetPropertyBlock(genericProperties);
                Assert.That(
                    genericProperties.GetTexture(BaseMapId),
                    Is.Not.Null,
                    "The east/custom batch wears the forefield sheet too: " +
                    "its ground is a hundred by two hundred metres the " +
                    "player walks, not a backdrop.");
                // The sheet is mean-controlled, so its measured
                // compensation multiplies the authored tint and the
                // ground keeps the brightness it had as flat colour.
                AssertColor(
                    genericProperties.GetColor(BaseColorId),
                    CityFringeYardSurfaceAppearance.CreateDisplayTint(
                        CityExteriorAppearance.YardGround,
                        CityFringeYardSurfaceKind.ForefieldGround));

                Renderer mountainRenderer =
                    result.MountainGround.GetComponent<Renderer>();
                Assert.That(
                    mountainRenderer.sharedMaterial,
                    Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial));
                var mountainProperties = new MaterialPropertyBlock();
                mountainRenderer.GetPropertyBlock(mountainProperties);
                Assert.That(
                    mountainProperties.GetTexture(BaseMapId),
                    Is.SameAs(
                        CityFringeYardSurfaceAppearance.GetTexture(
                            CityFringeYardSurfaceKind.ForefieldGround)));
                Assert.That(
                    mountainProperties.GetVector(BaseMapTransformId),
                    Is.EqualTo(Vector4.zero),
                    "The conforming mesh already bakes world-metre UVs.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        [Category("CityFringeYard")]
        public void EmptyPlan_KeepsAllOpenGroundGeneric()
        {
            CityLayout defaultLayout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            AssertAllGeneric(defaultLayout, CityFringeYardPlan.Empty);
        }

        private static void AssertAllGeneric(
            CityLayout layout,
            CityFringeYardPlan fringePlan)
        {
            var host = new GameObject("Generic Yard Ground Test Host");
            try
            {
                CityFringeYardGroundWorldResult result =
                    CityFringeYardGroundWorldBuilder.Build(
                        host.transform,
                        layout,
                        fringePlan);
                var expectedAreas = new HashSet<string>(
                    layout.Surfaces
                        .Where(surface =>
                            surface.Kind == CitySurfaceKind.OpenGround)
                        .Select(surface => surface.AreaId));

                Assert.That(result.GenericGround, Is.Not.Null);
                Assert.That(result.MountainGround, Is.Null);
                Assert.That(
                    host.transform.Find(
                        CityFringeYardGroundWorldBuilder
                            .MountainGroundObjectName),
                    Is.Null);
                Assert.That(
                    AssertTerrainSkin(
                        layout,
                        result.GenericGround,
                        expectedAreas,
                        CityFringeYardSurfaceAppearance.GetRecipe(
                            CityFringeYardSurfaceKind.ForefieldGround)
                            .MetersPerTile),
                    Is.EquivalentTo(expectedAreas));

                var properties = new MaterialPropertyBlock();
                result.GenericGround.GetComponent<Renderer>()
                    .GetPropertyBlock(properties);
                Assert.That(properties.GetTexture(BaseMapId), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static HashSet<string> AssertTerrainSkin(
            CityLayout layout,
            GameObject terrain,
            ISet<string> expectedAreaIds,
            float? expectedUvPitch)
        {
            MeshFilter filter = terrain.GetComponent<MeshFilter>();
            MeshCollider[] colliders = terrain.GetComponents<MeshCollider>();
            Assert.That(filter, Is.Not.Null);
            Assert.That(colliders, Has.Length.EqualTo(1));
            Assert.That(
                colliders[0].sharedMesh,
                Is.SameAs(filter.sharedMesh));
            Assert.That(terrain.GetComponent<BoxCollider>(), Is.Null);
            Assert.That(
                terrain.GetComponentsInChildren<Collider>(true),
                Has.Length.EqualTo(1),
                "Collision belongs only to the rendered terrain skin.");

            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            CitySurfaceDescriptor[] expectedSurfaces = layout.Surfaces
                .Where(surface =>
                    surface.Kind == CitySurfaceKind.OpenGround &&
                    expectedAreaIds.Contains(surface.AreaId))
                .ToArray();
            float minimumY = float.PositiveInfinity;
            float maximumY = float.NegativeInfinity;
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 vertex = vertices[index];
                minimumY = Mathf.Min(minimumY, vertex.y);
                maximumY = Mathf.Max(maximumY, vertex.y);
                Assert.That(
                    expectedSurfaces.Any(surface => MatchesSourceVertex(layout, surface, vertex)),
                    Is.True,
                    $"{terrain.name} vertex {index} at {vertex:F4} must conform to its " +
                    "source terrain area.");

                if (expectedUvPitch.HasValue)
                {
                    Assert.That(
                        uvs[index].x,
                        Is.EqualTo(vertex.x / expectedUvPitch.Value)
                            .Within(Tolerance));
                    Assert.That(
                        uvs[index].y,
                        Is.EqualTo(vertex.z / expectedUvPitch.Value)
                            .Within(Tolerance));
                }
            }

            if (expectedUvPitch.HasValue)
            {
                Assert.That(
                    maximumY - minimumY,
                    Is.GreaterThan(0.5f),
                    "The mountain forefield must remain sampled terrain, " +
                    "not a flat overlay.");
            }

            return CollectTriangleAreaIds(layout, mesh);
        }

        private static bool MatchesSourceVertex(CityLayout layout, CitySurfaceDescriptor surface, Vector3 vertex)
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

        private static HashSet<string> CollectTriangleAreaIds(
            CityLayout layout,
            Mesh mesh)
        {
            CitySurfaceDescriptor[] sources = layout.Surfaces
                .Where(surface =>
                    surface.Kind == CitySurfaceKind.OpenGround)
                .ToArray();
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            var result = new HashSet<string>();
            for (int index = 0; index < triangles.Length; index += 3)
            {
                Vector3 first = vertices[triangles[index]];
                Vector3 second = vertices[triangles[index + 1]];
                Vector3 third = vertices[triangles[index + 2]];
                if (Mathf.Abs(Vector3.Cross(second - first, third - first).y) < 0.000001f)
                    continue;
                Vector3 center = (first + second + third) / 3f;
                var centerXZ = new Vector2(center.x, center.z);
                string[] matches = sources
                    .Where(surface => ContainsGround(layout, surface, centerXZ))
                    .Select(surface => surface.AreaId)
                    .Distinct()
                    .ToArray();
                Assert.That(
                    matches,
                    Has.Length.EqualTo(1),
                    $"Triangle center {centerXZ} needs one source area.");
                result.Add(matches[0]);
            }

            return result;
        }

        private static bool ContainsGround(CityLayout layout, CitySurfaceDescriptor surface, Vector2 point)
        {
            if (!layout.RoadGeometry.HasReplannedGround(surface.Cell))
                return Contains(surface.WorldBounds, point);
            return Contains(layout.GetCellWorldBounds(surface.Cell), point) &&
                layout.RoadGeometry.GetGroundPolygons(surface.Cell)
                    .Any(polygon => CityRoadPolygon.Contains(polygon, point));
        }

        private static bool Contains(Rect bounds, Vector2 point)
        {
            return point.x >= bounds.xMin - Tolerance &&
                   point.x <= bounds.xMax + Tolerance &&
                   point.y >= bounds.yMin - Tolerance &&
                   point.y <= bounds.yMax + Tolerance;
        }

        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(Tolerance));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(Tolerance));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(Tolerance));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(Tolerance));
        }
    }
}
