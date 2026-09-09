using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [Serializable]
        private sealed class VillageOutdoorPartnersReport
        {
            public string scope = "gate, station partner, NPC errands and session reload";
            public bool starts_after_seeded_carry_and_porch_work = true;
            public bool verifies_firewood_or_player_shovel_journey = false;
            public VillageOutdoorReport measurements;
        }

        [UnityTest]
        [Timeout(420000)]
        [Explicit("Focused partner/errand regression from explicitly seeded household results. This is not the complete outdoor journey.")]
        [PrebuildSetup(typeof(VillageOutdoorLifeAssetsSetup))]
        public IEnumerator VillageOutdoorPartners()
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
                var progress = GameSessionState.VillageHousehold;
                // Arrange the exact state AFTER the separately exercised tasks.
                // No action completion below is inferred from these seed writes.
                for (int log = 0; log < VillageHouseholdProgress.LogCount; log++)
                    Assert.That(progress.TryLoadLog(log / VillageHouseholdProgress.LogsPerBasket, log), Is.True);
                for (int basket = 0; basket < VillageHouseholdProgress.BasketCount; basket++)
                    Assert.That(progress.TryDeliverBasket(basket, basket), Is.True);
                for (int stage = 0; stage < VillageHouseholdProgress.ClearingStageCount; stage++)
                    Assert.That(progress.TryAdvanceClearing(VillageHouseholdProgress.PorchClearingId, stage), Is.True);
                Assert.That(progress.TryCompleteChairRepair(), Is.True);

                Time.captureDeltaTime = 1f / 60f;
                yield return LoadOutdoorVillage(root => village = root);
                camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                cameraFov = camera.fieldOfView;
                var life = village.Life;
                var help = village.OutdoorHelp;
                var quiet = life.Neighbourhood.QuietHouse;
                float[] initialSnowDepths = life.Clearing.Patches.Select(patch =>
                    village.World.SnowTreading.SampleVisibleDepth(patch.Center)).ToArray();
                Debug.Log("Outdoor initial snow depths: " + string.Join(", ", life.Clearing.Patches.Select((patch, index) => patch.StableId + "=" + initialSnowDepths[index].ToString("F4"))));
                Assert.That(initialSnowDepths.Skip(1).Min(), Is.GreaterThan(.01f), "Unseeded chapel and station jobs need actual snow.");
                Assert.That(help, Is.Not.Null);
                AssertOutdoorStock(life, report);
                Assert.That(village.Workroom.ChairFixed, Is.True);
                Transform[] residents = life.Neighbours.Select(n => n.Actor.transform).ToArray();
                Assert.That(residents.Length, Is.EqualTo(6));

                // The only setup placement. Subsequent approach, work and
                // departure use the real Motor and shared positioned actions.
                Vector3 waiting = life.Neighbourhood.Yard(quiet, 1f, 3.3f);
                waiting.y = AlpineVillageTerrainSampler.SampleMeshHeight(village.Plan, new Vector2(waiting.x, waiting.z));
                village.Player.Motor.Teleport(waiting + Vector3.up * PlayerFactory.GroundedRootOffset);
                village.Player.GameObject.transform.rotation = Quaternion.LookRotation(quiet.Facing);
                Physics.SyncTransforms();
                ObserveOutdoorBody(village, report, observation, "off-route gate waiting space");

                yield return WaitOutdoorLife(village, () => life.CanReserveGateHelp, 480f,
                    report, observation, camera, "actual imminent basket passage");
                int passages = life.GatePassages;
                Assert.That(help.TryBegin(VillageOutdoorTask.HoldGate), Is.True);
                yield return WaitOutdoorAction(village, report, observation, camera, "partners-01-hold-gate", 1800);
                Assert.That(life.GatePassages, Is.GreaterThan(passages), "A helper timeout is not a basket passage.");
                Assert.That(life.GateHelpReserved, Is.False);
                report.gate_help_completed = true;
                yield return WalkOutdoorLeg(village, life.GateFrame.TransformPoint(new Vector3(-1.035f, 0f, -.60f)),
                    report, observation, camera, "leave the gate working side");
                yield return WalkOutdoorLeg(village, life.GateFrame.TransformPoint(new Vector3(-1.035f, 0f, .65f)),
                    report, observation, camera, "walk around the fence return");
                yield return WalkOutdoorLeg(village, life.Neighbourhood.Yard(quiet, -1.5f, 4.1f),
                    report, observation, camera, "outside the gate swing");
                yield return WalkOutdoorLeg(village, life.Neighbourhood.Yard(quiet, 0f, 4.1f),
                    report, observation, camera, "gate approach lane");
                yield return WalkOutdoorStreet(village, quiet.LaneDistance, 0f, report, observation, camera);
                Vector3 stationApproach = life.Plan.StationWork + life.Plan.StationForward * 1.4f;
                yield return WalkOutdoorLeg(village, stationApproach, report, observation, camera, "station helper approach");
                yield return WaitOutdoorLife(village, () => life.CanReserveStationHelp, 60f,
                    report, observation, camera, "station worker between jobs");
                Assert.That(help.TryBegin(VillageOutdoorTask.HoldLid), Is.True);
                yield return WaitOutdoorAction(village, report, observation, camera, "partners-02-hold-station-lid", 1800);
                Assert.That(life.StationHelpCompleted, Is.True);
                Assert.That(report.station_partner_contact_samples, Is.GreaterThan(30));
                Assert.That(report.station_partner_visible_hand_samples, Is.GreaterThanOrEqualTo(2));
                report.lid_help_completed = true;
                Assert.That(help.CompletedActionCount, Is.EqualTo(2), "Only the gate and lid actions are exercised here.");
                Assert.That(help.HasCarry, Is.False);
                yield return WalkOutdoorLeg(village, stationApproach, report, observation, camera, "release the station work dock");

                yield return WaitOutdoorLife(village, () => progress.CompletedWaterVisits > 0 &&
                    life.Clearing.IsComplete(VillageHouseholdProgress.ChapelClearingId) &&
                    life.Clearing.IsComplete(VillageHouseholdProgress.StationEdgeClearingId),
                    1500f, report, observation, camera, "water returned and both remaining NPC clearings complete");
                Assert.That(life.BucketFilled && life.Bucket.Find("Water").gameObject.activeInHierarchy, Is.True);
                Assert.That(report.bucket_fill_contact_samples, Is.GreaterThan(30));
                Assert.That(Vector3.Distance(life.Bucket.position,
                    life.Errands.BucketSupport + Vector3.up * AlpineVillageLifePlan.StandHeight), Is.LessThan(.025f));
                Assert.That(observation.Captures.Contains("10-spring-water-visit"), Is.True);
                Assert.That(observation.Captures.Contains("11-chapel-threshold-work"), Is.True);
                CollectionAssert.AreEqual(residents, life.Neighbours.Select(n => n.Actor.transform).ToArray());
                AssertOutdoorStock(life, report);
                report.completed_player_actions = help.CompletedActionCount;
                report.delivered_baskets = progress.DeliveredBasketCount;
                report.completed_water_visits = progress.CompletedWaterVisits;
                report.cleared_stages = life.Clearing.Patches.Sum(patch => life.Clearing.Stage(patch.StableId));
                report.snow_vertices_changed = life.Clearing.ChangedVertices;
                var snapshot = new VillageOutdoorSnapshot();
                Transform oldVillage = village.transform;
                AsyncOperation away = SceneManager.LoadSceneAsync(SceneIds.DoorTransition, LoadSceneMode.Single);
                Assert.That(away, Is.Not.Null);
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (!away.isDone && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(away.isDone && oldVillage == null, Is.True, "The original scene must really unload.");
                snapshot.AssertState();
                yield return LoadOutdoorVillage(root => village = root);
                camera = Camera.main;
                snapshot.AssertState();
                AssertOutdoorStock(village.Life, report);
                for (int basket = 0; basket < 2; basket++)
                {
                    int stand = progress.GetBasketDeliveryStand(basket);
                    Assert.That(Vector3.Distance(village.Life.Baskets[basket].position,
                        village.Life.Plan.Deliveries[stand] + Vector3.up * AlpineVillageLifePlan.StandHeight), Is.LessThan(.01f));
                }
                Assert.That(village.Workroom.ChairFixed && village.Life.BucketFilled, Is.True);
                Assert.That(village.Life.Bucket.Find("Water").gameObject.activeInHierarchy, Is.True);
                for (int patch = 0; patch < initialSnowDepths.Length; patch++)
                {
                    Assert.That(village.Life.Clearing.Stage(village.Life.Clearing.Patches[patch].StableId), Is.EqualTo(3));
                    Assert.That(village.World.SnowTreading.SampleVisibleDepth(village.Life.Clearing.Patches[patch].Center),
                        Is.LessThanOrEqualTo(initialSnowDepths[patch] + (patch == 0 ? .005f : -.005f)));
                }
                OutdoorFrame(camera, observation, "partners-03-reloaded-results",
                    village.Life.Plan.Yard(-1f, 7f) + Vector3.up * 1.8f,
                    village.Life.Plan.Yard(-1.6f, 1.5f) + Vector3.up * .7f);
                report.reload_restored_results = true;

                GameSessionState.BeginNewGame();
                Assert.That(progress.LooseLogCount, Is.EqualTo(6));
                Assert.That(progress.DeliveredBasketCount, Is.Zero);
                Assert.That(progress.ChairFixed || progress.WaterFetched, Is.False);
                foreach (var patch in village.Life.Clearing.Patches) Assert.That(progress.GetClearingStage(patch.StableId), Is.Zero);
                report.new_game_reset = true;
                report.captures = observation.Captures.OrderBy(name => name, StringComparer.Ordinal).ToArray();
                string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", "VillageOutdoorLife"));
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "partners-verification.json"),
                    JsonUtility.ToJson(new VillageOutdoorPartnersReport { measurements = report }, true));
            }
            finally
            {
                Time.captureDeltaTime = previousDelta;
                if (village != null)
                {
                    village.OutdoorHelp?.Cancel();
                    village.Player.GameObject.GetComponent<PlayerAnimatedInteractionController>()?.CancelActiveInteraction();
                    village.Player.Motor.CancelInteractionPoseMove();
                    village.Player.Motor.SetInputEnabled(true);
                    village.Life.enabled = true;
                    village.CameraFollow.enabled = true;
                }
                if (camera != null) camera.fieldOfView = cameraFov;
            }
        }
    }
}
