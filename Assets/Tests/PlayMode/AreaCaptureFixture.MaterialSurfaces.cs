using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class MaterialSurfaceAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type setup = Type.GetType(
                "BarPromenade.Editor.VillageJunctionTextureSetup, BarPromenade.Editor", true);
            setup.GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Production exterior surfaces and lighting, without gameplay/NPC startup.")]
        [PrebuildSetup(typeof(MaterialSurfaceAssetsSetup))]
        public IEnumerator MaterialSurfaces()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.CreateScene("MaterialSurfaceInspection");
            SceneManager.SetActiveScene(scene);
            var disabled = new List<GameObject>();
            foreach (Camera existing in Object.FindObjectsByType<Camera>())
                if (existing.gameObject.activeSelf)
                {
                    existing.gameObject.SetActive(false);
                    disabled.Add(existing.gameObject);
                }
            GameTimeRuntime clock = Object.FindAnyObjectByType<GameTimeRuntime>();
            bool clockEnabled = clock != null && clock.enabled;
            if (clock != null) clock.enabled = false;
            VolumeProfile profile = null;
            try
            {
                GameSessionState.BeginNewGame("material-surfaces-capture");
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
                var camera = new GameObject("Material inspection camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                var sun = new GameObject("Material inspection sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                RenderSettings.sun = sun;
                var volume = new GameObject("Material inspection grade").AddComponent<Volume>();
                volume.isGlobal = true;
                profile = RuntimeSceneSetup.CreateCityNoirRuntimeProfile();
                volume.sharedProfile = profile;

                var stage = new GameObject("City material surfaces");
                RuntimeSceneSetup.EnsureCityNight();
                CityLayout city = CityLayoutGenerator.Generate(CityBlueprintCatalog.Default,
                    CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);
                CityNightFixturePlan nightPlan = CityLayoutCache.GetOrCreateNightPlan(city);
                yield return BuildMaterialStage(CityWorldBuilder.BuildSteps(stage.transform, city,
                    CityGenerationSettings.Default, nightPlan, _ => { }), "roads_and_river");
                MeshFilter cityRoad = FindMaterialMesh(stage, "Street Surfaces");
                AssertMaterialRoadCoordinates(cityRoad.sharedMesh);
                AssertMaterialProperty(cityRoad.GetComponent<Renderer>(), "_GroundRoadCoordinates");
                CityWetSurfaceRegistry.SetImmediate(.65f);
                CityWaterResources.SetRainIntensity(.45f);
                CityStreetSurfacePlan streets = CityStreetSurfacePlanner.Create(city);
                RuntimeOrientedBox roadBox = default;
                float nearest = float.PositiveInfinity;
                foreach (RuntimeOrientedBox candidate in streets.StreetGeometry)
                {
                    if (candidate.Size.z < candidate.Size.x * 1.5f) continue;
                    float distance = (candidate.Center - city.SpawnWorldPosition).sqrMagnitude;
                    if (distance >= nearest) continue;
                    roadBox = candidate;
                    nearest = distance;
                }
                Assert.That(nearest, Is.LessThan(float.PositiveInfinity));
                Vector3 center = roadBox.Center + roadBox.Rotation * Vector3.up * (roadBox.Size.y * .5f);
                Vector3 forward = roadBox.Rotation * Vector3.forward;
                Vector3 right = roadBox.Rotation * Vector3.right;
                yield return CaptureMaterialFrame(camera, "City", "materials-00-asphalt-close",
                    center - forward * 2.8f + Vector3.up * 1.65f, center + forward * 1.4f);
                yield return CaptureMaterialFrame(camera, "City", "materials-01-asphalt-edge",
                    center - forward * 2.3f + right * 1.9f + Vector3.up * 1.45f,
                    center + right * (streets.CarriagewayWidth * .5f) + forward);
                CityNightWorldResult night = CityNightWorldBuilder.Build(stage.transform, nightPlan,
                    Array.Empty<BarEntrance>());
                night.InitializeAtmosphere(camera.transform, Array.Empty<Vector3>());
                DayNightVisualSample nightSample = GameTimeDayNightRules.Evaluate(20 * 60);
                RuntimeSceneSetup.ApplyCityExteriorLighting(nightSample);
                night.SetNightFactor(nightSample.NightFactor, true);
                night.Atmosphere.RefreshImmediate();
                yield return CaptureMaterialFrame(camera, "City", "materials-02-asphalt-night",
                    center - forward * 3.5f + Vector3.up * 1.65f, center + forward * 4f);
                Object.DestroyImmediate(stage);

                stage = new GameObject("Mountain material surfaces");
                RuntimeSceneSetup.EnsureMountainRoad();
                MountainRoadPlan mountain = MountainRoadPlanner.Create(GameSessionState.DefaultCitySeed);
                yield return BuildMaterialStage(MountainRoadWorldBuilder.BuildSteps(stage.transform,
                    mountain, camera, _ => { }), "road_bridge_tunnel");
                MeshFilter mountainRoad = FindMaterialMesh(stage, "Continuous Narrow Road");
                AssertMaterialRoadCoordinates(mountainRoad.sharedMesh);
                AssertMaterialProperty(mountainRoad.GetComponent<Renderer>(), "_GroundRoadCoordinates");
                MeshFilter mountainSnow = FindMaterialMesh(stage, "Upper Snow");
                AssertMaterialProperty(mountainSnow.GetComponent<Renderer>(), "_GroundVertexData");
                AssertMaterialSnowControls(mountainSnow.sharedMesh);
                MountainRoadRouteSample upper = mountain.Route.Sample(mountain.Route.Length * .77f);
                Vector3 edge = upper.Position + upper.Right * (upper.Width * .5f + .6f);
                edge.y = MountainRoadTerrainSampler.SampleHeight(mountain.Route, mountain.Plateau,
                    new Vector2(edge.x, edge.z));
                yield return CaptureMaterialFrame(camera, "MountainRoad", "materials-01-snow-road-edge",
                    upper.Position - upper.Forward * 2.5f + Vector3.up * 1.65f, edge);
                Object.DestroyImmediate(stage);

                stage = new GameObject("Village material surfaces");
                RuntimeSceneSetup.EnsureAlpineVillage();
                AlpineVillagePlan village = AlpineVillagePlanner.Create(GameSessionState.DefaultCitySeed);
                yield return BuildMaterialStage(AlpineVillageWorldBuilder.BuildSteps(stage.transform,
                    village, _ => { }), "brook_and_snow");
                MeshFilter villageRoad = FindMaterialMesh(stage, "Village Ground");
                AssertMaterialRoadCoordinates(villageRoad.sharedMesh);
                AssertMaterialProperty(villageRoad.GetComponent<Renderer>(), "_GroundRoadCoordinates", 2);
                MeshFilter snow = FindMaterialMesh(stage, AlpineVillageWorldBuilder.SnowDriftObjectName);
                AssertMaterialRoadCoordinates(snow.sharedMesh);
                AssertMaterialProperty(snow.GetComponent<Renderer>(), "_GroundVertexData", 0);
                AssertMaterialSnowControls(snow.sharedMesh);
                AlpineVillageSnowTreading treading = snow.GetComponent<AlpineVillageSnowTreading>();
                treading.enabled = false;
                Vector3 junction = VillageMaterialGround(village, new Vector2(-130f, -28f));
                AssertMaterialSnowTransition(snow.sharedMesh, treading, junction, village);
                yield return CaptureMaterialFrame(camera, "AlpineVillage", "materials-01-road-soil-junction",
                    VillageMaterialGround(village, new Vector2(-126f, -23f)) + Vector3.up * 1.6f, junction);
            }
            finally
            {
                foreach (GameObject root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
                if (profile != null) Object.DestroyImmediate(profile);
                SceneManager.SetActiveScene(previous);
                SceneManager.UnloadSceneAsync(scene);
                foreach (GameObject original in disabled) if (original != null) original.SetActive(true);
                if (clock != null) clock.enabled = clockEnabled;
            }
        }

        private static IEnumerator BuildMaterialStage(IEnumerator steps, string lastPhase)
        {
            bool reached = false;
            try
            {
                while (steps.MoveNext())
                {
                    if (steps.Current is CompositionStep step && step.Phase == lastPhase)
                    {
                        reached = true;
                        break;
                    }
                    yield return null;
                }
                Assert.That(reached, Is.True, "Missing surface composition phase: " + lastPhase);
            }
            finally { (steps as IDisposable)?.Dispose(); }
        }

        private static IEnumerator CaptureMaterialFrame(Camera camera, string area, string name,
            Vector3 eye, Vector3 target)
        {
            camera.fieldOfView = 62f;
            camera.transform.position = eye;
            camera.transform.LookAt(target + Vector3.up * .04f);
            for (int frame = 0; frame < 3; frame++) yield return null;
            CaptureCurrentCamera(camera, area, name);
        }

        private static Vector3 VillageMaterialGround(AlpineVillagePlan plan, Vector2 local)
        {
            Vector3 point = plan.Expansion.ToWorld(local);
            point.y = AlpineVillageTerrainSampler.SampleMeshHeight(plan, new Vector2(point.x, point.z));
            return point;
        }

        private static void AssertMaterialSnowTransition(Mesh mesh, AlpineVillageSnowTreading treading,
            Vector3 near, AlpineVillagePlan plan)
        {
            Vector3[] vertices = mesh.vertices;
            Color[] before = mesh.colors;
            int chosen = -1;
            float nearest = float.PositiveInfinity;
            for (int index = 0; index < vertices.Length; index++)
            {
                // Paths can be completely bare already. Press actual raised
                // snow; a buried zero-depth toe may share its XZ with a drift.
                float ground = AlpineVillageTerrainSampler.SampleMeshHeight(plan,
                    new Vector2(vertices[index].x, vertices[index].z));
                if (vertices[index].y - ground <= .08f) continue;
                if (treading.SampleVisibleDepth(vertices[index]) <= .02f) continue;
                float distance = (vertices[index] - near).sqrMagnitude;
                if (distance >= nearest) continue;
                chosen = index;
                nearest = distance;
            }
            Assert.That(chosen, Is.GreaterThanOrEqualTo(0), "No raised snow to compact.");
            Vector3 contact = vertices[chosen];
            float depth = treading.SampleVisibleDepth(contact);
            Assert.That(depth, Is.GreaterThan(0f));
            Assert.That(treading.ClearPatch(contact, Vector3.right, new Vector2(1.5f, 1.5f), .65f),
                Is.GreaterThan(0));
            Assert.That(treading.SampleVisibleDepth(contact), Is.LessThan(depth));
            Color after = mesh.colors[chosen];
            Assert.That(after.r, Is.GreaterThan(before[chosen].r));
            Assert.That(after.g, Is.GreaterThanOrEqualTo(before[chosen].g));
            Assert.That(after.b, Is.EqualTo(before[chosen].b));
            Assert.That(after.a, Is.EqualTo(before[chosen].a));
            Assert.That(mesh.vertices[chosen].y, Is.LessThan(contact.y));
            AssertMaterialSnowControls(mesh);
        }

        private static void AssertMaterialSnowControls(Mesh mesh)
        {
            Color[] controls = mesh.colors;
            Assert.That(controls.Length, Is.EqualTo(mesh.vertexCount), mesh.name);
            bool finite = true;
            foreach (Color control in controls)
                for (int channel = 0; channel < 4; channel++)
                    finite &= float.IsFinite(control[channel]) && control[channel] >= 0f && control[channel] <= 1f;
            Assert.That(finite, Is.True, mesh.name + " has invalid snow response controls.");
        }

        private static MeshFilter FindMaterialMesh(GameObject root, string name)
        {
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.name == name) return filter;
            Assert.Fail("Missing material capture surface: " + name);
            return null;
        }

        private static void AssertMaterialProperty(Renderer renderer, string property, int slot = -1)
        {
            var properties = new MaterialPropertyBlock();
            if (slot < 0) renderer.GetPropertyBlock(properties);
            else renderer.GetPropertyBlock(properties, slot);
            Assert.That(properties.GetFloat(property), Is.EqualTo(1f), renderer.name + " " + property);
        }

        private static void AssertMaterialRoadCoordinates(Mesh mesh)
        {
            Assert.That(mesh.HasVertexAttribute(VertexAttribute.TexCoord3), Is.True, mesh.name);
            var coordinates = new List<Vector4>();
            mesh.GetUVs(GroundSurfaceCoordinates.Channel, coordinates);
            Assert.That(coordinates.Count, Is.EqualTo(mesh.vertexCount));
            bool hasRoad = false, finite = true;
            foreach (Vector4 coordinate in coordinates)
            {
                for (int channel = 0; channel < 4; channel++) finite &= float.IsFinite(coordinate[channel]);
                hasRoad |= coordinate.z > 0f;
            }
            Assert.That(finite, Is.True, mesh.name + " has invalid road coordinates.");
            Assert.That(hasRoad, Is.True, mesh.name + " lost the road width.");
        }
    }
}
