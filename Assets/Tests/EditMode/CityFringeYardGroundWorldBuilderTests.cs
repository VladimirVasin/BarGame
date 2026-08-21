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

                HashSet<string> genericAreas = AssertTerrainSkin(
                    layout,
                    result.GenericGround,
                    expectedGeneric,
                    null);
                HomeSurfaceRecipe recipe =
                    CityFringeYardSurfaceAppearance.GetRecipe(
                        CityFringeYardSurfaceKind.ForefieldGround);
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
                    Is.Null,
                    "The east/custom batch keeps generic YardGround.");
                AssertColor(
                    genericProperties.GetColor(BaseColorId),
                    CityExteriorAppearance.YardGround);

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
                        null),
                    Is.EquivalentTo(expectedAreas));

                var properties = new MaterialPropertyBlock();
                result.GenericGround.GetComponent<Renderer>()
                    .GetPropertyBlock(properties);
                Assert.That(properties.GetTexture(BaseMapId), Is.Null);
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
                var worldXZ = new Vector2(vertex.x, vertex.z);
                Assert.That(
                    expectedSurfaces.Any(surface =>
                        Contains(surface.WorldBounds, worldXZ) &&
                        Mathf.Abs(
                            CityTerrainSurfacePlan.SampleTop(
                                layout,
                                surface,
                                worldXZ) -
                            vertex.y) <= Tolerance),
                    Is.True,
                    $"{terrain.name} vertex {index} must conform to its " +
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
                Vector3 center =
                    (vertices[triangles[index]] +
                     vertices[triangles[index + 1]] +
                     vertices[triangles[index + 2]]) / 3f;
                var centerXZ = new Vector2(center.x, center.z);
                CitySurfaceDescriptor[] matches = sources
                    .Where(surface => surface.WorldBounds.Contains(centerXZ))
                    .ToArray();
                Assert.That(
                    matches,
                    Has.Length.EqualTo(1),
                    $"Triangle center {centerXZ} needs one source area.");
                result.Add(matches[0].AreaId);
            }

            return result;
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
