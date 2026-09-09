using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class VillageOutdoorLifeAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            // The errand bank must exist before the resident importer binds it.
            foreach (string name in new[] { "VillageLifePropAssetSetup", "VillageResidentDoorAssetSetup",
                "VillageWorkroomAssetSetup", "VillageErrandAssetSetup", "VillageResidentAssetSetup",
                "VillageWorkroomPlayerActionAssetSetup", "VillageOutdoorPlayerActionAssetSetup" })
            {
                Type setup = Type.GetType("BarPromenade.Editor." + name + ", BarPromenade.Editor", true);
                setup.GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
            }
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [Serializable]
        private sealed class VillageOutdoorReport
        {
            public int unique_logs, npc_log_contact_samples, player_contact_samples, visible_hand_samples;
            public int body_collision_samples, completed_player_actions, delivered_baskets, cleared_stages;
            public int snow_vertices_changed, completed_water_visits;
            public int station_partner_contact_samples, station_partner_visible_hand_samples;
            public int bucket_fill_contact_samples;
            public float maximum_root_step_metres, maximum_prop_step_metres, maximum_grip_error_metres;
            public float maximum_station_partner_grip_error_metres;
            public float maximum_bucket_fill_grip_error_metres;
            public float maximum_visible_hand_error_metres, maximum_body_overlap_metres;
            public float snow_depth_before, snow_depth_after;
            public bool cancelled_pickup_not_committed, carry_walked, shovel_returned;
            public bool lid_help_completed, gate_help_completed, reload_restored_results, new_game_reset;
            public string[] captures;
        }

        private sealed class VillageOutdoorObservation : IDisposable
        {
            internal readonly Mesh Scratch = new Mesh();
            internal readonly HashSet<string> Captures = new HashSet<string>();
            internal readonly Dictionary<Transform, Vector3> PreviousProps = new Dictionary<Transform, Vector3>();
            internal readonly Collider[] Nearby = new Collider[128];
            public void Dispose() => Object.DestroyImmediate(Scratch);
        }

        [UnityTest]
        [Timeout(600000)]
        [Explicit("One finite outdoor household journey: physical hero help, NPC errands and scene-persistent results. Run alone.")]
        [PrebuildSetup(typeof(VillageOutdoorLifeAssetsSetup))]
        public IEnumerator VillageOutdoorLife()
        {
            var report = new VillageOutdoorReport();
            using var observation = new VillageOutdoorObservation();
            float previousDelta = Time.captureDeltaTime;
            AlpineVillageRoot village = null;
            Camera camera = null;
            float cameraFov = 60f;
            try
            {
                GameSessionState.BeginNewGame();
                GameSessionState.TryStartGameTimeFromWake();
                Time.captureDeltaTime = 1f / 60f;
                yield return LoadOutdoorVillage(root => village = root);
                camera = Camera.main; Assert.That(camera, Is.Not.Null); cameraFov = camera.fieldOfView;
                var life = village.Life;
                var help = village.OutdoorHelp;
                float[] initialSnowDepths = life.Clearing.Patches.Select(place =>
                    village.World.SnowTreading.SampleVisibleDepth(place.Center)).ToArray();
                Debug.Log("Outdoor initial snow depths: " + string.Join(", ", life.Clearing.Patches.Select((place, index) => place.StableId + "=" + initialSnowDepths[index].ToString("F4"))));
                if (initialSnowDepths[1] <= .01f)
                {
                    var paths = AlpineVillagePathPlanner.Create(village.Plan);
                    for (float forward = 0f; forward <= 3f; forward += .5f)
                    {
                        var depths = new List<string>();
                        for (float across = -.5f; across <= 2f; across += .5f)
                        {
                            Vector3 point = life.Errands.ChapelPatch.Center + life.Errands.Chapel.Facing * forward + life.Errands.ChapelPatch.Facing * across;
                            depths.Add(across.ToString("F1") + ":" + village.World.SnowTreading.SampleVisibleDepth(point).ToString("F3") + "/" +
                                AlpineVillageSnowDrift.SampleDepth(village.Plan, paths, new Vector2(point.x, point.z)).ToString("F3"));
                        }
                        Debug.Log("Outdoor chapel snow forward+" + forward.ToString("F1") + " [actual/pure] " + string.Join(", ", depths));
                    }
                }
                Assert.That(initialSnowDepths.Min(), Is.GreaterThan(.01f), "Every clearing must begin on actual visible snow.");
                Assert.That(help, Is.Not.Null);
                var shared = village.Player.GameObject.GetComponent<PlayerAnimatedInteractionController>();
                Assert.That(shared, Is.Not.Null);
                AssertOutdoorStock(life, report);
                Assert.That(GameSessionState.VillageHousehold.LooseLogCount, Is.EqualTo(6), "A new visit begins with real loose stock.");
                var hero = (Player3DCharacterPresentation)village.Player.Visual;
                int heroTriangles = VillageLifeTriangleCount(hero.Registry.transform);
                foreach (var neighbour in life.Neighbours) AssertVillageResidentDetail(neighbour.Actor, heroTriangles);

                // The sole setup placement starts outside the tool rack. Every
                // movement after this, including all carried travel, uses Motor.
                PlayerDoorActionPlan shovelPlan = help.GetPlan(VillageOutdoorTask.TakeShovel);
                village.Player.Motor.Teleport(shovelPlan.EntryRootPosition - shovelPlan.EntryFacingDirection * .6f);
                village.Player.GameObject.transform.rotation = shovelPlan.EntryRotation;
                Physics.SyncTransforms();
                var patch = life.Errands.PorchPatch;
                report.snow_depth_before = village.World.SnowTreading.SampleVisibleDepth(patch.Center);
                Assert.That(report.snow_depth_before, Is.GreaterThan(.01f), "The job must begin on actual visible snow.");
                Assert.That(help.TryBegin(VillageOutdoorTask.TakeShovel), Is.True, "Reserve before the neighbour's first outing.");
                yield return WaitOutdoorAction(village, report, observation, camera, "01-take-shovel");
                Assert.That(help.CarriedProp, Is.SameAs(life.Shovel));
                Assert.That(help.HasCarry && !shared.IsActive && hero.HasCarryPose, Is.True,
                    "Finished pickup hands the same shovel to ordinary locomotion.");
                yield return WalkOutdoorLeg(village, help.GetPlan(VillageOutdoorTask.ClearSnow, 0).EntryRootPosition,
                    report, observation, camera, "walk the shovel to the porch");
                for (int stage = 0; stage < 3; stage++)
                {
                    Assert.That(life.Clearing.Stage(patch.StableId), Is.EqualTo(stage));
                    Assert.That(help.TryBegin(VillageOutdoorTask.ClearSnow, 0), Is.True);
                    yield return WaitOutdoorAction(village, report, observation, camera, "02-clear-snow-" + stage);
                    Assert.That(life.Clearing.Stage(patch.StableId), Is.EqualTo(stage + 1), "One physical sweep commits one stage.");
                }
                Assert.That(help.TryBegin(VillageOutdoorTask.ClearSnow, 0), Is.False, "The finite patch cannot be cleared a fourth time.");
                report.cleared_stages = life.Clearing.Stage(patch.StableId);
                report.snow_vertices_changed = life.Clearing.ChangedVertices;
                report.snow_depth_after = village.World.SnowTreading.SampleVisibleDepth(patch.Center);
                Assert.That(report.snow_vertices_changed, Is.GreaterThan(0));
                Assert.That(report.snow_depth_after, Is.LessThan(report.snow_depth_before - .005f),
                    "The real snow surface must be lower, independently of the session counter.");
                Vector3 patchRight = Vector3.Cross(Vector3.up, patch.Facing);
                Vector3 clearedTarget = life.Neighbourhood.Ground(patch.Center) + Vector3.up * .025f;
                OutdoorFrame(camera, observation, "02-porch-cleared-result",
                    clearedTarget + patchRight * 1.3f + patch.Facing * 1f + Vector3.up * 2f, clearedTarget, 50f);
                yield return WalkOutdoorLeg(village, help.GetPlan(VillageOutdoorTask.ReturnShovel).EntryRootPosition,
                    report, observation, camera, "return to the same shovel rack");
                Assert.That(help.TryBegin(VillageOutdoorTask.ReturnShovel), Is.True);
                yield return WaitOutdoorAction(village, report, observation, camera, "03-return-shovel");
                Assert.That(help.HasCarry || hero.HasCarryPose, Is.False);
                Assert.That(Vector3.Distance(life.Shovel.position, life.ShovelRestPosition), Is.LessThan(.01f));
                Assert.That(Quaternion.Angle(life.Shovel.rotation, life.ShovelRestRotation), Is.LessThan(.5f));
                report.shovel_returned = true;

                var quiet = life.Neighbourhood.QuietHouse;
                yield return WalkOutdoorLeg(village, life.Neighbourhood.Yard(quiet, 2.1f, 2.3f), report, observation, camera, "leave the rack");
                yield return WalkOutdoorLeg(village, life.Neighbourhood.Yard(quiet, 0f, 2.3f), report, observation, camera, "cross the open yard");
                yield return WalkOutdoorLeg(village, quiet.DoorDockPosition, report, observation, camera, "house-11 lane connection");
                yield return WalkOutdoorStreet(village, quiet.LaneDistance, life.Plan.House.LaneDistance, report, observation, camera);
                yield return WalkOutdoorLeg(village, life.Plan.House.DoorDockPosition, report, observation, camera, "wood yard entrance");
                yield return WalkOutdoorLeg(village, life.Plan.Yard(0f, 4.5f), report, observation, camera, "outside the working lines");
                yield return WalkOutdoorLeg(village, life.Plan.Yard(1.45f, 4.5f), report, observation, camera, "wait beyond the basket approach");

                int basket = -1;
                yield return WaitOutdoorLife(village, () =>
                {
                    for (int index = 0; index < 2; index++)
                        if (life.CanReserveFirewood(index)) { basket = index; return true; }
                    return false;
                }, 360f, report, observation, camera, "NPC loads three distinct logs into a basket");
                Assert.That(report.npc_log_contact_samples, Is.GreaterThan(0), "Observe real log loading before the hero takes over.");
                Assert.That(GameSessionState.VillageHousehold.GetBasketLogCount(basket), Is.EqualTo(3));
                Assert.That(help.TryBegin(VillageOutdoorTask.TakeBasket, basket), Is.True, "Reserve in the ready interval before the NPC takes the basket.");
                int beforeCancellation = help.CompletedActionCount;
                for (int frame = 0; frame < 300 && shared.Phase != PlayerAnimatedInteractionPhase.Entering; frame++)
                {
                    Vector3 previous = village.Player.GameObject.transform.position;
                    AdvanceOutdoorLife(village, 1f / 60f, report, observation, camera);
                    yield return null;
                    ObserveOutdoorRoot(village, previous, report, "cancelled basket approach");
                    ObserveOutdoorBody(village, report, observation, "cancelled basket approach");
                }
                if (shared.Phase != PlayerAnimatedInteractionPhase.Entering)
                {
                    Vector3 playerPosition = village.Player.GameObject.transform.position;
                    Vector3 context = (playerPosition + life.Woman.transform.position + life.Baskets[basket].position) / 3f;
                    OutdoorFrame(camera, observation, "failure-basket-approach",
                        context + life.Plan.Right * 3f + life.Plan.Forward * 3.4f + Vector3.up * 2f,
                        context + Vector3.up * .8f, 54f);
                    TestContext.Out.WriteLine($"Basket approach failed: phase={shared.Phase}, basket={basket}, " +
                        $"womanStage={life.WomanStage}, woman={life.Woman.transform.position:F4}, blocked={life.IsWomanBlocked}, " +
                        $"hero={playerPosition:F4}, entry={help.ActivePlan.EntryRootPosition:F4}, " +
                        $"facing={help.ActivePlan.EntryFacingDirection:F4}, carried={help.HasCarry}, busy={help.IsBusy}, " +
                        $"motorStalled={village.Player.Motor.InteractionPoseMoveStalled}.");
                }
                Assert.That(shared.Phase, Is.EqualTo(PlayerAnimatedInteractionPhase.Entering));
                help.RefreshContacts();
                Assert.That(help.ActionSeconds, Is.LessThan(VillageOutdoorPlayerActions.PickupContactSeconds));
                Assert.That(help.Cancel(), Is.True);
                Assert.That(help.IsBusy || help.HasCarry || shared.IsActive, Is.False);
                Assert.That(help.CompletedActionCount, Is.EqualTo(beforeCancellation));
                Assert.That(GameSessionState.VillageHousehold.GetBasketDeliveryStand(basket), Is.EqualTo(-1));
                Assert.That(Vector3.Distance(life.Baskets[basket].position, life.Plan.Pickups[basket] + Vector3.up * AlpineVillageLifePlan.StandHeight), Is.LessThan(.01f));
                report.cancelled_pickup_not_committed = true;
                Assert.That(help.TryBegin(VillageOutdoorTask.TakeBasket, basket), Is.True);
                yield return WaitOutdoorAction(village, report, observation, camera, "06-take-firewood");
                Transform carriedBasket = life.Baskets[basket];
                Assert.That(help.CarriedProp, Is.SameAs(carriedBasket));
                Assert.That(!shared.IsActive && hero.HasCarryPose, Is.True);
                Vector3 carryStart = village.Player.GameObject.transform.position;
                int destination = GameSessionState.VillageHousehold.GetBasketAtStand(1) < 0 ? 1 : 0;
                yield return WalkOutdoorLeg(village, life.Plan.Yard(1.45f + basket * 1.05f, 3.5f), report, observation, camera, "lift away from the pickup support");
                yield return WalkOutdoorLeg(village, life.Plan.Yard(-1.5f - destination * 1.05f, 3.5f), report, observation, camera, "carry across the real yard");
                Assert.That(Vector3.Distance(carryStart, village.Player.GameObject.transform.position), Is.GreaterThan(2f));
                Assert.That(help.CarriedProp, Is.SameAs(carriedBasket));
                report.carry_walked = true;
                yield return WalkOutdoorLeg(village, help.GetPlan(VillageOutdoorTask.PutBasket, destination).EntryRootPosition, report, observation, camera, "empty delivery support");
                Assert.That(help.TryBegin(VillageOutdoorTask.PutBasket, destination), Is.True);
                yield return WaitOutdoorAction(village, report, observation, camera, "08-place-firewood");
                Assert.That(GameSessionState.VillageHousehold.GetBasketDeliveryStand(basket), Is.EqualTo(destination));
                Assert.That(help.HasCarry || hero.HasCarryPose, Is.False);
                Assert.That(Vector3.Distance(carriedBasket.position, life.Plan.Deliveries[destination] + Vector3.up * AlpineVillageLifePlan.StandHeight), Is.LessThan(.01f));
                AssertOutdoorStock(life, report);

                yield return WalkOutdoorLeg(village, life.Plan.Yard(-1.5f - destination * 1.05f, 3.5f), report, observation, camera, "leave the delivered basket");
                yield return WalkOutdoorLeg(village, life.Plan.Yard(0f, 4.5f), report, observation, camera, "wood yard exit");
                yield return WalkOutdoorLeg(village, life.Plan.House.DoorDockPosition, report, observation, camera, "wood lane connection");
                yield return WalkOutdoorStreet(village, life.Plan.House.LaneDistance, quiet.LaneDistance, report, observation, camera);
                yield return WalkOutdoorLeg(village, quiet.DoorDockPosition, report, observation, camera, "return to house-11");
                yield return WalkOutdoorLeg(village, life.Neighbourhood.Yard(quiet, 1f, 3.3f), report, observation, camera, "gate helper waiting space");
                // Reserve only for a visitor who is actually about to pass.
                // Basket loading was tested first so a slow gate queue cannot
                // consume both ready baskets before the hero reaches house 04.
                yield return WaitOutdoorLife(village, () => life.CanReserveGateHelp, 480f, report, observation, camera, "visitor ready at the gate");
                int passages = life.GatePassages;
                Assert.That(help.TryBegin(VillageOutdoorTask.HoldGate), Is.True);
                yield return WaitOutdoorAction(village, report, observation, camera, "04-hold-gate", 1800);
                Assert.That(life.GatePassages, Is.GreaterThan(passages), "Completion needs a real basket passage, not the help timeout.");
                Assert.That(life.GateHelpReserved, Is.False);
                report.gate_help_completed = true;
                yield return WalkOutdoorLeg(village, life.GateFrame.TransformPoint(new Vector3(-1.035f, 0f, -.60f)),
                    report, observation, camera, "leave the gate working side");
                yield return WalkOutdoorLeg(village, life.GateFrame.TransformPoint(new Vector3(-1.035f, 0f, .65f)),
                    report, observation, camera, "walk around the fence return");
                yield return WalkOutdoorLeg(village, life.Neighbourhood.Yard(quiet, -1.5f, 4.1f), report, observation, camera, "leave the gate swing");
                yield return WalkOutdoorLeg(village, life.Neighbourhood.Yard(quiet, 0f, 4.1f), report, observation, camera, "gate approach lane");
                yield return WalkOutdoorStreet(village, quiet.LaneDistance, 0f, report, observation, camera);
                yield return WalkOutdoorLeg(village, life.Plan.StationWork + life.Plan.StationForward * 1.4f, report, observation, camera, "station helper approach");
                yield return WaitOutdoorLife(village, () => life.CanReserveStationHelp, 60f, report, observation, camera, "station worker between jobs");
                Assert.That(help.TryBegin(VillageOutdoorTask.HoldLid), Is.True);
                yield return WaitOutdoorAction(village, report, observation, camera, "09-hold-station-lid", 1800);
                Assert.That(life.StationHelpCompleted, Is.True, "The neighbour must actually finish securing the strap.");
                Assert.That(report.station_partner_contact_samples, Is.GreaterThan(30),
                    "Observe the partner's complete fastening contact, not just the hero holding a lid.");
                Assert.That(report.station_partner_visible_hand_samples, Is.GreaterThanOrEqualTo(2),
                    "Both fastening hands must belong to the visible imported worker.");
                report.lid_help_completed = true;
                yield return WalkOutdoorLeg(village, life.Plan.StationWork + life.Plan.StationForward * 1.4f, report, observation, camera, "release the station work dock");

                yield return WaitOutdoorLife(village, () => life.DeliveredBaskets == 2 && GameSessionState.VillageHousehold.ChairFixed &&
                    GameSessionState.VillageHousehold.CompletedWaterVisits > 0 && life.Clearing.IsComplete(VillageHouseholdProgress.ChapelClearingId) &&
                    life.Clearing.IsComplete(VillageHouseholdProgress.StationEdgeClearingId),
                    1500f, report, observation, camera, "remaining finite stock, chair, water returned and both distant clearings");
                AssertOutdoorStock(life, report);
                Assert.That(report.bucket_fill_contact_samples, Is.GreaterThan(30));
                Assert.That(observation.Captures.Contains("10-spring-water-visit") && observation.Captures.Contains("11-chapel-threshold-work"), Is.True);
                Assert.That(Vector3.Distance(life.Bucket.position, life.Errands.BucketSupport + Vector3.up * AlpineVillageLifePlan.StandHeight), Is.LessThan(.025f));
                report.cleared_stages = life.Clearing.Patches.Sum(place => life.Clearing.Stage(place.StableId));
                report.snow_vertices_changed = life.Clearing.ChangedVertices;
                report.completed_player_actions = help.CompletedActionCount;
                Assert.That(report.completed_player_actions, Is.EqualTo(9));
                report.delivered_baskets = life.DeliveredBaskets;
                report.completed_water_visits = GameSessionState.VillageHousehold.CompletedWaterVisits;
                Assert.That(report.player_contact_samples, Is.GreaterThan(50));
                Assert.That(report.visible_hand_samples, Is.GreaterThan(10));
                var snapshot = new VillageOutdoorSnapshot();
                Transform oldBasket = life.Baskets[0];
                AsyncOperation away = SceneManager.LoadSceneAsync(SceneIds.DoorTransition, LoadSceneMode.Single);
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (!away.isDone && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(away.isDone && oldBasket == null, Is.True, "The old village must really unload.");
                snapshot.AssertState();
                yield return LoadOutdoorVillage(root => village = root);
                camera = Camera.main;
                snapshot.AssertState();
                AssertOutdoorStock(village.Life, report);
                for (int index = 0; index < 2; index++)
                {
                    int stand = GameSessionState.VillageHousehold.GetBasketDeliveryStand(index);
                    Assert.That(stand, Is.InRange(0, 1));
                    Assert.That(Vector3.Distance(village.Life.Baskets[index].position,
                        village.Life.Plan.Deliveries[stand] + Vector3.up * AlpineVillageLifePlan.StandHeight), Is.LessThan(.01f));
                }
                Assert.That(village.Workroom.ChairFixed && village.Life.BucketFilled, Is.True);
                Assert.That(village.Life.Bucket.Find("Water").gameObject.activeInHierarchy, Is.True);
                for (int patchIndex = 0; patchIndex < initialSnowDepths.Length; patchIndex++)
                {
                    VillageSnowPatch restored = village.Life.Clearing.Patches[patchIndex];
                    Assert.That(village.Life.Clearing.Stage(restored.StableId), Is.EqualTo(3));
                    Assert.That(village.World.SnowTreading.SampleVisibleDepth(restored.Center),
                        Is.LessThan(initialSnowDepths[patchIndex] - .005f), "Completed clearing must survive; temporary footprints need not.");
                }
                OutdoorFrame(camera, observation, "12-returned-village", village.Life.Plan.Yard(-1f, 7f) + Vector3.up * 1.8f,
                    village.Life.Plan.Yard(-1.6f, 1.5f) + Vector3.up * .7f);
                report.reload_restored_results = true;
                GameSessionState.BeginNewGame();
                Assert.That(GameSessionState.VillageHousehold.DeliveredBasketCount, Is.Zero);
                Assert.That(GameSessionState.VillageHousehold.LooseLogCount, Is.EqualTo(6));
                Assert.That(GameSessionState.VillageHousehold.ChairFixed || GameSessionState.VillageHousehold.WaterFetched, Is.False);
                foreach (VillageSnowPatch place in village.Life.Clearing.Patches)
                    Assert.That(GameSessionState.VillageHousehold.GetClearingStage(place.StableId), Is.Zero);
                report.new_game_reset = true;
                report.captures = observation.Captures.OrderBy(name => name, StringComparer.Ordinal).ToArray();
                string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", "VillageOutdoorLife"));
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "verification.json"), JsonUtility.ToJson(report, true));
            }
            finally
            {
                Time.captureDeltaTime = previousDelta;
                if (village != null)
                {
                    village.OutdoorHelp?.Cancel();
                    village.Player.GameObject.GetComponent<PlayerAnimatedInteractionController>()?.CancelActiveInteraction();
                    village.Player.Motor.CancelInteractionPoseMove(); village.Player.Motor.SetInputEnabled(true);
                    village.Life.enabled = true; village.CameraFollow.enabled = true;
                }
                if (camera != null) camera.fieldOfView = cameraFov;
            }
        }

        private static IEnumerator LoadOutdoorVillage(Action<AlpineVillageRoot> ready)
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(SceneIds.AlpineVillage, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            AlpineVillageRoot village = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                village = Object.FindAnyObjectByType<AlpineVillageRoot>();
                if (load.isDone && village != null && village.IsInitialized) break;
                yield return null;
            }
            Assert.That(village != null && village.IsInitialized, Is.True, "The real AlpineVillage root did not initialize.");
            village.Life.enabled = false;
            village.CameraFollow.enabled = false;
            for (int frame = 0; frame < SettleFrames; frame++) yield return null;
            ready(village);
        }

        private static void AdvanceOutdoorLife(AlpineVillageRoot village, float seconds, VillageOutdoorReport report,
            VillageOutdoorObservation observation, Camera camera)
        {
            // Waiting advances up to .25 s per rendered frame; contact and
            // continuity probes still see every <=20 ms physical substep.
            for (float remaining = seconds; remaining > .00001f;)
            {
                float dt = Mathf.Min(.02f, remaining); remaining -= dt;
                village.Life.Advance(dt, 600d, .25f, village.Life.Plan.Forward);
                ObserveOutdoorProps(village.Life, report, observation);
                ObserveOutdoorStationPartner(village.Life, report, observation);
                int logId = village.Life.LoadingLogId;
                if (logId >= 0 && village.Life.WomanStage == VillageWomanStage.WalkingWithLog)
                {
                    Transform log = village.Life.FirewoodLogs[logId];
                    var woman = village.Life.Woman;
                    Vector3 right = log.TransformPoint(Vector3.right * .18f), left = log.TransformPoint(Vector3.left * .18f);
                    float error = Mathf.Max(Vector3.Distance(left, woman.LeftGrip.position), Vector3.Distance(right, woman.RightGrip.position));
                    report.npc_log_contact_samples++;
                    Assert.That(error, Is.LessThan(.035f), "The NPC must carry the actual unique log with both visible hands.");
                    if (report.npc_log_contact_samples % 40 == 1)
                    {
                        AssertOutdoorVisibleHand(woman.ModelRoot, left, true, report, observation);
                        AssertOutdoorVisibleHand(woman.ModelRoot, right, false, report, observation);
                    }
                    OutdoorFrame(camera, observation, "05-npc-loads-real-stock",
                        woman.transform.position + woman.transform.right * 2.5f - woman.transform.forward * .7f + Vector3.up * 1.4f,
                        log.position, 42f);
                }
                foreach (VillageNeighbourState actor in village.Life.Neighbours)
                {
                    if (actor.Task == VillageNeighbourTask.FillBucket && actor.Actor.CurrentAction == VillageResidentAction.BucketFill)
                    {
                        Transform pail = village.Life.Bucket;
                        Vector3 right = pail.TransformPoint(new Vector3(.29f, .43f, 0f));
                        Vector3 left = pail.TransformPoint(new Vector3(-.29f, .43f, 0f));
                        float error = Mathf.Max(Vector3.Distance(right, actor.Actor.RightGrip.position), Vector3.Distance(left, actor.Actor.LeftGrip.position));
                        report.bucket_fill_contact_samples++;
                        report.maximum_bucket_fill_grip_error_metres = Mathf.Max(report.maximum_bucket_fill_grip_error_metres, error);
                        Assert.That(error, Is.LessThan(.035f), $"The same pail must remain in both hands while filling at {actor.Actor.CurrentActionSeconds:F3}s: {error:F4}m; actor={actor.Actor.transform.position:F4}, dip={village.Life.Errands.BucketDip:F4}.");
                        if (report.bucket_fill_contact_samples % 40 == 1)
                        {
                            AssertOutdoorVisibleHand(actor.Actor.ModelRoot, left, true, report, observation);
                            AssertOutdoorVisibleHand(actor.Actor.ModelRoot, right, false, report, observation);
                        }
                        if (actor.Actor.CurrentActionSeconds > 6.6f)
                        {
                            Assert.That(village.Life.BucketFilled, Is.True);
                            OutdoorFrame(camera, observation, "10-spring-water-visit",
                                actor.Actor.transform.position + actor.Actor.transform.right * 2.7f - actor.Actor.transform.forward * .8f + Vector3.up * 1.5f,
                                pail.position + Vector3.up * .25f, 48f);
                        }
                    }
                    if (actor.Task == VillageNeighbourTask.Shovel &&
                        actor.Actor.CurrentAction == VillageResidentAction.ShovelWork &&
                        Vector3.Distance(actor.Actor.transform.position, village.Life.Errands.ChapelPatch.Dock) < .25f &&
                        actor.Actor.CurrentActionSeconds > 1f)
                        OutdoorFrame(camera, observation, "11-chapel-threshold-work",
                            actor.Actor.transform.position - actor.Actor.transform.right * 2.7f - actor.Actor.transform.forward * .8f + Vector3.up * 1.6f,
                            village.Life.Errands.ChapelPatch.Center + Vector3.up * .5f, 55f);
                }
            }
        }

        private static IEnumerator WaitOutdoorLife(AlpineVillageRoot village, Func<bool> ready, float maximumSeconds,
            VillageOutdoorReport report, VillageOutdoorObservation observation, Camera camera, string label)
        {
            Debug.Log("Outdoor wait: " + label);
            int frames = Mathf.CeilToInt(maximumSeconds / .25f);
            for (int frame = 0; frame < frames && !ready(); frame++)
            {
                // The exact readiness boundary matters for a basket shared by
                // NPC and hero. Stop integration as soon as it becomes available.
                for (int step = 0; step < 15 && !ready(); step++)
                    AdvanceOutdoorLife(village, 1f / 60f, report, observation, camera);
                if (!ready() && (frame == 239 || frame == 719 || frame == 1439))
                    Debug.Log("Outdoor waiting state: " + label + "; hero=" + village.Player.GameObject.transform.position + "; " +
                        string.Join("; ", village.Life.Neighbours.Select(OutdoorNeighbourState)));
                if (!ready()) yield return null;
            }
            Assert.That(ready(), Is.True, label + " stalled. " + string.Join("; ", village.Life.Neighbours.Select(OutdoorNeighbourState)));
            Debug.Log("Outdoor wait complete: " + label);
        }

        private static string OutdoorNeighbourState(VillageNeighbourState n)
        {
            VillageNeighbourStep step = n.Steps.Count > 0 ? n.Steps.Peek() : null;
            Vector3? target = step?.Points != null && n.PointIndex < step.Points.Length ? step.Points[n.PointIndex] : (Vector3?)null;
            return $"{n.Role}/{n.Task} outside={n.IsOutside} blocked={n.IsBlocked} pos={n.Actor.transform.position:F3} " +
                $"outings={n.CompletedOutings} point={n.PointIndex} target={target} yield={n.YieldPoint} to={n.YieldTo?.Role}";
        }

        private static void ObserveOutdoorStationPartner(AlpineVillageLifeController life, VillageOutdoorReport report,
            VillageOutdoorObservation observation)
        {
            VillageResidentPresentation worker = life.StationWorker;
            if (!life.StationPartnerReady || worker.CurrentAction != VillageResidentAction.StationStrap ||
                worker.CurrentActionSeconds < 1.5f || worker.CurrentActionSeconds > 4.5f) return;
            Assert.That(life.StationStrap, Is.Not.Null);
            Transform fastener = life.StationStrap.Find("ANCHOR_Fastener");
            Transform grip = life.StationStrap.Find("ANCHOR_Grip");
            Assert.That(fastener, Is.Not.Null, "The real station strap must expose its fastening point.");
            Assert.That(grip, Is.Not.Null, "The real station strap must expose its lower holding point.");
            float rightError = Vector3.Distance(worker.RightGrip.position, fastener.position);
            float leftError = Vector3.Distance(worker.LeftGrip.position, grip.position);
            float error = Mathf.Max(leftError, rightError);
            report.station_partner_contact_samples++;
            report.maximum_station_partner_grip_error_metres = Mathf.Max(report.maximum_station_partner_grip_error_metres, error);
            Assert.That(error, Is.LessThan(.035f),
                $"The station partner misses the same actual strap at {worker.CurrentActionSeconds:F3}s: " +
                $"right/Fastener={rightError:F4}m, left/Grip={leftError:F4}m, worker={worker.transform.position:F4}, " +
                $"fastener={fastener.position:F4}, grip={grip.position:F4}.");
            if (report.station_partner_contact_samples % 40 != 1) return;
            AssertOutdoorVisibleHand(worker.ModelRoot, fastener.position, false, report, observation);
            AssertOutdoorVisibleHand(worker.ModelRoot, grip.position, true, report, observation);
            report.station_partner_visible_hand_samples += 2;
        }

        private static IEnumerator WalkOutdoorStreet(AlpineVillageRoot village, float from, float to,
            VillageOutdoorReport report, VillageOutdoorObservation observation, Camera camera)
        {
            float sign = Mathf.Sign(to - from);
            for (float distance = from; sign * (to - distance) > .01f; distance += sign * Mathf.Min(1.5f, Mathf.Abs(to - distance)))
            {
                AlpineVillageLaneSample sample = village.Plan.Lane.Sample(distance);
                Vector3 point = village.Life.Neighbourhood.Ground(sample.Position + sample.Right * Mathf.Min(.75f, sample.Width * .25f));
                yield return WalkOutdoorLeg(village, point, report, observation, camera, "ordinary village street");
            }
            yield return WalkOutdoorLeg(village, village.Plan.Lane.Sample(to).Position, report, observation, camera, "street junction");
        }

        private static IEnumerator WalkOutdoorLeg(AlpineVillageRoot village, Vector3 target, VillageOutdoorReport report,
            VillageOutdoorObservation observation, Camera camera, string label)
        {
            village.Player.Motor.SetInputEnabled(false);
            bool arrived = false;
            for (int frame = 0; frame < 750 && !arrived; frame++)
            {
                Vector3 previous = village.Player.GameObject.transform.position;
                ObserveOutdoorBody(village, report, observation, label);
                arrived = village.Player.Motor.MoveTowardsApproachWaypoint(target, .04f, .04f);
                AdvanceOutdoorLife(village, 1f / 60f, report, observation, camera);
                yield return null;
                ObserveOutdoorRoot(village, previous, report, label);
                ObserveOutdoorBody(village, report, observation, label);
                ObserveOutdoorContacts(village, report, observation);
                ObserveOutdoorProps(village.Life, report, observation);
                Assert.That(village.Player.Motor.InteractionPoseMoveStalled, Is.False,
                    $"The production capsule cannot walk {label}: {previous:F4} -> {target:F4}; " +
                    (village.Player.Motor.InteractionPoseMoveStalled ? string.Join("; ",
                        Physics.OverlapSphere(previous + Vector3.up * .7f, 1.1f).Where(c => !c.isTrigger)
                            .Select(c => c.transform.parent?.name + "/" + c.name + " " + c.bounds)) : ""));
                if (village.OutdoorHelp.HasCarry && village.OutdoorHelp.CarriedProp != village.Life.Shovel &&
                    Vector3.Distance(village.Player.GameObject.transform.position, village.OutdoorHelp.ActivePlan.EntryRootPosition) > 1f)
                {
                    Transform actor = village.Player.GameObject.transform;
                    OutdoorFrame(camera, observation, "07-free-firewood-carry", actor.position + actor.right * 2.4f - actor.forward * .8f + Vector3.up * 1.3f,
                        village.OutdoorHelp.CarriedProp.position + Vector3.up * .3f, 43f);
                }
            }
            village.Player.Motor.CancelInteractionPoseMove(); village.Player.Motor.SetInputEnabled(true);
            Assert.That(arrived, Is.True, "The production hero did not reach " + label);
        }

        private static IEnumerator WaitOutdoorAction(AlpineVillageRoot village, VillageOutdoorReport report,
            VillageOutdoorObservation observation, Camera camera, string capture, int maximumFrames = 1000)
        {
            var help = village.OutdoorHelp;
            int completed = help.CompletedActionCount, samples = report.player_contact_samples;
            PlayerDoorActionPlan plan = help.ActivePlan;
            VillageOutdoorTask task = help.ActiveTask;
            Debug.Log("Outdoor action: " + task);
            for (int frame = 0; frame < maximumFrames && help.IsBusy; frame++)
            {
                Vector3 previous = village.Player.GameObject.transform.position;
                AdvanceOutdoorLife(village, 1f / 60f, report, observation, camera);
                yield return null;
                ObserveOutdoorRoot(village, previous, report, task.ToString());
                ObserveOutdoorBody(village, report, observation, task.ToString());
                ObserveOutdoorContacts(village, report, observation);
                ObserveOutdoorProps(village.Life, report, observation);
                bool shovelWorking = task == VillageOutdoorTask.ClearSnow;
                bool captureReady = !shovelWorking || help.CurrentAction == VillageOutdoorPlayerAction.ShovelWork &&
                    help.ActionSeconds >= .8f && help.ActionSeconds <= 1.6f;
                bool lidWorking = task == VillageOutdoorTask.HoldLid;
                if (lidWorking) captureReady = village.Life.StationPartnerReady &&
                    village.Life.StationWorker.CurrentAction == VillageResidentAction.StationStrap &&
                    village.Life.StationWorker.CurrentActionSeconds >= 2f && village.Life.StationWorker.CurrentActionSeconds <= 4f;
                if (help.ContactWeight >= .9999f && captureReady)
                {
                    Transform actor = village.Player.GameObject.transform;
                    Vector3 target = shovelWorking ? (help.RightTarget + village.Life.Shovel.position) * .5f :
                        help.RightContactWeight >= .9999f ? help.RightTarget : help.LeftTarget;
                    Vector3 position = shovelWorking ? actor.position + actor.right * 2.1f - actor.forward * .7f + Vector3.up * 1.4f :
                        lidWorking ? actor.position - actor.right * 2.6f - actor.forward * .4f + Vector3.up * 1.65f :
                        actor.position + actor.right * 2f - actor.forward * .6f + Vector3.up * 1.35f;
                    OutdoorFrame(camera, observation, capture, position,
                        target, 46f);
                }
            }
            Assert.That(help.IsBusy, Is.False, "Outdoor action stalled: " + task);
            Assert.That(help.CompletedActionCount, Is.EqualTo(completed + 1),
                $"Cancellation is not a completed action: {task}, root={village.Player.GameObject.transform.position:F4}, entry={plan.EntryRootPosition:F4}.");
            Assert.That(report.player_contact_samples, Is.GreaterThan(samples), "The action never made a complete physical contact: " + task);
            Assert.That(Vector3.Distance(village.Player.GameObject.transform.position, plan.ExitRootPosition), Is.LessThan(.08f),
                "The shared action must release at its independent exit endpoint: " + task);
            Assert.That(village.Player.Motor.InputEnabled, Is.True);
            Debug.Log("Outdoor action complete: " + task);
        }

        private static void ObserveOutdoorContacts(AlpineVillageRoot village, VillageOutdoorReport report,
            VillageOutdoorObservation observation)
        {
            var help = village.OutdoorHelp;
            help.RefreshContacts(); // Same production late seam after this frame's graph sampling.
            if (!help.IsBusy && !help.HasCarry) return;
            Transform prop;
            bool gate = help.CurrentAction == VillageOutdoorPlayerAction.GateEnter || help.CurrentAction == VillageOutdoorPlayerAction.GateHold ||
                help.CurrentAction == VillageOutdoorPlayerAction.GateExit;
            bool lid = help.CurrentAction == VillageOutdoorPlayerAction.LidEnter || help.CurrentAction == VillageOutdoorPlayerAction.LidHold ||
                help.CurrentAction == VillageOutdoorPlayerAction.LidExit;
            bool shovel = help.CurrentAction == VillageOutdoorPlayerAction.ShovelPickup || help.CurrentAction == VillageOutdoorPlayerAction.ShovelCarry ||
                help.CurrentAction == VillageOutdoorPlayerAction.ShovelWork || help.CurrentAction == VillageOutdoorPlayerAction.ShovelPutBack;
            if (gate) prop = village.Life.Gate;
            else if (lid) prop = village.Life.StationLid;
            else if (shovel) prop = village.Life.Shovel;
            else prop = help.CarriedProp != null ? help.CarriedProp : village.Life.Baskets.OrderBy(basket =>
                Vector3.Distance(basket.position, help.ActivePlan.InteractionPosition)).First();
            Check(true, help.LeftContactWeight, help.LeftGrip, help.LeftTarget);
            Check(false, help.RightContactWeight, help.RightGrip, help.RightTarget);

            void Check(bool left, float weight, Transform actualHand, Vector3 target)
            {
                if (weight < .9999f) return;
                Assert.That(actualHand, Is.Not.Null);
                // The hero faces the crate: its authored local X is opposite
                // his anatomical left/right. Check the physical opposite grip.
                string anchor = gate && !left ? "Handle" : (lid ? !left : left) ? "LeftGrip" : "RightGrip";
                Transform physical = prop.Find("ANCHOR_" + anchor);
                Assert.That(physical, Is.Not.Null, "Missing physical grip on " + prop.name);
                Assert.That(Vector3.Distance(physical.position, target), Is.LessThan(.005f),
                    $"The sampled target misses the actual prop: {help.CurrentAction}/{(left ? "left" : "right")}.");
                float error = Vector3.Distance(actualHand.position, physical.position);
                report.player_contact_samples++;
                report.maximum_grip_error_metres = Mathf.Max(report.maximum_grip_error_metres, error);
                Assert.That(error, Is.LessThan(.035f),
                    $"Hero hand misses {prop.name}/{physical.name} in {help.CurrentAction}@{help.ActionSeconds:F3}: {error:F4} m; " +
                    $"root={village.Player.GameObject.transform.position:F4}, target={physical.position:F4}, " +
                    $"right wrist/reach={help.RightWristDistance:F4}/{help.RightReachLimit:F4}.");
                if (report.player_contact_samples % 20 == 1 || report.player_contact_samples % 20 == 2)
                    AssertOutdoorVisibleHand(((Player3DCharacterPresentation)village.Player.Visual).Registry.ModelRoot,
                        physical.position, left, report, observation);
            }
        }

        private sealed class VillageOutdoorSnapshot
        {
            private readonly int[] owners = Enumerable.Range(0, 6).Select(GameSessionState.VillageHousehold.GetLogBasket).ToArray();
            private readonly int[] stands = Enumerable.Range(0, 2).Select(GameSessionState.VillageHousehold.GetBasketDeliveryStand).ToArray();
            private readonly string[] ids = { VillageHouseholdProgress.PorchClearingId, VillageHouseholdProgress.ChapelClearingId,
                VillageHouseholdProgress.StationEdgeClearingId };
            private readonly int[] stages;
            private readonly bool chair = GameSessionState.VillageHousehold.ChairFixed;
            private readonly int water = GameSessionState.VillageHousehold.CompletedWaterVisits;
            internal VillageOutdoorSnapshot() => stages = ids.Select(GameSessionState.VillageHousehold.GetClearingStage).ToArray();
            internal void AssertState()
            {
                CollectionAssert.AreEqual(owners, Enumerable.Range(0, 6).Select(GameSessionState.VillageHousehold.GetLogBasket).ToArray());
                CollectionAssert.AreEqual(stands, Enumerable.Range(0, 2).Select(GameSessionState.VillageHousehold.GetBasketDeliveryStand).ToArray());
                CollectionAssert.AreEqual(stages, ids.Select(GameSessionState.VillageHousehold.GetClearingStage).ToArray());
                Assert.That(GameSessionState.VillageHousehold.ChairFixed, Is.EqualTo(chair));
                Assert.That(GameSessionState.VillageHousehold.CompletedWaterVisits, Is.EqualTo(water));
            }
        }

        private static void ObserveOutdoorRoot(AlpineVillageRoot village, Vector3 previous,
            VillageOutdoorReport report, string label)
        {
            float step = Vector3.Distance(previous, village.Player.GameObject.transform.position);
            report.maximum_root_step_metres = Mathf.Max(report.maximum_root_step_metres, step);
            Assert.That(step, Is.LessThan(.17f), $"Visible hero root jumped during {label}: {step:F4} m; {previous:F4} -> {village.Player.GameObject.transform.position:F4}.");
            Assert.That(village.Player.PresentationVisibility.RenderersHidden, Is.False,
                "Outdoor work must retain the production hero throughout entry, contact and exit.");
        }

        private static void ObserveOutdoorBody(AlpineVillageRoot village, VillageOutdoorReport report,
            VillageOutdoorObservation observation, string label)
        {
            CharacterController body = village.Player.GameObject.GetComponent<CharacterController>();
            Assert.That(body != null && body.enabled && body.detectCollisions, Is.True,
                "The actual player capsule must collide during " + label);
            Physics.SyncTransforms();
            int count = Physics.OverlapBoxNonAlloc(body.bounds.center, body.bounds.extents + Vector3.one * .03f,
                observation.Nearby, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            Assert.That(count, Is.LessThan(observation.Nearby.Length), "The collision query overflowed.");
            for (int index = 0; index < count; index++)
            {
                Collider solid = observation.Nearby[index];
                if (solid == body || solid.transform.IsChildOf(body.transform)) continue;
                if (!Physics.ComputePenetration(body, body.transform.position, body.transform.rotation,
                    solid, solid.transform.position, solid.transform.rotation, out _, out float distance)) continue;
                report.maximum_body_overlap_metres = Mathf.Max(report.maximum_body_overlap_metres, distance);
                Assert.That(distance, Is.LessThan(.025f),
                    $"Actual player capsule intersects {solid.name} during {label}: {distance:F4} m at {body.transform.position:F4}.");
            }
            report.body_collision_samples++;
        }

        private static void ObserveOutdoorProps(AlpineVillageLifeController life, VillageOutdoorReport report,
            VillageOutdoorObservation observation)
        {
            foreach (Transform prop in life.FirewoodLogs.Concat(life.Baskets).Concat(new[] { life.Shovel, life.Bucket }))
            {
                Assert.That(prop != null && prop.gameObject.activeInHierarchy, Is.True, "Finite stock disappeared.");
                if (observation.PreviousProps.TryGetValue(prop, out Vector3 before))
                {
                    float step = Vector3.Distance(before, prop.position);
                    report.maximum_prop_step_metres = Mathf.Max(report.maximum_prop_step_metres, step);
                    Assert.That(step, Is.LessThan(.17f), $"The actual {prop.name} jumped {step:F4} m.");
                }
                observation.PreviousProps[prop] = prop.position;
            }
        }

        private static void AssertOutdoorStock(AlpineVillageLifeController life, VillageOutdoorReport report)
        {
            var progress = GameSessionState.VillageHousehold;
            Assert.That(life.FirewoodLogs.Count, Is.EqualTo(VillageHouseholdProgress.LogCount));
            Assert.That(life.FirewoodLogs.Distinct().Count(), Is.EqualTo(VillageHouseholdProgress.LogCount));
            Assert.That(life.Baskets.Count, Is.EqualTo(VillageHouseholdProgress.BasketCount));
            for (int log = 0; log < VillageHouseholdProgress.LogCount; log++)
            {
                Transform actual = life.FirewoodLogs[log];
                Assert.That(actual != null && actual.gameObject.activeInHierarchy, Is.True);
                Assert.That(actual.GetComponentsInChildren<MeshFilter>().Any(mesh => mesh.sharedMesh != null && mesh.sharedMesh.vertexCount > 8),
                    Is.True, "A log identity must own actual imported geometry.");
                int basket = progress.GetLogBasket(log);
                if (basket >= 0) Assert.That(actual.IsChildOf(life.Baskets[basket]), Is.True,
                    $"Log {log} has a different visible owner from its committed session owner {basket}.");
            }
            Assert.That(progress.LooseLogCount + progress.GetBasketLogCount(0) + progress.GetBasketLogCount(1),
                Is.EqualTo(VillageHouseholdProgress.LogCount));
            report.unique_logs = life.FirewoodLogs.Count;
        }

        private static void AssertOutdoorVisibleHand(Transform modelRoot, Vector3 target, bool left,
            VillageOutdoorReport report, VillageOutdoorObservation observation)
        {
            string part = left ? "GEO_Hand.L" : "GEO_Hand.R";
            float distance = float.PositiveInfinity;
            foreach (SkinnedMeshRenderer renderer in modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!renderer.name.StartsWith(part, StringComparison.Ordinal)) continue;
                Assert.That(renderer.enabled, Is.True, "The measured hand is hidden.");
                renderer.BakeMesh(observation.Scratch, true);
                foreach (Vector3 vertex in observation.Scratch.vertices)
                    distance = Mathf.Min(distance, Vector3.Distance(renderer.transform.TransformPoint(vertex), target));
            }
            report.visible_hand_samples++;
            report.maximum_visible_hand_error_metres = Mathf.Max(report.maximum_visible_hand_error_metres, distance);
            Assert.That(distance, Is.LessThan(.08f), $"The deformed visible {part} misses its real contact: {distance:F4} m.");
        }

        private static void OutdoorFrame(Camera camera, VillageOutdoorObservation observation, string name,
            Vector3 position, Vector3 target, float fov = 50f)
        {
            if (!observation.Captures.Add(name)) return;
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
            camera.fieldOfView = fov;
            CaptureCurrentCamera(camera, "VillageOutdoorLife", name);
        }
    }
}
