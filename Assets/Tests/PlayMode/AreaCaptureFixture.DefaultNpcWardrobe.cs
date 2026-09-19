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
    public sealed class DefaultNpcWardrobeAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.Player3DV2AssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
            Type.GetType("BarPromenade.Editor.DefaultNpcAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
            // The cannery uses imported FBXs directly. Its existing asset
            // postprocessor configures this one changed truck on import.
            UnityEditor.AssetDatabase.ImportAsset("Assets/Resources/City/Cannery/Truck.fbx",
                UnityEditor.ImportAssetOptions.ForceSynchronousImport | UnityEditor.ImportAssetOptions.ForceUpdate);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [Serializable]
        private sealed class DefaultNpcWardrobeReport
        {
            public string capture_folder = "DefaultNpcWardrobe";
            public bool completed, fair_contacts, port_contacts, factory_contacts, driver_contacts;
            public int city_instances, village_instances, hero_visible_triangles;
            public float truck_seat_contact_m, bench_seat_contact_m;
            public Vector3 wheel_driver_facing_normal;
            public float wheel_chest_side_m, wheel_column_alignment, wheel_column_offset_m;
            public List<DefaultNpcGripSnapshot> grips = new List<DefaultNpcGripSnapshot>();
            public List<string> grip_failures = new List<string>();
            public List<string> captures = new List<string>();
            public List<DefaultNpcWardrobeSnapshot> actors = new List<DefaultNpcWardrobeSnapshot>();
            public List<DefaultNpcWardrobeSnapshot> roster = new List<DefaultNpcWardrobeSnapshot>();
        }

        [Serializable]
        private sealed class DefaultNpcWardrobeSnapshot
        {
            public string frame, actor, outfit, action, face, hair_color, appearance_key, model_id, visible_combination;
            public string[] items;
            public int visible_triangles;
            public Vector3 position;
        }

        [Serializable]
        private sealed class DefaultNpcGripSnapshot
        {
            public string phase, hand;
            public bool gloves;
            public float rim_radius_m, tube_radius_m, palm_near_side_m, finger_far_side_m, thumb_far_side_m;
            public float finger_radial_side_m, thumb_radial_side_m, finger_surface_gap_m, thumb_surface_gap_m;
            public float minimum_analytic_clearance_m;
        }

        [UnityTest]
        [Explicit("One default-NPC art review: three modular outfits, mixed clothes, hero comparison and current work placements.")]
        [PrebuildSetup(typeof(DefaultNpcWardrobeAssetsSetup))]
        public IEnumerator DefaultNpcWardrobe()
        {
            var report = new DefaultNpcWardrobeReport();
            float previousDelta = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                yield return CaptureFocusedPort((camera, city, port, crew) =>
                    CaptureDefaultNpcCity(camera, city, port, crew, report));
                yield return CaptureDefaultNpcVillage(report);
                report.completed = true;
            }
            finally
            {
                Time.captureDeltaTime = previousDelta;
                WriteDefaultNpcReport(report);
            }
        }

        [UnityTest]
        [Explicit("Focused actual driver finger grip on the wheel, glove variation and neutral release.")]
        [PrebuildSetup(typeof(DefaultNpcWardrobeAssetsSetup))]
        public IEnumerator DefaultNpcDriverGrip()
        {
            var report = new DefaultNpcWardrobeReport { capture_folder = "DefaultNpcDriverGrip" };
            float previousDelta = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                yield return CaptureFocusedPort((camera, city, port, crew) =>
                    CaptureDefaultNpcDriver(camera, city, report));
                report.completed = true;
            }
            finally
            {
                Time.captureDeltaTime = previousDelta;
                WriteDefaultNpcReport(report);
            }
        }

        private static IEnumerator CaptureDefaultNpcCity(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew, DefaultNpcWardrobeReport report)
        {
            var follow = camera.GetComponent<PlayerCameraFollow>();
            bool following = follow != null && follow.enabled;
            Vector3 before = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView, aspect = camera.aspect;
            IDisposable pause = null;
            try
            {
                if (follow != null) follow.enabled = false;
                camera.aspect = (float)Width / Height;
                GameSessionState.AdvanceGameTime((float)((12d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                    GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                pause = GameTimeScaleRuntime.AcquirePause();
                for (int frame = 0; frame < 3; frame++) yield return null;
                NpcWardrobe[] cityWardrobes = Object.FindObjectsByType<NpcWardrobe>(FindObjectsInactive.Include)
                    .Where(w => w.IsModular && w.gameObject.scene == city.gameObject.scene).ToArray();
                report.city_instances = cityWardrobes.Length;
                Assert.That(report.city_instances, Is.EqualTo(12));
                foreach (NpcWardrobe wardrobe in cityWardrobes) RecordDefaultNpcRoster(report, wardrobe);

                CityFairVendors fair = city.World.Fair.Vendors;
                Assert.That(fair, Is.Not.Null);
                fair.ApplyAt(3f);
                report.fair_contacts = fair.HandsMatch;
                yield return null;
                for (int i = 0; i < fair.Actors.Length; i++)
                    DefaultNpcActorFrame(camera, report, "10-fair-vendor-" + i, fair.Actors[i], 2.5f, .3f, 48f);
                // This existing wipe can exceed the authored arm reach. Record it
                // for the art review; model generation retains every bind transform.
                // The isolated wardrobe upgrade does not change stall placement/IK.
                yield return null;

                double unloading = CityPortCycle.UnloadStartSeconds + 2d;
                port.ApplyAt(unloading, 15f);
                crew.ApplyAt(unloading, unloading);
                yield return null;
                report.port_contacts = crew.CaptainHandsMatch && crew.CraneHandsMatch && crew.TrolleyHandsMatch;
                Assert.That(report.port_contacts, Is.True, "The upgraded body keeps the ship/crane/cart hand contacts.");
                DefaultNpcActorFrame(camera, report, "11-port-crane-operator", crew.FirstCraneOperator, 2.4f, .65f, 49f);
                DefaultNpcActorFrame(camera, report, "12-port-shore-worker", crew.ShoreWorker, 2.6f, .3f, 48f);
                yield return null;

                CityCanneryController cannery = city.Cannery;
                cannery.ApplyAt(CanneryTime(cannery, CityCanneryProductionStage.Prepare, .4f));
                cannery.ApplyLifeAt(100d);
                yield return null;
                report.factory_contacts = cannery.WorkerHandsMatch;
                Assert.That(report.factory_contacts, Is.True, cannery.LastCrewContactFailure);
                DefaultNpcActorFrame(camera, report, "13-cannery-preparation", cannery.GetFactoryWorker(1), 2.1f, .75f, 53f);
                DefaultNpcActorFrame(camera, report, "14-cannery-packing", cannery.GetFactoryWorker(3), 2.1f, .7f, 53f);
                yield return null;

                yield return CaptureDefaultNpcDriver(camera, city, report);
            }
            finally
            {
                pause?.Dispose();
                camera.transform.SetPositionAndRotation(before, rotation);
                camera.fieldOfView = fov; camera.aspect = aspect;
                if (follow != null) follow.enabled = following;
            }
        }

        private static IEnumerator CaptureDefaultNpcDriver(Camera camera, CityGameRoot city,
            DefaultNpcWardrobeReport report)
        {
            var state = new DefaultNpcCameraState(camera);
            var follow = camera.GetComponent<PlayerCameraFollow>();
            bool following = follow != null && follow.enabled;
            IDisposable pause = null;
            NpcWardrobe wardrobe = null;
            string previousGloves = null;
            try
            {
                if (follow != null) follow.enabled = false;
                camera.aspect = (float)Width / Height;
                GameSessionState.AdvanceGameTime((float)((12d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                    GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                pause = GameTimeScaleRuntime.AcquirePause();
                CityCanneryController cannery = city.Cannery;
                double driving = CanneryTime(cannery, CityFishSupplyStage.PortToFactory, .35f);
                cannery.ApplyAt(driving);
                var driver = cannery.GetComponentsInChildren<VillageResidentPresentation>(true)
                    .Single(actor => actor.name == "Fish Delivery Driver");
                Assert.That(driver.gameObject.activeInHierarchy, Is.True);
                wardrobe = driver.GetComponent<NpcWardrobe>();
                previousGloves = wardrobe.GetEquippedItem("gloves");
                NpcHandPose handPose = driver.GetComponent<NpcHandPose>();
                Assert.That(handPose, Is.Not.Null, "The catalog model owns its authored hand shapes.");
                Assert.That(handPose.ShapeName, Is.EqualTo("CylindricalGrip"));
                Transform truckSeat = CityPedestrianHandProps.FindSocket(cannery.Truck, "ANCHOR_TruckDriver");
                Assert.That(truckSeat, Is.Not.Null);

                foreach (bool gloves in new[] { false, true })
                {
                    wardrobe.SetSlot("gloves", gloves ? "gloves.work" : null);
                    cannery.ApplyAt(driving);
                    yield return null; // Flush the changed skin/shape to the rendered frame.
                    string variant = gloves ? "gloves" : "bare";
                    Vector3 forward = DefaultNpcFacing(driver), right = cannery.Truck.right;
                    Vector3 target = driver.Head.position - Vector3.up * .22f;
                    if (!gloves)
                    {
                        DefaultNpcFrame(camera, report, "15-driver-seated-clothing",
                            target + right * .57f + forward * .45f + Vector3.up * .02f, target, 88f, driver);
                        Vector3 wheel = cannery.DriverWheelCentre;
                        DefaultNpcFrame(camera, report, "15-driver-wheel-column-profile",
                            wheel + right * .80f + cannery.Truck.up * .06f - cannery.Truck.forward * .02f,
                            wheel - cannery.Truck.up * .17f, 70f, driver);
                    }
                    CaptureDefaultNpcWheelHands(camera, driver, cannery, report, variant, true);
                    report.driver_contacts = cannery.DriverSeatedContactsMatch;
                    Assert.That(report.driver_contacts, Is.True, cannery.LastCrewContactFailure);
                    DefaultNpcAssertWheelHands(driver, cannery, report, variant, gloves);
                    DefaultNpcAssertVisibleGrips(driver);
                    report.truck_seat_contact_m = DefaultNpcSeatSurfaceDistance(driver, truckSeat.position, cannery.Truck.up, forward);
                    Assert.That(report.truck_seat_contact_m, Is.InRange(-.015f, .025f), "Drawn hips rest on the truck cushion.");
                    if (gloves)
                    {
                        SkinnedMeshRenderer[] parts = handPose.Hands.SelectMany(binding => binding.Renderers).ToArray();
                        Mesh[] shared = parts.Select(part => part.sharedMesh).ToArray();
                        driver.gameObject.SetActive(false);
                        DefaultNpcAssertNeutralHands(handPose);
                        yield return null;
                        driver.gameObject.SetActive(true);
                        cannery.ApplyAt(driving);
                        yield return null;
                        for (int i = 0; i < parts.Length; i++)
                            Assert.That(parts[i].sharedMesh, Is.SameAs(shared[i]), "A grip reuses the shared mesh after wake.");
                        DefaultNpcAssertWheelHands(driver, cannery, report, "gloves-after-wake", true);
                    }
                }

                wardrobe.SetSlot("gloves", null);
                double reverseStart = cannery.Cycle.StageStart(CityFishSupplyStage.PortReverse, cannery.Snapshot.Batch);
                foreach (float seconds in new[] { .4f, .85f, 1.4f })
                {
                    cannery.ApplyAt(reverseStart + seconds);
                    Assert.That(cannery.DriverSeatedContactsMatch, Is.True, "Reverse pedals and pelvis stay supported.");
                    float contact = DefaultNpcSeatSurfaceDistance(driver, truckSeat.position, cannery.Truck.up, cannery.Truck.forward);
                    Assert.That(contact, Is.InRange(-.015f, .025f), "Drawn hips stay on the cushion through reverse lean " + seconds);
                }
                yield return null;
                CaptureDefaultNpcWheelHands(camera, driver, cannery, report, "reverse", false);
                Vector3 reverseTarget = driver.Head.position - Vector3.up * .30f;
                DefaultNpcFrame(camera, report, "15-driver-reverse-full-pose",
                    reverseTarget - cannery.Truck.right * .70f + cannery.Truck.forward * .65f + Vector3.up * .05f,
                    reverseTarget, 83f, driver);
                DefaultNpcAssertWheelHands(driver, cannery, report, "reverse", false, false);
                Assert.That(handPose.LeftGripWeight, Is.EqualTo(0f).Within(.001f), "The left hand releases the wheel for the door.");

                double waiting = cannery.Cycle.StageStart(CityFishSupplyStage.WaitForProduction, cannery.Snapshot.Batch);
                cannery.ApplyAt(waiting + 3.5d);
                yield return null;
                Assert.That(cannery.DriverBenchSeatWeight, Is.GreaterThan(.99f));
                DefaultNpcAssertNeutralHands(handPose);
                report.bench_seat_contact_m = DefaultNpcSeatSurfaceDistance(driver, cannery.DriverBenchSeatContact,
                    Vector3.up, DefaultNpcFacing(driver), .20f);
                DefaultNpcFrame(camera, report, "15-driver-bench-contact",
                    cannery.DriverBenchSeatContact + DefaultNpcFacing(driver) * 1.15f + Vector3.up * .43f,
                    cannery.DriverBenchSeatContact + Vector3.up * .28f, 65f, driver);
                Assert.That(report.bench_seat_contact_m, Is.InRange(-.015f, .025f), "Drawn hips rest on the bench planks.");
                wardrobe.SetSlot("gloves", "gloves.work");
                cannery.ApplyAt(waiting + 3.5d);
                yield return null;
                DefaultNpcAssertNeutralHands(handPose);
                Vector3 neutralHands = (driver.LeftGrip.position + driver.RightGrip.position) * .5f;
                DefaultNpcFrame(camera, report, "15-driver-off-wheel-neutral-gloves",
                    neutralHands + DefaultNpcFacing(driver) * .64f + Vector3.up * .36f, neutralHands, 65f, driver);
                Assert.That(report.grip_failures, Is.Empty, string.Join("\n", report.grip_failures));
            }
            finally
            {
                if (wardrobe != null) wardrobe.SetSlot("gloves", previousGloves);
                pause?.Dispose();
                state.Restore(camera);
                if (follow != null) follow.enabled = following;
            }
        }

        private static void CaptureDefaultNpcWheelHands(Camera camera, VillageResidentPresentation driver,
            CityCanneryController cannery, DefaultNpcWardrobeReport report, string variant, bool bothHands)
        {
            NpcHandPose pose = driver.GetComponent<NpcHandPose>();
            Vector3 axis = cannery.DriverWheelPalmNormal.normalized;
            Vector3 hands = bothHands ? cannery.DriverWheelCentre : pose.CylinderCentre(false);
            DefaultNpcFrame(camera, report, "15-driver-" + variant + "-wheel-back",
                hands + cannery.Truck.right * .30f - axis * .27f + cannery.Truck.up * .18f, hands, 68f, driver);
            foreach (bool left in bothHands ? new[] { true, false } : new[] { false })
            {
                Vector3 contact = pose.CylinderCentre(left);
                Vector3 radial = Vector3.ProjectOnPlane(contact - cannery.DriverWheelCentre, axis).normalized;
                DefaultNpcFrame(camera, report, "15-driver-" + variant + (left ? "-left-wrap" : "-right-wrap"),
                    contact + axis * .31f + radial * .14f + cannery.Truck.up * .30f, contact, 48f, driver);
            }
        }

        private static void DefaultNpcAssertNeutralHands(NpcHandPose pose)
        {
            Assert.That(pose.LeftGripWeight, Is.EqualTo(0f).Within(.001f));
            Assert.That(pose.RightGripWeight, Is.EqualTo(0f).Within(.001f));
            foreach (NpcHandPose.HandBinding binding in pose.Hands)
                foreach (SkinnedMeshRenderer renderer in binding.Renderers)
                    Assert.That(renderer.GetBlendShapeWeight(Enumerable.Range(0, renderer.sharedMesh.blendShapeCount).Single(index =>
                        renderer.sharedMesh.GetBlendShapeName(index) == pose.ShapeName ||
                        renderer.sharedMesh.GetBlendShapeName(index).EndsWith("." + pose.ShapeName, StringComparison.Ordinal))),
                        Is.EqualTo(0f).Within(.001f), renderer.name + " restores its neutral shape, including hidden gloves");
        }

        private static IEnumerator CaptureDefaultNpcVillage(DefaultNpcWardrobeReport report)
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.AlpineVillage, LoadSceneMode.Single);
            AlpineVillageRoot village = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                village = Object.FindAnyObjectByType<AlpineVillageRoot>();
                if (village != null && village.IsInitialized && !CompositionDriver.IsComposing) break;
                yield return null;
            }
            Assert.That(village != null && village.IsInitialized, Is.True, "Village composition completes before the NPC review.");
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            var state = new DefaultNpcCameraState(camera);
            GameObject references = null;
            IDisposable pause = null;
            bool following = village.CameraFollow.enabled, motorEnabled = village.Player.Motor.enabled;
            bool lifeEnabled = village.Life.enabled;
            Transform heroTransform = village.Player.GameObject.transform;
            Vector3 heroBefore = heroTransform.position;
            Quaternion heroRotation = heroTransform.rotation;
            Transform[] heroParts = heroTransform.GetComponentsInChildren<Transform>(true);
            int[] heroLayers = heroParts.Select(t => t.gameObject.layer).ToArray();
            try
            {
                village.CameraFollow.enabled = false;
                village.Player.Motor.enabled = false;
                village.Life.enabled = false;
                pause = GameTimeScaleRuntime.AcquirePause();
                camera.aspect = (float)Width / Height;
                for (int frame = 0; frame < 3; frame++) yield return null;
                var worker = village.Life.StationWorker;
                report.village_instances = village.Life.GetComponentsInChildren<NpcWardrobe>(true).Count(w => w.IsModular);
                Assert.That(report.village_instances, Is.EqualTo(1));
                RecordDefaultNpcRoster(report, worker.GetComponent<NpcWardrobe>());
                Assert.That(report.roster.Count, Is.EqualTo(13));
                Assert.That(report.roster.Select(entry => entry.appearance_key).Distinct().Count(), Is.EqualTo(13));
                Assert.That(report.roster.Select(entry => entry.visible_combination).Distinct().Count(),
                    Is.EqualTo(13), "The current roster exhausts no eligible pool, so visible model/face/clothing combinations must be unique.");
                NpcWardrobe stationClothes = worker.GetComponent<NpcWardrobe>();
                foreach (string slot in new[] { "outerwear", "boots", "headwear" })
                    Assert.That(stationClothes.GetEquippedItem(slot), Is.EqualTo(slot + ".warm"), "The station role retains winter protection.");
                Assert.That(stationClothes.GetEquippedItem("scarf"), Is.EqualTo("scarf.warm"));
                DefaultNpcActorFrame(camera, report, "16-village-station-worker", worker, 2.6f, .45f, 51f);
                yield return null;

                // Isolate only for equal-scale comparison. Real scene lights, the same
                // camera and post-processing remain; all actual placements are above.
                references = new GameObject("Default NPC wardrobe review models");
                references.transform.position = new Vector3(0f, 1000f, 0f);
                var actors = new VillageResidentPresentation[4];
                for (int i = 0; i < actors.Length; i++)
                {
                    actors[i] = DefaultNpcFactory.Create(references.transform);
                    actors[i].transform.localPosition = new Vector3((i - 2) * 1.2f, 0f, 0f);
                    actors[i].name = i < 3 ? new[] { "everyday", "work", "warm" }[i] : "mixed";
                    actors[i].GetComponent<NpcWardrobe>().ApplyOutfit(i < 3 ? actors[i].name : "everyday");
                    float yaw = Vector3.SignedAngle(DefaultNpcFacing(actors[i]), Vector3.forward, Vector3.up);
                    actors[i].ModelRoot.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * actors[i].ModelRoot.rotation;
                    foreach (Transform part in actors[i].GetComponentsInChildren<Transform>(true)) part.gameObject.layer = 31;
                    actors[i].Apply(VillageResidentAction.Idle, .3f);
                }
                NpcWardrobe mixed = actors[3].GetComponent<NpcWardrobe>();
                mixed.SetSlot("outerwear", "outerwear.work");
                mixed.SetSlot("shirt", "shirt.warm");
                mixed.SetSlot("headwear", "headwear.warm");
                mixed.SetSlot("scarf", "scarf.warm");
                var hero = village.Player.Visual as Player3DCharacterPresentation;
                Assert.That(hero, Is.Not.Null);
                heroTransform.SetPositionAndRotation(references.transform.position + Vector3.right * 2.4f, Quaternion.identity);
                foreach (Transform part in heroParts) if (part != null) part.gameObject.layer = 31;
                hero.SetMotion(PlayerMotionSample.Stationary);
                hero.ReapplyLatePresentationPose();
                report.hero_visible_triangles = DefaultNpcVisibleTriangles(hero.Renderers);
                camera.cullingMask = 1 << 31;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.18f, .19f, .18f);
                Vector3 center = references.transform.position + Vector3.up * 1f;
                yield return AdvanceDefaultNpcSkinningFrame(actors);
                DefaultNpcFrame(camera, report, "00-three-outfits-mixed-and-hero", center + new Vector3(0f, .05f, 7.8f), center, 34f, actors);
                string[] mixedItems = mixed.EquippedItemIds.ToArray();
                actors[3].gameObject.SetActive(false);
                yield return null;
                actors[3].gameObject.SetActive(true);
                actors[3].Apply(VillageResidentAction.Idle, .3f);
                yield return AdvanceDefaultNpcSkinningFrame(actors);
                CollectionAssert.AreEqual(mixedItems, mixed.EquippedItemIds);
                DefaultNpcFrame(camera, report, "00-mixed-after-visibility-restore",
                    center + new Vector3(0f, .05f, 7.8f), center, 34f, actors);
                for (int i = 0; i < actors.Length; i++)
                {
                    DefaultNpcActorFrame(camera, report, "01-" + actors[i].name + "-front", actors[i], 2.8f, .2f, 43f);
                    DefaultNpcActorFrame(camera, report, "02-" + actors[i].name + "-back", actors[i], -2.8f, .4f, 43f);
                }
                foreach (string view in new[] { "front", "profile" })
                {
                    Vector3 target = actors[0].Head.position + Vector3.up * .055f;
                    Vector3 offset = view == "front" ? new Vector3(.12f, .04f, .85f) : new Vector3(.8f, .03f, .1f);
                    DefaultNpcFrame(camera, report, "03-head-" + view, target + offset, target, 35f, actors[0]);
                }
                DefaultNpcAppearance faceAppearance = actors[0].GetComponent<DefaultNpcAppearance>();
                Assert.That(faceAppearance.Faces.Count, Is.EqualTo(4));
                string originalFace = faceAppearance.CurrentFaceId;
                string originalHair = faceAppearance.CurrentHairColorId;
                foreach (DefaultNpcAppearance.FaceBinding face in faceAppearance.Faces)
                {
                    faceAppearance.ApplyFace(face.Id);
                    yield return AdvanceDefaultNpcSkinningFrame(actors[0]);
                    Vector3 target = actors[0].Head.position + Vector3.up * .055f;
                    DefaultNpcFrame(camera, report, "03-face-" + face.Id,
                        target + new Vector3(.09f, .04f, .85f), target, 35f, actors[0]);
                }
                NpcWardrobe portraitClothes = actors[0].GetComponent<NpcWardrobe>();
                string originalHeadwear = portraitClothes.GetEquippedItem("headwear");
                portraitClothes.SetSlot("headwear", null);
                faceAppearance.ApplyFace("face-03");
                foreach (DefaultNpcAppearance.HairColorBinding hair in faceAppearance.HairColors)
                {
                    faceAppearance.ApplyHairColor(hair.Id);
                    yield return AdvanceDefaultNpcSkinningFrame(actors[0]);
                    Vector3 target = actors[0].Head.position + Vector3.up * .055f;
                    DefaultNpcFrame(camera, report, "03-hair-and-beard-" + hair.Id,
                        target + new Vector3(.09f, .04f, .85f), target, 35f, actors[0]);
                }
                portraitClothes.SetSlot("headwear", originalHeadwear);
                faceAppearance.ApplyFace(originalFace);
                faceAppearance.ApplyHairColor(originalHair);
                Vector3 heroLineupPosition = heroTransform.position;
                heroTransform.position = actors[0].transform.position + Vector3.right * .48f;
                hero.ReapplyLatePresentationPose();
                yield return AdvanceDefaultNpcSkinningFrame(actors[0]);
                Vector3 heads = (actors[0].Head.position + hero.Registry.Anchors.Head.position) * .5f + Vector3.up * .055f;
                DefaultNpcFrame(camera, report, "03-head-beside-hero",
                    heads + new Vector3(.025f, .025f, 1.3f), heads, 34f, actors[0]);
                yield return CaptureDefaultNpcAndHeroHands(camera, report, actors[0], hero, heroTransform);
                heroTransform.position = heroLineupPosition;
                hero.ReapplyLatePresentationPose();
                foreach (VillageResidentAction action in new[] { VillageResidentAction.Walk, VillageResidentAction.Reach })
                {
                    foreach (var actor in actors) actor.Apply(action, actor.ClipLength(action) * .37f);
                    yield return AdvanceDefaultNpcSkinningFrame(actors);
                    DefaultNpcFrame(camera, report, "04-outfits-" + action, center + new Vector3(.4f, .05f, 7.8f), center, 34f, actors);
                    var focusActor = actors[0];
                    Vector3 hands = (focusActor.LeftGrip.position + focusActor.RightGrip.position) * .5f;
                    DefaultNpcFrame(camera, report, "05-hands-" + action,
                        hands + Vector3.forward * 1.05f + Vector3.right * .35f + Vector3.up * .20f, hands, 45f, focusActor);
                    DefaultNpcAssertVisibleGrips(focusActor);
                }
                mixed.SetSlot("outerwear", null);
                mixed.SetSlot("scarf", null);
                yield return AdvanceDefaultNpcSkinningFrame(actors[3]);
                DefaultNpcActorFrame(camera, report, "06-mixed-inner-sweater", actors[3], 2.8f, .3f, 43f);
                yield return null;
            }
            finally
            {
                if (references != null)
                {
                    foreach (var actor in references.GetComponentsInChildren<VillageResidentPresentation>(true))
                        NpcFootstepSources.Unregister(actor.transform);
                    references.SetActive(false);
                    Object.Destroy(references);
                }
                for (int i = 0; i < heroParts.Length; i++) if (heroParts[i] != null) heroParts[i].gameObject.layer = heroLayers[i];
                heroTransform.SetPositionAndRotation(heroBefore, heroRotation);
                state.Restore(camera);
                village.CameraFollow.enabled = following;
                village.Player.Motor.enabled = motorEnabled;
                village.Life.enabled = lifeEnabled;
                pause?.Dispose();
                NpcFootstepSources.Prune();
            }
        }

        private static Vector3 DefaultNpcFacing(VillageResidentPresentation actor)
        {
            Transform right = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "upper_arm.R");
            Transform left = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "upper_arm.L");
            Assert.That(right, Is.Not.Null); Assert.That(left, Is.Not.Null);
            return Vector3.Cross(Vector3.ProjectOnPlane(right.position - left.position, Vector3.up), Vector3.up).normalized;
        }

        private static IEnumerator AdvanceDefaultNpcSkinningFrame(params VillageResidentPresentation[] actors)
        {
            VillageResidentAction[] actions = actors.Select(actor => actor.CurrentAction).ToArray();
            float[] seconds = actors.Select(actor => actor.CurrentActionSeconds).ToArray();
            Vector3[] left = actors.Select(actor => actor.LeftGrip.position).ToArray();
            Vector3[] right = actors.Select(actor => actor.RightGrip.position).ToArray();
            Vector3[] heads = actors.Select(actor => actor.Head.position).ToArray();
            // Camera.Render can reuse the frame's already skinned vertex buffer.
            // A new frame makes a changed manual pose or newly exposed layer visible.
            yield return null;
            for (int i = 0; i < actors.Length; i++)
            {
                VillageResidentPresentation actor = actors[i];
                Assert.That(actor.CurrentAction, Is.EqualTo(actions[i]), "Standalone capture actors have no automatic action owner.");
                Assert.That(actor.CurrentActionSeconds, Is.EqualTo(seconds[i]));
                Assert.That(Vector3.Distance(actor.LeftGrip.position, left[i]), Is.LessThan(.0001f), actor.name + " left pose after skinning frame");
                Assert.That(Vector3.Distance(actor.RightGrip.position, right[i]), Is.LessThan(.0001f), actor.name + " right pose after skinning frame");
                Assert.That(Vector3.Distance(actor.Head.position, heads[i]), Is.LessThan(.0001f), actor.name + " head pose after skinning frame");
            }
        }

        private static IEnumerator CaptureDefaultNpcAndHeroHands(Camera camera, DefaultNpcWardrobeReport report,
            VillageResidentPresentation actor, Player3DCharacterPresentation hero, Transform heroTransform)
        {
            Vector3 heroBefore = heroTransform.position;
            try
            {
                actor.Apply(VillageResidentAction.Idle, .3f);
                hero.SetMotion(PlayerMotionSample.Stationary);
                hero.ReapplyLatePresentationPose();
                // Bring the two actual hands into one close frame at the same scale.
                // Move only the temporary comparison placement; bones/meshes keep their poses.
                heroTransform.position += actor.RightGrip.position + Vector3.right * .23f - hero.Registry.Anchors.LeftGrip.position;
                hero.ReapplyLatePresentationPose();
                yield return AdvanceDefaultNpcSkinningFrame(actor);
                Vector3 target = (actor.RightGrip.position + hero.Registry.Anchors.LeftGrip.position) * .5f;
                DefaultNpcFrame(camera, report, "05-hands-npc-and-hero-front",
                    target + new Vector3(0f, .12f, .72f), target, 36f, actor);
                DefaultNpcFrame(camera, report, "05-hands-npc-and-hero-oblique",
                    target + new Vector3(.22f, .18f, .68f), target, 36f, actor);
                DefaultNpcAssertVisibleGrips(actor);
            }
            finally
            {
                heroTransform.position = heroBefore;
                hero.ReapplyLatePresentationPose();
            }
        }

        private static void DefaultNpcActorFrame(Camera camera, DefaultNpcWardrobeReport report,
            string name, VillageResidentPresentation actor, float distance, float lateral, float fov)
        {
            Vector3 target = actor.transform.position + Vector3.up * .96f;
            Vector3 forward = DefaultNpcFacing(actor), right = Vector3.Cross(Vector3.up, forward);
            DefaultNpcFrame(camera, report, name, target + forward * distance + right * lateral + Vector3.up * .30f, target, fov, actor);
        }

        private static void DefaultNpcFrame(Camera camera, DefaultNpcWardrobeReport report, string name,
            Vector3 position, Vector3 target, float fov, params VillageResidentPresentation[] actors)
        {
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
            camera.fieldOfView = fov;
            CaptureCurrentCamera(camera, report.capture_folder, name);
            report.captures.Add(name + ".png");
            foreach (var actor in actors)
            {
                NpcWardrobe wardrobe = actor.GetComponent<NpcWardrobe>();
                DefaultNpcAppearance appearance = actor.GetComponent<DefaultNpcAppearance>();
                Assert.That(wardrobe, Is.Not.Null);
                Assert.That(wardrobe.IsModular, Is.True);
                string modelId = string.IsNullOrEmpty(appearance.AppearanceKey) ? DefaultNpcCatalog.OrdinaryWorker :
                    DefaultNpcPopulation.GetAssignment(appearance.AppearanceKey).ModelId;
                int triangles = DefaultNpcVisibleTriangles(actor.GetComponentsInChildren<Renderer>(true));
                Assert.That(triangles, Is.InRange(1, 8000), name + " worn triangles");
                report.actors.Add(new DefaultNpcWardrobeSnapshot
                {
                    frame = name, actor = actor.name, outfit = wardrobe.CurrentOutfitId,
                    face = appearance.CurrentFaceId, hair_color = appearance.CurrentHairColorId,
                    appearance_key = appearance.AppearanceKey, model_id = modelId,
                    visible_combination = DefaultNpcVisibleCombination(modelId, appearance, wardrobe),
                    action = actor.CurrentAction.ToString(), items = wardrobe.EquippedItemIds.ToArray(),
                    visible_triangles = triangles, position = actor.transform.position
                });
            }
            Debug.Log("DEFAULT NPC: " + name);
            // NUnit may stop a nested iterator without disposing the outer one.
            // Keep the completed frames available even if a later assertion fails.
            WriteDefaultNpcReport(report);
        }

        private static void WriteDefaultNpcReport(DefaultNpcWardrobeReport report)
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", report.capture_folder);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "report.json"), JsonUtility.ToJson(report, true));
        }

        private static void RecordDefaultNpcRoster(DefaultNpcWardrobeReport report, NpcWardrobe wardrobe)
        {
            DefaultNpcAppearance appearance = wardrobe.GetComponent<DefaultNpcAppearance>();
            Assert.That(appearance, Is.Not.Null);
            Assert.That(appearance.AppearanceKey, Is.Not.Null.And.Not.Empty);
            string modelId = DefaultNpcPopulation.GetAssignment(appearance.AppearanceKey).ModelId;
            report.roster.Add(new DefaultNpcWardrobeSnapshot
            {
                actor = wardrobe.name, outfit = wardrobe.CurrentOutfitId, face = appearance.CurrentFaceId,
                hair_color = appearance.CurrentHairColorId,
                appearance_key = appearance.AppearanceKey, model_id = modelId,
                visible_combination = DefaultNpcVisibleCombination(modelId, appearance, wardrobe),
                items = wardrobe.EquippedItemIds.ToArray(),
                position = wardrobe.transform.position
            });
        }

        private static string DefaultNpcVisibleCombination(string modelId, DefaultNpcAppearance appearance, NpcWardrobe wardrobe) =>
            modelId + "|" + appearance.CurrentFaceId + "|" + appearance.CurrentHairColorId + "|" + string.Join("|",
                wardrobe.Items.Where(item => wardrobe.IsEquipped(item.Id) && item.Parts.Any(part => part.Renderer.enabled))
                    .Select(item => item.Id).OrderBy(item => item));

        private static int DefaultNpcVisibleTriangles(IEnumerable<Renderer> renderers)
        {
            int triangles = 0;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh :
                    renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                if (mesh != null) triangles += mesh.triangles.Length / 3;
            }
            return triangles;
        }

        private static void DefaultNpcAssertVisibleGrips(VillageResidentPresentation actor)
        {
            float left = float.PositiveInfinity, right = float.PositiveInfinity;
            var baked = new Mesh();
            try
            {
                foreach (SkinnedMeshRenderer renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!renderer.enabled) continue;
                    string part = renderer.name;
                    if (!(part.Contains("HandPalm") || part.Contains("Finger") || part.Contains("Thumb") || part.Contains("Glove"))) continue;
                    // These FBXs retain their import unit factors, like the hero
                    // and the existing cannery probes. Include that authored scale.
                    renderer.BakeMesh(baked, true);
                    foreach (Vector3 vertex in baked.vertices)
                    {
                        Vector3 point = renderer.transform.TransformPoint(vertex);
                        left = Mathf.Min(left, Vector3.Distance(point, actor.LeftGrip.position));
                        right = Mathf.Min(right, Vector3.Distance(point, actor.RightGrip.position));
                    }
                }
            }
            finally { Object.DestroyImmediate(baked); }
            Assert.That(left, Is.LessThan(.085f), "The visible left hand must reach its retained grip.");
            Assert.That(right, Is.LessThan(.085f), "The visible right hand must reach its retained grip.");
        }

        private static float DefaultNpcSeatSurfaceDistance(VillageResidentPresentation actor,
            Vector3 seat, Vector3 up, Vector3 forward, float halfDepth = .28f)
        {
            // Restrict the measured mesh to the support footprint. Shins,
            // knees and coat tails beyond the seat are not cushion contacts.
            float minimum = float.PositiveInfinity;
            var baked = new Mesh();
            try
            {
                Vector3 right = Vector3.Cross(up, forward).normalized;
                foreach (var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!renderer.enabled || !renderer.name.StartsWith("CLO_Trousers_", StringComparison.Ordinal)) continue;
                    renderer.BakeMesh(baked, true);
                    Vector3[] points = baked.vertices.Select(vertex =>
                    {
                        Vector3 offset = renderer.transform.TransformPoint(vertex) - seat;
                        return new Vector3(Vector3.Dot(offset, right), Vector3.Dot(offset, up), Vector3.Dot(offset, forward));
                    }).ToArray();
                    int[] triangles = baked.triangles;
                    for (int index = 0; index < triangles.Length; index += 3)
                    {
                        var polygon = new List<Vector3> { points[triangles[index]], points[triangles[index + 1]], points[triangles[index + 2]] };
                        polygon = CityBusNpcPassengerPlayModeTests.ClipSeatPolygon(polygon, 0, -.23f, true);
                        polygon = CityBusNpcPassengerPlayModeTests.ClipSeatPolygon(polygon, 0, .23f, false);
                        polygon = CityBusNpcPassengerPlayModeTests.ClipSeatPolygon(polygon, 2, -halfDepth, true);
                        polygon = CityBusNpcPassengerPlayModeTests.ClipSeatPolygon(polygon, 2, halfDepth, false);
                        foreach (Vector3 point in polygon) minimum = Mathf.Min(minimum, point.y);
                    }
                }
            }
            finally { Object.DestroyImmediate(baked); }
            Assert.That(float.IsInfinity(minimum), Is.False, "The worn trousers overlap the seat footprint.");
            return minimum;
        }

        private static void DefaultNpcAssertWheelHands(VillageResidentPresentation actor,
            CityCanneryController cannery, DefaultNpcWardrobeReport report, string phase, bool gloves, bool bothHands = true)
        {
            // Read the drawn 16-sided rim and its actual tube surface. Hand
            // anchors and the runtime's requested rotation cannot prove curl.
            DefaultNpcRimSurface rim = DefaultNpcReadRim(cannery, actor, report);
            NpcHandPose pose = actor.GetComponent<NpcHandPose>();
            var meshes = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var baked = new Mesh();
            try
            {
                Vector3[] Points(string name)
                {
                    SkinnedMeshRenderer renderer = meshes.Single(part => part.name == name);
                    Assert.That(renderer.enabled, Is.True, name + " is the visible anatomy in this outfit");
                    renderer.BakeMesh(baked, true);
                    return baked.vertices.Select(vertex => renderer.transform.TransformPoint(vertex)).ToArray();
                }
                foreach (bool left in bothHands ? new[] { true, false } : new[] { false })
                {
                    string suffix = left ? ".L" : ".R", prefix = gloves ? "CLO_Glove" : "GEO_Hand";
                    string label = phase + suffix;
                    void Check(bool passes, string message)
                    {
                        if (!passes) report.grip_failures.Add(label + ": " + message);
                    }
                    Check((left ? pose.LeftGripWeight : pose.RightGripWeight) > .999f, "authored grip is fully applied");
                    Vector3[] palm = Points(prefix + "Palm" + suffix);
                    Vector3[][] fingers = Enumerable.Range(0, 4).Select(index => Points(prefix + "Finger" + index + suffix)).ToArray();
                    Vector3[] thumb = Points(prefix + "Thumb" + suffix);
                    Vector3[] allFingers = fingers.SelectMany(points => points).ToArray();
                    float Axial(Vector3 point) => Vector3.Dot(point - rim.Centre, rim.Axis);
                    float Radial(Vector3 point) => Vector3.ProjectOnPlane(point - rim.Centre, rim.Axis).magnitude - rim.Radius;
                    float Clearance(Vector3 point)
                    {
                        float x = Axial(point), y = Radial(point);
                        return Mathf.Sqrt(x * x + y * y) - rim.TubeRadius;
                    }
                    var snapshot = new DefaultNpcGripSnapshot
                    {
                        phase = phase, hand = left ? "left" : "right", gloves = gloves,
                        rim_radius_m = rim.Radius, tube_radius_m = rim.TubeRadius,
                        palm_near_side_m = palm.Average(Axial),
                        finger_far_side_m = fingers.Min(points => points.Max(Axial)),
                        thumb_far_side_m = thumb.Max(Axial),
                        finger_radial_side_m = allFingers.Average(Radial),
                        thumb_radial_side_m = thumb.Average(Radial),
                        finger_surface_gap_m = fingers.Max(points => points.Min(rim.Distance)),
                        thumb_surface_gap_m = thumb.Min(rim.Distance),
                        minimum_analytic_clearance_m = allFingers.Concat(thumb).Min(Clearance)
                    };
                    report.grips.Add(snapshot);
                    Check(snapshot.palm_near_side_m < -rim.TubeRadius * .55f,
                        $"palm must remain behind the tube, measured {snapshot.palm_near_side_m:F4} m");
                    Check(snapshot.finger_far_side_m > rim.TubeRadius * .20f &&
                          fingers.All(points => points.Min(Axial) < -rim.TubeRadius * .35f),
                        $"each visible finger must curl from the near half onto the far half; least far reach {snapshot.finger_far_side_m:F4} m");
                    Check(snapshot.thumb_far_side_m > rim.TubeRadius * .20f,
                        $"thumb reaches around the tube, measured {snapshot.thumb_far_side_m:F4} m");
                    Check(snapshot.finger_radial_side_m > rim.TubeRadius * .20f &&
                          snapshot.thumb_radial_side_m < -rim.TubeRadius * .20f,
                        $"fingers and thumb oppose across the tube: {snapshot.finger_radial_side_m:F4}/{snapshot.thumb_radial_side_m:F4} m");
                    // Source contact allows 8 mm; the physical rim's 16 straight
                    // segments deviate from the circular hand cylinder by up to
                    // 4.5 mm. Measure the real triangle surface for the gap.
                    Check(snapshot.finger_surface_gap_m < .0125f && snapshot.thumb_surface_gap_m < .0125f,
                        $"drawn digits touch the real rim: worst finger gap {snapshot.finger_surface_gap_m:F4}, thumb {snapshot.thumb_surface_gap_m:F4} m");
                    Check(snapshot.minimum_analytic_clearance_m > -.008f,
                        $"digits must not pass through the tube: clearance {snapshot.minimum_analytic_clearance_m:F4} m");
                }
            }
            finally { Object.DestroyImmediate(baked); }
            WriteDefaultNpcReport(report);
        }

        private static DefaultNpcRimSurface DefaultNpcReadRim(CityCanneryController cannery,
            VillageResidentPresentation driver, DefaultNpcWardrobeReport report)
        {
            MeshFilter[] meshes = cannery.Truck.GetComponentsInChildren<MeshFilter>(true);
            MeshFilter rim = meshes.Single(mesh => mesh.name == "TruckSteeringRim__Rubber");
            Vector3[] vertices = rim.sharedMesh.vertices.Select(vertex => rim.transform.TransformPoint(vertex)).ToArray();
            Vector3 centre = vertices.Aggregate(Vector3.zero, (sum, point) => sum + point) / vertices.Length;
            // A ring's smallest spatial variance is perpendicular to its
            // plane. Fit its actual vertices independently of runtime anchors.
            Vector3 facing = DefaultNpcMeshAxis(vertices, centre, cannery.Truck.up, true);
            Assert.That(Vector3.Dot(facing, cannery.Truck.up), Is.GreaterThan(.65f), "Wheel face tilts upward.");
            Assert.That(Vector3.Dot(facing, cannery.Truck.forward), Is.LessThan(-.30f),
                "The physical wheel faces back toward the driver, not up toward the windscreen.");
            Transform chest = CityPedestrianHandProps.FindSocket(driver.ModelRoot, "chest");
            Assert.That(chest, Is.Not.Null);
            report.wheel_driver_facing_normal = facing;
            report.wheel_chest_side_m = Vector3.Dot(chest.position - centre, facing);
            Assert.That(report.wheel_chest_side_m, Is.GreaterThan(.04f), "The drawn wheel's front side faces the driver's chest.");
            Assert.That(Vector3.Distance(centre, cannery.DriverWheelCentre), Is.LessThan(.006f));
            Assert.That(Vector3.Dot(facing, cannery.DriverWheelAxis.normalized), Is.GreaterThan(.995f),
                "Runtime wheel axis agrees with the measured mesh plane.");
            Assert.That(Vector3.Dot(-facing, cannery.DriverWheelPalmNormal.normalized), Is.GreaterThan(.995f),
                "Palms face into the wheel from the driver's side.");

            MeshFilter column = meshes.Single(mesh => mesh.name == "TruckSteeringColumn__Steel");
            Vector3[] shaft = column.sharedMesh.vertices.Select(vertex => column.transform.TransformPoint(vertex)).ToArray();
            Vector3 shaftCentre = shaft.Aggregate(Vector3.zero, (sum, point) => sum + point) / shaft.Length;
            Vector3 shaftAxis = DefaultNpcMeshAxis(shaft, shaftCentre, facing, false);
            report.wheel_column_alignment = Vector3.Dot(shaftAxis, facing);
            report.wheel_column_offset_m = Vector3.ProjectOnPlane(shaftCentre - centre, facing).magnitude;
            Assert.That(report.wheel_column_alignment, Is.GreaterThan(.995f), "The visible steering column shares the wheel's axis.");
            Assert.That(report.wheel_column_offset_m, Is.LessThan(.005f), "The visible shaft meets the wheel hub at its centre.");
            float shaftTop = shaft.Max(point => Vector3.Dot(point - centre, facing));
            float shaftBottom = shaft.Min(point => Vector3.Dot(point - centre, facing));
            MeshFilter hub = meshes.Single(mesh => mesh.name == "TruckSteeringHub__Steel");
            float[] hubDepths = hub.sharedMesh.vertices.Select(vertex =>
                Vector3.Dot(hub.transform.TransformPoint(vertex) - centre, facing)).ToArray();
            Assert.That(shaftTop, Is.InRange(hubDepths.Min() + .002f, hubDepths.Max() - .002f),
                "The shaft ends inside the drawn hub rather than stopping short or protruding through its face.");
            Assert.That(hubDepths.Min(), Is.LessThan(0f));
            Assert.That(hubDepths.Max(), Is.GreaterThan(0f), "The hub straddles the rim plane.");
            Assert.That(shaftBottom, Is.InRange(-.55f, -.45f), "The column extends under the dashboard along the same axis.");

            float[] radii = vertices.Select(point => Vector3.ProjectOnPlane(point - centre, facing).magnitude).ToArray();
            float radiusMean = (radii.Min() + radii.Max()) * .5f;
            float tubeRadius = vertices.Max(point =>
            {
                Vector3 offset = point - centre;
                float radial = Vector3.ProjectOnPlane(offset, facing).magnitude - radiusMean;
                float axial = Vector3.Dot(offset, facing);
                return Mathf.Sqrt(radial * radial + axial * axial);
            });
            Assert.That(radiusMean, Is.InRange(.225f, .235f));
            Assert.That(tubeRadius, Is.InRange(.017f, .025f));
            var triangles = new List<Vector3[]>();
            int[] indices = rim.sharedMesh.triangles;
            for (int i = 0; i < indices.Length; i += 3)
                triangles.Add(new[] { vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]] });
            WriteDefaultNpcReport(report);
            // Positive grip depth points away from the driver, through the
            // tube. The palm stays near and fingertips curl onto its far half.
            return new DefaultNpcRimSurface { Centre = centre, Axis = -facing,
                Radius = radiusMean, TubeRadius = tubeRadius, Triangles = triangles };
        }

        private static Vector3 DefaultNpcMeshAxis(Vector3[] points, Vector3 centre, Vector3 seed, bool smallest)
        {
            float xx = 0f, yy = 0f, zz = 0f, xy = 0f, xz = 0f, yz = 0f;
            foreach (Vector3 point in points)
            {
                Vector3 delta = point - centre;
                xx += delta.x * delta.x; yy += delta.y * delta.y; zz += delta.z * delta.z;
                xy += delta.x * delta.y; xz += delta.x * delta.z; yz += delta.y * delta.z;
            }
            Vector3 axis = seed.normalized;
            for (int i = 0; i < 24; i++)
            {
                Vector3 product = new Vector3(xx * axis.x + xy * axis.y + xz * axis.z,
                    xy * axis.x + yy * axis.y + yz * axis.z, xz * axis.x + yz * axis.y + zz * axis.z);
                axis = (smallest ? (xx + yy + zz) * axis - product : product).normalized;
            }
            return Vector3.Dot(axis, seed) < 0f ? -axis : axis;
        }

        private sealed class DefaultNpcRimSurface
        {
            public Vector3 Centre, Axis;
            public float Radius, TubeRadius;
            public List<Vector3[]> Triangles;

            public float Distance(Vector3 point)
            {
                float minimum = float.PositiveInfinity;
                foreach (Vector3[] triangle in Triangles)
                    minimum = Mathf.Min(minimum, Vector3.Distance(point,
                        DefaultNpcTrianglePoint(point, triangle[0], triangle[1], triangle[2])));
                return minimum;
            }
        }

        private static Vector3 DefaultNpcTrianglePoint(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = point - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 >= d3 && d5 >= d6)
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denominator = 1f / (va + vb + vc);
            return a + ab * (vb * denominator) + ac * (vc * denominator);
        }

        private readonly struct DefaultNpcCameraState
        {
            private readonly Vector3 position;
            private readonly Quaternion rotation;
            private readonly float fov, aspect;
            private readonly int mask;
            private readonly CameraClearFlags flags;
            private readonly Color background;
            public DefaultNpcCameraState(Camera camera)
            {
                position = camera.transform.position; rotation = camera.transform.rotation;
                fov = camera.fieldOfView; aspect = camera.aspect; mask = camera.cullingMask;
                flags = camera.clearFlags; background = camera.backgroundColor;
            }
            public void Restore(Camera camera)
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov; camera.aspect = aspect; camera.cullingMask = mask;
                camera.clearFlags = flags; camera.backgroundColor = background;
            }
        }
    }
}
