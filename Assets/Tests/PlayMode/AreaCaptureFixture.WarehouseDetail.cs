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
                Texture2D texture = Resources.Load<Texture2D>(WarehouseArenaAssetProvider.ResourceFolder + "Textures/" + name);
                Assert.That(texture, Is.Not.Null, name);
                int size = name == "WarehouseMasterAtlas" ? 4096 : name == "WarehouseMaterialResponse" ? 2048 : 512;
                Assert.That(texture.width, Is.EqualTo(size), name);
                Assert.That(texture.height, Is.EqualTo(size), name);
                Assert.That(texture.mipmapCount, Is.EqualTo((int)Math.Log(size, 2) + 1), name);
                for (int mipSize = size; ; mipSize /= 2)
                {
                    long blocks = Math.Max(1, (mipSize + 3) / 4);
                    result.declaredTextureBc7MipBytes += blocks * blocks * 16;
                    if (mipSize == 1) break;
                }
                textures.Add(texture);
            }
            Assert.That(result.declaredTextureBc7MipBytes, Is.EqualTo(28661184L),
                "The complete 4096/2048/two 512 bank must retain its BC7 mip accounting.");
            Assert.That(result.declaredTextureBc7MipBytes, Is.LessThanOrEqualTo(28L * 1024 * 1024));
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

        private static void AssertWarehouseSurfaceSlots(CombatTestRoot root)
        {
            TextAsset source = Resources.Load<TextAsset>(WarehouseArenaAssetProvider.ResourceFolder + "WarehouseSurfaceVariants");
            Assert.That(source, Is.Not.Null);
            WarehouseSurfaceManifest manifest = JsonUtility.FromJson<WarehouseSurfaceManifest>(source.text);
            GameObject library = Resources.Load<GameObject>(WarehouseArenaAssetProvider.ResourceFolder + "WarehouseSurfaceVariants");
            Assert.That(library, Is.Not.Null);
            var nodes = new Dictionary<string, Mesh>();
            foreach (MeshFilter filter in library.GetComponentsInChildren<MeshFilter>(true)) nodes.Add(filter.name, filter.sharedMesh);
            var replacements = new Dictionary<string, Dictionary<string, Mesh>>();
            foreach (WarehouseSurfaceVariant variant in manifest.variants)
            {
                var meshes = new Dictionary<string, Mesh>();
                foreach (WarehouseSurfaceMesh mesh in variant.meshes) meshes.Add(mesh.source, nodes[mesh.node]);
                replacements.Add(variant.kind + "/" + variant.slot, meshes);
            }
            var counts = new Dictionary<string, int>();
            foreach (WarehouseSurfaceCount count in manifest.counts)
            {
                counts.Add(count.kind, count.count);
                Assert.That(WarehouseArenaAssetProvider.SurfaceSlotCount(count.kind), Is.EqualTo(count.count));
            }
            var ordinals = new Dictionary<string, int>();
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            void AssertPlacement(string kind, Vector3 position, Quaternion rotation, bool substituted = false)
            {
                ordinals.TryGetValue(kind, out int ordinal);
                ordinals[kind] = ordinal + 1;
                int count = counts.TryGetValue(kind, out int authored) ? authored : 1;
                if (counts.ContainsKey(kind)) Assert.That(ordinal, Is.LessThan(count),
                    "Every supported plan occurrence must retain its own authored surface slot: " + kind + ".");
                if (substituted || !counts.ContainsKey(kind)) return;
                int slot = ordinal % count;
                Transform wrapper = Array.Find(transforms, item => item.name == "Warehouse " + kind &&
                    Vector3.Distance(item.localPosition, position) < .001f && Quaternion.Angle(item.localRotation, rotation) < .01f);
                Assert.That(wrapper, Is.Not.Null, "The planned surface copy must exist: " + kind + ".");
                Dictionary<string, Mesh> expected;
                if (slot == 0)
                {
                    GameObject original = Resources.Load<GameObject>(WarehouseArenaAssetProvider.ResourceFolder + kind);
                    expected = new Dictionary<string, Mesh>();
                    foreach (MeshFilter filter in original.GetComponentsInChildren<MeshFilter>(true))
                        if (!filter.name.StartsWith("Collision_", StringComparison.Ordinal) && !filter.name.Contains("__WearDecal"))
                            expected.Add(WarehouseSurfaceSourceName(filter.name), filter.sharedMesh);
                }
                else expected = replacements[kind + "/" + slot];
                int checkedSources = 0;
                foreach (MeshFilter filter in wrapper.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!expected.TryGetValue(WarehouseSurfaceSourceName(filter.name), out Mesh mesh)) continue;
                    checkedSources++;
                    Assert.That(filter.sharedMesh, Is.SameAs(mesh),
                        "Each copy must receive its deterministic shared UV0 variant: " + kind + ".");
                }
                Assert.That(checkedSources, Is.EqualTo(expected.Count));
            }
            foreach (WarehouseArenaPlacement part in root.WarehousePlan.ModulePlacements)
            {
                bool gateDeck = part.Kind == "Platform" && Mathf.Abs(part.Position.y - 3.2f) < .01f &&
                    Mathf.Abs(part.Position.z - 10.5f) < .01f && Mathf.Abs(Mathf.Abs(part.Position.x) - 1.5f) < .01f;
                AssertPlacement(part.Kind, part.Position, Quaternion.Euler(0f, part.Yaw, 0f), gateDeck);
            }
            foreach (WarehouseArenaStair stair in root.WarehousePlan.Stairs)
                AssertPlacement("Stair", stair.Foot, Quaternion.identity);
            foreach (WarehouseArenaRailSpan rail in root.WarehousePlan.RailSpans)
                AssertPlacement("Rail", rail.Position, Quaternion.Euler(0f, rail.Yaw, 0f));
            foreach (WarehouseArenaLamp lamp in root.WarehousePlan.Lamps)
                AssertPlacement(lamp.Kind, lamp.Position, lamp.ModelRotation);
        }

        private static string WarehouseSurfaceSourceName(string name)
        {
            int suffix = name.LastIndexOf('.');
            return suffix >= 0 && int.TryParse(name.Substring(suffix + 1), out _) ? name.Substring(0, suffix) : name;
        }

        [Serializable] private sealed class WarehouseSurfaceManifest
        {
            public WarehouseSurfaceVariant[] variants;
            public WarehouseSurfaceCount[] counts;
        }
        [Serializable] private sealed class WarehouseSurfaceVariant
        {
            public string kind;
            public int slot;
            public WarehouseSurfaceMesh[] meshes;
        }
        [Serializable] private sealed class WarehouseSurfaceMesh { public string source, node; }
        [Serializable] private sealed class WarehouseSurfaceCount { public string kind; public int count; }

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
            public long declaredTextureBc7MipBytes;
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
