using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("City common ground, painted roads, local excavation and collider-free Home view.")]
        public IEnumerator CityGroundSurface()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime((float)((12d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                GameTimeState.GameMinutesPerRealSecond));
            CityGameRoot city = null;
            yield return Capture(SceneIds.City, () =>
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                return city != null && city.IsInitialized ? city : null;
            }, () => CommonGroundShots(city));
            AssertCommonGroundExcavation(city);
            yield return AssertCommonGroundSand(city);
            AssertHomeCommonGround(city.Layout);
        }

        private static Shot[] CommonGroundShots(CityGameRoot city)
        {
            CityGroundSurfaceSystem system = city.World.Root.GetComponent<CityGroundSurfaceSystem>();
            Assert.That(system, Is.Not.Null);
            Assert.That(system.SourceCount, Is.GreaterThan(0));
            Assert.That(system.SectorCount, Is.GreaterThan(0));
            Assert.That(system.SurfaceTriangleCount, Is.GreaterThan(0));
            // Retain every measured production source and sector. Candidate
            // traversal can alter equivalent polygon subdivisions, so coverage,
            // height and appearance are checked below rather than a fan count.
            Assert.That(system.SourceCount, Is.EqualTo(208));
            Assert.That(system.SectorCount, Is.EqualTo(98));
            Debug.Log($"City ground composition: {system.BuildMilliseconds:F1} ms, {system.SurfaceTriangleCount} top triangles (previous capture 25136.4 ms, 425068 triangles).");
            Transform common = system.transform.Find("City Ground");
            Assert.That(common, Is.Not.Null);
            Assert.That(common.GetComponentsInChildren<MeshRenderer>(), Is.Not.Empty);
            string[] formerLayers = { "Street Surfaces", "Curved Street Surfaces", "Park Paths",
                "Sidewalk Surfaces", "Curved Sidewalk Surfaces", "Road Center Markings",
                "Curved Road Center Markings", "Pedestrian Crossings" };
            foreach (Renderer renderer in city.World.Root.GetComponentsInChildren<Renderer>(true))
                if (formerLayers.Contains(renderer.name))
                    Assert.That(renderer.enabled, Is.False, renderer.name + " still draws a separate ground layer.");

            Physics.SyncTransforms();
            CityStreetSurfacePlan plan = CityStreetSurfacePlanner.Create(city.Layout);
            Assert.That(plan.CenterMarkingGeometry, Is.Not.Empty);
            RuntimeOrientedBox mark = plan.CenterMarkingGeometry[0];
            float markingRoadTop = CommonPlannedRoadTop(plan, mark.Center);
            Vector3 marking = new Vector3(mark.Center.x, markingRoadTop, mark.Center.z);
            AssertCommonGroundContact(system, marking, FootstepGroundKind.Concrete,
                Resources.Load<Texture2D>(CityExteriorAppearance.RoadMarkingTextureResourcePath));
            Assert.That(plan.CrosswalkMarkingGeometry, Is.Not.Empty);
            // Curved crossings also use oriented paint boxes, but their road
            // substrate is a ribbon. Keep this probe on a straight crossing;
            // the separate curved-road probe below checks its graded ribbon.
            RuntimeOrientedBox crossingPaint = plan.CrosswalkMarkingGeometry.First(box =>
                plan.StreetGeometry.Any(street => street.TrySampleTop(box.Center, out _)));
            Vector3 crossing = crossingPaint.Center;
            crossing.y = CommonPlannedRoadTop(plan, crossing);
            AssertCommonGroundContact(system, crossing, FootstepGroundKind.Concrete,
                Resources.Load<Texture2D>(CityExteriorAppearance.RoadMarkingTextureResourcePath));

            RuntimeOrientedBox pavement = plan.SidewalkGeometry[0];
            Assert.That(pavement.TrySampleTop(pavement.Center, out float pavementTop), Is.True);
            AssertCommonGroundContact(system, new Vector3(pavement.Center.x, pavementTop, pavement.Center.z),
                FootstepGroundKind.Stone, Resources.Load<Texture2D>(CityExteriorAppearance.SidewalkTextureResourcePath));

            Assert.That(city.Layout.RoadGeometry.CurvedEdges, Is.Not.Empty);
            RoadEdge curve = city.Layout.RoadGeometry.CurvedEdges[0];
            CityRoadPath path = city.Layout.RoadGeometry.Get(curve);
            CityRoadSample middle = path.SampleDistance(path.Length * .5f);
            float datum = city.Layout.ElevationPlan.SampleRoadDatum(curve, .5f);
            Vector3 road = new Vector3(middle.Position.x, datum + CityStreetSurfacePlanner.RoadTop, middle.Position.y);
            AssertCommonGroundContact(system, road, FootstepGroundKind.Concrete,
                Resources.Load<Texture2D>(CityExteriorAppearance.RoadTextureResourcePath), checkTexture: false);
            Vector2 sidePoint = middle.Position + middle.Right * (city.Layout.RoadWidth * .5f - .5f);
            Vector3 side = new Vector3(sidePoint.x, datum + CityStreetSurfacePlanner.SidewalkTop, sidePoint.y);
            AssertCommonGroundContact(system, side, FootstepGroundKind.Stone,
                Resources.Load<Texture2D>(CityExteriorAppearance.SidewalkTextureResourcePath));

            var shots = new List<Shot>
            {
                Shot.At("common-ground-01-road-paint", marking + new Vector3(2f, 2.1f, -3f), marking, 62f),
                Shot.At("common-ground-02-curved-kerb", road + Vector3.up * EyeHeight,
                    side + new Vector3(middle.Tangent.x, .12f, middle.Tangent.y) * 6f, 72f)
            };
            CityEastGroundTransitionPlan seam = CityEastGroundTransitionPlan.Create(city.Layout);
            Assert.That(seam.IsEnabled, Is.True);
            Vector2 seamPoint = new Vector2(seam.Bounds.center.x, seam.SeamZ);
            Assert.That(CityTerrainSurfacePlan.TrySampleGroundTop(city.Layout, seamPoint, out float seamTop, out _), Is.True);
            Vector3 seamTarget = new Vector3(seamPoint.x, seamTop, seamPoint.y);
            Assert.That(TryCommonGroundHit(system, seamTarget, out RaycastHit seamHit), Is.True);
            Assert.That(seamHit.point.y, Is.EqualTo(seamTop).Within(.015f), "The garden/yard join changed its physical height.");
            shots.Add(Shot.At("common-ground-03-garden-yard-seam", seamTarget + new Vector3(-4f, 2.5f, -4f),
                seamTarget + Vector3.right * 4f, 72f));
            return shots.ToArray();
        }

        private static float CommonPlannedRoadTop(CityStreetSurfacePlan plan, Vector3 point)
        {
            bool found = false;
            float top = float.NegativeInfinity;
            foreach (RuntimeOrientedBox box in plan.StreetGeometry)
                if (box.TrySampleTop(point, out float candidate)) { found = true; top = Mathf.Max(top, candidate); }
            Assert.That(found, Is.True, "The chosen road-paint probe has no planned substrate.");
            return top;
        }

        private static void AssertCommonGroundContact(CityGroundSurfaceSystem system, Vector3 expected,
            FootstepGroundKind kind, Texture texture, bool checkTexture = true)
        {
            Assert.That(TryCommonGroundHit(system, expected, out RaycastHit hit), Is.True, "Missing common ground at " + expected);
            Assert.That(hit.point.y, Is.EqualTo(expected.y).Within(.003f), "Render/collision height at " + expected);
            FootstepGround marker = hit.collider.GetComponent<FootstepGround>();
            Assert.That(marker, Is.Not.Null);
            Assert.That(marker.ResolveKind(hit.triangleIndex), Is.EqualTo(kind), "Surface sound at " + expected);
            if (!checkTexture) return;
            Assert.That(texture, Is.Not.Null);
            MeshCollider collider = (MeshCollider)hit.collider;
            int triangle = hit.triangleIndex;
            int slot = 0;
            while (slot < collider.sharedMesh.subMeshCount - 1)
            {
                int count = (int)collider.sharedMesh.GetIndexCount(slot) / 3;
                if (triangle < count) break;
                triangle -= count; slot++;
            }
            Renderer renderer = collider.GetComponent<Renderer>();
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, slot);
            Texture actual = block.GetTexture("_BaseMap");
            if (actual == null) actual = renderer.sharedMaterials[slot].GetTexture("_BaseMap");
            Assert.That(actual, Is.SameAs(texture), "The contact must belong to its visible material region.");
        }

        private static bool TryCommonGroundHit(CityGroundSurfaceSystem system, Vector3 expected, out RaycastHit nearest)
        {
            nearest = default;
            bool found = false;
            var ray = new Ray(expected + Vector3.up * .30f, Vector3.down);
            foreach (MeshCollider collider in system.GetComponentsInChildren<MeshCollider>(true))
            {
                if (!collider.enabled || !collider.gameObject.activeInHierarchy ||
                    !(collider.name.StartsWith("Ground sector") || collider.name.StartsWith("Dynamic ground sector"))) continue;
                if (collider.Raycast(ray, out RaycastHit hit, .60f) && (!found || hit.distance < nearest.distance))
                { found = true; nearest = hit; }
            }
            return found;
        }

        private static void AssertCommonGroundExcavation(CityGameRoot city)
        {
            CityGroundSurfaceSystem system = city.World.Root.GetComponent<CityGroundSurfaceSystem>();
            CityCemeteryGroundExcavation excavation = city.World.CemeteryGroundExcavation;
            Assert.That(excavation, Is.Not.Null);
            CityCemeteryPartDescriptor alley = city.World.CemeteryPlan.Parts.First(part =>
                part.Kind == CityCemeteryPartKind.Alley && part.Size.x > .8f && part.Size.z > .8f &&
                TryCommonGroundHit(system, part.Center, out RaycastHit hit) && hit.collider.name.StartsWith("Dynamic ground sector"));
            Vector3 point = new Vector3(alley.Center.x, city.World.CemeteryPlan.GroundTopY, alley.Center.z);
            var mouth = new Rect(point.x - .25f, point.z - .30f, .50f, .60f);
            Assert.That(TryCommonGroundHit(system, point, out RaycastHit before), Is.True);
            Assert.That(excavation.Excavate(mouth), Is.True);
            try
            {
                Physics.SyncTransforms();
                Assert.That(TryCommonGroundHit(system, before.point, out _), Is.False,
                    "A dug mouth must remove its painted alley and every ground cap.");
            }
            finally { Assert.That(excavation.Fill(mouth), Is.True); }
            Physics.SyncTransforms();
            Assert.That(TryCommonGroundHit(system, before.point, out RaycastHit filled), Is.True);
            Assert.That(filled.point.y, Is.EqualTo(before.point.y).Within(.003f));
        }

        private static IEnumerator AssertCommonGroundSand(CityGameRoot city)
        {
            CitySandTreading sand = city.World.Root.GetComponentsInChildren<CitySandTreading>().Single();
            Mesh visual = sand.GetComponent<MeshFilter>().sharedMesh;
            MeshCollider collider = sand.GetComponent<MeshCollider>();
            Assert.That(sand.enabled && sand.GetComponent<Renderer>().enabled && collider.enabled, Is.True);
            Assert.That(visual, Is.Not.SameAs(collider.sharedMesh));
            Vector3[] physicalBefore = collider.sharedMesh.vertices;
            Vector3 point = visual.vertices.First(vertex => sand.SampleVisibleDepth(vertex) > .035f);
            float looseBefore = sand.SampleVisibleDepth(point);
            sand.Press(point);
            yield return new WaitForSeconds(CitySandTreading.RebuildInterval * 2f);
            Assert.That(sand.SampleVisibleDepth(point), Is.LessThan(looseBefore * .9f));
            CollectionAssert.AreEqual(physicalBefore, collider.sharedMesh.vertices,
                "Local loose sand must still deform independently of physical ground.");
        }

        private static void AssertHomeCommonGround(CityLayout layout)
        {
            var host = new GameObject("Common Ground Home Acceptance");
            host.SetActive(false);
            try
            {
                HomeInteriorLayoutPlan interior = HomeInteriorLayoutPlanner.Generate();
                Transform exterior = HomeExteriorViewBuilder.Build(host.transform,
                    HomeBalconyLayoutPlanner.Generate(interior), HomeExteriorContextPlanner.Generate(layout));
                CityGroundSurfaceSystem system = exterior.GetComponent<CityGroundSurfaceSystem>();
                Assert.That(system, Is.Not.Null);
                Assert.That(system.SectorCount, Is.GreaterThan(0));
                Assert.That(exterior.GetComponentsInChildren<Collider>(true).Any(collider => collider.enabled), Is.False,
                    "The bounded Home reconstruction must remain collider-free.");
                foreach (Renderer renderer in exterior.GetComponentsInChildren<Renderer>(true))
                    if (renderer.name.StartsWith("Home Exterior Street Surfaces") ||
                        renderer.name.StartsWith("Home Exterior Sidewalk Surfaces") ||
                        renderer.name.StartsWith("Home Exterior Road Center Markings") ||
                        renderer.name.StartsWith("Home Exterior Pedestrian Crossings"))
                        Assert.That(renderer.enabled, Is.False, renderer.name);
            }
            finally { Object.DestroyImmediate(host); }
        }
    }
}
