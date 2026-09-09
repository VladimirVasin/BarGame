using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [Serializable]
        private sealed class ScarfPerfStatistics
        {
            public int sample_count;
            public double mean_ms, p95_ms, max_ms;

            public static ScarfPerfStatistics From(double[] values)
            {
                var result = new ScarfPerfStatistics { sample_count = values.Length };
                foreach (double value in values) result.mean_ms += value;
                result.mean_ms /= values.Length;
                double[] sorted = (double[])values.Clone();
                Array.Sort(sorted);
                result.p95_ms = sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * .95d) - 1)];
                result.max_ms = sorted[sorted.Length - 1];
                return result;
            }
        }

        [Serializable]
        private sealed class ScarfPerfPhase
        {
            public string name;
            public bool paused, preview_expected, preview_stable = true, preview_passive = true;
            public bool native_execution = true;
            public int native_execution_samples;
            public float preview_rotation_start, preview_rotation_end;
            public int maximum_surface_contact_passes;
            public ScarfPerfStatistics frame_interval, whole_scarf, world, tail, wrap_and_knot;
            public ScarfPerfStatistics world_discovery, world_collection, world_spatial_build;
        }

        [Serializable]
        private sealed class ScarfPerfAction
        {
            public string name;
            public double milliseconds;
        }

        [Serializable]
        private sealed class ScarfPerfReport
        {
            public string status = "running", phase = "loading";
            public int frames_per_phase = 60, render_width = 640, render_height = 360;
            public List<ScarfPerfPhase> phases = new List<ScarfPerfPhase>();
            public List<ScarfPerfAction> actions = new List<ScarfPerfAction>();
        }

        [UnityTest]
        [Timeout(180000)]
        [Explicit("Short production scarf/inventory performance reproduction without contact-oracle work. Run alone.")]
        [PrebuildSetup(typeof(ScarfAssetsSetup))]
        public IEnumerator ScarfPerformance()
        {
            var report = new ScarfPerfReport();
            MothersHouseInteriorRoot house = null;
            AlpineVillageRoot village = null;
            Camera camera = null;
            RenderTexture oldTarget = null;
            var target = new RenderTexture(report.render_width, report.render_height, 24);
            float oldCaptureDelta = Time.captureDeltaTime;
            try
            {
                GameSessionState.BeginNewGame();
                Assert.That(GameSessionState.TryStartGameTimeFromWake(), Is.True);
                // Frame intervals use real elapsed time; simulation is not pinned
                // to the fixed capture delta used by the geometry regression.
                Time.captureDeltaTime = 0f;
                WriteScarfPerfReport(report);
                yield return LoadScarfScene<MothersHouseInteriorRoot>(SceneIds.MothersHouseInterior,
                    value => value.IsInitialized, value => house = value);
                camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                oldTarget = camera.targetTexture;
                target.Create();
                camera.targetTexture = target;
                house.CameraFollow.enabled = false;
                house.FixedCamera.enabled = false;
                house.Player.Motor.enabled = false;
                var pickup = house.ScarfPickup;
                Assert.That(pickup, Is.Not.Null);
                Vector3 standing = pickup.transform.position + Vector3.right * 1.1f;
                standing.y = house.Layout.UpperFloor.FloorElevation + PlayerFactory.GroundedRootOffset;
                house.Player.Motor.Teleport(standing);
                house.Player.GameObject.transform.rotation = Quaternion.LookRotation(Vector3.left);
                ((Player3DCharacterPresentation)house.Player.Visual).SetMotion(PlayerMotionSample.Stationary);
                Physics.SyncTransforms();
                AimScarfHero(camera, house.Player, new Vector3(.9f, 1.50f, -1.4f));
                yield return ScarfFrames(2);
                Assert.That(pickup.CanInteract(house.Player.Interactor), Is.True);
                pickup.Interact(house.Player.Interactor);
                Assert.That(GameSessionState.HasInventoryItem(InventoryItemId.Scarf), Is.True);
                var scarf = house.Player.GameObject.GetComponent<PlayerScarfController>().Presentation;
                InventoryController inventory = house.Inventory;
                InventoryItemPreviewRenderer preview = inventory.View.PreviewRenderer;

                yield return MeasureScarfPerfPhase("house-unequipped", camera, scarf, preview, report, false);
                MeasureScarfPerfAction(report, "open-inventory", inventory.Open);
                yield return MeasureScarfPerfPhase("inventory-keys-control", camera, scarf, preview, report, true);
                MeasureScarfPerfAction(report, "select-scarf", () => inventory.SelectItem(ScarfInventoryIndex()));
                Assert.That(preview.CurrentItemId, Is.EqualTo(InventoryItemId.Scarf));
                Transform originalModel = preview.ModelRoot;
                Texture originalTexture = preview.Texture;
                Camera originalCamera = preview.PreviewCamera;
                yield return MeasureScarfPerfPhase("inventory-unequipped", camera, scarf, preview, report, true);

                MeasureScarfPerfAction(report, "equip-in-inventory", inventory.UseSelected);
                Assert.That(GameSessionState.IsInventoryItemEquipped(InventoryItemId.Scarf), Is.True);
                yield return MeasureScarfPerfPhase("inventory-equipped", camera, scarf, preview, report, true);
                Assert.That(preview.ModelRoot, Is.SameAs(originalModel));
                Assert.That(preview.Texture, Is.SameAs(originalTexture));
                Assert.That(preview.PreviewCamera, Is.SameAs(originalCamera));
                MeasureScarfPerfAction(report, "close-equipped-inventory", inventory.Close);
                Assert.That(preview.IsRendering, Is.False);
                yield return MeasureScarfPerfPhase("house-equipped", camera, scarf, preview, report, false);

                MeasureScarfPerfAction(report, "reopen-equipped-inventory", inventory.Open);
                if (inventory.SelectedItemIndex != ScarfInventoryIndex())
                    MeasureScarfPerfAction(report, "reselect-scarf", () => inventory.SelectItem(ScarfInventoryIndex()));
                yield return MeasureScarfPerfPhase("inventory-reopened-equipped", camera, scarf, preview, report, true);
                MeasureScarfPerfAction(report, "unequip-in-inventory", inventory.UseSelected);
                MeasureScarfPerfAction(report, "close-unequipped-inventory", inventory.Close);
                yield return MeasureScarfPerfPhase("house-after-unequip", camera, scarf, preview, report, false);

                report.phase = "loading-village";
                WriteScarfPerfReport(report);
                yield return LoadScarfScene<AlpineVillageRoot>(SceneIds.AlpineVillage,
                    value => value.IsInitialized, value => village = value);
                camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                oldTarget = camera.targetTexture;
                camera.targetTexture = target;
                village.CameraFollow.enabled = false;
                village.Player.Motor.enabled = false;
                AlpineVillageLaneSample lane = village.Plan.Lane.Sample(10f);
                village.Player.Motor.Teleport(lane.Position + Vector3.up * PlayerFactory.GroundedRootOffset);
                village.Player.GameObject.transform.rotation = Quaternion.LookRotation(lane.Forward);
                ((Player3DCharacterPresentation)village.Player.Visual).SetMotion(PlayerMotionSample.Stationary);
                Physics.SyncTransforms();
                AimScarfHero(camera, village.Player, new Vector3(.9f, 1.50f, -1.4f));
                scarf = village.Player.GameObject.GetComponent<PlayerScarfController>().Presentation;
                preview = village.Inventory.View.PreviewRenderer;
                yield return MeasureScarfPerfPhase("village-unequipped", camera, scarf, preview, report, false);
                MeasureScarfPerfAction(report, "equip-outdoors", () =>
                    GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.Scarf, true));
                yield return MeasureScarfPerfPhase("village-equipped", camera, scarf, preview, report, false);

                // Keep all phase measurements even when a final lifecycle check
                // exposes the reproduction. The budget covers only scarf work;
                // whole-frame timing includes this machine's rendering costs.
                foreach (ScarfPerfPhase phase in report.phases)
                {
                    Assert.That(phase.preview_stable && phase.preview_passive, Is.True, phase.name);
                    Assert.That(phase.whole_scarf.p95_ms, Is.LessThan(16d),
                        phase.name + " exceeded the 16 ms whole-scarf p95 budget.");
                    if (phase.native_execution_samples > 0)
                        Assert.That(phase.native_execution, Is.True, phase.name + " used a managed contact solver.");
                    if (phase.paused)
                        Assert.That(phase.whole_scarf.max_ms, Is.EqualTo(0d), phase.name + " recomputed paused scarf geometry.");
                    if (phase.preview_expected)
                        Assert.That(Mathf.Abs(Mathf.DeltaAngle(phase.preview_rotation_start,
                            phase.preview_rotation_end)), Is.GreaterThan(.01f), phase.name + " preview must rotate on unscaled time.");
                }
                foreach (ScarfPerfAction action in report.actions)
                    if (action.name == "equip-in-inventory" || action.name == "equip-outdoors")
                        Assert.That(action.milliseconds, Is.LessThan(100d),
                            action.name + " exceeded the 100 ms synchronous equip budget.");
                report.status = "passed";
                report.phase = "complete";
            }
            finally
            {
                if (report.status != "passed") report.status = "failed";
                WriteScarfPerfReport(report);
                if (house != null)
                {
                    house.Inventory.Close();
                    house.Player.Motor.enabled = true;
                    house.CameraFollow.enabled = true;
                    house.FixedCamera.enabled = true;
                }
                if (village != null)
                {
                    village.Player.Motor.enabled = true;
                    village.CameraFollow.enabled = true;
                }
                if (camera != null) camera.targetTexture = oldTarget;
                Time.captureDeltaTime = oldCaptureDelta;
                target.Release();
                Object.DestroyImmediate(target);
                GameSessionState.BeginNewGame();
            }
        }

        private static IEnumerator MeasureScarfPerfPhase(string name, Camera camera,
            PlayerScarfPresentation scarf, InventoryItemPreviewRenderer preview,
            ScarfPerfReport report, bool previewExpected)
        {
            report.phase = name;
            WriteScarfPerfReport(report);
            Debug.Log("SCARF_PERFORMANCE_PHASE begin " + name);
            int count = report.frames_per_phase;
            var frameTimes = new double[count];
            var geometryTimes = new double[count];
            var worldTimes = new double[count];
            var discoveryTimes = new double[count];
            var collectionTimes = new double[count];
            var spatialBuildTimes = new double[count];
            var tailTimes = new double[count];
            var surfaceTimes = new double[count];
            var phase = new ScarfPerfPhase
            {
                name = name, paused = GameTimeScaleRuntime.IsPaused, preview_expected = previewExpected,
                preview_rotation_start = preview.RotationDegrees
            };
            Transform model = preview.ModelRoot;
            Texture texture = preview.Texture;
            Camera previewCamera = preview.PreviewCamera;
            if (previewExpected)
                phase.preview_passive = model != null &&
                    model.GetComponentInChildren<PlayerScarfController>(true) == null &&
                    model.GetComponentInChildren<PlayerScarfPresentation>(true) == null;
            int sampled = 0;
            long previous = 0;
            Exception failure = null;
            void OnRendered(ScriptableRenderContext context, Camera rendered)
            {
                if (rendered != camera || sampled >= count || failure != null) return;
                try
                {
                    long now = Stopwatch.GetTimestamp();
                    if (previous == 0) { previous = now; return; }
                    frameTimes[sampled] = (now - previous) * 1000d / Stopwatch.Frequency;
                    previous = now;
                    double geometry = scarf.LastGeometryMilliseconds;
                    geometryTimes[sampled] = geometry;
                    // Inner scopes retain their previous value when skipped;
                    // only a fresh whole-geometry scope makes them a sample.
                    worldTimes[sampled] = geometry > 0d ? scarf.CollisionWorld.LastUpdateMilliseconds : 0d;
                    discoveryTimes[sampled] = geometry > 0d ? scarf.CollisionWorld.LastDiscoveryMilliseconds : 0d;
                    collectionTimes[sampled] = geometry > 0d ? scarf.CollisionWorld.LastCollectionMilliseconds : 0d;
                    spatialBuildTimes[sampled] = geometry > 0d ? scarf.CollisionWorld.LastSpatialBuildMilliseconds : 0d;
                    tailTimes[sampled] = geometry > 0d ? scarf.TailSimulation.LastStepMilliseconds : 0d;
                    surfaceTimes[sampled] = geometry > 0d ? scarf.LastSurfaceMilliseconds : 0d;
                    if (geometry > 0d)
                    {
                        phase.native_execution_samples++;
                        phase.native_execution &= PlayerScarfContactSolver.LastExecutionWasNative;
                    }
                    phase.maximum_surface_contact_passes = Math.Max(phase.maximum_surface_contact_passes,
                        scarf.LastSurfaceContactPassCount);
                    phase.preview_stable &= preview.ModelRoot == model && preview.Texture == texture &&
                        preview.PreviewCamera == previewCamera && preview.IsRendering == previewExpected;
                    phase.preview_rotation_end = preview.RotationDegrees;
                    sampled++;
                }
                catch (Exception error) { failure = error; }
            }
            RenderPipelineManager.endCameraRendering += OnRendered;
            try
            {
                double deadline = Time.realtimeSinceStartupAsDouble + 120d;
                while (sampled < count && failure == null && Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;
                if (failure != null) throw failure;
                Assert.That(sampled, Is.EqualTo(count), name + " did not render the requested measurement frames.");
            }
            finally { RenderPipelineManager.endCameraRendering -= OnRendered; }
            phase.frame_interval = ScarfPerfStatistics.From(frameTimes);
            phase.whole_scarf = ScarfPerfStatistics.From(geometryTimes);
            phase.world = ScarfPerfStatistics.From(worldTimes);
            phase.world_discovery = ScarfPerfStatistics.From(discoveryTimes);
            phase.world_collection = ScarfPerfStatistics.From(collectionTimes);
            phase.world_spatial_build = ScarfPerfStatistics.From(spatialBuildTimes);
            phase.tail = ScarfPerfStatistics.From(tailTimes);
            phase.wrap_and_knot = ScarfPerfStatistics.From(surfaceTimes);
            report.phases.Add(phase);
            WriteScarfPerfReport(report);
            Debug.Log("SCARF_PERFORMANCE_PHASE " + JsonUtility.ToJson(phase));
        }

        private static void MeasureScarfPerfAction(ScarfPerfReport report, string name, Func<bool> action)
        {
            long started = Stopwatch.GetTimestamp();
            bool succeeded = action();
            report.actions.Add(new ScarfPerfAction
            {
                name = name, milliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency
            });
            WriteScarfPerfReport(report);
            Assert.That(succeeded, Is.True, name);
        }

        private static void WriteScarfPerfReport(ScarfPerfReport report)
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures",
                Environment.GetEnvironmentVariable("BARPROMENADE_SCARF_CAPTURE") == "optimization"
                    ? "ScarfPerformanceOptimization" : "ScarfPerformance");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "performance-report.json"), JsonUtility.ToJson(report, true));
        }
    }
}
