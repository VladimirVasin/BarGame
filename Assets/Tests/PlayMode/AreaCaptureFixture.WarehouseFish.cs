using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("The warehouse fish station, seeded refrigerator rooms and real shared inspection/pickup flow. Run without -batchmode for UI frames.")]
        public IEnumerator CombatWarehouseFishProps()
        {
            CombatTestRoot root = null;
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse pointerMouse = InputSystem.AddDevice<Mouse>();
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            GameSessionState.BeginNewGame();
            try
            {
#if UNITY_EDITOR
                Type.GetType("BarPromenade.Editor.CombatWarehouseAssetValidation, BarPromenade.Editor", true)
                    .GetMethod("ValidateOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
                var selectedRooms = new HashSet<int>();
                var route = new List<Vector3>();
                foreach (int seed in new[] { 0, 1, 2, 3, 4, 5, 6, 7, int.MinValue, int.MaxValue })
                {
                    WarehouseArenaPlan plan = WarehouseArenaPlanner.Create(seed);
                    AssertWarehouseDeterminism(plan, WarehouseArenaPlanner.Create(seed));
                    var area = new WarehouseArenaArea(plan);
                    foreach (WarehouseRefrigeratorPlacement fridge in plan.Refrigerators)
                    {
                        Assert.That(fridge.RoomIndex / 3, Is.EqualTo(fridge.Floor));
                        selectedRooms.Add(fridge.RoomIndex);
                        Assert.That(area.FindRoute(plan.HeroSpawn, fridge.Approach, route), Is.True);
                    }
                }
                Assert.That(selectedRooms.Count, Is.EqualTo(9), "Seed variation must reach every candidate room.");
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root, Is.Not.Null);
                root.AutomaticSimulation = false;
                root.SetSparring(false);
                root.SetDuelLogging(false);
                for (int frame = 0; frame < 12; frame++) yield return null;
                AssertWarehouseGoalVisuals(root);
                AssertFishRoomLamps(root);
                var ids = new[] { InventoryItemId.RottenFishTailCan, InventoryItemId.RottenFishBellyCan, InventoryItemId.RottenFishHeadCan };
                WarehouseFishLamp pendant = AssertFishPendantMount(root);
                WarehouseArenaMechanisms machines = root.GetComponentInChildren<WarehouseArenaMechanisms>();
                Assert.That(machines, Is.Not.Null);
                AssertFishLampCollectionOrders(root, pendant, machines, ids);
                AssertFishLampPowered(pendant, false);
                float fanFullIntensity = machines.FanLight.intensity;
                int initialGrinds = machines.FanGrindPlayCount;
                GameObject station = FindWarehouseModel(root, "FishPreparationStation");
                WarehouseArenaAssetProvider.ValidatePlacedModel("FishPreparationStation", station);
                Assert.That(station.transform.position, Is.EqualTo(root.WarehousePlan.FishStationPosition));
                Assert.That(station.GetComponentsInChildren<Collider>(), Is.Not.Empty);
                Camera camera = root.CameraFollow.Camera;
                root.CameraFollow.enabled = false;
                Vector3 upperView = new Vector3(0f, 8.05f, 7.25f);
                root.Hero.ResetActor(new Vector3(-2.1f, 6.4f + PlayerFactory.GroundedRootOffset, 7.5f), Vector3.forward);
                camera.transform.SetPositionAndRotation(upperView,
                    Quaternion.LookRotation(root.WarehousePlan.FishStationPosition + Vector3.up * .85f - upperView));
                camera.fieldOfView = 57f;
                yield return null;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "warehouse-fish-station");
                SetFishPendantCamera(root, camera);
                yield return null;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Fish table lamp off");
                foreach (bool lit in new[] { true, false })
                {
                    int room = lit ? root.WarehousePlan.Refrigerators[0].RoomIndex :
                        (root.WarehousePlan.Refrigerators[0].RoomIndex + 1) % 3;
                    float yaw = room == 0 ? 0f : 180f;
                    Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
                    Vector3 roomCenter = root.WarehousePlan.RoomCenters[room];
                    Vector3 eye = roomCenter + rotation * new Vector3(5.25f, 1.7f, -.4f);
                    Vector3 target = root.WarehousePlan.Lamps[6 + room].Position;
                    root.Hero.ResetActor(new Vector3(eye.x, PlayerFactory.GroundedRootOffset, eye.z), rotation * Vector3.left);
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                    camera.fieldOfView = 60f;
                    yield return null;
                    CaptureCurrentCamera(camera, SceneIds.CombatTest, lit ? "warehouse-fish-room-lit" : "warehouse-fish-room-unlit");
                    if (lit) AssertRoomIndicatorSpill(root, camera, target);
                }

                WarehouseRefrigerator[] fridges = root.GetComponentsInChildren<WarehouseRefrigerator>();
                Array.Sort(fridges, (a, b) => a.Floor.CompareTo(b.Floor));
                Assert.That(fridges.Length, Is.EqualTo(3));
                var kinds = new[] { HomeRefrigeratorItemKind.RottenFishTailCan, HomeRefrigeratorItemKind.RottenFishBellyCan,
                    HomeRefrigeratorItemKind.RottenFishHeadCan };
                for (int floor = 0; floor < 3; floor++)
                {
                    WarehouseRefrigerator fridge = fridges[floor];
                    WarehouseRoomIndicator roomIndicator = FindFishRoomIndicator(root, fridge.SourceId);
                    int indicatorShutdowns = roomIndicator.ShutdownPlayCount;
                    Assert.That(fridge.Floor, Is.EqualTo(floor));
                    Assert.That(fridge.transform.position, Is.EqualTo(root.WarehousePlan.Refrigerators[floor].Position));
                    Assert.That(fridge.View.Items.Count, Is.EqualTo(1));
                    Assert.That(fridge.View.Items[0].Kind, Is.EqualTo(kinds[floor]));
                    Assert.That(fridge.View.Items[0].SourceId, Is.EqualTo(root.WarehousePlan.Refrigerators[floor].SourceId));
                    Vector3 dock = fridge.transform.TransformPoint(fridge.Plan.ApproachPosition);
                    root.Hero.ResetActor(dock, fridge.transform.forward);
                    Physics.SyncTransforms();
                    root.CameraFollow.enabled = true;
                    root.CameraFollow.ClearFixedPose();
                    input.Set(pointerMouse.position, camera.pixelRect.center);
                    for (int frame = 0; frame < 4; frame++) yield return null;
                    Assert.That(fridge.BeginInteraction(), Is.True);
                    for (int frame = 0; frame < 360 && fridge.Interaction.IsPositioning; frame++) yield return null;
                    Assert.That(fridge.Interaction.IsPositioning, Is.False, "The refrigerator dock must settle before its opening timeline advances.");
                    fridge.Interaction.AdvanceInteraction(HomeRefrigeratorInteractionTimeline.OpenSequenceDurationSeconds + .1f);
                    Assert.That(fridge.Interaction.Phase, Is.EqualTo(HomeRefrigeratorInteractionPhase.Inspecting));
                    Assert.That(fridge.View.DoorOpenAmount, Is.EqualTo(1f));
                    Assert.That(camera.GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Empty,
                        "No refrigerator may create a camera-local hero hand.");
                    foreach (Renderer heroRenderer in root.Hero.GetComponentsInChildren<Renderer>(true))
                        Assert.That(heroRenderer.enabled && !heroRenderer.forceRenderingOff && heroRenderer.gameObject.activeInHierarchy,
                            Is.False, "The borrowed close-up must hide every hero surface, including " + heroRenderer.name);
                    Assert.That(root.Flashlight.HasInspectionAim, Is.True);
                    if (floor == 0) yield return AssertDelayedRefrigeratorLight(root, fridge, input, pointerMouse);
                    if (floor == 1)
                    {
                        fridge.gameObject.SetActive(false);
                        yield return null;
                        Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
                        Assert.That(root.Player.Motor.InputEnabled && root.Player.Interactor.InputEnabled, Is.True);
                        Assert.That(root.CameraFollow.FixedPoseActive, Is.False);
                        fridge.gameObject.SetActive(true);
                        yield return null;
                        Assert.That(fridge.BeginInteraction(), Is.True, "A cancelled cabinet can be opened again.");
                        for (int frame = 0; frame < 360 && fridge.Interaction.IsPositioning; frame++) yield return null;
                        Assert.That(fridge.Interaction.IsPositioning, Is.False);
                        fridge.Interaction.AdvanceInteraction(HomeRefrigeratorInteractionTimeline.OpenSequenceDurationSeconds + .1f);
                    }
                    yield return null;
                    CaptureCurrentCamera(camera, SceneIds.CombatTest, "warehouse-fish-fridge-" + (floor + 1));
                    HomeRefrigeratorItemView item = fridge.View.Items[0];
                    var inspection = fridge.Interaction.ItemInspection;
                    Assert.That(inspection.TryBeginInspection(item), Is.True);
                    Assert.That(Quaternion.Angle(item.OriginalRoot.localRotation, Quaternion.identity), Is.LessThan(.001f),
                        "The held pose must use the model frame, regardless of the refrigerator's world yaw.");
                    inspection.AdvanceInspection(WorldItemInspectionTimeline.FlyingInDurationSeconds + .01f);
                    Assert.That(inspection.IsInspecting, Is.True);
                    AssertFishLampPowered(pendant, false);
                    Assert.That(machines.FanShutdownStarted, Is.False);
                    yield return null;
                    foreach (Volume volume in Object.FindObjectsByType<Volume>())
                        if (volume.name == "Cinematic Depth Of Field" && volume.profile.TryGet(out DepthOfField focus))
                        {
                            Assert.That(focus.focusDistance.value, Is.LessThan(1f), "Focus must follow the held can rather than the shelf.");
                            Assert.That(focus.aperture.value, Is.GreaterThanOrEqualTo(12f), "The whole can label must remain sharp.");
                        }
                    Vector3 heldLightTarget = root.Flashlight.InspectionAimTarget;
                    Quaternion heldLightRotation = root.Flashlight.SpotLight.transform.rotation;
                    input.Set(pointerMouse.position, camera.pixelRect.center + Vector2.right * 100f);
                    InputSystem.Update();
                    root.Flashlight.AdvanceInspectionAim(.2f);
                    Assert.That(root.Flashlight.InspectionAimTarget, Is.EqualTo(heldLightTarget));
                    Assert.That(Quaternion.Angle(root.Flashlight.SpotLight.transform.rotation, heldLightRotation), Is.LessThan(.01f));
                    CaptureCurrentCamera(camera, SceneIds.CombatTest, "warehouse-fish-can-" + (floor + 1));
                    yield return CaptureFishUi("warehouse-fish-inspection-" + (floor + 1));
                    if (floor == 2)
                    {
                        Assert.That(GameSessionState.TryAddInventoryItem(ids[floor]), Is.True);
                        Assert.That(inspection.InvokeAction(HomeRefrigeratorItemAction.Take), Is.True);
                        Assert.That(inspection.IsInspecting, Is.True, "Full item capacity must keep the can available for inspection.");
                        Assert.That(inspection.FeedbackKey, Is.EqualTo(WorldItemFoundScreen.NoRoomFeedbackKey));
                        Assert.That(fridge.View.Items.Count, Is.EqualTo(1));
                        Assert.That(GameSessionState.IsWorldItemCollected(fridge.SourceId), Is.False);
                        AssertFishLampPowered(pendant, false);
                        Assert.That(machines.FanShutdownStarted, Is.False);
                        Assert.That(roomIndicator.ShutdownPlayCount, Is.EqualTo(indicatorShutdowns));
                        Assert.That(GameSessionState.TryRemoveInventoryItem(ids[floor]), Is.True);
                    }
                    if (floor == 0)
                    {
                        input.Press(keyboard.eKey, queueEventOnly: true);
                        yield return null;
                        input.Release(keyboard.eKey, queueEventOnly: true);
                        yield return null;
                        Assert.That(fridge.Interaction.Phase, Is.EqualTo(HomeRefrigeratorInteractionPhase.Inspecting),
                            "The take key must be consumed by the nested item screen without closing the refrigerator.");
                    }
                    else Assert.That(inspection.InvokeAction(HomeRefrigeratorItemAction.Take), Is.True);
                    Assert.That(GameSessionState.GetInventoryItemCount(ids[floor]), Is.EqualTo(1));
                    Assert.That(GameSessionState.IsWorldItemCollected(fridge.SourceId), Is.True);
                    Assert.That(fridge.View.Items.Count, Is.Zero);
                    AssertFishLampPowered(pendant, floor == 2);
                    Assert.That(machines.FanShutdownStarted, Is.EqualTo(floor == 2));
                    if (floor == 2)
                    {
                        Assert.That(machines.FanShutdownProgress, Is.Zero, "The last can begins a coast-down without snapping to rest.");
                        Assert.That(machines.FanSpeedFactor, Is.EqualTo(1f));
                        Assert.That(machines.FanLight.intensity, Is.EqualTo(fanFullIntensity));
                        Assert.That(machines.FanGrindPlayCount, Is.EqualTo(initialGrinds + (GameTimeScaleRuntime.IsPaused ? 0 : 1)));
                        if (GameTimeScaleRuntime.IsPaused) Assert.That(machines.FanGrindSource.isPlaying, Is.False);
                    }
                    Assert.That(inspection.InvokeAction(HomeRefrigeratorItemAction.Take), Is.False);
                    Assert.That(GameSessionState.GetInventoryItemCount(ids[floor]), Is.EqualTo(1));
                    AssertFishLampPowered(pendant, floor == 2);
                    Assert.That(fridge.Interaction.RequestClose(), Is.True);
                    fridge.Interaction.AdvanceInteraction(HomeRefrigeratorInteractionTimeline.CloseSequenceDurationSeconds + .1f);
                    yield return null;
                    Assert.That(fridge.Interaction.OwnsInteraction, Is.False);
                    Assert.That(root.Flashlight.HasInspectionAim, Is.False);
                    Assert.That(root.CameraFollow.FixedPoseActive, Is.False, "Closing must restore the chase camera.");
                    Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
                    Assert.That(root.Player.Motor.InputEnabled && root.Player.Interactor.InputEnabled, Is.True);
                    yield return AssertFishRoomIndicatorFade(root, roomIndicator, indicatorShutdowns + 1);
                }
                machines.Advance(0f);
                Assert.That(machines.FanGrindPlayCount, Is.EqualTo(initialGrinds + 1));
                yield return AssertFishFanShutdown(root, machines, fanFullIntensity);
                SetFishPendantCamera(root, camera);
                yield return null;
                AdvanceFishPendantToFactor(pendant, HomeBathroomLightFlicker.MaximumFactor);
                float fullIntensity = pendant.Light.intensity;
                AssertFishPendantFactor(pendant, 1f, fullIntensity);
                AssertFishPendantSpill(root, camera, pendant);
                SetFishPendantCamera(root, camera);
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Fish table lamp white");
                int crackles = pendant.CracklePlayCount;
                AdvanceFishPendantToFactor(pendant, HomeBathroomLightFlicker.MinimumFactor);
                AssertFishPendantFactor(pendant, HomeBathroomLightFlicker.MinimumFactor, fullIntensity);
                Assert.That(pendant.CracklePlayCount, Is.GreaterThan(crackles));
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Fish table lamp flicker");
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    int pausedCrackles = pendant.CracklePlayCount;
                    pendant.Advance(HomeBathroomLightFlicker.CycleSeconds);
                    yield return null;
                    AssertFishPendantFactor(pendant, HomeBathroomLightFlicker.MinimumFactor, fullIntensity);
                    Assert.That(pendant.CracklePlayCount, Is.EqualTo(pausedCrackles));
                    Assert.That(pendant.Source.isPlaying, Is.False, "Pause must suspend the local electrical sound.");
                }
                pendant.Advance(.2f);
                Assert.That(pendant.CurrentFactor, Is.GreaterThan(HomeBathroomLightFlicker.MinimumFactor));
                WarehouseArenaPlan retained = root.WarehousePlan;
                root.ResetRound();
                Assert.That(root.WarehousePlan, Is.SameAs(retained));
                Assert.That(root.GetComponentInChildren<WarehouseFishLamp>(), Is.SameAs(pendant));
                AssertFishLampPowered(pendant, true);
                Assert.That(machines.FanShutdownProgress, Is.EqualTo(1f));
                Assert.That(machines.FanGrindPlayCount, Is.EqualTo(initialGrinds + 1));
                Assert.That(machines.FanGrindSource.isPlaying, Is.False);
                AssertWarehouseTapeProgress(root, machines);
                foreach (WarehouseRefrigerator fridge in fridges) Assert.That(fridge.View.Items.Count, Is.Zero);
                Assert.That(root.Inventory.Open(), Is.True);
                Assert.That(root.Inventory.SelectItem(2), Is.True);
                Assert.That(root.Inventory.ExamineSelected(), Is.True);
                yield return null;
                yield return CaptureFishUi("warehouse-fish-inventory");
                Assert.That(root.Inventory.Close(), Is.True);
                yield return null;
                AudioSource oldPendantSource = pendant.Source;
                AudioClip oldPendantClip = oldPendantSource.clip;
                AudioSource oldFanGrindSource = machines.FanGrindSource;
                AudioClip oldFanGrindClip = oldFanGrindSource.clip;
                WarehouseRoomIndicator[] oldIndicators = root.GetComponentsInChildren<WarehouseRoomIndicator>();
                var oldIndicatorSources = new AudioSource[oldIndicators.Length];
                for (int index = 0; index < oldIndicators.Length; index++) oldIndicatorSources[index] = oldIndicators[index].Source;
                pendant.gameObject.SetActive(false);
                Assert.That(pendant.Light.enabled, Is.False);
                Assert.That(oldPendantSource.isPlaying, Is.False);
                pendant.gameObject.SetActive(true);
                AssertFishLampPowered(pendant, true);
                Assert.That(root.NewWarehouseLayout(5), Is.True);
                yield return null;
                for (int frame = 0; frame < 3 && (oldPendantClip != null || oldFanGrindClip != null); frame++) yield return null;
                Assert.That(pendant == null, Is.True, "A new layout must destroy the old pendant owner.");
                Assert.That(oldPendantSource == null, Is.True);
                Assert.That(oldPendantClip == null, Is.True, "The old layout's generated crackle must be released.");
                Assert.That(oldFanGrindSource == null, Is.True);
                Assert.That(oldFanGrindClip == null, Is.True, "The old layout's generated grinding must be released.");
                foreach (AudioSource source in oldIndicatorSources) Assert.That(source == null, Is.True);
                pendant = AssertFishPendantMount(root);
                AssertFishLampPowered(pendant, true);
                machines = root.GetComponentInChildren<WarehouseArenaMechanisms>();
                Assert.That(machines.FanShutdownProgress, Is.EqualTo(1f));
                Assert.That(machines.FanSpeedFactor, Is.Zero);
                Assert.That(machines.FanLight.enabled, Is.False);
                Assert.That(machines.FanGrindPlayCount, Is.Zero, "A retained completed collection cannot replay the grinding in a new layout.");
                Assert.That(machines.FanGrindSource.isPlaying, Is.False);
                AssertWarehouseTapeProgress(root, machines);
                foreach (WarehouseRoomIndicator indicator in root.GetComponentsInChildren<WarehouseRoomIndicator>())
                {
                    Assert.That(indicator.CurrentFactor, Is.Zero);
                    Assert.That(indicator.ShutdownPlayCount, Is.Zero);
                    Assert.That(indicator.Source.isPlaying, Is.False);
                }
                Assert.That(root.WarehousePlan.Seed, Is.EqualTo(5));
                Assert.That(root.GetComponentsInChildren<WarehouseRefrigerator>().Length, Is.EqualTo(3));
                foreach (WarehouseRefrigerator fridge in root.GetComponentsInChildren<WarehouseRefrigerator>())
                    Assert.That(fridge.View.Items.Count, Is.Zero, "A new layout cannot duplicate the same collected session cans.");
                foreach (InventoryItemId id in ids) Assert.That(GameSessionState.GetInventoryItemCount(id), Is.EqualTo(1));
                AssertFishRoomLamps(root);
                Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
            }
            finally
            {
                if (root != null)
                    foreach (WarehouseRefrigerator fridge in root.GetComponentsInChildren<WarehouseRefrigerator>(true))
                        fridge.Interaction?.CancelInteraction();
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = null;
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (pointerMouse != null && pointerMouse.added) InputSystem.RemoveDevice(pointerMouse);
                input.TearDown();
            }
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            GameSessionState.ResetInventoryState();
        }

        private static WarehouseFishLamp AssertFishPendantMount(CombatTestRoot root)
        {
            WarehouseFishLamp[] lamps = root.GetComponentsInChildren<WarehouseFishLamp>();
            Assert.That(lamps.Length, Is.EqualTo(1));
            WarehouseFishLamp lamp = lamps[0];
            GameObject fixture = FindWarehouseModel(root, "FishStationLamp");
            WarehouseArenaAssetProvider.ValidatePlacedModel("FishStationLamp", fixture);
            Transform mount = WarehouseArenaAssetProvider.FindAnchor(fixture, "CeilingMount");
            Transform source = WarehouseArenaAssetProvider.FindAnchor(fixture, "Light");
            Assert.That(mount.position.y, Is.EqualTo(WarehouseArenaPlan.CeilingHeight).Within(.001f));
            Assert.That(fixture.transform.position, Is.EqualTo(mount.position));
            Assert.That(Vector3.Distance(lamp.Light.transform.position, source.position), Is.LessThan(.001f));
            Assert.That(lamp.Light.color, Is.EqualTo(Color.white));
            Assert.That(lamp.Light.type, Is.EqualTo(LightType.Point));
            Assert.That(lamp.Light.shadows, Is.EqualTo(LightShadows.Hard));
            Assert.That(lamp.Source.spatialBlend, Is.EqualTo(1f));
            Assert.That(lamp.Source.ignoreListenerPause, Is.False);
            Assert.That(lamp.Source.transform.position, Is.EqualTo(source.position));
            Renderer rose = FishPendantRenderer(fixture, "CeilingRose__");
            Renderer cord = FishPendantRenderer(fixture, "SupplyCord__");
            Renderer socket = FishPendantRenderer(fixture, "Socket__");
            Renderer bulb = FishPendantRenderer(fixture, "LampBulb__");
            Assert.That(rose.bounds.max.y, Is.GreaterThanOrEqualTo(mount.position.y));
            Assert.That(cord.bounds.max.y, Is.GreaterThan(rose.bounds.min.y), "The visible cord must enter the ceiling attachment.");
            Assert.That(cord.bounds.min.y, Is.LessThan(socket.bounds.max.y), "The visible cord must meet its socket.");
            Assert.That(socket.bounds.min.y, Is.LessThan(bulb.bounds.max.y), "The bare bulb must be held by its socket.");
            Assert.That(mount.position.y - bulb.bounds.min.y, Is.InRange(2.15f, 2.25f),
                "The imported pendant must preserve its authored metre scale.");
            Transform workSurface = WarehouseArenaAssetProvider.FindAnchor(FindWarehouseModel(root, "FishPreparationStation"), "WorkSurface");
            Assert.That(bulb.bounds.min.y - workSurface.position.y, Is.InRange(.75f, .95f),
                "The bare work lamp must hang low above the table while leaving working clearance.");
            Assert.That(fixture.GetComponentsInChildren<Collider>(), Is.Empty);
            return lamp;
        }

        private static Renderer FishPendantRenderer(GameObject fixture, string prefix)
        {
            foreach (Renderer renderer in fixture.GetComponentsInChildren<Renderer>())
                if (renderer.name.StartsWith(prefix, StringComparison.Ordinal)) return renderer;
            Assert.Fail("The authored fish pendant is missing " + prefix + ".");
            return null;
        }

        private static void AssertFishLampCollectionOrders(CombatTestRoot root, WarehouseFishLamp lamp,
            WarehouseArenaMechanisms machines, InventoryItemId[] ids)
        {
            int[][] orders = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
                new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
            foreach (int[] order in orders)
            {
                GameSessionState.ResetInventoryState();
                AssertFishLampPowered(lamp, false);
                Assert.That(machines.FanShutdownStarted, Is.False);
                Assert.That(machines.FanSpeedFactor, Is.EqualTo(1f));
                foreach (InventoryItemId id in ids) Assert.That(GameSessionState.TryAddInventoryItem(id), Is.True);
                AssertFishLampPowered(lamp, false);
                Assert.That(machines.FanShutdownStarted, Is.False);
                foreach (InventoryItemId id in ids) Assert.That(GameSessionState.TryRemoveInventoryItem(id), Is.True);
                for (int step = 0; step < order.Length; step++)
                {
                    int floor = order[step];
                    string source = root.WarehousePlan.Refrigerators[floor].SourceId;
                    Assert.That(GameSessionState.TryCollectWorldItem(source, ids[floor]), Is.True);
                    AssertFishLampPowered(lamp, step == 2);
                    Assert.That(machines.FanShutdownStarted, Is.EqualTo(step == 2));
                    Assert.That(GameSessionState.TryCollectWorldItem(source, ids[floor]), Is.False);
                    AssertFishLampPowered(lamp, step == 2);
                    foreach (WarehouseRoomIndicator indicator in root.GetComponentsInChildren<WarehouseRoomIndicator>())
                    {
                        indicator.Advance(WarehouseRoomIndicator.FadeDuration);
                        bool collected = GameSessionState.IsWorldItemCollected(indicator.SourceId);
                        Assert.That(indicator.CurrentFactor, Is.EqualTo(collected ? 0f : 1f));
                        Assert.That(indicator.Light.isActiveAndEnabled, Is.EqualTo(!collected));
                    }
                }
                foreach (InventoryItemId id in ids) Assert.That(GameSessionState.TryRemoveInventoryItem(id), Is.True);
                AssertFishLampPowered(lamp, true);
                machines.Advance(.75f);
                float progress = machines.FanShutdownProgress;
                int grinds = machines.FanGrindPlayCount;
                Assert.That(GameSessionState.TryCollectWorldItem(root.WarehousePlan.Refrigerators[order[2]].SourceId,
                    ids[order[2]]), Is.False);
                Assert.That(machines.FanShutdownProgress, Is.EqualTo(progress), "A duplicate source cannot restart the fan transition.");
                Assert.That(machines.FanGrindPlayCount, Is.EqualTo(grinds));
            }
            GameSessionState.ResetInventoryState();
        }

        private static WarehouseRoomIndicator FindFishRoomIndicator(CombatTestRoot root, string sourceId)
        {
            foreach (WarehouseRoomIndicator indicator in root.GetComponentsInChildren<WarehouseRoomIndicator>())
                if (indicator.SourceId == sourceId) return indicator;
            Assert.Fail("The selected refrigerator room must own an indicator for its finite source.");
            return null;
        }

        private static IEnumerator AssertFishRoomIndicatorFade(CombatTestRoot root, WarehouseRoomIndicator indicator, int expectedSounds)
        {
            indicator.Advance(0f);
            Assert.That(indicator.ShutdownPlayCount, Is.EqualTo(expectedSounds));
            Assert.That(indicator.Source.spatialBlend, Is.EqualTo(1f));
            Assert.That(indicator.Source.ignoreListenerPause, Is.False);
            Assert.That(indicator.Source.transform.position, Is.EqualTo(indicator.Light.transform.position));
            float before = indicator.CurrentFactor;
            indicator.Advance(.25f);
            Assert.That(indicator.CurrentFactor, Is.GreaterThan(0f).And.LessThan(before),
                "Only this collected room's red bulb must fade rather than disappear at Take.");
            float pausedFactor = indicator.CurrentFactor, pausedProgress = indicator.FadeProgress;
            using (GameTimeScaleRuntime.AcquirePause())
            {
                indicator.Advance(WarehouseRoomIndicator.FadeDuration);
                yield return null;
                Assert.That(indicator.CurrentFactor, Is.EqualTo(pausedFactor));
                Assert.That(indicator.FadeProgress, Is.EqualTo(pausedProgress));
                Assert.That(indicator.Source.isPlaying, Is.False);
            }
            indicator.Advance(WarehouseRoomIndicator.FadeDuration);
            Assert.That(indicator.CurrentFactor, Is.Zero);
            Assert.That(indicator.Light.enabled, Is.False);
            Assert.That(indicator.Source.isPlaying, Is.False);
            Assert.That(indicator.ShutdownPlayCount, Is.EqualTo(expectedSounds));
            AssertFishRoomLamps(root);
        }

        private static IEnumerator AssertFishFanShutdown(CombatTestRoot root, WarehouseArenaMechanisms machines, float fullIntensity)
        {
            Transform[] movingParts = { FindWarehouseModel(root, "LiftPlatform").transform,
                WarehouseArenaAssetProvider.FindAnchor(FindWarehouseModel(root, "ChainDrum"), "DrumRotor"),
                FindWarehouseModel(root, "PusherHead").transform };
            var initialPositions = new Vector3[movingParts.Length];
            var initialRotations = new Quaternion[movingParts.Length];
            for (int index = 0; index < movingParts.Length; index++)
            { initialPositions[index] = movingParts[index].localPosition; initialRotations[index] = movingParts[index].localRotation; }
            AudioSource grinding = machines.FanGrindSource;
            WarehouseArenaAmbience ventilation = machines.GetComponent<WarehouseArenaAmbience>();
            Assert.That(ventilation, Is.Not.Null);
            Renderer lens = null;
            foreach (Renderer renderer in FindWarehouseModel(root, "FanHousing").GetComponentsInChildren<Renderer>())
                if (renderer.name.EndsWith("__Lamp", StringComparison.Ordinal)) { lens = renderer; break; }
            Assert.That(lens, Is.Not.Null);
            var lensProperties = new MaterialPropertyBlock();
            Assert.That(grinding, Is.Not.Null);
            Assert.That(grinding.spatialBlend, Is.EqualTo(1f));
            Assert.That(grinding.ignoreListenerPause, Is.False);
            Assert.That(grinding.loop, Is.False);
            Assert.That(grinding.volume, Is.GreaterThan(.4f), "The grinding must be prominent above the old ventilation bed.");
            Assert.That(Vector3.Distance(grinding.transform.position, machines.FanRotor.position), Is.LessThan(.1f));
            Assert.That(grinding.clip, Is.Not.Null);
            Assert.That(grinding.clip.length, Is.GreaterThanOrEqualTo(WarehouseArenaMechanisms.FanShutdownDuration - .1f));
            Quaternion before = machines.FanRotor.localRotation;
            machines.Advance(.5f);
            float firstAngle = Quaternion.Angle(before, machines.FanRotor.localRotation);
            float firstPower = machines.FanLight.intensity;
            Assert.That(firstAngle, Is.GreaterThan(.01f));
            Assert.That(firstPower, Is.GreaterThan(0f).And.LessThan(fullIntensity));
            Assert.That(machines.FanSpeedFactor, Is.GreaterThan(0f).And.LessThan(1f));
            AssertWarehouseTapeProgress(root, machines);
            lens.GetPropertyBlock(lensProperties);
            Assert.That(lensProperties.GetColor("_EmissionColor").maxColorComponent,
                Is.EqualTo(lens.sharedMaterial.GetColor("_EmissionColor").maxColorComponent * machines.FanSpeedFactor).Within(.0001f));
            Assert.That(ventilation.PowerFactor, Is.EqualTo(machines.FanSpeedFactor));
            before = machines.FanRotor.localRotation;
            machines.Advance(.5f);
            Assert.That(Quaternion.Angle(before, machines.FanRotor.localRotation), Is.GreaterThan(0f).And.LessThan(firstAngle),
                "Equal time steps must turn the fan through progressively smaller angles.");
            Assert.That(machines.FanLight.intensity, Is.GreaterThan(0f).And.LessThan(firstPower));
            AssertWarehouseTapeProgress(root, machines);
            for (int index = 0; index < movingParts.Length; index++)
                Assert.That(Vector3.Distance(initialPositions[index], movingParts[index].localPosition) > .00001f ||
                    Quaternion.Angle(initialRotations[index], movingParts[index].localRotation) > .001f, Is.True,
                    "Every working mechanism must continue moving during its gradual rundown.");
            float pausedProgress = machines.FanShutdownProgress;
            float pausedPower = machines.FanLight.intensity;
            float pausedSpeed = machines.FanSpeedFactor;
            Quaternion pausedPose = machines.FanRotor.localRotation;
            int grinds = machines.FanGrindPlayCount;
            using (GameTimeScaleRuntime.AcquirePause())
            {
                machines.Advance(WarehouseArenaMechanisms.FanShutdownDuration);
                yield return null;
                Assert.That(machines.FanShutdownProgress, Is.EqualTo(pausedProgress));
                Assert.That(machines.FanSpeedFactor, Is.EqualTo(pausedSpeed));
                Assert.That(machines.FanLight.intensity, Is.EqualTo(pausedPower));
                Assert.That(Quaternion.Angle(pausedPose, machines.FanRotor.localRotation), Is.LessThan(.001f));
                Assert.That(grinding.isPlaying, Is.False);
                AssertWarehouseTapeProgress(root, machines);
                Assert.That(root.Music.Source.isPlaying, Is.False, "The chewing cassette must freeze with the fan during pause.");
            }
            Camera camera = root.CameraFollow.Camera;
            root.CameraFollow.enabled = false;
            Vector3 eye = new Vector3(0f, 7.9f, -5.6f);
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(machines.FanRotor.position - eye));
            camera.fieldOfView = 78f;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Fan slowing after third can");
            machines.Advance(WarehouseArenaMechanisms.FanShutdownDuration);
            Assert.That(machines.FanShutdownProgress, Is.EqualTo(1f));
            Assert.That(machines.FanSpeedFactor, Is.Zero);
            Assert.That(machines.FanLight.intensity, Is.Zero);
            Assert.That(machines.FanLight.enabled, Is.False);
            lens.GetPropertyBlock(lensProperties);
            Assert.That(lensProperties.GetColor("_EmissionColor").maxColorComponent, Is.Zero);
            Assert.That(ventilation.Source.volume, Is.Zero);
            Assert.That(ventilation.Source.isPlaying, Is.False);
            Assert.That(grinding.isPlaying, Is.False);
            Assert.That(machines.FanGrindPlayCount, Is.EqualTo(grinds));
            AssertWarehouseTapeProgress(root, machines);
            for (int index = 0; index < movingParts.Length; index++)
            { initialPositions[index] = movingParts[index].localPosition; initialRotations[index] = movingParts[index].localRotation; }
            Quaternion stopped = machines.FanRotor.localRotation;
            machines.Advance(1f);
            Assert.That(Quaternion.Angle(stopped, machines.FanRotor.localRotation), Is.LessThan(.001f));
            for (int index = 0; index < movingParts.Length; index++)
            {
                Assert.That(movingParts[index].localPosition, Is.EqualTo(initialPositions[index]));
                Assert.That(Quaternion.Angle(initialRotations[index], movingParts[index].localRotation), Is.LessThan(.001f),
                    "Lift, drum and pusher must stay at their final physical poses.");
            }
            int strainVoices = 0;
            foreach (AudioSource source in machines.GetComponentsInChildren<AudioSource>())
                if (source.name.EndsWith(" Strain", StringComparison.Ordinal))
                { strainVoices++; Assert.That(source.volume, Is.Zero); Assert.That(source.isPlaying, Is.False); }
            Assert.That(strainVoices, Is.EqualTo(3));
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse - Fan stopped after third can");
        }

        private static void AssertWarehouseTapeProgress(CombatTestRoot root, WarehouseArenaMechanisms machines)
        {
            CombatWarehouseMusicPlayer music = root.Music;
            Assert.That(music, Is.Not.Null);
            Assert.That(music.TapeStopProgress, Is.EqualTo(machines.FanShutdownProgress).Within(.00001f));
            Assert.That(music.TapeStopped, Is.EqualTo(machines.FanSpeedFactor == 0f));
            if (music.TapeStopped)
            {
                Assert.That(music.Source.isPlaying, Is.False);
                Assert.That(music.Source.volume, Is.Zero);
                Assert.That(music.Source.pitch, Is.Zero);
            }
            else
            {
                float basePitch = CombatWarehouseMusicPlayer.PlaybackSpeed;
                Assert.That(music.Source.pitch, Is.InRange(basePitch * Mathf.Max(.025f, machines.FanSpeedFactor * .90f),
                    basePitch * Mathf.Max(.025f, machines.FanSpeedFactor * 1.10f)));
                Assert.That(music.ToneFilter.cutoffFrequency, Is.LessThanOrEqualTo(MusicMix.ToneCutoffFrequency));
            }
        }

        private static void AssertFishLampPowered(WarehouseFishLamp lamp, bool powered)
        {
            Assert.That(lamp.IsLit, Is.EqualTo(powered));
            Assert.That(lamp.Light.isActiveAndEnabled, Is.EqualTo(powered));
            Assert.That(lamp.CurrentFactor > 0f, Is.EqualTo(powered));
            Assert.That(lamp.Light.intensity > 0f, Is.EqualTo(powered));
        }

        private static void AssertFishPendantSpill(CombatTestRoot root, Camera camera, WarehouseFishLamp pendant)
        {
            Assert.That(root.GetComponentInChildren<WarehouseArenaMechanisms>().FanLight.enabled, Is.False);
            GameObject station = FindWarehouseModel(root, "FishPreparationStation");
            float workHeight = WarehouseArenaAssetProvider.FindAnchor(station, "WorkSurface").position.y;
            Vector3 eye = new Vector3(2.3f, 9f, 7.45f);
            camera.transform.SetPositionAndRotation(eye,
                Quaternion.LookRotation(station.transform.position + Vector3.up * 1.1f - eye));
            camera.fieldOfView = 65f;
            Color32[] lit = CaptureWarehouseFanPixels(camera, "Test warehouse - Fish table surface light on");
            Color32[] dark;
            try
            {
                // Only the real source changes. The bulb's emissive surface,
                // camera, other lights and stopped fan remain identical.
                pendant.Light.enabled = false;
                dark = CaptureWarehouseFanPixels(camera, "Test warehouse - Fish table surface light off");
            }
            finally { pendant.Light.enabled = true; }
            int tableSamples = 0, wallSamples = 0, tableBrightened = 0, wallBrightened = 0;
            float tableIncrease = 0f, wallIncrease = 0f;
            // Project a conservative patch on the actual horizontal bowl floor,
            // seen above the front lip. Its collision proxy is a full AABB and
            // cannot identify the visible work surface.
            var tablePixels = new HashSet<int>();
            for (int row = 0; row < 6; row++)
                for (int column = 0; column < 21; column++)
                {
                    Vector3 point = station.transform.position + new Vector3(-.6f + column * .06f, 0f, .05f + row * .06f);
                    point.y = workHeight + .011f;
                    Vector3 viewport = camera.WorldToViewportPoint(point);
                    Assert.That(viewport.z > 0f && viewport.x > .02f && viewport.x < .98f && viewport.y > .02f && viewport.y < .98f,
                        Is.True, "The diagnostic must see the actual inner work surface.");
                    int pixel = Mathf.FloorToInt(viewport.y * Height) * Width + Mathf.FloorToInt(viewport.x * Width);
                    if (!tablePixels.Add(pixel)) continue;
                    float increase = FanPixelLight(lit[pixel]) - FanPixelLight(dark[pixel]);
                    tableSamples++; tableIncrease += increase; if (increase > .025f) tableBrightened++;
                }
            Physics.SyncTransforms();
            for (int y = 8; y < Height - 8; y += 8)
                for (int x = 8; x < Width - 8; x += 8)
                {
                    Ray ray = camera.ViewportPointToRay(new Vector3((x + .5f) / Width, (y + .5f) / Height, 0f));
                    if (!Physics.Raycast(ray, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    float increase = FanPixelLight(lit[y * Width + x]) - FanPixelLight(dark[y * Width + x]);
                    bool wall = hit.normal.z < -.8f && hit.point.z > 11.8f && hit.point.z < 12.2f &&
                        Mathf.Abs(hit.point.x) < 3f && hit.point.y > workHeight && hit.point.y < workHeight + 2.1f;
                    if (wall) { wallSamples++; wallIncrease += increase; if (increase > .02f) wallBrightened++; }
                }
            Assert.That(tableSamples, Is.GreaterThan(20), "The diagnostic must see the actual horizontal bowl floor.");
            Assert.That(wallSamples, Is.GreaterThan(80), "The diagnostic must see the surrounding rear wall.");
            Debug.Log($"Test fish lamp spill: table={tableIncrease / tableSamples:F3}, wall={wallIncrease / wallSamples:F3}");
            Assert.That(tableIncrease / tableSamples, Is.GreaterThan(.03f), "The white lamp must visibly illuminate the table beyond bulb emission.");
            Assert.That(wallIncrease / wallSamples, Is.GreaterThan(.02f), "The white lamp must visibly light the nearby space.");
            Assert.That((float)tableBrightened / tableSamples, Is.GreaterThan(.2f));
            Assert.That((float)wallBrightened / wallSamples, Is.GreaterThan(.2f));
        }

        private static void AssertFishPendantFactor(WarehouseFishLamp lamp, float expected, float fullIntensity)
        {
            Assert.That(lamp.CurrentFactor, Is.EqualTo(expected).Within(.0001f));
            Assert.That(lamp.Light.intensity, Is.EqualTo(fullIntensity * expected).Within(.0001f));
            Renderer bulb = FishPendantRenderer(lamp.gameObject, "LampBulb__");
            var properties = new MaterialPropertyBlock();
            bulb.GetPropertyBlock(properties);
            Color emission = properties.GetColor("_EmissionColor");
            Assert.That(emission.r, Is.EqualTo(1.4f * expected).Within(.0001f));
            Assert.That(emission.g, Is.EqualTo(emission.r));
            Assert.That(emission.b, Is.EqualTo(emission.r));
            Assert.That(lamp.Source.clip, Is.Not.Null);
        }

        private static void AdvanceFishPendantToFactor(WarehouseFishLamp lamp, float factor)
        {
            for (int step = 0; step < 650 && !Mathf.Approximately(lamp.CurrentFactor, factor); step++) lamp.Advance(.01f);
            Assert.That(lamp.CurrentFactor, Is.EqualTo(factor).Within(.0001f));
        }

        private static void SetFishPendantCamera(CombatTestRoot root, Camera camera)
        {
            root.CameraFollow.enabled = false;
            root.Hero.ResetActor(new Vector3(-2.1f, WarehouseArenaPlan.TopHeight + PlayerFactory.GroundedRootOffset, 7.5f), Vector3.forward);
            Vector3 eye = new Vector3(2.3f, 8.1f, 7.45f);
            Vector3 target = root.WarehousePlan.FishStationPosition + Vector3.up * 2.2f;
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            camera.fieldOfView = 65f;
        }

        private static void AssertFishRoomLamps(CombatTestRoot root)
        {
            int fixtures = 0, on = 0, off = 0, sources = 0, expectedOnCount = 0;
            Material onMaterial = null, offMaterial = null;
            foreach (Transform child in root.GetComponentsInChildren<Transform>())
            {
                if (child.name != "Warehouse EmergencyRoomLamp") continue;
                fixtures++;
                WarehouseArenaAssetProvider.ValidatePlacedModel("EmergencyRoomLamp", child.gameObject);
                MeshRenderer lens = null;
                foreach (MeshRenderer mesh in child.GetComponentsInChildren<MeshRenderer>())
                    if (mesh.name.EndsWith("__SignalRed", StringComparison.Ordinal)) lens = mesh;
                Assert.That(lens, Is.Not.Null);
                WarehouseRoomIndicator indicator = child.GetComponent<WarehouseRoomIndicator>();
                var lensProperties = new MaterialPropertyBlock();
                lens.GetPropertyBlock(lensProperties);
                Color emission = indicator != null ? lensProperties.GetColor("_EmissionColor") : lens.sharedMaterial.GetColor("_EmissionColor");
                bool expectedOn = false, found = false;
                foreach (WarehouseArenaLamp placement in root.WarehousePlan.Lamps)
                    if (placement.Kind == "EmergencyRoomLamp" && Vector3.Distance(placement.Position, child.position) < .001f)
                    { expectedOn = placement.Enabled && (indicator == null || !GameSessionState.IsWorldItemCollected(indicator.SourceId) ||
                        indicator.FadeProgress < 1f); found = true; break; }
                Assert.That(found, Is.True);
                if (expectedOn) expectedOnCount++;
                Assert.That(emission.maxColorComponent > .01f, Is.EqualTo(expectedOn),
                    "The glowing lens must identify this floor's chosen refrigerator room.");
                if (emission.maxColorComponent > .01f) on++; else off++;
                if (indicator != null)
                { if (onMaterial == null) onMaterial = lens.sharedMaterial; else Assert.That(lens.sharedMaterial, Is.SameAs(onMaterial)); }
                else
                { if (offMaterial == null) offMaterial = lens.sharedMaterial; else Assert.That(lens.sharedMaterial, Is.SameAs(offMaterial)); }
            }
            foreach (Light light in root.GetComponentsInChildren<Light>())
                if (light.name == "Warehouse Room Indicator Light")
                {
                    sources++;
                    WarehouseRoomIndicator indicator = light.GetComponentInParent<WarehouseRoomIndicator>();
                    Assert.That(indicator, Is.Not.Null);
                    Assert.That(light.isActiveAndEnabled, Is.EqualTo(indicator.CurrentFactor > 0f));
                    AssertWarehouseIndicatorShadowProfile(light);
                }
            Assert.That(fixtures, Is.EqualTo(9)); Assert.That(on, Is.EqualTo(expectedOnCount)); Assert.That(off, Is.EqualTo(9 - expectedOnCount));
            Assert.That(sources, Is.EqualTo(3));
        }

        private static IEnumerator AssertDelayedRefrigeratorLight(CombatTestRoot root, WarehouseRefrigerator fridge,
            InputTestFixture input, Mouse mouse)
        {
            yield return null;
            Vector3 target = fridge.View.InteriorRoot.TransformPoint(new Vector3(.30f, .10f, 0f));
            Vector3 screen = root.CameraFollow.Camera.WorldToScreenPoint(target);
            input.Set(mouse.position, new Vector2(screen.x, screen.y));
            InputSystem.Update();
            Quaternion before = root.Flashlight.SpotLight.transform.rotation;
            root.Flashlight.AdvanceInspectionAim(0f);
            Assert.That(Vector3.Distance(root.Flashlight.InspectionAimTarget, target), Is.LessThan(.025f));
            Assert.That(Quaternion.Angle(before, root.Flashlight.SpotLight.transform.rotation), Is.LessThan(.01f),
                "A new cursor target cannot teleport the lamp direction.");
            root.Flashlight.AdvanceInspectionAim(.02f);
            float firstStep = Quaternion.Angle(before, root.Flashlight.SpotLight.transform.rotation);
            Assert.That(firstStep, Is.GreaterThan(.001f));
            for (int step = 0; step < 40; step++) root.Flashlight.AdvanceInspectionAim(1f / 60f);
            Assert.That(Quaternion.Angle(before, root.Flashlight.SpotLight.transform.rotation), Is.GreaterThan(firstStep + .05f),
                "The beam must lag and then approach the new aiming point.");
            Vector3 settledTarget = root.Flashlight.InspectionAimTarget;
            Quaternion settledRotation = root.Flashlight.SpotLight.transform.rotation;
            using (GameTimeScaleRuntime.AcquirePause())
            {
                input.Set(mouse.position, new Vector2(screen.x - 100f, screen.y));
                InputSystem.Update();
                root.Flashlight.AdvanceInspectionAim(.2f);
                Assert.That(root.Flashlight.InspectionAimTarget, Is.EqualTo(settledTarget));
                Assert.That(Quaternion.Angle(settledRotation, root.Flashlight.SpotLight.transform.rotation), Is.LessThan(.01f));
            }
        }

        private static void AssertRoomIndicatorSpill(CombatTestRoot root, Camera camera, Vector3 fixturePosition)
        {
            Light indicator = null;
            foreach (Light light in root.GetComponentsInChildren<Light>())
                if (light.name == "Warehouse Room Indicator Light" && Vector3.Distance(light.transform.position, fixturePosition) < .6f)
                    indicator = light;
            Assert.That(indicator, Is.Not.Null);
            Color32[] lit = ReadIndicatorFrame(camera);
            Color32[] dark;
            try
            {
                indicator.enabled = false;
                dark = ReadIndicatorFrame(camera);
            }
            finally { indicator.enabled = true; }
            int redSurfacePixels = 0;
            for (int index = 0; index < lit.Length; index++)
            {
                int red = lit[index].r - dark[index].r;
                int other = Mathf.Max(lit[index].g - dark[index].g, lit[index].b - dark[index].b);
                if (red >= 18 && red > other * 2) redSurfacePixels++;
            }
            Assert.That(redSurfacePixels, Is.GreaterThan(150),
                "The real room lamp must visibly cast red light on entrance surfaces, beyond its unchanged emissive lens.");
        }

        private static Color32[] ReadIndicatorFrame(Camera camera)
        {
            var target = new RenderTexture(160, 90, 24);
            var pixels = new Texture2D(160, 90, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0f, 0f, 160f, 90f), 0, 0);
                pixels.Apply();
                return pixels.GetPixels32();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(pixels);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static IEnumerator CaptureFishUi(string name)
        {
            if (Application.isBatchMode) yield break;
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            for (int frame = 0; frame < 3; frame++) yield return null;
            Assert.That(File.Exists(path), Is.True, "The real Game View inspection/inventory UI must be captured.");
        }
    }
}
