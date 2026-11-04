using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class VodkaBottleAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.PlayerVodkaActionAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
            Type.GetType("BarPromenade.Editor.SupermarketProductAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    /// <summary>Real inventory/input/rig/physics integration, with reviewable bottle and contact frames.</summary>
    [PrebuildSetup(typeof(VodkaBottleAssetsSetup))]
    public sealed class VodkaBottleEquipmentPlayModeTests
    {
        private const int CaptureLayer = 28, TileSize = 640;
        private readonly object cursorOwner = new object();
        private InputTestFixture input;
        private Keyboard keyboard;
        private Mouse mouse;
        private GameObject fixture;
        private PlayerRuntime player;
        private Player3DCharacterPresentation presentation;
        private PlayerVodkaBottleController bottle;
        private Transform leftEye, rightEye;
        private Vector3 headLocalForward;
        private PlayerCrowbarCombatController crowbar;
        private InventoryController inventory;
        private Camera camera;
        private RenderTexture target;
        private Texture2D tile;
        private Mesh handScratch;
        private Scene previousScene, fixtureScene;
        private PlayerVodkaBottleController[] previousPlayers;
        private bool[] previousPlayerStates;
        private float previousDelta, previousScale;
        private bool previousFocus, previousFog;
        private AmbientMode previousAmbientMode;
        private Color previousAmbient;
        private Light previousSun;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // A preceding scene test can leave its production hero loaded.
            // Only this fixture's hero may read the synthetic input or mutate
            // the shared session bottle; restore the prior actors afterward.
            previousPlayers = Object.FindObjectsByType<PlayerVodkaBottleController>(FindObjectsInactive.Include);
            previousPlayerStates = new bool[previousPlayers.Length];
            for (int i = 0; i < previousPlayers.Length; i++)
            {
                previousPlayerStates[i] = previousPlayers[i].gameObject.activeSelf;
                previousPlayers[i].gameObject.SetActive(false);
            }
            previousDelta = Time.captureDeltaTime; previousScale = Time.timeScale;
            previousFocus = GameInput.MovementFocused;
            previousFog = RenderSettings.fog; previousAmbientMode = RenderSettings.ambientMode;
            previousAmbient = RenderSettings.ambientLight; previousSun = RenderSettings.sun;
            Time.captureDeltaTime = 1f / 60f; Time.timeScale = 1f;
            input = new InputTestFixture(); input.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
            GameSessionState.BeginNewGame(); GameInput.HandleApplicationFocus(true);
            GameCursor.Capture(cursorOwner, CursorLockMode.Locked, false, 0);
            previousScene = SceneManager.GetActiveScene();
            fixtureScene = SceneManager.CreateScene("VodkaBottleEquipmentFixture");
            SceneManager.SetActiveScene(fixtureScene);
            fixture = new GameObject("Vodka bottle equipment fixture");
            Vector3 origin = new Vector3(2200f, 0f, 2200f);
            MakeFloor("Concrete bottle floor", origin + Vector3.down * .1f, new Vector3(40f, .2f, 40f), FootstepGroundKind.Concrete);
            var cameraObject = new GameObject("Vodka bottle inspection camera");
            cameraObject.transform.SetParent(fixture.transform, false);
            camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            if (Object.FindAnyObjectByType<AudioListener>() == null) cameraObject.AddComponent<AudioListener>();
            camera.orthographic = true; camera.orthographicSize = 1.3f; camera.aspect = 1f;
            camera.nearClipPlane = .03f; camera.farClipPlane = 30f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.22f, .24f, .27f);
            camera.cullingMask = 1 << CaptureLayer; camera.allowHDR = camera.allowMSAA = false;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false; data.volumeLayerMask = 0;
            var lightObject = new GameObject("Vodka inspection key");
            lightObject.transform.SetParent(fixture.transform, false);
            Light key = lightObject.AddComponent<Light>(); key.type = LightType.Directional;
            key.intensity = 1.7f; key.color = new Color(1f, .9f, .8f);
            key.cullingMask = 1 << CaptureLayer; key.shadows = LightShadows.Hard;
            lightObject.transform.rotation = Quaternion.Euler(48f, 145f, 0f);
            RenderSettings.sun = key; RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.5f, .53f, .57f);
            player = PlayerFactory.Create(fixture.transform, origin + Vector3.up * PlayerFactory.GroundedRootOffset, camera, null, null);
            presentation = (Player3DCharacterPresentation)player.Visual;
            int leftEyeCount = 0, rightEyeCount = 0;
            foreach (Transform bone in presentation.Registry.Anchors.Head.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == "face.eye.L") { leftEye = bone; leftEyeCount++; }
                if (bone.name == "face.eye.R") { rightEye = bone; rightEyeCount++; }
            }
            Assert.That(leftEyeCount, Is.EqualTo(1)); Assert.That(rightEyeCount, Is.EqualTo(1));
            Assert.That(leftEye, Is.Not.Null); Assert.That(rightEye, Is.Not.Null);
            Assert.That(leftEye.IsChildOf(presentation.Registry.Anchors.Head), Is.True);
            Assert.That(rightEye.IsChildOf(presentation.Registry.Anchors.Head), Is.True);
            // Imported bone axes are not gameplay forward. Capture the same
            // neutral anatomical frame used by the production head probe.
            headLocalForward = presentation.Registry.Anchors.Head.InverseTransformDirection(player.GameObject.transform.forward);
            bottle = player.GameObject.GetComponent<PlayerVodkaBottleController>();
            crowbar = player.GameObject.GetComponent<PlayerCrowbarCombatController>();
            Assert.That(bottle, Is.Not.Null);
            var follow = cameraObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.GameObject.transform, true); follow.enabled = false;
            var ui = new GameObject("Vodka inventory"); ui.transform.SetParent(fixture.transform, false);
            inventory = ui.AddComponent<InventoryController>();
            inventory.Initialize(player, follow, ui.AddComponent<IntoxicationHudView>());
            target = new RenderTexture(TileSize, TileSize, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
            tile = new Texture2D(TileSize, TileSize, TextureFormat.RGB24, false);
            handScratch = new Mesh { name = "Vodka actual hand geometry" };
            Assert.That(GameSessionState.TryCollectWorldItem("test-vodka-bottle", InventoryItemId.VodkaBottle), Is.True);
            Assert.That(GameSessionState.TryCollectWorldItem("test-vodka-crowbar", InventoryItemId.CombatCrowbar), Is.True);
            Physics.SyncTransforms();
            yield return Await(() => crowbar.Actor != null, "Ordinary combat actor did not initialize.");
            GameCursor.Capture(cursorOwner, CursorLockMode.Locked, false, 0);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (inventory != null) inventory.enabled = false;
            GameCursor.Release(cursorOwner);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            input?.TearDown();
            if (fixture != null) Object.Destroy(fixture);
            if (target != null) { target.Release(); Object.Destroy(target); }
            if (tile != null) Object.Destroy(tile);
            if (handScratch != null) Object.Destroy(handScratch);
            GameSessionState.BeginNewGame(); GameInput.HandleApplicationFocus(previousFocus);
            Time.captureDeltaTime = previousDelta; Time.timeScale = previousScale;
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (fixtureScene.IsValid() && fixtureScene.isLoaded) yield return SceneManager.UnloadSceneAsync(fixtureScene);
            RenderSettings.fog = previousFog; RenderSettings.ambientMode = previousAmbientMode;
            RenderSettings.ambientLight = previousAmbient; RenderSettings.sun = previousSun;
            for (int i = 0; i < previousPlayers.Length; i++)
                if (previousPlayers[i] != null) previousPlayers[i].gameObject.SetActive(previousPlayerStates[i]);
            previousPlayers = null; previousPlayerStates = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator DrinkContainers_PurchaseSipSwitch_PreserveFillOpeningAndContacts()
        {
            DrinkId[] balancedDrinks = { DrinkId.LightBeer, DrinkId.DarkBeer, DrinkId.WhiteWine,
                DrinkId.RedWine, DrinkId.CognacVs, DrinkId.CognacVsop };
            int[] balancedDoses = { 10, 12, 17, 20, 22, 24 };
            for (int index = 0; index < balancedDrinks.Length; index++)
            {
                GameSessionState.BeginNewGame();
                GameSessionState.UpdateNeeds(0, 50);
                DrinkPurchaseResult bar = GameSessionState.TryOrderDrink(balancedDrinks[index], out DrinkOrderToken order);
                Assert.That(bar.Succeeded, Is.True);
                Assert.That(GameSessionState.IntoxicationLevel, Is.Zero, "Ordering alone must not apply the increased dose.");
                for (int portion = 1; portion <= order.PortionCount; portion++)
                    Assert.That(GameSessionState.TryConsumeOrderedDrinkPortion(order, portion), Is.True);
                Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(balancedDoses[index]));
                Assert.That(GameSessionState.HeroCorruptionDose, Is.EqualTo(balancedDoses[index]));
                Assert.That(GameSessionState.StressLevel, Is.EqualTo(50 - DrinkRules.GetStressRelief(balancedDrinks[index])));
                Assert.That(GameSessionState.TryConsumeOrderedDrink(order), Is.False, "A completed bar order cannot apply its dose twice.");
            }
            GameSessionState.BeginNewGame();
            foreach (InventoryItemId item in new[] { InventoryItemId.BeerCan, InventoryItemId.WineBottle, InventoryItemId.VodkaBottle })
            {
                DrinkContainerProfile profile = DrinkContainerCatalog.Get(item);
                InventoryConsumableDefinition definition = InventoryConsumableCatalog.Get(item);
                int expectedDose = PlayerNeedsRules.ScaleRelief(DrinkRules.GetIntoxicationGain(definition.DrinkId), definition.Servings);
                var doses = new InventoryState();
                Assert.That(doses.TryAdd(item, 1), Is.True); Assert.That(doses.TrySetEquipped(item, true), Is.True);
                int doseId = doses.DrinkContainerId(item), totalDose = 0, totalStress = 0, totalServings = 0;
                DrinkPurchaseResult barPortion = DrinkPurchaseRules.Evaluate(definition.DrinkId, 999, 0, DrinkId.None, 0);
                for (int sip = 0; sip < profile.SipCount; sip++)
                {
                    Assert.That(doses.TryDrinkContainerSip(item, doseId, out var amount), Is.True);
                    totalDose += amount.AlcoholDose; totalStress += amount.StressRelief; totalServings += amount.Servings;
                    // At each complete serving boundary the same amount of
                    // drink has the same dose at the bar and from its package.
                    int scaledPortions = (sip + 1) * (int)definition.Servings;
                    if (scaledPortions % profile.SipCount == 0)
                        Assert.That(totalDose, Is.EqualTo(barPortion.ActualIntoxicationDelta * scaledPortions / profile.SipCount));
                }
                Assert.That(totalDose, Is.EqualTo(expectedDose)); Assert.That(totalStress, Is.EqualTo(definition.StressRelief));
                Assert.That(totalServings, Is.EqualTo((int)definition.Servings)); Assert.That(doses.GetCount(item), Is.Zero);
                Assert.That(doses.TryDrinkContainerSip(item, doseId, out _), Is.False, "An empty package cannot commit again.");

                int initialCount = GameSessionState.GetInventoryItemCount(item);
                int initialCash = GameSessionState.CashBalance;
                Assert.That(GameSessionState.TryPurchaseWorldItem("test.drink." + item, item).Succeeded, Is.True);
                Assert.That(GameSessionState.GetInventoryItemCount(item), Is.EqualTo(initialCount + 1));
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(initialCash - SupermarketProductCatalog.GetOffer(item).Price));
                yield return InventoryToggle(true, itemId: item);
                yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Opening && bottle.PhaseProgress >= .62f,
                    "A newly bought sealed drink must visibly open.");
                yield return new WaitForEndOfFrame();
                Capture("drink-" + item + "-opening", true, bottle.Bottle.Cap.position, new Vector3(1f,1f,1f), .18f);
                yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Carrying && bottle.ItemId == item,
                    "The selected drink never reached the original hero hand.");
                Assert.That(bottle.Bottle.ItemId, Is.EqualTo(item));
                Assert.That(bottle.Bottle.IsOpened, Is.True);
                int id = GameSessionState.DrinkContainerId(item);
                Assert.That(GameSessionState.DrinkRemainingMl(item), Is.EqualTo(profile.FullMl));
                NpcHandPose hands = presentation.Registry.GetComponent<NpcHandPose>();
                Assert.That(hands.GetGripKind(false), Is.EqualTo(profile.GripKind));
                Assert.That(Vector3.Distance(hands.AnatomicalCylinderCentre(false), bottle.Bottle.Grip.position), Is.LessThan(.015f));
                if (profile.IsCan)
                {
                    Assert.That(bottle.Bottle.Cap.gameObject.activeSelf, Is.True, "The ring remains attached after opening.");
                    SupermarketProductAssetRegistry registry = bottle.Bottle.GetComponentInChildren<SupermarketProductAssetRegistry>();
                    foreach (SupermarketProductPartBinding part in registry.Parts)
                        if (part.Role == "can_body") Assert.That(part.Renderer.sharedMaterial.renderQueue, Is.LessThan(2500), "Aluminium stays opaque.");
                }
                Capture("drink-" + item + "-carry", true);
                int sips = bottle.CompletedSips;
                input.Press(mouse.leftButton, queueEventOnly: true); yield return null;
                input.Release(mouse.leftButton, queueEventOnly: true);
                yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Sipping, "Drink failed to reach the mouth.");
                yield return new WaitForEndOfFrame();
                Assert.That(bottle.MouthContactError, Is.LessThan(.015f));
                Capture("drink-" + item + "-mouth", true, bottle.Bottle.Mouth.position, new Vector3(1f,.25f,1f), .20f);
                yield return Await(() => bottle.CompletedSips == sips + 1 && bottle.Phase == PlayerVodkaBottleController.BottlePhase.Carrying,
                    "One click must commit exactly one swallow.");
                int remaining = GameSessionState.DrinkRemainingMl(item);
                Assert.That(remaining, Is.EqualTo(profile.FullMl - profile.FullMl / profile.SipCount));
                Assert.That(GameSessionState.TryDrinkContainerSip(item, id, profile.FullMl, out _), Is.False, "A stale swallow cannot debit twice.");
                yield return InventoryToggle(false, itemId: item);
                Assert.That(GameSessionState.DrinkRemainingMl(item), Is.EqualTo(remaining));
                Assert.That(GameSessionState.TryDrinkContainerSip(item, id, remaining, out _), Is.False, "An unequipped drink cannot consume.");
                if (profile.IsCan) Assert.That(GameSessionState.DrinkOpened(item), Is.True);
                yield return InventoryToggle(true, itemId: item);
                yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Carrying, "Re-equipped drink lost its carry state.");
                Assert.That(GameSessionState.DrinkContainerId(item), Is.EqualTo(id));
                Assert.That(bottle.Bottle.Remaining, Is.EqualTo((float)remaining / profile.FullMl).Within(.001f));
                bottle.enabled = false; yield return null; bottle.enabled = true;
                yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Carrying, "Presentation cleanup lost the session container.");
                Assert.That(GameSessionState.DrinkRemainingMl(item), Is.EqualTo(remaining));
                Assert.That(GameSessionState.DrinkContainerId(item), Is.EqualTo(id));
                yield return InventoryToggle(false, itemId: item);
            }
            Assert.That(player.GameObject.GetComponents<PlayerVodkaBottleController>(), Has.Length.EqualTo(1));
            FootstepGroundKind[] canSurfaces = { FootstepGroundKind.Concrete, FootstepGroundKind.Metal,
                FootstepGroundKind.Wood, FootstepGroundKind.Soil };
            RetroSfxId[] surfaceCues = { RetroSfxId.FootstepConcrete, RetroSfxId.FootstepMetal,
                RetroSfxId.FootstepWood, RetroSfxId.FootstepSoil };
            for (int index = 0; index < canSurfaces.Length; index++)
            {
                FootstepGroundKind kind = canSurfaces[index];
                Assert.That(ThrownVodkaBottle.ShouldBreak(InventoryItemId.BeerCan, kind, 100f), Is.False);
                Vector3 drop = new Vector3(2204f + index * 2f, 0f, 2200f);
                MakeFloor("Can drop " + kind, drop + Vector3.up * .15f, new Vector3(1.5f, .3f, 1.5f), kind);
                VodkaBottleView can = VodkaBottleView.Create(fixture.transform, InventoryItemId.BeerCan);
                can.transform.position = drop + Vector3.up * 2f;
                ThrownVodkaBottle released = ThrownVodkaBottle.Release(can, Vector3.down * 8f, Vector3.right * 2f,
                    player.GameObject.transform);
                Physics.SyncTransforms();
                yield return Await(() => released.ImpactCount > 0, "Can failed to contact " + kind);
                Assert.That(released.IsBroken, Is.False, "Aluminium cannot become glass shards.");
                Assert.That(released.ActiveShardCount, Is.Zero);
                Assert.That(released.GetComponent<WarehouseFishLampBurstAudio>(), Is.Null, "A can must not own the glass fracture audio.");
                Assert.That(released.LastCanImpactSurface, Is.EqualTo(kind));
                Assert.That(released.LastCanImpactCue, Is.EqualTo(surfaceCues[index]));
                Assert.That(released.LastCanMetalVolume, Is.GreaterThan(0f));
                Object.Destroy(released.gameObject); yield return null;
            }
            Assert.That(ThrownVodkaBottle.CanMetalImpactVolume(FootstepGroundKind.Soil),
                Is.LessThan(ThrownVodkaBottle.CanMetalImpactVolume(FootstepGroundKind.Wood)));
            Assert.That(ThrownVodkaBottle.CanMetalImpactVolume(FootstepGroundKind.Wood),
                Is.LessThan(ThrownVodkaBottle.CanMetalImpactVolume(FootstepGroundKind.Metal)));
        }

        [UnityTest]
        public IEnumerator InventoryWalkingSipPersistenceAndLastSipThrow()
        {
            AssertLiquidAndDoseContracts();
            input.Press(keyboard.digit2Key, queueEventOnly: true);
            yield return Await(() => crowbar.Actor.HasWeaponInHand && !crowbar.Actor.IsSwitchingWeapon,
                "The initial weapon hotkey did not visibly draw the acquired crowbar.");
            input.Release(keyboard.digit2Key, queueEventOnly: true);
            yield return Frames(3);
            Capture("initial-crowbar-held", false);
            yield return InventoryToggle(true, holdMouse: true);
            bool sawWeaponStow = crowbar.Actor.IsSwitchingWeapon;
            bool hadConcealedNeck = false, checkedDrawTransfer = false;
            Vector3 lastConcealedNeck = Vector3.zero;
            int drawFrame = 0, openingFrame = 0;
            float[] drawStops = { .025f, .075f, .15f, .23f, .29f, .48f, .65f, .90f };
            float[] openingStops = { .12f, .32f, .46f, .60f, .75f, .86f, .98f };
            for (int frame = 0; frame < 360 && bottle.Phase != PlayerVodkaBottleController.BottlePhase.Carrying; frame++)
            {
                yield return null;
                sawWeaponStow |= crowbar.Actor.IsSwitchingWeapon;
                Assert.That(crowbar.Actor.HasWeaponInHand && bottle.OccupiesHand, Is.False,
                    "Bottle draw must wait for the real weapon handoff, rather than overlapping two props.");
                if (bottle.Phase == PlayerVodkaBottleController.BottlePhase.Drawing ||
                    bottle.Phase == PlayerVodkaBottleController.BottlePhase.Opening)
                {
                    presentation.ReapplyLatePresentationPose();
                    bottle.ReapplyPresentation();
                    AssertArmsOutsideCore();
                    if (bottle.Phase == PlayerVodkaBottleController.BottlePhase.Drawing)
                    {
                        Assert.That(bottle.IsBottleConcealed,
                            Is.EqualTo(bottle.PhaseProgress < PlayerVodkaActions.DrawTransferProgress));
                        if (bottle.IsBottleConcealed)
                        { lastConcealedNeck = bottle.Bottle.Grip.position; hadConcealedNeck = true; }
                        else if (!checkedDrawTransfer)
                        {
                            Assert.That(hadConcealedNeck, Is.True);
                            Assert.That(Vector3.Distance(lastConcealedNeck, bottle.Bottle.Grip.position), Is.LessThan(.015f),
                                "The bottle must leave its coat dock at the authored hand contact, without a hidden prop jump.");
                            checkedDrawTransfer = true;
                        }
                        foreach (Renderer renderer in bottle.Bottle.GetComponentsInChildren<Renderer>(true))
                            Assert.That(renderer.forceRenderingOff, Is.EqualTo(bottle.IsBottleConcealed));
                        if (drawFrame < drawStops.Length && bottle.PhaseProgress >= drawStops[drawFrame])
                        {
                            Capture("draw-coat-" + drawFrame, false, null, new Vector3(.7f, .15f, 2f));
                            if (drawFrame <= 4)
                                Capture("draw-right-side-" + drawFrame, false,
                                    player.GameObject.transform.position + Vector3.up * 1.25f, Vector3.right, .6f);
                            drawFrame++;
                        }
                    }
                    else if (openingFrame < openingStops.Length && bottle.PhaseProgress >= openingStops[openingFrame])
                        Capture("open-in-front-" + openingFrame++, false, null, new Vector3(.7f, .15f, 2f));
                }
            }
            Assert.That(drawFrame, Is.EqualTo(drawStops.Length));
            Assert.That(checkedDrawTransfer, Is.True);
            Assert.That(openingFrame, Is.EqualTo(openingStops.Length));
            Assert.That(sawWeaponStow, Is.True, "Equipping the bottle must visibly stow the already held weapon.");
            Assert.That(bottle.Phase, Is.EqualTo(PlayerVodkaBottleController.BottlePhase.Carrying), "Bottle did not finish its visible draw.");
            int id = GameSessionState.VodkaBottleId;
            Assert.That(GameSessionState.VodkaBottleRemainingMl, Is.EqualTo(500));
            Assert.That(bottle.CompletedSips, Is.Zero, "A menu-owned held click must not start drinking.");
            Assert.That(crowbar.Actor.HasWeaponInHand, Is.False);
            Assert.That(bottle.Bottle.IsOpened, Is.True, "The cap opens only after the bottle has cleared the coat.");
            Capture("00-full-carried", false);
            Capture("01-full-close", true);
            Capture("01-thumb-and-four-fingers-side", true, null, Vector3.right);
            Capture("01-thumb-and-four-fingers-rear", true, null, new Vector3(1f, .15f, -2f));
            Capture("01-thumb-and-four-fingers-front", true, null, new Vector3(.7f, .15f, 2f));
            AssertBottleGrip();
            input.Release(mouse.leftButton, queueEventOnly: true);
            yield return Frames(3);
            Vector3 start = player.GameObject.transform.position;
            input.Press(keyboard.wKey, queueEventOnly: true);
            yield return Frames(24);
            input.Press(mouse.leftButton, queueEventOnly: true);
            yield return null;
            input.Release(mouse.leftButton, queueEventOnly: true);
            yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Sipping, "A short LMB click did not start a moving sip.");
            yield return Await(() => bottle.CompletedSips == 1, "The swallow did not commit exactly once.");
            yield return Frames(3);
            AssertSharedGulpPlayed();
            Capture("02-moving-sip", false);
            Capture("03-mouth-contact", true);
            Capture("03-thumb-and-four-fingers-sip-side", true, null, Vector3.right);
            Capture("03-thumb-and-four-fingers-sip-rear", true, null, new Vector3(1f, .15f, -2f));
            AssertBottleGrip();
            Assert.That(bottle.MouthContactError, Is.LessThan(.012f));
            Vector2 fullDrinkPose = MeasureDrinkPose();
            Vector2 middleDrinkPose = fullDrinkPose;
            Assert.That(Vector3.Distance(start, player.GameObject.transform.position), Is.GreaterThan(.5f), "Sip must preserve walking.");
            Assert.That(player.Motor.InputEnabled, Is.True);
            yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Carrying, "Sip did not return to carry.");
            yield return Frames(10);
            Assert.That(bottle.CompletedSips, Is.EqualTo(1), "A released short click must finish exactly one sip.");
            Assert.That(GameSessionState.VodkaBottleRemainingMl, Is.EqualTo(467));
            Assert.That(crowbar.Actor.HasWeaponInHand || crowbar.Actor.State.IsAttacking || crowbar.HasPendingAction,
                Is.False, "The drinking click cannot auto-draw or strike with the crowbar.");
            input.Release(mouse.leftButton, queueEventOnly: true); input.Release(keyboard.wKey, queueEventOnly: true);
            yield return Frames(3);
            input.Press(keyboard.digit2Key, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.digit2Key, queueEventOnly: true);
            int closeFrame = 0;
            bool sawClosedBottleBeforeStow = false;
            float[] closeStops = { .12f, .32f, .50f, .75f, .98f };
            for (int frame = 0; frame < 360 && (bottle.OccupiesHand || !crowbar.Actor.HasWeaponInHand || crowbar.Actor.IsSwitchingWeapon); frame++)
            {
                yield return null;
                if (bottle.Phase == PlayerVodkaBottleController.BottlePhase.Closing)
                {
                    AssertArmsOutsideCore();
                    if (closeFrame < closeStops.Length && bottle.PhaseProgress >= closeStops[closeFrame])
                    {
                        Capture("close-before-weapon-" + closeFrame++, false, null, new Vector3(.7f, .15f, 2f));
                        if (closeFrame == 1) AssertCapInLeftHand();
                    }
                }
                if (bottle.Phase == PlayerVodkaBottleController.BottlePhase.Stowing)
                {
                    AssertClosedBottleBeforeStow(); sawClosedBottleBeforeStow = true;
                    AssertArmsOutsideCore();
                }
                Assert.That(crowbar.Actor.HasWeaponInHand && bottle.OccupiesHand, Is.False,
                    "The weapon must wait until the partial bottle has left the hand.");
            }
            Assert.That(closeFrame, Is.EqualTo(closeStops.Length), "The cap must visibly return before the weapon handoff.");
            Assert.That(sawClosedBottleBeforeStow, Is.True);
            Assert.That(bottle.Phase, Is.EqualTo(PlayerVodkaBottleController.BottlePhase.Hidden));
            Assert.That(crowbar.Actor.HasWeaponInHand && !crowbar.Actor.IsSwitchingWeapon, Is.True,
                "The weapon hotkey did not complete its bottle-to-crowbar handoff.");
            Assert.That(GameSessionState.VodkaBottleRemainingMl, Is.EqualTo(467));
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.VodkaBottle), Is.EqualTo(1));
            Capture("partial-stowed-weapon-held", false);
            yield return InventoryToggle(true);
            yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Carrying, "Partial bottle did not return after weapon stow.");
            Assert.That(GameSessionState.VodkaBottleId, Is.EqualTo(id));
            Assert.That(GameSessionState.VodkaBottleRemainingMl, Is.EqualTo(467));
            Capture("04-partial-carried", true);

            // Pause cannot advance a swallow, and disabling before its commit
            // must release the rig without consuming or changing bottle identity.
            yield return Frames(3);
            input.Press(mouse.leftButton, queueEventOnly: true);
            yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Sipping, "Second sip did not start.");
            using (GameTimeScaleRuntime.AcquirePause())
            {
                yield return Frames(8);
                Assert.That(GameSessionState.VodkaBottleRemainingMl, Is.EqualTo(467));
            }
            bottle.enabled = false;
            yield return null;
            Assert.That(GameSessionState.VodkaBottleRemainingMl, Is.EqualTo(467));
            Assert.That(presentation.OwnsClip(bottle) || presentation.HasCarryPose, Is.False);
            input.Release(mouse.leftButton, queueEventOnly: true);
            bottle.enabled = true;
            yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Carrying, "Cancelled bottle did not restore.");
            Assert.That(GameSessionState.VodkaBottleId, Is.EqualTo(id));
            yield return InventoryToggle(false);
            yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Hidden, "Partial bottle did not stow before preparing final dose.");
            yield return InventoryToggle(true);
            yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Carrying, "Partial bottle did not draw for continuous drinking.");
            // Releasing finishes one swallow and lowers once. The next hold
            // retains the mouth contact through every subsequent loop seam.
            Time.captureDeltaTime = .05f;
            yield return Frames(3);
            input.Press(mouse.leftButton, queueEventOnly: true);
            double previousHeldCommit = -1d;
            for (int sip = 2; sip <= VodkaBottleState.FullBottleSipCount; sip++)
            {
                int expected = sip;
                if (sip <= 2) yield return Await(() => bottle.CompletedSips >= expected, "Held LMB did not advance to sip " + expected);
                else yield return AwaitHeldSip(expected, allowRaise: sip == 3);
                Assert.That(bottle.CompletedSips, Is.EqualTo(expected), "One cycle can commit only one sip.");
                if (sip >= 3)
                {
                    double committedAt = Time.timeAsDouble;
                    if (previousHeldCommit >= 0d)
                        Assert.That(committedAt - previousHeldCommit,
                            Is.InRange(1d - .0001d, 1d + 2d * Time.deltaTime + .0001d),
                            "Continuous drinking must swallow about once a second, including bounded terminal-frame overhead.");
                    previousHeldCommit = committedAt;
                }
                Assert.That(GameSessionState.VodkaBottleRemainingMl,
                    Is.EqualTo(VodkaBottleState.FullMl - expected * VodkaBottleState.FullMl / VodkaBottleState.FullBottleSipCount));
                if (sip == 8 || sip == VodkaBottleState.FullBottleSipCount)
                {
                    Vector2 pose = MeasureDrinkPose();
                    Vector2 previous = sip == 8 ? fullDrinkPose : middleDrinkPose;
                    Assert.That(pose.x, Is.GreaterThan(previous.x + 12f), "Draining the bottle must visibly raise its base.");
                    Assert.That(pose.y, Is.GreaterThan(previous.y + 10f), "The actual head must lean back further as the bottle drains.");
                    if (sip == 8) middleDrinkPose = pose;
                    Capture("drink-fill-" + (sip == 8 ? "middle" : "last"), false, null, Vector3.right);
                    AssertBottleGrip();
                }
                if (sip == 2 || sip == 3)
                {
                    Capture("held-sip-" + sip, false);
                    Assert.That(bottle.MouthContactError, Is.LessThan(.012f));
                    AssertBottleGrip();
                }
                if (sip == 2) input.Release(mouse.leftButton, queueEventOnly: true);
                if (sip == 2)
                {
                    yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Lowering,
                        "Releasing LMB must visibly lower the bottle after its current swallow.");
                    Capture("released-sip-lowering", false);
                    yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Carrying,
                        "The released sequence did not return to carry.");
                    Assert.That(Vector3.Dot(bottle.Bottle.transform.up, Vector3.up), Is.GreaterThan(.99f));
                    Assert.That(bottle.Bottle.Remaining,
                        Is.EqualTo((float)GameSessionState.VodkaBottleRemainingMl / VodkaBottleState.FullMl).Within(.0001f));
                    Capture("released-sip-carried", false);
                    yield return Frames(10);
                    Assert.That(bottle.CompletedSips, Is.EqualTo(2), "Releasing a held sequence must stop after its current sip.");
                    input.Press(mouse.leftButton, queueEventOnly: true);
                }
            }
            yield return ObserveEmptyCheck("standing");
            yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Throwing, "The empty check did not lead to the throw.");
            input.Release(mouse.leftButton, queueEventOnly: true);
            Capture("06-empty-throw-start", false);
            yield return ObserveThrow("standing");
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.VodkaBottle), Is.Zero);
            Assert.That(GameSessionState.IsInventoryItemEquipped(InventoryItemId.VodkaBottle), Is.False);
            ThrownVodkaBottle thrown = Object.FindAnyObjectByType<ThrownVodkaBottle>();
            Assert.That(thrown, Is.Not.Null);
            Assert.That(thrown.Body.isKinematic, Is.False);
            Capture("07-physical-flight", false, thrown.transform.position);
            yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Hidden, "Throw did not release hand presentation.");
            yield return Await(() => thrown == null || thrown.IsBroken, "The real hard-floor throw did not shatter.");
            Assert.That(thrown, Is.Not.Null, "Physics owner vanished before its debris lifetime.");
            Assert.That(thrown.ActiveShardCount, Is.GreaterThan(0));
            Assert.That(bottle.CompletedSips, Is.EqualTo(VodkaBottleState.FullBottleSipCount));
            Capture("08-concrete-shards", true, thrown.transform.position);
        }

        [UnityTest]
        public IEnumerator SeatedBottlePreservesParentPoseInventoryAndExit()
        {
            yield return InventoryToggle(true);
            yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Carrying, "Bottle did not draw before sitting.");
            int id = GameSessionState.VodkaBottleId;
            VodkaBottleView original = bottle.Bottle;
            Vector3 seatGround = player.GameObject.transform.position + Vector3.forward * 2f;
            seatGround.y = 0f;
            var plan = new CityBenchSitPlan(new CityBenchSeat("vodka-seat", seatGround + Vector3.up * .52f,
                1.6f, .45f, 0f, Vector3.forward));
            CityMiscAssetProvider meshes = CityMiscAssetProvider.LoadOrThrow();
            for (int i = 0; i < CityMiscAssetProvider.GetPartCount(CityMiscKind.YardBench); i++)
            {
                var part = meshes.GetPartOrThrow(CityMiscKind.YardBench, 0, i);
                var geometry = new GameObject("Bottle test authored bench " + i);
                geometry.transform.SetParent(fixture.transform, false); geometry.transform.position = seatGround;
                geometry.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                geometry.AddComponent<MeshRenderer>().sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            }
            PlayerAnimatedInteractionController seatController = player.GameObject.GetComponent<PlayerAnimatedInteractionController>();
            var seatObject = new GameObject("Bottle compatible bench"); seatObject.transform.SetParent(fixture.transform, false);
            var seat = seatObject.AddComponent<CityBenchSitInteraction>(); seat.Initialize(player, seatController, plan);
            player.Motor.Teleport(plan.EntryRootPosition); player.GameObject.transform.rotation = plan.EntryRotation;
            Assert.That(bottle.TryPrepareInteraction(seat), Is.False, "A compatible seat must keep the held bottle.");
            seat.Interact(player.Interactor);
            for (int frame = 0; frame < 300 && !seat.IsSeated; frame++)
            {
                yield return null;
                Assert.That(bottle.Bottle, Is.SameAs(original), "Sitting must retain the actual held prop.");
                Assert.That(GameSessionState.VodkaBottleId, Is.EqualTo(id));
                if (!seat.IsSeated) Assert.That(bottle.TrySip(), Is.False, "Transfers cannot start a sip.");
            }
            Assert.That(seat.IsSeated, Is.True);
            Assert.That(presentation.OwnsEquipmentPose(bottle), Is.True);
            Capture("seated-carry", false, seatGround + Vector3.up * .8f);
            yield return Frames(3);
            input.Press(mouse.leftButton, queueEventOnly: true);
            yield return Await(() => bottle.CompletedSips == 1, "A seated hold did not commit its first swallow.");
            Assert.That(Vector3.Distance(presentation.Registry.Anchors.Pelvis.position, plan.ActionHipPosition), Is.LessThan(.015f));
            AssertEquipmentLeavesSeatLegsAlone(seatController);
            Capture("seated-sip", false, seatGround + Vector3.up * .8f);
            yield return AwaitHeldSip(2);
            Assert.That(Vector3.Distance(presentation.Registry.Anchors.Pelvis.position, plan.ActionHipPosition), Is.LessThan(.015f));
            AssertEquipmentLeavesSeatLegsAlone(seatController);
            Capture("seated-held-second-sip", false, seatGround + Vector3.up * .8f);
            input.Release(mouse.leftButton, queueEventOnly: true);
            yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Carrying, "Seated sip did not return to carry.");
            Assert.That(player.Motor.InputEnabled, Is.False);
            yield return InventoryToggle(false);
            yield return ObserveStow("seated", seatGround + Vector3.up * .8f);
            Assert.That(seat.IsSeated, Is.True);
            yield return InventoryToggle(true);
            yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Carrying, "Seated inventory draw failed.");
            Assert.That(GameSessionState.VodkaBottleId, Is.EqualTo(id));
            Assert.That(GameSessionState.VodkaBottleRemainingMl, Is.EqualTo(434));
            Assert.That(player.Motor.InputEnabled, Is.False, "Inventory must retain the seat's movement lock.");
            Assert.That(seatController.BeginNestedLoopAction(CounterSeatInteraction.CreateBarDrinkDefinition()), Is.True);
            for (int frame = 0; frame < 180 && bottle.OccupiesHand; frame++)
            {
                yield return null;
                Assert.That(seatController.NestedLoopActionPhaseProgress, Is.Zero, "Child actions must wait for the bottle's visible stow.");
            }
            Assert.That(bottle.OccupiesHand, Is.False);
            Assert.That(seatController.CancelNestedLoopAction(), Is.True);
            yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Carrying, "Parent loop did not restore its equipped bottle.");
            original = bottle.Bottle;
            Assert.That(bottle.TryPrepareInteraction(seat), Is.False);
            seat.Interact(player.Interactor);
            yield return Await(() => !seatController.IsActive, "Standing up did not release the seat.");
            yield return Frames(3);
            Assert.That(bottle.Bottle, Is.SameAs(original));
            Assert.That(GameSessionState.VodkaBottleId, Is.EqualTo(id));
            Assert.That(GameSessionState.VodkaBottleRemainingMl, Is.EqualTo(434));
            Assert.That(player.Motor.InputEnabled, Is.True);
            Assert.That(presentation.HasCarryPose, Is.True);
            Assert.That(presentation.OwnsEquipmentPose(bottle), Is.False);

            // Prepare only the remaining final sip through the pure session
            // contract; redraw synchronizes the actual prop before sitting.
            while (GameSessionState.VodkaBottleRemainingMl > 34)
                Assert.That(GameSessionState.TryDrinkVodkaSip(id, out _), Is.True);
            GameSessionState.UpdateDrinkingProgress(0, DrinkId.Vodka, 0);
            yield return InventoryToggle(false);
            yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Hidden, "The prepared final sip did not stow.");
            yield return InventoryToggle(true);
            yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Carrying, "The prepared final sip did not draw.");
            Assert.That(GameSessionState.VodkaBottleRemainingMl, Is.EqualTo(34));
            Assert.That(bottle.TryPrepareInteraction(seat), Is.False);
            seat.Interact(player.Interactor);
            yield return Await(() => seat.IsSeated, "The final-sip check did not return to the seat.");
            int previousSips = bottle.CompletedSips;
            yield return Frames(3);
            input.Press(mouse.leftButton, queueEventOnly: true); yield return null;
            input.Release(mouse.leftButton, queueEventOnly: true);
            yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Sipping,
                "The seated last sip did not raise the bottle.");
            AssertLastSipMaximum();
            AssertEquipmentLeavesSeatLegsAlone(seatController);
            yield return Await(() => bottle.CompletedSips == previousSips + 1, "The seated final sip did not commit.");
            Capture("seated-final-sip", false, seatGround + Vector3.up * .8f);
            yield return ObserveEmptyCheck("seated", seatController, plan.ActionHipPosition, seatGround + Vector3.up * .8f);
            yield return ObserveThrow("seated", seatController, plan.ActionHipPosition, seatGround + Vector3.up * .8f);
            yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Hidden, "The seated throw did not release its equipment layer.");
            Assert.That(seat.IsSeated, Is.True);
            Assert.That(player.Motor.InputEnabled, Is.False);
            Assert.That(presentation.OwnsEquipmentPose(bottle), Is.False);
            Assert.That(Vector3.Distance(presentation.Registry.Anchors.Pelvis.position, plan.ActionHipPosition), Is.LessThan(.015f));
        }

        private void AssertEquipmentLeavesSeatLegsAlone(PlayerAnimatedInteractionController seatController,
            string clip = PlayerVodkaActions.SipClip, float? clipTime = null)
        {
            var parts = new[] { Player3DAnatomicalPart.Pelvis, Player3DAnatomicalPart.LeftThigh, Player3DAnatomicalPart.RightThigh,
                Player3DAnatomicalPart.LeftShin, Player3DAnatomicalPart.RightShin, Player3DAnatomicalPart.LeftFoot, Player3DAnatomicalPart.RightFoot };
            var before = new Quaternion[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            { Assert.That(presentation.Registry.TryGetPart(parts[i], out var binding), Is.True); before[i] = binding.Bone.localRotation; }
            float time = clipTime ?? bottle.PhaseProgress * PlayerVodkaActions.SipSeconds;
            presentation.ReleaseEquipmentPose(bottle); seatController.RefreshActiveClipAlignment();
            for (int i = 0; i < parts.Length; i++)
            { presentation.Registry.TryGetPart(parts[i], out var binding); Assert.That(Quaternion.Angle(before[i], binding.Bone.localRotation), Is.LessThan(.01f), parts[i].ToString()); }
            Assert.That(presentation.TryAcquireEquipmentPose(bottle, clip), Is.True);
            Assert.That(presentation.SampleEquipmentPose(bottle, time), Is.True);
            seatController.RefreshActiveClipAlignment(); bottle.ReapplyPresentation();
        }

        private IEnumerator AwaitHeldSip(int expected, bool allowRaise = false)
        {
            bool checkedLastSip = false;
            for (int frame = 0; frame < 300 && bottle.CompletedSips < expected; frame++)
            {
                yield return null;
                if (allowRaise && (bottle.Phase == PlayerVodkaBottleController.BottlePhase.MouthAccess ||
                    bottle.Phase == PlayerVodkaBottleController.BottlePhase.Raising)) continue;
                Assert.That(bottle.Phase, Is.EqualTo(PlayerVodkaBottleController.BottlePhase.Sipping),
                    "Held LMB must retain the raised bottle through the next swallow.");
                presentation.ReapplyLatePresentationPose(); bottle.ReapplyPresentation();
                Assert.That(bottle.MouthContactError, Is.LessThan(.012f), "The held-loop seam must retain lip contact.");
                Assert.That(Vector3.Dot(bottle.Bottle.transform.up, Vector3.up), Is.LessThan(.25f),
                    "The bottle cannot dip to its upright carry pose between held swallows.");
                NpcHandPose hands = presentation.Registry.GetComponent<NpcHandPose>();
                Assert.That(Vector3.Distance(hands.AnatomicalCylinderCentre(false), bottle.Bottle.Grip.position), Is.LessThan(.015f),
                    "The held-loop seam must retain the original hand's neck contact.");
                if (!checkedLastSip && expected == VodkaBottleState.FullBottleSipCount &&
                    bottle.PhaseProgress < PlayerVodkaActions.SipCommitProgress)
                {
                    AssertLastSipMaximum();
                    Capture("drink-fill-last-before-commit", false, null, Vector3.right);
                    checkedLastSip = true;
                }
            }
            Assert.That(bottle.CompletedSips, Is.EqualTo(expected), "Held LMB did not commit its next single swallow.");
            if (expected == VodkaBottleState.FullBottleSipCount) Assert.That(checkedLastSip, Is.True);
        }

        private Vector2 MeasureDrinkPose()
        {
            Vector3 axis = -bottle.Bottle.transform.up;
            Vector3 gaze = presentation.Registry.Anchors.Head.TransformDirection(headLocalForward).normalized;
            return new Vector2(Mathf.Asin(Mathf.Clamp(axis.y, -1f, 1f)) * Mathf.Rad2Deg,
                Mathf.Asin(Mathf.Clamp(gaze.y, -1f, 1f)) * Mathf.Rad2Deg);
        }

        private void AssertLastSipMaximum()
        {
            presentation.ReapplyLatePresentationPose(); bottle.ReapplyPresentation();
            Assert.That(GameSessionState.VodkaBottleRemainingMl, Is.InRange(1, 34),
                "The maximum pose must be visible before the final swallow removes the active bottle.");
            Vector2 pose = MeasureDrinkPose();
            Assert.That(pose.x, Is.GreaterThan(75f), "The last sip must lift the base almost vertically above the mouth.");
            Assert.That(pose.y, Is.GreaterThan(45f), "The last sip must visibly lean the head back.");
            Assert.That(bottle.MouthContactError, Is.LessThan(.012f));
            AssertBottleGrip();
            Quaternion head = presentation.Registry.Anchors.Head.rotation;
            bottle.ReapplyPresentation();
            Assert.That(Quaternion.Angle(head, presentation.Registry.Anchors.Head.rotation), Is.LessThan(.01f),
                "Reapplying a held-item contact must not accumulate head pitch.");
        }

        private void AssertClosedBottleBeforeStow()
        {
            Assert.That(bottle.Bottle, Is.Not.Null);
            Assert.That(bottle.Bottle.IsOpened, Is.False, "A nonempty bottle must be closed before entering the coat.");
            Assert.That(bottle.Bottle.Cap.gameObject.activeInHierarchy, Is.True, "The real cap must remain visible on the closed bottle.");
        }

        private void AssertCapInLeftHand()
        {
            Assert.That(bottle.Bottle.Cap.gameObject.activeInHierarchy, Is.True, "Closing retrieves the real visible cap.");
            NpcHandPose hands = presentation.Registry.GetComponent<NpcHandPose>();
            Assert.That(Vector3.Distance(hands.AnatomicalCylinderCentre(true), bottle.Bottle.Cap.position), Is.LessThan(.015f),
                "The retrieved cap must begin in the original left hand before returning to the neck.");
        }

        private IEnumerator ObserveStow(string label, Vector3? focus = null)
        {
            int captured = 0;
            bool sawClosed = false;
            float[] stops = { .12f, .32f, .50f, .75f, .98f };
            for (int frame = 0; frame < 300 && bottle.OccupiesHand; frame++)
            {
                yield return null;
                if (bottle.Phase == PlayerVodkaBottleController.BottlePhase.Closing &&
                    captured < stops.Length && bottle.PhaseProgress >= stops[captured])
                {
                    Capture(label + "-close-" + captured++, false, focus, new Vector3(.7f, .15f, 2f));
                    if (captured == 1) AssertCapInLeftHand();
                    AssertArmsOutsideCore();
                }
                if (bottle.Phase == PlayerVodkaBottleController.BottlePhase.Stowing)
                { AssertClosedBottleBeforeStow(); sawClosed = true; }
            }
            Assert.That(captured, Is.EqualTo(stops.Length), "Closing must be visible before stowing: " + label);
            Assert.That(sawClosed, Is.True);
            Assert.That(bottle.Phase, Is.EqualTo(PlayerVodkaBottleController.BottlePhase.Hidden));
        }

        private IEnumerator ObserveEmptyCheck(string label, PlayerAnimatedInteractionController seatController = null,
            Vector3? seatedPelvis = null, Vector3? focus = null)
        {
            yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.CheckingEmpty,
                "The final swallow must lead to the visible empty check.");
            VodkaBottleView original = bottle.Bottle;
            Assert.That(original, Is.Not.Null);
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.VodkaBottle), Is.Zero);
            Assert.That(GameSessionState.IsInventoryItemEquipped(InventoryItemId.VodkaBottle), Is.False);
            int thrown = bottle.ThrownBottles, captured = 0, previousShakes = 0;
            bool inspected = false;
            float[] stops = { PlayerVodkaActions.EmptyCheckTurnProgress, PlayerVodkaActions.FirstShakeProgress,
                PlayerVodkaActions.ShakeRestProgress, PlayerVodkaActions.SecondShakeProgress, PlayerVodkaActions.EmptyCheckReturnProgress };
            Vector3[] neckPositions = new Vector3[stops.Length];
            double[] presentedAt = new double[stops.Length];
            Vector3 checkAxis = Vector3.zero;
            for (int frame = 0; frame < 300 && bottle.Phase == PlayerVodkaBottleController.BottlePhase.CheckingEmpty; frame++)
            {
                yield return null;
                if (bottle.Phase != PlayerVodkaBottleController.BottlePhase.CheckingEmpty) break;
                presentation.ReapplyLatePresentationPose(); bottle.ReapplyPresentation();
                Assert.That(bottle.Bottle, Is.SameAs(original), "The empty check retains the actual emptied prop after automatic unequip.");
                Assert.That(bottle.ThrownBottles, Is.EqualTo(thrown), "The prop cannot leave the hand before both shakes finish.");
                Assert.That(bottle.CompletedEmptyShakes, Is.InRange(previousShakes, 2));
                previousShakes = bottle.CompletedEmptyShakes;
                if (bottle.PhaseProgress >= PlayerVodkaActions.EmptyCheckTurnProgress &&
                    bottle.PhaseProgress <= PlayerVodkaActions.EmptyCheckReturnProgress)
                    Assert.That(Vector3.Dot(original.transform.up, Vector3.down), Is.GreaterThan(.9999f),
                        "Both empty-check thrusts must keep the neck strictly toward the ground.");
                if (!inspected && bottle.PhaseProgress >= PlayerVodkaActions.EmptyCheckInspectProgress &&
                    bottle.PhaseProgress <= PlayerVodkaActions.EmptyCheckInspectEndProgress)
                {
                    Vector3 toOpening = original.Mouth.position - (leftEye.position + rightEye.position) * .5f;
                    Vector3 gaze = presentation.Registry.Anchors.Head.TransformDirection(headLocalForward).normalized;
                    Assert.That(Vector3.Dot(gaze, toOpening.normalized), Is.GreaterThan(.95f));
                    Assert.That(Vector3.Dot(-original.transform.up, toOpening.normalized), Is.GreaterThan(.90f),
                        "Inspect the opening before rotating into the two vertical shakes.");
                    Capture(label + "-look-into-empty-neck", false, focus);
                    inspected = true;
                }
                if (captured >= stops.Length || bottle.PhaseProgress + .00001f < stops[captured]) continue;
                if (seatController != null)
                {
                    Assert.That(Vector3.Distance(presentation.Registry.Anchors.Pelvis.position, seatedPelvis.Value), Is.LessThan(.015f));
                    AssertEquipmentLeavesSeatLegsAlone(seatController, PlayerVodkaActions.EmptyCheckClip,
                        bottle.PhaseProgress * PlayerVodkaActions.EmptyCheckSeconds);
                    Assert.That(Vector3.Dot(original.Grip.position - presentation.Registry.Anchors.Pelvis.position,
                        player.GameObject.transform.forward), Is.GreaterThan(.10f), "The inverted bottle must stay in front of the seated knees.");
                }
                int expectedShakes = captured < 1 ? 0 : captured < 3 ? 1 : 2;
                Assert.That(bottle.CompletedEmptyShakes, Is.EqualTo(expectedShakes));
                bool examining = captured == 0 || captured == 2 || captured == 4;
                Vector3 eyes = (leftEye.position + rightEye.position) * .5f;
                Vector3 eyeToMouth = original.Mouth.position - eyes;
                Assert.That(Vector3.Dot(eyeToMouth, player.GameObject.transform.forward), Is.InRange(.15f, .55f),
                    "The bottle opening must remain in front of the face, with clearance for the inspection.");
                Assert.That(eyeToMouth.y, Is.InRange(examining ? -.04f : -.12f, .10f),
                    "Empty-check holds lift the bottle opening to eye level; only the two thrusts lower it.");
                // The ordinary body may sway while the raised bottle follows
                // the face. Measure each shake and recovered hold relative to
                // those live eyes rather than requiring a frozen world pose.
                neckPositions[captured] = player.GameObject.transform.InverseTransformDirection(original.Grip.position - eyes);
                presentedAt[captured] = Time.timeAsDouble;
                if (captured == 0)
                    checkAxis = player.GameObject.transform.InverseTransformDirection(original.transform.up).normalized;
                Capture(label + "-empty-check-" + captured, false, focus);
                Capture(label + "-empty-check-head-" + captured++, false,
                    (eyes + original.Mouth.position + original.transform.position) / 3f, new Vector3(.7f, .1f, 2f), .43f);
            }
            Assert.That(captured, Is.EqualTo(stops.Length), "The inverted hold and both separate shakes must be presented.");
            Assert.That(inspected, Is.True, "The opening inspection must be presented before the vertical shakes.");
            Assert.That(bottle.CompletedEmptyShakes, Is.EqualTo(2));
            // Each of the two bounded segments between peaks may round up to
            // the next rendered frame, while the impulses remain one pair.
            Assert.That(presentedAt[3] - presentedAt[1],
                Is.InRange(.25d - .0001d, .25d + 2d * Time.deltaTime + .0001d),
                "The two empty-check shakes must land about a quarter-second apart.");
            Vector3 firstThrust = neckPositions[1] - neckPositions[0];
            Vector3 secondThrust = neckPositions[3] - neckPositions[2];
            Assert.That(Vector3.Dot(firstThrust, checkAxis), Is.GreaterThan(.055f), "The first shake must visibly thrust along the inverted bottle axis.");
            Assert.That(Vector3.Dot(secondThrust, checkAxis), Is.GreaterThan(.055f), "The second shake must visibly thrust along the inverted bottle axis.");
            Assert.That(-firstThrust.y, Is.GreaterThan(.025f), "The first thrust must retain a downward component.");
            Assert.That(-secondThrust.y, Is.GreaterThan(.025f), "The second thrust must retain a downward component.");
            Assert.That(Vector3.Distance(neckPositions[2], neckPositions[0]), Is.LessThan(.02f), "The two shakes require a distinct restored hold between them.");
            Assert.That(Vector3.Distance(neckPositions[4], neckPositions[2]), Is.LessThan(.02f), "The second shake must recover before the throw.");
        }

        private IEnumerator ObserveThrow(string label, PlayerAnimatedInteractionController seatController = null,
            Vector3? seatedPelvis = null, Vector3? focus = null)
        {
            yield return Await(() => bottle.Phase == PlayerVodkaBottleController.BottlePhase.Throwing,
                "The inspected empty bottle must lead to the throw.");
            VodkaBottleView original = bottle.Bottle;
            Assert.That(original, Is.Not.Null);
            Vector3 root = player.GameObject.transform.position;
            Quaternion facing = player.GameObject.transform.rotation;
            int expectedThrows = bottle.ThrownBottles + 1;
            bool capturedWindup = false, capturedRelease = false;
            for (int frame = 0; frame < 300 && !capturedRelease; frame++)
            {
                yield return null;
                presentation.ReapplyLatePresentationPose(); bottle.ReapplyPresentation();
                Assert.That(Vector3.Distance(player.GameObject.transform.position, root), Is.LessThan(.01f),
                    "The frustrated throw must retain the player's grounded root.");
                Assert.That(Quaternion.Angle(player.GameObject.transform.rotation, facing), Is.LessThan(.25f));
                if (!capturedWindup && bottle.Phase == PlayerVodkaBottleController.BottlePhase.Throwing &&
                    bottle.PhaseProgress >= .44f && bottle.Bottle != null)
                {
                    Assert.That(bottle.Bottle, Is.SameAs(original));
                    NpcHandPose hands = presentation.Registry.GetComponent<NpcHandPose>();
                    Assert.That(Vector3.Distance(hands.AnatomicalCylinderCentre(false), original.Grip.position), Is.LessThan(.015f),
                        "The windup must keep the actual palm attached until physical release.");
                    if (seatController != null)
                    {
                        Assert.That(Vector3.Distance(presentation.Registry.Anchors.Pelvis.position, seatedPelvis.Value), Is.LessThan(.015f));
                        AssertEquipmentLeavesSeatLegsAlone(seatController, PlayerVodkaActions.ThrowClip,
                            bottle.PhaseProgress * PlayerVodkaActions.ThrowSeconds);
                    }
                    else AssertStandingFeetGrounded();
                    Capture(label + "-throw-windup", false, focus);
                    capturedWindup = true;
                }
                if (bottle.ThrownBottles != expectedThrows) continue;
                Assert.That(bottle.Bottle, Is.Null);
                ThrownVodkaBottle thrown = original.GetComponent<ThrownVodkaBottle>();
                Assert.That(thrown, Is.Not.Null, "The same inspected prop must become the physical thrown bottle.");
                Assert.That(thrown.gameObject, Is.SameAs(original.gameObject));
                Assert.That(thrown.Body.isKinematic, Is.False);
                if (seatController != null)
                {
                    Assert.That(Vector3.Distance(presentation.Registry.Anchors.Pelvis.position, seatedPelvis.Value), Is.LessThan(.015f));
                    AssertEquipmentLeavesSeatLegsAlone(seatController, PlayerVodkaActions.ThrowClip,
                        bottle.PhaseProgress * PlayerVodkaActions.ThrowSeconds);
                }
                else AssertStandingFeetGrounded();
                Capture(label + "-throw-release", false, focus);
                capturedRelease = true;
            }
            Assert.That(capturedWindup && capturedRelease, Is.True, "Both the frustrated windup and real release must remain visible.");
        }

        private void AssertStandingFeetGrounded()
        {
            foreach (Player3DAnatomicalPart part in new[] { Player3DAnatomicalPart.LeftFoot, Player3DAnatomicalPart.RightFoot })
            {
                Assert.That(presentation.Registry.TryGetPart(part, out Player3DAnatomicalPartBinding binding), Is.True);
                Assert.That(binding.Renderer, Is.InstanceOf<SkinnedMeshRenderer>());
                var renderer = (SkinnedMeshRenderer)binding.Renderer;
                renderer.BakeMesh(handScratch, true);
                float lowest = float.PositiveInfinity;
                foreach (Vector3 point in handScratch.vertices)
                    lowest = Mathf.Min(lowest, renderer.transform.TransformPoint(point).y);
                Assert.That(lowest, Is.InRange(-.025f, .05f),
                    "The standing throw must retain real boot contact with the fixture floor: " + part);
            }
        }

        [UnityTest]
        public IEnumerator PhysicalDropsRespectSoftGroundAndPause()
        {
            Vector3 position = player.GameObject.transform.position + Vector3.right * 7f;
            MakeFloor("Soil landing patch", new Vector3(position.x, .04f, position.z), new Vector3(5f, .1f, 5f), FootstepGroundKind.Soil);
            VodkaBottleView view = VodkaBottleView.Create(fixture.transform);
            view.SetOpened(true); view.SetRemaining(0f);
            view.transform.SetPositionAndRotation(new Vector3(position.x, .6f, position.z), Quaternion.Euler(0f, 0f, 30f));
            ThrownVodkaBottle thrown = ThrownVodkaBottle.Release(view, Vector3.down, Vector3.zero, player.GameObject.transform);
            Physics.SyncTransforms();
            yield return Await(() => thrown.ImpactCount > 0, "The soft-ground bottle never reached its collider.");
            Assert.That(thrown.IsBroken, Is.False, "A modest soil landing must remain intact.");
            Capture("09-soil-intact", true, thrown.transform.position);
            using (GameTimeScaleRuntime.AcquirePause())
            {
                yield return Frames(2);
                Vector3 paused = thrown.Body.position;
                yield return Frames(8);
                Assert.That(Vector3.Distance(paused, thrown.Body.position), Is.LessThan(.001f));
                Assert.That(thrown.IsBroken, Is.False);
            }
            Object.Destroy(thrown.gameObject);
            yield return null;
            var waterObject = new GameObject("Actual water sheet marker");
            waterObject.transform.SetParent(fixture.transform, false);
            WorldWaterSurface water = waterObject.AddComponent<WorldWaterSurface>();
            Vector3 waterCentre = player.GameObject.transform.position + Vector3.left * 7f;
            water.Configure(new Rect(waterCentre.x - 2f, waterCentre.z - 2f, 4f, 4f), .8f, .8f, null);
            VodkaBottleView floating = VodkaBottleView.Create(fixture.transform);
            floating.SetOpened(true); floating.SetRemaining(0f);
            floating.transform.position = new Vector3(waterCentre.x, 1.4f, waterCentre.z);
            ThrownVodkaBottle wet = ThrownVodkaBottle.Release(floating, Vector3.down, Vector3.zero, player.GameObject.transform);
            yield return Await(() => wet.IsSubmerged, "The thrown bottle failed to cross the real water sheet.");
            Assert.That(wet.IsBroken, Is.False, "Water entry cannot produce a glass fracture.");
            Assert.That(wet.Body.linearVelocity.y, Is.LessThan(0f), "A bottle entering water must sink.");
            Capture("10-water-entry", true, wet.transform.position);
        }

        private void AssertBottleGrip()
        {
            NpcHandPose hands = presentation.Registry.GetComponent<NpcHandPose>();
            Assert.That(hands.GetGripKind(false), Is.EqualTo(NpcHandGripKind.Bottle));
            Assert.That(hands.RightGripWeight, Is.GreaterThan(.99f));
            foreach (NpcHandPose.HandBinding binding in hands.Hands)
            {
                if (binding.IsLeft) continue;
                AnatomicalHandFrame frame = hands.GetAnatomicalFrame(false, NpcHandGripKind.Bottle);
                Vector3 posedAxis = binding.Hand.rotation * frame.ThumbDirectionInHand;
                Vector3 posedPalm = binding.Hand.rotation * frame.PalmNormalInHand;
                TestContext.Out.WriteLine("Bottle grip frame hand=" + binding.Hand.name + "/" + binding.Hand.GetEntityId() +
                    " handRotation=" + binding.Hand.rotation.ToString("F5") + " axis=" + posedAxis.ToString("F5") +
                    " palm=" + posedPalm.ToString("F5") + " wrapperUp=" + bottle.Bottle.transform.up.ToString("F5") +
                    " importedAnchorUp=" + bottle.Bottle.Grip.up.ToString("F5"));
                foreach (SkinnedMeshRenderer thumb in binding.Renderers)
                {
                    if (thumb.name != "GEO_Thumb.R") continue;
                    Assert.That(PlayerVodkaGripCatalog.TryGetEntry(thumb.sharedMesh, out PlayerVodkaGripCatalog.Entry entry), Is.True);
                    thumb.BakeMesh(handScratch, true);
                    Vector3[] points = handScratch.vertices;
                    Vector3 Mean(int[] indices)
                    { Vector3 point = Vector3.zero; foreach (int i in indices) point += thumb.transform.TransformPoint(points[i]); return point / indices.Length; }
                    Vector3 direction = (Mean(entry.ThumbTipRing) - Mean(entry.ThumbBaseRing)).normalized;
                    TestContext.Out.WriteLine("Actual baked thumb axis=" + direction.ToString("F5") +
                        " dotWrapper=" + Vector3.Dot(direction, bottle.Bottle.transform.up) +
                        " inHand=" + (Quaternion.Inverse(binding.Hand.rotation) * direction).ToString("F5"));
                }
                Assert.That(Vector3.Dot(posedAxis, bottle.Bottle.transform.up), Is.GreaterThan(.99f),
                    "The actual wrist must align to the bottle wrapper axis; the imported FBX anchor has a different basis.");
            }
            foreach (NpcHandPose.HandBinding hand in hands.Hands)
            {
                if (hand.IsLeft) continue;
                foreach (SkinnedMeshRenderer renderer in hand.Renderers)
                {
                    int shape = CharacterJointDeformation.FindShape(renderer.sharedMesh, "BottleGrip");
                    Assert.That(shape, Is.GreaterThanOrEqualTo(0));
                    Assert.That(renderer.GetBlendShapeWeight(shape), Is.GreaterThan(99f), renderer.name + ": authored thumb/curl must remain active after combat's stowed update.");
                    Assert.That(PlayerVodkaGripCatalog.TryGetEntry(renderer.sharedMesh, out PlayerVodkaGripCatalog.Entry entry), Is.True);
                    renderer.BakeMesh(handScratch, true);
                    Vector3[] vertices = handScratch.vertices;
                    for (int i = 0; i < vertices.Length; i++) vertices[i] = renderer.transform.TransformPoint(vertices[i]);
                    if (renderer.name == "GEO_Thumb.R")
                    {
                        Vector3 Base(int[] ring)
                        { Vector3 value = Vector3.zero; foreach (int index in ring) value += vertices[index]; return value / ring.Length; }
                        Vector3 thumb = (Base(entry.ThumbTipRing) - Base(entry.ThumbBaseRing)).normalized;
                        Assert.That(Vector3.Dot(thumb, bottle.Bottle.transform.up), Is.GreaterThan(.94f),
                            "The actual baked thumb, not a socket/Empty, must point toward the bottle mouth.");
                    }
                    if (renderer.name == "GEO_Hand.R")
                    {
                        Assert.That(entry.PalmPlane.Length, Is.EqualTo(3));
                        Vector3 normal = Vector3.Cross(vertices[entry.PalmPlane[1]] - vertices[entry.PalmPlane[0]],
                            vertices[entry.PalmPlane[2]] - vertices[entry.PalmPlane[0]]).normalized;
                        TestContext.Out.WriteLine("Actual distal palm plane=" + normal.ToString("F5") +
                            " dotBottle=" + Vector3.Dot(normal, bottle.Bottle.transform.up) +
                            " inHand=" + (Quaternion.Inverse(hand.Hand.rotation) * normal).ToString("F5") +
                            " vertices=" + string.Join(",", entry.PalmPlane));
                        Assert.That(Mathf.Abs(Vector3.Dot(normal, bottle.Bottle.transform.up)), Is.LessThan(.08f),
                            "The real palm plane must run vertically along the bottle neck.");
                        if (bottle.Phase == PlayerVodkaBottleController.BottlePhase.Carrying)
                            Assert.That(Mathf.Abs(Vector3.Dot(normal, Vector3.up)), Is.LessThan(.08f), "A carried bottle needs a vertical palm, not an upward serving palm.");
                    }
                    Vector3 centre = bottle.Bottle.Grip.position;
                    Vector3 right = bottle.Bottle.transform.right, forward = bottle.Bottle.transform.forward;
                    var projected = new Vector2[vertices.Length];
                    for (int i = 0; i < projected.Length; i++)
                    { Vector3 delta = vertices[i] - centre; projected[i] = new Vector2(Vector3.Dot(delta, right), Vector3.Dot(delta, forward)); }
                    int[] triangles = handScratch.triangles;
                    float minimum = float.PositiveInfinity;
                    for (int i = 0; i < triangles.Length; i += 3)
                        minimum = Mathf.Min(minimum, TriangleAxisDistance(projected[triangles[i]], projected[triangles[i + 1]], projected[triangles[i + 2]]));
                    Assert.That(minimum, Is.GreaterThan(VodkaBottleDimensions.GripRadius - .0003f), renderer.name + ": the real baked hand surface penetrates the bottle neck.");
                }
            }
            if (bottle.Phase == PlayerVodkaBottleController.BottlePhase.Carrying)
                Assert.That(Vector3.Dot(bottle.Bottle.transform.up, Vector3.up), Is.GreaterThan(.99f));
        }

        private static float TriangleAxisDistance(Vector2 a, Vector2 b, Vector2 c)
        {
            float Cross(Vector2 first, Vector2 second) => first.x * second.y - first.y * second.x;
            float ab = Cross(a, b), bc = Cross(b, c), ca = Cross(c, a);
            if (Mathf.Abs(Cross(b - a, c - a)) > .0000000001f &&
                ((ab >= 0f && bc >= 0f && ca >= 0f) || (ab <= 0f && bc <= 0f && ca <= 0f))) return 0f;
            float Segment(Vector2 first, Vector2 second)
            {
                Vector2 delta = second - first;
                float t = delta.sqrMagnitude > .0000000001f ? Mathf.Clamp01(-Vector2.Dot(first, delta) / delta.sqrMagnitude) : 0f;
                return (first + delta * t).magnitude;
            }
            return Mathf.Min(Segment(a, b), Mathf.Min(Segment(b, c), Segment(c, a)));
        }

        private void AssertArmsOutsideCore()
        {
            var anatomy = crowbar.Actor.Ragdoll.PhysicsController.AnatomicalColliders;
            foreach (var arm in anatomy)
            {
                if (arm.Value != Player3DAnatomicalPart.LeftForearm && arm.Value != Player3DAnatomicalPart.LeftHand &&
                    arm.Value != Player3DAnatomicalPart.RightForearm && arm.Value != Player3DAnatomicalPart.RightHand) continue;
                foreach (var core in anatomy)
                {
                    if (core.Value != Player3DAnatomicalPart.Pelvis && core.Value != Player3DAnatomicalPart.LowerTorso &&
                        core.Value != Player3DAnatomicalPart.Torso && core.Value != Player3DAnatomicalPart.Head) continue;
                    bool overlaps = Physics.ComputePenetration(arm.Key, arm.Key.transform.position, arm.Key.transform.rotation,
                        core.Key, core.Key.transform.position, core.Key.transform.rotation, out _, out float depth);
                    Assert.That(overlaps && depth > .004f, Is.False,
                        bottle.Phase + "/" + bottle.PhaseProgress + ": " + arm.Value + " crosses " + core.Value + " by " + depth);
                }
            }
        }

        private void AssertSharedGulpPlayed()
        {
            RetroAudioService audio = RetroAudioService.Instance;
            Assert.That(audio, Is.Not.Null);
            bool played = false;
            foreach (AudioSource source in audio.GetComponentsInChildren<AudioSource>())
                for (int variant = 0; variant < RetroSfxLibrary.GetDefinition(RetroSfxId.DrinkGulp).VariantCount; variant++)
                    if (source.clip != null && source.clip == audio.GetClip(RetroSfxId.DrinkGulp, variant))
                    {
                        played = true;
                        Assert.That(source.loop, Is.False, "Each swallow uses one bounded gulp cue.");
                        Assert.That(source.ignoreListenerPause, Is.False);
                    }
            Assert.That(played, Is.True, "A completed swallow must play the shared gulp cue.");
        }

        private static void AssertLiquidAndDoseContracts()
        {
            Assert.That(VodkaBottleState.FullBottleSipCount, Is.EqualTo(15));
            var state = new VodkaBottleState();
            Assert.That(state.TryAdd(1), Is.True);
            Assert.That(state.SelectBottle(), Is.True);
            int id = state.ActiveId, alcohol = 0, stress = 0, servings = 0;
            int[] remainders = { 467, 434, 400, 367, 334, 300, 267, 234, 200, 167, 134, 100, 67, 34, 0 };
            for (int sip = 0; sip < remainders.Length; sip++)
            {
                Assert.That(state.TrySip(id, 96, 48, 4, out VodkaBottleSipResult dose), Is.True);
                Assert.That(dose.RemainingMl, Is.EqualTo(remainders[sip]));
                Assert.That(dose.BeforeMl - dose.RemainingMl, Is.InRange(33, 34));
                Assert.That(dose.Emptied, Is.EqualTo(sip == remainders.Length - 1));
                alcohol += dose.AlcoholDose; stress += dose.StressRelief; servings += dose.Servings;
            }
            Assert.That(alcohol, Is.EqualTo(96)); Assert.That(stress, Is.EqualTo(48)); Assert.That(servings, Is.EqualTo(4));
            Assert.That(state.Count, Is.Zero); Assert.That(state.TrySip(id, 96, 48, 4, out _), Is.False);
            state.Clear(); Assert.That(state.TryAdd(1, VodkaBottleState.RefrigeratorMl), Is.True);
            Assert.That(state.SelectBottle(), Is.True); Assert.That(state.ActiveRemainingMl, Is.EqualTo(100));
            id = state.ActiveId;
            for (int sip = 0; sip < 3; sip++)
            {
                Assert.That(state.TrySip(id, 96, 48, 4, out VodkaBottleSipResult dose), Is.True);
                Assert.That(dose.RemainingMl, Is.EqualTo(sip == 0 ? 67 : sip == 1 ? 34 : 0));
                Assert.That(dose.Emptied, Is.EqualTo(sip == 2));
            }
            foreach (int initialMl in new[] { 1, 32, 34, 67, 101, 499 })
            {
                state.Clear(); Assert.That(state.TryAdd(1, initialMl), Is.True);
                Assert.That(state.SelectBottle(), Is.True); id = state.ActiveId;
                int swallowedMl = 0, sipCount = 0;
                while (state.Count > 0 && sipCount <= VodkaBottleState.FullBottleSipCount)
                {
                    Assert.That(state.TrySip(id, 96, 48, 4, out VodkaBottleSipResult dose), Is.True);
                    Assert.That(dose.BeforeMl - dose.RemainingMl, Is.InRange(1, 34));
                    swallowedMl += dose.BeforeMl - dose.RemainingMl; sipCount++;
                }
                Assert.That(state.Count, Is.Zero); Assert.That(swallowedMl, Is.EqualTo(initialMl));
                Assert.That(sipCount, Is.EqualTo((initialMl * 15 + 499) / 500));
            }

            Vector3[] normals = { Vector3.up, Vector3.right, Vector3.down, new Vector3(.7f, .3f, .6f).normalized };
            float capacity = VodkaBottleView.VolumeBelow(Vector3.up, 1f);
            Assert.That(capacity, Is.GreaterThan(0f));
            foreach (Vector3 normal in normals)
            {
                float previous = float.NegativeInfinity;
                foreach (float remaining in new[] { 0f, .1f, .5f, 1f })
                {
                    float level = VodkaBottleView.SolveLevel(normal, remaining);
                    Assert.That(level, Is.GreaterThanOrEqualTo(previous)); previous = level;
                    float volume = VodkaBottleView.VolumeBelow(normal, level);
                    // Fixed midpoint slices bound upright quantization to one
                    // slice. Tilt must preserve volume within that same bound.
                    Assert.That(volume, Is.EqualTo(capacity * .82f * remaining).Within(capacity * .025f),
                        "Liquid lost volume under bottle tilt: " + normal + "/" + remaining);
                }
            }
        }

        private IEnumerator InventoryToggle(bool equip, bool holdMouse = false, InventoryItemId itemId = InventoryItemId.VodkaBottle)
        {
            Assert.That(inventory.Open(), Is.True);
            int item = -1;
            for (int i = 0; i < GameSessionState.InventoryItems.Count; i++)
                if (GameSessionState.InventoryItems[i].ItemId == itemId) item = i;
            Assert.That(item, Is.GreaterThanOrEqualTo(0));
            if (inventory.SelectedItemIndex != item) Assert.That(inventory.SelectItem(item), Is.True);
            yield return null;
            input.Press(keyboard.uKey, queueEventOnly: true); yield return null;
            input.Release(keyboard.uKey, queueEventOnly: true); yield return null;
            Assert.That(inventory.SelectedIsEquipped, Is.EqualTo(equip));
            if (holdMouse) { input.Press(mouse.leftButton, queueEventOnly: true); yield return null; }
            Assert.That(inventory.Close(), Is.True); yield return null;
        }

        private void MakeFloor(string name, Vector3 position, Vector3 scale, FootstepGroundKind kind)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = name;
            floor.transform.SetParent(fixture.transform, false); floor.transform.position = position; floor.transform.localScale = scale;
            floor.layer = CaptureLayer; FootstepGround.Stamp(floor, kind);
        }
        private void Capture(string name, bool close, Vector3? explicitFocus = null, Vector3? viewDirection = null, float? size = null)
        {
            presentation.ReapplyLatePresentationPose();
            bottle.ReapplyPresentation();
            if (bottle.Bottle != null && (bottle.Phase == PlayerVodkaBottleController.BottlePhase.Carrying ||
                bottle.Phase == PlayerVodkaBottleController.BottlePhase.Raising ||
                bottle.Phase == PlayerVodkaBottleController.BottlePhase.Sipping ||
                bottle.Phase == PlayerVodkaBottleController.BottlePhase.Lowering ||
                bottle.Phase == PlayerVodkaBottleController.BottlePhase.Closing ||
                bottle.Phase == PlayerVodkaBottleController.BottlePhase.CheckingEmpty))
            {
                NpcHandPose hands = presentation.Registry.GetComponent<NpcHandPose>();
                Assert.That(Vector3.Distance(hands.AnatomicalCylinderCentre(false), bottle.Bottle.Grip.position), Is.LessThan(.015f),
                    name + ": the actual palm must remain on the visible bottle, including the mouth-contact correction.");
            }
            foreach (Transform child in fixture.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = CaptureLayer;
            foreach (ThrownVodkaBottle thrown in Object.FindObjectsByType<ThrownVodkaBottle>())
                foreach (Transform child in thrown.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = CaptureLayer;
            Transform body = player.GameObject.transform;
            bool handClose = name.Contains("thumb-and-four-fingers");
            Vector3 focus = explicitFocus ?? (close && bottle.Bottle != null ? bottle.Bottle.Grip.position : body.position + Vector3.up);
            camera.orthographic = true; camera.orthographicSize = size ?? (handClose ? .16f : close ? .23f : 1.25f); camera.aspect = 1f;
            Vector3 direction = viewDirection.HasValue ? body.TransformDirection(viewDirection.Value) :
                body.forward * 2f + body.right * 3f + Vector3.up * .6f;
            camera.transform.position = focus + direction.normalized * 8f; camera.transform.LookAt(focus);
            camera.Render(); RenderTexture prior = RenderTexture.active;
            try
            {
                RenderTexture.active = target; tile.ReadPixels(new Rect(0f, 0f, TileSize, TileSize), 0, 0); tile.Apply();
                string directory = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "VodkaBottleEquipment");
                Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, name + ".png"), tile.EncodeToPNG());
            }
            finally { RenderTexture.active = prior; }
        }
        private static IEnumerator Frames(int count) { for (int frame = 0; frame < count; frame++) yield return null; }
        private IEnumerator Await(Func<bool> condition, string message)
        {
            for (int frame = 0; frame < 300 && !condition(); frame++) yield return null;
            string detail = bottle == null ? "" : $" Phase={bottle.Phase}/{bottle.PhaseProgress} sips={bottle.CompletedSips} fill={GameSessionState.VodkaBottleRemainingMl} id={GameSessionState.VodkaBottleId} equipped={GameSessionState.IsInventoryItemEquipped(InventoryItemId.VodkaBottle)} canDrink={GameSessionState.CanDrinkVodkaSip} scale={Time.timeScale} dt={Time.deltaTime} paused={GameTimeScaleRuntime.IsPaused} motor={player.Motor.InputEnabled} interactor={player.Interactor.InputEnabled} context={player.GameObject.GetComponent<PlayerAnimatedInteractionController>().Phase}";
            if (!condition() && bottle != null)
            {
                foreach (string name in new[] { "elapsed", "committed", "beforeSipMl", "displayedMl", "bottleId", "equipmentOverlay" })
                    detail += " " + name + "=" + typeof(PlayerVodkaBottleController).GetField(name,
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(bottle);
                detail += $" clip={presentation.ActiveClipName} ownClip={presentation.OwnsClip(bottle)} ownOverlay={presentation.OwnsEquipmentPose(bottle)}";
            }
            Assert.That(condition(), Is.True, message + detail);
        }
    }
}
