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
                var ids = new[] { InventoryItemId.RottenFishTailCan, InventoryItemId.RottenFishBellyCan, InventoryItemId.RottenFishHeadCan };
                var kinds = new[] { HomeRefrigeratorItemKind.RottenFishTailCan, HomeRefrigeratorItemKind.RottenFishBellyCan,
                    HomeRefrigeratorItemKind.RottenFishHeadCan };
                for (int floor = 0; floor < 3; floor++)
                {
                    WarehouseRefrigerator fridge = fridges[floor];
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
                    yield return null;
                    foreach (Volume volume in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
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
                    if (floor == 0)
                    {
                        Assert.That(GameSessionState.TryAddInventoryItem(ids[floor]), Is.True);
                        Assert.That(inspection.InvokeAction(HomeRefrigeratorItemAction.Take), Is.True);
                        Assert.That(inspection.IsInspecting, Is.True, "Full item capacity must keep the can available for inspection.");
                        Assert.That(inspection.FeedbackKey, Is.EqualTo(WorldItemFoundScreen.NoRoomFeedbackKey));
                        Assert.That(fridge.View.Items.Count, Is.EqualTo(1));
                        Assert.That(GameSessionState.IsWorldItemCollected(fridge.SourceId), Is.False);
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
                    Assert.That(inspection.InvokeAction(HomeRefrigeratorItemAction.Take), Is.False);
                    Assert.That(GameSessionState.GetInventoryItemCount(ids[floor]), Is.EqualTo(1));
                    Assert.That(fridge.Interaction.RequestClose(), Is.True);
                    fridge.Interaction.AdvanceInteraction(HomeRefrigeratorInteractionTimeline.CloseSequenceDurationSeconds + .1f);
                    yield return null;
                    Assert.That(fridge.Interaction.OwnsInteraction, Is.False);
                    Assert.That(root.Flashlight.HasInspectionAim, Is.False);
                    Assert.That(root.CameraFollow.FixedPoseActive, Is.False, "Closing must restore the chase camera.");
                    Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
                    Assert.That(root.Player.Motor.InputEnabled && root.Player.Interactor.InputEnabled, Is.True);
                }
                WarehouseArenaPlan retained = root.WarehousePlan;
                root.ResetRound();
                Assert.That(root.WarehousePlan, Is.SameAs(retained));
                foreach (WarehouseRefrigerator fridge in fridges) Assert.That(fridge.View.Items.Count, Is.Zero);
                Assert.That(root.Inventory.Open(), Is.True);
                Assert.That(root.Inventory.SelectItem(2), Is.True);
                Assert.That(root.Inventory.ExamineSelected(), Is.True);
                yield return null;
                yield return CaptureFishUi("warehouse-fish-inventory");
                Assert.That(root.Inventory.Close(), Is.True);
                yield return null;
                Assert.That(root.NewWarehouseLayout(5), Is.True);
                yield return null;
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

        private static void AssertFishRoomLamps(CombatTestRoot root)
        {
            int fixtures = 0, on = 0, off = 0, sources = 0;
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
                Color emission = lens.sharedMaterial.GetColor("_EmissionColor");
                bool expectedOn = false, found = false;
                foreach (WarehouseArenaLamp placement in root.WarehousePlan.Lamps)
                    if (placement.Kind == "EmergencyRoomLamp" && Vector3.Distance(placement.Position, child.position) < .001f)
                    { expectedOn = placement.Enabled; found = true; break; }
                Assert.That(found, Is.True);
                Assert.That(emission.maxColorComponent > .01f, Is.EqualTo(expectedOn),
                    "The glowing lens must identify this floor's chosen refrigerator room.");
                if (emission.maxColorComponent > .01f)
                { on++; if (onMaterial == null) onMaterial = lens.sharedMaterial; else Assert.That(lens.sharedMaterial, Is.SameAs(onMaterial)); }
                else
                { off++; if (offMaterial == null) offMaterial = lens.sharedMaterial; else Assert.That(lens.sharedMaterial, Is.SameAs(offMaterial)); }
            }
            foreach (Light light in root.GetComponentsInChildren<Light>())
                if (light.name == "Warehouse Room Indicator Light")
                { sources++; Assert.That(light.isActiveAndEnabled, Is.True); Assert.That(light.shadows, Is.EqualTo(LightShadows.None)); }
            Assert.That(fixtures, Is.EqualTo(9)); Assert.That(on, Is.EqualTo(3)); Assert.That(off, Is.EqualTo(6));
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
