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
    public sealed class CityFairCaptureAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type models = Type.GetType("BarPromenade.Editor.CityFairAssetSetup, BarPromenade.Editor", true);
            models.GetMethod("ValidateOrThrow", Type.EmptyTypes).Invoke(null, null);
            Type actions = Type.GetType("BarPromenade.Editor.CityFairPlayerActionAssetSetup, BarPromenade.Editor", true);
            actions.GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
            Type children = Type.GetType("BarPromenade.Editor.CityFairChildAssetSetup, BarPromenade.Editor", true);
            children.GetMethod("ValidateOrThrow", Type.EmptyTypes).Invoke(null, null);
            Type childActions = Type.GetType("BarPromenade.Editor.CityFairChildActionAssetSetup, BarPromenade.Editor", true);
            childActions.GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [Serializable]
        private sealed class CityFairReport
        {
            public int stalls, goods_displays, vendors, seats, garlands, rain_shelters, pooled_lights;
            public int ground_samples, support_samples, grip_samples, completed_actions;
            public float maximum_ground_error_metres, maximum_support_error_metres, maximum_grip_error_metres;
            public bool center_path_walked, pause_froze_mechanism_and_vendors, cancellation_released, disable_released;
            public string[] captures;
            public int children, child_contact_samples;
            public int[] child_visible_triangles, child_completed_cycles;
            public float maximum_child_hand_error_metres, maximum_child_root_ground_error_metres;
            public bool child_pause_verified, bench_claims_verified, child_disable_reentry_verified;
            public int adults;
            public bool adult_pause_verified, adult_disable_reentry_verified, adult_routes_clear;
            public float maximum_adult_ground_error_metres;
        }

        [UnityTest]
        [Timeout(300000)]
        [Explicit("The actual City fair: grounded supports, clear crossing, both hero actions, recovery and reviewable views. Run alone.")]
        [PrebuildSetup(typeof(CityFairCaptureAssetsSetup))]
        public IEnumerator CityFair()
        {
            var report = new CityFairReport();
            var captures = new List<string>();
            float previousDelta = Time.captureDeltaTime;
            CityGameRoot city = null;
            Camera camera = null;
            PlayerCameraFollow follow = null;
            bool followEnabled = false;
            Vector3 cameraPosition = default;
            Quaternion cameraRotation = default;
            float cameraFov = 60f;
            try
            {
                GameSessionState.BeginNewGame();
                GameSessionState.TryStartGameTimeFromWake();
                Time.captureDeltaTime = .05f;
                AsyncOperation load = SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    if (load.isDone && city != null && city.IsInitialized && !CompositionDriver.IsComposing) break;
                    yield return null;
                }
                Assert.That(city != null && city.IsInitialized, Is.True, "City composition must finish before inspecting the fair.");
                CityFairWorld fair = city.World.Fair;
                Assert.That(fair, Is.Not.Null);
                Assert.That(fair.Plan.IsEnabled, Is.True);
                CityFairPlan plan = fair.Plan;
                CityFairPlanner.ValidateOrThrow(city.Layout, plan);
                camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                follow = camera.GetComponent<PlayerCameraFollow>();
                followEnabled = follow != null && follow.enabled;
                if (follow != null) follow.enabled = false;
                cameraPosition = camera.transform.position; cameraRotation = camera.transform.rotation; cameraFov = camera.fieldOfView;
                city.Player.Motor.Teleport(plan.CenterPathNorth + Vector3.up * PlayerFactory.GroundedRootOffset);
                city.Player.GameObject.transform.rotation = Quaternion.LookRotation(Vector3.back);
                Physics.SyncTransforms();
                for (int frame = 0; frame < 3; frame++) yield return null;

                Vector3 center = new Vector3(plan.Bounds.center.x, plan.SouthY, plan.Bounds.center.y);
                yield return FairFrame(camera, captures, "01-west-approach",
                    new Vector3(plan.Bounds.xMin - .7f, plan.SouthY + EyeHeight, center.z), center + Vector3.up * 1.35f, 72f);
                yield return FairFrame(camera, captures, "02-east-approach",
                    new Vector3(plan.Bounds.xMax + .7f, plan.SouthY + EyeHeight, center.z), center + Vector3.up * 1.35f, 72f);
                yield return FairFrame(camera, captures, "03-raised-overview",
                    center + new Vector3(-8.5f, 9f, -1.8f), center + Vector3.up * 1.15f, 68f);
                for (int index = 0; index < fair.Stalls.Count; index++)
                {
                    CityFairStall stall = plan.Stalls[index];
                    yield return FairFrame(camera, captures, "04-counter-" + index,
                        stall.Position + stall.Facing * 2.35f + Vector3.up * 1.85f,
                        stall.Position + stall.Facing * .35f + Vector3.up * 1.05f, 65f);
                }
                AssertFairGeometry(city, fair, report);
                AssertFairGround(fair, report);
                yield return VerifyFairAdults(fair, camera, captures, report);

                Debug.Log("FAIR: walking the real capsule across the continuous ground.");
                PlayerMotor motor = city.Player.Motor;
                motor.SetInputEnabled(false);
                Vector3 destination = plan.CenterPathSouth + Vector3.up * PlayerFactory.GroundedRootOffset;
                bool arrived = false;
                for (int frame = 0; frame < 180 && !arrived; frame++)
                {
                    arrived = motor.MoveTowardsApproachWaypoint(destination, .04f, .05f);
                    Assert.That(motor.InteractionPoseMoveStalled, Is.False, $"Fair crossing stopped the real motor at {motor.transform.position:F4}.");
                    yield return null;
                }
                motor.CancelInteractionPoseMove();
                motor.SetInputEnabled(true);
                yield return null;
                Assert.That(arrived, Is.True, "The central fair path must carry the real capsule between both rows.");
                Assert.That(Mathf.Abs(motor.transform.position.y - destination.y), Is.LessThan(.09f));
                report.center_path_walked = true;
                yield return FairFrame(camera, captures, "05-ground-crossing",
                    plan.CenterPathNorth + new Vector3(-2.0f, 1.7f, .5f), plan.CenterPathSouth + Vector3.up * .3f, 62f);

                Assert.That(fair.Organ, Is.Not.Null);
                Assert.That(fair.Bell, Is.Not.Null);
                foreach (CityFairInteraction action in new[] { fair.Organ, fair.Bell })
                    yield return RunFairAction(city, fair, action, camera, captures, report);
                Assert.That(report.completed_actions, Is.EqualTo(2));
                Assert.That(report.grip_samples, Is.GreaterThan(2), "Both moving mechanisms must be observed at actual held contact.");

                CityFairInteraction recovery = fair.Organ;
                PlaceFairActionStart(city, recovery);
                Assert.That(recovery.Begin(), Is.True);
                for (int frame = 0; frame < 100 && recovery.Controller.Phase != PlayerAnimatedInteractionPhase.Entering; frame++) yield return null;
                Assert.That(recovery.Controller.Phase, Is.EqualTo(PlayerAnimatedInteractionPhase.Entering));
                Assert.That(recovery.Cancel(), Is.True);
                yield return null;
                yield return null;
                AssertFairReleased(city, recovery);
                report.cancellation_released = true;

                PlaceFairActionStart(city, recovery);
                Assert.That(recovery.Begin(), Is.True);
                for (int frame = 0; frame < 150 && recovery.Controller.Phase != PlayerAnimatedInteractionPhase.Looping; frame++) yield return null;
                Assert.That(recovery.Controller.Phase, Is.EqualTo(PlayerAnimatedInteractionPhase.Looping));
                recovery.enabled = false;
                yield return null;
                yield return null;
                AssertFairReleased(city, recovery);
                Assert.That(recovery.CompletedCount, Is.EqualTo(1), "Neither interrupted attempt completes the organ action.");
                recovery.enabled = true;
                report.disable_released = true;
                Debug.Log("FAIR: both actions, contacts, pause, cancellation and disable cleanup verified.");
                yield return VerifyFairChildren(city, fair, camera, captures, report);
            }
            finally
            {
                if (city != null && city.World?.Fair != null)
                {
                    city.World.Fair.Organ?.Cancel(); city.World.Fair.Bell?.Cancel();
                    city.Player.Motor.CancelInteractionPoseMove();
                    city.Player.Motor.SetInputEnabled(true);
                }
                Time.captureDeltaTime = previousDelta;
                if (camera != null)
                {
                    camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                    camera.fieldOfView = cameraFov;
                }
                if (follow != null) follow.enabled = followEnabled;
                report.captures = captures.ToArray();
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "CityFair");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "verification.json"), JsonUtility.ToJson(report, true));
            }
        }

        private static void AssertFairGeometry(CityGameRoot city, CityFairWorld fair, CityFairReport report)
        {
            CityFairPlan plan = fair.Plan;
            foreach (var item in new[] { ("Building 11-3", plan.SouthBuildingBounds), ("Building 11-4", plan.NorthBuildingBounds) })
            {
                CityBuildingAssetRegistry building = city.World.Root.GetComponentsInChildren<CityBuildingAssetRegistry>()
                    .SingleOrDefault(value => value.transform.parent != null && value.transform.parent.name == item.Item1);
                Assert.That(building, Is.Not.Null, item.Item1);
                Bounds actual = FairMeshBounds(building.gameObject);
                Assert.That(Vector3.Distance(actual.center, item.Item2.center), Is.LessThan(.13f), item.Item1 + " actual imported facade position");
                Assert.That(Vector3.Distance(actual.size, item.Item2.size), Is.LessThan(.20f), item.Item1 + " actual imported facade extent");
            }
            Assert.That(fair.Stalls.Count, Is.EqualTo(4));
            Assert.That(fair.GoodsDisplays.Count, Is.EqualTo(4));
            Assert.That(fair.Vendors.ActorCount, Is.EqualTo(4));
            Assert.That(plan.Benches.Length, Is.EqualTo(2));
            var seats = city.GetComponentsInChildren<CityBenchSitInteraction>()
                .Where(seat => plan.Contains(seat.InteractionPosition)).ToArray();
            Assert.That(seats.Length, Is.EqualTo(2), "Both actual bench offers must belong to the fair.");
            for (int index = 0; index < 4; index++)
            {
                CityFairStall expected = plan.Stalls[index];
                Bounds stall = FairMeshBounds(fair.Stalls[index]);
                Bounds localStall = FairLocalMeshBounds(fair.Stalls[index]);
                Bounds goods = FairLocalMeshBounds(fair.GoodsDisplays[index]);
                Assert.That(localStall.size.x, Is.InRange(2.95f, 3.02f), expected.Id + " full-size canopy width");
                Assert.That(localStall.size.y, Is.InRange(2.50f, 2.62f), expected.Id + " full-size canopy height");
                Assert.That(localStall.size.z, Is.InRange(2.38f, 2.45f), expected.Id + " full-size canopy depth");
                foreach (float x in new[] { -1.32f, 1.32f })
                    foreach (float z in new[] { -1f, 1f })
                        AssertFairSupport(fair, fair.Stalls[index].transform.TransformPoint(new Vector3(x, 0f, z)),
                            expected.Id + " post foot", report);
                Assert.That(stall.min.z, Is.GreaterThanOrEqualTo(plan.Bounds.yMin - .025f));
                Assert.That(stall.max.z, Is.LessThanOrEqualTo(plan.Bounds.yMax + .025f));
                Assert.That(goods.size.x, Is.GreaterThan(.6f), "Goods are physical stock at real scale.");
                Assert.That(goods.min.y, Is.InRange(.94f, .99f), expected.Id + " goods touch the countertop");
                Assert.That(goods.max.y, Is.LessThan(1.75f), "The stock remains below the canopy.");
                Assert.That(Quaternion.Angle(fair.Stalls[index].transform.rotation, fair.GoodsDisplays[index].transform.rotation),
                    Is.LessThan(.001f), "Counter and stock follow one support plane.");
                Assert.That(Vector3.Distance(fair.Vendors.Actors[index].transform.position, expected.VendorPosition), Is.LessThan(.03f));
                AssertFairSupport(fair, fair.Vendors.Actors[index].transform.position, expected.Id + " vendor feet", report);
                Assert.That(Vector3.Dot(fair.Vendors.Actors[index].transform.position - expected.Position, expected.Facing), Is.LessThan(-.35f));
                Assert.That(FairMeshBounds(fair.Vendors.Actors[index].gameObject).size.y, Is.GreaterThan(1.3f));
            }
            Assert.That(fair.LampAnchors.Count, Is.EqualTo(5));
            Assert.That(plan.Garlands.Length, Is.EqualTo(3));
            Assert.That(fair.GetComponentsInChildren<Transform>().Count(part => part.name.StartsWith("Fixture_", StringComparison.Ordinal) &&
                part.GetComponent<MeshFilter>() == null), Is.EqualTo(39), "Each actual cable carries thirteen individual bulbs.");
            Assert.That(city.Night.Atmosphere.RealtimeLightCount, Is.LessThanOrEqualTo(12));
            foreach (Transform lamp in fair.LampAnchors)
                Assert.That(city.Night.Atmosphere.LampAnchors.Contains(lamp), Is.True, "The fair shares the city's bounded light pool.");
            CityRainField rain = city.GetComponentInChildren<CityRainField>();
            Assert.That(rain, Is.Not.Null);
            Assert.That(fair.RainShelters.Count, Is.EqualTo(4));
            foreach (Collider shelter in fair.RainShelters)
            {
                Assert.That(rain.LocalShelters.Contains(shelter), Is.True, "The actual rain field must register every canopy.");
                Assert.That(shelter.isTrigger && shelter.enabled, Is.True);
                Assert.That(shelter.bounds.size.x, Is.GreaterThan(2.8f));
                Assert.That(shelter.bounds.size.z, Is.GreaterThan(2.2f));
            }
            Assert.That(rain.Particles.trigger.enabled, Is.True);
            Assert.That(rain.Particles.trigger.inside, Is.EqualTo(ParticleSystemOverlapAction.Kill));
            report.stalls = fair.Stalls.Count; report.goods_displays = fair.GoodsDisplays.Count; report.vendors = fair.Vendors.ActorCount;
            report.seats = seats.Length; report.garlands = plan.Garlands.Length; report.rain_shelters = fair.RainShelters.Count;
            report.pooled_lights = city.Night.Atmosphere.RealtimeLightCount;
        }

        private static void AssertFairGround(CityFairWorld fair, CityFairReport report)
        {
            CityFairPlan plan = fair.Plan;
            Physics.SyncTransforms();
            for (float z = plan.CenterPathNorth.z; z >= plan.CenterPathSouth.z; z -= .10f)
            {
                Vector3 point = new Vector3(plan.CenterPathNorth.x, 0f, z);
                RaycastHit hit = FairGroundHit(fair, point, false);
                float expected = plan.SampleGroundY(point);
                float error = Mathf.Abs(hit.point.y - expected);
                report.maximum_ground_error_metres = Mathf.Max(report.maximum_ground_error_metres, error);
                report.ground_samples++;
                Assert.That(error, Is.LessThan(.04f), $"Fair ground physics differs at Z={z:F4}: {hit.collider.name}, actual={hit.point.y:F4}, expected={expected:F4}.");
            }
        }

        private static void AssertFairSupport(CityFairWorld fair, Vector3 support, string label, CityFairReport report)
        {
            RaycastHit hit = FairGroundHit(fair, support, true);
            float error = Mathf.Abs(support.y - hit.point.y);
            report.support_samples++;
            report.maximum_support_error_metres = Mathf.Max(report.maximum_support_error_metres, error);
            Assert.That(error, Is.LessThan(.035f), $"{label} is buried or floating: support={support:F4}, ground={hit.point:F4}.");
        }

        private static RaycastHit FairGroundHit(CityFairWorld fair, Vector3 point, bool ignoreFair)
        {
            point.y = fair.Plan.SampleGroundY(point) + 2f;
            RaycastHit[] hits = Physics.RaycastAll(point, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore)
                .Where(value => value.collider.GetComponentInParent<PlayerMotor>() == null &&
                    (!ignoreFair || value.collider.GetComponentInParent<CityFairWorld>() == null))
                .OrderBy(value => value.distance).ToArray();
            Assert.That(hits.Length, Is.GreaterThan(0), "The actual fair ground must remain continuous.");
            return hits[0];
        }

        private static void PlaceFairActionStart(CityGameRoot city, CityFairInteraction action)
        {
            city.Player.Motor.Teleport(action.Plan.EntryRootPosition - action.Plan.EntryFacingDirection * .65f);
            city.Player.GameObject.transform.rotation = action.Plan.EntryRotation * Quaternion.Euler(0f, 55f, 0f);
            city.Player.Motor.SetInputEnabled(true);
            Physics.SyncTransforms();
        }

        private static IEnumerator RunFairAction(CityGameRoot city, CityFairWorld fair, CityFairInteraction action,
            Camera camera, List<string> captures, CityFairReport report)
        {
            Debug.Log("FAIR: real hero action " + action.Kind);
            PlaceFairActionStart(city, action);
            yield return null;
            Assert.That(action.Begin(), Is.True, action.Kind + " should acquire the shared positioned controller.");
            bool photographed = false, observedApproach = false;
            int initialCompleted = action.CompletedCount;
            for (int frame = 0; frame < 230 && action.IsPlaying; frame++)
            {
                yield return null;
                observedApproach |= action.Controller.Phase == PlayerAnimatedInteractionPhase.Positioning;
                action.RefreshPresentation();
                if (action.ContactWeight >= .9999f)
                {
                    float error = Vector3.Distance(action.RightGrip.position, action.GripTarget.position);
                    report.grip_samples++;
                    report.maximum_grip_error_metres = Mathf.Max(report.maximum_grip_error_metres, error);
                    Assert.That(error, Is.LessThan(.025f), action.Kind + " loses its physical grip.");
                    if (action.Kind == CityFairInteractionKind.Bell)
                    {
                        Assert.That(Vector3.Distance(action.RopeSpanTopAnchor.position, action.RopeTopAnchor.position),
                            Is.LessThan(.005f), "The rope must remain attached to the swinging bell lever.");
                        Assert.That(Vector3.Distance(action.RopeSpanBottomAnchor.position, action.RopeTailAnchor.position),
                            Is.LessThan(.005f), "The imported rope must reach the same moving knot held by the hero.");
                    }
                }
                if (!photographed && action.Controller.Phase == PlayerAnimatedInteractionPhase.Looping && action.Controller.PhaseProgress >= .28f)
                {
                    photographed = true;
                    Vector3 dock = action.Plan.EntryRootPosition;
                    Vector3 right = action.Plan.EntryRotation * Vector3.right;
                    yield return FairFrame(camera, captures, "06-action-" + action.Kind,
                        dock + right * 1.6f + action.Plan.EntryFacingDirection * 1.0f + Vector3.up * 1.45f,
                        action.GripTarget.position - action.Plan.EntryFacingDirection * .20f, 58f, action);
                    if (action.Kind == CityFairInteractionKind.Organ)
                    {
                        using (GameTimeScaleRuntime.AcquirePause())
                        {
                            yield return null;
                            Vector3 grip = action.GripTarget.position;
                            float phase = action.Controller.PhaseProgress;
                            float vendorsTime = fair.Vendors.ElapsedSeconds;
                            for (int paused = 0; paused < 3; paused++) yield return null;
                            Assert.That(Vector3.Distance(action.GripTarget.position, grip), Is.LessThan(.00001f));
                            Assert.That(action.Controller.PhaseProgress, Is.EqualTo(phase).Within(.00001f));
                            Assert.That(fair.Vendors.ElapsedSeconds, Is.EqualTo(vendorsTime).Within(.00001f));
                            report.pause_froze_mechanism_and_vendors = true;
                        }
                    }
                }
            }
            Assert.That(observedApproach, Is.True, "The hero must visibly walk to the authored dock.");
            Assert.That(photographed, Is.True, action.Kind + " never reached the visible working phase.");
            Assert.That(action.IsPlaying, Is.False, action.Kind + " did not finish its bounded timeline.");
            yield return null;
            AssertFairReleased(city, action);
            Assert.That(action.CompletedCount, Is.EqualTo(initialCompleted + 1));
            Assert.That(Vector3.Distance(city.Player.GameObject.transform.position, action.Plan.ExitRootPosition), Is.LessThan(.04f));
            report.completed_actions++;
        }

        private static void AssertFairReleased(CityGameRoot city, CityFairInteraction action)
        {
            Assert.That(action.IsPlaying || action.Controller.IsActive, Is.False);
            Assert.That(city.Player.Motor.InputEnabled && city.Player.Interactor.InputEnabled, Is.True,
                "Fair completion/recovery must return ordinary controls.");
            Assert.That(action.ContactWeight, Is.Zero);
        }

        private static Bounds FairMeshBounds(GameObject owner)
        {
            Renderer[] selected = owner.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer is MeshRenderer || renderer is SkinnedMeshRenderer).ToArray();
            Assert.That(selected.Length, Is.GreaterThan(0), owner.name + " must have actual rendered geometry.");
            Bounds bounds = selected[0].bounds;
            foreach (Renderer renderer in selected.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static Bounds FairLocalMeshBounds(GameObject owner)
        {
            Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
            foreach (MeshFilter filter in owner.GetComponentsInChildren<MeshFilter>(true))
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 point = owner.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    low = Vector3.Min(low, point); high = Vector3.Max(high, point);
                }
            return new Bounds((low + high) * .5f, high - low);
        }

        private static IEnumerator FairFrame(Camera camera, List<string> captures, string name, Vector3 position, Vector3 target, float fov,
            CityFairInteraction action = null)
        {
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            camera.fieldOfView = fov;
            yield return null;
            // The test coroutine resumes before LateUpdate. Sample the same
            // no-time presentation seam before a synchronous camera render so
            // the prop and hand belong to the same current action frame.
            action?.RefreshPresentation();
            CaptureCurrentCamera(camera, "CityFair", name);
            captures.Add(name + ".png");
        }
    }
}
