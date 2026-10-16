using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Focused warehouse detail/material capture with real powered and blackout lighting; no combat or stair traversal loop.")]
        public IEnumerator CombatWarehouseDetailVisuals()
        {
            float previousDelta = Time.captureDeltaTime;
            CombatTestRoot root = null;
            Time.captureDeltaTime = 1f / 60f;
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            var report = new WarehouseDetailReport();
            try
            {
#if UNITY_EDITOR
                Type.GetType("BarPromenade.Editor.CombatWarehouseAssetValidation, BarPromenade.Editor", true)
                    .GetMethod("ValidateOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
                AssertWarehouseDetailPlans();
                GameSessionState.BeginNewGame();
                long started = Stopwatch.GetTimestamp();
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root != null && root.IsInitialized, Is.True);
                report.sceneLoadAndCompositionMilliseconds =
                    (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
                root.AutomaticSimulation = false;
                root.SetDuelLogging(false);
                root.SetSparring(false);
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;
                Assert.That(root.WarehousePlan.Seed, Is.Zero);
                AssertWarehouseImportedMetres(root);
                AssertWarehouseSurfaceMaterials(root);
                AssertWarehousePhysicalSurfaces(root);
                AssertWarehouseLightAndMechanisms(root);
                AssertWarehouseRoomDetailSelection(root);
                report.geometry = MeasureWarehouseDetailGeometry(root);

                var phases = new List<WarehouseDetailPhase>(2);
                yield return MeasureWarehouseDetailFrames(root, "Production lighting", phases);
                yield return CaptureWarehouseEnvironment(root, "Production lighting", false);

                // Commit the production source identities, then advance the existing
                // shutdown and lamp-failure owners. No test-specific lights or materials.
                var items = new[] { InventoryItemId.RottenFishTailCan, InventoryItemId.RottenFishBellyCan,
                    InventoryItemId.RottenFishHeadCan };
                for (int floor = 0; floor < items.Length; floor++)
                    Assert.That(GameSessionState.TryCollectWorldItem(root.WarehousePlan.Refrigerators[floor].SourceId,
                        items[floor]), Is.True);
                WarehouseArenaMechanisms machines = root.GetComponentInChildren<WarehouseArenaMechanisms>();
                WarehouseFishLamp pendant = root.GetComponentInChildren<WarehouseFishLamp>();
                Assert.That(pendant, Is.Not.Null);
                machines.Advance(WarehouseArenaMechanisms.FanShutdownDuration);
                Assert.That(GameSessionState.WarehouseFishLamp.TryBegin(true, true), Is.True);
                pendant.Advance(WarehouseFishLamp.HeatingSeconds);
                pendant.Advance(3f);
                Assert.That(pendant.Phase, Is.EqualTo(WarehouseFishLampPhase.Burst));
                Assert.That(machines.FanSpeedFactor, Is.Zero);
                Assert.That(root.Flashlight.SpotLight.isActiveAndEnabled, Is.True,
                    "The production chest light must remain the blackout's only source.");
                foreach (Light light in root.GetComponentsInChildren<Light>())
                    if (light != root.Flashlight.SpotLight)
                        Assert.That(light.isActiveAndEnabled && light.intensity > 0f, Is.False,
                            "The production blackout must disable " + light.name + ".");
                for (int frame = 0; frame < 4; frame++) yield return null;
                yield return MeasureWarehouseDetailFrames(root, "Power off", phases);
                yield return CaptureWarehouseEnvironment(root, "Power off", false, closeViewsOnly: true);
                report.phases = phases.ToArray();
                report.seed = root.WarehousePlan.Seed;
                report.unityVersion = Application.unityVersion;
                report.cpu = SystemInfo.processorType;
                report.gpu = SystemInfo.graphicsDeviceName;
                report.graphicsApi = SystemInfo.graphicsDeviceType.ToString();
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "Test warehouse detail report.json"), JsonUtility.ToJson(report, true));
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                GameSessionState.ResetInventoryState();
                Time.captureDeltaTime = previousDelta;
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = null;
            }
        }

        private static void AssertWarehouseDetailPlans()
        {
            var templates = new HashSet<WarehouseArenaTemplate>();
            var route = new List<Vector3>();
            foreach (int seed in new[] { 0, 1, 3 })
            {
                WarehouseArenaPlan plan = WarehouseArenaPlanner.Create(seed);
                AssertWarehouseDeterminism(plan, WarehouseArenaPlanner.Create(seed));
                AssertWarehouseRoomFurnitureFacesEntry(plan);
                templates.Add(plan.Template);
                Assert.That(plan.RoomCenters.Count, Is.EqualTo(9));
                Assert.That(plan.Stairs.Count, Is.EqualTo(4));
                var area = new WarehouseArenaArea(plan);
                foreach (Vector3 room in plan.RoomCenters)
                    Assert.That(area.FindRoute(plan.HeroSpawn, room, route), Is.True);
                foreach (Vector3 spawn in plan.SpawnPoints)
                    Assert.That(area.FindRoute(plan.HeroSpawn, spawn, route), Is.True);
                Assert.That(area.FindRoute(plan.HeroSpawn, plan.GateControlApproach, route), Is.True);
                Assert.That(area.CanTraverse(plan.GateApproach, plan.GoalDoorPosition), Is.False);
            }
            Assert.That(templates.Count, Is.EqualTo(3));
        }

        private static WarehouseDetailGeometry MeasureWarehouseDetailGeometry(CombatTestRoot root)
        {
            var renderers = new HashSet<MeshRenderer>();
            var meshes = new HashSet<Mesh>();
            var materials = new HashSet<Material>();
            var textures = new HashSet<Texture>();
            foreach (Transform wrapper in root.GetComponentsInChildren<Transform>(true))
            {
                if (!wrapper.name.StartsWith("Warehouse ", StringComparison.Ordinal) ||
                    Array.IndexOf(WarehouseArenaAssetProvider.ModelNames, wrapper.name.Substring("Warehouse ".Length)) < 0)
                    continue;
                foreach (MeshRenderer renderer in wrapper.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.enabled && renderer.gameObject.activeInHierarchy) renderers.Add(renderer);
            }
            var result = new WarehouseDetailGeometry { rendererCount = renderers.Count };
            foreach (MeshRenderer renderer in renderers)
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                meshes.Add(mesh);
                result.placedVertices += mesh.vertexCount;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    result.placedTriangles += mesh.GetIndexCount(submesh) / 3;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || !materials.Add(material)) continue;
                    foreach (string property in material.GetTexturePropertyNames())
                    {
                        Texture texture = material.GetTexture(property);
                        if (texture != null) textures.Add(texture);
                    }
                }
            }
            // Include the complete declared warehouse texture bank, including the
            // fish-label atlas, rather than counting only the nearby visible props.
            foreach (string name in WarehouseArenaAssetProvider.TextureNames)
            {
                Texture texture = Resources.Load<Texture>(WarehouseArenaAssetProvider.ResourceFolder + "Textures/" + name);
                Assert.That(texture, Is.Not.Null, name);
                textures.Add(texture);
            }
            foreach (Mesh mesh in meshes)
            {
                result.uniqueVertices += mesh.vertexCount;
                result.meshRuntimeBytes += Profiler.GetRuntimeMemorySizeLong(mesh);
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    result.uniqueTriangles += mesh.GetIndexCount(submesh) / 3;
            }
            var textureRows = new List<WarehouseDetailTexture>();
            foreach (Texture texture in textures)
            {
                long bytes = Profiler.GetRuntimeMemorySizeLong(texture);
                result.textureRuntimeBytes += bytes;
                textureRows.Add(new WarehouseDetailTexture { name = texture.name, width = texture.width,
                    height = texture.height, format = texture.graphicsFormat.ToString(), runtimeBytes = bytes });
            }
            textureRows.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            result.uniqueMeshCount = meshes.Count;
            result.sharedMaterialCount = materials.Count;
            result.textures = textureRows.ToArray();
            return result;
        }

        private static void AssertWarehouseRoomDetailSelection(CombatTestRoot root)
        {
            int index = (int)(unchecked((uint)root.WarehousePlan.Seed) % 3u), rooms = 0;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (WarehouseArenaPlacement placement in root.WarehousePlan.ModulePlacements)
            {
                if (placement.Kind != "TechnicalProps" && placement.Kind != "LockerProps" &&
                    placement.Kind != "BreakProps") continue;
                Transform wrapper = Array.Find(transforms, item => item.name == "Warehouse " + placement.Kind &&
                    Vector3.Distance(item.localPosition, placement.Position) < .001f);
                Assert.That(wrapper, Is.Not.Null);
                int selected = index++ % 3;
                var authored = new HashSet<int>();
                foreach (Renderer renderer in wrapper.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.name.StartsWith("RoomVariant", StringComparison.Ordinal)) continue;
                    int variant = renderer.name["RoomVariant".Length] - '0';
                    Assert.That(variant, Is.InRange(0, 2));
                    authored.Add(variant);
                    Assert.That(renderer.gameObject.activeInHierarchy, Is.EqualTo(variant == selected),
                        "Each room must show only its deterministic furnishing variant: " + renderer.name + ".");
                }
                Assert.That(authored.Count, Is.EqualTo(3), "Each furnishing module retains its three authored choices.");
                rooms++;
            }
            Assert.That(rooms, Is.EqualTo(9));
        }

        private static IEnumerator MeasureWarehouseDetailFrames(CombatTestRoot root, string phase,
            List<WarehouseDetailPhase> phases)
        {
            var main = WarehouseDetailRecorder(ProfilerCategory.Internal, "Main Thread");
            var render = WarehouseDetailRecorder(ProfilerCategory.Internal, "Render Thread");
            var allocations = WarehouseDetailRecorder(ProfilerCategory.Memory, "GC Allocated In Frame");
            var draws = WarehouseDetailRecorder(ProfilerCategory.Render, "Draw Calls Count");
            var batches = WarehouseDetailRecorder(ProfilerCategory.Render, "Batches Count");
            var intervals = new WarehouseDetailSamples();
            var mainSamples = new WarehouseDetailSamples();
            var renderSamples = new WarehouseDetailSamples();
            var allocationSamples = new WarehouseDetailSamples();
            var drawSamples = new WarehouseDetailSamples();
            var batchSamples = new WarehouseDetailSamples();
            try
            {
                // Counters and normal rendering settle before the bounded sample;
                // texture readback, PNG encoding and report generation stay outside it.
                for (int frame = 0; frame < 4; frame++) yield return null;
                long previous = Stopwatch.GetTimestamp();
                for (int frame = 0; frame < 16; frame++)
                {
                    yield return null;
                    long now = Stopwatch.GetTimestamp();
                    intervals.Add((now - previous) * 1000d / Stopwatch.Frequency);
                    previous = now;
                    mainSamples.Add(WarehouseDetailRecorderValue(main, .000001));
                    renderSamples.Add(WarehouseDetailRecorderValue(render, .000001));
                    allocationSamples.Add(WarehouseDetailRecorderValue(allocations));
                    drawSamples.Add(WarehouseDetailRecorderValue(draws));
                    batchSamples.Add(WarehouseDetailRecorderValue(batches));
                }
                int lights = 0;
                foreach (Light light in root.GetComponentsInChildren<Light>())
                    if (light.isActiveAndEnabled && light.intensity > 0f) lights++;
                phases.Add(new WarehouseDetailPhase { name = phase, activeLightCount = lights,
                    frameIntervalMilliseconds = intervals.Summary(), mainThreadMilliseconds = mainSamples.Summary(),
                    renderThreadMilliseconds = renderSamples.Summary(), gcAllocatedBytes = allocationSamples.Summary(),
                    drawCalls = drawSamples.Summary(), batches = batchSamples.Summary() });
            }
            finally
            {
                if (main.Valid) main.Dispose();
                if (render.Valid) render.Dispose();
                if (allocations.Valid) allocations.Dispose();
                if (draws.Valid) draws.Dispose();
                if (batches.Valid) batches.Dispose();
            }
        }

        private static ProfilerRecorder WarehouseDetailRecorder(ProfilerCategory category, string name)
        {
            try { return ProfilerRecorder.StartNew(category, name, 1); }
            catch (Exception) { return default; }
        }

        private static double WarehouseDetailRecorderValue(ProfilerRecorder recorder, double scale = 1d) =>
            recorder.Valid && recorder.Count > 0 ? recorder.LastValue * scale : -1d;

        private sealed class WarehouseDetailSamples
        {
            private int count;
            private double sum, maximum;
            public void Add(double value)
            {
                if (value < 0d) return;
                count++; sum += value; maximum = Math.Max(maximum, value);
            }
            public WarehouseDetailMetric Summary() => new WarehouseDetailMetric
                { sampleCount = count, mean = count > 0 ? sum / count : 0d, maximum = maximum };
        }

        [Serializable] private sealed class WarehouseDetailReport
        {
            public string name = "Test warehouse detail";
            public string interpretation = "Editor diagnostic at the same entry camera, seed and production settings; " +
                "frame intervals include pacing and Editor work, CPU markers can include waits, and sampleCount=0 means unavailable. " +
                "The bounded frame sample excludes screenshots, readback and PNG encoding; it is not a player benchmark. " +
                "Mesh/texture runtimeBytes are Unity runtime memory estimates, not a claim of GPU-only residency. " +
                "Zero batchmode draw/batch counters are unavailable; no GPU timing or baseline timing comparison was captured.";
            public string unityVersion, cpu, gpu, graphicsApi;
            public int seed;
            public double sceneLoadAndCompositionMilliseconds;
            public WarehouseDetailGeometry geometry;
            public WarehouseDetailPhase[] phases;
        }

        [Serializable] private sealed class WarehouseDetailGeometry
        {
            public int rendererCount, uniqueMeshCount, sharedMaterialCount;
            public long placedVertices, placedTriangles, uniqueVertices, uniqueTriangles, meshRuntimeBytes, textureRuntimeBytes;
            public WarehouseDetailTexture[] textures;
        }

        [Serializable] private sealed class WarehouseDetailTexture
        {
            public string name, format;
            public int width, height;
            public long runtimeBytes;
        }

        [Serializable] private sealed class WarehouseDetailPhase
        {
            public string name;
            public int activeLightCount;
            public WarehouseDetailMetric frameIntervalMilliseconds, mainThreadMilliseconds, renderThreadMilliseconds,
                gcAllocatedBytes, drawCalls, batches;
        }

        [Serializable] private sealed class WarehouseDetailMetric
        {
            public int sampleCount;
            public double mean, maximum;
        }
    }
}
