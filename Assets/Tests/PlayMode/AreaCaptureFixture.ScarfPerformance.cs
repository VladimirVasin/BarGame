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
            public float motion_distance_m, motion_yaw_degrees;
            public int tail_motion_samples;
            public Vector3 tail_rest_tip, tail_authored_back, tail_tip_mean_offset;
            public float tail_trail_mean_m;
            public float tail_flutter_span_m;
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
            public Vector3 idle_tail_offset, settled_tail_offset;
            public float settle_seconds, paused_tail_drift_m;
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
                AimScarfHero(camera, village.Player, ScarfPerfSideBackOffset);
                yield return ScarfRenderedFrame(camera, () =>
                {
                    report.idle_tail_offset = new ScarfPerfTailMotion(scarf).ReadOffset();
                    SaveScarfPerfFrame(camera, "village-idle-equipped");
                });
                foreach (bool turns in new[] { false, true })
                {
                    Assert.That(GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.Scarf, false), Is.True);
                    yield return MeasureScarfPerfRunningPhase(village, camera, scarf, preview, report, turns, false);
                    Assert.That(GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.Scarf, true), Is.True);
                    yield return MeasureScarfPerfRunningPhase(village, camera, scarf, preview, report, turns, true);
                }
                yield return VerifyScarfPerfSettling(village, camera, scarf, preview, report);
                ((Player3DCharacterPresentation)village.Player.Visual).SetMotion(PlayerMotionSample.Stationary);
                AlpineVillageLaneSample resetLane = village.Plan.Lane.Sample(18f);
                village.Player.Motor.Teleport(resetLane.Position + Vector3.up * PlayerFactory.GroundedRootOffset);
                village.Player.GameObject.transform.rotation = Quaternion.LookRotation(-resetLane.Forward);
                AimScarfHero(camera, village.Player, new Vector3(.9f, 1.50f, -1.4f));
                yield return ScarfRenderedFrame(camera, () =>
                {
                    AssertScarfPerfBodyExclusion(scarf, "village-teleport-reset");
                    SaveScarfPerfFrame(camera, "village-teleport-reset");
                });

                // Keep all phase measurements even when a final lifecycle check
                // exposes the reproduction. The budget covers only scarf work;
                // whole-frame timing includes this machine's rendering costs.
                foreach (ScarfPerfPhase phase in report.phases)
                {
                    Assert.That(phase.preview_stable && phase.preview_passive, Is.True, phase.name);
                    Assert.That(phase.whole_scarf.p95_ms, Is.LessThan(2d),
                        phase.name + " exceeded the 2 ms whole-scarf p95 budget.");
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
            ScarfPerfReport report, bool previewExpected, Action<float> advanceFrame = null,
            Action<int> sampleRendered = null)
        {
            report.phase = name;
            WriteScarfPerfReport(report);
            Debug.Log("SCARF_PERFORMANCE_PHASE begin " + name);
            Assert.That(scarf.CollisionWorld, Is.Null, name + " must not collect external model geometry.");
            Assert.That(scarf.BodyContacts, Is.Not.Null, name + " must keep hero body contact.");
            Assert.That(scarf.BodyContacts.Count, Is.GreaterThan(0), name + " needs hero body contact shapes.");
            // Let a changed gait and a repositioned root settle before timing.
            // Movement continues through the same callback during measurement.
            if (advanceFrame != null)
                for (int frame = 0; frame < 12; frame++)
                {
                    advanceFrame(Time.deltaTime);
                    yield return null;
                }
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
                    PlayerScarfCollisionWorld world = scarf.CollisionWorld;
                    worldTimes[sampled] = geometry > 0d && world != null ? world.LastUpdateMilliseconds : 0d;
                    discoveryTimes[sampled] = geometry > 0d && world != null ? world.LastDiscoveryMilliseconds : 0d;
                    collectionTimes[sampled] = geometry > 0d && world != null ? world.LastCollectionMilliseconds : 0d;
                    spatialBuildTimes[sampled] = geometry > 0d && world != null ? world.LastSpatialBuildMilliseconds : 0d;
                    tailTimes[sampled] = geometry > 0d && scarf.TailSimulation != null
                        ? scarf.TailSimulation.LastStepMilliseconds : 0d;
                    surfaceTimes[sampled] = geometry > 0d ? scarf.LastSurfaceMilliseconds : 0d;
                    if (geometry > 0d && world != null &&
                        (scarf.LastSurfaceContactPassCount > 0 || (scarf.TailSimulation?.LastContactPassCount ?? 0) > 0))
                    {
                        phase.native_execution_samples++;
                        phase.native_execution &= PlayerScarfContactSolver.LastExecutionWasNative;
                    }
                    phase.maximum_surface_contact_passes = Math.Max(phase.maximum_surface_contact_passes,
                        scarf.LastSurfaceContactPassCount);
                    phase.preview_stable &= preview.ModelRoot == model && preview.Texture == texture &&
                        preview.PreviewCamera == previewCamera && preview.IsRendering == previewExpected;
                    phase.preview_rotation_end = preview.RotationDegrees;
                    sampleRendered?.Invoke(sampled);
                    sampled++;
                }
                catch (Exception error) { failure = error; }
            }
            RenderPipelineManager.endCameraRendering += OnRendered;
            try
            {
                double deadline = Time.realtimeSinceStartupAsDouble + 120d;
                while (sampled < count && failure == null && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    advanceFrame?.Invoke(Time.deltaTime);
                    yield return null;
                }
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

        private static IEnumerator MeasureScarfPerfRunningPhase(AlpineVillageRoot village, Camera camera,
            PlayerScarfPresentation scarf, InventoryItemPreviewRenderer preview, ScarfPerfReport report,
            bool turns, bool equipped)
        {
            string name = "village-running" + (turns ? "-turns" : "") + (equipped ? "-equipped" : "-unequipped");
            var hero = (Player3DCharacterPresentation)village.Player.Visual;
            Transform root = village.Player.GameObject.transform;
            AlpineVillageLaneSample start = village.Plan.Lane.Sample(10f);
            village.Player.Motor.Teleport(start.Position + Vector3.up * PlayerFactory.GroundedRootOffset);
            root.rotation = Quaternion.LookRotation(start.Forward);
            float along = 10f, travelled = 0f, yaw = 0f;
            ScarfPerfTailMotion tailMotion = equipped ? new ScarfPerfTailMotion(scarf) : null;
            void Advance(float delta)
            {
                delta = Mathf.Clamp(delta, .001f, .1f);
                // A shallow weave makes repeated left/right running turns while
                // keeping the hero on the lane and close to the 4.2 m/s run cap.
                float slope = turns ? .65f * 1.4f * Mathf.Cos((along - 10f) * 1.4f) : 0f;
                along += Player3DCharacterPresentation.FullRunSpeed * delta / Mathf.Sqrt(1f + slope * slope);
                AlpineVillageLaneSample lane = village.Plan.Lane.Sample(along);
                Vector3 position = lane.Position + Vector3.up * PlayerFactory.GroundedRootOffset;
                if (turns) position += lane.Right * (.65f * Mathf.Sin((along - 10f) * 1.4f));
                Vector3 velocity = (position - root.position) / delta;
                velocity.y = 0f;
                Quaternion rotation = velocity.sqrMagnitude > .0001f ? Quaternion.LookRotation(velocity) : root.rotation;
                travelled += Vector3.Distance(root.position, position);
                float turn = Mathf.DeltaAngle(root.eulerAngles.y, rotation.eulerAngles.y);
                yaw += Mathf.Abs(turn);
                root.SetPositionAndRotation(position, rotation);
                hero.SetMotion(new PlayerMotionSample(velocity, velocity.magnitude, turn / 5f, 1f));
                Physics.SyncTransforms();
                AimScarfHero(camera, village.Player, ScarfPerfSideBackOffset);
            }
            void SampleRendered(int frame)
            {
                // Five late rendered samples see actual mesh deformation after
                // the speed response settles, without baking a second skin.
                if (tailMotion != null && frame >= report.frames_per_phase / 2 && frame % 6 == 0)
                    tailMotion.Sample();
            }
            yield return MeasureScarfPerfPhase(name, camera, scarf, preview, report, false, Advance, SampleRendered);
            Assert.That(hero.CurrentLocomotionState, Is.EqualTo(Player3DLocomotionState.Run), name);
            Assert.That(travelled, Is.GreaterThan(1f), name + " must move the root through the world.");
            if (turns) Assert.That(yaw, Is.GreaterThan(45f), name + " must exercise actual root turns.");
            ScarfPerfPhase phase = report.phases[report.phases.Count - 1];
            phase.motion_distance_m = travelled;
            phase.motion_yaw_degrees = yaw;
            if (tailMotion != null)
            {
                phase.tail_motion_samples = tailMotion.Samples.Count;
                phase.tail_rest_tip = tailMotion.RestTip;
                phase.tail_authored_back = tailMotion.AuthoredBack;
                phase.tail_tip_mean_offset = tailMotion.MeanOffset;
                phase.tail_trail_mean_m = Vector3.Dot(phase.tail_tip_mean_offset, tailMotion.AuthoredBack);
                phase.tail_flutter_span_m = tailMotion.MotionSpan;
            }
            WriteScarfPerfReport(report);
            // Readback and PNG encoding are deliberately outside the timed loop.
            // Save first so a failed shape assertion still leaves visual proof.
            if (equipped)
            {
                Advance(Time.deltaTime);
                yield return ScarfRenderedFrame(camera, () =>
                {
                    SaveScarfPerfFrame(camera, name);
                    AssertScarfPerfBodyExclusion(scarf, name);
                });
            }
            if (tailMotion != null)
            {
                Assert.That(phase.tail_motion_samples, Is.GreaterThanOrEqualTo(4), name);
                Assert.That(phase.tail_tip_mean_offset.y, Is.GreaterThan(.12f),
                    name + " must visibly lift the rendered tail above its resting position.");
                Assert.That(phase.tail_trail_mean_m, Is.GreaterThan(.15f),
                    name + " must trail away from the hero along the normalized authored back direction.");
                Assert.That(phase.tail_flutter_span_m, Is.GreaterThan(.004f),
                    name + " must keep moving after lifting, rather than hold one rigid running pose.");
            }
        }

        private sealed class ScarfPerfTailMotion
        {
            private readonly PlayerScarfPresentation scarf;
            private readonly List<Vector3> rendered = new List<Vector3>();
            private readonly List<int> tipIndices = new List<int>();
            private readonly Vector3 restTip;
            public Vector3 RestTip => restTip;
            public Vector3 AuthoredBack { get; }
            public readonly List<Vector3> Samples = new List<Vector3>();

            public ScarfPerfTailMotion(PlayerScarfPresentation value)
            {
                scarf = value;
                Vector3[] rest = scarf.TailSimulation.RestVertices;
                float minimumY = float.PositiveInfinity;
                float averageZ = 0f;
                foreach (Vector3 vertex in rest)
                {
                    minimumY = Mathf.Min(minimumY, vertex.y);
                    averageZ += vertex.z;
                }
                averageZ /= rest.Length;
                Assert.That(Mathf.Abs(averageZ), Is.GreaterThan(.01f),
                    "The normalized authored tail must sit behind the hero's origin.");
                // Normalization includes the hero model's 180-degree yaw. Use
                // that mesh's side of the body, not the source FBX's +Z back.
                AuthoredBack = Vector3.forward * Mathf.Sign(averageZ);
                for (int index = 0; index < rest.Length; index++)
                    if (rest[index].y <= minimumY + .004f)
                    {
                        tipIndices.Add(index);
                        restTip += rest[index];
                    }
                Assert.That(tipIndices, Is.Not.Empty, "The authored scarf needs a bottom row.");
                restTip /= tipIndices.Count;
                rendered.Capacity = rest.Length;
            }

            public Vector3 ReadOffset()
            {
                // The production tail has one identity skin bone. Its current
                // mesh vertices are exactly the rendered cloth-frame positions.
                scarf.TailRenderer.sharedMesh.GetVertices(rendered);
                Vector3 tip = Vector3.zero;
                foreach (int index in tipIndices) tip += rendered[index];
                return tip / tipIndices.Count - restTip;
            }

            public void Sample() => Samples.Add(ReadOffset());
            public Vector3 MeanOffset
            {
                get
                {
                    Vector3 total = Vector3.zero;
                    foreach (Vector3 sample in Samples) total += sample;
                    return Samples.Count > 0 ? total / Samples.Count : Vector3.zero;
                }
            }
            public float MotionSpan
            {
                get
                {
                    float span = 0f;
                    for (int first = 0; first < Samples.Count; first++)
                        for (int second = first + 1; second < Samples.Count; second++)
                            span = Mathf.Max(span, Vector3.Distance(Samples[first], Samples[second]));
                    return span;
                }
            }
        }

        private static IEnumerator VerifyScarfPerfSettling(AlpineVillageRoot village, Camera camera,
            PlayerScarfPresentation scarf, InventoryItemPreviewRenderer preview, ScarfPerfReport report)
        {
            var input = village.gameObject.AddComponent<ScarfCollisionWindInput>();
            input.Scarf = scarf;
            input.Wind = new WindSample(0f, 0f);
            var motion = new ScarfPerfTailMotion(scarf);
            ((Player3DCharacterPresentation)village.Player.Visual).SetMotion(PlayerMotionSample.Stationary);
            report.phase = "village-settling-after-run";
            WriteScarfPerfReport(report);
            try
            {
                float elapsed = 0f;
                while (elapsed < 1.1f)
                {
                    yield return null;
                    elapsed += Mathf.Min(Time.deltaTime, .05f);
                }
                report.settle_seconds = elapsed;
                yield return ScarfRenderedFrame(camera, () =>
                {
                    report.settled_tail_offset = motion.ReadOffset();
                    SaveScarfPerfFrame(camera, "village-settled-after-run");
                    Assert.That(Mathf.Abs(report.settled_tail_offset.y), Is.LessThan(.06f),
                        "The scarf must lower again within about one second of stopping in calm air.");
                    AssertScarfPerfBodyExclusion(scarf, "village-settled-after-run");
                });
                yield return MeasureScarfPerfPhase("village-stopped-calm", camera, scarf, preview, report, false);
                Assert.That(village.Inventory.Open(), Is.True);
                yield return ScarfFrames(2);
                Vector3[] paused = scarf.TailRenderer.sharedMesh.vertices;
                yield return ScarfFrames(8);
                Vector3[] stillPaused = scarf.TailRenderer.sharedMesh.vertices;
                for (int index = 0; index < paused.Length; index++)
                    report.paused_tail_drift_m = Mathf.Max(report.paused_tail_drift_m,
                        Vector3.Distance(paused[index], stillPaused[index]));
                Assert.That(report.paused_tail_drift_m, Is.LessThan(.000001f),
                    "Pausing after the run must freeze the rendered scarf mesh.");
                Assert.That(scarf.LastGeometryMilliseconds, Is.Zero);
                WriteScarfPerfReport(report);
            }
            finally
            {
                village.Inventory.Close();
                Object.DestroyImmediate(input);
            }
        }

        private static readonly Vector3 ScarfPerfSideBackOffset = new Vector3(1.25f, 1.48f, -.9f);

        private static void AssertScarfPerfBodyExclusion(PlayerScarfPresentation scarf, string phase)
        {
            Assert.That(scarf.CollisionWorld, Is.Null, phase + " must ignore external objects.");
            Vector3[] rest = scarf.TailSimulation.RestVertices;
            Vector3[] world = scarf.TailSimulation.WorldVertices;
            for (int index = 0; index < world.Length; index++)
            {
                // The authored attachment is pinned; only the hanging cloth is
                // free to move out of the hero's bounded contact shapes.
                if ((1.567f - rest[index].y) / PlayerScarfPresentation.TailLength < .035f) continue;
                Assert.That(scarf.BodyContacts.Contains(world[index]), Is.False,
                    phase + " left free tail vertex " + index + " inside the hero body.");
            }
        }

        private static void SaveScarfPerfFrame(Camera camera, string name)
        {
            RenderTexture target = camera.targetTexture;
            Assert.That(target, Is.Not.Null);
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                pixels.Apply();
                Assert.That(IsBlank(pixels), Is.False, "Scarf performance capture must show the running hero.");
                Directory.CreateDirectory(ScarfPerfFolder);
                File.WriteAllBytes(Path.Combine(ScarfPerfFolder, name + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(pixels);
            }
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
            Directory.CreateDirectory(ScarfPerfFolder);
            File.WriteAllText(Path.Combine(ScarfPerfFolder, "performance-report.json"), JsonUtility.ToJson(report, true));
        }

        private static string ScarfPerfFolder => Path.Combine(Directory.GetCurrentDirectory(), "Captures",
            Environment.GetEnvironmentVariable("BARPROMENADE_SCARF_CAPTURE") == "optimization"
                ? "ScarfPerformanceOptimization" : "ScarfPerformance");
    }
}
