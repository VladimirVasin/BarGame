using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityGroundSurfaceSystemTests
    {
        private GameObject root;
        private CityGroundSurfaceSystem system;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Unified Ground Contract");
            system = CityGroundSurfaceSystem.Begin(root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }

        [Test]
        public void FinalizeSurface_MarkingFollowsEachGradePlaneAndRetainsSupportingFootstep()
        {
            MeshRenderer road = CreateSurface("Graded Road", new[]
            {
                new Vector3(0, 0, 0), new Vector3(0, 0, 2), new Vector3(2, .2f, 0), new Vector3(2, .2f, 2),
                new Vector3(2, .2f, 0), new Vector3(2, .2f, 2), new Vector3(4, .1f, 0), new Vector3(4, .1f, 2)
            }, new[] { 0, 1, 2, 2, 1, 3, 4, 5, 6, 6, 5, 7 },
                CityExteriorAppearance.ApplyRoadSurface, FootstepGroundKind.Concrete);
            Mesh roadMesh = road.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] gradedVertices = roadMesh.vertices;
            Vector3[] initialVertices = (Vector3[])gradedVertices.Clone();
            for (int i = 0; i < initialVertices.Length; i++) initialVertices[i].y = 0f;
            roadMesh.vertices = initialVertices;
            roadMesh.RecalculateNormals();
            MeshRenderer marking = CreateSurface("Flat Marking", Quad(.5f, 3.5f, .6f, 1.4f, .3f),
                QuadIndices, CityExteriorAppearance.ApplyRoadMarkingSurface);
            CityGroundSurfaceSystem.Register(road, 20);
            CityGroundSurfaceSystem.Register(marking, 100, paint: true);
            // Builders can alter an already registered mesh in place. Keep the
            // same mesh reference and require the final snapshot's grade/normal.
            roadMesh.vertices = gradedVertices;
            roadMesh.RecalculateNormals();
            system.FinalizeSurface();

            float area = 0f;
            int paintTriangles = 0;
            foreach (MeshRenderer renderer in GroundRoot.GetComponentsInChildren<MeshRenderer>())
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                Vector3[] vertices = mesh.vertices;
                for (int slot = 0; slot < mesh.subMeshCount; slot++)
                {
                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties, slot);
                    if (properties.GetFloat("_GroundKind") != (float)GroundSurfaceKind.Marking) continue;
                    int[] indices = mesh.GetTriangles(slot);
                    for (int index = 0; index < indices.Length; index += 3)
                    {
                        Vector3 a = renderer.transform.TransformPoint(vertices[indices[index]]);
                        Vector3 b = renderer.transform.TransformPoint(vertices[indices[index + 1]]);
                        Vector3 c = renderer.transform.TransformPoint(vertices[indices[index + 2]]);
                        float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
                        float maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                        Assert.That(minX < 2f - .0001f && maxX > 2f + .0001f, Is.False,
                            "One paint triangle spans two different support planes.");
                        foreach (Vector3 point in new[] { a, b, c })
                            Assert.That(point.y, Is.EqualTo(RoadHeight(point.x)).Within(.0001f),
                                "The marking changed its physical support height.");
                        area += ProjectedArea(a, b, c);
                        paintTriangles++;
                    }
                }
            }
            Assert.That(paintTriangles, Is.GreaterThan(0));
            Assert.That(area, Is.EqualTo(2.4f).Within(.0001f), "Projection lost or doubled the paint footprint.");
            Physics.SyncTransforms();
            foreach (float x in new[] { 1f, 3f })
            {
                RaycastHit hit = GroundHit(new Vector3(x, 2f, 1f));
                Assert.That(hit.point.y, Is.EqualTo(RoadHeight(x)).Within(.0001f), DescribeGroundHit(hit));
                Assert.That(hit.collider.GetComponent<FootstepGround>().ResolveKind(hit.triangleIndex),
                    Is.EqualTo(FootstepGroundKind.Concrete), "Paint must inherit the road's footstep material.");
            }
        }

        [Test]
        public void FinalizeSurface_HigherPriorityOwnsOverlapAndReplacesSourceCollision()
        {
            MeshRenderer soil = CreateSurface("Base Soil", Quad(0, 4, 0, 4, 0), QuadIndices,
                renderer => CityExteriorAppearance.ApplyGroundSurface(renderer), FootstepGroundKind.Soil);
            MeshRenderer pavement = CreateSurface("Raised Pavement", Quad(1, 3, 1, 3, .14f), QuadIndices,
                CityExteriorAppearance.ApplySidewalkSurface, FootstepGroundKind.Concrete);
            CityGroundSurfaceSystem.Register(soil, 0);
            CityGroundSurfaceSystem.Register(pavement, 30);
            system.FinalizeSurface();

            Assert.That(soil.enabled, Is.False);
            Assert.That(pavement.enabled, Is.False);
            Assert.That(soil.GetComponent<MeshCollider>().enabled, Is.False);
            Assert.That(pavement.GetComponent<MeshCollider>().enabled, Is.False);
            Assert.That(system.SourceCount, Is.EqualTo(2));
            Assert.That(system.SectorCount, Is.EqualTo(1));
            float soilArea = 0f, pavementArea = 0f;
            foreach (MeshRenderer renderer in GroundRoot.GetComponentsInChildren<MeshRenderer>())
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                MeshCollider collider = renderer.GetComponent<MeshCollider>();
                Assert.That(renderer.enabled, Is.True);
                Assert.That(collider.enabled, Is.True);
                Assert.That(collider.sharedMesh, Is.SameAs(mesh));
                for (int slot = 0; slot < mesh.subMeshCount; slot++)
                {
                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties, slot);
                    float kind = properties.GetFloat("_GroundKind");
                    Vector3[] vertices = mesh.vertices;
                    int[] indices = mesh.GetTriangles(slot);
                    for (int index = 0; index < indices.Length; index += 3)
                    {
                        float area = ProjectedArea(vertices[indices[index]], vertices[indices[index + 1]],
                            vertices[indices[index + 2]]);
                        if (kind == (float)GroundSurfaceKind.Soil) soilArea += area;
                        else if (kind == (float)GroundSurfaceKind.Paving) pavementArea += area;
                    }
                }
            }
            Assert.That(soilArea, Is.EqualTo(12f).Within(.0001f));
            Assert.That(pavementArea, Is.EqualTo(4f).Within(.0001f));
            Physics.SyncTransforms();
            RaycastHit pavementHit = GroundHit(new Vector3(1.4f, 2f, 2.2f));
            Assert.That(pavementHit.point.y, Is.EqualTo(.14f).Within(.0001f));
            Assert.That(pavementHit.collider.GetComponent<FootstepGround>().ResolveKind(pavementHit.triangleIndex),
                Is.EqualTo(FootstepGroundKind.Concrete));
            RaycastHit soilHit = GroundHit(new Vector3(.4f, 2f, 2.2f));
            Assert.That(soilHit.point.y, Is.Zero.Within(.0001f));
            Assert.That(soilHit.collider.GetComponent<FootstepGround>().ResolveKind(soilHit.triangleIndex),
                Is.EqualTo(FootstepGroundKind.Soil));
        }

        [TestCase("_Smoothness")]
        [TestCase("_BaseColor")]
        [TestCase("_BaseMap")]
        [TestCase("presence")]
        public void FinalizeSurface_EqualStylesMergeAndChangedPropertyOverrideRemainsDistinct(string property)
        {
            MeshRenderer changed = null;
            for (int surface = 0; surface < 3; surface++)
            {
                MeshRenderer renderer = CreateSurface("Style " + surface,
                    Quad(surface * 2f, surface * 2f + 1f, 0f, 1f, 0f), QuadIndices,
                    candidate => ApplyStyleProbe(candidate, property, false), FootstepGroundKind.Soil);
                CityGroundSurfaceSystem.Register(renderer, 0);
                changed = renderer;
            }
            // Finalization must capture a builder's late override while still
            // merging the two sources whose complete appearance is equal.
            ApplyStyleProbe(changed, property, true);
            system.FinalizeSurface();

            MeshRenderer sector = GroundRoot.GetComponentInChildren<MeshRenderer>();
            Mesh mesh = sector.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.subMeshCount, Is.EqualTo(2));
            CollectionAssert.AreEquivalent(new[] { 12, 6 }, new[]
                { mesh.GetTriangles(0).Length, mesh.GetTriangles(1).Length });
            int changedSlot = mesh.GetTriangles(0).Length == 6 ? 0 : 1;
            var properties = new MaterialPropertyBlock();
            sector.GetPropertyBlock(properties, changedSlot);
            switch (property)
            {
                case "_BaseMap": Assert.That(properties.GetTexture(property), Is.SameAs(Texture2D.blackTexture)); break;
                case "_BaseColor": Assert.That(properties.GetColor(property), Is.EqualTo(Color.blue)); break;
                case "presence": Assert.That(properties.HasProperty("_Smoothness"), Is.True); break;
                default: Assert.That(properties.GetFloat(property), Is.EqualTo(.7f)); break;
            }
        }

        private static void ApplyStyleProbe(Renderer renderer, string property, bool alternate)
        {
            var properties = new MaterialPropertyBlock();
            switch (property)
            {
                case "_BaseMap": properties.SetTexture(property, alternate ? Texture2D.blackTexture : Texture2D.whiteTexture); break;
                case "_BaseColor": properties.SetColor(property, alternate ? Color.blue : Color.red); break;
                case "presence": if (alternate) properties.SetFloat("_Smoothness", 0f); break;
                default: properties.SetFloat(property, alternate ? .7f : .2f); break;
            }
            renderer.SetPropertyBlock(properties);
        }

        private Transform GroundRoot => root.transform.Find("City Ground");
        private static readonly int[] QuadIndices = { 0, 1, 2, 2, 1, 3 };

        private MeshRenderer CreateSurface(string name, Vector3[] vertices, int[] indices,
            Action<Renderer> appearance, FootstepGroundKind kind = FootstepGroundKind.None)
        {
            var host = new GameObject(name);
            host.transform.SetParent(root.transform, false);
            var mesh = new Mesh { name = name + " Test Mesh", vertices = vertices, triangles = indices };
            var uv = new Vector2[vertices.Length];
            for (int index = 0; index < uv.Length; index++) uv[index] = new Vector2(vertices[index].x, vertices[index].z);
            mesh.uv = uv;
            mesh.RecalculateNormals();
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            host.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            MeshRenderer renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            host.AddComponent<MeshCollider>().sharedMesh = mesh;
            if (kind != FootstepGroundKind.None) FootstepGround.Stamp(host, kind);
            appearance(renderer);
            return renderer;
        }

        private RaycastHit GroundHit(Vector3 origin)
        {
            var ray = new Ray(origin, Vector3.down);
            foreach (MeshCollider collider in GroundRoot.GetComponentsInChildren<MeshCollider>())
                if (collider.Raycast(ray, out RaycastHit hit, 4f)) return hit;
            Assert.Fail("No composed ground collider beneath " + origin);
            return default;
        }

        private static string DescribeGroundHit(RaycastHit hit)
        {
            var collider = (MeshCollider)hit.collider;
            Mesh mesh = collider.sharedMesh;
            int[] indices = mesh.triangles;
            int start = hit.triangleIndex * 3;
            if (start < 0 || start + 2 >= indices.Length)
                return "Collider " + collider.name + " returned out-of-range triangle " + hit.triangleIndex;
            Vector3[] vertices = mesh.vertices;
            Vector3 a = collider.transform.TransformPoint(vertices[indices[start]]);
            Vector3 b = collider.transform.TransformPoint(vertices[indices[start + 1]]);
            Vector3 c = collider.transform.TransformPoint(vertices[indices[start + 2]]);
            Vector3 normal = Vector3.Cross(b - a, c - a);
            float manual = a.y - (normal.x * (hit.point.x - a.x) + normal.z * (hit.point.z - a.z)) / normal.y;
            double ax = (double)b.x - a.x, ay = (double)b.y - a.y, az = (double)b.z - a.z;
            double bx = (double)c.x - a.x, by = (double)c.y - a.y, bz = (double)c.z - a.z;
            double nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
            double precise = a.y - (nx * ((double)hit.point.x - a.x) + nz * ((double)hit.point.z - a.z)) / ny;
            return $"Collider {collider.name}, triangle {hit.triangleIndex}, hit {hit.point.ToString("G9")}, " +
                $"barycentric {hit.barycentricCoordinate.ToString("G9")}, " +
                $"A {a.ToString("G9")} expectedY={RoadHeight(a.x):G9}, " +
                $"B {b.ToString("G9")} expectedY={RoadHeight(b.x):G9}, " +
                $"C {c.ToString("G9")} expectedY={RoadHeight(c.x):G9}, " +
                $"cross {normal.ToString("G9")}, areaXZ={Math.Abs(ny) * .5:G17}, " +
                $"manualY={manual:G9}, doubleManualY={precise:G17}.";
        }

        private static Vector3[] Quad(float minX, float maxX, float minZ, float maxZ, float height) => new[]
        {
            new Vector3(minX, height, minZ), new Vector3(minX, height, maxZ),
            new Vector3(maxX, height, minZ), new Vector3(maxX, height, maxZ)
        };
        private static float RoadHeight(float x) => x <= 2f ? .1f * x : .2f - .05f * (x - 2f);
        private static float ProjectedArea(Vector3 a, Vector3 b, Vector3 c) =>
            Mathf.Abs((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x)) * .5f;
    }
}
