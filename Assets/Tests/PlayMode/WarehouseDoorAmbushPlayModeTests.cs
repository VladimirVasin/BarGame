using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class WarehouseDoorAmbushAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.CombatDoorAmbushAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    /// <summary>One real-warehouse trip through preview, shin contact and persistent inverted gameplay.</summary>
    [PrebuildSetup(typeof(WarehouseDoorAmbushAssetsSetup))]
    public sealed class WarehouseDoorAmbushPlayModeTests
    {
        private CombatTestRoot root;
        private float previousCaptureDelta;
        private bool previousAudioPause, previousBackground;
        private IDisposable gameView;
        private CombatWeaponId originalWeapon;
        private Transform originalWeaponParent;
        private LocalPose originalWeaponPose;
        private IFirearmState originalPistol, originalShotgun;
        private int originalPistolRounds, originalPistolReserve, originalShotgunRounds, originalShotgunReserve;
        private WarehouseValveLampFlicker valveLamp;
        private Light valveLight;
        private Vector3 valveLightPosition;
        private Quaternion valveLightRotation;
        private Color valveLightColor;
        private float valveLightInnerAngle, valveLightOuterAngle;

        private static bool CaptureRequested => Array.IndexOf(Environment.GetCommandLineArgs(), "-bp-cutscene-capture") >= 0;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousCaptureDelta = Time.captureDeltaTime;
            previousAudioPause = AudioListener.pause;
            previousBackground = Application.runInBackground;
            Application.runInBackground = true;
            Time.captureDeltaTime = 1f / 60f;
            RetroAudioService.Instance?.StopAll();
            EnsureListener();
            GameInput.HandleApplicationFocus(true);
            GameSessionState.BeginNewGame();
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.UseDockerPopulationForTests = false;
            CombatTestRoot.AuthoredEnemyKindForTests = null;
            CombatTestRoot.WarehouseSeedForTests = 0;
            yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
            root = Object.FindAnyObjectByType<CombatTestRoot>();
            Assert.That(root != null && root.IsInitialized && root.WarehousePlan != null, Is.True);
            root.AutomaticSimulation = false;
            root.AutomaticOpponentDecisions = false;
            root.SetDuelLogging(false);
            root.SendMessage("OnApplicationFocus", true);
            GameInput.HandleApplicationFocus(true);
            root.Cutscenes.AutomaticPlayback = false;
            for (int frame = 0; frame < 3; frame++) yield return null;
            valveLamp = root.GetComponentInChildren<WarehouseValveLampFlicker>(true);
            Assert.That(valveLamp, Is.Not.Null);
            valveLight = valveLamp.Source;
            Assert.That(valveLight, Is.Not.Null);
            valveLightPosition = valveLight.transform.position;
            valveLightRotation = valveLight.transform.rotation;
            valveLightColor = valveLight.color;
            valveLightInnerAngle = valveLight.innerSpotAngle;
            valveLightOuterAngle = valveLight.spotAngle;
            AssertValveLightingRestored();
            if (CaptureRequested && !Application.isBatchMode) PrepareGameView();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            gameView?.Dispose();
            gameView = null;
            if (root != null)
            {
                root.AutomaticSimulation = false;
                root.Cutscenes?.Stop();
                root.PauseMenu?.Cancel();
            }
            RetroAudioService.Instance?.StopAll();
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.name == SceneIds.CombatTest)
            {
                Scene cleanup = SceneManager.CreateScene("Test warehouse door cleanup");
                SceneManager.SetActiveScene(cleanup);
                foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>())
                    if (listener.gameObject.scene == active) listener.enabled = false;
                EnsureListener();
                AsyncOperation unload = SceneManager.UnloadSceneAsync(active);
                if (unload != null) yield return unload;
            }
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.UseDockerPopulationForTests = false;
            CombatTestRoot.AuthoredEnemyKindForTests = null;
            CombatTestRoot.WarehouseSeedForTests = null;
            GameSessionState.BeginNewGame();
            Time.captureDeltaTime = previousCaptureDelta;
            AudioListener.pause = previousAudioPause;
            Application.runInBackground = previousBackground;
            root = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator DoorAmbush_ValveGatePreviewReplayAndInvertedGameplayKeepTheLoadedWarehouse()
        {
            WarehouseDoorAmbushScene scene = root.DoorAmbush;
            Assert.That(scene, Is.Not.Null);
            Assert.That(root.Cutscenes.Catalog.Count, Is.EqualTo(1));
            Assert.That(root.Cutscenes.Catalog.Find(WarehouseDoorAmbushScene.CutsceneId), Is.Not.Null);
            Assert.That(root.Player.Visual, Is.InstanceOf<Player3DCharacterPresentation>());
            Assert.That(GameSessionState.WarehouseDoorAmbushPlayed, Is.False);
            AssertDoorAndValveInsideEmergencyLight();
            WarehouseGateController gate = root.GetComponentInChildren<WarehouseGateController>();
            Assert.That(gate, Is.Not.Null);

            Vector3 initialSpawn = root.Hero.transform.position;
            PlaceHero(scene.EntryRootPosition + Vector3.back * .25f);
            Assert.That(scene.DoorInteraction.CanInteract(root.Player.Interactor), Is.False,
                "The unturned valve must keep the actual goal-door trigger unavailable.");
            Assert.That(GameSessionState.WarehouseGate.TryUnlock(true), Is.True);
            Assert.That(GameSessionState.WarehouseGate.Advance(3f, true), Is.True);
            ApplyGateTravel(gate);
            Assert.That(scene.DoorInteraction.CanInteract(root.Player.Interactor), Is.False,
                "A partial turn cannot launch the scene.");
            Assert.That(GameSessionState.WarehouseGate.Advance(3f, true), Is.True);
            ApplyGateTravel(gate);
            Physics.SyncTransforms();
            Assert.That(gate.IsOpen, Is.True);
            Assert.That(scene.DoorInteraction.CanInteract(root.Player.Interactor), Is.True);
            GameSessionState.WarehouseGate.Reset();
            ApplyGateTravel(gate);
            PlaceHero(initialSpawn);
            foreach (WarehouseRefrigeratorPlacement refrigerator in root.WarehousePlan.Refrigerators)
                Assert.That(GameSessionState.IsWorldItemCollected(refrigerator.SourceId), Is.False);

            Vector3 previewOrigin = root.Hero.transform.position;
            Quaternion previewFacing = root.Hero.transform.rotation;
            originalWeapon = root.Hero.WeaponId;
            originalWeaponParent = root.Hero.CarriedWeapon(originalWeapon).transform.parent;
            originalWeaponPose = new LocalPose(root.Hero.CarriedWeapon(originalWeapon).transform);
            originalPistol = root.Hero.CarriedFirearm(CombatWeaponId.Pistol);
            originalShotgun = root.Hero.CarriedFirearm(CombatWeaponId.Shotgun);
            originalPistolRounds = originalPistol.Rounds; originalPistolReserve = originalPistol.ReserveRounds;
            originalShotgunRounds = originalShotgun.Rounds; originalShotgunReserve = originalShotgun.ReserveRounds;
            Transform[] doorParts = gate.DoorInteraction.SubjectRoot.GetComponentsInChildren<Transform>(true);
            var savedDoor = new LocalPose[doorParts.Length];
            for (int i = 0; i < savedDoor.Length; i++) savedDoor[i] = new LocalPose(doorParts[i]);
            Scene loaded = root.gameObject.scene;

            // The first launch goes through the same pause-menu registration as a developer uses.
            yield return StartFromPauseMenu();
            AssertCommittedPrerequisites();
            yield return AwaitFirstActionSample();
            yield return AdvanceTo(1d);
            yield return Capture("Test door ambush - Handle attempt");
            yield return AdvanceTo(8d);
            yield return AwaitInteractive();
            Assert.That(root.Cutscenes.IsInteractivePlayback && root.Hero.IsRestrained, Is.True);
            root.Cutscenes.ReturnToList();
            yield return AwaitStopped();
            Assert.That(root.PauseMenu.IsOpen, Is.True);
            AssertPreviewRestored(scene, previewOrigin, previewFacing, savedDoor);
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return AwaitGameplayInput();

            // A second preview proves the exact onset, a paused clock and the physical lift.
            yield return StartFromPauseMenu();
            yield return AwaitFirstActionSample();
            yield return AdvanceTo(1d);
            Assert.That(root.CameraFollow.Camera.transform.position.y,
                Is.EqualTo(scene.EntryRootPosition.y + 1.18f).Within(.01f));
            Assert.That(root.CameraFollow.Camera.transform.position.z, Is.GreaterThan(scene.EntryRootPosition.z),
                "The raised opening camera must keep its frontal view of the hero.");
            Assert.That(root.Hero.CarriedWeaponLocation(originalWeapon), Is.EqualTo(CombatWeaponCarryLocation.Stowed),
                "The same weapon must visibly reach its body mount before the hand grips the door.");
            Assert.That(root.Cutscenes.TogglePause(), Is.True);
            double paused = root.Cutscenes.ElapsedSeconds;
            root.Cutscenes.Advance(.5d);
            Assert.That(root.Cutscenes.ElapsedSeconds, Is.EqualTo(paused));
            Assert.That(root.Cutscenes.TogglePause(), Is.True);
            yield return Capture("Test door ambush - Handle attempt");
            yield return AdvanceTo(2.99d);
            Assert.That(scene.AttackStarted, Is.False);
            Assert.That(root.Hero.IsRestrained, Is.False);
            yield return AdvanceTo(3d);
            Assert.That(scene.AttackStarted, Is.True, "The paired attack begins exactly at the three-second cue.");
            Assert.That(scene.Source.ActiveClipName, Is.EqualTo("CraneDoorAmbush"));
            yield return AdvanceTo(3.05d);
            AssertValveLightingForDoorActors();
            yield return Capture("Test door ambush - Attack onset");
            Assert.That(scene.Source.DoorAmbushLegCapture, Is.False);
            yield return AdvanceTo(7.95d);
            Assert.That(root.Hero.IsRestrained || scene.Source.DoorAmbushLegCapture, Is.False,
                "The five-second menace must not embed the hook before the terminal jab.");
            Assert.That(root.GetComponentInChildren<WarehouseArenaMechanisms>().DoorAmbushOverdriveActive, Is.False);
            AssertFanSpotlight(root.GetComponentInChildren<WarehouseArenaMechanisms>(), powered: false);
            Assert.That(root.Hero.RegisterRestraintEscapePress(), Is.False);
            Assert.That(root.Hero.RestraintEscapeProgress, Is.Zero);
            AssertValveLightingForDoorActors();
            yield return Capture("Test door ambush - Final leg jab");
            yield return AdvanceTo(8d);
            yield return Capture("Test door ambush - Shin contact");
            yield return AwaitInteractive();
            Assert.That(root.Cutscenes.IsInteractivePlayback, Is.True);
            Assert.That(root.Hero.IsRestrained, Is.True);
            Assert.That(root.Hero.RestraintPart, Is.EqualTo(Player3DAnatomicalPart.RightShin));
            Assert.That(scene.Source.DoorAmbushCaptured, Is.True);
            Assert.That(scene.Source.DoorAmbushLegCapture, Is.True);
            Assert.That(scene.Source.CraneHookPhase, Is.EqualTo(CombatCraneHookPhase.Embedded));
            Assert.That(Vector3.Distance(scene.Source.CraneHookPosition, root.Hero.RestraintAnchor), Is.LessThan(.04f));
            Assert.That(root.Hero.CarriedWeaponLocation(originalWeapon), Is.EqualTo(CombatWeaponCarryLocation.Stowed));
            Assert.That(GameTimeScaleRuntime.IsPaused, Is.False);
            Assert.That(root.Player.Interactor.InteractKeyClaimed, Is.True);
            AssertFanOverdriveStarted();
            yield return AwaitInvertedCeiling();
            Assert.That(root.CameraFollow.CombatBodyFocusActive, Is.True);
            yield return Capture("Test door ambush - Inverted ceiling lift");
            yield return AwaitCompletion();
            yield return AssertPersistentHangingGameplay();
            Assert.That(GameSessionState.WarehouseDoorAmbushPlayed, Is.False);
            Assert.That(root.gameObject.scene, Is.EqualTo(loaded), "Preview must keep the same loaded warehouse.");
            Assert.That(root.PauseMenu.IsOpen, Is.False, "A completed debug scene hands control to the hanging hero.");

            // Opening the same catalog again releases the retained preview before preparing its next episode.
            yield return StartFromPauseMenu();
            yield return AwaitFirstActionSample();
            Assert.That(scene.HangingGameplayActive, Is.False);
            root.Cutscenes.ReturnToList();
            yield return AwaitStopped();
            AssertPreviewRestored(scene, previewOrigin, previewFacing, savedDoor);
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return AwaitGameplayInput();

            // The actual door uses the same episode, and only its successful completion commits progress.
            PlaceHero(scene.EntryRootPosition + Vector3.back * .25f);
            Assert.That(scene.DoorInteraction.CanInteract(root.Player.Interactor), Is.True);
            scene.DoorInteraction.Interact(root.Player.Interactor);
            Assert.That(root.Cutscenes.IsPlaying && !root.Cutscenes.IsDebugPreview, Is.True);
            yield return AwaitFirstActionSample();
            yield return AdvanceTo(8d);
            yield return AwaitInteractive();
            Assert.That(root.Hero.IsRestrained, Is.True);
            yield return AwaitInvertedCeiling();
            yield return AwaitCompletion();
            yield return AssertPersistentHangingGameplay();
            Assert.That(GameSessionState.WarehouseDoorAmbushPlayed, Is.True);
            Assert.That(root.Cutscenes.IsPlaying, Is.False);
            Assert.That(scene.IsActive && scene.HangingGameplayActive && root.Hero.IsRestrained, Is.True);
            Assert.That(root.LiveOpponentCount, Is.Zero);
            Assert.That(root.CorpseCount, Is.Zero);
            Assert.That(root.EndlessKills, Is.Zero);
            root.Tick(2f);
            Assert.That(root.LiveOpponentCount, Is.Zero, "The staged attacker must never reopen the retired population's spawn gate.");
            Assert.That(root.gameObject.scene, Is.EqualTo(loaded));
            Assert.That(root.Player.Interactor.InputEnabled, Is.True);
            Assert.That(root.Player.Interactor.InteractKeyClaimed, Is.True);
            Assert.That(root.Hero.CarriedWeaponLocation(originalWeapon), Is.EqualTo(CombatWeaponCarryLocation.Stowed),
                "The hanging pose retains the same body-mounted weapon.");
            AssertAmmunitionPreserved();
            Assert.That(GameTimeScaleRuntime.IsPaused || BarMinigameModalLock.IsAnyLocked, Is.False);
            root.ResetRound();
            Assert.That(scene.IsActive || root.Hero.IsRestrained || root.CameraFollow.FreeAimActive, Is.False,
                "A round reset must release the retained hanging episode and its camera owner.");
            LogAssert.NoUnexpectedReceived();
        }

        private void AssertCommittedPrerequisites()
        {
            var fish = new[] { InventoryItemId.RottenFishTailCan, InventoryItemId.RottenFishBellyCan, InventoryItemId.RottenFishHeadCan };
            for (int i = 0; i < fish.Length; i++)
            {
                Assert.That(GameSessionState.IsWorldItemCollected(root.WarehousePlan.Refrigerators[i].SourceId), Is.True);
                Assert.That(GameSessionState.GetInventoryItemCount(fish[i]), Is.Zero);
            }
            Assert.That(GameSessionState.WarehouseFishAssembly.Phase, Is.EqualTo(WarehouseFishAssemblyPhase.Complete));
            Assert.That(GameSessionState.WarehouseFishAssembly.RewardGranted, Is.True);
            Assert.That(GameSessionState.WarehouseFishAssembly.ReceiptPending, Is.False);
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.WarehouseGateKey), Is.EqualTo(1));
            Assert.That(GameSessionState.WarehouseGate.Unlocked && GameSessionState.WarehouseGate.IsOpen, Is.True);
            Assert.That(GameSessionState.WarehouseFishLamp.Phase, Is.EqualTo(WarehouseFishLampPhase.Burst));
            Assert.That(root.GetComponentInChildren<WarehouseArenaMechanisms>().FanShutdownProgress, Is.EqualTo(1f));
            Assert.That(root.LiveOpponentCount, Is.Zero);
            Assert.That(root.CorpseCount, Is.Zero);
        }

        private IEnumerator StartFromPauseMenu()
        {
            Assert.That(root.PauseMenu.OpenCutscenes(), Is.True);
            Assert.That(root.PauseMenu.TryStartCutscene(WarehouseDoorAmbushScene.CutsceneId), Is.True);
            for (int frame = 0; frame < 30 && !root.Cutscenes.IsPlaying; frame++) yield return null;
            Assert.That(root.Cutscenes.IsPlaying && root.Cutscenes.IsDebugPreview, Is.True);
            Assert.That(root.PauseMenu.IsOpen, Is.False);
            AssertFanSpotlight(root.GetComponentInChildren<WarehouseArenaMechanisms>(), powered: false);
        }

        private IEnumerator AwaitFirstActionSample()
        {
            for (int frame = 0; frame < 180 && root.Cutscenes.IsPlaying && root.Cutscenes.ElapsedSeconds == 0d; frame++)
            {
                root.Cutscenes.Advance(1d / 60d);
                yield return null;
            }
            Assert.That(root.Cutscenes.IsPlaying && root.Cutscenes.ElapsedSeconds > 0d, Is.True,
                "The constrained approach must reach its grounded dock and render the neutral entry before the clock starts. " + Diagnostic());
        }

        private IEnumerator AdvanceTo(double target)
        {
            for (int step = 0; step < 600 && root.Cutscenes.IsPlaying && root.Cutscenes.ElapsedSeconds < target - .0000001d; step++)
            {
                root.Cutscenes.Advance(Math.Min(1d / 60d, target - root.Cutscenes.ElapsedSeconds));
                yield return null;
            }
            Assert.That(root.Cutscenes.IsPlaying, Is.True, Diagnostic());
            Assert.That(root.Cutscenes.ElapsedSeconds, Is.EqualTo(target).Within(.000001d), Diagnostic());
        }

        private IEnumerator AwaitInvertedCeiling()
        {
            CombatActor source = root.DoorAmbush.Source;
            Assert.That(source.DoorAmbushEscapeEnabled || root.Hero.CanEscapeRestraint, Is.False);
            Assert.That(root.Hero.RegisterRestraintEscapePress(), Is.False,
                "This scene has no escape action after the terminal jab.");
            for (int frame = 0; frame < 1800 && root.Cutscenes.IsPlaying && source.DoorAmbushCaptured &&
                !source.DoorAmbushHoistComplete; frame++)
            {
                Assert.That(root.Hero.RegisterRestraintEscapePress(), Is.False,
                    "Interaction input must not end the inward haul or inverted hoist.");
                Assert.That(root.Hero.RestraintEscapeProgress, Is.Zero);
                root.Cutscenes.Advance(1d / 60d);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(source.DoorAmbushHoistComplete, Is.True,
                "The actual lower-shin anchor must reach the ceiling inverted. " + Diagnostic());
            Assert.That(source.DoorAmbushHoistTargetHeight, Is.EqualTo(9.78f).Within(.001f));
            Assert.That(root.Hero.IsRestraintAirborne, Is.True);
            Assert.That(root.Hero.CanEscapeRestraint || source.DoorAmbushEscapeEnabled, Is.False);
            Assert.That(source.DoorAmbushVictimInverted, Is.True);
            Assert.That(root.Hero.RestraintAnchor.y, Is.InRange(source.DoorAmbushHoistTargetHeight - .65f,
                source.DoorAmbushHoistTargetHeight + .15f));
            Transform head = HeroBone(Player3DAnatomicalPart.Head);
            Transform pelvis = HeroBone(Player3DAnatomicalPart.Pelvis);
            Transform foot = HeroBone(Player3DAnatomicalPart.RightFoot);
            Assert.That(root.Hero.RestraintAnchor.y, Is.GreaterThan(pelvis.position.y + .35f));
            Assert.That(pelvis.position.y, Is.GreaterThan(head.position.y + .15f));
            Assert.That(foot.position.y, Is.GreaterThan(head.position.y + .5f));
            Assert.That(root.Hero.RestraintEscapeProgress, Is.Zero,
                "Rejected interaction presses must not become buffered escape progress.");
            AssertFanSpotlight(root.GetComponentInChildren<WarehouseArenaMechanisms>(), powered: true);
        }

        private IEnumerator AssertPersistentHangingGameplay()
        {
            CombatActor source = root.DoorAmbush.Source;
            yield return AwaitGameplayInput();
            AssertValveLightingRestored();
            Assert.That(root.Cutscenes.IsPlaying, Is.False);
            Assert.That(root.DoorAmbush.HangingGameplayActive && source.DoorAmbushHangingGameplay, Is.True);
            Assert.That(root.Hero.IsRestrained && root.Hero.IsRestraintAirborne && source.DoorAmbushVictimInverted, Is.True);
            Assert.That(root.Hero.Ragdoll.PhysicsController.IsRestraintDriveActive, Is.True);
            Assert.That(root.Hero.CanEscapeRestraint || source.DoorAmbushEscapeEnabled, Is.False);
            Assert.That(root.Hero.RegisterRestraintEscapePress(), Is.False);
            Assert.That(root.Hero.RestraintEscapeProgress, Is.Zero);
            Assert.That(root.CameraFollow.FreeAimActive && root.CameraFollow.OrbitInputEnabled, Is.True);
            Assert.That(root.CameraFollow.FixedPoseActive, Is.False);
            Assert.That(root.Player.Interactor.InputEnabled, Is.True);
            Assert.That(Vector3.Dot(root.CameraFollow.Camera.transform.up, Vector3.down), Is.GreaterThan(.8f),
                "The smooth cinematic roll must finish upside down before the camera is handed to gameplay.");
            Quaternion beforeLook = root.CameraFollow.Camera.transform.rotation;
            root.CameraFollow.RotateYaw(15f);
            yield return null;
            yield return null;
            Assert.That(Quaternion.Angle(beforeLook, root.CameraFollow.Camera.transform.rotation), Is.GreaterThan(5f),
                "The hanging hero must be able to look around with the returned gameplay camera.");
            Assert.That(root.Hero.IsRestrained && root.DoorAmbush.HangingGameplayActive, Is.True);
            WarehouseArenaMechanisms mechanisms = root.GetComponentInChildren<WarehouseArenaMechanisms>();
            Assert.That(mechanisms.DoorAmbushOverdriveActive, Is.True);
            mechanisms.Advance(5f);
            Assert.That(mechanisms.DoorAmbushOverdriveSpeed, Is.EqualTo(720f).Within(.01f));
            AssertFanSpotlight(mechanisms, powered: true);
        }

        private Transform HeroBone(Player3DAnatomicalPart part)
        {
            Assert.That(root.Hero.Ragdoll.PhysicsController.TryGetCombatBone(part, out Transform bone), Is.True, part.ToString());
            return bone;
        }

        private void AssertFanOverdriveStarted()
        {
            WarehouseArenaMechanisms mechanisms = root.GetComponentInChildren<WarehouseArenaMechanisms>();
            Assert.That(mechanisms.DoorAmbushOverdriveActive, Is.True);
            Assert.That(mechanisms.DoorAmbushOverdriveSpeed, Is.InRange(0f, 720f));
            Assert.That(mechanisms.DoorAmbushSparkEmissionCount, Is.GreaterThan(0));
            Assert.That(mechanisms.DoorAmbushSparkCount, Is.GreaterThan(0));
            AssertFanSpotlight(mechanisms, powered: true);
            // The handoff removes the cinematic pause after Begin emitted its
            // first burst. Advance that same live pulse through the unpause bridge.
            mechanisms.Advance(.01f);
            AssertSparkPointLight(mechanisms, lit: true);
            float previousSpeed = mechanisms.DoorAmbushOverdriveSpeed;
            mechanisms.Advance(.5f);
            Assert.That(mechanisms.DoorAmbushOverdriveSpeed, Is.GreaterThan(previousSpeed));
            Assert.That(mechanisms.DoorAmbushOverdriveSpeed, Is.LessThan(720f));
            Assert.That(mechanisms.FanLight.intensity, Is.GreaterThan(0f).And.LessThan(250f));
            AssertFanSpotlight(mechanisms, powered: true);
        }

        private static void AssertFanSpotlight(WarehouseArenaMechanisms mechanisms, bool powered)
        {
            Light spotlight = mechanisms.FanLight;
            Assert.That(spotlight, Is.Not.Null);
            Assert.That(spotlight.type, Is.EqualTo(LightType.Spot));
            float expectedIntensity = powered
                ? 250f * Mathf.Clamp01(mechanisms.DoorAmbushOverdriveSpeed / 720f) : 0f;
            Assert.That(spotlight.enabled, Is.EqualTo(expectedIntensity > 0f));
            Assert.That(spotlight.intensity, Is.EqualTo(expectedIntensity).Within(.01f),
                "The retained overhead spotlight must fade with the actual fan speed through its five-second restart.");
        }

        private static void AssertSparkPointLight(WarehouseArenaMechanisms mechanisms, bool lit)
        {
            Light pulse = mechanisms.DoorAmbushSparkLight;
            Assert.That(pulse, Is.Not.Null);
            Assert.That(pulse.type, Is.EqualTo(LightType.Point));
            Assert.That(pulse.enabled, Is.EqualTo(lit));
            if (lit)
            {
                Assert.That(pulse.intensity, Is.GreaterThan(0f));
                Assert.That(pulse.range, Is.GreaterThan(0f));
                Assert.That(Vector3.Distance(pulse.transform.position, mechanisms.DoorAmbushSparkOrigin), Is.LessThan(.001f));
            }
            else Assert.That(pulse.intensity, Is.Zero);
        }

        private void AssertDoorAndValveInsideEmergencyLight()
        {
            bool found = false;
            foreach (WarehouseArenaLamp lamp in root.WarehousePlan.Lamps)
            {
                if (lamp.Kind != "EmergencyLamp") continue;
                found = true;
                foreach (Vector3 target in new[] { root.DoorAmbush.EntryRootPosition + Vector3.up * 1.35f,
                    root.WarehousePlan.GateControlApproach + Vector3.up * 1.35f })
                {
                    Vector3 delta = target - lamp.Position;
                    Assert.That(delta.magnitude, Is.LessThan(lamp.Range));
                    Assert.That(Vector3.Angle(lamp.Direction, delta), Is.LessThan(lamp.InnerAngle * .5f),
                        "The retained physical red lamp must cover the door and valve operator within its inner cone.");
                }
            }
            Assert.That(found, Is.True);
        }

        private void AssertValveLightingForDoorActors()
        {
            Assert.That(valveLamp.DoorAmbushLightingActive, Is.True);
            Assert.That(valveLamp.Source, Is.SameAs(valveLight));
            Assert.That(valveLight.enabled, Is.True);
            Assert.That(valveLight.type, Is.EqualTo(LightType.Spot));
            Assert.That(valveLight.intensity, Is.InRange(30.6f - .001f, 36f + .001f));
            Assert.That(valveLight.color.r, Is.GreaterThanOrEqualTo(.99f));
            Assert.That(valveLight.color.g, Is.GreaterThanOrEqualTo(.2f));
            Assert.That(valveLight.color.r, Is.GreaterThan(valveLight.color.g));
            Assert.That(valveLight.innerSpotAngle, Is.EqualTo(88f).Within(.001f));
            Assert.That(valveLight.spotAngle, Is.EqualTo(110f).Within(.001f));
            Assert.That(valveLight.range, Is.EqualTo(6f).Within(.001f));
            Assert.That(valveLight.shadows, Is.EqualTo(LightShadows.Hard));
            Assert.That(Vector3.Distance(valveLight.transform.position, valveLightPosition), Is.LessThan(.0001f));
            Assert.That(Vector3.Angle(valveLight.transform.forward,
                new Vector3(-.03f, 1.30f, 10.98f) - valveLightPosition), Is.LessThan(.1f));
            foreach (CombatActor actor in new[] { root.Hero, root.DoorAmbush.Source })
            {
                Assert.That(actor.Ragdoll, Is.Not.Null);
                foreach (Player3DAnatomicalPart part in new[] { Player3DAnatomicalPart.Head, Player3DAnatomicalPart.Torso })
                {
                    Assert.That(actor.Ragdoll.PhysicsController.TryGetCombatBone(part, out Transform bone), Is.True);
                    Vector3 delta = bone.position - valveLight.transform.position;
                    Assert.That(delta.magnitude, Is.LessThan(valveLight.range), actor.name + " " + part);
                    Assert.That(Vector3.Angle(valveLight.transform.forward, delta),
                        Is.LessThan(valveLight.innerSpotAngle * .5f), actor.name + " " + part);
                }
            }
        }

        private void AssertValveLightingRestored()
        {
            Assert.That(valveLamp.DoorAmbushLightingActive, Is.False);
            Assert.That(valveLamp.Source, Is.SameAs(valveLight));
            Assert.That(Vector3.Distance(valveLight.transform.position, valveLightPosition), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(valveLight.transform.rotation, valveLightRotation), Is.LessThan(.01f));
            Assert.That(valveLight.color, Is.EqualTo(valveLightColor));
            Assert.That(valveLight.innerSpotAngle, Is.EqualTo(valveLightInnerAngle).Within(.001f));
            Assert.That(valveLight.spotAngle, Is.EqualTo(valveLightOuterAngle).Within(.001f));
            Assert.That(valveLight.intensity, Is.EqualTo(WarehouseArenaPlan.GoalLampIntensity * valveLamp.CurrentFactor).Within(.001f));
        }

        private IEnumerator AwaitInteractive()
        {
            for (int frame = 0; frame < 12 && root.Cutscenes.IsPlaying && !root.Cutscenes.IsInteractivePlayback; frame++)
            {
                yield return null;
                root.Cutscenes.Advance(0d);
            }
            Assert.That(root.Cutscenes.IsInteractivePlayback, Is.True,
                "The paired terminal pose must be rendered before the live leg-restraint handoff. " + Diagnostic());
        }

        private IEnumerator AwaitCompletion()
        {
            for (int frame = 0; frame < 720 && root.Cutscenes.IsPlaying; frame++)
            {
                root.Cutscenes.Advance(1d / 60d);
                yield return null;
            }
            Assert.That(root.Cutscenes.IsPlaying, Is.False,
                "The ceiling presentation and camera roll must finish within a bounded interval. " + Diagnostic());
        }

        private IEnumerator AwaitStopped()
        {
            for (int frame = 0; frame < 30 && root.Cutscenes.IsPlaying; frame++) yield return null;
            Assert.That(root.Cutscenes.IsPlaying, Is.False);
        }

        private IEnumerator AwaitGameplayInput()
        {
            for (int frame = 0; frame < 30 && !GameInput.CanRead(GameInputContext.Gameplay); frame++) yield return null;
            Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.True);
        }

        private void AssertPreviewRestored(WarehouseDoorAmbushScene scene, Vector3 origin, Quaternion facing, LocalPose[] door)
        {
            AssertCommittedPrerequisites();
            Assert.That(GameSessionState.WarehouseDoorAmbushPlayed, Is.False);
            Assert.That(scene.IsActive || root.Hero.IsRestrained || root.Hero.IsRagdollActive, Is.False);
            Assert.That(Vector3.Distance(root.Hero.transform.position, origin), Is.LessThan(.005f));
            Assert.That(Quaternion.Angle(root.Hero.transform.rotation, facing), Is.LessThan(.1f));
            foreach (LocalPose part in door) part.AssertRestored();
            Assert.That(root.LiveOpponentCount, Is.Zero);
            Assert.That(root.CorpseCount, Is.Zero);
            Assert.That(root.Hero.CarriedWeaponLocation(originalWeapon), Is.EqualTo(CombatWeaponCarryLocation.Held));
            Assert.That(root.Hero.CarriedWeapon(originalWeapon).transform.parent, Is.SameAs(originalWeaponParent));
            originalWeaponPose.AssertRestored();
            AssertAmmunitionPreserved();
            WarehouseArenaMechanisms mechanisms = root.GetComponentInChildren<WarehouseArenaMechanisms>();
            Assert.That(mechanisms.DoorAmbushOverdriveActive, Is.False);
            Assert.That(mechanisms.DoorAmbushOverdriveSpeed, Is.Zero);
            Assert.That(mechanisms.DoorAmbushSparkCount, Is.Zero);
            AssertFanSpotlight(mechanisms, powered: false);
            AssertSparkPointLight(mechanisms, lit: false);
            AssertValveLightingRestored();
        }

        private void AssertAmmunitionPreserved()
        {
            Assert.That(root.Hero.CarriedFirearm(CombatWeaponId.Pistol), Is.SameAs(originalPistol));
            Assert.That(root.Hero.CarriedFirearm(CombatWeaponId.Shotgun), Is.SameAs(originalShotgun));
            Assert.That(originalPistol.Rounds, Is.EqualTo(originalPistolRounds));
            Assert.That(originalPistol.ReserveRounds, Is.EqualTo(originalPistolReserve));
            Assert.That(originalShotgun.Rounds, Is.EqualTo(originalShotgunRounds));
            Assert.That(originalShotgun.ReserveRounds, Is.EqualTo(originalShotgunReserve));
        }

        private string Diagnostic()
        {
            PlayerAnimatedInteractionController action = root.Player.GameObject.GetComponent<PlayerAnimatedInteractionController>();
            CombatActor source = root.DoorAmbush != null ? root.DoorAmbush.Source : null;
            return "active=" + root.Cutscenes.IsPlaying + "; elapsed=" + root.Cutscenes.ElapsedSeconds +
                "; hero=" + root.Hero.transform.position + "; phase=" + action.Phase +
                "; blocked anatomy=" + (source != null ? source.CraneBlockedAnatomy : "missing source") +
                "; door blocked=" + (source != null && source.DoorAmbushBlocked) +
                "; hoist complete=" + (source != null && source.DoorAmbushHoistComplete) +
                "; inverted=" + (source != null && source.DoorAmbushVictimInverted) +
                "; anchor=" + root.Hero.RestraintAnchor +
                "; top hold=" + (source != null ? source.DoorAmbushTopHoldElapsed : 0f);
        }

        private void PlaceHero(Vector3 position)
        {
            root.Hero.SetCombatFocused(false);
            CharacterController capsule = root.Player.GameObject.GetComponent<CharacterController>();
            bool enabled = capsule.enabled;
            capsule.enabled = false;
            root.Hero.transform.SetPositionAndRotation(position, Quaternion.identity);
            capsule.enabled = enabled;
            Physics.SyncTransforms();
        }

        private static void ApplyGateTravel(WarehouseGateController gate) =>
            typeof(WarehouseGateController).GetMethod("ApplyTravel", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(gate, null);

        private static void EnsureListener()
        {
            foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>())
                if (listener.isActiveAndEnabled) return;
            new GameObject("Test warehouse audio listener").AddComponent<AudioListener>();
        }

        private IEnumerator Capture(string name)
        {
            if (!CaptureRequested) yield break;
            yield return null;
            AreaCaptureFixture.CaptureCurrentCamera(root.CameraFollow.Camera, "CombatDoorAmbush", name);
            if (gameView == null) yield break;
            yield return new WaitForEndOfFrame();
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "CombatDoorAmbush");
                File.WriteAllBytes(Path.Combine(folder, name + " - Timecode.png"), frame.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(frame); }
        }

        private void PrepareGameView()
        {
#if UNITY_EDITOR
            Type type = typeof(AreaLoadingVisualCapture).GetNestedType("GameViewResolution", BindingFlags.NonPublic);
            Assert.That(type, Is.Not.Null);
            object value = Activator.CreateInstance(type, nonPublic: true);
            gameView = (IDisposable)value;
            type.GetMethod("Set", BindingFlags.Public | BindingFlags.Instance).Invoke(value, new object[] { 1280, 720 });
#endif
        }

        private readonly struct LocalPose
        {
            private readonly Transform target;
            private readonly Vector3 position, scale;
            private readonly Quaternion rotation;
            internal LocalPose(Transform value)
            { target = value; position = value.localPosition; rotation = value.localRotation; scale = value.localScale; }
            internal void AssertRestored()
            {
                Assert.That(target, Is.Not.Null);
                Assert.That(Vector3.Distance(target.localPosition, position), Is.LessThan(.0001f), target.name);
                Assert.That(Quaternion.Angle(target.localRotation, rotation), Is.LessThan(.03f), target.name);
                Assert.That(Vector3.Distance(target.localScale, scale), Is.LessThan(.0001f), target.name);
            }
        }
    }
}
